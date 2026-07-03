using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.Quests;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeExtractZoneSpawner : MonoBehaviour
    {
        public static RuntimeExtractZoneSpawner Instance { get; private set; }

        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private List<ExfiltrationPoint> _spawned = new List<ExfiltrationPoint>();
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
                    Plugin.Log.LogInfo("[MLEL Extract] GameWorld changed or ended, clearing extract zones.");
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
                Plugin.Log.LogInfo($"[MLEL Extract] Map detected: {mapId}. Waiting for exfiltration controller initialization.");
            }
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning("[MLEL Extract] No pack directories found; extract zones will not be spawned.");
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
                            Plugin.Log.LogInfo($"[MLEL Extract] Loaded pack '{pack.name}' from {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[MLEL Extract] Failed to load pack {file}: {ex.Message}");
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
                var harmony = new Harmony("com.maplooteditorlite.extractzones");
                var method = AccessTools.Method(typeof(ExfiltrationControllerClass), nameof(ExfiltrationControllerClass.InitAllExfiltrationPoints));
                var postfix = AccessTools.Method(typeof(RuntimeExtractZoneSpawner), nameof(InitAllExfiltrationPointsPostfix));
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                Plugin.Log.LogInfo("[MLEL Extract] Patched ExfiltrationControllerClass.InitAllExfiltrationPoints.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MLEL Extract] Failed to apply patch: {ex.Message}");
            }
        }

        public static void InitAllExfiltrationPointsPostfix(MongoID locationId, LocationExitClass[] settings, GClass1432[] secretExitsSettings, bool justLoadSettings = false, string disabledScavExits = "", bool giveAuthority = true)
        {
            if (Instance == null)
            {
                Plugin.Log.LogWarning("[MLEL Extract] No spawner instance, skipping custom extract zones.");
                return;
            }

            if (justLoadSettings)
                return;

            Instance.SpawnCustomZones();
        }

        private void SpawnCustomZones()
        {
            var world = Singleton<GameWorld>.Instance;
            var mapId = world?.LocationId;
            if (string.IsNullOrEmpty(mapId) && world?.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (string.IsNullOrEmpty(mapId))
            {
                Plugin.Log.LogWarning("[MLEL Extract] Cannot spawn zones: no current map.");
                return;
            }

            var zones = new List<ExtractZone>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    foreach (var zone in map.extractZones ?? new List<ExtractZone>())
                    {
                        if (!QuestConditionsMet(zone.questOnly, zone.questCompleted, zone.questId))
                        {
                            Plugin.Log.LogInfo($"[MLEL Extract] Skipping quest-gated extract zone '{zone.name}' (quest {zone.questId} not active/completed).");
                            continue;
                        }
                        zones.Add(zone);
                    }
                }
            }

            if (zones.Count == 0)
            {
                Plugin.Log.LogInfo($"[MLEL Extract] No custom extract zones for map {mapId}.");
                return;
            }

            var player = world?.MainPlayer;
            var entryPoint = player?.Profile?.Info?.EntryPoint?.ToLower() ?? "";
            var rng = new System.Random();

            Plugin.Log.LogInfo($"[MLEL Extract] Spawning {zones.Count} custom extract zones for map {mapId}.");
            foreach (var zone in zones)
            {
                if (zone.spawnChance < 100f && rng.NextDouble() * 100 > zone.spawnChance)
                {
                    Plugin.Log.LogInfo($"[MLEL Extract] Zone '{zone.name}' failed spawn chance roll ({zone.spawnChance:F2}%).");
                    continue;
                }

                try
                {
                    var point = CreateExfiltrationPoint(zone, entryPoint);
                    if (point != null)
                    {
                        var list = ExfiltrationControllerClass.Instance.ExfiltrationPoints?.ToList() ?? new List<ExfiltrationPoint>();
                        list.Add(point);
                        ExfiltrationControllerClass.Instance.ExfiltrationPoints = list.ToArray();
                        _spawned.Add(point);
                        Plugin.Log.LogInfo($"[MLEL Extract] Registered custom extract zone '{zone.name}' ({zone.exitName}) at {zone.position.x:F2}, {zone.position.y:F2}, {zone.position.z:F2}.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[MLEL Extract] Failed to create extract zone '{zone.name}': {ex.Message}");
                }
            }
        }

        private ExfiltrationPoint CreateExfiltrationPoint(ExtractZone zone, string entryPoint)
        {
            var go = new GameObject($"CustomExtractZone_{zone.name}");
            go.layer = LayerMask.NameToLayer("Triggers");
            go.transform.position = zone.position.ToVector3();
            go.transform.rotation = zone.rotation.ToQuaternion();

            var boxCollider = go.AddComponent<BoxCollider>();
            boxCollider.isTrigger = true;
            boxCollider.size = Vector3.one * 0.01f;
            boxCollider.enabled = false;

            Collider triggerCollider = null;
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
                    boxCollider.size = new Vector3(scale.x, scale.y, scale.z);
                    boxCollider.enabled = true;
                    triggerCollider = boxCollider;
                    break;
                case ZoneShape.Sphere:
                    var sphere = go.AddComponent<SphereCollider>();
                    sphere.isTrigger = true;
                    sphere.radius = zone.radius * (zone.scale?.x ?? 1f);
                    triggerCollider = sphere;
                    break;
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.isTrigger = true;
                    capsule.radius = zone.radius * (zone.scale?.x ?? 1f);
                    capsule.height = (zone.scale?.y ?? 1f);
                    triggerCollider = capsule;
                    break;
            }

            var point = go.AddComponent<ExfiltrationPoint>();
            point.Settings = new ExitTriggerSettings
            {
                Id = zone.id,
                Name = string.IsNullOrWhiteSpace(zone.exitName) ? zone.name : zone.exitName,
                ExfiltrationType = ParseExfiltrationType(zone.exfiltrationType),
                ExfiltrationTime = zone.exfiltrationTime,
                Chance = 100f,
                MinTime = 0f,
                MaxTime = 0f,
                PlayersCount = 1,
                EntryPoints = entryPoint
            };
            point.EligibleEntryPoints = new[] { entryPoint };
            point.Reusable = false;

            var requirements = new List<ExfiltrationRequirement>();
            foreach (var req in zone.requirements ?? new List<ExtractZoneRequirement>())
            {
                var requirement = CreateRequirement(req);
                if (requirement != null)
                {
                    requirement.Start(point);
                    requirements.Add(requirement);
                }
            }
            point.Requirements = requirements.ToArray();

            point.SetInitialStatus();
            point.Entered.ItemAdded += point.OnPlayerEnter;
            point.Entered.ItemRemoved += point.OnPlayerExit;

            return point;
        }

        private ExfiltrationRequirement CreateRequirement(ExtractZoneRequirement req)
        {
            if (string.IsNullOrWhiteSpace(req.type))
                return null;

            switch (req.type)
            {
                case "TransferItem":
                    var transfer = new TransferItemRequirement();
                    transfer.Requirement = ERequirementState.TransferItem;
                    transfer.Id = req.templateId ?? "";
                    transfer.Count = req.count;
                    transfer.RequirementTip = string.IsNullOrWhiteSpace(req.requirementTip) ? "Exfiltration/TransferItem" : req.requirementTip;
                    return transfer;
                case "HasItem":
                    var hasItem = new GClass3706();
                    hasItem.Requirement = ERequirementState.HasItem;
                    hasItem.Id = req.templateId ?? "";
                    hasItem.Count = req.count;
                    hasItem.RequirementTip = string.IsNullOrWhiteSpace(req.requirementTip) ? "Exfiltration/HasItem" : req.requirementTip;
                    return hasItem;
                case "WearsItem":
                    var wears = new CustomWearsItemRequirement();
                    wears.Requirement = ERequirementState.WearsItem;
                    wears.Id = req.templateId ?? "";
                    wears.Count = req.count;
                    wears.RequirementTip = string.IsNullOrWhiteSpace(req.requirementTip) ? "Exfiltration/WearsItem" : req.requirementTip;
                    return wears;
                case "QuestActive":
                    var questActive = new CustomQuestRequirement { RequireCompleted = false };
                    questActive.Requirement = ERequirementState.None;
                    questActive.Id = req.templateId ?? "";
                    questActive.RequirementTip = string.IsNullOrWhiteSpace(req.requirementTip) ? "Quest active" : req.requirementTip;
                    return questActive;
                case "QuestCompleted":
                    var questCompleted = new CustomQuestRequirement { RequireCompleted = true };
                    questCompleted.Requirement = ERequirementState.None;
                    questCompleted.Id = req.templateId ?? "";
                    questCompleted.RequirementTip = string.IsNullOrWhiteSpace(req.requirementTip) ? "Quest completed" : req.requirementTip;
                    return questCompleted;
                default:
                    return null;
            }
        }

        private EExfiltrationType ParseExfiltrationType(string type)
        {
            if (Enum.TryParse<EExfiltrationType>(type, true, out var result))
                return result;
            return EExfiltrationType.Individual;
        }

        private void ClearSpawned()
        {
            foreach (var point in _spawned)
            {
                if (point != null)
                {
                    try
                    {
                        Destroy(point.gameObject);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[MLEL Extract] Error destroying zone: {ex.Message}");
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

    public class CustomWearsItemRequirement : ExfiltrationRequirement
    {
        public override bool Met(Player player, ExfiltrationPoint point)
        {
            if (string.IsNullOrWhiteSpace(Id))
                return false;
            var equipment = player.Profile.Inventory.Equipment;
            if (equipment == null)
                return false;
            return equipment.GetAllItems().Any(item => item.TemplateId == Id || item.StringTemplateId == Id);
        }
    }

    public class CustomQuestRequirement : ExfiltrationRequirement
    {
        public bool RequireCompleted;

        public override bool Met(Player player, ExfiltrationPoint point)
        {
            if (string.IsNullOrWhiteSpace(Id))
                return false;
            var quest = player.Profile.QuestsData.FirstOrDefault(q => q.Id == Id);
            if (quest == null)
                return false;

            if (RequireCompleted)
                return quest.Status == EQuestStatus.Success;

            return quest.Status == EQuestStatus.AvailableForStart
                || quest.Status == EQuestStatus.Started
                || quest.Status == EQuestStatus.AvailableForFinish;
        }
    }
}
