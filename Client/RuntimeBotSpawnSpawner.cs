using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LoadPacks();
            ApplyPatch();
        }

        public void ResetState()
        {
            _currentWorld = null;
            _currentMapId = null;
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
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
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
                }
            }

            if (points.Count == 0 && zones.Count == 0)
            {
                Plugin.Log.LogInfo($"[MLEL Bot] No custom bot spawn data for map {mapId}.");
                return;
            }

            var rng = new System.Random();
            var created = 0;

            foreach (var point in points)
            {
                if (point.spawnChance < 100f && rng.NextDouble() * 100 > point.spawnChance)
                    continue;
                var zoneName = ResolveBotZoneName(point.botZoneName, point.position.ToVector3(), botZones);
                var marker = CreateSpawnMarker(point.id, point.position.ToVector3(), point.rotation.ToVector3().y, point.radius, point.side, point.category, point.delayToCanSpawnSec, zoneName);
                if (marker != null)
                    created++;
            }

            foreach (var zone in zones)
            {
                var zoneName = ResolveBotZoneName(zone.botZoneName, zone.position.ToVector3(), botZones);
                for (int i = 0; i < zone.spawnCount; i++)
                {
                    if (zone.spawnChance < 100f && rng.NextDouble() * 100 > zone.spawnChance)
                        continue;
                    var pos = GetRandomPointInZone(zone);
                    var id = $"{zone.id}_spawn_{i}";
                    var marker = CreateSpawnMarker(id, pos, zone.rotation.ToVector3().y, 1f, zone.side, zone.category, zone.delayToCanSpawnSec, zoneName);
                    if (marker != null)
                        created++;
                }
            }

            Plugin.Log.LogInfo($"[MLEL Bot] Created {created} custom bot spawn markers for map {mapId}.");
        }

        private SpawnPointMarker CreateSpawnMarker(string id, Vector3 position, float rotationY, float radius, BotSpawnSide side, BotSpawnCategory category, float delay, string botZoneName)
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
                return SpawnPointMarker.Create(@params);
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

        private void OnDestroy()
        {
            Instance = null;
        }
    }
}
