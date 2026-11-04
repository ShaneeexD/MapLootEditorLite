using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace MapLootEditorLite.Client
{
    // Strong Leash combat containment. SAIN moves bots through its own SAINMoverClass (RunToPoint / WalkToPoint and the
    // *ByWay variants; cover, pursuit, suppression, regroup all funnel through these four), never through BotOwner.GoToPoint.
    // A prefix on those four methods lets MEL validate the destination only for exact MEL-owned bots whose assigned zone is
    // Strong. A destination outside the zone is replaced by a verified reachable NavMesh point inside the zone (along the
    // line from zone center to the requested point), or the request is refused if none exists. SAIN keeps all combat
    // decisions; grenade avoidance and flash recovery are never constrained. SAIN's own patches target BotMover (SetPosition,
    // ManualUpdate, Sprint), not these methods, so nothing is stacked. SAIN stays a soft dependency (found by name, no reference).
    internal static class StrongLeashContainment
    {
        private const string MoverTypeName = "SAIN.SAINComponent.Classes.Mover.SAINMoverClass";
        private const float InnerFactorPrimary = 0.9f;
        private const float InnerFactorFallback = 0.7f;
        private const float CacheSeconds = 1.0f;
        private const float RejectCacheSeconds = 1.5f;
        private const float CacheMatchDistance = 3f;
        private const float LogIntervalSeconds = 5f;
        private const int RepeatedBoundaryRequestThreshold = 5;
        private const float BoundaryRepeatWindowSeconds = 8f;
        private const float BoundaryRecoveryCooldownSeconds = 5f;
        private const float BoundaryRecoveryStuckDistance = 0.8f;

        private sealed class Cache
        {
            public Vector3 Requested;
            public Vector3 Replacement;
            public NavMeshPath ReplacementPath;
            public bool Rejected;
            public float At = -100f;
            public string LastKey;
            public float NextLogAt;
            public Vector3 RepeatedRequest;
            public Vector3 RepeatStartPosition;
            public float RepeatStartedAt = -100f;
            public float RepeatLastAt = -100f;
            public int RepeatedRequestCount;
            public Vector3 RecoveryRequest;
            public Vector3 RecoveryDestination;
            public NavMeshPath RecoveryPath;
            public float RecoveryUntil;
        }

        private static readonly ConditionalWeakTable<BotOwner, Cache> Caches = new ConditionalWeakTable<BotOwner, Cache>();
        private static bool _attempted;
        private static int _patchedMethodCount;
        private static string _adapterStatus = "not-initialized";
        private static PropertyInfo _botOwnerProp;
        private static PropertyInfo _botProp;
        private static PropertyInfo _currentActionProp;

        public static string GetMovementStatus(BotOwner bot)
        {
            if (bot != null && Caches.TryGetValue(bot, out _))
                return _adapterStatus + "; SAIN destination call observed for this bot";
            return _adapterStatus + "; no SAIN destination call observed for this bot";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void EnsureInstalled()
        {
            if (_attempted)
                return;
            _attempted = true;
            try
            {
                if (Plugin.StrongLeashCombatContainment == null || !Plugin.StrongLeashCombatContainment.Value)
                {
                    _adapterStatus = "disabled-by-config";
                    Plugin.Log.LogInfo("[MEL StrongLeash] Combat containment disabled by config (Bots/StrongLeashCombatContainment).");
                    return;
                }
                var moverType = AccessTools.TypeByName(MoverTypeName);
                if (moverType == null)
                {
                    _adapterStatus = "SAIN-mover-type-not-found";
                    Plugin.Log.LogInfo("[MEL StrongLeash] SAIN mover type not found; combat containment inactive (roaming/recovery unaffected).");
                    return;
                }
                _botOwnerProp = AccessTools.Property(moverType, "BotOwner");
                _botProp = AccessTools.Property(moverType, "Bot");
                _currentActionProp = _botProp != null ? AccessTools.Property(_botProp.PropertyType, "CurrentAction") : null;
                if (_botOwnerProp == null)
                {
                    _adapterStatus = "SAIN-mover-missing-BotOwner";
                    Plugin.Log.LogWarning("[MEL StrongLeash] SAIN mover has no BotOwner property (version mismatch?); combat containment inactive.");
                    return;
                }

                var harmony = new Harmony("com.shane.mapeditorlite.strongleash");
                var pointPrefix = new HarmonyMethod(typeof(StrongLeashContainment), nameof(PointPrefix));
                var wayPrefix = new HarmonyMethod(typeof(StrongLeashContainment), nameof(WayPrefix));
                foreach (var name in new[] { "RunToPoint", "WalkToPoint" })
                {
                    var m = AccessTools.Method(moverType, name);
                    if (m == null) { Plugin.Log.LogWarning($"[MEL StrongLeash] SAIN method {name} not found; skipped."); continue; }
                    harmony.Patch(m, prefix: pointPrefix);
                    _patchedMethodCount++;
                }
                foreach (var name in new[] { "RunToPointByWay", "WalkToPointByWay" })
                {
                    var m = AccessTools.Method(moverType, name);
                    if (m == null) { Plugin.Log.LogWarning($"[MEL StrongLeash] SAIN method {name} not found; skipped."); continue; }
                    harmony.Patch(m, prefix: wayPrefix);
                    _patchedMethodCount++;
                }
                _adapterStatus = _patchedMethodCount == 4 ? "SAIN-adapter-installed(4/4-methods)" : $"SAIN-adapter-partial({_patchedMethodCount}/4-methods)";
                Plugin.Log.LogInfo($"[MEL StrongLeash] SAIN combat containment installed ({_patchedMethodCount}/4 SAINMoverClass methods). Affects only MEL-owned bots in Strong zones.");
            }
            catch (Exception ex)
            {
                _adapterStatus = "SAIN-adapter-install-failed";
                Plugin.Log.LogWarning($"[MEL StrongLeash] Combat containment install failed; inactive: {ex.Message}");
            }
        }

        public static bool PointPrefix(object __instance, ref Vector3 point, ref bool __result, MethodBase __originalMethod)
        {
            try
            {
                if (!TryContext(__instance, out var bot, out var zone, out var cache, out var exempt))
                    return true;
                var decision = Decide(bot, zone, cache, exempt, __originalMethod.Name, point, out var replacement, out _);
                if (decision == 1)
                    point = replacement;
                else if (decision == 2)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ActivityZoneLeashCore.Log($"[MEL StrongLeash] Containment prefix error (request allowed unchanged): {ex.Message}");
            }
            return true;
        }

        public static bool WayPrefix(object __instance, ref NavMeshPath path, ref bool __result, MethodBase __originalMethod)
        {
            try
            {
                if (path == null || !TryContext(__instance, out var bot, out var zone, out var cache, out var exempt))
                    return true;
                var corners = path.corners;
                if (corners == null || corners.Length < 2)
                    return true;
                var patrolPathAllowed = true;
                var unauthorizedPathOutsideZone = false;
                for (var i = 0; i < corners.Length; i++)
                {
                    if (ActivityZoneLeashCore.EdgeRatio(zone, corners[i]) <= 1f)
                        continue;
                    if (ActivityZonePatrolRuntime.IsActive(bot, zone) &&
                        ActivityZonePatrolRuntime.AllowsPosition(bot, zone, corners[i]))
                    {
                        continue;
                    }
                    patrolPathAllowed = false;
                    unauthorizedPathOutsideZone = true;
                }
                var decision = Decide(bot, zone, cache, exempt, __originalMethod.Name, corners[corners.Length - 1],
                    out _, out var replacementPath, patrolPathAllowed, unauthorizedPathOutsideZone);
                if (decision == 1 && replacementPath != null)
                    path = replacementPath;
                else if (decision == 2 || decision == 1)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                ActivityZoneLeashCore.Log($"[MEL StrongLeash] Containment prefix error (request allowed unchanged): {ex.Message}");
            }
            return true;
        }

        private static bool TryContext(object mover, out BotOwner bot, out BotActivityZone zone, out Cache cache, out bool exempt)
        {
            bot = null;
            zone = null;
            cache = null;
            exempt = false;
            if (mover == null || _botOwnerProp == null || Plugin.StrongLeashCombatContainment == null || !Plugin.StrongLeashCombatContainment.Value)
                return false;
            bot = _botOwnerProp.GetValue(mover, null) as BotOwner;
            if (bot == null || bot.IsDead)
                return false;
            // Exact profile-ID ownership only; vanilla and third-party bots return here.
            if (!RuntimeBotSpawnSpawner.TryGetLeashAssignment(bot.ProfileId, out zone, out _, out _, out _, out _))
                return false;
            if (!ActivityZoneLeashCore.IsStrong(zone))
                return false;
            cache = Caches.GetValue(bot, _ => new Cache());
            try
            {
                var component = _botProp?.GetValue(mover, null);
                var action = component != null ? _currentActionProp?.GetValue(component, null) : null;
                var name = action?.GetType().Name;
                exempt = name == "AvoidGrenadeAction" || name == "FlashedAction";
            }
            catch { }
            return true;
        }

        // 0 = allow unchanged, 1 = replace destination, 2 = refuse request.
        private static int Decide(BotOwner bot, BotActivityZone zone, Cache cache, bool exempt, string method, Vector3 requested,
            out Vector3 replacement, out NavMeshPath replacementPath, bool patrolPathAllowed = true, bool forceInsideReplacement = false)
        {
            replacement = requested;
            replacementPath = null;
            var now = Time.time;
            var edge = ActivityZoneLeashCore.EdgeRatio(zone, requested);
            var layer = ActivityZoneLeashCore.ActiveLayerName(bot);

            if (edge <= 1f && !forceInsideReplacement)
            {
                Report(bot, zone, cache, now, method, "ALLOWED", $"Reason=InsideZone Requested={Fmt(requested)} DestEdge={edge:F2} Layer={layer}");
                return 0;
            }
            if (exempt)
            {
                Report(bot, zone, cache, now, method, "ALLOWED", $"Reason=SafetyCriticalAction(grenade/flash) Requested={Fmt(requested)} DestEdge={edge:F2} Layer={layer}");
                return 0;
            }
            if (patrolPathAllowed && ActivityZonePatrolRuntime.AllowsPosition(bot, zone, requested))
            {
                Report(bot, zone, cache, now, method, "ALLOWED", $"Reason=ExactBotPatrolAuthorization Requested={Fmt(requested)} DestEdge={edge:F2} Layer={layer}");
                return 0;
            }

            if (cache.RecoveryUntil > now && Flat(requested, cache.RecoveryRequest) < CacheMatchDistance)
            {
                replacement = cache.RecoveryDestination;
                replacementPath = cache.RecoveryPath;
                Report(bot, zone, cache, now, method, "RECOVERY_HELD",
                    $"Requested={Fmt(requested)} Recovery={Fmt(replacement)} Remaining={cache.RecoveryUntil - now:F1}s Reason=RepeatedBoundaryRequest");
                return 1;
            }

            var repeatedBoundaryRequest = TrackRepeatedBoundaryRequest(bot, zone, cache, requested, now);
            if (now - cache.At < (cache.Rejected ? RejectCacheSeconds : CacheSeconds) && Flat(requested, cache.Requested) < CacheMatchDistance)
            {
                if (cache.Rejected)
                    return 2;
                replacement = cache.Replacement;
                replacementPath = cache.ReplacementPath;
                return 1;
            }

            if (repeatedBoundaryRequest && TryFindNearbyInward(bot, zone, out replacement, out replacementPath))
            {
                cache.RecoveryRequest = requested;
                cache.RecoveryDestination = replacement;
                cache.RecoveryPath = replacementPath;
                cache.RecoveryUntil = now + BoundaryRecoveryCooldownSeconds;
                cache.RepeatedRequestCount = 0;
                cache.At = now;
                cache.Requested = requested;
                cache.Replacement = replacement;
                cache.ReplacementPath = replacementPath;
                cache.Rejected = false;
                Report(bot, zone, cache, now, method, "RECOVERY_OFFERED",
                    $"Requested={Fmt(requested)} Recovery={Fmt(replacement)} RecoveryEdge={ActivityZoneLeashCore.EdgeRatio(zone, replacement):F2} " +
                    $"Cooldown={BoundaryRecoveryCooldownSeconds:F1}s Teleport=False AIReset=False");
                return 1;
            }

            var found = TryFindInside(bot, zone, requested, Mathf.Max(edge, 0.01f), out replacement, out replacementPath, out var why);
            cache.At = now;
            cache.Requested = requested;
            cache.Rejected = !found;
            cache.Replacement = replacement;
            cache.ReplacementPath = replacementPath;
            if (found)
            {
                Report(bot, zone, cache, now, method, "REPLACED",
                    $"Reason=DestinationOutsideZone Requested={Fmt(requested)} DestEdge={edge:F2} Replacement={Fmt(replacement)} ReplacementEdge={ActivityZoneLeashCore.EdgeRatio(zone, replacement):F2} NavMesh=OK PathValid=True Layer={layer} SAINRetainsControl=True");
                return 1;
            }
            Report(bot, zone, cache, now, method, "REJECTED",
                $"Reason={why} Requested={Fmt(requested)} DestEdge={edge:F2} Fallback=RequestRefused(SAIN keeps current behavior, retry after {RejectCacheSeconds:F1}s) Layer={layer}");
            return 2;
        }

        private static bool TrackRepeatedBoundaryRequest(BotOwner bot, BotActivityZone zone, Cache cache, Vector3 requested, float now)
        {
            if (Flat(requested, cache.RepeatedRequest) >= CacheMatchDistance ||
                now - cache.RepeatLastAt > BoundaryRepeatWindowSeconds ||
                now - cache.RepeatStartedAt > BoundaryRepeatWindowSeconds ||
                Flat(bot.Position, cache.RepeatStartPosition) > BoundaryRecoveryStuckDistance ||
                ActivityZoneLeashCore.EdgeRatio(zone, bot.Position) < 0.85f)
            {
                cache.RepeatedRequest = requested;
                cache.RepeatStartPosition = bot.Position;
                cache.RepeatStartedAt = now;
                cache.RepeatedRequestCount = 1;
            }
            else
            {
                cache.RepeatedRequestCount++;
            }
            cache.RepeatLastAt = now;
            return cache.RepeatedRequestCount >= RepeatedBoundaryRequestThreshold &&
                now - cache.RepeatStartedAt >= 1.5f &&
                Flat(bot.Position, cache.RepeatStartPosition) <= BoundaryRecoveryStuckDistance;
        }

        private static bool TryFindNearbyInward(BotOwner bot, BotActivityZone zone, out Vector3 destination, out NavMeshPath path)
        {
            destination = default(Vector3);
            path = null;
            var botPosition = bot.Position;
            if (!NavMesh.SamplePosition(botPosition, out var botNav, 2.5f, NavMesh.AllAreas))
                return false;

            var center = zone.position.ToVector3();
            var inward = center - botPosition;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.01f)
                return false;
            inward.Normalize();
            var botInside = ActivityZoneLeashCore.EdgeRatio(zone, botPosition) <= 1f;
            var botEdge = ActivityZoneLeashCore.EdgeRatio(zone, botPosition);

            foreach (var distance in new[] { 2f, 4f, 6f, 8f })
            {
                var candidate = botPosition + inward * distance;
                if (!NavMesh.SamplePosition(candidate, out var hit, 2.5f, NavMesh.AllAreas))
                    continue;
                var hitEdge = ActivityZoneLeashCore.EdgeRatio(zone, hit.position);
                if (hitEdge > 1f || hitEdge >= botEdge - 0.01f)
                    continue;
                if (!ActivityZoneLeashCore.ValidatePath(botNav.position, hit.position, zone, botInside,
                    ActivityZoneLeashCore.StrongPathSlack, out _, out _))
                    continue;

                var fresh = new NavMeshPath();
                if (!NavMesh.CalculatePath(botNav.position, hit.position, NavMesh.AllAreas, fresh) ||
                    fresh.status != NavMeshPathStatus.PathComplete)
                    continue;
                destination = hit.position;
                path = fresh;
                return true;
            }
            return false;
        }

        // Bounded: at most two NavMesh samples and two path calculations per computation, and results are cached.
        private static bool TryFindInside(BotOwner bot, BotActivityZone zone, Vector3 requested, float edge,
            out Vector3 replacement, out NavMeshPath path, out string why)
        {
            replacement = requested;
            path = null;
            var botPos = bot.Position;
            if (!NavMesh.SamplePosition(botPos, out var botNav, 2.5f, NavMesh.AllAreas))
            {
                why = "bot-not-on-navmesh";
                return false;
            }
            var botInside = ActivityZoneLeashCore.EdgeRatio(zone, botPos) <= 1f;
            var center = zone.position.ToVector3();
            var lastReason = "no-candidate";
            foreach (var factor in new[] { InnerFactorPrimary, InnerFactorFallback })
            {
                var d = requested - center;
                var scaled = new Vector3(center.x + d.x * (factor / edge), requested.y, center.z + d.z * (factor / edge));
                if (!NavMesh.SamplePosition(scaled, out var hit, 3f, NavMesh.AllAreas))
                {
                    lastReason = "no-navmesh-near-inner-point";
                    continue;
                }
                if (ActivityZoneLeashCore.EdgeRatio(zone, hit.position) > 1f)
                {
                    lastReason = "navmesh-sample-outside-zone";
                    continue;
                }
                if (!ActivityZoneLeashCore.ValidatePath(botNav.position, hit.position, zone, botInside, ActivityZoneLeashCore.StrongPathSlack, out _, out var pathReason))
                {
                    lastReason = "path-" + pathReason;
                    continue;
                }
                var fresh = new NavMeshPath();
                if (!NavMesh.CalculatePath(botNav.position, hit.position, NavMesh.AllAreas, fresh) || fresh.status != NavMeshPathStatus.PathComplete)
                {
                    lastReason = "path-recalc-failed";
                    continue;
                }
                replacement = hit.position;
                path = fresh;
                why = "ok";
                return true;
            }
            why = "NoReachableDestinationInsideZone(" + lastReason + ")";
            return false;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            var d = a - b;
            d.y = 0f;
            return d.magnitude;
        }

        private static string Fmt(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";

        // State-change plus interval throttling so repeated identical decisions never flood the log.
        private static void Report(BotOwner bot, BotActivityZone zone, Cache cache, float now, string method, string action, string detail)
        {
            if (!RuntimeBotSpawnSpawner.LeashDiagnosticsEnabled)
                return;
            // Inside-zone allows are the common case; only log them on a state change, not on the interval.
            var key = method + "|" + action;
            if (cache.LastKey == key && now < cache.NextLogAt)
                return;
            if (action == "ALLOWED" && cache.LastKey == key)
                return;
            cache.LastKey = key;
            cache.NextLogAt = now + LogIntervalSeconds;
            ActivityZoneLeashCore.StrongLog(bot, zone, $"Method={method} Action={action} {detail}");
        }
    }
}
