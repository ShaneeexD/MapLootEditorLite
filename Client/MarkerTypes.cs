using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public enum MarkerKind
    {
        LooseLoot,
        LootZone,
        StaticObject,
        WTTQuestZone,
        WTTStaticObject,
        InteractiveObject,
        ExtractZone,
        BotSpawnPoint,
        BotSpawnZone,
        PmcSpawnZone,
        LightZone,
        TriggerZone,
        OcclusionRepairVolume,
        CutVolume
    }

    public enum ExtractZoneRequirementType
    {
        None,
        TransferItem,
        HasItem,
        WearsItem,
        QuestActive,
        QuestCompleted
    }

    public enum InteractiveObjectType
    {
        Door,
        Container,
        StationaryWeapon
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerMode
    {
        OneTime,
        Repeatable,
        OncePerPlayer
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerLightAction
    {
        Toggle,
        Enable,
        Disable
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum TriggerSide
    {
        Any,
        Pmc,
        Scav
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum BotSpawnSide
    {
        Savage,
        Bear,
        Usec,
        Pmc,
        All
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum BotSpawnCategory
    {
        Bot,
        Boss,
        BotPmc,
        All
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum BotSpawnPreset
    {
        Any,
        Scav,
        SniperScav,
        Raider,
        Rogue,
        PMC,
        Bear,
        Usec,
        Boss,
        Killa,
        Tagilla,
        Gluhar,
        Sanitar,
        Kojaniy,
        Knight,
        Zryachiy,
        Boar,
        Kolontay,
        Partisan,
        Cultist,
        Infected
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ContainerLootMode
    {
        Default,
        Hybrid,
        Custom
    }

    public static class BotSpawnPresetMapping
    {
        public static readonly Dictionary<BotSpawnPreset, string> PresetNames = new Dictionary<BotSpawnPreset, string>
        {
            { BotSpawnPreset.Any, "Any / Default" },
            { BotSpawnPreset.Scav, "Scav" },
            { BotSpawnPreset.SniperScav, "Sniper Scav" },
            { BotSpawnPreset.Raider, "Raider" },
            { BotSpawnPreset.Rogue, "Rogue" },
            { BotSpawnPreset.PMC, "PMC" },
            { BotSpawnPreset.Bear, "BEAR" },
            { BotSpawnPreset.Usec, "USEC" },
            { BotSpawnPreset.Boss, "Boss (generic)" },
            { BotSpawnPreset.Killa, "Killa" },
            { BotSpawnPreset.Tagilla, "Tagilla" },
            { BotSpawnPreset.Gluhar, "Gluhar" },
            { BotSpawnPreset.Sanitar, "Sanitar" },
            { BotSpawnPreset.Kojaniy, "Kojaniy (Shturman)" },
            { BotSpawnPreset.Knight, "Knight (Goons)" },
            { BotSpawnPreset.Zryachiy, "Zryachiy" },
            { BotSpawnPreset.Boar, "Boar (Kaban)" },
            { BotSpawnPreset.Kolontay, "Kolontay" },
            { BotSpawnPreset.Partisan, "Partisan" },
            { BotSpawnPreset.Cultist, "Cultist" },
            { BotSpawnPreset.Infected, "Infected" }
        };

        public static void ApplyPreset(BotSpawnPreset preset, BotSpawnPoint point)
        {
            point.wildSpawnType = GetWildSpawnType(preset);
            ApplyPreset(preset, ref point.side, ref point.category);
        }

        public static void ApplyPreset(BotSpawnPreset preset, BotSpawnZone zone)
        {
            zone.wildSpawnType = GetWildSpawnType(preset);
            ApplyPreset(preset, ref zone.side, ref zone.category);
        }

        public static void ApplyPreset(BotSpawnPreset preset, BotSpawnGroup group)
        {
            group.wildSpawnType = GetWildSpawnType(preset);
            ApplyPreset(preset, ref group.side, ref group.category);
        }

        private static void ApplyPreset(BotSpawnPreset preset, ref BotSpawnSide side, ref BotSpawnCategory category)
        {
            switch (preset)
            {
                case BotSpawnPreset.PMC:
                case BotSpawnPreset.Bear:
                case BotSpawnPreset.Usec:
                    side = BotSpawnSide.Pmc;
                    category = BotSpawnCategory.BotPmc;
                    break;
                case BotSpawnPreset.Boss:
                case BotSpawnPreset.Killa:
                case BotSpawnPreset.Tagilla:
                case BotSpawnPreset.Gluhar:
                case BotSpawnPreset.Sanitar:
                case BotSpawnPreset.Kojaniy:
                case BotSpawnPreset.Knight:
                case BotSpawnPreset.Zryachiy:
                case BotSpawnPreset.Boar:
                case BotSpawnPreset.Kolontay:
                case BotSpawnPreset.Partisan:
                    side = BotSpawnSide.Savage;
                    category = BotSpawnCategory.Boss;
                    break;
                case BotSpawnPreset.Cultist:
                case BotSpawnPreset.Infected:
                case BotSpawnPreset.SniperScav:
                case BotSpawnPreset.Raider:
                case BotSpawnPreset.Rogue:
                case BotSpawnPreset.Scav:
                    side = BotSpawnSide.Savage;
                    category = BotSpawnCategory.Bot;
                    break;
                default:
                    side = BotSpawnSide.All;
                    category = BotSpawnCategory.All;
                    break;
            }
        }

        public static string GetWildSpawnType(BotSpawnPreset preset)
        {
            switch (preset)
            {
                case BotSpawnPreset.Scav: return "assault";
                case BotSpawnPreset.SniperScav: return "marksman";
                case BotSpawnPreset.Raider:
                case BotSpawnPreset.Rogue: return "exUsec";
                case BotSpawnPreset.PMC: return "pmcBot";
                case BotSpawnPreset.Bear: return "pmcBEAR";
                case BotSpawnPreset.Usec: return "pmcUSEC";
                case BotSpawnPreset.Killa: return "bossKilla";
                case BotSpawnPreset.Tagilla: return "bossTagilla";
                case BotSpawnPreset.Gluhar: return "bossGluhar";
                case BotSpawnPreset.Sanitar: return "bossSanitar";
                case BotSpawnPreset.Kojaniy: return "bossKojaniy";
                case BotSpawnPreset.Knight: return "bossKnight";
                case BotSpawnPreset.Zryachiy: return "bossZryachiy";
                case BotSpawnPreset.Boar: return "bossBoar";
                case BotSpawnPreset.Kolontay: return "bossKolontay";
                case BotSpawnPreset.Partisan: return "bossPartisan";
                case BotSpawnPreset.Cultist: return "sectantWarrior";
                case BotSpawnPreset.Infected: return "infectedAssault";
                default: return "";
            }
        }

        public static BotSpawnPreset ParsePreset(string wildSpawnType)
        {
            if (string.IsNullOrEmpty(wildSpawnType))
                return BotSpawnPreset.Any;
            foreach (var kvp in PresetNames)
            {
                if (GetWildSpawnType(kvp.Key).Equals(wildSpawnType, StringComparison.OrdinalIgnoreCase))
                    return kvp.Key;
            }
            return BotSpawnPreset.Any;
        }
    }

    public class MapData
    {
        public string map;
        public List<LooseLootSpawn> lootSpawns = new List<LooseLootSpawn>();
        public List<LootZone> lootZones = new List<LootZone>();
        public List<StaticObject> objects = new List<StaticObject>();
        public List<WTTQuestZone> wttQuestZones = new List<WTTQuestZone>();
        public List<WTTStaticObject> wttStaticObjects = new List<WTTStaticObject>();
        public List<InteractiveObject> interactiveObjects = new List<InteractiveObject>();
        public List<ExtractZone> extractZones = new List<ExtractZone>();
        public List<BotSpawnPoint> botSpawnPoints = new List<BotSpawnPoint>();
        public List<BotSpawnZone> botSpawnZones = new List<BotSpawnZone>();
        public List<PmcSpawnZone> pmcSpawnZones = new List<PmcSpawnZone>();
        public List<LightZone> lightZones = new List<LightZone>();
        public List<TriggerZone> triggerZones = new List<TriggerZone>();
        public List<OcclusionRepairVolume> occlusionRepairVolumes = new List<OcclusionRepairVolume>();
        public List<CutVolume> cutVolumes = new List<CutVolume>();
        public List<RemovedObject> removedObjects = new List<RemovedObject>();
    }

    public class RemovedObject
    {
        public string id = System.Guid.NewGuid().ToString("N");
        public string name = "";
        public string path = "";
        public TransformData position = new TransformData();
        public TransformData rotation = new TransformData();
        public TransformData scale = new TransformData() { x = 1, y = 1, z = 1 };
        public int originalDoorState = -1; // -1 = not a door, otherwise EFT.Interactive.EDoorState value
    }

    public class PackData
    {
        public string name = "My Loot Pack";
        public string author = "";
        public string version = "1.1.0";
        public System.Collections.Generic.Dictionary<string, MapData> maps = new System.Collections.Generic.Dictionary<string, MapData>(System.StringComparer.OrdinalIgnoreCase);
    }

    public class TransformData
    {
        public float x;
        public float y;
        public float z;

        public static TransformData FromVector3(Vector3 v)
        {
            return new TransformData { x = v.x, y = v.y, z = v.z };
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }

        public Quaternion ToQuaternion()
        {
            return Quaternion.Euler(x, y, z);
        }
    }

    public abstract class MarkerBase
    {
        public string id = Guid.NewGuid().ToString();
        public string name = "marker";
        [JsonProperty("group")]
        public string group = "";
        public TransformData position = new TransformData();
        public TransformData rotation = new TransformData();

        [JsonIgnore]
        public bool isVanilla = false;

        [JsonIgnore]
        public abstract MarkerKind Kind { get; }
    }

    public class LootItem
    {
        public string template = "";
        public float chance = 100f;
        public TransformData rotation = new TransformData();
        public bool randomRotation = true;
        public float yOffset = 0f;
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";
        public bool isDistribution = false; // True when chance is a relative weight from staticLoot.json
        public int count = 1; // Stack count for currency/ammo/etc
    }

    public class LooseLootSpawn : MarkerBase
    {
        public List<string> itemTpls = new List<string>();
        public List<LootItem> items = new List<LootItem>();
        public float spawnChance = 100f;
        public bool respawnable = false;
        public bool forced = false;
        public bool useGravity = false;
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";

        public override MarkerKind Kind => MarkerKind.LooseLoot;
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ZoneShape
    {
        Sphere,
        Box,
        Cylinder,
        Capsule
    }

    public class LootZone : MarkerBase
    {
        public float radius = 1f;
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };
        public ZoneShape shape = ZoneShape.Box;
        public List<string> itemTpls = new List<string>();
        public List<LootItem> items = new List<LootItem>();
        public float spawnChance = 100f;
        public bool forced = false;
        public bool useGravity = false;
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";

        public override MarkerKind Kind => MarkerKind.LootZone;
    }

    public interface IHasSourceObject
    {
        string sourceObjectName { get; set; }
        TransformData sourceObjectPosition { get; set; }
    }

    public class StaticObject : MarkerBase, IHasSourceObject
    {
        public string prefabPath = "";
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };

        // Fallback: copy an existing vanilla scene object instead of loading a bundle.
        public string sourceObjectName { get; set; } = "";
        public TransformData sourceObjectPosition { get; set; } = new TransformData();
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";

        public override MarkerKind Kind => MarkerKind.StaticObject;
    }

    public class WTTQuestZone : MarkerBase
    {
        public string zoneId = "";
        public string zoneName = "";
        public string zoneLocation = "";
        public string zoneType = "placeitem";
        public string flareType = "";
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };

        public override MarkerKind Kind => MarkerKind.WTTQuestZone;
    }

    public class WTTStaticObject : MarkerBase, IHasSourceObject
    {
        public string spawnType = "bundle"; // "bundle" or "clone"

        // Bundle mode
        public string bundleName = "";
        public string prefabName = "";

        // Clone mode (copy an existing scene object)
        public string sourceObjectName { get; set; } = "";
        public TransformData sourceObjectPosition { get; set; } = new TransformData();
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };

        // Quest conditions
        public string questId = "";
        public List<string> requiredQuestStatuses = new List<string>();
        public List<string> excludedQuestStatuses = new List<string>();
        public bool questMustExist = true;
        public string linkedQuestId = "";
        public List<string> linkedRequiredStatuses = new List<string>();
        public List<string> linkedExcludedStatuses = new List<string>();
        public bool? linkedQuestMustExist = null;
        public string requiredItemInInventory = "";
        public int requiredLevel = 0;
        public string requiredFaction = "";
        public string requiredBossSpawned = "";
        public bool questOnly = false;
        public bool questCompleted = false;

        public override MarkerKind Kind => MarkerKind.WTTStaticObject;
    }

    public class InteractiveObject : MarkerBase, IHasSourceObject
    {
        public InteractiveObjectType interactiveType = InteractiveObjectType.Door;

        public string sourceObjectName { get; set; } = "";
        public TransformData sourceObjectPosition { get; set; } = new TransformData();
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };

        public string keyId = ""; // Door key template id

        public string containerId = ""; // Container item id
        public string containerTemplate = "578f87a3245977356274f2cb"; // Container root template id

        public string weaponTemplate = "5cdeb229d7f00c000e7ce174"; // Stationary weapon root template id (NSV Utes)
        public ContainerLootMode lootMode = ContainerLootMode.Default;
        public List<LootItem> items = new List<LootItem>();
        public int itemCountMin = 0;
        public int itemCountMax = 0;
        public float spawnChance = 100f;
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";

        public override MarkerKind Kind => MarkerKind.InteractiveObject;
    }

    public class ExtractZoneRequirement
    {
        public string type = "None";
        public string templateId = "";
        public int count = 1;
        public string requiredSlot = "";
        public string requirementTip = "";
    }

    public class ExtractZone : MarkerBase
    {
        public float radius = 1f;
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };
        public ZoneShape shape = ZoneShape.Box;
        public string exitName = "";
        public float exfiltrationTime = 5f;
        public string exfiltrationType = "Individual";
        public float spawnChance = 100f;
        public string side = "Pmc";
        public string passageRequirement = "None";
        public string requirementTip = "";
        public string requiredSlot = "FirstPrimaryWeapon";
        public int count = 0;
        public int playersCount = 0;
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";
        public List<ExtractZoneRequirement> requirements = new List<ExtractZoneRequirement>();
        public bool linkLights = false;
        public TriggerLightAction lightAction = TriggerLightAction.Toggle;
        public List<string> lightZoneNames = new List<string>();

        public override MarkerKind Kind => MarkerKind.ExtractZone;
    }

    public class BotSpawnGroup
    {
        public string id = "";
        public int spawnCount = 1;
        public BotSpawnPreset preset = BotSpawnPreset.Scav;
        public string wildSpawnType = "";
        public BotSpawnSide side = BotSpawnSide.Savage;
        public BotSpawnCategory category = BotSpawnCategory.Bot;
    }

    public class TriggerZone : MarkerBase
    {
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };
        public ZoneShape shape = ZoneShape.Sphere;
        public TriggerMode triggerMode = TriggerMode.OneTime;
        public float triggerChance = 100f;
        public float delaySeconds = 0f;
        public float cooldownSeconds = 0f;
        public float minRaidTime = 0f;
        public float maxRaidTime = 0f;
        public TriggerSide allowedSide = TriggerSide.Any;
        public TriggerLightAction lightAction = TriggerLightAction.Toggle;
        public List<string> lightZoneNames = new List<string>();
        public override MarkerKind Kind => MarkerKind.TriggerZone;
    }

    public class BotSpawnPoint : MarkerBase
    {
        public float radius = 1f;
        public BotSpawnSide side = BotSpawnSide.Savage;
        public BotSpawnCategory category = BotSpawnCategory.Bot;
        public BotSpawnPreset preset = BotSpawnPreset.Scav;
        public string wildSpawnType = "";
        public float spawnChance = 100f;
        public float delayToCanSpawnSec = 4f;
        public string botZoneName = "";
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";
        public string spawnMode = "Forced";
        public float botSpawnChance = 100f;
        public List<string> randomSpawnTypes = new();
        public bool triggerActivated = false;
        public string triggerZoneName = "";
        public bool forcePlayerSpawn = false;

        public override MarkerKind Kind => MarkerKind.BotSpawnPoint;
    }

    public class BotSpawnZone : MarkerBase
    {
        public float radius = 5f;
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };
        public ZoneShape shape = ZoneShape.Sphere;
        public BotSpawnSide side = BotSpawnSide.Savage;
        public BotSpawnCategory category = BotSpawnCategory.Bot;
        public BotSpawnPreset preset = BotSpawnPreset.Scav;
        public string wildSpawnType = "";
        public int spawnCount = 3;
        public float spawnChance = 100f;
        public float delayToCanSpawnSec = 4f;
        public string botZoneName = "";
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";
        public string spawnMode = "Forced";
        public float botSpawnChance = 100f;
        public List<string> randomSpawnTypes = new();
        public List<BotSpawnGroup> randomGroups = new();
        public bool triggerActivated = false;
        public string triggerZoneName = "";

        public override MarkerKind Kind => MarkerKind.BotSpawnZone;
    }

    public class PmcSpawnZone : MarkerBase
    {
        public float radius = 5f;
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };
        public ZoneShape shape = ZoneShape.Sphere;
        public BotSpawnSide side = BotSpawnSide.Pmc;
        public BotSpawnCategory category = BotSpawnCategory.BotPmc;
        public BotSpawnPreset preset = BotSpawnPreset.PMC;
        public string wildSpawnType = "pmcBot";
        public int minGroupSize = 1;
        public int maxGroupSize = 1;
        public float spawnChance = 100f;
        public float delayToCanSpawnSec = 4f;
        public string botZoneName = "";
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";
        public bool forcePlayerSpawn = false;

        public override MarkerKind Kind => MarkerKind.PmcSpawnZone;
    }

    public class LightColorData
    {
        public float r = 1f;
        public float g = 1f;
        public float b = 1f;
        public float a = 1f;

        public static LightColorData FromColor(Color c)
        {
            return new LightColorData { r = c.r, g = c.g, b = c.b, a = c.a };
        }

        public Color ToColor()
        {
            return new Color(r, g, b, a);
        }
    }

    public class LightZone : MarkerBase
    {
        public LightColorData color = new LightColorData { r = 1f, g = 1f, b = 1f, a = 1f };
        public float intensity = 1f;
        public float range = 10f;
        public float spotAngle = 30f;
        public string lightType = "Point";
        public bool enabled = true;
        public float spawnChance = 100f;
        public bool questOnly = false;
        public bool questCompleted = false;
        public string questId = "";

        public string shadows = "Soft";
        public float shadowStrength = 1f;
        public float shadowBias = 0.05f;
        public float shadowNormalBias = 0.4f;

        public override MarkerKind Kind => MarkerKind.LightZone;
    }

    public class OcclusionRepairVolume : MarkerBase
    {
        public TransformData scale = new TransformData { x = 10f, y = 10f, z = 10f };
        public ZoneShape shape = ZoneShape.Box;

        public bool disableCameraOcclusion = true;
        public bool manageRenderers = true;
        public float rendererRadius = 60f;
        public float maxVisibleDistance = 80f;
        public float checkInterval = 0.25f;
        public bool raycastCull = false;
        public string raycastMask = "Default"; // comma-separated layer names
        public bool disableCullingObjects = true;
        public float cullingObjectRadius = 60f;

        public override MarkerKind Kind => MarkerKind.OcclusionRepairVolume;
    }

    public class CutVolume : MarkerBase, IHasSourceObject
    {
        public string sourceObjectName { get; set; } = "";
        public TransformData sourceObjectPosition { get; set; } = new TransformData();
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };
        public ZoneShape shape = ZoneShape.Box;
        public bool invert = false; // keep geometry inside instead of removing it

        public override MarkerKind Kind => MarkerKind.CutVolume;
    }
}
