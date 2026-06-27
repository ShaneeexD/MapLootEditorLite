using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using Path = System.IO.Path;

namespace MapLootEditorLite.Server;

public static class WttSpawnConverter
{
    public static void WriteForcedSpawns(List<PackData> packs, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            ServerPlugin.Logger?.Warning("[MLEL] WTT spawn output directory is empty; skipping forced spawn export.");
            return;
        }

        // Safety check: the output directory must be a concrete CustomLootspawns subfolder
        var normalized = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.GetFileName(normalized).Equals("CustomLootspawns", StringComparison.OrdinalIgnoreCase))
        {
            ServerPlugin.Logger?.Warning($"[MLEL] WTT spawn output directory '{outputDirectory}' is not the expected CustomLootspawns folder; skipping forced spawn export.");
            return;
        }

        var forcedDir = Path.Combine(outputDirectory, "CustomSpawnpointsForced");
        if (Directory.Exists(forcedDir))
        {
            try
            {
                Directory.Delete(forcedDir, true);
            }
            catch (Exception ex)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Failed to clean WTT forced spawn directory: {ex.Message}");
            }
        }

        Directory.CreateDirectory(forcedDir);

        var forcedByMap = new Dictionary<string, List<WttSpawnpoint>>();

        foreach (var pack in packs)
        {
            foreach (var (packMapKey, map) in pack.Maps)
            {
                foreach (var spawn in map.LootSpawns.Where(s => s.Forced))
                {
                    foreach (var wttMapId in ToWttMapIds(packMapKey))
                    {
                        forcedByMap.TryAdd(wttMapId, []);
                        forcedByMap[wttMapId].Add(CreateWttSpawnpoint(spawn));
                    }
                }

                foreach (var zone in map.LootZones.Where(z => z.Forced))
                {
                    foreach (var wttMapId in ToWttMapIds(packMapKey))
                    {
                        forcedByMap.TryAdd(wttMapId, []);
                        forcedByMap[wttMapId].Add(CreateWttSpawnpoint(zone));
                    }
                }
            }
        }

        foreach (var (mapId, spawns) in forcedByMap)
        {
            var filePath = Path.Combine(outputDirectory, "CustomSpawnpointsForced", $"{mapId}_forced.json");
            var json = JsonSerializer.Serialize(new Dictionary<string, List<WttSpawnpoint>> { [mapId] = spawns }, WttJsonOptions);
            File.WriteAllText(filePath, json);
        }

        ServerPlugin.Logger?.Info($"[MLEL] Wrote forced spawns for {forcedByMap.Count} maps to {outputDirectory}");
    }

    private static WttSpawnpoint CreateWttSpawnpoint(LooseLootSpawn spawn)
    {
        var itemTpl = spawn.ItemTpls.FirstOrDefault() ?? "544fb45d4bdc2dee738b4568";
        var rootId = new MongoId();
        var composedKey = itemTpl;

        return new WttSpawnpoint
        {
            LocationId = spawn.Id,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = spawn.Id,
                IsContainer = false,
                UseGravity = false,
                RandomRotation = false,
                Position = new XYZ { X = spawn.Position.X, Y = spawn.Position.Y, Z = spawn.Position.Z },
                Rotation = new XYZ { X = spawn.Rotation.X, Y = spawn.Rotation.Y, Z = spawn.Rotation.Z },
                IsAlwaysSpawn = true,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items =
                [
                    new SptLootItem
                    {
                        Id = rootId,
                        Template = itemTpl,
                        ComposedKey = composedKey,
                        Upd = new Upd { SpawnedInSession = true }
                    }
                ]
            },
            ItemDistribution =
            [
                new WttItemDistribution
                {
                    ComposedKey = new WttComposedKey { Key = composedKey },
                    RelativeProbability = 1
                }
            ]
        };
    }

    private static WttSpawnpoint CreateWttSpawnpoint(LootZone zone)
    {
        var itemTpl = zone.ItemTpls.FirstOrDefault() ?? "544fb45d4bdc2dee738b4568";
        var rootId = new MongoId();
        var composedKey = itemTpl;

        return new WttSpawnpoint
        {
            LocationId = zone.Id,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = zone.Id,
                IsContainer = false,
                UseGravity = false,
                RandomRotation = false,
                Position = new XYZ { X = zone.Position.X, Y = zone.Position.Y, Z = zone.Position.Z },
                Rotation = new XYZ { X = zone.Rotation.X, Y = zone.Rotation.Y, Z = zone.Rotation.Z },
                IsAlwaysSpawn = true,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items =
                [
                    new SptLootItem
                    {
                        Id = rootId,
                        Template = itemTpl,
                        ComposedKey = composedKey,
                        Upd = new Upd { SpawnedInSession = true }
                    }
                ]
            },
            ItemDistribution =
            [
                new WttItemDistribution
                {
                    ComposedKey = new WttComposedKey { Key = composedKey },
                    RelativeProbability = 1
                }
            ]
        };
    }

    private static IEnumerable<string> ToWttMapIds(string packMapKey)
    {
        var key = packMapKey.ToLowerInvariant();
        return key switch
        {
            "customs" or "bigmap" => ["bigmap"],
            "woods" => ["woods"],
            "factory" => ["factory4_day", "factory4_night"],
            "factory4_day" => ["factory4_day"],
            "factory4_night" => ["factory4_night"],
            "interchange" => ["interchange"],
            "lighthouse" => ["lighthouse"],
            "reserve" or "rezervbase" => ["rezervbase"],
            "shoreline" => ["shoreline"],
            "streets" or "tarkovstreets" => ["tarkovstreets"],
            "labs" or "laboratory" => ["laboratory"],
            "groundzero" or "sandbox" => ["sandbox"],
            _ => [key]
        };
    }

    private static readonly JsonSerializerOptions WttJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,
        Converters = { new MongoIdJsonConverter() }
    };

    private class MongoIdJsonConverter : JsonConverter<MongoId>
    {
        public override MongoId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                return string.IsNullOrEmpty(value) ? new MongoId() : new MongoId(value);
            }

            if (reader.TokenType == JsonTokenType.Null)
                return new MongoId();

            throw new JsonException($"Expected string for MongoId, got {reader.TokenType}");
        }

        public override void Write(Utf8JsonWriter writer, MongoId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    private class WttSpawnpoint
    {
        [JsonPropertyName("locationId")]
        public string LocationId { get; set; } = string.Empty;

        [JsonPropertyName("probability")]
        public double Probability { get; set; }

        [JsonPropertyName("template")]
        public SpawnpointTemplate Template { get; set; } = null!;

        [JsonPropertyName("itemDistribution")]
        public List<WttItemDistribution> ItemDistribution { get; set; } = [];
    }

    private class WttItemDistribution
    {
        [JsonPropertyName("composedKey")]
        public WttComposedKey ComposedKey { get; set; } = null!;

        [JsonPropertyName("relativeProbability")]
        public int RelativeProbability { get; set; }
    }

    private class WttComposedKey
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;
    }
}
