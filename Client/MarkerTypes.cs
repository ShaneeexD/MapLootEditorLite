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
        InteractiveObject
    }

    public enum InteractiveObjectType
    {
        Door,
        Container
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ContainerLootMode
    {
        Default,
        Hybrid,
        Custom
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
    }

    public class PackData
    {
        public string name = "My Loot Pack";
        public string author = "";
        public string version = "1.0.0";
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
        public abstract MarkerKind Kind { get; }
    }

    public class LootItem
    {
        public string template = "";
        public float chance = 100f;
        public TransformData rotation = new TransformData();
        public bool randomRotation = true;
        public float yOffset = 0f;
    }

    public class LooseLootSpawn : MarkerBase
    {
        public List<string> itemTpls = new List<string>();
        public List<LootItem> items = new List<LootItem>();
        public float spawnChance = 100f;
        public bool respawnable = false;
        public bool forced = false;
        public bool useGravity = false;

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

        // Bundle mode (WTT CustomStaticSpawnService)
        public string bundleName = "";
        public string prefabName = "";

        // Clone mode (copy an existing scene object)
        public string sourceObjectName { get; set; } = "";
        public TransformData sourceObjectPosition { get; set; } = new TransformData();
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };

        // WTT CustomStaticSpawn conditions
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
        public ContainerLootMode lootMode = ContainerLootMode.Default;
        public List<LootItem> items = new List<LootItem>();
        public float spawnChance = 100f;

        public override MarkerKind Kind => MarkerKind.InteractiveObject;
    }
}
