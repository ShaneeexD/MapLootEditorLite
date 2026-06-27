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
}

public record LooseLootSpawn
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("itemTpls")]
    public List<string> ItemTpls { get; set; } = [];

    [JsonPropertyName("spawnChance")]
    public double SpawnChance { get; set; } = 100;

    [JsonPropertyName("respawnable")]
    public bool Respawnable { get; set; } = false;

    [JsonPropertyName("forced")]
    public bool Forced { get; set; } = false;
}

public record LootZone
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("radius")]
    public double Radius { get; set; } = 1;

    [JsonPropertyName("itemTpls")]
    public List<string> ItemTpls { get; set; } = [];

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

    [JsonPropertyName("position")]
    public TransformData Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public TransformData Rotation { get; set; } = new();

    [JsonPropertyName("scale")]
    public TransformData Scale { get; set; } = new();

    [JsonPropertyName("prefabPath")]
    public string PrefabPath { get; set; } = string.Empty;
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
