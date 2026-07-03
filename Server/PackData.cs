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

    [JsonPropertyName("interactiveObjects")]
    public List<InteractiveObject> InteractiveObjects { get; set; } = [];

    [JsonPropertyName("extractZones")]
    public List<ExtractZone> ExtractZones { get; set; } = [];

    [JsonPropertyName("botSpawnPoints")]
    public List<BotSpawnPoint> BotSpawnPoints { get; set; } = [];

    [JsonPropertyName("botSpawnZones")]
    public List<BotSpawnZone> BotSpawnZones { get; set; } = [];

    [JsonPropertyName("lightZones")]
    public List<LightZone> LightZones { get; set; } = [];
}

public record ExtractZoneRequirement
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "None";

    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("requiredSlot")]
    public string RequiredSlot { get; set; } = string.Empty;

    [JsonPropertyName("requirementTip")]
    public string RequirementTip { get; set; } = string.Empty;
}

public record ExtractZone
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

    [JsonPropertyName("radius")]
    public double Radius { get; set; } = 1;

    [JsonPropertyName("shape")]
    public ZoneShape Shape { get; set; } = ZoneShape.Box;

    [JsonPropertyName("exitName")]
    public string ExitName { get; set; } = string.Empty;

    [JsonPropertyName("exfiltrationTime")]
    public double ExfiltrationTime { get; set; } = 5;

    [JsonPropertyName("exfiltrationType")]
    public string ExfiltrationType { get; set; } = "Individual";

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;

    [JsonPropertyName("requirements")]
    public List<ExtractZoneRequirement> Requirements { get; set; } = [];
}

public record BotSpawnPoint
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

    [JsonPropertyName("side")]
    public BotSpawnSide Side { get; set; } = BotSpawnSide.Savage;

    [JsonPropertyName("category")]
    public BotSpawnCategory Category { get; set; } = BotSpawnCategory.Bot;

    [JsonPropertyName("preset")]
    public BotSpawnPreset Preset { get; set; } = BotSpawnPreset.Scav;

    [JsonPropertyName("wildSpawnType")]
    public string WildSpawnType { get; set; } = string.Empty;

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("delayToCanSpawnSec")]
    public double DelayToCanSpawnSec { get; set; } = 4;

    [JsonPropertyName("botZoneName")]
    public string BotZoneName { get; set; } = string.Empty;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
}

public record BotSpawnZone
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
    public double Radius { get; set; } = 5;

    [JsonPropertyName("scale")]
    public TransformData Scale { get; set; } = new() { X = 1, Y = 1, Z = 1 };

    [JsonPropertyName("shape")]
    public ZoneShape Shape { get; set; } = ZoneShape.Sphere;

    [JsonPropertyName("side")]
    public BotSpawnSide Side { get; set; } = BotSpawnSide.Savage;

    [JsonPropertyName("category")]
    public BotSpawnCategory Category { get; set; } = BotSpawnCategory.Bot;

    [JsonPropertyName("preset")]
    public BotSpawnPreset Preset { get; set; } = BotSpawnPreset.Scav;

    [JsonPropertyName("wildSpawnType")]
    public string WildSpawnType { get; set; } = string.Empty;

    [JsonPropertyName("spawnCount")]
    public int SpawnCount { get; set; } = 3;

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("delayToCanSpawnSec")]
    public double DelayToCanSpawnSec { get; set; } = 4;

    [JsonPropertyName("botZoneName")]
    public string BotZoneName { get; set; } = string.Empty;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
}

public record LightColorData
{
    [JsonPropertyName("r")]
    public double R { get; set; } = 1;

    [JsonPropertyName("g")]
    public double G { get; set; } = 1;

    [JsonPropertyName("b")]
    public double B { get; set; } = 1;

    [JsonPropertyName("a")]
    public double A { get; set; } = 1;
}

public record LightZone
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

    [JsonPropertyName("color")]
    public LightColorData Color { get; set; } = new() { R = 1, G = 1, B = 1, A = 1 };

    [JsonPropertyName("intensity")]
    public double Intensity { get; set; } = 1;

    [JsonPropertyName("range")]
    public double Range { get; set; } = 10;

    [JsonPropertyName("spotAngle")]
    public double SpotAngle { get; set; } = 30;

    [JsonPropertyName("lightType")]
    public string LightType { get; set; } = "Point";

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
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

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
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

    [JsonPropertyName("useGravity")]
    public bool UseGravity { get; set; } = false;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
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

    [JsonPropertyName("useGravity")]
    public bool UseGravity { get; set; } = false;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
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

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
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

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("requiredItemInInventory")]
    public string RequiredItemInInventory { get; set; } = string.Empty;

    [JsonPropertyName("requiredLevel")]
    public int RequiredLevel { get; set; }

    [JsonPropertyName("requiredFaction")]
    public string RequiredFaction { get; set; } = string.Empty;

    [JsonPropertyName("requiredBossSpawned")]
    public string RequiredBossSpawned { get; set; } = string.Empty;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InteractiveObjectType
{
    Door,
    Container
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BotSpawnSide
{
    Savage,
    Bear,
    Usec,
    Pmc,
    All
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BotSpawnCategory
{
    Bot,
    Boss,
    BotPmc,
    All
}

[JsonConverter(typeof(JsonStringEnumConverter))]
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContainerLootMode
{
    Default,
    Hybrid,
    Custom
}

public record InteractiveObject
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

    [JsonPropertyName("interactiveType")]
    public InteractiveObjectType InteractiveType { get; set; } = InteractiveObjectType.Door;

    [JsonPropertyName("sourceObjectName")]
    public string SourceObjectName { get; set; } = string.Empty;

    [JsonPropertyName("sourceObjectPosition")]
    public TransformData SourceObjectPosition { get; set; } = new();

    [JsonPropertyName("keyId")]
    public string KeyId { get; set; } = string.Empty;

    [JsonPropertyName("containerId")]
    public string ContainerId { get; set; } = string.Empty;

    [JsonPropertyName("containerTemplate")]
    public string ContainerTemplate { get; set; } = "578f87a3245977356274f2cb";

    [JsonPropertyName("lootMode")]
    public ContainerLootMode LootMode { get; set; } = ContainerLootMode.Default;

    [JsonPropertyName("items")]
    public List<LootItem> Items { get; set; } = [];

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("questOnly")]
    public bool QuestOnly { get; set; } = false;

    [JsonPropertyName("questCompleted")]
    public bool QuestCompleted { get; set; } = false;

    [JsonPropertyName("questId")]
    public string QuestId { get; set; } = string.Empty;
}
