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
        private float _raidStartTime;

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
        }

        private class TriggerSpawnData
        {
            public BotSpawnPoint Point;
            public BotSpawnZone Zone;
            public BotZone BotZone;
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
            _currentWorld = null;
            _currentMapId = null;
            _spawnRequests.Clear();
            _botsController = null;
            _triggerZones.Clear();
            _triggeredSpawnData.Clear();
            _triggerStates.Clear();
            _pendingTriggers.Clear();
            _raidStartTime = 0f;
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning("[MLEL Bot] No pack directories found; bot spawn points will not be spawned.");
                return;
            }

            foreach (var dir in directories)
            {
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var pack = JsonConvert.DeserializeObject<PackData>(json);
                        if (pack?.maps != null)
                        {
                            _packs.Add(pack);
                            Plugin.Log.LogInfo($"[MLEL Bot] Loaded pack '{pack.name}' from {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[MLEL Bot] Failed to load pack {file}: {ex.Message}");
                    }
                }
            }
        }

        private void ApplyPatch()
        {
            if (_patchApplied)
                return;
            _patchApplied = true;

            try
            {
                var harmony = new Harmony("com.maplooteditorlite.botspawns");
                var method = AccessTools.Method(typeof(BotsController), nameof(BotsController.Init));
                var prefix = AccessTools.Method(typeof(RuntimeBotSpawnSpawner), nameof(InitBotsControllerPrefix));
                var postfix = AccessTools.Method(typeof(RuntimeBotSpawnSpawner), nameof(InitBotsControllerPostfix));
                harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                Plugin.Log.LogInfo("[MLEL Bot] Patched BotsController.Init.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Failed to apply patch: {ex.Message}");
            }
        }

        public static void InitBotsControllerPrefix(BotZone[] __2)
        {
            if (Instance == null)
            {
                Plugin.Log.LogWarning("[MLEL Bot] No spawner instance, skipping custom bot spawn points.");
                return;
            }

            try
            {
                Instance.SpawnCustomMarkers(__2);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Error in prefix: {ex.Message}");
            }
        }

        public static void InitBotsControllerPostfix(BotsController __instance)
        {
            if (Instance == null)
            {
                Plugin.Log.LogWarning("[MLEL Bot] No spawner instance, skipping custom bot spawn.");
                return;
            }

            try
            {
                Instance.ForceSpawnCustomBots(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Error in postfix: {ex.Message}");
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
                Plugin.Log.LogWarning("[MLEL Bot] Cannot spawn bot points: no current map.");
                return;
            }

            _currentWorld = world;
            _currentMapId = mapId;
            _spawnRequests.Clear();
            _triggerZones.Clear();
            _triggeredSpawnData.Clear();
            _triggerStates.Clear();
            _pendingTriggers.Clear();
            _raidStartTime = Time.time;

            var points = new List<BotSpawnPoint>();
            var zones = new List<BotSpawnZone>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    foreach (var point in map.botSpawnPoints ?? new List<BotSpawnPoint>())
                    {
                        if (QuestConditionsMet(point.questOnly, point.questCompleted, point.questId))
                            points.Add(point);
                    }
                    foreach (var zone in map.botSpawnZones ?? new List<BotSpawnZone>())
                    {
                        if (QuestConditionsMet(zone.questOnly, zone.questCompleted, zone.questId))
                            zones.Add(zone);
                    }
                    foreach (var trigger in map.triggerZones ?? new List<TriggerZone>())
                    {
                        _triggerZones.Add(trigger);
                    }
                }
            }

            Plugin.Log.LogInfo($"[MLEL Bot] Spawning for map '{mapId}' with {points.Count} points, {zones.Count} zones, and {_triggerZones.Count} trigger zones from {_packs.Count} packs.");
            if (points.Count == 0 && zones.Count == 0 && _triggerZones.Count == 0)
            {
                Plugin.Log.LogInfo($"[MLEL Bot] No custom bot spawn or trigger data for map {mapId}.");
                return;
            }

            foreach (var point in points.Where(p => p.triggerActivated))
            {
                var zoneName = ResolveBotZoneName(point.botZoneName, point.position.ToVector3(), botZones);
                var botZone = botZones?.FirstOrDefault(z => z.name == zoneName);
                if (botZone == null)
                {
                    Plugin.Log.LogWarning($"[MLEL Bot] Trigger-activated point {point.id} has no valid BotZone, skipping.");
                    continue;
                }
                AddTriggeredSpawnData(point.triggerZoneName, new TriggerSpawnData { Point = point, BotZone = botZone });
            }

            foreach (var zone in zones.Where(z => z.triggerActivated))
            {
                var zoneName = ResolveBotZoneName(zone.botZoneName, zone.position.ToVector3(), botZones);
                var botZone = botZones?.FirstOrDefault(z => z.name == zoneName);
                if (botZone == null)
                {
                    Plugin.Log.LogWarning($"[MLEL Bot] Trigger-activated zone {zone.id} has no valid BotZone, skipping.");
                    continue;
                }
                AddTriggeredSpawnData(zone.triggerZoneName, new TriggerSpawnData { Zone = zone, BotZone = botZone });
            }

            var created = 0;

            foreach (var point in points.Where(p => !p.triggerActivated))
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

            foreach (var zone in zones.Where(z => !z.triggerActivated))
            {
                if (zone.spawnChance < 100f && _rng.NextDouble() * 100 > zone.spawnChance)
                    continue;

                var zoneName = ResolveBotZoneName(zone.botZoneName, zone.position.ToVector3(), botZones);
                created += CreateZoneMarkers(zone, botZones, zoneName, "spawn").Count;
            }

            Plugin.Log.LogInfo($"[MLEL Bot] Created {created} custom bot spawn markers for map {mapId} across {botZones?.Length ?? 0} zones. {points.Count(p => p.triggerActivated) + zones.Count(z => z.triggerActivated)} spawns are trigger-activated.");
        }

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

        private SpawnPointMarker CreateSpawnMarker(string id, Vector3 position, float rotationY, float radius, BotSpawnSide side, BotSpawnCategory category, float delay, string botZoneName, BotZone[] botZones, BotSpawnPoint point = null, BotSpawnZone zoneData = null, BotSpawnGroup group = null, string resolvedType = null)
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
                    Plugin.Log.LogWarning($"[MLEL Bot] No BotZone found for marker {id} (resolved name: '{botZoneName}'). Skipping.");
                    return null;
                }
                if (zone.SpawnPointMarkers == null)
                    zone.SpawnPointMarkers = new List<SpawnPointMarker>();

                var marker = SpawnPointMarker.Create(@params, zone.transform);
                if (marker == null)
                {
                    Plugin.Log.LogError($"[MLEL Bot] SpawnPointMarker.Create returned null for {id}.");
                    return null;
                }

                if (zone != null && !zone.SpawnPointMarkers.Contains(marker))
                    zone.SpawnPointMarkers.Add(marker);

                _spawnRequests.Add(new SpawnRequest { Zone = zone, Marker = marker, Point = point, ZoneData = zoneData, Group = group, ResolvedWildSpawnType = resolvedType });

                Plugin.Log.LogInfo($"[MLEL Bot] Created marker {id} at {position} in zone '{botZoneName}' (category: {category}, side: {side}).");
                return marker;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Failed to create spawn marker {id}: {ex.Message}");
                return null;
            }
        }

        private string ResolveBotZoneName(string requestedName, Vector3 position, BotZone[] botZones)
        {
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

        private bool QuestConditionsMet(bool questOnly, bool questCompleted, string questId)
        {
            if (!questOnly && !questCompleted)
                return true;

            if (string.IsNullOrWhiteSpace(questId))
                return false;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player?.Profile?.QuestsData == null)
                return false;

            var quest = player.Profile.QuestsData.FirstOrDefault(q => q.Id == questId);
            if (quest == null)
                return false;

            var active = quest.Status == EQuestStatus.AvailableForStart
                || quest.Status == EQuestStatus.Started
                || quest.Status == EQuestStatus.AvailableForFinish;

            var completed = quest.Status == EQuestStatus.Success;

            return (questOnly && active) || (questCompleted && completed);
        }

        private void ForceSpawnCustomBots(BotsController controller)
        {
            _botsController = controller;
            if (controller?.BotSpawner == null)
            {
                Plugin.Log.LogWarning("[MLEL Bot] BotsController or BotSpawner is null, cannot force custom spawn.");
                return;
            }

            Plugin.Log.LogInfo($"[MLEL Bot] Forcing spawn for {_spawnRequests.Count} custom bot requests.");
            foreach (var request in _spawnRequests)
            {
                UpdateMarkerCorePoint(controller, request.Marker);
            }

            var forcedRequests = _spawnRequests.Where(r => IsForcedSpawn(r) && BotSpawnChancePassed(r)).ToList();

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
                    Plugin.Log.LogWarning("[MLEL Bot] Skipping zone group spawn with unknown wild spawn type.");
                    return;
                }

                var side = ResolveSide(request);
                var difficulty = BotDifficulty.normal;
                var count = requests.Count;
                var spawnParams = new BotSpawnParams();
                spawnParams.ShallBeGroup = new ShallBeGroupParams(true, true, count);

                var data = await BotCreationDataClass.Create(new BotProfileDataClass(side, wildSpawnType.Value, difficulty, 0f, spawnParams, false), controller.BotSpawner.BotCreator, count, controller.BotSpawner);
                var points = requests.Select(r => r.Marker.SpawnPoint).ToList();
                controller.BotSpawner.TryToSpawnInZoneAndDelay(request.Zone, data, true, true, points, true);
                Plugin.Log.LogInfo($"[MLEL Bot] Submitted group spawn of {count} {wildSpawnType.Value} in zone {request.Zone.name} (corePoint={points[0].CorePointId}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Failed to spawn zone group: {ex.Message}");
            }
        }

        private async void SpawnBotAt(BotsController controller, SpawnRequest request)
        {
            try
            {
                var wildSpawnType = ResolveWildSpawnType(request);
                if (!wildSpawnType.HasValue)
                {
                    Plugin.Log.LogWarning("[MLEL Bot] Skipping spawn request with unknown wild spawn type.");
                    return;
                }

                var side = ResolveSide(request);
                var difficulty = BotDifficulty.normal;
                var point = request.Marker.SpawnPoint;
                var data = await BotCreationDataClass.Create(new BotProfileDataClass(side, wildSpawnType.Value, difficulty, 0f, new BotSpawnParams(), false), controller.BotSpawner.BotCreator, 1, controller.BotSpawner);
                controller.BotSpawner.TryToSpawnInZoneAndDelay(request.Zone, data, true, true, new List<ISpawnPoint> { point }, true);
                Plugin.Log.LogInfo($"[MLEL Bot] Submitted forced spawn at {point.Position} ({wildSpawnType.Value}, {side}, zone={request.Zone.name}, corePoint={point.CorePointId}).");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Failed to force spawn bot: {ex.Message}");
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
                Plugin.Log.LogError($"[MLEL Bot] Failed to get core point: {ex.Message}");
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
                    Plugin.Log.LogWarning($"[MLEL Bot] Could not resolve core point for {marker.SpawnPoint.Id}, using 0.");
                    return;
                }

                var spawnPointField = typeof(SpawnPointMarker).GetField("_spawnPoint", BindingFlags.NonPublic | BindingFlags.Instance);
                if (spawnPointField == null)
                {
                    Plugin.Log.LogWarning("[MLEL Bot] Could not find _spawnPoint field on SpawnPointMarker.");
                    return;
                }

                var spawnPoint = spawnPointField.GetValue(marker);
                if (spawnPoint == null)
                {
                    Plugin.Log.LogWarning("[MLEL Bot] _spawnPoint is null on marker.");
                    return;
                }

                var spawnPointType = spawnPoint.GetType();
                var newSpawnPoint = Activator.CreateInstance(spawnPointType);
                foreach (var prop in spawnPointType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.Name == "CorePointId")
                        prop.SetValue(newSpawnPoint, corePointId);
                    else if (prop.CanRead && prop.CanWrite)
                        prop.SetValue(newSpawnPoint, prop.GetValue(spawnPoint));
                }
                foreach (var field in spawnPointType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.Name == "CorePointId")
                        field.SetValue(newSpawnPoint, corePointId);
                    else
                        field.SetValue(newSpawnPoint, field.GetValue(spawnPoint));
                }
                spawnPointField.SetValue(marker, newSpawnPoint);
                Plugin.Log.LogInfo($"[MLEL Bot] Updated core point for {marker.SpawnPoint.Id} to {corePointId}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Bot] Failed to update marker core point: {ex.Message}");
            }
        }

        private WildSpawnType? ResolveWildSpawnType(SpawnRequest request)
        {
            var wildSpawnType = request.ResolvedWildSpawnType;
            if (string.IsNullOrWhiteSpace(wildSpawnType))
                wildSpawnType = request.Group?.wildSpawnType ?? request.Point?.wildSpawnType ?? request.ZoneData?.wildSpawnType;
            if (string.IsNullOrWhiteSpace(wildSpawnType))
            {
                var preset = request.Group?.preset ?? request.Point?.preset ?? request.ZoneData?.preset ?? BotSpawnPreset.Scav;
                wildSpawnType = BotSpawnPresetMapping.GetWildSpawnType(preset);
                Plugin.Log.LogInfo($"[MLEL Bot] wildSpawnType empty, inferred '{wildSpawnType}' from preset {preset}.");
            }
            if (string.IsNullOrWhiteSpace(wildSpawnType))
                return WildSpawnType.assault;
            if (Enum.TryParse<WildSpawnType>(wildSpawnType, true, out var result))
                return result;
            Plugin.Log.LogWarning($"[MLEL Bot] Unknown WildSpawnType: {wildSpawnType}");
            return null;
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

        private void Update()
        {
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
                Plugin.Log.LogInfo($"[MLEL Bot] Trigger zone '{trigger.name}' failed chance roll ({trigger.triggerChance:F2}%).");
                return;
            }

            if (!_triggerStates.TryGetValue(trigger.name, out var state))
            {
                state = new TriggerState();
                _triggerStates[trigger.name] = state;
            }
            state.LastActivationTime = Time.time;
            state.ActivatedPlayers.Add(profileId);

            Plugin.Log.LogInfo($"[MLEL Bot] Trigger zone '{trigger.name}' activated for player {profileId}.");

            ApplyTriggerLightActions(trigger);

            if (_triggeredSpawnData.TryGetValue(trigger.name, out var data))
            {
                foreach (var spawn in data)
                {
                    if (spawn.Point != null)
                        SpawnTriggeredPoint(spawn.Point, spawn.BotZone);
                    else if (spawn.Zone != null)
                        SpawnTriggeredZone(spawn.Zone, spawn.BotZone);
                }
            }
        }

        private void ApplyTriggerLightActions(TriggerZone trigger)
        {
            if (trigger.lightZoneNames == null || trigger.lightZoneNames.Count == 0)
                return;

            var spawner = RuntimeLightZoneSpawner.Instance;
            if (spawner == null)
            {
                Plugin.Log.LogWarning("[MLEL Bot] Cannot apply light actions: RuntimeLightZoneSpawner is not available.");
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
                Plugin.Log.LogInfo($"[MLEL Bot] Trigger-activated point {point.id} failed spawn chance.");
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
                Plugin.Log.LogInfo($"[MLEL Bot] Trigger-activated point {point.id} failed bot spawn chance.");
        }

        private void SpawnTriggeredZone(BotSpawnZone zone, BotZone botZone)
        {
            if (_botsController?.BotSpawner == null)
                return;

            if (zone.spawnChance < 100f && _rng.NextDouble() * 100 > zone.spawnChance)
            {
                Plugin.Log.LogInfo($"[MLEL Bot] Trigger-activated zone {zone.id} failed spawn chance.");
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
                Plugin.Log.LogInfo($"[MLEL Bot] Trigger-activated zone {zone.id} spawned {filtered.Count} bots.");
            }
            else
            {
                Plugin.Log.LogInfo($"[MLEL Bot] Trigger-activated zone {zone.id} had no bots pass bot spawn chance.");
            }
        }

        private void OnDestroy()
        {
            Instance = null;
        }
    }
}
