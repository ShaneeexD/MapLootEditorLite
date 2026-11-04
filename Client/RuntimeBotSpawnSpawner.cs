using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using EFT.Quests;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeBotSpawnSpawner : MonoBehaviour
    {
        public static RuntimeBotSpawnSpawner Instance { get; private set; }

        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private static bool _patchApplied;
        private List<SpawnRequest> _spawnRequests = new List<SpawnRequest>();
        private System.Random _rng;
        private BotsController _botsController;
        private List<TriggerZone> _triggerZones = new List<TriggerZone>();
        private Dictionary<string, List<TriggerSpawnData>> _triggeredSpawnData = new Dictionary<string, List<TriggerSpawnData>>();
        private Dictionary<string, TriggerState> _triggerStates = new Dictionary<string, TriggerState>();
        private List<PendingTrigger> _pendingTriggers = new List<PendingTrigger>();
        private readonly Dictionary<BotSpawnPoint, string> _botSpawnGroupKeys = new Dictionary<BotSpawnPoint, string>();
        private readonly HashSet<string> _invalidBotSpawnGroupKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _dispatchedBotSpawnGroups = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _submittedBotSpawnGroupEvents = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, BotActivityZone> _activityZonesById = new Dictionary<string, BotActivityZone>(StringComparer.Ordinal);
        private readonly HashSet<string> _ambiguousActivityZoneIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ActivityBotTracking> _activityBotsByProfileId = new Dictionary<string, ActivityBotTracking>(StringComparer.Ordinal);
        private BotSpawner _activityObservedSpawner;
        private bool _activityZonesIndexed;
        private float _nextActivityDiagnosticTime;
        private float _raidStartTime;
        private int _triggerActivationCounter;
        private Vector3? _pendingForcePosition;
        private Quaternion? _pendingForceRotation;
        private bool _forceSpawned;

        private class TriggerState
        {
            public float LastActivationTime;
            public HashSet<string> ActivatedPlayers = new HashSet<string>();
        }

        private class PendingTrigger
        {
            public TriggerZone Trigger;
            public string PlayerProfileId;
            public float ActivationTime;
        }

        private class SpawnRequest
        {
            public BotZone Zone;
            public SpawnPointMarker Marker;
            public BotSpawnPoint Point;
            public BotSpawnZone ZoneData;
            public BotSpawnGroup Group;
            public string ResolvedWildSpawnType;
            public string BotSpawnGroupKey;
            public string BotSpawnGroupEventId;
        }

        private sealed class ActivityBotTracking
        {
            public string ProfileId;
            public string ZoneId;
            public string SpawnPointId;
            public string SpawnPath;
            public float RequestedAt;
            public float RegisteredAt;
            public bool Confirmed;
            public BotOwner Owner;
        }

        private class EligibleBotSpawnPoint
        {
            public BotSpawnPoint Point;
            public string BotZoneName;
            public BotZone BotZone;
        }

        private class TriggerSpawnData
        {
            public BotSpawnPoint Point;
            public BotSpawnZone Zone;
            public BotZone BotZone;
            public string GroupKey;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            _rng = new System.Random();
            LoadPacks();
            ApplyPatch();
        }

        public void ResetState()
        {
            ClearActivityTracking("raid-reset");
            _packs = RuntimePackLoader.LoadPacks("bot spawns");
            _currentWorld = null;
            _currentMapId = null;
            _spawnRequests.Clear();
            _botsController = null;
            _triggerZones.Clear();
            _triggeredSpawnData.Clear();
            _triggerStates.Clear();
            _pendingTriggers.Clear();
            _botSpawnGroupKeys.Clear();
            _invalidBotSpawnGroupKeys.Clear();
            _dispatchedBotSpawnGroups.Clear();
            _submittedBotSpawnGroupEvents.Clear();
            _activityZonesById.Clear();
            _ambiguousActivityZoneIds.Clear();
            _activityZonesIndexed = false;
            _raidStartTime = 0f;
            _triggerActivationCounter = 0;
            _pendingForcePosition = null;
            _pendingForceRotation = null;
            _forceSpawned = false;
        }

        private void LoadPacks()
        {
            _packs = RuntimePackLoader.LoadPacks("bot spawns");
        }

        private void BuildActivityZoneIndex(string mapId)
        {
            _activityZonesById.Clear();
            _ambiguousActivityZoneIds.Clear();
            foreach (var pack in _packs)
            {
                if (RuntimePackLoader.TryGetMapData(pack, mapId, out var map))
                    IndexActivityZones(map.botActivityZones);
            }
            _activityZonesIndexed = true;
        }

        private void ApplyPatch()
        {
            if (_patchApplied)
                return;
            _patchApplied = true;

            try
            {
                var harmony = new Harmony("com.shane.mapeditorlite.botspawns");
                var method = AccessTools.Method(typeof(BotsController), nameof(BotsController.Init));
                var prefix = AccessTools.Method(typeof(RuntimeBotSpawnSpawner), nameof(InitBotsControllerPrefix));
                var postfix = AccessTools.Method(typeof(RuntimeBotSpawnSpawner), nameof(InitBotsControllerPostfix));
                harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                Plugin.Log.LogInfo("Patched BotsController.Init.");
                // Run executes after BotsController.Init, so MEL's rolled boss requests already exist.
                harmony.Patch(AccessTools.Method(typeof(BossSpawnScenario), nameof(BossSpawnScenario.Run)),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(RuntimeBotSpawnSpawner), nameof(BossScenarioRunPrefix))));
                FollowerCreationDiagnostics.Apply();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to apply patch: {ex.Message}");
            }
        }

        public static void InitBotsControllerPrefix(BotZone[] __2)
        {
            if (Instance == null)
            {
                Plugin.Log.LogWarning("No spawner instance, skipping custom bot spawn points.");
                return;
            }

            try
            {
                Instance.SpawnCustomMarkers(__2);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in prefix: {ex.Message}");
            }
        }

        public static void BossScenarioRunPrefix(BossSpawnScenario __instance)
        {
            try
            {
                Instance?.SuppressVanillaBossWaves(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MEL boss] Failed to filter vanilla boss waves: {ex.Message}");
            }
        }

        private void SuppressVanillaBossWaves(BossSpawnScenario scenario)
        {
            if (!Plugin.SuppressVanillaBossWaves.Value || scenario?.BossSpawnWaves == null || _spawnRequests.Count == 0)
                return;

            var melBosses = new HashSet<WildSpawnType>();
            foreach (var request in _spawnRequests)
            {
                var role = ResolveWildSpawnType(request);
                if (role.HasValue && role.Value.ToString().StartsWith("boss", StringComparison.OrdinalIgnoreCase))
                    melBosses.Add(role.Value);
            }
            if (melBosses.Count == 0)
                return;

            var removed = scenario.BossSpawnWaves.Where(w => w != null && melBosses.Contains(w.BossType)).ToList();
            if (removed.Count == 0)
                return;

            scenario.BossSpawnWaves = scenario.BossSpawnWaves.Except(removed).ToArray();
            Plugin.Log.LogInfo($"[MEL boss] Removed {removed.Count} vanilla boss wave(s) for MEL-spawned boss type(s) [{string.Join(", ", melBosses.Select(b => b.ToString()))}]: {string.Join(", ", removed.Select(w => $"{w.BossType}@{(string.IsNullOrEmpty(w.BossZone) ? "any" : w.BossZone)}"))}.");
        }

        public static void InitBotsControllerPostfix(BotsController __instance)
        {
            if (Instance == null)
            {
                Plugin.Log.LogWarning("No spawner instance, skipping custom bot spawn.");
                return;
            }

            try
            {
                Instance.ForceSpawnCustomBots(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error in postfix: {ex.Message}");
            }
        }

        private void SpawnCustomMarkers(BotZone[] botZones)
        {
            var world = Singleton<GameWorld>.Instance;
            var mapId = world?.LocationId;
            if (string.IsNullOrEmpty(mapId) && world?.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (string.IsNullOrEmpty(mapId))
            {
                Plugin.Log.LogWarning("Cannot spawn bot points: no current map.");
                return;
            }

            if (_currentWorld != null && !object.ReferenceEquals(_currentWorld, world))
            {
                ClearActivityTracking("world-changed");
                _dispatchedBotSpawnGroups.Clear();
                _submittedBotSpawnGroupEvents.Clear();
                _activityZonesById.Clear();
                _ambiguousActivityZoneIds.Clear();
                _activityZonesIndexed = false;
            }
            _currentWorld = world;
            _currentMapId = mapId;
            _activityZonesById.Clear();
            _ambiguousActivityZoneIds.Clear();
            _activityZonesIndexed = false;
            _spawnRequests.Clear();
            _triggerZones.Clear();
            _triggeredSpawnData.Clear();
            _triggerStates.Clear();
            _pendingTriggers.Clear();
            _botSpawnGroupKeys.Clear();
            _invalidBotSpawnGroupKeys.Clear();
            _triggerActivationCounter = 0;
            _raidStartTime = Time.time;

            var points = new List<BotSpawnPoint>();
            var zones = new List<BotSpawnZone>();
            var pmcZones = new List<PmcSpawnZone>();
            for (int packIndex = 0; packIndex < _packs.Count; packIndex++)
            {
                var pack = _packs[packIndex];
                if (RuntimePackLoader.TryGetMapData(pack, mapId, out var map))
                {
                    ValidateBotSpawnGroups(packIndex, mapId, map.botSpawnPoints);
                    var deployGate = BuildDeploymentGate(map, mapId);
                    foreach (var point in map.botSpawnPoints ?? new List<BotSpawnPoint>())
                    {
                        if (point == null)
                            continue;
                        var groupKey = GetBotSpawnGroupKey(packIndex, mapId, point);
                        if (groupKey != null)
                            _botSpawnGroupKeys[point] = groupKey;

                        if (groupKey != null && _invalidBotSpawnGroupKeys.Contains(groupKey))
                            continue;
                        if (!QuestConditionsMet(point.questOnly, point.questCompleted, point.questId, "bot spawn point", point.name, point.id, mapId))
                            continue;

                        if (point.forcePlayerSpawn)
                        {
                            if (!_pendingForcePosition.HasValue)
                            {
                                _pendingForcePosition = point.position.ToVector3();
                                _pendingForceRotation = Quaternion.Euler(point.rotation.ToVector3());
                                Plugin.Log.LogInfo($"Registered force player spawn point '{point.name}' at {_pendingForcePosition.Value}.");
                            }
                            continue;
                        }

                        if (!deployGate.IsEligible(point.deploymentId, "bot spawn point", point.name, point.id, mapId, Plugin.Log.LogWarning))
                            continue;

                        points.Add(point);
                    }
                    foreach (var zone in map.botSpawnZones ?? new List<BotSpawnZone>())
                    {
                        if (QuestConditionsMet(zone.questOnly, zone.questCompleted, zone.questId, "bot spawn zone", zone.name, zone.id, mapId)
                            && deployGate.IsEligible(zone.deploymentId, "bot spawn zone", zone.name, zone.id, mapId, Plugin.Log.LogWarning))
                            zones.Add(zone);
                    }
                    foreach (var pmc in map.pmcSpawnZones ?? new List<PmcSpawnZone>())
                    {
                        if (!QuestConditionsMet(pmc.questOnly, pmc.questCompleted, pmc.questId, "PMC spawn zone", pmc.name, pmc.id, mapId))
                            continue;

                        if (pmc.forcePlayerSpawn)
                        {
                            if (!_pendingForcePosition.HasValue)
                            {
                                _pendingForcePosition = pmc.position.ToVector3();
                                _pendingForceRotation = Quaternion.Euler(pmc.rotation.ToVector3());
                                Plugin.Log.LogInfo($"Registered force player spawn zone '{pmc.name}' at {_pendingForcePosition.Value}.");
                            }
                            continue;
                        }

                        pmcZones.Add(pmc);
                    }
                    foreach (var trigger in map.triggerZones ?? new List<TriggerZone>())
                    {
                        _triggerZones.Add(trigger);
                    }
                }
            }

            try
            {
                BuildActivityZoneIndex(mapId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MEL activity-zone] Zone index build failed; ownership will be indexed lazily: {ex.Message}");
                _activityZonesIndexed = false;
            }

            Plugin.Log.LogInfo($"Spawning for map '{mapId}' with {points.Count} points, {zones.Count} zones, {pmcZones.Count} PMC zones and {_triggerZones.Count} trigger zones from {_packs.Count} packs.");
            if (points.Count == 0 && zones.Count == 0 && pmcZones.Count == 0 && _triggerZones.Count == 0)
            {
                Plugin.Log.LogInfo($"No custom bot spawn or trigger data for map {mapId}.");
                return;
            }

            foreach (var point in points.Where(p => p.triggerActivated))
            {
                if (point.position == null)
                {
                    Plugin.Log.LogWarning($"Trigger-activated point {point.id} has no configured position and is ineligible.");
                    continue;
                }
                var zoneName = ResolveBotZoneName(point.botZoneName, point.position.ToVector3(), botZones);
                var botZone = botZones?.FirstOrDefault(z => z.name == zoneName);
                if (botZone == null)
                {
                    Plugin.Log.LogWarning($"Trigger-activated point {point.id} has no valid BotZone, skipping.");
                    continue;
                }
                _botSpawnGroupKeys.TryGetValue(point, out var groupKey);
                AddTriggeredSpawnData(point.triggerZoneName, new TriggerSpawnData { Point = point, BotZone = botZone, GroupKey = groupKey });
            }

            foreach (var zone in zones.Where(z => z.triggerActivated))
            {
                var zoneName = ResolveBotZoneName(zone.botZoneName, zone.position.ToVector3(), botZones);
                var botZone = botZones?.FirstOrDefault(z => z.name == zoneName);
                if (botZone == null)
                {
                    Plugin.Log.LogWarning($"Trigger-activated zone {zone.id} has no valid BotZone, skipping.");
                    continue;
                }
                AddTriggeredSpawnData(zone.triggerZoneName, new TriggerSpawnData { Zone = zone, BotZone = botZone });
            }

            var created = 0;

            foreach (var point in points.Where(p => !p.triggerActivated && string.IsNullOrWhiteSpace(p.botSpawnGroupId)))
            {
                if (point.spawnChance < 100f && _rng.NextDouble() * 100 > point.spawnChance)
                    continue;
                var zoneName = ResolveBotZoneName(point.botZoneName, point.position.ToVector3(), botZones);
                var resolvedType = point.randomSpawnTypes != null && point.randomSpawnTypes.Count > 0
                    ? point.randomSpawnTypes[_rng.Next(point.randomSpawnTypes.Count)]
                    : point.wildSpawnType;
                var marker = CreateSpawnMarker(point.id, point.position.ToVector3(), point.rotation.ToVector3().y, point.radius, point.side, point.category, point.delayToCanSpawnSec, zoneName, botZones, point, resolvedType: resolvedType);
                if (marker != null)
                    created++;
            }

            foreach (var group in points
                         .Where(point => !point.triggerActivated && !string.IsNullOrWhiteSpace(point.botSpawnGroupId))
                         .Where(point => _botSpawnGroupKeys.ContainsKey(point) && !_invalidBotSpawnGroupKeys.Contains(_botSpawnGroupKeys[point]))
                         .GroupBy(point => _botSpawnGroupKeys[point], StringComparer.Ordinal))
            {
                var dispatchCount = CreateGroupedBotSpawnMarker(group.Key, group.ToList(), botZones, _dispatchedBotSpawnGroups);
                created += dispatchCount;
            }

            foreach (var zone in zones.Where(z => !z.triggerActivated))
            {
                if (zone.spawnChance < 100f && _rng.NextDouble() * 100 > zone.spawnChance)
                    continue;

                var zoneName = ResolveBotZoneName(zone.botZoneName, zone.position.ToVector3(), botZones);
                created += CreateZoneMarkers(zone, botZones, zoneName, "spawn").Count;
            }

            foreach (var pmc in pmcZones)
            {
                if (pmc.spawnChance < 100f && _rng.NextDouble() * 100 > pmc.spawnChance)
                    continue;

                var zoneName = ResolveBotZoneName(pmc.botZoneName, pmc.position.ToVector3(), botZones);
                if (string.IsNullOrWhiteSpace(zoneName))
                {
                    Plugin.Log.LogWarning($"PmcSpawnZone {pmc.id} has no valid BotZone, skipping.");
                    continue;
                }

                var pmcZone = new BotSpawnZone
                {
                    id = pmc.id,
                    position = pmc.position,
                    rotation = pmc.rotation,
                    radius = pmc.radius,
                    scale = pmc.scale,
                    shape = pmc.shape,
                    side = pmc.side,
                    category = pmc.category,
                    preset = pmc.preset,
                    wildSpawnType = pmc.wildSpawnType,
                    spawnCount = _rng.Next(pmc.minGroupSize, pmc.maxGroupSize + 1),
                    spawnChance = 100f,
                    spawnMode = "Potential",
                    delayToCanSpawnSec = pmc.delayToCanSpawnSec,
                    botZoneName = zoneName
                };
                created += CreateZoneMarkers(pmcZone, botZones, zoneName, "pmc").Count;
            }

            Plugin.Log.LogInfo($"Created {created} custom bot spawn markers for map {mapId} across {botZones?.Length ?? 0} zones. {points.Count(p => p.triggerActivated) + zones.Count(z => z.triggerActivated)} spawns are trigger-activated.");
        }

        private void ValidateBotSpawnGroups(int packIndex, string mapId, List<BotSpawnPoint> points)
        {
            if (points == null)
                return;

            var duplicateMarkerIds = new HashSet<string>(points
                .Where(point => point != null && !string.IsNullOrWhiteSpace(point.id))
                .GroupBy(point => point.id, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key), StringComparer.Ordinal);

            foreach (var group in points
                         .Where(point => point != null && !string.IsNullOrWhiteSpace(point.botSpawnGroupId))
                         .GroupBy(point => point.botSpawnGroupId.Trim(), StringComparer.Ordinal))
            {
                var key = $"{packIndex}|{mapId}|{group.Key}";
                var first = group.First();
                var invalidId = group.Key.Length > 128 || group.Key.Any(char.IsControl);
                var incompatible = group.Any(point => !BotSpawnPointsShareEventConfiguration(first, point));
                var duplicateMemberId = group.Any(point => duplicateMarkerIds.Contains(point.id ?? ""));
                if (invalidId || incompatible || duplicateMemberId)
                {
                    _invalidBotSpawnGroupKeys.Add(key);
                    var reason = invalidId
                        ? "group ID is malformed"
                        : duplicateMemberId
                            ? "one or more member marker IDs are duplicated"
                            : "members have different bot, trigger, chance, or follower configuration";
                    Plugin.Log.LogWarning($"Bot spawn group '{group.Key}' in pack #{packIndex}, map '{mapId}' is invalid ({reason}); the group will not spawn.");
                }
            }
        }

        private string GetBotSpawnGroupKey(int packIndex, string mapId, BotSpawnPoint point)
        {
            if (point == null || string.IsNullOrWhiteSpace(point.botSpawnGroupId))
                return null;
            return $"{packIndex}|{mapId}|{point.botSpawnGroupId.Trim()}";
        }

        private static bool BotSpawnPointsShareEventConfiguration(BotSpawnPoint left, BotSpawnPoint right)
        {
            return IsFinite(left.spawnChance) && left.spawnChance >= 0f && left.spawnChance <= 100f &&
                   IsFinite(left.botSpawnChance) && left.botSpawnChance >= 0f && left.botSpawnChance <= 100f &&
                   IsFinite(left.delayToCanSpawnSec) && left.delayToCanSpawnSec >= 0f &&
                   left.forcePlayerSpawn == right.forcePlayerSpawn &&
                   left.side == right.side &&
                   left.category == right.category &&
                   left.preset == right.preset &&
                   string.Equals(left.wildSpawnType ?? "", right.wildSpawnType ?? "", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.spawnMode ?? "Forced", right.spawnMode ?? "Forced", StringComparison.OrdinalIgnoreCase) &&
                   Mathf.Abs(left.spawnChance - right.spawnChance) <= 0.001f &&
                   Mathf.Abs(left.botSpawnChance - right.botSpawnChance) <= 0.001f &&
                   Mathf.Abs(left.delayToCanSpawnSec - right.delayToCanSpawnSec) <= 0.001f &&
                   left.triggerActivated == right.triggerActivated &&
                   string.Equals(left.triggerZoneName ?? "", right.triggerZoneName ?? "", StringComparison.OrdinalIgnoreCase) &&
                   left.followersEnabled == right.followersEnabled &&
                   left.minFollowers == right.minFollowers &&
                   left.maxFollowers == right.maxFollowers &&
                   (left.randomSpawnTypes ?? new List<string>()).SequenceEqual(
                       right.randomSpawnTypes ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        }

        private int CreateGroupedBotSpawnMarker(string groupKey, List<BotSpawnPoint> members, BotZone[] botZones, HashSet<string> dispatchedGroups)
        {
            if (!dispatchedGroups.Add(groupKey))
            {
                Plugin.Log.LogWarning($"Bot spawn group event '{groupKey}' was already processed this raid; duplicate dispatch skipped.");
                return 0;
            }

            var representative = members.FirstOrDefault();
            if (representative == null)
                return 0;

            var eligible = new List<EligibleBotSpawnPoint>();
            foreach (var point in members)
            {
                var position = point.position?.ToVector3() ?? Vector3.zero;
                if (point.position == null || !IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                    !IsFinite(point.radius) || point.radius <= 0f)
                {
                    Plugin.Log.LogWarning($"Bot spawn group '{groupKey}' location id={point.id} has an invalid position or radius and is ineligible.");
                    continue;
                }

                var zoneName = ResolveBotZoneName(point.botZoneName, position, botZones);
                var botZone = botZones?.FirstOrDefault(zone => zone != null && zone.name == zoneName);
                if (botZone == null)
                {
                    Plugin.Log.LogWarning($"Bot spawn group '{groupKey}' location id={point.id} has no valid BotZone and is ineligible.");
                    continue;
                }

                eligible.Add(new EligibleBotSpawnPoint { Point = point, BotZoneName = zoneName, BotZone = botZone });
            }

            if (eligible.Count == 0)
            {
                Plugin.Log.LogWarning($"Bot spawn group '{groupKey}' has no eligible locations; no spawn request was created.");
                return 0;
            }

            var selected = eligible[_rng.Next(eligible.Count)];
            var chance = representative.spawnChance;
            if (chance < 100f && _rng.NextDouble() * 100 > chance)
            {
                Plugin.Log.LogInfo($"Bot spawn group '{groupKey}' failed its single group-level Spawn Chance roll ({chance:F2}%).");
                return 0;
            }
            var resolvedType = selected.Point.randomSpawnTypes != null && selected.Point.randomSpawnTypes.Count > 0
                ? selected.Point.randomSpawnTypes[_rng.Next(selected.Point.randomSpawnTypes.Count)]
                : selected.Point.wildSpawnType;
            var marker = CreateSpawnMarker(
                selected.Point.id,
                selected.Point.position.ToVector3(),
                selected.Point.rotation != null ? selected.Point.rotation.ToVector3().y : 0f,
                selected.Point.radius,
                selected.Point.side,
                selected.Point.category,
                selected.Point.delayToCanSpawnSec,
                selected.BotZoneName,
                botZones,
                selected.Point,
                resolvedType: resolvedType,
                botSpawnGroupKey: groupKey,
                botSpawnGroupEventId: groupKey + "|raid");
            if (marker == null)
            {
                Plugin.Log.LogWarning($"Bot spawn group '{groupKey}' selected location id={selected.Point.id}, but marker creation failed; no alternate location will be submitted.");
                return 0;
            }

            Plugin.Log.LogInfo($"Bot spawn group event '{groupKey}' selected one of {eligible.Count} eligible locations: marker={selected.Point.id}, botZone='{selected.BotZoneName}', role='{resolvedType}'.");
            return 1;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void AddTriggeredSpawnData(string triggerZoneName, TriggerSpawnData data)
        {
            var key = triggerZoneName ?? "";
            if (!_triggeredSpawnData.ContainsKey(key))
                _triggeredSpawnData[key] = new List<TriggerSpawnData>();
            _triggeredSpawnData[key].Add(data);
        }

        private List<SpawnRequest> CreateZoneMarkers(BotSpawnZone zone, BotZone[] botZones, string zoneName, string idSuffix)
        {
            var requests = new List<SpawnRequest>();
            if (zone.randomGroups != null && zone.randomGroups.Count > 0)
            {
                var group = zone.randomGroups[_rng.Next(zone.randomGroups.Count)];
                for (int i = 0; i < group.spawnCount; i++)
                {
                    var pos = GetRandomPointInZone(zone);
                    var id = $"{zone.id}_{idSuffix}_group_{group.id}_spawn_{i}";
                    var marker = CreateSpawnMarker(id, pos, zone.rotation.ToVector3().y, 1f, group.side, group.category, zone.delayToCanSpawnSec, zoneName, botZones, zoneData: zone, group: group, resolvedType: group.wildSpawnType);
                    if (marker != null)
                    {
                        var request = _spawnRequests.LastOrDefault(r => r.Marker == marker);
                        if (request != null)
                            requests.Add(request);
                    }
                }
            }
            else if (zone.randomSpawnTypes != null && zone.randomSpawnTypes.Count > 0)
            {
                for (int i = 0; i < zone.spawnCount; i++)
                {
                    var type = zone.randomSpawnTypes[_rng.Next(zone.randomSpawnTypes.Count)];
                    var pos = GetRandomPointInZone(zone);
                    var id = $"{zone.id}_{idSuffix}_random_spawn_{i}";
                    var marker = CreateSpawnMarker(id, pos, zone.rotation.ToVector3().y, 1f, zone.side, zone.category, zone.delayToCanSpawnSec, zoneName, botZones, zoneData: zone, resolvedType: type);
                    if (marker != null)
                    {
                        var request = _spawnRequests.LastOrDefault(r => r.Marker == marker);
                        if (request != null)
                            requests.Add(request);
                    }
                }
            }
            else
            {
                for (int i = 0; i < zone.spawnCount; i++)
                {
                    var pos = GetRandomPointInZone(zone);
                    var id = $"{zone.id}_{idSuffix}_spawn_{i}";
                    var marker = CreateSpawnMarker(id, pos, zone.rotation.ToVector3().y, 1f, zone.side, zone.category, zone.delayToCanSpawnSec, zoneName, botZones, zoneData: zone);
                    if (marker != null)
                    {
                        var request = _spawnRequests.LastOrDefault(r => r.Marker == marker);
                        if (request != null)
                            requests.Add(request);
                    }
                }
            }
            return requests;
        }

        private SpawnPointMarker CreateSpawnMarker(string id, Vector3 position, float rotationY, float radius, BotSpawnSide side, BotSpawnCategory category, float delay, string botZoneName, BotZone[] botZones, BotSpawnPoint point = null, BotSpawnZone zoneData = null, BotSpawnGroup group = null, string resolvedType = null, string botSpawnGroupKey = null, string botSpawnGroupEventId = null)
        {
            try
            {
                var sides = ConvertSide(side);
                var categories = ConvertCategory(category);
                var @params = new SpawnPointParams
                {
                    Id = id,
                    Position = position,
                    Rotation = rotationY,
                    Sides = sides,
                    Categories = categories,
                    Infiltration = null,
                    BotZoneName = botZoneName,
                    DelayToCanSpawnSec = delay,
                    CorePointId = 0,
                    ColliderParams = new SpawnSphereParams { Center = Vector3.zero, Radius = radius }
                };

                var zone = string.IsNullOrEmpty(botZoneName) ? null : botZones?.FirstOrDefault(z => z != null && z.name == botZoneName);
                if (zone == null)
                {
                    Plugin.Log.LogWarning($"No BotZone found for marker {id} (resolved name: '{botZoneName}'). Skipping.");
                    return null;
                }
                if (zone.SpawnPointMarkers == null)
                    zone.SpawnPointMarkers = new List<SpawnPointMarker>();

                var marker = SpawnPointMarker.Create(@params, zone.transform);
                if (marker == null)
                {
                    Plugin.Log.LogError($"SpawnPointMarker.Create returned null for {id}.");
                    return null;
                }

                if (zone != null && !zone.SpawnPointMarkers.Contains(marker))
                    zone.SpawnPointMarkers.Add(marker);

                _spawnRequests.Add(new SpawnRequest
                {
                    Zone = zone,
                    Marker = marker,
                    Point = point,
                    ZoneData = zoneData,
                    Group = group,
                    ResolvedWildSpawnType = resolvedType,
                    BotSpawnGroupKey = botSpawnGroupKey,
                    BotSpawnGroupEventId = botSpawnGroupEventId
                });

                Plugin.Log.LogInfo($"Created marker {id} at {position} in zone '{botZoneName}' (category: {category}, side: {side}).");
                return marker;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to create spawn marker {id}: {ex.Message}");
                return null;
            }
        }

        private string ResolveBotZoneName(string requestedName, Vector3 position, BotZone[] botZones)
        {
            if (botZones == null || botZones.Length == 0)
                return "";
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                var zone = botZones.FirstOrDefault(z => z.name == requestedName);
                if (zone != null)
                    return zone.name;
            }

            BotZone nearest = null;
            float nearestDist = float.MaxValue;
            foreach (var zone in botZones)
            {
                if (zone == null)
                    continue;
                var dist = Vector3.Distance(position, zone.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = zone;
                }
            }
            return nearest?.name ?? "";
        }

        private Vector3 GetRandomPointInZone(BotSpawnZone zone)
        {
            var center = zone.position.ToVector3();
            var rng = new System.Random();
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
                    return center + new Vector3(
                        (float)(rng.NextDouble() - 0.5) * scale.x,
                        (float)(rng.NextDouble() - 0.5) * scale.y,
                        (float)(rng.NextDouble() - 0.5) * scale.z);
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var radius = zone.radius * (zone.scale?.x ?? 1f);
                    var height = zone.scale?.y ?? 1f;
                    var angle = rng.NextDouble() * Math.PI * 2;
                    var dist = Math.Sqrt(rng.NextDouble()) * radius;
                    return center + new Vector3(
                        (float)(Math.Cos(angle) * dist),
                        (float)((rng.NextDouble() - 0.5) * height),
                        (float)(Math.Sin(angle) * dist));
                default:
                    var sphereRadius = zone.radius * (zone.scale?.x ?? 1f);
                    var u = rng.NextDouble();
                    var v = rng.NextDouble();
                    var theta = u * Math.PI * 2;
                    var phi = Math.Acos(2 * v - 1);
                    var r = Math.Pow(rng.NextDouble(), 1.0 / 3.0) * sphereRadius;
                    return center + new Vector3(
                        (float)(r * Math.Sin(phi) * Math.Cos(theta)),
                        (float)(r * Math.Sin(phi) * Math.Sin(theta)),
                        (float)(r * Math.Cos(phi)));
            }
        }

        private EPlayerSideMask ConvertSide(BotSpawnSide side)
        {
            switch (side)
            {
                case BotSpawnSide.Bear: return EPlayerSideMask.Bear;
                case BotSpawnSide.Usec: return EPlayerSideMask.Usec;
                case BotSpawnSide.Pmc: return EPlayerSideMask.Pmc;
                case BotSpawnSide.All: return EPlayerSideMask.All;
                default: return EPlayerSideMask.Savage;
            }
        }

        private ESpawnCategoryMask ConvertCategory(BotSpawnCategory category)
        {
            switch (category)
            {
                case BotSpawnCategory.Boss: return ESpawnCategoryMask.Boss;
                case BotSpawnCategory.BotPmc: return ESpawnCategoryMask.BotPmc;
                case BotSpawnCategory.All: return ESpawnCategoryMask.Bot | ESpawnCategoryMask.Boss | ESpawnCategoryMask.BotPmc;
                default: return ESpawnCategoryMask.Bot;
            }
        }

        private DeploymentGate BuildDeploymentGate(MapData map, string mapId)
        {
            if (map?.deployments == null || map.deployments.Count == 0)
                return DeploymentGate.Evaluate(null, mapId, null, null, null);

            var quests = Singleton<GameWorld>.Instance?.MainPlayer?.Profile?.QuestsData;
            if (quests == null)
                Plugin.Log.LogWarning($"{DeploymentGate.LogPrefix} Player quest data is unavailable on map '{mapId}'; all deployments are inactive (fail closed).");
            return DeploymentGate.Evaluate(map.deployments, mapId, questId =>
            {
                var quest = quests?.FirstOrDefault(q => q.Id == questId);
                if (quest == null)
                    return DeploymentQuestState.Missing;
                return quest.Status == EQuestStatus.Success ? DeploymentQuestState.Completed : DeploymentQuestState.Incomplete;
            }, Plugin.Log.LogInfo, Plugin.Log.LogWarning);
        }

        private bool QuestConditionsMet(bool questOnly, bool questCompleted, string questId, string kind, string entryName, string entryId, string mapId)
        {
            if (!questOnly && !questCompleted)
                return true;

            var required = $"questOnly={questOnly} (active), questCompleted={questCompleted} (completed)";
            var label = $"Quest-gated {kind} '{entryName}' (id={entryId}) on map '{mapId}', questId='{questId}', required: {required}";

            if (string.IsNullOrWhiteSpace(questId))
            {
                Plugin.Log.LogWarning($"{label}: REJECTED, no questId configured.");
                return false;
            }

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null)
            {
                Plugin.Log.LogWarning($"{label}: REJECTED, MainPlayer is null at this point of initialization.");
                return false;
            }

            if (player.Profile == null)
            {
                Plugin.Log.LogWarning($"{label}: REJECTED, MainPlayer.Profile is null.");
                return false;
            }

            if (player.Profile.QuestsData == null)
            {
                Plugin.Log.LogWarning($"{label}: REJECTED, Profile.QuestsData is null.");
                return false;
            }

            var quest = player.Profile.QuestsData.FirstOrDefault(q => q.Id == questId);
            if (quest == null)
            {
                Plugin.Log.LogWarning($"{label}: REJECTED, quest not found in profile ({player.Profile.QuestsData.Count} quests present).");
                return false;
            }

            var active = quest.Status == EQuestStatus.AvailableForStart
                || quest.Status == EQuestStatus.Started
                || quest.Status == EQuestStatus.AvailableForFinish;

            var completed = quest.Status == EQuestStatus.Success;

            var met = (questOnly && active) || (questCompleted && completed);
            if (met)
                Plugin.Log.LogInfo($"{label}: ACCEPTED, actual status={quest.Status} ({(int)quest.Status}).");
            else
                Plugin.Log.LogWarning($"{label}: REJECTED, actual status={quest.Status} ({(int)quest.Status}) does not satisfy the required condition.");

            return met;
        }

        private void ForceSpawnCustomBots(BotsController controller)
        {
            if (_botsController != null && !ReferenceEquals(_botsController, controller))
                ClearActivityTracking("bot-controller-reinitialized");
            _botsController = controller;
            if (controller?.BotSpawner == null)
            {
                Plugin.Log.LogWarning("BotsController or BotSpawner is null, cannot force custom spawn.");
                return;
            }

            Plugin.Log.LogInfo($"Forcing spawn for {_spawnRequests.Count} custom bot requests.");
            foreach (var request in _spawnRequests)
            {
                UpdateMarkerCorePoint(controller, request.Marker);
            }

            var spawnModeRequests = _spawnRequests.Where(IsForcedSpawn).ToList();
            var forcedRequests = spawnModeRequests
                .Where(request => string.IsNullOrWhiteSpace(request.BotSpawnGroupEventId) && BotSpawnChancePassed(request))
                .ToList();
            foreach (var request in spawnModeRequests.Where(request => !string.IsNullOrWhiteSpace(request.BotSpawnGroupEventId)))
            {
                if (!_submittedBotSpawnGroupEvents.Add(request.BotSpawnGroupEventId))
                {
                    Plugin.Log.LogWarning($"Bot spawn group event '{request.BotSpawnGroupEventId}' was already submitted; duplicate request skipped.");
                    continue;
                }

                if (BotSpawnChancePassed(request))
                    forcedRequests.Add(request);
                else
                    Plugin.Log.LogInfo($"Bot spawn group event '{request.BotSpawnGroupEventId}' failed its single Bot Spawn Chance roll; no alternate location will be submitted.");
            }

            var zoneGroups = forcedRequests
                .Where(r => r.ZoneData != null)
                .GroupBy(r => r.ZoneData)
                .ToList();

            foreach (var group in zoneGroups)
            {
                SpawnZoneGroup(controller, group.Key, group.ToList());
            }

            foreach (var request in forcedRequests.Where(r => r.ZoneData == null))
            {
                SpawnBotAt(controller, request);
            }
        }

        private bool IsForcedSpawn(SpawnRequest request)
        {
            var mode = request.Point?.spawnMode ?? request.ZoneData?.spawnMode ?? "Forced";
            return !string.Equals(mode, "Potential", StringComparison.OrdinalIgnoreCase);
        }

        private bool BotSpawnChancePassed(SpawnRequest request)
        {
            var chance = request.Point?.botSpawnChance ?? request.ZoneData?.botSpawnChance ?? 100f;
            if (chance >= 100f)
                return true;
            return _rng.NextDouble() * 100 <= chance;
        }

        private async void SpawnZoneGroup(BotsController controller, BotSpawnZone zoneData, List<SpawnRequest> requests)
        {
            try
            {
                if (requests.Count == 0)
                    return;

                var request = requests[0];
                var wildSpawnType = ResolveWildSpawnType(request);
                if (!wildSpawnType.HasValue)
                {
                    Plugin.Log.LogWarning("Skipping zone group spawn with unknown wild spawn type.");
                    return;
                }

                if (zoneData != null && zoneData.followersEnabled)
                {
                    // Each zone spawn is its own boss group, so submit them individually.
                    foreach (var r in requests)
                        SpawnBotAt(controller, r);
                    return;
                }

                var side = ResolveSide(request);
                var difficulty = BotDifficulty.normal;
                var count = requests.Count;
                // Non-null Id_spawn: MoreBotsAPI HuntManager.OnBotCreated calls Id_spawn.ToLower().
                var spawnParams = new BotSpawnParams { Id_spawn = string.Empty };
                spawnParams.ShallBeGroup = new ShallBeGroupParams(true, true, count);

                var data = await BotCreationData.Create(new GetProfileDataParams(side, wildSpawnType.Value, difficulty, 0f, spawnParams, false), controller.BotSpawner._botCreator, count, controller.BotSpawner);
                var points = requests.Select(r => r.Marker.SpawnPoint).ToList();
                controller.BotSpawner.TryToSpawnInZoneAndDelay(request.Zone, data, true, true, points, true);
                Plugin.Log.LogInfo($"Submitted group spawn of {count} {wildSpawnType.Value} in zone {request.Zone.name} (corePoint={points[0].CorePointId}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to spawn zone group: {ex.Message}");
            }
        }

        private async void SpawnBotAt(BotsController controller, SpawnRequest request)
        {
            try
            {
                var wildSpawnType = ResolveWildSpawnType(request);
                if (!wildSpawnType.HasValue)
                {
                    Plugin.Log.LogWarning("Skipping spawn request with unknown wild spawn type.");
                    return;
                }

                var side = ResolveSide(request);
                var difficulty = BotDifficulty.normal;
                var point = request.Marker.SpawnPoint;
                Plugin.Log.LogInfo($"[MEL diag] Spawn request id={request.Point?.id ?? request.ZoneData?.id}, preset={request.Point?.preset ?? request.ZoneData?.preset}, role={wildSpawnType.Value}, followersEnabled={request.Point?.followersEnabled ?? request.ZoneData?.followersEnabled ?? false}.");
                if (NpcProviderRegistry.FindOwner(wildSpawnType.Value.ToString()) is BotGenNpcProvider)
                    Plugin.Log.LogInfo($"[MEL BotGen] Requesting exact registered roleName='{wildSpawnType.Value}', WildSpawnType={(int)wildSpawnType.Value}, spawnEntryId={request.Point?.id ?? request.ZoneData?.id}; no base-archetype substitution.");

                var followerPlan = GetFollowerPlan(request, wildSpawnType.Value);
                if (followerPlan != null)
                {
                    SpawnBossWithFollowers(controller, request, wildSpawnType.Value, followerPlan, side, difficulty, point);
                    return;
                }

                var data = await BotCreationData.Create(new GetProfileDataParams(side, wildSpawnType.Value, difficulty, 0f, new BotSpawnParams { Id_spawn = string.Empty }, false), controller.BotSpawner._botCreator, 1, controller.BotSpawner);
                if (!IsGroupedSpawnLocationValid(request))
                {
                    Plugin.Log.LogWarning($"Bot spawn group event '{request.BotSpawnGroupEventId}' became invalid before dispatch; no alternate location will be submitted.");
                    return;
                }
                if (data?.Profiles != null && data.Profiles.Count == 1)
                    QueueActivityBot(request, data.Profiles[0].Id, GetActivitySpawnPath(request));
                controller.BotSpawner.TryToSpawnInZoneAndDelay(request.Zone, data, true, true, new List<ISpawnPoint> { point }, true);
                Plugin.Log.LogInfo($"Submitted forced spawn at {point.Position} ({wildSpawnType.Value}, {side}, zone={request.Zone.name}, corePoint={point.CorePointId}).");
                if (data?.Profiles != null && data.Profiles.Count > 0)
                    TrackMelBotMovement(request.Point?.id ?? request.ZoneData?.id, data.Profiles[0].Id, point.Position);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to force spawn bot: {ex.Message}");
            }
        }

        private sealed class FollowerGroup
        {
            public WildSpawnType Role;
            public int Count;
        }

        // Roles SPT spawns as the boss's "Supports" (Goons: BigPipe + BirdEye; the primary exUsec escort is never spawned and is not in Knight's group).
        // Both roles are in Knight's WildSpawnTypeExtension suitability group, so they join the boss's group.
        private static FollowerGroup[] GetVanillaSupports(WildSpawnType boss)
        {
            switch (boss)
            {
                case WildSpawnType.bossKnight:
                    return new[] { new FollowerGroup { Role = WildSpawnType.followerBigPipe, Count = 1 }, new FollowerGroup { Role = WildSpawnType.followerBirdEye, Count = 1 } };
                default:
                    return null;
            }
        }

        // Single-role escort bosses: the configured/rolled count applies to this one role.
        private static WildSpawnType? MapFollowerType(WildSpawnType boss)
        {
            switch (boss)
            {
                case WildSpawnType.bossBully: return WildSpawnType.followerBully;
                case WildSpawnType.bossSanitar: return WildSpawnType.followerSanitar;
                case WildSpawnType.bossKojaniy: return WildSpawnType.followerKojaniy;
                case WildSpawnType.bossZryachiy: return WildSpawnType.followerZryachiy;
                // Vanilla BossEscortType only; their mixed extra supports (Assault/Scout/Close1/Close2...) are a planned later feature.
                case WildSpawnType.bossBoar: return WildSpawnType.followerBoar;
                case WildSpawnType.bossGluhar: return WildSpawnType.followerGluharSecurity;
                case WildSpawnType.bossKolontay: return WildSpawnType.followerKolontaySecurity;
                default: return null;
            }
        }

        // Returns the follower groups to spawn when followers are enabled and meaningful for this boss; otherwise null (legacy solo path).
        // Single-role bosses use the rolled min..max count; Supports bosses use the fixed vanilla support amounts (min..max only gates on/off, 0 = none).
        private List<FollowerGroup> GetFollowerPlan(SpawnRequest request, WildSpawnType bossType)
        {
            var enabled = request.Point?.followersEnabled ?? request.ZoneData?.followersEnabled ?? false;
            if (!enabled)
                return null;

            var min = request.Point?.minFollowers ?? request.ZoneData?.minFollowers ?? 0;
            var max = request.Point?.maxFollowers ?? request.ZoneData?.maxFollowers ?? 0;
            if (min < 0 || max < min)
            {
                Plugin.Log.LogWarning($"Invalid follower range {min}-{max} on {request.Marker?.name}; sanitizing.");
                min = Math.Max(0, min);
                max = Math.Max(min, max);
            }

            var bossId = request.ResolvedWildSpawnType;
            if (string.IsNullOrWhiteSpace(bossId))
                bossId = request.Group?.wildSpawnType ?? request.Point?.wildSpawnType ?? request.ZoneData?.wildSpawnType;
            var botGen = NpcProviderRegistry.FindOwner(bossId) as BotGenNpcProvider;
            if (botGen != null)
            {
                var count = _rng.Next(min, max + 1);
                return count <= 0 ? null : GetBotGenFollowerPlan(botGen, bossId, count);
            }

            var supports = GetVanillaSupports(bossType);
            var followerType = MapFollowerType(bossType);
            if (supports == null && !followerType.HasValue)
            {
                Plugin.Log.LogWarning($"Followers requested for {bossType}, but SPT defines no followers or supports for it; spawning without followers.");
                return null;
            }

            var followerCount = _rng.Next(min, max + 1);
            if (followerCount <= 0)
                return null;
            if (supports != null)
            {
                // Rolled count is spread round-robin over the boss's support roles (Knight: BigPipe, BirdEye, BigPipe, ...), so min..max is honored.
                var plan = supports.Select(s => new FollowerGroup { Role = s.Role, Count = 0 }).ToList();
                for (var i = 0; i < followerCount; i++)
                    plan[i % plan.Count].Count++;
                plan = plan.Where(g => g.Count > 0).ToList();
                Plugin.Log.LogInfo($"[MEL followers] {bossType} rolled {followerCount} (range {min}-{max}) spread over supports: {string.Join(", ", plan.Select(s => s.Count + "x" + s.Role).ToArray())}.");
                return plan;
            }
            return new List<FollowerGroup> { new FollowerGroup { Role = followerType.Value, Count = followerCount } };
        }

        private async void SpawnBossWithFollowers(BotsController controller, SpawnRequest request, WildSpawnType bossType, List<FollowerGroup> followerPlan, EPlayerSide side, BotDifficulty difficulty, ISpawnPoint point)
        {
            var followerCount = followerPlan.Sum(g => g.Count);
            var planText = string.Join(", ", followerPlan.Select(g => g.Count + "x" + g.Role).ToArray());
            const int BossWaitSeconds = 900;
            var spawner = controller.BotSpawner;
            Action<BotOwner> handler = null;
            var id = request.Point?.id ?? request.ZoneData?.id;
            var startedAt = DateTime.Now;
            try
            {
                Plugin.Log.LogInfo($"[MEL followers] Start id={id}, boss={bossType}, followerPlan=[{planText}], totalFollowers={followerCount}, t={startedAt:HH:mm:ss}.");

                // Same group setup as SPT's BotBossSpawn.SpawnBossAndFollowers: one shared params object, boss + followers.
                var spawnParams = new BotSpawnParams();
                // Vanilla waves set Id_spawn to wave.TriggerId (a string, "" when untriggered). Leaving it null makes OnBotCreated subscribers that
                // call SpawnParams.Id_spawn.ToLower() (MoreBotsAPI HuntManager) throw, which aborts SPT's sequential follower creation after bot #1.
                spawnParams.Id_spawn = string.Empty;
                spawnParams.ShallBeGroup = new ShallBeGroupParams(true, true, followerCount + 1);

                var bossData = await BotCreationData.Create(new GetProfileDataParams(side, bossType, difficulty, 0f, spawnParams, false), spawner._botCreator, 1, spawner);
                if (bossData == null || bossData.Profiles == null || bossData.Profiles.Count == 0)
                {
                    Plugin.Log.LogError($"[MEL followers] Boss profile generation returned {bossData?.Profiles?.Count ?? -1} profiles for {bossType}; no boss or followers spawned.");
                    return;
                }
                if (!IsGroupedSpawnLocationValid(request))
                {
                    Plugin.Log.LogWarning($"Bot spawn group event '{request.BotSpawnGroupEventId}' became invalid before boss dispatch; no alternate location or followers will be submitted.");
                    return;
                }

                var bossProfileId = bossData.Profiles[0].Id;
                QueueActivityBot(request, bossProfileId, GetActivitySpawnPath(request) + "-boss");
                Plugin.Log.LogInfo($"[MEL followers] Boss profile generated: profileId={bossProfileId}, groupStart={spawnParams.ShallBeGroup.StartCount}.");
                var bossCreated = new TaskCompletionSource<BotOwner>();
                handler = owner =>
                {
                    var role = owner?.Profile?.Info?.Settings?.Role;
                    var matched = owner?.Profile != null && owner.Profile.Id == bossProfileId;
                    Plugin.Log.LogInfo($"[MEL followers] OnBotCreated: role={role}, profileId={owner?.Profile?.Id}, matchesBoss={matched}, t={DateTime.Now:HH:mm:ss}.{DescribeBotOwner(owner, point.Position)}");
                    if (matched)
                        bossCreated.TrySetResult(owner);
                };
                spawner.OnBotCreated += handler;

                Plugin.Log.LogInfo($"[MEL followers] Spawner state BEFORE boss submit: {DescribeSpawnerState(spawner, bossData, request.Zone)}");
                spawner.TryToSpawnInZoneAndDelay(request.Zone, bossData, true, true, new List<ISpawnPoint> { point }, true);
                Plugin.Log.LogInfo($"[MEL followers] Boss spawn request submitted (request only, not yet created): {bossType} profileId={bossProfileId} at {point.Position}, zone={request.Zone.name}, corePoint={point.CorePointId}.");
                // A rising WaitCount means BotSpawner queued the request instead of activating it; a rising inSpawnProcess means activation started.
                Plugin.Log.LogInfo($"[MEL followers] Spawner state AFTER boss submit: {DescribeSpawnerState(spawner, bossData, request.Zone)}");

                // Followers are submitted only after the boss exists so the boss owns the group and calls SetBoss.
                // The spawner may hold the request until the raid actually starts, so wait generously and log progress.
                var waited = 0;
                while (!bossCreated.Task.IsCompleted && waited < BossWaitSeconds)
                {
                    await Task.WhenAny(bossCreated.Task, Task.Delay(TimeSpan.FromSeconds(1)));
                    waited += 1;

                    // OnBotCreated was observed not firing for MEL's boss even though he spawned, so also look the exact profile id up in the live world.
                    if (!bossCreated.Task.IsCompleted)
                    {
                        var live = FindLiveBotByProfileId(bossProfileId);
                        if (live != null)
                        {
                            Plugin.Log.LogInfo($"[MEL followers] Boss found via live world lookup (OnBotCreated did not report it): role={live.Profile?.Info?.Settings?.Role}, profileId={live.Profile?.Id}, matchesBoss=True, t={DateTime.Now:HH:mm:ss}.{DescribeBotOwner(live, point.Position)}");
                            bossCreated.TrySetResult(live);
                        }
                    }

                    if (!bossCreated.Task.IsCompleted && waited % 30 == 0)
                    {
                        Plugin.Log.LogInfo($"[MEL followers] Still waiting for boss creation ({waited}s since submit, raidStarted={(Time.time - _raidStartTime)}s since raid-start marker). {DescribeSpawnerState(spawner, bossData, request.Zone)} liveBossFollowerBots=[{DescribeLiveBossBots(point.Position)}]");
                    }
                }
                if (!bossCreated.Task.IsCompleted)
                {
                    Plugin.Log.LogWarning($"[MEL followers] Boss {bossType} (profileId={bossProfileId}) was not created within {BossWaitSeconds}s; followers were not spawned.");
                    return;
                }
                var boss = bossCreated.Task.Result;
                Plugin.Log.LogInfo($"[MEL followers] Boss obtained: profileId={boss.Profile.Id}, role={boss.Profile.Info.Settings.Role}, state={boss.BotState}, group={(boss.BotsGroup != null ? boss.BotsGroup.Id.ToString() : "null")}, groupMembers={boss.BotsGroup?.MembersCount ?? -1}, isBoss={boss.Boss?.IamBoss}, shallBeGroupRemain={spawnParams.ShallBeGroup.RemainCount}, bossSetted={spawnParams.ShallBeGroup.IsBossSetted}.");
                TrackMelBotMovement(id, boss.Profile.Id, point.Position);

                // Count followers as they are created, by role, for the final check.
                var created = 0;
                var planRoles = new HashSet<WildSpawnType>(followerPlan.Select(g => g.Role));
                Action<BotOwner> followerHandler = o =>
                {
                    var r = o?.Profile?.Info?.Settings?.Role;
                    if (r.HasValue && planRoles.Contains(r.Value))
                    {
                        created++;
                        Plugin.Log.LogInfo($"[MEL followers] Follower created #{created}: role={r.Value}, profileId={o.Profile.Id}, group={(o.BotsGroup != null ? o.BotsGroup.Id.ToString() : "null")}, sameGroupAsBoss={ReferenceEquals(o.BotsGroup, boss.BotsGroup)}.");
                    }
                };
                spawner.OnBotCreated += followerHandler;

                // One BotCreationData per role, all sharing the boss's spawnParams/ShallBeGroup, like SPT's SpawnEscort.
                var requestedIds = new List<string>();
                var actual = 0;
                foreach (var group in followerPlan)
                {
                    Plugin.Log.LogInfo($"[MEL followers] Follower profile generation started: role={group.Role}, requested={group.Count}.");
                    var followerData = await BotCreationData.Create(new GetProfileDataParams(EPlayerSide.Savage, group.Role, difficulty, 0f, spawnParams, false), spawner._botCreator, group.Count, spawner);
                    var groupActual = followerData?.Profiles?.Count ?? 0;
                    Plugin.Log.LogInfo($"[MEL followers] Follower profile generation completed: role={group.Role}, requested={group.Count}, returned={groupActual}.");
                    if (groupActual == 0)
                    {
                        Plugin.Log.LogWarning($"[MEL followers] Follower profile generation returned 0 of {group.Count} {group.Role}; this follower group is missing.");
                        continue;
                    }
                    if (groupActual < group.Count)
                        Plugin.Log.LogWarning($"[MEL followers] Only {groupActual} of {group.Count} {group.Role} profiles were generated.");

                    var points = Enumerable.Repeat(point, groupActual).ToList();
                    FollowerCreationDiagnostics.Track(followerData.Profiles.Select(pr => pr.Id), "followers");
                    foreach (var profile in followerData.Profiles)
                        QueueActivityBot(request, profile.Id, GetActivitySpawnPath(request) + "-follower");
                    Plugin.Log.LogInfo($"[MEL followers] Pre-submit: role={group.Role}, followerProfiles={followerData.Profiles.Count}, positionsPassed={points.Count}, dataPositions={followerData.CountPositions}, spawnStopped={followerData.SpawnStopped}, inSpawnProcess={spawner._inSpawnProcess}, allBots={spawner.AllBotsWithLoaded}, delayedWaitCount={spawner.SpawnDelaysService.WaitCount}.");
                    spawner.TryToSpawnInZoneAndDelay(request.Zone, followerData, false, true, points, true);
                    Plugin.Log.LogInfo($"[MEL followers] Post-submit (TryToSpawnInZoneAndDelay returns void): role={group.Role}, dataPositions={followerData.CountPositions}, followerProfiles={followerData.Profiles.Count}, inSpawnProcess={spawner._inSpawnProcess}, delayedWaitCount={spawner.SpawnDelaysService.WaitCount}.");
                    Plugin.Log.LogInfo($"[MEL followers] Follower spawn request submitted (request only): {groupActual} x {group.Role}, shallBeGroupRemain={spawnParams.ShallBeGroup.RemainCount}, bossGroup={(boss.BotsGroup != null ? boss.BotsGroup.Id.ToString() : "null")}.");
                    actual += groupActual;
                    requestedIds.AddRange(followerData.Profiles.Select(pr => pr.Id));
                    await Task.Yield();
                }
                if (requestedIds.Count == 0)
                {
                    spawner.OnBotCreated -= followerHandler;
                    Plugin.Log.LogWarning($"[MEL followers] No follower profiles were generated for {bossType}; boss spawned alone.");
                    return;
                }
                Plugin.Log.LogInfo($"[MEL followers] Requested follower profile ids (bossProfileId={boss.Profile.Id}, bossGroup={boss.BotsGroup?.Id}): {string.Join(", ", requestedIds)}");
                ReconcileFollowers(id, boss, requestedIds, point.Position, spawner);

                await Task.Delay(TimeSpan.FromSeconds(30));
                spawner.OnBotCreated -= followerHandler;
                var bossInfo = boss.Boss;
                Plugin.Log.LogInfo($"[MEL followers] Final check for {bossType}: followersCreated={created}, bossFollowers={bossInfo?.Followers?.Count ?? -1}, target={bossInfo?.TargetFollowersCount ?? -1}, groupMembers={boss.BotsGroup?.MembersCount ?? -1}, requested={followerCount}, submitted={actual}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MEL followers] Exception: {ex}");
            }
            finally
            {
                if (handler != null && spawner != null)
                    spawner.OnBotCreated -= handler;
            }
        }
        // Read-only diagnostics: reconciles every requested follower profile id against the live world (alive, role, position, group, boss link)
        // so the log shows which stage lost a follower: never spawned, spawned in another group, or only missing from MEL's tracking.
        private async void ReconcileFollowers(string entryId, BotOwner boss, List<string> requestedIds, Vector3 melPoint, BotSpawner spawner)
        {
            var checkpoints = new[] { 2, 5, 10, 20, 40, 60 };
            var elapsed = 0;
            try
            {
                foreach (var at in checkpoints)
                {
                    await Task.Delay(TimeSpan.FromSeconds(at - elapsed));
                    elapsed = at;
                    var found = 0;
                    var inBossGroup = 0;
                    foreach (var pid in requestedIds)
                    {
                        var bot = FindLiveBotByProfileId(pid);
                        if (bot == null)
                        {
                            Plugin.Log.LogInfo($"[MEL followers] Reconcile t+{at}s id={entryId}: follower {pid} NOT in live world.");
                            continue;
                        }
                        found++;
                        var same = boss.BotsGroup != null && ReferenceEquals(bot.BotsGroup, boss.BotsGroup);
                        if (same)
                            inBossGroup++;
                        Plugin.Log.LogInfo($"[MEL followers] Reconcile t+{at}s id={entryId}: follower {pid} role={bot.Profile?.Info?.Settings?.Role} alive={bot.GetPlayer?.HealthController?.IsAlive} sameGroupAsBoss={same}.{DescribeBotOwner(bot, melPoint)}");
                    }
                    var bossInfo = boss.Boss;
                    Plugin.Log.LogInfo($"[MEL followers] Reconcile t+{at}s id={entryId}: requested={requestedIds.Count}, foundLive={found}, inBossGroup={inBossGroup}, bossGroup={boss.BotsGroup?.Id}, bossGroupMembers={boss.BotsGroup?.MembersCount ?? -1}, bossIamBoss={bossInfo?.IamBoss}, bossFollowers={bossInfo?.Followers?.Count ?? -1}/{bossInfo?.TargetFollowersCount ?? -1}, nearbyBossFollowerBots=[{DescribeLiveBossBots(melPoint)}], spawner: inSpawnProcess={spawner._inSpawnProcess}, allBots={spawner.AllBotsWithLoaded}, delayedWaitCount={spawner.SpawnDelaysService.WaitCount}");
                    if (found == requestedIds.Count && at >= 10)
                        break;
                }

                // Final verdict by exact profile id: never report success unless every requested follower is live, alive and in the boss's own group.
                var live = new List<string>();
                var missing = new List<string>();
                var wrongGroup = new List<string>();
                foreach (var pid in requestedIds)
                {
                    var bot = FindLiveBotByProfileId(pid);
                    if (bot == null || bot.GetPlayer?.HealthController?.IsAlive != true)
                    {
                        missing.Add(pid);
                        continue;
                    }
                    live.Add(pid);
                    if (boss.BotsGroup == null || !ReferenceEquals(bot.BotsGroup, boss.BotsGroup))
                        wrongGroup.Add(pid);
                }
                // Group membership by role alone could pull in foreign bots, so list any member of the boss group that is not the boss or a requested follower.
                var foreign = new List<string>();
                var grp = boss.BotsGroup;
                if (grp != null)
                {
                    for (var i = 0; i < grp.MembersCount; i++)
                    {
                        var m = grp.Member(i);
                        var mid = m?.Profile?.Id;
                        if (mid != null && mid != boss.Profile.Id && !requestedIds.Contains(mid))
                            foreign.Add($"{m.Profile.Info?.Settings?.Role}:{mid}");
                    }
                }
                var summary = $"[MEL followers] FINAL RECONCILE id={entryId}: boss={boss.Profile.Id} requested={requestedIds.Count}, live={live.Count}, missing=[{string.Join(", ", missing)}], notInBossGroup=[{string.Join(", ", wrongGroup)}], foreignGroupMembers=[{string.Join(", ", foreign)}].";
                if (missing.Count == 0 && wrongGroup.Count == 0 && foreign.Count == 0)
                    Plugin.Log.LogInfo(summary + " RESULT=OK");
                else
                    Plugin.Log.LogWarning(summary + " RESULT=PARTIAL_OR_FAILED");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MEL followers] Reconcile stopped by {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Read-only diagnostics for MEL-spawned bots: finds the bot by its exact profile id, then samples it and its group members
        // so the log shows where they are, where they are heading and which AI system is driving them. Never moves or alters a bot.
        private async void TrackMelBotMovement(string entryId, string leaderProfileId, Vector3 melPoint)
        {
            const int SampleSeconds = 10;
            const int TotalSeconds = 300;
            try
            {
                BotOwner leader = null;
                for (var i = 0; i < 600 && leader == null; i++)
                {
                    leader = FindLiveBotByProfileId(leaderProfileId);
                    if (leader == null)
                        await Task.Delay(TimeSpan.FromSeconds(1));
                }
                if (leader == null)
                {
                    Plugin.Log.LogWarning($"[MEL movement] id={entryId}: leader {leaderProfileId} never found; not tracking.");
                    return;
                }

                Plugin.Log.LogInfo($"[MEL movement] Tracking id={entryId}: MEL point=({melPoint.x:F1},{melPoint.y:F1},{melPoint.z:F1}), leader={leaderProfileId}.");
                for (var t = 0; t <= TotalSeconds; t += SampleSeconds)
                {
                    var world = Singleton<GameWorld>.Instance;
                    if (world == null || leader.GetPlayer == null || !leader.GetPlayer.HealthController.IsAlive)
                    {
                        Plugin.Log.LogInfo($"[MEL movement] id={entryId}: leader gone or dead at t={t}s; stopping.");
                        return;
                    }

                    Plugin.Log.LogInfo($"[MEL movement] id={entryId} t={t}s LEADER {DescribeMovement(leader, melPoint)}");
                    foreach (var p in world.AllAlivePlayersList)
                    {
                        var member = p?.AIData != null && p.AIData.IsAI ? p.AIData.BotOwner : null;
                        if (member == null || ReferenceEquals(member, leader) || member.BotsGroup == null || !ReferenceEquals(member.BotsGroup, leader.BotsGroup))
                            continue;
                        Plugin.Log.LogInfo($"[MEL movement] id={entryId} t={t}s MEMBER {DescribeMovement(member, melPoint)}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(SampleSeconds));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MEL movement] id={entryId}: tracking stopped by {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string DescribeMovement(BotOwner bot, Vector3 melPoint)
        {
            try
            {
                var pos = bot.Position;
                var dest = bot.Mover?.TargetPoint;
                var patrol = bot.PatrollingData;
                string layer;
                try { layer = bot.Brain?.ActiveLayerName(); } catch { layer = "n/a"; }
                return $"role={bot.Profile?.Info?.Settings?.Role} id={bot.Profile?.Id} pos=({pos.x:F0},{pos.z:F0}) dist={Vector3.Distance(pos, melPoint):F0}m " +
                       $"dest={(dest.HasValue ? $"({dest.Value.x:F0},{dest.Value.z:F0}) destDist={Vector3.Distance(dest.Value, melPoint):F0}m" : "none")} " +
                       $"layer={layer} brain={bot.Brain?.GetStateName} patrolActive={patrol?.IsActivated} patrolStatus={patrol?.Status} way={patrol?.Way?.name} " +
                       $"group={bot.BotsGroup?.Id} iamBoss={bot.Boss?.IamBoss} spawnZone={bot.SpawnBotZone?.name} groupZone={bot.BotsGroup?.BotZone?.name}";
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name}: {ex.Message})";
            }
        }

        private static BotOwner FindLiveBotByProfileId(string profileId)
        {
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return null;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p?.Profile != null && p.Profile.Id == profileId && p.AIData != null && p.AIData.IsAI)
                    return p.AIData.BotOwner;
            }
            return null;
        }

        private static string DescribeLiveBossBots(Vector3 melPoint)
        {
            try
            {
                var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
                if (players == null)
                    return "no world";
                var parts = new List<string>();
                foreach (var p in players)
                {
                    var role = p?.Profile?.Info?.Settings?.Role;
                    if (p == null || p.AIData == null || !p.AIData.IsAI || role == null)
                        continue;
                    if (role.Value.ToString().StartsWith("boss", StringComparison.OrdinalIgnoreCase) || role.Value.ToString().StartsWith("follower", StringComparison.OrdinalIgnoreCase))
                        parts.Add($"{role}:{p.Profile.Id}@{Vector3.Distance(p.Position, melPoint):F0}m");
                }
                return string.Join(", ", parts);
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name})";
            }
        }

        private static string DescribeSpawnerState(BotSpawner spawner, BotCreationData data, BotZone zone)
        {
            try
            {
                return $"startProfilesLoaded={spawner._botCreator.StartProfilesLoaded}, delayedWaitCount={spawner.SpawnDelaysService.WaitCount}, inSpawnProcess={spawner._inSpawnProcess}, allBots={spawner.AllBotsWithLoaded}, spawnStopped={data.SpawnStopped}, zoneFreeSpace(1)={zone.HaveFreeSpace(1)}.";
            }
            catch (Exception ex)
            {
                return $"state unavailable ({ex.GetType().Name}: {ex.Message}).";
            }
        }

        private static string DescribeBotOwner(BotOwner owner, Vector3 melPoint)
        {
            try
            {
                if (owner == null)
                    return " owner=null";
                var pos = owner.Position;
                var group = owner.BotsGroup;
                return $" ownerId={owner.GetInstanceID()}, pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}), distToMelPoint={Vector3.Distance(pos, melPoint):F1}m, state={owner.BotState}, group={(group != null ? group.Id.ToString() : "null")}, groupMembers={group?.MembersCount ?? -1}, iamBoss={owner.Boss?.IamBoss}";
            }
            catch (Exception ex)
            {
                return $" ownerInfo unavailable ({ex.GetType().Name}: {ex.Message})";
            }
        }

        private int GetCorePointId(BotsController controller, Vector3 position)
        {
            try
            {
                var coversData = controller.BotSpawner?.BotGame?.BotsController?.CoversData;
                if (coversData == null)
                    return 0;
                var closest = coversData.GetClosest(position);
                return closest?.CorePointInGame?.Id ?? 0;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to get core point: {ex.Message}");
                return 0;
            }
        }

        private void UpdateMarkerCorePoint(BotsController controller, SpawnPointMarker marker)
        {
            try
            {
                var corePointId = GetCorePointId(controller, marker.SpawnPoint.Position);
                if (corePointId == 0)
                {
                    Plugin.Log.LogWarning($"Could not resolve core point for {marker.SpawnPoint.Id}, using 0.");
                    return;
                }

                var spawnPointField = typeof(SpawnPointMarker).GetField("_spawnPoint", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawnPointField == null)
                {
                    Plugin.Log.LogWarning("Could not find _spawnPoint field on SpawnPointMarker.");
                    return;
                }

                var spawnPoint = spawnPointField.GetValue(marker);
                if (spawnPoint == null)
                {
                    Plugin.Log.LogWarning("_spawnPoint is null on marker.");
                    return;
                }

                var spawnPointType = spawnPoint.GetType();
                // Mutate in place so BotZone's cached ISpawnPoint[] sees the same object.
                var corePointField = spawnPointType.GetField("CorePointId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (corePointField != null)
                {
                    corePointField.SetValue(spawnPoint, corePointId);
                }
                else
                {
                    var corePointProp = spawnPointType.GetProperty("CorePointId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (corePointProp == null || !corePointProp.CanWrite)
                    {
                        Plugin.Log.LogWarning("Could not find a writable CorePointId on SpawnPoint.");
                        return;
                    }
                    corePointProp.SetValue(spawnPoint, corePointId);
                }
                Plugin.Log.LogInfo($"Updated core point for {marker.SpawnPoint.Id} to {corePointId}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to update marker core point: {ex.Message}");
            }
        }

        private WildSpawnType? ResolveWildSpawnType(SpawnRequest request)
        {
            var preset = request.Group?.preset ?? request.Point?.preset ?? request.ZoneData?.preset ?? BotSpawnPreset.Scav;
            var wildSpawnType = request.ResolvedWildSpawnType;
            if (string.IsNullOrWhiteSpace(wildSpawnType))
                wildSpawnType = request.Group?.wildSpawnType ?? request.Point?.wildSpawnType ?? request.ZoneData?.wildSpawnType;
            if (preset == BotSpawnPreset.BotGen &&
                (string.IsNullOrWhiteSpace(wildSpawnType) || !wildSpawnType.StartsWith("botgen_", StringComparison.OrdinalIgnoreCase)))
            {
                var id = request.Group?.id ?? request.Point?.id ?? request.ZoneData?.id;
                Plugin.Log.LogWarning($"[MEL BotGen] BotGen is selected for spawn entry '{id}', but no BotGen roleName is selected; refusing to spawn a vanilla fallback.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(wildSpawnType))
            {
                wildSpawnType = BotSpawnPresetMapping.GetWildSpawnType(preset);
                if (preset == BotSpawnPreset.Boss && string.IsNullOrWhiteSpace(wildSpawnType))
                {
                    var id = request.Point?.id ?? request.ZoneData?.id ?? request.Group?.id;
                    Plugin.Log.LogWarning($"Boss preset has no explicit wildSpawnType (entry id={id}); refusing to fall back to 'assault'. Set wildSpawnType (e.g. 'bossBully') or use a specific boss preset. Skipping spawn.");
                    Plugin.Log.LogWarning($"[MEL diag] Missing role in loaded pack data: id={id}, preset={preset} (generic Boss has no role mapping), point.wildSpawnType='{request.Point?.wildSpawnType}', zone.wildSpawnType='{request.ZoneData?.wildSpawnType}', followersEnabled={request.Point?.followersEnabled ?? request.ZoneData?.followersEnabled ?? false}. Pick the specific boss preset (e.g. Reshala) or fill Wild Type in the editor.");
                    return null;
                }
                Plugin.Log.LogInfo($"wildSpawnType empty, inferred '{wildSpawnType}' from preset {preset}.");
            }
            if (string.IsNullOrWhiteSpace(wildSpawnType))
                return WildSpawnType.assault;
            var provider = NpcProviderRegistry.FindOwner(wildSpawnType) as BotGenNpcProvider;
            if (provider != null)
            {
                string reason;
                if (!provider.IsAvailable(wildSpawnType, out reason))
                {
                    provider.Warn("spawn:" + wildSpawnType, $"Selected type '{wildSpawnType}' is unavailable: {reason}. Skipping spawn (no vanilla substitution).");
                    return null;
                }
                if (Enum.TryParse<WildSpawnType>(wildSpawnType, true, out var custom))
                {
                    provider.Warn("ok:" + wildSpawnType, $"Resolved '{wildSpawnType}' to WildSpawnType {(int)custom}.");
                    return custom;
                }
                provider.Warn("parse:" + wildSpawnType, $"'{wildSpawnType}' passed availability but could not be parsed as WildSpawnType. Skipping spawn.");
                return null;
            }
            if (Enum.TryParse<WildSpawnType>(wildSpawnType, true, out var result))
                return result;
            Plugin.Log.LogWarning($"Unknown WildSpawnType: {wildSpawnType}");
            return null;
        }

        // BotGen bosses: followers come from the NPC's own follower list in BotGen's config; each follower is spawned by its own custom role.
        private List<FollowerGroup> GetBotGenFollowerPlan(BotGenNpcProvider provider, string bossId, int followerCount)
        {
            var boss = provider.Find(bossId);
            if (boss == null || boss.FollowerIds.Count == 0)
            {
                provider.Warn("nofollow:" + bossId, $"Followers requested for '{bossId}' but BotGen lists none for it; spawning without followers.");
                return null;
            }
            var roles = new List<WildSpawnType>();
            foreach (var fid in boss.FollowerIds)
            {
                string reason;
                WildSpawnType parsed;
                if (!provider.IsAvailable(fid, out reason) || !Enum.TryParse<WildSpawnType>(fid, true, out parsed))
                {
                    provider.Warn("follower:" + fid, $"Follower '{fid}' of '{bossId}' is unavailable: {reason ?? "parse failed"}. Skipping this follower type.");
                    continue;
                }
                roles.Add(parsed);
            }
            if (roles.Count == 0)
                return null;
            var plan = roles.Select(r => new FollowerGroup { Role = r, Count = 0 }).ToList();
            for (var i = 0; i < followerCount; i++)
                plan[i % plan.Count].Count++;
            return plan.Where(g => g.Count > 0).ToList();
        }

        private EPlayerSide ResolveSide(SpawnRequest request)
        {
            var wildSpawnType = request.ResolvedWildSpawnType;
            if (string.IsNullOrWhiteSpace(wildSpawnType))
                wildSpawnType = request.Group?.wildSpawnType ?? request.Point?.wildSpawnType ?? request.ZoneData?.wildSpawnType;
            if (string.IsNullOrWhiteSpace(wildSpawnType))
            {
                var preset = request.Group?.preset ?? request.Point?.preset ?? request.ZoneData?.preset ?? BotSpawnPreset.Scav;
                wildSpawnType = BotSpawnPresetMapping.GetWildSpawnType(preset);
            }
            if ("pmcBEAR".Equals(wildSpawnType, StringComparison.OrdinalIgnoreCase))
                return EPlayerSide.Bear;
            if ("pmcUSEC".Equals(wildSpawnType, StringComparison.OrdinalIgnoreCase))
                return EPlayerSide.Usec;
            var side = request.Group?.side ?? request.Point?.side ?? request.ZoneData?.side ?? BotSpawnSide.Savage;
            if (side == BotSpawnSide.Bear)
                return EPlayerSide.Bear;
            if (side == BotSpawnSide.Usec)
                return EPlayerSide.Usec;
            return EPlayerSide.Savage;
        }

        private void IndexActivityZones(List<BotActivityZone> zones)
        {
            if (zones == null)
                return;

            foreach (var zone in zones)
            {
                if (zone == null || string.IsNullOrWhiteSpace(zone.zoneId))
                    continue;

                if (_ambiguousActivityZoneIds.Contains(zone.zoneId))
                    continue;
                if (_activityZonesById.ContainsKey(zone.zoneId))
                {
                    _activityZonesById.Remove(zone.zoneId);
                    _ambiguousActivityZoneIds.Add(zone.zoneId);
                    ActivityLogWarn($"[MEL activity-zone] Invalid zone reference: duplicate zoneId='{zone.zoneId}' across loaded packs; assignments to this ID will not be tracked.");
                    continue;
                }

                _activityZonesById.Add(zone.zoneId, zone);
            }
        }

        private static bool TryValidateActivityZone(BotActivityZone zone, out string reason)
        {
            reason = null;
            if (zone == null)
            {
                reason = "missing-zone";
                return false;
            }
            if (!zone.enabled)
            {
                reason = "zone-disabled";
                return false;
            }
            if (zone.position == null || !IsFinite(zone.position.x) || !IsFinite(zone.position.y) || !IsFinite(zone.position.z))
            {
                reason = "invalid-zone-center";
                return false;
            }

            if (zone.shape == BotActivityZoneShape.Circle)
            {
                if (!IsFinite(zone.radius) || zone.radius < BotActivityZone.MinSize)
                {
                    reason = "invalid-circle-radius";
                    return false;
                }
            }
            else if (zone.shape == BotActivityZoneShape.Square)
            {
                if (zone.rotation == null || !IsFinite(zone.rotation.y))
                {
                    reason = "invalid-square-yaw";
                    return false;
                }
                if (!IsFinite(zone.EffectiveWidth) || zone.EffectiveWidth < BotActivityZone.MinSize ||
                    !IsFinite(zone.EffectiveDepth) || zone.EffectiveDepth < BotActivityZone.MinSize)
                {
                    reason = "invalid-square-width-or-depth";
                    return false;
                }
            }
            else
            {
                reason = "invalid-zone-shape";
                return false;
            }

            return true;
        }

        private bool TryGetAssignedActivityZone(BotSpawnPoint point, out BotActivityZone zone, out string reason)
        {
            zone = null;
            reason = null;
            if (point == null || string.IsNullOrWhiteSpace(point.botActivityZoneId))
                return false;

            return TryGetActivityZoneById(point.botActivityZoneId, out zone, out reason);
        }

        private bool TryGetActivityZoneById(string zoneId, out BotActivityZone zone, out string reason)
        {
            zone = null;
            reason = null;
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                reason = "missing-zone-id";
                return false;
            }
            if (_ambiguousActivityZoneIds.Contains(zoneId))
            {
                reason = "duplicate-zone-id";
                return false;
            }
            if (!_activityZonesById.TryGetValue(zoneId, out zone))
            {
                reason = "missing-zone";
                return false;
            }
            if (!TryValidateActivityZone(zone, out reason))
            {
                zone = null;
                return false;
            }
            return true;
        }

        private static string GetActivitySpawnPath(SpawnRequest request)
        {
            if (request?.BotSpawnGroupEventId != null &&
                request.BotSpawnGroupEventId.IndexOf("|trigger|", StringComparison.Ordinal) >= 0)
                return "triggered-grouped-point";
            if (!string.IsNullOrEmpty(request?.BotSpawnGroupEventId))
                return "group-selected-point";
            if (request?.Point?.triggerActivated == true)
                return "triggered-point";
            return "custom-point";
        }

        // Read-only lookup for the leash layer: exact profile ID only, confirmed MEL-owned bots only.
        // isFollower is true for escorts MEL spawned with the boss (they get boundary-only enforcement).
        public static bool TryGetLeashAssignment(string profileId, out BotActivityZone zone, out string zoneId, out string spawnPointId, out bool isFollower, out string reason)
        {
            zone = null;
            zoneId = null;
            spawnPointId = null;
            isFollower = false;
            reason = null;

            var spawner = Instance;
            if (spawner == null || string.IsNullOrEmpty(profileId))
            {
                reason = "no-registry";
                return false;
            }
            if (!spawner._activityBotsByProfileId.TryGetValue(profileId, out var tracking) || tracking == null)
            {
                reason = "not-mel-owned";
                return false;
            }
            zoneId = tracking.ZoneId;
            spawnPointId = tracking.SpawnPointId;
            if (!tracking.Confirmed)
            {
                reason = "ownership-unconfirmed";
                return false;
            }
            isFollower = tracking.SpawnPath != null && tracking.SpawnPath.EndsWith("-follower", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(tracking.ZoneId))
            {
                reason = "no-zone-assigned";
                return false;
            }
            return spawner.TryGetActivityZoneById(tracking.ZoneId, out zone, out reason);
        }

        public static bool TryGetLeashActivityZone(string zoneId, out BotActivityZone zone)
        {
            zone = null;
            var spawner = Instance;
            return spawner != null && spawner.TryGetActivityZoneById(zoneId, out zone, out _);
        }

        // Escort anchor: the live, confirmed, non-follower MEL bot from the SAME spawn point (exact ID match, never proximity).
        public static bool TryGetEscortAnchor(string followerProfileId, out Vector3 anchor)
        {
            anchor = default(Vector3);
            var spawner = Instance;
            if (spawner == null || string.IsNullOrEmpty(followerProfileId) ||
                !spawner._activityBotsByProfileId.TryGetValue(followerProfileId, out var self) || self == null)
                return false;

            foreach (var other in spawner._activityBotsByProfileId.Values)
            {
                if (other == null || !other.Confirmed || other.Owner == null || other.Owner.IsDead)
                    continue;
                if (other.SpawnPointId != self.SpawnPointId || other.ProfileId == self.ProfileId)
                    continue;
                if (other.SpawnPath != null && other.SpawnPath.EndsWith("-follower", StringComparison.Ordinal))
                    continue;
                anchor = other.Owner.Position;
                return true;
            }
            return false;
        }

        public static bool TryIsInsideLeashZone(BotActivityZone zone, Vector3 position, out bool inside, out string reason)
        {
            return TryIsInsideActivityZone(zone, position, out inside, out reason);
        }

        public static void LeashLog(string message)
        {
            ActivityLogInfo(message);
        }

        public static bool LeashDiagnosticsEnabled => ActivityDiagnosticsEnabled;

        private static bool ActivityDiagnosticsEnabled =>
            Plugin.EnableBotActivityZoneDiagnostics != null && Plugin.EnableBotActivityZoneDiagnostics.Value;

        // Diagnostics only: never affects the ownership registry and never throws.
        private static void ActivityLogInfo(string message)
        {
            if (!ActivityDiagnosticsEnabled)
                return;
            try { Plugin.Log.LogInfo(message); } catch { }
        }

        private static void ActivityLogWarn(string message)
        {
            if (!ActivityDiagnosticsEnabled)
                return;
            try { Plugin.Log.LogWarning(message); } catch { }
        }

        // Ownership bookkeeping must never abort the real spawn request, so failures are contained here.
        private void QueueActivityBot(SpawnRequest request, string profileId, string spawnPath)
        {
            try
            {
                QueueActivityBotCore(request, profileId, spawnPath);
            }
            catch (Exception ex)
            {
                try { Plugin.Log.LogWarning($"[MEL activity-zone] Ownership bookkeeping failed for profileId={profileId}; spawn request is unaffected: {ex.Message}"); } catch { }
            }
        }

        private void QueueActivityBotCore(SpawnRequest request, string profileId, string spawnPath)
        {
            if (request == null || !_spawnRequests.Contains(request))
            {
                ActivityLogWarn($"[MEL activity-zone] Registration skipped profileId={profileId}: reason=spawn-request-no-longer-current.");
                return;
            }
            if (!_activityZonesIndexed)
                BuildActivityZoneIndex(_currentMapId);
            if (string.IsNullOrWhiteSpace(profileId) || request.Point == null ||
                string.IsNullOrWhiteSpace(request.Point.botActivityZoneId))
                return;

            if (!TryGetAssignedActivityZone(request.Point, out var zone, out var reason))
            {
                ActivityLogWarn($"[MEL activity-zone] Registration skipped profileId={profileId}, spawnPointId={request.Point.id}, zoneId={request.Point.botActivityZoneId}: reason={reason ?? "invalid-assignment"}.");
                return;
            }

            if (_activityBotsByProfileId.ContainsKey(profileId))
            {
                ActivityLogWarn($"[MEL activity-zone] Registration skipped profileId={profileId}, spawnPointId={request.Point.id}, zoneId={zone.zoneId}: reason=duplicate-profile-id.");
                return;
            }

            EnsureActivityLifecycleHooks(_botsController?.BotSpawner);
            _activityBotsByProfileId.Add(profileId, new ActivityBotTracking
            {
                ProfileId = profileId,
                ZoneId = zone.zoneId,
                SpawnPointId = request.Point.id ?? "",
                SpawnPath = spawnPath,
                RequestedAt = Time.unscaledTime
            });
            ActivityLogInfo($"[MEL activity-zone] Ownership candidate profileId={profileId}, spawnPointId={request.Point.id}, zoneId={zone.zoneId}, path={spawnPath}; waiting for exact live-profile confirmation.");
        }

        private void EnsureActivityLifecycleHooks(BotSpawner spawner)
        {
            if (spawner == null || ReferenceEquals(spawner, _activityObservedSpawner))
                return;

            if (_activityObservedSpawner != null)
                ClearActivityTracking("bot-spawner-reinitialized");

            _activityObservedSpawner = spawner;
            _activityObservedSpawner.OnBotCreated += OnActivityBotCreated;
            _activityObservedSpawner.OnBotRemoved += OnActivityBotRemoved;
        }

        private void OnActivityBotCreated(BotOwner owner)
        {
            var profileId = owner?.Profile?.Id;
            if (string.IsNullOrWhiteSpace(profileId) ||
                !_activityBotsByProfileId.TryGetValue(profileId, out var tracking))
                return;

            ConfirmActivityBot(tracking, owner, "BotSpawner.OnBotCreated");
        }

        private void OnActivityBotRemoved(BotOwner owner)
        {
            var profileId = owner?.Profile?.Id;
            if (string.IsNullOrWhiteSpace(profileId) ||
                !_activityBotsByProfileId.TryGetValue(profileId, out var tracking))
                return;

            RemoveActivityBot(tracking, "BotSpawner.OnBotRemoved");
        }

        private void ConfirmActivityBot(ActivityBotTracking tracking, BotOwner owner, string confirmation)
        {
            if (tracking == null || owner?.Profile == null ||
                !string.Equals(owner.Profile.Id, tracking.ProfileId, StringComparison.Ordinal))
                return;

            tracking.Owner = owner;
            if (!tracking.Confirmed)
            {
                tracking.RegisteredAt = Time.time;
                tracking.Confirmed = true;
                var capability = DescribeLeashCapability(owner, tracking.ZoneId);
                ActivityLogInfo($"[MEL activity-zone] Ownership=CONFIRMED profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={tracking.ZoneId}, path={tracking.SpawnPath}, confirmation={confirmation}; {capability}; containment=not-yet-confirmed-by-movement.");
                if (capability.IndexOf("bigBrainLayer=not-registered", StringComparison.Ordinal) >= 0 ||
                    capability.IndexOf("botOwnerMover=missing", StringComparison.Ordinal) >= 0)
                    ActivityLogWarn($"[MEL activity-zone] Movement support may be unavailable profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={tracking.ZoneId}; {capability}.");
            }
        }

        private static string DescribeLeashCapability(BotOwner owner, string zoneId)
        {
            var role = "unknown";
            var brainName = "unknown";
            var activeLayer = "unknown";
            var hasMover = false;
            try
            {
                role = owner.Profile?.Info?.Settings?.Role.ToString() ?? "unknown";
                brainName = owner.Brain?.BaseBrain?.ShortName() ?? "unknown";
                activeLayer = ActivityZoneLeashCore.ActiveLayerName(owner);
                hasMover = owner.Mover != null;
            }
            catch { }

            var bigBrainInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("xyz.drakia.bigbrain");
            var registered = false;
            if (bigBrainInstalled)
            {
                try { registered = ActivityZoneLeashRegistrar.IsBrainRegistered(brainName); }
                catch { }
            }
            var bigBrainStatus = !bigBrainInstalled ? "not-installed" : (registered ? "registered" : "not-registered");
            var movementMode = BotActivityZone.LeashModeSoft;
            if (Instance != null && Instance.TryGetActivityZoneById(zoneId, out var zone, out _) && zone != null)
                movementMode = BotActivityZone.NormalizeLeashMode(zone.leashMode);
            return $"role={role}, brain={brainName}, bigBrainLayer={bigBrainStatus}, activeLayer={activeLayer}, botOwnerMover={(hasMover ? "available" : "missing")}, movementApi={(registered && hasMover ? "BigBrain->BotOwner.GoToPoint" : "no-confirmed-BigBrain-movement-path")}, mode={movementMode}, sainStrongAdapter={StrongLeashContainment.GetMovementStatus(owner)}";
        }

        private void UpdateActivityZoneTracking()
        {
            if (Time.unscaledTime < _nextActivityDiagnosticTime)
                return;
            _nextActivityDiagnosticTime = Time.unscaledTime + 10f;

            if (_activityBotsByProfileId.Count == 0)
                return;

            var world = Singleton<GameWorld>.Instance;
            if (world == null || !ReferenceEquals(world, _currentWorld))
            {
                if (_currentWorld != null)
                {
                    ClearActivityTracking(world == null ? "world-unavailable" : "world-changed");
                    _activityZonesById.Clear();
                    _ambiguousActivityZoneIds.Clear();
                    _activityZonesIndexed = false;
                }
                return;
            }

            var profileIds = new List<string>(_activityBotsByProfileId.Keys);
            foreach (var profileId in profileIds)
            {
                if (!_activityBotsByProfileId.TryGetValue(profileId, out var tracking))
                    continue;

                var owner = FindLiveBotByProfileId(profileId);
                if (owner == null)
                {
                    if (tracking.Confirmed)
                    {
                        RemoveActivityBot(tracking, "exact-profile-not-alive-in-world");
                    }
                    else if (Time.unscaledTime - tracking.RequestedAt >= 900f)
                    {
                        ActivityLogWarn($"[MEL activity-zone] Registration failed profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={tracking.ZoneId}, path={tracking.SpawnPath}: reason=profile-not-observed-within-900-seconds.");
                        RemoveActivityBot(tracking, "profile-not-observed-within-900-seconds");
                    }
                    continue;
                }

                ConfirmActivityBot(tracking, owner, "exact-live-profile-id");
                if (!TryGetActivityZoneById(tracking.ZoneId, out var zone, out var invalidReason))
                {
                    RemoveActivityBot(tracking, "zone-invalid-" + (invalidReason ?? "unknown"));
                    continue;
                }

                var position = owner.Position;
                if (!ActivityDiagnosticsEnabled)
                    continue;

                if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) ||
                    !TryIsInsideActivityZone(zone, position, out var inside, out invalidReason))
                {
                    ActivityLogWarn($"[MEL activity-zone] Sample skipped profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={tracking.ZoneId}: reason={invalidReason ?? "invalid-bot-position"}.");
                    continue;
                }

                var center = zone.position.ToVector3();
                var dimensions = zone.shape == BotActivityZoneShape.Circle
                    ? $"radius={zone.radius:F2}m"
                    : $"width={zone.EffectiveWidth:F2}m depth={zone.EffectiveDepth:F2}m yaw={zone.rotation.y:F1}deg";
                ActivityLogInfo(
                    $"[MEL activity-zone] Sample profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={zone.zoneId}, path={tracking.SpawnPath}, " +
                    $"position=({position.x:F2},{position.y:F2},{position.z:F2}), center=({center.x:F2},{center.y:F2},{center.z:F2}), " +
                    $"shape={zone.shape}, {dimensions}, inside={inside}, enabled={zone.enabled}, valid=true, registeredRaidSeconds={tracking.RegisteredAt - _raidStartTime:F1}, {DescribeLeashCapability(owner, zone.zoneId)}, containment=not-inferred-from-ownership.");
            }
        }

        private static bool TryIsInsideActivityZone(BotActivityZone zone, Vector3 position, out bool inside, out string reason)
        {
            inside = false;
            reason = null;
            if (!TryValidateActivityZone(zone, out reason))
                return false;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z))
            {
                reason = "invalid-bot-position";
                return false;
            }

            var center = zone.position.ToVector3();
            var delta = position - center;
            if (zone.shape == BotActivityZoneShape.Circle)
            {
                inside = delta.x * delta.x + delta.z * delta.z <= zone.radius * zone.radius;
                return true;
            }

            var local = Quaternion.Euler(0f, -zone.rotation.y, 0f) * delta;
            inside = Mathf.Abs(local.x) <= zone.EffectiveWidth * 0.5f &&
                Mathf.Abs(local.z) <= zone.EffectiveDepth * 0.5f;
            return true;
        }

        private void RemoveActivityBot(ActivityBotTracking tracking, string reason)
        {
            if (tracking == null)
                return;

            ActivityZonePatrolRuntime.Remove(tracking.ProfileId, "bot-" + reason);
            _activityBotsByProfileId.Remove(tracking.ProfileId);
            var state = tracking.Confirmed ? "registered" : "pending";
            ActivityLogInfo($"[MEL activity-zone] Cleanup profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={tracking.ZoneId}, path={tracking.SpawnPath}, state={state}, reason={reason}.");
            if (_activityBotsByProfileId.Count == 0 && _activityObservedSpawner != null)
            {
                _activityObservedSpawner.OnBotCreated -= OnActivityBotCreated;
                _activityObservedSpawner.OnBotRemoved -= OnActivityBotRemoved;
                _activityObservedSpawner = null;
            }
        }

        private void ClearActivityTracking(string reason)
        {
            ActivityZonePatrolRuntime.ClearAll("raid-" + reason);
            if (_activityObservedSpawner != null)
            {
                _activityObservedSpawner.OnBotCreated -= OnActivityBotCreated;
                _activityObservedSpawner.OnBotRemoved -= OnActivityBotRemoved;
                _activityObservedSpawner = null;
            }

            foreach (var tracking in _activityBotsByProfileId.Values)
            {
                var state = tracking.Confirmed ? "registered" : "pending";
                ActivityLogInfo($"[MEL activity-zone] Cleanup profileId={tracking.ProfileId}, spawnPointId={tracking.SpawnPointId}, zoneId={tracking.ZoneId}, path={tracking.SpawnPath}, state={state}, reason={reason}.");
            }
            _activityBotsByProfileId.Clear();
            _nextActivityDiagnosticTime = 0f;
        }

        private void TryForcePlayerSpawn()
        {
            if (_forceSpawned || !_pendingForcePosition.HasValue)
                return;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || player.Transform == null)
                return;

            player.Transform.position = _pendingForcePosition.Value;
            if (_pendingForceRotation.HasValue)
                player.Transform.rotation = _pendingForceRotation.Value;

            _forceSpawned = true;
            Plugin.Log.LogInfo($"Force spawned player at {_pendingForcePosition.Value} rotation={_pendingForceRotation}.");
        }

        private void Update()
        {
            if (_activityBotsByProfileId.Count > 0 || _activityObservedSpawner != null)
                UpdateActivityZoneTracking();
            TryForcePlayerSpawn();

            if (_triggerZones.Count == 0)
                return;

            var player = _currentWorld?.MainPlayer;
            if (player == null || player.Transform == null)
                return;

            var playerPos = player.Transform.position;
            var profileId = player.ProfileId ?? "";
            var side = player.Side;
            var raidTime = Time.time - _raidStartTime;

            for (int i = _pendingTriggers.Count - 1; i >= 0; i--)
            {
                var pending = _pendingTriggers[i];
                if (Time.time >= pending.ActivationTime)
                {
                    _pendingTriggers.RemoveAt(i);
                    ActivateTrigger(pending.Trigger, pending.PlayerProfileId);
                }
            }

            foreach (var trigger in _triggerZones)
            {
                if (!IsPlayerInsideTrigger(trigger, playerPos))
                    continue;

                if (!CanTriggerActivate(trigger, profileId, side, raidTime))
                    continue;

                if (trigger.delaySeconds > 0f)
                {
                    if (!_pendingTriggers.Any(p => p.Trigger == trigger && p.PlayerProfileId == profileId))
                        _pendingTriggers.Add(new PendingTrigger { Trigger = trigger, PlayerProfileId = profileId, ActivationTime = Time.time + trigger.delaySeconds });
                    continue;
                }

                ActivateTrigger(trigger, profileId);
            }
        }

        private bool CanTriggerActivate(TriggerZone trigger, string profileId, EPlayerSide side, float raidTime)
        {
            if (trigger.minRaidTime > 0f && raidTime < trigger.minRaidTime)
                return false;
            if (trigger.maxRaidTime > 0f && raidTime > trigger.maxRaidTime)
                return false;

            if (trigger.allowedSide == TriggerSide.Pmc && side != EPlayerSide.Bear && side != EPlayerSide.Usec)
                return false;
            if (trigger.allowedSide == TriggerSide.Scav && side != EPlayerSide.Savage)
                return false;

            if (!_triggerStates.TryGetValue(trigger.name, out var state))
                return true;

            if (trigger.triggerMode == TriggerMode.OneTime)
                return false;

            if (trigger.triggerMode == TriggerMode.OncePerPlayer && state.ActivatedPlayers.Contains(profileId))
                return false;

            if (trigger.triggerMode == TriggerMode.Repeatable && trigger.cooldownSeconds > 0f && Time.time - state.LastActivationTime < trigger.cooldownSeconds)
                return false;

            return true;
        }

        private bool IsPlayerInsideTrigger(TriggerZone trigger, Vector3 position)
        {
            var center = trigger.position.ToVector3();
            var rotation = trigger.rotation.ToQuaternion();
            var scale = trigger.scale.ToVector3();
            var localPos = Quaternion.Inverse(rotation) * (position - center);

            switch (trigger.shape)
            {
                case ZoneShape.Box:
                    return Mathf.Abs(localPos.x) <= scale.x / 2f && Mathf.Abs(localPos.y) <= scale.y / 2f && Mathf.Abs(localPos.z) <= scale.z / 2f;
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var radius = Mathf.Max(scale.x, scale.z) / 2f;
                    var distXZ = Mathf.Sqrt(localPos.x * localPos.x + localPos.z * localPos.z);
                    return distXZ <= radius && Mathf.Abs(localPos.y) <= scale.y / 2f;
                default:
                    return localPos.magnitude <= scale.x / 2f;
            }
        }

        private void ActivateTrigger(TriggerZone trigger, string profileId)
        {
            if (trigger.triggerChance < 100f && _rng.NextDouble() * 100 > trigger.triggerChance)
            {
                Plugin.Log.LogInfo($"Trigger zone '{trigger.name}' failed chance roll ({trigger.triggerChance:F2}%).");
                return;
            }

            if (!_triggerStates.TryGetValue(trigger.name, out var state))
            {
                state = new TriggerState();
                _triggerStates[trigger.name] = state;
            }
            state.LastActivationTime = Time.time;
            state.ActivatedPlayers.Add(profileId);

            Plugin.Log.LogInfo($"Trigger zone '{trigger.name}' activated for player {profileId}.");

            ApplyTriggerLightActions(trigger);

            if (_triggeredSpawnData.TryGetValue(trigger.name, out var data))
            {
                var activationId = ++_triggerActivationCounter;
                foreach (var spawn in data.Where(spawn => spawn.Point != null && string.IsNullOrWhiteSpace(spawn.GroupKey)))
                {
                    SpawnTriggeredPoint(spawn.Point, spawn.BotZone);
                }

                foreach (var group in data
                             .Where(spawn => spawn.Point != null && !string.IsNullOrWhiteSpace(spawn.GroupKey))
                             .GroupBy(spawn => spawn.GroupKey, StringComparer.Ordinal))
                    SpawnTriggeredPointGroup(group.Key, group.ToList(), activationId);

                foreach (var spawn in data.Where(spawn => spawn.Zone != null))
                    SpawnTriggeredZone(spawn.Zone, spawn.BotZone);
            }
        }

        private void SpawnTriggeredPointGroup(string groupKey, List<TriggerSpawnData> members, int activationId)
        {
            if (_botsController?.BotSpawner == null || members.Count == 0)
                return;

            var representative = members[0].Point;
            var eligible = members.Where(spawn =>
            {
                var point = spawn.Point;
                var position = point.position?.ToVector3() ?? Vector3.zero;
                return spawn.BotZone != null &&
                       point.position != null &&
                       IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z) &&
                       IsFinite(point.radius) && point.radius > 0f;
            }).ToList();
            if (eligible.Count == 0)
            {
                Plugin.Log.LogWarning($"Trigger event {activationId} bot spawn group '{groupKey}' has no eligible locations.");
                return;
            }

            if (representative.spawnChance < 100f && _rng.NextDouble() * 100 > representative.spawnChance)
            {
                Plugin.Log.LogInfo($"Trigger event {activationId} bot spawn group '{groupKey}' failed its single Spawn Chance roll.");
                return;
            }

            var selected = eligible[_rng.Next(eligible.Count)];
            var pointData = selected.Point;
            var resolvedType = pointData.randomSpawnTypes != null && pointData.randomSpawnTypes.Count > 0
                ? pointData.randomSpawnTypes[_rng.Next(pointData.randomSpawnTypes.Count)]
                : pointData.wildSpawnType;
            var eventId = groupKey + "|trigger|" + activationId;
            if (!_submittedBotSpawnGroupEvents.Add(eventId))
            {
                Plugin.Log.LogWarning($"Trigger event {activationId} bot spawn group '{groupKey}' was already submitted; duplicate request skipped.");
                return;
            }
            var marker = CreateSpawnMarker(
                pointData.id,
                pointData.position.ToVector3(),
                pointData.rotation != null ? pointData.rotation.ToVector3().y : 0f,
                pointData.radius,
                pointData.side,
                pointData.category,
                pointData.delayToCanSpawnSec,
                selected.BotZone.name,
                new[] { selected.BotZone },
                pointData,
                resolvedType: resolvedType,
                botSpawnGroupKey: groupKey,
                botSpawnGroupEventId: eventId);
            if (marker == null)
            {
                Plugin.Log.LogWarning($"Trigger event {activationId} bot spawn group '{groupKey}' selected marker={pointData.id}, but marker creation failed; no alternate location will be submitted.");
                return;
            }

            var request = _spawnRequests.LastOrDefault(candidate => candidate.Marker == marker);
            if (request == null)
                return;

            UpdateMarkerCorePoint(_botsController, marker);
            if (BotSpawnChancePassed(request))
            {
                Plugin.Log.LogInfo($"Trigger event {activationId} bot spawn group '{groupKey}' selected marker={pointData.id} of {eligible.Count} eligible locations.");
                SpawnBotAt(_botsController, request);
            }
            else
            {
                Plugin.Log.LogInfo($"Trigger event {activationId} bot spawn group '{groupKey}' failed its single Bot Spawn Chance roll; no alternate location will be submitted.");
            }
        }

        private static bool IsGroupedSpawnLocationValid(SpawnRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.BotSpawnGroupKey))
                return true;
            return request.Marker != null &&
                   request.Zone != null &&
                   request.Zone.SpawnPointMarkers != null &&
                   request.Zone.SpawnPointMarkers.Contains(request.Marker);
        }

        private void ApplyTriggerLightActions(TriggerZone trigger)
        {
            if (trigger.lightZoneNames == null || trigger.lightZoneNames.Count == 0)
                return;

            var spawner = RuntimeLightZoneSpawner.Instance;
            if (spawner == null)
            {
                Plugin.Log.LogWarning("Cannot apply light actions: RuntimeLightZoneSpawner is not available.");
                return;
            }

            foreach (var name in trigger.lightZoneNames)
            {
                bool? desiredState = null;
                if (trigger.lightAction == TriggerLightAction.Enable)
                    desiredState = true;
                else if (trigger.lightAction == TriggerLightAction.Disable)
                    desiredState = false;
                spawner.SetLightState(name, desiredState);
            }
        }

        private void SpawnTriggeredPoint(BotSpawnPoint point, BotZone botZone)
        {
            if (_botsController?.BotSpawner == null)
                return;

            if (point.spawnChance < 100f && _rng.NextDouble() * 100 > point.spawnChance)
            {
                Plugin.Log.LogInfo($"Trigger-activated point {point.id} failed spawn chance.");
                return;
            }

            var resolvedType = point.randomSpawnTypes != null && point.randomSpawnTypes.Count > 0
                ? point.randomSpawnTypes[_rng.Next(point.randomSpawnTypes.Count)]
                : point.wildSpawnType;

            var marker = CreateSpawnMarker(point.id, point.position.ToVector3(), point.rotation.ToVector3().y, point.radius, point.side, point.category, point.delayToCanSpawnSec, botZone.name, new[] { botZone }, point, resolvedType: resolvedType);
            if (marker == null)
                return;

            var request = _spawnRequests.LastOrDefault(r => r.Marker == marker);
            if (request == null)
                return;

            UpdateMarkerCorePoint(_botsController, marker);
            if (BotSpawnChancePassed(request))
                SpawnBotAt(_botsController, request);
            else
                Plugin.Log.LogInfo($"Trigger-activated point {point.id} failed bot spawn chance.");
        }

        private void SpawnTriggeredZone(BotSpawnZone zone, BotZone botZone)
        {
            if (_botsController?.BotSpawner == null)
                return;

            if (zone.spawnChance < 100f && _rng.NextDouble() * 100 > zone.spawnChance)
            {
                Plugin.Log.LogInfo($"Trigger-activated zone {zone.id} failed spawn chance.");
                return;
            }

            var requests = CreateZoneMarkers(zone, new[] { botZone }, botZone.name, "trigger");
            if (requests.Count == 0)
                return;

            foreach (var request in requests)
            {
                UpdateMarkerCorePoint(_botsController, request.Marker);
            }

            var filtered = requests.Where(r => BotSpawnChancePassed(r)).ToList();
            if (filtered.Count > 0)
            {
                SpawnZoneGroup(_botsController, zone, filtered);
                Plugin.Log.LogInfo($"Trigger-activated zone {zone.id} spawned {filtered.Count} bots.");
            }
            else
            {
                Plugin.Log.LogInfo($"Trigger-activated zone {zone.id} had no bots pass bot spawn chance.");
            }
        }

        private void OnDestroy()
        {
            ClearActivityTracking("component-destroyed");
            Instance = null;
        }
    }
}
