using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MapLootEditorLite.Server;

public record PackData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("maps")]
    public Dictionary<string, MapData> Maps { get; set; } = [];
}

public record MapData
{
    [JsonPropertyName("map")]
    public string Map { get; set; } = string.Empty;

    [JsonPropertyName("lootSpawns")]
    public List<LooseLootSpawn> LootSpawns { get; set; } = [];

    [JsonPropertyName("lootZones")]
    public List<LootZone> LootZones { get; set; } = [];

    [JsonPropertyName("objects")]
    public List<StaticObject> Objects { get; set; } = [];

    [JsonPropertyName("wttStaticObjects")]
    public List<WTTStaticObject> WttStaticObjects { get; set; } = [];
}

public record LootItem
{
    [JsonPropertyName("template")]
    public string Template { get; set; } = string.Empty;

    [JsonPropertyName("chance")]
    public double Chance { get; set; } = 100;

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("randomRotation")]
    public bool RandomRotation { get; set; } = true;
}

public record LooseLootSpawn
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    // Kept for loading legacy packs; new packs use items
    [JsonPropertyName("itemTpls")]
    public List<string> ItemTpls { get; set; } = [];

    [JsonPropertyName("items")]
    public List<LootItem> Items { get; set; } = [];

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("respawnable")]
    public bool Respawnable { get; set; } = false;

    [JsonPropertyName("forced")]
    public bool Forced { get; set; } = false;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ZoneShape
{
    Sphere,
    Box,
    Cylinder,
    Capsule
}

public record LootZone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("radius")]
    public double Radius { get; set; } = 1;

    [JsonPropertyName("scale")]
    public TransformData Scale { get; set; } = new() { X = 1, Y = 1, Z = 1 };

    [JsonPropertyName("shape")]
    public ZoneShape Shape { get; set; } = ZoneShape.Sphere;

    // Kept for loading legacy packs; new packs use items
    [JsonPropertyName("itemTpls")]
    public List<string> ItemTpls { get; set; } = [];

    [JsonPropertyName("items")]
    public List<LootItem> Items { get; set; } = [];

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("forced")]
    public bool Forced { get; set; } = false;
}

public record StaticObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("scale")]
    public TransformData Scale { get; set; } = new();

    [JsonPropertyName("prefabPath")]
    public string PrefabPath { get; set; } = string.Empty;

    [JsonPropertyName("sourceObjectName")]
    public string SourceObjectName { get; set; } = string.Empty;

    [JsonPropertyName("sourceObjectPosition")]
    public TransformData SourceObjectPosition { get; set; } = new();
}

public record TransformData
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}

public record WTTStaticObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("scale")]
    public TransformData Scale { get; set; } = new() { X = 1, Y = 1, Z = 1 };

    [JsonPropertyName("spawnType")]
    public string SpawnType { get; set; } = "bundle";

    [JsonPropertyName("bundleName")]
    public string BundleName { get; set; } = string.Empty;

    [JsonPropertyName("prefabName")]
    public string PrefabName { get; set; } = string.Empty;

    [JsonPropertyName("sourceObjectName")]
    public string SourceObjectName { get; set; } = string.Empty;

    [JsonPropertyName("sourceObjectPosition")]
    public TransformData SourceObjectPosition { get; set; } = new();

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;

    [JsonPropertyName("requiredQuestStatuses")]
    public List<string> RequiredQuestStatuses { get; set; } = [];

    [JsonPropertyName("excludedQuestStatuses")]
    public List<string> ExcludedQuestStatuses { get; set; } = [];

    [JsonPropertyName("questMustExist")]
    public bool QuestMustExist { get; set; } = true;

    [JsonPropertyName("linkedQuestId")]
    public string LinkedQuestId { get; set; } = string.Empty;

    [JsonPropertyName("linkedRequiredStatuses")]
    public List<string> LinkedRequiredStatuses { get; set; } = [];

    [JsonPropertyName("linkedExcludedStatuses")]
    public List<string> LinkedExcludedStatuses { get; set; } = [];

    [JsonPropertyName("linkedQuestMustExist")]
    public bool? LinkedQuestMustExist { get; set; }

    [JsonPropertyName("requiredItemInInventory")]
    public string RequiredItemInInventory { get; set; } = string.Empty;

    [JsonPropertyName("requiredLevel")]
    public int RequiredLevel { get; set; }

    [JsonPropertyName("requiredFaction")]
    public string RequiredFaction { get; set; } = string.Empty;

    [JsonPropertyName("requiredBossSpawned")]
    public string RequiredBossSpawned { get; set; } = string.Empty;
}
