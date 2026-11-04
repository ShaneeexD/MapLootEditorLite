using System;
using System.Collections.Generic;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace MapLootEditorLite.Client
{
    internal static class ActivityZonePatrolRuntime
    {
        private const float ArrivalRadius = 3.5f;
        private const float AuthorizationRadius = 5f;
        private const float CustomPointRadius = 6f;
        private const float ProgressDistance = 1f;
        private const float StalledSeconds = 12f;
        private const float RetryDelaySeconds = 4f;
        private const int MaxLegAttempts = 3;

        private enum PatrolPhase
        {
            Travel,
            Dwell
        }

        private sealed class PatrolState
        {
            public BotOwner Owner;
            public string SourceZoneId;
            public int PointIndex;
            public bool Returning;
            public PatrolPhase Phase;
            public BotActivityPatrolPoint Point;
            public BotActivityZone DestinationZone;
            public Vector3 Target;
            public Vector3[] PathCorners;
            public bool HasPreparedLeg;
            public int PreparedPointIndex;
            public bool PreparedReturning;
            public Vector3 PreparedTarget;
            public string PreparedDestinationZoneId;
            public BotActivityZone PreparedDestinationZone;
            public Vector3 PreparedDestinationCenter;
            public float PreparedDestinationRadius;
            public float PreparedDestinationWidth;
            public float PreparedDestinationDepth;
            public float PreparedDestinationYaw;
            public BotActivityZoneShape PreparedDestinationShape;
            public float DwellElapsed;
            public float LastTickAt;
            public float RetryAt;
            public float LastProgressAt;
            public Vector3 LastProgressPosition;
            public int LegAttempts;
            public bool CombatPaused;
        }

        private static readonly Dictionary<string, PatrolState> States =
            new Dictionary<string, PatrolState>(StringComparer.Ordinal);

        public static bool Tick(BotOwner bot, BotActivityZone sourceZone, float now)
        {
            if (bot == null || bot.IsDead || sourceZone == null || string.IsNullOrEmpty(bot.ProfileId))
                return false;
            if (!sourceZone.patrolEnabled || sourceZone.patrolPoints == null || !HasEnabledPoint(sourceZone))
            {
                Remove(bot.ProfileId, "route-disabled-or-empty");
                return false;
            }

            if (!States.TryGetValue(bot.ProfileId, out var state) || state == null ||
                !ReferenceEquals(state.Owner, bot) || !string.Equals(state.SourceZoneId, sourceZone.zoneId, StringComparison.Ordinal))
            {
                Remove(bot.ProfileId, "assignment-changed");
                state = new PatrolState
                {
                    Owner = bot,
                    SourceZoneId = sourceZone.zoneId,
                    PointIndex = FindNextEnabled(sourceZone, 0),
                    LastTickAt = now
                };
                States[bot.ProfileId] = state;
                ActivityZoneLeashCore.Log($"[MEL patrol] Started profileId={bot.ProfileId} zoneId={sourceZone.zoneId} firstPoint={GetPointId(sourceZone, state.PointIndex)}.");
            }

            if (ActivityZoneLeashCore.EdgeRatio(sourceZone, bot.Position) > 1f &&
                !AllowsPosition(bot, sourceZone, bot.Position))
            {
                Remove(bot.ProfileId, "bot-left-authorized-patrol-area");
                return false;
            }

            if (state.CombatPaused)
            {
                state.CombatPaused = false;
                state.LastTickAt = now;
                if (state.Phase == PatrolPhase.Travel)
                {
                    state.PathCorners = null;
                    state.LastProgressAt = now;
                    state.LastProgressPosition = bot.Position;
                }
                ActivityZoneLeashCore.Log($"[MEL patrol] Resumed profileId={bot.ProfileId} phase={state.Phase} combatTimerPaused=True.");
            }

            if (state.Phase == PatrolPhase.Dwell && HasDwellConfigurationChanged(sourceZone, state))
            {
                state.Phase = PatrolPhase.Travel;
                state.DwellElapsed = 0f;
                state.LastProgressAt = 0f;
                ActivityZoneLeashCore.Log($"[MEL patrol] Replanning profileId={bot.ProfileId} pointId={state.Point?.id ?? "return"} reason=route-configuration-changed.");
            }

            if (state.Phase == PatrolPhase.Dwell)
            {
                var elapsed = Mathf.Clamp(now - state.LastTickAt, 0f, 0.25f);
                state.LastTickAt = now;
                state.DwellElapsed += elapsed;
                if (state.Point == null || state.DwellElapsed >= GetDwellSeconds(state.Point.dwellSeconds))
                    AdvanceAfterDwell(bot, sourceZone, state, now);
                else
                    return true;
            }

            if (!PrepareLeg(bot, sourceZone, state, now))
                return States.TryGetValue(bot.ProfileId, out var current) && ReferenceEquals(current, state);

            if (HasArrived(bot.Position, state))
            {
                if (state.Returning)
                {
                    ActivityZoneLeashCore.Log($"[MEL patrol] Returned profileId={bot.ProfileId} zoneId={sourceZone.zoneId}; authorization revoked.");
                    Remove(bot.ProfileId, "returned-to-origin");
                    return false;
                }

                state.Phase = PatrolPhase.Dwell;
                state.DwellElapsed = 0f;
                state.LastTickAt = now;
                state.PathCorners = null;
                ActivityZoneLeashCore.Log($"[MEL patrol] Arrival profileId={bot.ProfileId} pointId={state.Point.id} dwell={GetDwellSeconds(state.Point.dwellSeconds):F0}s.");
                ActivityZoneLeashCore.Log($"[MEL patrol] Dwell started profileId={bot.ProfileId} pointId={state.Point.id}; combat pauses the timer.");
                return true;
            }

            if (state.LastProgressAt <= 0f)
            {
                state.LastProgressAt = now;
                state.LastProgressPosition = bot.Position;
            }
            else
            {
                var progress = bot.Position - state.LastProgressPosition;
                progress.y = 0f;
                if (progress.magnitude >= ProgressDistance)
                {
                    state.LastProgressAt = now;
                    state.LastProgressPosition = bot.Position;
                }
                else if (now - state.LastProgressAt >= StalledSeconds)
                {
                    FailLeg(bot, sourceZone, state, now, "no-position-progress");
                    return true;
                }
            }

            return true;
        }

        public static void NotifyCombat(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId) ||
                !States.TryGetValue(bot.ProfileId, out var state) || state == null ||
                !ReferenceEquals(state.Owner, bot) || state.CombatPaused)
                return;

            state.CombatPaused = true;
            state.LastTickAt = Time.time;
            ActivityZoneLeashCore.Log($"[MEL patrol] Combat interruption profileId={bot.ProfileId} phase={state.Phase}; authorization retained and dwell paused.");
        }

        public static void NotifyControlReturned(BotOwner bot)
        {
            if (!TryGetState(bot, out var state) || state.Phase != PatrolPhase.Travel)
                return;
            state.PathCorners = null;
            state.LastProgressAt = Time.time;
            state.LastProgressPosition = bot.Position;
        }

        public static bool AllowsPosition(BotOwner bot, BotActivityZone assignedZone, Vector3 position)
        {
            if (!TryGetState(bot, out var state) || assignedZone == null ||
                !string.Equals(state.SourceZoneId, assignedZone.zoneId, StringComparison.Ordinal))
                return false;

            if (state.DestinationZone != null &&
                (state.Phase == PatrolPhase.Dwell || state.PathCorners != null) &&
                ActivityZoneLeashCore.EdgeRatio(state.DestinationZone, position) <= 1f)
                return true;

            if (state.Phase == PatrolPhase.Dwell)
                return HorizontalDistance(position, state.Target) <= CustomPointRadius;

            return state.PathCorners != null &&
                IsNearPath(position, state.PathCorners, AuthorizationRadius);
        }

        private static bool IsFinite(Vector3 point)
        {
            return !(float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                float.IsNaN(point.y) || float.IsInfinity(point.y) ||
                float.IsNaN(point.z) || float.IsInfinity(point.z));
        }

        public static bool IsActive(BotOwner bot, BotActivityZone assignedZone)
        {
            return TryGetState(bot, out var state) && assignedZone != null &&
                string.Equals(state.SourceZoneId, assignedZone.zoneId, StringComparison.Ordinal);
        }

        public static void Remove(string profileId, string reason)
        {
            if (string.IsNullOrEmpty(profileId) || !States.TryGetValue(profileId, out var state))
                return;
            States.Remove(profileId);
            ActivityZoneLeashCore.Log($"[MEL patrol] Authorization revoked profileId={profileId} reason={reason}.");
        }

        public static void ClearAll(string reason)
        {
            if (States.Count == 0)
                return;
            foreach (var profileId in new List<string>(States.Keys))
                Remove(profileId, reason);
        }

        private static bool TryGetState(BotOwner bot, out PatrolState state)
        {
            state = null;
            if (bot == null || bot.IsDead || string.IsNullOrEmpty(bot.ProfileId) ||
                !States.TryGetValue(bot.ProfileId, out state) || state == null ||
                !ReferenceEquals(state.Owner, bot))
                return false;
            return true;
        }

        private static bool HasEnabledPoint(BotActivityZone zone)
        {
            for (var i = 0; i < zone.patrolPoints.Count; i++)
                if (zone.patrolPoints[i] != null && zone.patrolPoints[i].enabled)
                    return true;
            return false;
        }

        private static int FindNextEnabled(BotActivityZone zone, int startIndex)
        {
            if (zone?.patrolPoints == null)
                return -1;
            for (var i = Math.Max(0, startIndex); i < zone.patrolPoints.Count; i++)
                if (zone.patrolPoints[i] != null && zone.patrolPoints[i].enabled)
                    return i;
            return -1;
        }

        private static string GetPointId(BotActivityZone zone, int index)
        {
            return zone?.patrolPoints != null && index >= 0 && index < zone.patrolPoints.Count
                ? zone.patrolPoints[index]?.id ?? "invalid"
                : "none";
        }

        private static void AdvanceAfterDwell(BotOwner bot, BotActivityZone sourceZone, PatrolState state, float now)
        {
            ActivityZoneLeashCore.Log($"[MEL patrol] Dwell ended profileId={bot.ProfileId} pointId={state.Point?.id}; selecting next route state.");
            state.Phase = PatrolPhase.Travel;
            state.Point = null;
            state.PathCorners = null;
            state.LegAttempts = 0;
            state.LastProgressAt = 0f;
            state.RetryAt = now;

            var next = FindNextEnabled(sourceZone, state.PointIndex + 1);
            if (next >= 0)
            {
                state.PointIndex = next;
                state.Returning = false;
            }
            else
            {
                state.Returning = true;
            }
        }

        private static bool PrepareLeg(BotOwner bot, BotActivityZone sourceZone, PatrolState state, float now)
        {
            Vector3 requestedTarget;
            BotActivityZone destinationZone;
            string destinationZoneId;
            var pointIndex = state.Returning ? -1 : state.PointIndex;
            if (state.Returning)
            {
                state.Point = null;
                destinationZone = sourceZone;
                destinationZoneId = sourceZone.zoneId;
                requestedTarget = sourceZone.position.ToVector3();
            }
            else
            {
                if (state.PointIndex < 0 || state.PointIndex >= sourceZone.patrolPoints.Count)
                {
                    state.Returning = true;
                    return PrepareLeg(bot, sourceZone, state, now);
                }

                state.Point = sourceZone.patrolPoints[state.PointIndex];
                if (state.Point == null || !state.Point.enabled)
                {
                    var next = FindNextEnabled(sourceZone, state.PointIndex + 1);
                    if (next >= 0)
                        state.PointIndex = next;
                    else
                        state.Returning = true;
                    return PrepareLeg(bot, sourceZone, state, now);
                }

                destinationZoneId = state.Point.destinationZoneId;
                destinationZone = null;
                if (!string.IsNullOrWhiteSpace(destinationZoneId))
                {
                    if (!RuntimeBotSpawnSpawner.TryGetLeashActivityZone(destinationZoneId, out destinationZone))
                    {
                        FailLeg(bot, sourceZone, state, now, "destination-zone-unavailable");
                        return false;
                    }
                }

                requestedTarget = state.Point.position != null
                    ? state.Point.position.ToVector3()
                    : sourceZone.position.ToVector3();
                if (!IsFinite(requestedTarget))
                {
                    FailLeg(bot, sourceZone, state, now, "invalid-point-position");
                    return false;
                }
                if (destinationZone != null &&
                    ActivityZoneLeashCore.EdgeRatio(destinationZone, requestedTarget) > 1f)
                    requestedTarget = destinationZone.position.ToVector3();
            }

            state.DestinationZone = destinationZone;
            if (HasPreparedLegChanged(state, pointIndex, requestedTarget, destinationZoneId, destinationZone))
            {
                state.PathCorners = null;
                state.RetryAt = 0f;
                state.LegAttempts = 0;
                state.LastProgressAt = 0f;
                state.Target = requestedTarget;
                RememberPreparedLeg(state, pointIndex, requestedTarget, destinationZoneId, destinationZone);
            }

            if (state.PathCorners == null)
            {
                if (now < state.RetryAt)
                    return false;
                if (!TryBuildPath(bot.Position, state.Target, state.DestinationZone, out var corners, out var navTarget))
                {
                    FailLeg(bot, sourceZone, state, now, "navmesh-path-unavailable");
                    return false;
                }
                state.PathCorners = corners;
                state.Target = navTarget;
                state.LastProgressAt = now;
                state.LastProgressPosition = bot.Position;
                var status = bot.GoToPoint(state.Target, true, ActivityZoneLeashCore.ReachDist, false, true, true, false, false);
                if (status != NavMeshPathStatus.PathComplete)
                {
                    FailLeg(bot, sourceZone, state, now, "goto-" + status);
                    return false;
                }
                ActivityZoneLeashCore.Log($"[MEL patrol] Authorization granted profileId={bot.ProfileId} sourceZone={sourceZone.zoneId} destinationZone={state.DestinationZone?.zoneId ?? "position"} target=({state.Target.x:F1},{state.Target.y:F1},{state.Target.z:F1}) pathCorners={corners.Length}.");
            }
            return true;
        }

        private static bool HasPreparedLegChanged(
            PatrolState state, int pointIndex, Vector3 target, string destinationZoneId, BotActivityZone destinationZone)
        {
            if (!state.HasPreparedLeg || state.PreparedPointIndex != pointIndex ||
                state.PreparedReturning != state.Returning || !state.PreparedTarget.Equals(target) ||
                !string.Equals(state.PreparedDestinationZoneId, destinationZoneId, StringComparison.Ordinal) ||
                !ReferenceEquals(state.PreparedDestinationZone, destinationZone))
                return true;
            if (destinationZone == null)
                return false;

            var center = destinationZone.position.ToVector3();
            var yaw = destinationZone.rotation != null ? destinationZone.rotation.y : 0f;
            return !state.PreparedDestinationCenter.Equals(center) ||
                !state.PreparedDestinationRadius.Equals(destinationZone.radius) ||
                !state.PreparedDestinationWidth.Equals(destinationZone.EffectiveWidth) ||
                !state.PreparedDestinationDepth.Equals(destinationZone.EffectiveDepth) ||
                !state.PreparedDestinationYaw.Equals(yaw) ||
                state.PreparedDestinationShape != destinationZone.shape;
        }

        private static bool HasDwellConfigurationChanged(BotActivityZone sourceZone, PatrolState state)
        {
            if (state.Returning || state.PointIndex < 0 || state.PointIndex >= sourceZone.patrolPoints.Count)
                return true;
            var point = sourceZone.patrolPoints[state.PointIndex];
            if (point == null || !point.enabled || !ReferenceEquals(point, state.Point))
                return true;

            var destinationZoneId = point.destinationZoneId;
            BotActivityZone destinationZone = null;
            if (!string.IsNullOrWhiteSpace(destinationZoneId) &&
                !RuntimeBotSpawnSpawner.TryGetLeashActivityZone(destinationZoneId, out destinationZone))
                return true;
            var target = point.position != null ? point.position.ToVector3() : sourceZone.position.ToVector3();
            if (!IsFinite(target))
                return true;
            if (destinationZone != null && ActivityZoneLeashCore.EdgeRatio(destinationZone, target) > 1f)
                target = destinationZone.position.ToVector3();
            return HasPreparedLegChanged(state, state.PointIndex, target, destinationZoneId, destinationZone);
        }

        private static void RememberPreparedLeg(
            PatrolState state, int pointIndex, Vector3 target, string destinationZoneId, BotActivityZone destinationZone)
        {
            state.HasPreparedLeg = true;
            state.PreparedPointIndex = pointIndex;
            state.PreparedReturning = state.Returning;
            state.PreparedTarget = target;
            state.PreparedDestinationZoneId = destinationZoneId;
            state.PreparedDestinationZone = destinationZone;
            if (destinationZone == null)
                return;
            state.PreparedDestinationCenter = destinationZone.position.ToVector3();
            state.PreparedDestinationRadius = destinationZone.radius;
            state.PreparedDestinationWidth = destinationZone.EffectiveWidth;
            state.PreparedDestinationDepth = destinationZone.EffectiveDepth;
            state.PreparedDestinationYaw = destinationZone.rotation != null ? destinationZone.rotation.y : 0f;
            state.PreparedDestinationShape = destinationZone.shape;
        }

        private static bool TryBuildPath(Vector3 start, Vector3 target, BotActivityZone destinationZone, out Vector3[] corners, out Vector3 navTarget)
        {
            corners = null;
            navTarget = target;
            if (!IsFinite(start) || !IsFinite(target))
                return false;
            if (!NavMesh.SamplePosition(start, out var startNav, 2.5f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(target, out var targetNav, 3f, NavMesh.AllAreas))
                return false;
            if (destinationZone != null && ActivityZoneLeashCore.EdgeRatio(destinationZone, targetNav.position) > 1f)
                return false;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startNav.position, targetNav.position, NavMesh.AllAreas, path) ||
                path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length == 0)
                return false;
            corners = path.corners;
            navTarget = targetNav.position;
            return true;
        }

        private static bool HasArrived(Vector3 position, PatrolState state)
        {
            if (HorizontalDistance(position, state.Target) > ArrivalRadius || Mathf.Abs(position.y - state.Target.y) > 3f)
                return false;
            return state.DestinationZone == null || ActivityZoneLeashCore.EdgeRatio(state.DestinationZone, position) <= 1f;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            var dx = a.x - b.x;
            var dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float GetDwellSeconds(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 60f : Mathf.Clamp(value, 1f, 3600f);
        }

        private static bool IsNearPath(Vector3 position, Vector3[] corners, float radius)
        {
            if (corners == null || corners.Length == 0)
                return false;
            if (corners.Length == 1)
                return Vector3.Distance(position, corners[0]) <= radius;
            for (var i = 1; i < corners.Length; i++)
            {
                var start = corners[i - 1];
                var segment = corners[i] - start;
                var lengthSquared = segment.sqrMagnitude;
                var t = lengthSquared > 0.001f ? Mathf.Clamp01(Vector3.Dot(position - start, segment) / lengthSquared) : 0f;
                if (Vector3.Distance(position, start + segment * t) <= radius)
                    return true;
            }
            return false;
        }

        private static void FailLeg(BotOwner bot, BotActivityZone sourceZone, PatrolState state, float now, string reason)
        {
            state.LegAttempts++;
            state.PathCorners = null;
            if (state.LegAttempts < MaxLegAttempts)
            {
                state.RetryAt = now + RetryDelaySeconds;
                ActivityZoneLeashCore.Log($"[MEL patrol] Leg retry profileId={bot.ProfileId} pointId={state.Point?.id ?? "return"} attempt={state.LegAttempts}/{MaxLegAttempts} reason={reason} retryIn={RetryDelaySeconds:F0}s.");
                return;
            }

            ActivityZoneLeashCore.Log($"[MEL patrol] Leg abandoned profileId={bot.ProfileId} pointId={state.Point?.id ?? "return"} reason={reason}.");
            state.LegAttempts = 0;
            state.LastProgressAt = 0f;
            state.RetryAt = now + RetryDelaySeconds;
            if (state.Returning)
            {
                Remove(bot.ProfileId, "return-leg-failed");
                return;
            }

            var next = FindNextEnabled(sourceZone, state.PointIndex + 1);
            if (next >= 0)
            {
                state.PointIndex = next;
            }
            else
            {
                state.Returning = true;
            }
            state.Point = null;
            state.DestinationZone = null;
        }
    }
}
