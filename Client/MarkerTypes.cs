using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public enum MarkerKind
    {
        LooseLoot,
        LootZone,
        StaticObject
    }

    public class MapData
    {
        public string map;
        public List<LooseLootSpawn> lootSpawns = new List<LooseLootSpawn>();
        public List<LootZone> lootZones = new List<LootZone>();
        public List<StaticObject> objects = new List<StaticObject>();
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
        public TransformData position = new TransformData();
        public TransformData rotation = new TransformData();

        [JsonIgnore]
        public abstract MarkerKind Kind { get; }
    }

    public class LooseLootSpawn : MarkerBase
    {
        public List<string> itemTpls = new List<string>();
        public float spawnChance = 100f;
        public bool respawnable = false;
        public bool forced = false;

        public override MarkerKind Kind => MarkerKind.LooseLoot;
    }

    public class LootZone : MarkerBase
    {
        public float radius = 1f;
        public List<string> itemTpls = new List<string>();
        public float spawnChance = 100f;
        public bool forced = false;

        public override MarkerKind Kind => MarkerKind.LootZone;
    }

    public class StaticObject : MarkerBase
    {
        public string prefabPath = "";
        public TransformData scale = new TransformData { x = 1f, y = 1f, z = 1f };

        public override MarkerKind Kind => MarkerKind.StaticObject;
    }
}
