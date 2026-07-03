using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Quests;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeLightZoneSpawner : MonoBehaviour
    {
        public static RuntimeLightZoneSpawner Instance { get; private set; }

        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private List<GameObject> _spawned = new List<GameObject>();

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LoadPacks();
        }

        public void ResetState()
        {
            ClearSpawned();
            _currentWorld = null;
            _currentMapId = null;
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            var worldChanged = _currentWorld != world;

            if (world == null || worldChanged)
            {
                if (_currentWorld != null)
                {
                    Plugin.Log.LogInfo("[MLEL Light] GameWorld changed or ended, clearing light zones.");
                    ClearSpawned();
                    _currentWorld = null;
                    _currentMapId = null;
                }
            }

            if (world == null)
                return;

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentWorld = world;
                _currentMapId = mapId;
                ClearSpawned();
                SpawnCustomLights();
            }
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning("[MLEL Light] No pack directories found; light zones will not be spawned.");
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
                            Plugin.Log.LogInfo($"[MLEL Light] Loaded pack '{pack.name}' from {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[MLEL Light] Failed to load pack {file}: {ex.Message}");
                    }
                }
            }
        }

        private void SpawnCustomLights()
        {
            var world = Singleton<GameWorld>.Instance;
            var mapId = world?.LocationId;
            if (string.IsNullOrEmpty(mapId) && world?.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (string.IsNullOrEmpty(mapId))
            {
                Plugin.Log.LogWarning("[MLEL Light] Cannot spawn light zones: no current map.");
                return;
            }

            var zones = new List<LightZone>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    foreach (var zone in map.lightZones ?? new List<LightZone>())
                    {
                        if (!QuestConditionsMet(zone.questOnly, zone.questCompleted, zone.questId))
                        {
                            Plugin.Log.LogInfo($"[MLEL Light] Skipping quest-gated light zone '{zone.name}' (quest {zone.questId} not active/completed).");
                            continue;
                        }
                        zones.Add(zone);
                    }
                }
            }

            if (zones.Count == 0)
            {
                Plugin.Log.LogInfo($"[MLEL Light] No custom light zones for map {mapId}.");
                return;
            }

            var rng = new System.Random();
            var created = 0;

            Plugin.Log.LogInfo($"[MLEL Light] Spawning {zones.Count} custom light zones for map {mapId}.");
            foreach (var zone in zones)
            {
                if (zone.spawnChance < 100f && rng.NextDouble() * 100 > zone.spawnChance)
                {
                    Plugin.Log.LogInfo($"[MLEL Light] Zone '{zone.name}' failed spawn chance roll ({zone.spawnChance:F2}%).");
                    continue;
                }

                try
                {
                    var go = CreateLightObject(zone);
                    if (go != null)
                    {
                        _spawned.Add(go);
                        created++;
                        Plugin.Log.LogInfo($"[MLEL Light] Created light zone '{zone.name}' at {zone.position.x:F2}, {zone.position.y:F2}, {zone.position.z:F2}.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[MLEL Light] Failed to create light zone '{zone.name}': {ex.Message}");
                }
            }

            Plugin.Log.LogInfo($"[MLEL Light] Created {created} custom light zones for map {mapId}.");
        }

        private GameObject CreateLightObject(LightZone zone)
        {
            var go = new GameObject($"CustomLightZone_{zone.name}");
            go.transform.position = zone.position.ToVector3();
            go.transform.rotation = zone.rotation.ToQuaternion();

            var light = go.AddComponent<Light>();
            light.type = ParseLightType(zone.lightType);
            light.color = zone.color.ToColor().linear;
            light.intensity = zone.intensity;
            light.range = zone.range;
            if (light.type == LightType.Spot)
                light.spotAngle = zone.spotAngle;

            Plugin.Log.LogInfo($"[MLEL Light] Created light '{zone.name}' color={zone.color.r:F2},{zone.color.g:F2},{zone.color.b:F2},{zone.color.a:F2} linear={light.color.r:F2},{light.color.g:F2},{light.color.b:F2}.");

            return go;
        }

        private LightType ParseLightType(string type)
        {
            if (Enum.TryParse<LightType>(type, true, out var result))
                return result;
            return LightType.Point;
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    try
                    {
                        Destroy(go);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[MLEL Light] Error destroying light zone: {ex.Message}");
                    }
                }
            }
            _spawned.Clear();
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
            ClearSpawned();
        }
    }
}
