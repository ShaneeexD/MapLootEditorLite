using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace MapLootEditorLite.Client
{
    // Soft leash: a low-priority BigBrain layer that steers MEL-owned (exact profile ID) bosses toward their
    // assigned Bot Activity Zone and roams inside it. It never teleports, never touches vanilla patrol data,
    // and is inactive whenever any higher-priority layer (SAIN, vanilla combat, avoid-danger...) is selected.
    internal static class ActivityZoneLeashRegistrar
    {
        private const int DefaultPriority = 5;
        private static readonly KeyValuePair<int, string[]>[] PriorityOverrides =
        {
            new KeyValuePair<int, string[]>(39, new[] { "BossKojaniy" }),         // lowest vanilla layer 40
            new KeyValuePair<int, string[]>(10, new[] { "FollowerKojaniy" }),     // lowest vanilla layer 11
            new KeyValuePair<int, string[]>(21, new[] { "FlBoarCl" }),            // patrol layers at 20
            new KeyValuePair<int, string[]>(68, new[] { "BossPartisan" })         // lowest vanilla layer 70
        };

        private static readonly HashSet<string> RegisteredBrains = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<ushort, OpCode> OpCodesByValue = CreateOpCodeMap();

        internal static bool IsBrainRegistered(string brainName)
        {
            return !string.IsNullOrEmpty(brainName) && RegisteredBrains.Contains(brainName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Register()
        {
            RegisteredBrains.Clear();
            var discovered = DiscoverBrainNames();
            discovered.UnionWith(PriorityOverrides.SelectMany(p => p.Value));
            var priorityBrainNames = new HashSet<string>(PriorityOverrides.SelectMany(p => p.Value), StringComparer.Ordinal);
            foreach (var entry in PriorityOverrides)
            {
                var matching = entry.Value.Where(discovered.Contains).ToList();
                if (matching.Count == 0)
                    continue;

                BrainManager.AddCustomLayer(typeof(ActivityZoneLeashLayer), matching, entry.Key);
                foreach (var name in matching)
                    RegisteredBrains.Add(name);
                Plugin.Log.LogInfo($"[MEL leash] Activity-zone leash layer registered (priority {entry.Key}) for discovered brains: {string.Join(", ", matching)}.");
            }

            var defaultBrains = discovered.Where(name => !priorityBrainNames.Contains(name)).ToList();
            if (defaultBrains.Count > 0)
            {
                BrainManager.AddCustomLayer(typeof(ActivityZoneLeashLayer), defaultBrains, DefaultPriority);
                foreach (var name in defaultBrains)
                    RegisteredBrains.Add(name);
                Plugin.Log.LogInfo($"[MEL leash] Activity-zone leash layer registered (priority {DefaultPriority}) for {defaultBrains.Count} discovered brain strategies.");
            }

            if (discovered.Count == 0)
                Plugin.Log.LogWarning("[MEL leash] No compatible BaseBrain strategies were discovered; BigBrain leash steering is unavailable.");
            else
                Plugin.Log.LogInfo($"[MEL leash] Registered for {RegisteredBrains.Count} discovered brain strategies and all WildSpawnType values; role names are not used as an eligibility list.");
        }

        private static HashSet<string> DiscoverBrainNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var baseBrainType = typeof(BaseBrain);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in GetTypes(assembly))
                {
                    if (type == null || !type.IsClass || type.IsAbstract || !type.IsSubclassOf(baseBrainType))
                        continue;

                    MethodInfo shortName = null;
                    try
                    {
                        shortName = type.GetMethod("ShortName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (shortName == null)
                            continue;
                        var instance = FormatterServices.GetUninitializedObject(type);
                        var name = shortName.Invoke(instance, null) as string;
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                    catch (Exception ex)
                    {
                        var candidates = new List<string>();
                        if (shortName != null)
                        {
                            try { candidates = GetStringConstants(shortName); }
                            catch (Exception parseException)
                            {
                                Plugin.Log.LogWarning($"[MEL leash] Could not inspect brain-name variants for {type.FullName}: {parseException.GetBaseException().Message}");
                            }
                        }
                        foreach (var candidate in candidates)
                        {
                            if (!string.IsNullOrWhiteSpace(candidate))
                                names.Add(candidate);
                        }
                        if (candidates.Count == 0)
                            Plugin.Log.LogWarning($"[MEL leash] Could not discover brain name for {type.FullName}; that brain cannot receive the BigBrain leash layer: {ex.GetBaseException().Message}");
                        else
                            Plugin.Log.LogInfo($"[MEL leash] Brain strategy {type.FullName} selects its name from runtime state; registered its {candidates.Count} literal name candidate(s).");
                    }
                }
            }
            return names;
        }

        private static List<string> GetStringConstants(MethodInfo method)
        {
            var strings = new List<string>();
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null)
                return strings;

            var position = 0;
            while (position < il.Length)
            {
                var opcodeByte = il[position++];
                ushort key = opcodeByte;
                if (opcodeByte == 0xFE)
                {
                    if (position >= il.Length)
                        throw new BadImageFormatException("Truncated two-byte opcode.");
                    key = (ushort)((opcodeByte << 8) | il[position++]);
                }
                if (!OpCodesByValue.TryGetValue(key, out var opcode))
                    throw new BadImageFormatException("Unknown IL opcode " + key + ".");

                var operandStart = position;
                var operandSize = GetOperandSize(opcode.OperandType, il, operandStart);
                if (operandSize < 0 || position + operandSize > il.Length)
                    throw new BadImageFormatException("Truncated operand for " + opcode.Name + ".");
                if (opcode == OpCodes.Ldstr)
                {
                    var token = BitConverter.ToInt32(il, operandStart);
                    var stringValue = method.Module.ResolveString(token);
                    if (!strings.Contains(stringValue))
                        strings.Add(stringValue);
                }
                position += operandSize;
            }
            return strings;
        }

        private static int GetOperandSize(OperandType operandType, byte[] il, int position)
        {
            switch (operandType)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch:
                    if (position + 4 > il.Length)
                        return -1;
                    return 4 + 4 * BitConverter.ToInt32(il, position);
                default:
                    throw new BadImageFormatException("Unsupported IL operand type " + operandType + ".");
            }
        }

        private static Dictionary<ushort, OpCode> CreateOpCodeMap()
        {
            var result = new Dictionary<ushort, OpCode>();
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode opcode)
                    result[(ushort)opcode.Value] = opcode;
            }
            return result;
        }

        private static IEnumerable<Type> GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MEL leash] Could not inspect brain types in {assembly.GetName().Name}: {ex.Message}");
                return Enumerable.Empty<Type>();
            }
        }
    }

    internal sealed class LeashState
    {
        public Vector3? Destination;
        public float IssuedAt;
        public float Timeout;
        public float NextDecisionAt;
        public int FailedRounds;
        public int LockCount;
        public float LockedUntil;
        public string Phase = "waiting";
        public string LastInactiveReason;
        public float NextSampleLogAt;
        public float NextStatusLogAt;
        public bool GuardActive;
        public bool IssuedWhileInside;
        public Vector3? LastDestination;
        public float NextBoundaryReplanAt;
        public bool? LastInside;
        public int RecoveryAttempts;
        public readonly List<Vector3> FailedPoints = new List<Vector3>();

        // Soft-only handoff debounce (BigBrain flapping with a higher-priority layer).
        public bool ControlHeld;
        public float TakenAt;
        public bool ReentryPending;        // set when MEL was stopped; cleared when re-entry is allowed or the assignment is lost
        public float EligibleSince = -1f;  // start of the current continuous run of IsActive evaluations where MEL was otherwise eligible
        public float CooldownUntil;
        public float LastEvalAt = -1f;
        public bool LastEvalResult;
        public bool CooldownLogged;
        public string LastYieldReason;     // reason MEL's IsActive returned false in the latest evaluation; null if it did not
    }

    internal static class ActivityZoneLeashCore
    {
        public const float ArriveDistance = 2.0f;
        public const float ReachDist = 1.5f;
        public const int CandidatesPerRound = 6;
        public const int MaxPathChecksPerRound = 4;
        public const int MaxFailedRoundsBeforeLock = 4;
        public const float SampleLogInterval = 10f;
        public const float CombatHoldoffSeconds = 10f;
        // Followers stay under the leash (trailing their boss) instead of handing back to vanilla patrol.
        public const float EscortHoldDistance = 9f;

        // Strong Leash policy. Same layer and priority as Soft; only these parameters and the boundary replan differ.
        public const float StrongCombatHoldoffSeconds = 3.5f;

        // Soft handoff protection: MEL must be continuously eligible for this long before regaining control after a stop,
        // and a stop shortly after taking control triggers a longer cooldown (the two overlap; they do not add up).
        public const float SoftReentryDebounceSeconds = 3f;
        public const float SoftRapidStopWindowSeconds = 2f;
        public const float SoftRapidStopCooldownSeconds = 5f;
        // IsActive is only called while no higher-priority layer is active; a longer gap means eligibility was interrupted.
        public const float SoftEvalGapSeconds = 1.5f;
        public const float StrongApproachEdge = 0.9f;      // start re-planning before the bot reaches the edge
        public const float StrongDestMaxEdge = 0.85f;      // destinations must sit comfortably inside
        public const float StrongPathSlack = 1.03f;        // tighter than Soft's 1.08
        public const float StrongReplanCooldown = 3f;      // hysteresis between boundary re-plans
        public const float StrongMinRepeatDistance = 4f;   // do not re-pick (almost) the same destination

        public static bool IsStrong(BotActivityZone zone)
        {
            return zone != null && BotActivityZone.NormalizeLeashMode(zone.leashMode) == BotActivityZone.LeashModeStrong;
        }

        public static void StrongLog(BotOwner bot, BotActivityZone zone, string message)
        {
            try
            {
                if (!RuntimeBotSpawnSpawner.LeashDiagnosticsEnabled || bot == null || zone == null)
                    return;
                var edge = EdgeRatio(zone, bot.Position);
                RuntimeBotSpawnSpawner.LeashLog(
                    $"[MEL StrongLeash] Bot={bot.Profile?.Info?.Nickname} Profile={bot.ProfileId} Zone={zone.zoneId} Shape={zone.shape} EdgeRatio={edge:F2} State={(edge <= 1f ? "INSIDE" : "OUTSIDE")} {message}");
            }
            catch { }
        }

        private static readonly ConditionalWeakTable<BotOwner, LeashState> States = new ConditionalWeakTable<BotOwner, LeashState>();
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        private static readonly NavMeshPath PathBuffer = new NavMeshPath();

        public static LeashState GetState(BotOwner bot)
        {
            return States.GetValue(bot, _ => new LeashState());
        }

        public static void Log(string message)
        {
            try
            {
                if (RuntimeBotSpawnSpawner.LeashDiagnosticsEnabled)
                    RuntimeBotSpawnSpawner.LeashLog(message);
            }
            catch { }
        }

        public static string ActiveLayerName(BotOwner bot)
        {
            try { return bot.Brain?.ActiveLayerName() ?? "unknown"; }
            catch { return "unknown"; }
        }

        public static bool InCombatHoldoff(BotOwner bot, out string why, float holdoffSeconds = CombatHoldoffSeconds)
        {
            why = null;
            var memory = bot.Memory;
            if (memory == null)
                return false;
            if (memory.HaveEnemy)
            {
                why = "has-enemy";
                return true;
            }
            if (memory.IsUnderFire)
            {
                why = "under-fire";
                return true;
            }
            var seen = memory.LastEnemyTimeSeen;
            if (seen > 0f && Time.time - seen < holdoffSeconds)
            {
                why = "recent-enemy-contact";
                return true;
            }
            return false;
        }

        // 0 = center, 1 = edge, >1 = outside.
        public static float EdgeRatio(BotActivityZone zone, Vector3 position)
        {
            var delta = position - zone.position.ToVector3();
            if (zone.shape == BotActivityZoneShape.Circle)
                return Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z) / Mathf.Max(zone.radius, 0.01f);
            var local = Quaternion.Euler(0f, -zone.rotation.y, 0f) * delta;
            return Mathf.Max(
                Mathf.Abs(local.x) / Mathf.Max(zone.EffectiveWidth * 0.5f, 0.01f),
                Mathf.Abs(local.z) / Mathf.Max(zone.EffectiveDepth * 0.5f, 0.01f));
        }

        public static string DescribeBot(BotOwner bot)
        {
            try
            {
                var role = bot.Profile?.Info?.Settings?.Role;
                return $"role={role}, brain={bot.Brain?.GetStateName}";
            }
            catch { return "role=?, brain=?"; }
        }

        public static string DescribeZone(BotActivityZone zone)
        {
            var c = zone.position.ToVector3();
            var dims = zone.shape == BotActivityZoneShape.Circle
                ? $"radius={zone.radius:F1}"
                : $"width={zone.EffectiveWidth:F1}, depth={zone.EffectiveDepth:F1}, yaw={zone.rotation.y:F1}";
            return $"shape={zone.shape}, center=({c.x:F2},{c.y:F2},{c.z:F2}), {dims}";
        }

        // Random XZ point inside the zone footprint, scaled toward the center by 'factor' (0..1).
        private static Vector2 RandomFootprintOffset(BotActivityZone zone, float factor, out Vector3 worldXZ)
        {
            var center = zone.position.ToVector3();
            if (zone.shape == BotActivityZoneShape.Circle)
            {
                var r = zone.radius * factor * Mathf.Sqrt(UnityEngine.Random.value);
                var a = UnityEngine.Random.value * Mathf.PI * 2f;
                var offset = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                worldXZ = new Vector3(center.x + offset.x, center.y, center.z + offset.y);
                return offset;
            }

            var halfWidth = zone.EffectiveWidth * 0.5f * factor;
            var halfDepth = zone.EffectiveDepth * 0.5f * factor;
            var local = new Vector3(UnityEngine.Random.Range(-halfWidth, halfWidth), 0f, UnityEngine.Random.Range(-halfDepth, halfDepth));
            var world = Quaternion.Euler(0f, zone.rotation.y, 0f) * local;
            worldXZ = new Vector3(center.x + world.x, center.y, center.z + world.z);
            return new Vector2(world.x, world.z);
        }

        private static bool IsNearFailed(LeashState state, Vector3 p)
        {
            for (var i = 0; i < state.FailedPoints.Count; i++)
            {
                var d = state.FailedPoints[i] - p;
                d.y = 0f;
                if (d.sqrMagnitude < 9f)
                    return true;
            }
            return false;
        }

        private static void RememberFailed(LeashState state, Vector3 p)
        {
            if (state.FailedPoints.Count >= 24)
                state.FailedPoints.RemoveAt(0);
            state.FailedPoints.Add(p);
        }

        // Walkable floor positions under an XZ column. The zone marker's Y is only used as a search origin,
        // the returned positions are real NavMesh samples.
        private static void CollectFloorSamples(Vector3 xz, float originY, List<Vector3> results)
        {
            results.Clear();
            var origin = new Vector3(xz.x, originY + 60f, xz.z);
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, HitBuffer, 220f, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                if (!NavMesh.SamplePosition(HitBuffer[i].point, out var nav, 1.0f, NavMesh.AllAreas))
                    continue;
                var duplicate = false;
                for (var j = 0; j < results.Count; j++)
                {
                    if (Mathf.Abs(results[j].y - nav.position.y) < 0.75f)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    results.Add(nav.position);
            }
        }

        // The zone is convex (circle / square), so a path whose corners are all inside never leaves it.
        // Small slack so corners hugging the boundary are tolerated.
        public const float PathEdgeSlack = 1.08f;

        internal static bool ValidatePath(Vector3 from, Vector3 to, BotActivityZone zone, bool requireInside, float slack, out float length, out string reason)
        {
            length = 0f;
            reason = null;
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, PathBuffer))
            {
                reason = "path-calc-failed";
                return false;
            }
            if (PathBuffer.status != NavMeshPathStatus.PathComplete)
            {
                reason = "path-" + PathBuffer.status;
                return false;
            }
            var corners = PathBuffer.corners;
            if (corners == null || corners.Length < 2)
            {
                reason = "path-too-short";
                return false;
            }
            for (var i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            var end = corners[corners.Length - 1] - to;
            end.y = 0f;
            if (end.magnitude > 1.5f)
            {
                reason = "path-does-not-reach";
                return false;
            }
            var straight = Vector3.Distance(from, to);
            if (requireInside)
            {
                for (var i = 1; i < corners.Length; i++)
                {
                    if (EdgeRatio(zone, corners[i]) > slack)
                    {
                        reason = "path-leaves-zone";
                        return false;
                    }
                }
            }
            // A route far longer than the straight line usually means a different floor or a large detour.
            if (length > straight * 2.5f + 15f)
            {
                reason = "path-detour-too-long";
                return false;
            }
            return true;
        }

        // Returns a verified destination or false with a summarized reason. Bounded: at most
        // CandidatesPerRound columns and MaxPathChecksPerRound path calculations.
        public static bool TrySelectDestination(BotOwner bot, BotActivityZone zone, LeashState state, bool botInside, bool isFollower, out Vector3 destination, out float pathLength, out string reason)
        {
            destination = default(Vector3);
            pathLength = 0f;
            reason = "no-candidate";

            var botPos = bot.Position;
            if (!NavMesh.SamplePosition(botPos, out var botNav, 2.5f, NavMesh.AllAreas))
            {
                reason = "bot-not-on-navmesh";
                return false;
            }

            var strong = IsStrong(zone);
            var edge = EdgeRatio(zone, botPos);
            var factor = strong
                ? (!botInside ? 0.5f : (edge > 0.7f ? 0.4f : 0.6f))
                : (!botInside ? 0.7f : (edge > 0.8f ? 0.55f : 0.85f));
            var minHop = botInside ? Mathf.Max(6f, 0.35f * (zone.shape == BotActivityZoneShape.Circle ? zone.radius : Mathf.Max(zone.EffectiveWidth, zone.EffectiveDepth) * 0.5f)) : 0f;
            var zoneY = zone.position.y;
            var originY = Mathf.Max(zoneY, botPos.y);

            // Escorts trail their own boss: candidates are a ring around the boss, kept well inside the zone.
            var useAnchor = false;
            var anchor = default(Vector3);
            if (isFollower && RuntimeBotSpawnSpawner.TryGetEscortAnchor(bot.ProfileId, out anchor) && EdgeRatio(zone, anchor) <= 0.9f)
            {
                useAnchor = true;
                minHop = 0f;
            }

            var candidates = new List<Vector3>(CandidatesPerRound);
            for (var i = 0; i < CandidatesPerRound; i++)
            {
                Vector3 xz;
                if (useAnchor)
                {
                    var a = UnityEngine.Random.value * Mathf.PI * 2f;
                    var r = UnityEngine.Random.Range(3f, 8f);
                    xz = new Vector3(anchor.x + Mathf.Cos(a) * r, anchor.y, anchor.z + Mathf.Sin(a) * r);
                    if (EdgeRatio(zone, xz) > 0.9f)
                        continue;
                }
                else
                    RandomFootprintOffset(zone, factor, out xz);
                if (IsNearFailed(state, xz))
                    continue;
                candidates.Add(xz);
            }
            if (!botInside)
                candidates.Sort((a, b) => (a - botPos).sqrMagnitude.CompareTo((b - botPos).sqrMagnitude));

            var floors = new List<Vector3>(8);
            var pathChecks = 0;
            var rejectedFootprint = 0;
            var rejectedHop = 0;
            var rejectedFloor = 0;
            var lastPathReason = "none";
            for (var i = 0; i < candidates.Count && pathChecks < MaxPathChecksPerRound; i++)
            {
                CollectFloorSamples(candidates[i], originY, floors);
                if (floors.Count == 0)
                {
                    RememberFailed(state, candidates[i]);
                    lastPathReason = "no-navmesh-floor";
                    continue;
                }
                for (var f = 0; f < floors.Count && pathChecks < MaxPathChecksPerRound; f++)
                {
                    var floor = floors[f];
                    if (!ZoneContainsSample(zone, floor) || (strong && EdgeRatio(zone, floor) > StrongDestMaxEdge))
                    {
                        rejectedFootprint++;
                        continue;
                    }
                    if (strong && state.LastDestination.HasValue)
                    {
                        var repeat = floor - state.LastDestination.Value;
                        repeat.y = 0f;
                        if (repeat.magnitude < StrongMinRepeatDistance)
                        {
                            rejectedHop++;
                            continue;
                        }
                    }
                    if (botInside)
                    {
                        // Stay on the floor the bot is already on while roaming.
                        if (Mathf.Abs(floor.y - botNav.position.y) > 4f)
                        {
                            rejectedFloor++;
                            continue;
                        }
                        var hop = floor - botPos;
                        hop.y = 0f;
                        if (hop.magnitude < minHop)
                        {
                            rejectedHop++;
                            continue;
                        }
                    }

                    pathChecks++;
                    if (ValidatePath(botNav.position, floor, zone, botInside, strong ? StrongPathSlack : PathEdgeSlack, out var length, out var pathReason))
                    {
                        destination = floor;
                        pathLength = length;
                        reason = "ok";
                        return true;
                    }
                    lastPathReason = pathReason;
                    RememberFailed(state, floor);
                }
            }

            reason = $"no-valid-destination(pathChecks={pathChecks}, lastPath={lastPathReason}, outsideFootprint={rejectedFootprint}, wrongFloor={rejectedFloor}, tooClose={rejectedHop})";
            return false;
        }

        private static bool ZoneContainsSample(BotActivityZone zone, Vector3 p)
        {
            return RuntimeBotSpawnSpawner.TryIsInsideLeashZone(zone, p, out var inside, out _) && inside;
        }
    }

    internal sealed class ActivityZoneLeashLayer : CustomLayer
    {
        private string _profileId;

        public ActivityZoneLeashLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            // Idempotent; installs the Strong-zone SAIN movement validation once SAIN is loaded (layers are created at raid time).
            StrongLeashContainment.EnsureInstalled();
        }

        public override string GetName()
        {
            return "MEL Activity Zone Leash";
        }

        public override bool IsActive()
        {
            var result = false;
            try
            {
                // Cleared every evaluation so a stale yield reason can never be attributed to a later stop.
                if (BotOwner != null)
                    ActivityZoneLeashCore.GetState(BotOwner).LastYieldReason = null;
                result = Evaluate();
                return result;
            }
            catch (Exception ex)
            {
                ActivityZoneLeashCore.Log($"[MEL leash] IsActive failed; yielding: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (BotOwner != null)
                    {
                        var s = ActivityZoneLeashCore.GetState(BotOwner);
                        s.LastEvalAt = Time.time;
                        s.LastEvalResult = result;
                    }
                }
                catch { }
            }
        }

        // Soft only. Returns true when MEL may take (or keep) control.
        private static bool SoftReentryAllowed(BotOwner bot, BotActivityZone zone, LeashState state, string profileId)
        {
            if (state.ControlHeld || !state.ReentryPending)
                return true;

            var now = Time.time;
            var gap = state.LastEvalAt < 0f || now - state.LastEvalAt > ActivityZoneLeashCore.SoftEvalGapSeconds;
            if (state.EligibleSince < 0f || gap)
            {
                if (state.EligibleSince >= 0f)
                    ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff debounce RESET profileId={profileId}, zoneId={zone.zoneId}, reason=eligibility-interrupted (no evaluation for {now - state.LastEvalAt:F1}s).");
                state.EligibleSince = now;
                ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff debounce STARTED profileId={profileId}, zoneId={zone.zoneId}, need={ActivityZoneLeashCore.SoftReentryDebounceSeconds:F1}s.");
            }

            if (state.CooldownUntil > 0f && now >= state.CooldownUntil)
            {
                state.CooldownUntil = 0f;
                state.CooldownLogged = false;
                ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff rapid-stop cooldown EXPIRED profileId={profileId}, zoneId={zone.zoneId}.");
            }

            if (now < state.CooldownUntil)
            {
                if (!state.CooldownLogged)
                {
                    state.CooldownLogged = true;
                    ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff rapid-stop cooldown ACTIVE profileId={profileId}, zoneId={zone.zoneId}, remaining={state.CooldownUntil - now:F1}s.");
                }
                return false;
            }

            if (now - state.EligibleSince >= ActivityZoneLeashCore.SoftReentryDebounceSeconds)
            {
                ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff debounce COMPLETED profileId={profileId}, zoneId={zone.zoneId}, eligibleFor={now - state.EligibleSince:F1}s.");
                state.ReentryPending = false;
                state.EligibleSince = -1f;
                return true;
            }
            return false;
        }

        private bool Evaluate()
        {
            var bot = BotOwner;
            if (bot == null || bot.IsDead)
                return false;

            var state = ActivityZoneLeashCore.GetState(bot);
            var profileId = _profileId ?? (_profileId = bot.ProfileId);

            if (!RuntimeBotSpawnSpawner.TryGetLeashAssignment(profileId, out var zone, out _, out _, out var isFollower, out var reason))
            {
                SetInactive(bot, null, state, profileId, reason);
                return false;
            }

            if (Time.time < state.LockedUntil)
            {
                SetInactive(bot, zone, state, profileId, "retry-lock");
                return false;
            }

            var strongZone = ActivityZoneLeashCore.IsStrong(zone);
            if (ActivityZoneLeashCore.InCombatHoldoff(bot, out var why, strongZone ? ActivityZoneLeashCore.StrongCombatHoldoffSeconds : ActivityZoneLeashCore.CombatHoldoffSeconds))
            {
                ActivityZonePatrolRuntime.NotifyCombat(bot);
                state.GuardActive = false;
                SetInactive(bot, zone, state, profileId, "yield-" + why);
                return false;
            }

            if (isFollower)
            {
                // Escorts stay under the leash for the whole time they are outside combat. Handing control back at the
                // edge let the vanilla BirdEye patrol pick the same outside point again (pull-in / walk-out loop), so
                // they now trail their own boss inside the zone instead of resuming vanilla patrol.
                state.GuardActive = true;
            }

            if (!strongZone && !SoftReentryAllowed(bot, zone, state, profileId))
                return false;

            state.LastInactiveReason = null;
            return true;
        }

        private static void SetInactive(BotOwner bot, BotActivityZone zone, LeashState state, string profileId, string reason)
        {
            state.LastYieldReason = reason;
            // Any other eligibility failure interrupts the Soft re-entry debounce run.
            if (state.EligibleSince >= 0f)
            {
                if (zone != null)
                    ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff debounce RESET profileId={profileId}, zoneId={zone.zoneId}, reason={reason}, eligibleFor={Time.time - state.EligibleSince:F1}s.");
                state.EligibleSince = -1f;
            }
            if (zone == null)
            {
                state.ReentryPending = false;
                state.CooldownUntil = 0f;
                state.CooldownLogged = false;
            }

            // Periodic status for every MEL-owned bot that is not being steered (escorts inside the zone, combat, locks),
            // so diagnostics always show that the bot is tracked. Vanilla bots never reach here with a zone.
            if (zone != null && RuntimeBotSpawnSpawner.LeashDiagnosticsEnabled && Time.time >= state.NextStatusLogAt)
            {
                state.NextStatusLogAt = Time.time + ActivityZoneLeashCore.SampleLogInterval;
                try
                {
                    var edge = ActivityZoneLeashCore.EdgeRatio(zone, bot.Position);
                    ActivityZoneLeashCore.Log(
                        $"[MEL leash] Status profileId={profileId}, {ActivityZoneLeashCore.DescribeBot(bot)}, leash=YIELDING({reason}), " +
                        $"zoneId={zone.zoneId}, edgeRatio={edge:F2}, inside={edge <= 1f}, botPos=({bot.Position.x:F1},{bot.Position.y:F1},{bot.Position.z:F1}), " +
                        $"activeLayer={ActivityZoneLeashCore.ActiveLayerName(bot)}.");
                }
                catch { }
            }

            if (state.LastInactiveReason == reason)
                return;
            state.LastInactiveReason = reason;
            if (zone != null && ActivityZoneLeashCore.IsStrong(zone))
            {
                var combat = reason.StartsWith("yield-", StringComparison.Ordinal);
                var higher = combat || reason == "retry-lock" ? "likely(SAIN/vanilla combat or lock)" : "unknown";
                ActivityZoneLeashCore.StrongLog(bot, zone,
                    $"Layer={ActivityZoneLeashCore.ActiveLayerName(bot)} MelLayerActive=False Control=YIELDING Reason={reason} CombatHoldoff={combat} " +
                    $"HigherPriorityControl={higher} Destination={(state.Destination.HasValue ? "set" : "none")} ResumesWhen=holdoff-ends-and-higher-layers-yield");
            }
            var where = "";
            if (zone != null && RuntimeBotSpawnSpawner.LeashDiagnosticsEnabled)
            {
                try
                {
                    var edge = ActivityZoneLeashCore.EdgeRatio(zone, bot.Position);
                    where = $", {ActivityZoneLeashCore.DescribeBot(bot)}, edgeRatio={edge:F2}, inside={edge <= 1f}, activeLayer={ActivityZoneLeashCore.ActiveLayerName(bot)}";
                }
                catch { }
            }
            ActivityZoneLeashCore.Log($"[MEL leash] YIELDING profileId={profileId}, reason={reason}{where}.");
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(ActivityZoneLeashLogic), "MEL activity zone leash");
        }

        public override bool IsCurrentActionEnding()
        {
            return false;
        }

        public override void Start()
        {
            try
            {
                var state = ActivityZoneLeashCore.GetState(BotOwner);
                state.Destination = null;
                state.Phase = "waiting";
                state.LastInside = null;
                state.NextDecisionAt = Time.time + 0.5f;
                var regained = state.ReentryPending;
                state.ControlHeld = true;
                state.TakenAt = Time.time;
                state.ReentryPending = false;
                state.EligibleSince = -1f;
                state.CooldownLogged = false;
                if (regained)
                    ActivityZoneLeashCore.Log($"[MEL leash] SoftHandoff control REGAINED profileId={BotOwner.ProfileId}.");
                // Drop whatever the previous layer (vanilla patrol, SAIN, combat) was walking toward; it may be outside the zone.
                try { BotOwner.StopMove(); } catch { }
                ActivityZonePatrolRuntime.NotifyControlReturned(BotOwner);
                if (RuntimeBotSpawnSpawner.TryGetLeashAssignment(BotOwner.ProfileId, out var startZone, out _, out var startSpawn, out var startFollower, out _) && ActivityZoneLeashCore.IsStrong(startZone))
                    ActivityZoneLeashCore.StrongLog(BotOwner, startZone,
                        $"Control=TAKEN Mode={startZone.leashMode} Brain={BotOwner.Brain?.GetStateName} SpawnPoint={startSpawn} Follower={startFollower} {ActivityZoneLeashCore.DescribeZone(startZone)} StaleMoverTargetCleared=True");
                ActivityZoneLeashCore.Log($"[MEL leash] Control taken profileId={BotOwner.ProfileId}, layer={GetName()}, brain={BotOwner.Brain?.GetStateName}, staleMoverTargetCleared=true.");
            }
            catch { }
        }

        public override void Stop()
        {
            try
            {
                var state = ActivityZoneLeashCore.GetState(BotOwner);
                state.Destination = null;
                ActivityZoneLeashCore.Log($"[MEL leash] Control released profileId={BotOwner.ProfileId}, activeLayer={ActivityZoneLeashCore.ActiveLayerName(BotOwner)}.");
                RecordSoftStop(state);
            }
            catch { }
        }

        private void RecordSoftStop(LeashState state)
        {
            var wasHeld = state.ControlHeld;
            state.ControlHeld = false;
            if (!wasHeld)
                return;
            if (!RuntimeBotSpawnSpawner.TryGetLeashAssignment(BotOwner.ProfileId, out var zone, out _, out _, out _, out _) || ActivityZoneLeashCore.IsStrong(zone))
                return;

            var now = Time.time;
            var held = now - state.TakenAt;
            // If our own IsActive returned false this very frame, MEL yielded itself (lock/holdoff). Otherwise a different
            // layer was selected without MEL being consulted; which layer is not observable here (the active layer is only
            // switched after Stop()).
            var selfYield = !state.LastEvalResult && Mathf.Approximately(state.LastEvalAt, now);
            // Only the combat yields reported by InCombatHoldoff count as rapid; lock/assignment/other yields do not.
            var combatYield = selfYield && (state.LastYieldReason == "yield-under-fire" || state.LastYieldReason == "yield-recent-enemy-contact");
            var rapid = (!selfYield || combatYield) && held < ActivityZoneLeashCore.SoftRapidStopWindowSeconds;

            state.ReentryPending = true;
            state.EligibleSince = -1f;
            state.CooldownLogged = false;
            if (rapid)
                state.CooldownUntil = now + ActivityZoneLeashCore.SoftRapidStopCooldownSeconds; // replaces, never accumulates

            ActivityZoneLeashCore.Log(
                $"[MEL leash] SoftHandoff stopped profileId={BotOwner.ProfileId}, zoneId={zone.zoneId}, mode={BotActivityZone.NormalizeLeashMode(zone.leashMode)}, held={held:F2}s, " +
                $"cause={(selfYield ? "mel-isactive-false(" + (state.LastYieldReason ?? "none") + ")" : "other-layer-selected(unverified which)")}, rapidStop={rapid}, cooldownActivated={rapid}, " +
                $"cooldownRemaining={(rapid ? ActivityZoneLeashCore.SoftRapidStopCooldownSeconds : 0f):F1}s, debounce={ActivityZoneLeashCore.SoftReentryDebounceSeconds:F1}s.");
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            try
            {
                var state = ActivityZoneLeashCore.GetState(BotOwner);
                stringBuilder.AppendLine($"MEL leash phase: {state.Phase}");
            }
            catch { }
        }
    }

    internal sealed class ActivityZoneLeashLogic : CustomLogic
    {
        public ActivityZoneLeashLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Update(CustomLayer.ActionData data)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                ActivityZoneLeashCore.Log($"[MEL leash] Update failed profileId={BotOwner?.ProfileId}: {ex.Message}");
            }
        }

        private void Tick()
        {
            var bot = BotOwner;
            if (bot == null || bot.IsDead)
                return;
            if (!RuntimeBotSpawnSpawner.TryGetLeashAssignment(bot.ProfileId, out var zone, out var zoneId, out var spawnPointId, out var isFollower, out _))
                return;

            // Same upkeep vanilla move logic does (doors, mines, danger places, magazine checks).
            bot.DoorOpener.UpdateDoorInteractionStatus();
            bot.BewarePlantedMine.Update();
            bot.BotAvoidDangerPlaces.Update();
            bot.MagazineChecker.ManualUpdate();

            var state = ActivityZoneLeashCore.GetState(bot);
            var now = Time.time;
            if (ActivityZonePatrolRuntime.Tick(bot, zone, now))
            {
                state.Destination = null;
                state.Phase = "patrolling";
                return;
            }

            bot.SetPose(1f);
            bot.SetTargetMoveSpeed(1f);

            var pos = bot.Position;
            RuntimeBotSpawnSpawner.TryIsInsideLeashZone(zone, pos, out var inside, out _);

            // Sprint only while returning to the zone from outside; walk once inside. Stamina is respected by the mover (it stops sprinting when exhausted).
            var returning = !inside && state.Destination.HasValue && !state.IssuedWhileInside;
            var strong = ActivityZoneLeashCore.IsStrong(zone);
            var edgeNow = ActivityZoneLeashCore.EdgeRatio(zone, pos);
            if (strong && state.LastInside != inside)
            {
                state.LastInside = inside;
                ActivityZoneLeashCore.StrongLog(bot, zone, $"Transition={(inside ? "ENTERED_ZONE" : "LEFT_ZONE")} Action={(inside ? "ROAMING" : "RECOVERY")} Layer={ActivityZoneLeashCore.ActiveLayerName(bot)}");
            }
            var wantSprint = returning && Plugin.SprintToZone.Value && ActivityZoneLeashCore.EdgeRatio(zone, pos) > 1.1f && bot.GetPlayer != null && bot.GetPlayer.Physical.CanSprint;
            bot.Sprint(wantSprint);

            if (state.Destination.HasValue)
            {
                bot.Steering.LookToMovingDirection();
                var dest = state.Destination.Value;
                var d = dest - pos;
                var horizontal = new Vector3(d.x, 0f, d.z).magnitude;
                var arrived = horizontal < ActivityZoneLeashCore.ArriveDistance && Mathf.Abs(d.y) < 3f;
                var stalled = now - state.IssuedAt > 2f && !bot.Mover.IsMoving && !bot.Mover.HasPathAndNoComplete;
                var timedOut = now - state.IssuedAt > state.Timeout;
                // A roaming destination was chosen while inside; if the bot has since crossed the boundary
                // (path detour, push, door/avoid logic) abandon it and head back inward instead of continuing outward.
                var crossedBoundary = state.IssuedWhileInside && !inside;
                // Strong: re-plan before the edge when the current destination does not lead back inward (hysteresis via cooldown).
                var approachingBoundary = strong && inside && edgeNow > ActivityZoneLeashCore.StrongApproachEdge &&
                    now >= state.NextBoundaryReplanAt && ActivityZoneLeashCore.EdgeRatio(zone, dest) >= edgeNow - 0.02f;

                if (arrived || stalled || timedOut || crossedBoundary || approachingBoundary)
                {
                    var result = arrived ? "arrived" : (stalled ? "stalled" : (timedOut ? "timed-out" : (approachingBoundary ? "near-boundary" : "left-zone")));
                    state.Destination = null;
                    if (arrived)
                    {
                        state.Phase = "waiting";
                        state.NextDecisionAt = now + (isFollower ? 1f : UnityEngine.Random.Range(4f, 10f));
                        state.FailedRounds = 0;
                    }
                    else if (crossedBoundary || approachingBoundary)
                    {
                        try { bot.StopMove(); } catch { }
                        state.Phase = approachingBoundary ? "replanning" : "retrying";
                        state.NextDecisionAt = now;
                        if (approachingBoundary)
                        {
                            state.NextBoundaryReplanAt = now + ActivityZoneLeashCore.StrongReplanCooldown;
                            ActivityZoneLeashCore.StrongLog(bot, zone, $"Approaching=True OldDest=({dest.x:F1},{dest.y:F1},{dest.z:F1}) OldDestEdge={ActivityZoneLeashCore.EdgeRatio(zone, dest):F2} Action=CLEARED Reason=ApproachingBoundary ReplanCooldown={ActivityZoneLeashCore.StrongReplanCooldown:F0}s");
                        }
                    }
                    else
                    {
                        state.Phase = "retrying";
                        state.NextDecisionAt = now + 3f;
                        state.FailedPoints.Add(dest);
                    }
                    ActivityZoneLeashCore.Log($"[MEL leash] Destination {result} profileId={bot.ProfileId}, distToDest={horizontal:F1}, insideZone={inside}.");
                }
            }
            else if (now >= state.NextDecisionAt)
            {
                // An escort already close to its boss inside the zone just waits; it re-targets when the boss moves away.
                if (isFollower && inside && RuntimeBotSpawnSpawner.TryGetEscortAnchor(bot.ProfileId, out var anchor) &&
                    ActivityZoneLeashCore.EdgeRatio(zone, anchor) <= 0.9f &&
                    new Vector3(anchor.x - pos.x, 0f, anchor.z - pos.z).magnitude < ActivityZoneLeashCore.EscortHoldDistance)
                {
                    state.Phase = "escort-holding";
                    state.NextDecisionAt = now + 2f;
                }
                else
                    SelectAndGo(bot, zone, state, inside, isFollower, now);
            }

            LogSample(bot, zone, zoneId, spawnPointId, isFollower, state, inside, pos, now);
        }

        private static void SelectAndGo(BotOwner bot, BotActivityZone zone, LeashState state, bool inside, bool isFollower, float now)
        {
            state.Phase = isFollower ? (inside ? "escorting" : "approaching") : (inside ? "roaming" : "approaching");
            var strong = ActivityZoneLeashCore.IsStrong(zone);
            if (strong && !inside)
                state.RecoveryAttempts++;
            if (!ActivityZoneLeashCore.TrySelectDestination(bot, zone, state, inside, isFollower, out var dest, out var length, out var reason))
            {
                if (strong)
                    ActivityZoneLeashCore.StrongLog(bot, zone, $"{(inside ? "Roam" : "Recovery")}=FAILED Reason={reason} Attempt={state.RecoveryAttempts} Round={state.FailedRounds + 1}/{ActivityZoneLeashCore.MaxFailedRoundsBeforeLock} RetryAfter=5s");
                RegisterFailure(bot, zone, state, now, reason);
                return;
            }

            var status = bot.GoToPoint(dest, true, ActivityZoneLeashCore.ReachDist, false, true, true, false, false);
            if (status != NavMeshPathStatus.PathComplete)
            {
                try { bot.StopMove(); } catch { }
                state.FailedPoints.Add(dest);
                if (strong)
                    ActivityZoneLeashCore.StrongLog(bot, zone, $"Dest=({dest.x:F1},{dest.y:F1},{dest.z:F1}) Action=REJECTED Reason=goto-{status} PathValid=False");
                RegisterFailure(bot, zone, state, now, "goto-" + status);
                return;
            }

            state.Destination = dest;
            state.LastDestination = dest;
            if (inside)
                state.RecoveryAttempts = 0;
            state.IssuedAt = now;
            state.IssuedWhileInside = inside;
            state.Timeout = 20f + length / 1.2f;
            state.FailedRounds = 0;
            if (strong)
                ActivityZoneLeashCore.StrongLog(bot, zone,
                    $"Action={(inside ? "ROAM_ACCEPTED" : "RECOVERY_ACCEPTED")} Dest=({dest.x:F1},{dest.y:F1},{dest.z:F1}) DestInside={ActivityZoneLeashCore.EdgeRatio(zone, dest) <= 1f} DestEdge={ActivityZoneLeashCore.EdgeRatio(zone, dest):F2} " +
                    $"NavMeshSample=OK PathValid=True PathInsideZone={inside} PathLength={length:F1} Timeout={state.Timeout:F0}s Attempt={state.RecoveryAttempts}");
            ActivityZoneLeashCore.Log(
                $"[MEL leash] Destination ACCEPTED profileId={bot.ProfileId}, phase={state.Phase}, returningToZone={!inside}, dest=({dest.x:F2},{dest.y:F2},{dest.z:F2}), " +
                $"destInsideZone={ActivityZoneLeashCore.EdgeRatio(zone, dest) <= 1f}, destEdgeRatio={ActivityZoneLeashCore.EdgeRatio(zone, dest):F2}, " +
                $"pathStatus=PathComplete, pathLength={length:F1}, pathCornersChecked={inside}, timeout={state.Timeout:F0}s.");
        }

        private static void RegisterFailure(BotOwner bot, BotActivityZone zone, LeashState state, float now, string reason)
        {
            state.FailedRounds++;
            state.Phase = "retrying";
            state.NextDecisionAt = now + 5f;
            ActivityZoneLeashCore.Log($"[MEL leash] Destination failed profileId={bot.ProfileId}, round={state.FailedRounds}/{ActivityZoneLeashCore.MaxFailedRoundsBeforeLock}, reason={reason}.");

            if (state.FailedRounds >= ActivityZoneLeashCore.MaxFailedRoundsBeforeLock)
            {
                state.LockCount++;
                var lockSeconds = 30f * Mathf.Min(state.LockCount, 3);
                state.LockedUntil = now + lockSeconds;
                state.FailedRounds = 0;
                state.FailedPoints.Clear();
                state.Phase = "locked";
                if (ActivityZoneLeashCore.IsStrong(zone))
                    ActivityZoneLeashCore.StrongLog(bot, zone, $"Recovery=GAVE_UP Reason={reason} Lock={lockSeconds:F0}s LockCount={state.LockCount} Action=NONE (no movement forced, no teleport)");
                ActivityZoneLeashCore.Log($"[MEL leash] Giving up temporarily profileId={bot.ProfileId}, lock={lockSeconds:F0}s, lockCount={state.LockCount}; vanilla behavior resumes. No movement was forced.");
            }
        }

        private static void LogSample(BotOwner bot, BotActivityZone zone, string zoneId, string spawnPointId, bool isFollower, LeashState state, bool inside, Vector3 pos, float now)
        {
            if (now < state.NextSampleLogAt || !RuntimeBotSpawnSpawner.LeashDiagnosticsEnabled)
                return;
            state.NextSampleLogAt = now + ActivityZoneLeashCore.SampleLogInterval;

            if (ActivityZoneLeashCore.IsStrong(zone))
            {
                var sd = state.Destination;
                ActivityZoneLeashCore.StrongLog(bot, zone,
                    $"Sample Mode={zone.leashMode} SpawnPoint={spawnPointId} Follower={isFollower} Brain={bot.Brain?.GetStateName} {ActivityZoneLeashCore.DescribeZone(zone)} " +
                    $"Pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) Approaching={(inside && ActivityZoneLeashCore.EdgeRatio(zone, pos) > ActivityZoneLeashCore.StrongApproachEdge)} " +
                    $"Dest={(sd.HasValue ? $"({sd.Value.x:F1},{sd.Value.y:F1},{sd.Value.z:F1})" : "none")} DestInside={(sd.HasValue && ActivityZoneLeashCore.EdgeRatio(zone, sd.Value) <= 1f)} " +
                    $"Phase={state.Phase} Layer={ActivityZoneLeashCore.ActiveLayerName(bot)} MelLayerActive=True Recovery={(inside ? "n/a" : "active")} RecoveryAttempts={state.RecoveryAttempts} " +
                    $"ReplanCooldownLeft={Mathf.Max(0f, state.NextBoundaryReplanAt - now):F1}s Locked={now < state.LockedUntil}");
            }

            var center = zone.position.ToVector3();
            var dx = pos.x - center.x;
            var dz = pos.z - center.z;
            var distCenter = Mathf.Sqrt(dx * dx + dz * dz);
            var dest = state.Destination.HasValue
                ? $"({state.Destination.Value.x:F2},{state.Destination.Value.y:F2},{state.Destination.Value.z:F2})"
                : "none";
            var destInside = state.Destination.HasValue && ActivityZoneLeashCore.EdgeRatio(zone, state.Destination.Value) <= 1f;
            ActivityZoneLeashCore.Log(
                $"[MEL leash] Sample profileId={bot.ProfileId}, {ActivityZoneLeashCore.DescribeBot(bot)}, follower={isFollower}, spawnPointId={spawnPointId}, zoneId={zoneId}, zoneEnabled={zone.enabled}, zoneValid=true, {ActivityZoneLeashCore.DescribeZone(zone)}, " +
                $"botPos=({pos.x:F2},{pos.y:F2},{pos.z:F2}), distToZoneCenter={distCenter:F1}, edgeRatio={ActivityZoneLeashCore.EdgeRatio(zone, pos):F2}, inside={inside}, phase={state.Phase}, " +
                $"leash=ACTIVE, returningToZone={(!inside)}, destination={dest}, destInsideZone={destInside}, activeLayer={ActivityZoneLeashCore.ActiveLayerName(bot)}.");
        }
    }
}
