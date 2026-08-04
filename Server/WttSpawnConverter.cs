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
            ServerPlugin.Logger?.Warning("[MEL] WTT spawn output directory is empty; skipping forced spawn export.");
            return;
        }

        // Safety check: the output directory must be a concrete CustomLootspawns subfolder
        var normalized = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.GetFileName(normalized).Equals("CustomLootspawns", StringComparison.OrdinalIgnoreCase))
        {
            ServerPlugin.Logger?.Warning($"[MEL] WTT spawn output directory '{outputDirectory}' is not the expected CustomLootspawns folder; skipping forced spawn export.");
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
                ServerPlugin.Logger?.Warning($"[MEL] Failed to clean WTT forced spawn directory: {ex.Message}");
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
                        if (zone.Items == null || zone.Items.Count == 0)
                        {
                            forcedByMap[wttMapId].Add(CreateWttSpawnpoint(zone));
                            continue;
                        }

                        for (int i = 0; i < zone.Items.Count; i++)
                        {
                            forcedByMap[wttMapId].Add(CreateWttZoneItemSpawnpoint(zone, zone.Items[i], i));
                        }
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

        ServerPlugin.Logger?.Info($"[MEL] Wrote forced spawns for {forcedByMap.Count} maps to {outputDirectory}");
    }

    private static WttSpawnpoint CreateWttSpawnpoint(LooseLootSpawn spawn)
    {
        var items = BuildWttItems(spawn.Items, out var chances);
        var rootItems = items.Where(i => string.IsNullOrEmpty(i.ParentId)).ToList();
        var rootId = rootItems.FirstOrDefault()?.Id ?? items.FirstOrDefault()?.Id ?? new MongoId();

        return new WttSpawnpoint
        {
            LocationId = spawn.Id,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = spawn.Id,
                IsContainer = false,
                UseGravity = spawn.UseGravity,
                RandomRotation = false,
                Position = new Vector3 { X = (float)spawn.Position.X, Y = (float)spawn.Position.Y, Z = (float)spawn.Position.Z },
                Rotation = new Vector3 { X = (float)spawn.Rotation.X, Y = (float)spawn.Rotation.Y, Z = (float)spawn.Rotation.Z },
                IsAlwaysSpawn = true,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items = items
            },
            ItemDistribution = BuildWttItemDistribution(rootItems, chances)
        };
    }

    private static WttSpawnpoint CreateWttSpawnpoint(LootZone zone)
    {
        var items = BuildWttItems(zone.Items, out var chances);
        var rootItems = items.Where(i => string.IsNullOrEmpty(i.ParentId)).ToList();
        var rootId = rootItems.FirstOrDefault()?.Id ?? items.FirstOrDefault()?.Id ?? new MongoId();

        return new WttSpawnpoint
        {
            LocationId = zone.Id,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = zone.Id,
                IsContainer = false,
                UseGravity = zone.UseGravity,
                RandomRotation = false,
                Position = new Vector3 { X = (float)zone.Position.X, Y = (float)zone.Position.Y, Z = (float)zone.Position.Z },
                Rotation = new Vector3 { X = (float)zone.Rotation.X, Y = (float)zone.Rotation.Y, Z = (float)zone.Rotation.Z },
                IsAlwaysSpawn = true,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items = items
            },
            ItemDistribution = BuildWttItemDistribution(rootItems, chances)
        };
    }

    private static WttSpawnpoint CreateWttZoneItemSpawnpoint(LootZone zone, LootItem item, int index)
    {
        var items = BuildWttItems([item], out var chances);
        var rootId = items.FirstOrDefault()?.Id ?? new MongoId();
        var chance = chances.Count > 0 ? chances[0] : 1;
        var rotation = item.RandomRotation ? RandomYRotation() : item.Rotation;
        var locationId = $"{zone.Id}_{index}";
        var position = RandomPointInShape(zone);
        var composedKey = items.FirstOrDefault()?.ComposedKey ?? string.Empty;

        return new WttSpawnpoint
        {
            LocationId = locationId,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = locationId,
                IsContainer = false,
                UseGravity = zone.UseGravity,
                RandomRotation = false,
                Position = new Vector3 { X = (float)position.X, Y = (float)position.Y, Z = (float)position.Z },
                Rotation = new Vector3 { X = (float)rotation.X, Y = (float)rotation.Y, Z = (float)rotation.Z },
                IsAlwaysSpawn = true,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items = items
            },
            ItemDistribution =
            [
                new WttItemDistribution
                {
                    ComposedKey = new WttComposedKey { Key = composedKey },
                    RelativeProbability = chance
                }
            ]
        };
    }

    private static List<SptLootItem> BuildWttItems(List<LootItem> items, out List<int> chances)
    {
        chances = new List<int>();
        if (items == null || items.Count == 0)
        {
            chances.Add(100);
            return
            [
                new SptLootItem
                {
                    Id = new MongoId(),
                    Template = new MongoId("544fb45d4bdc2dee738b4568"),
                    ComposedKey = "544fb45d4bdc2dee738b4568",
                    Upd = new Upd { SpawnedInSession = true }
                }
            ];
        }

        var result = new List<SptLootItem>();
        for (int i = 0; i < items.Count; i++)
            AddLootItemRecursive(items[i], null, result, chances, i);

        return result;
    }

    private static void AddLootItemRecursive(LootItem item, SptLootItem? parent, List<SptLootItem> result, List<int> chances, int index)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Template))
            return;

        var id = new MongoId();
        var spt = new SptLootItem
        {
            Id = id,
            Template = item.Template,
            ParentId = parent == null ? null : parent.Id.ToString(),
            SlotId = item.SlotId ?? string.Empty,
            ComposedKey = $"{item.Template}_{index}_{result.Count}",
            Upd = new Upd { SpawnedInSession = true }
        };

        if (parent == null)
            chances.Add((int)item.Chance);

        result.Add(spt);

        if (item.Children != null)
        {
            foreach (var child in item.Children)
                AddLootItemRecursive(child, spt, result, chances, index);
        }
    }

    private static List<WttItemDistribution> BuildWttItemDistribution(List<SptLootItem> items, List<int> chances)
    {
        var distribution = new List<WttItemDistribution>();
        for (int i = 0; i < items.Count; i++)
        {
            var chance = i < chances.Count ? chances[i] : 1;
            distribution.Add(new WttItemDistribution
            {
                ComposedKey = new WttComposedKey { Key = items[i].ComposedKey ?? string.Empty },
                RelativeProbability = chance > 0 ? chance : 1
            });
        }
        return distribution;
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

    private static TransformData RandomPointInShape(LootZone zone)
    {
        var scale = zone.Scale;
        if (scale == null || (scale.X == 0 && scale.Y == 0 && scale.Z == 0))
            scale = new TransformData { X = 1, Y = 1, Z = 1 };

        var angle = Random.Shared.NextDouble() * Math.PI * 2;
        var radius = zone.Radius * scale.X;

        switch (zone.Shape)
        {
            case ZoneShape.Box:
                return new TransformData
                {
                    X = zone.Position.X + (Random.Shared.NextDouble() - 0.5) * scale.X,
                    Y = zone.Position.Y,
                    Z = zone.Position.Z + (Random.Shared.NextDouble() - 0.5) * scale.Z
                };
            case ZoneShape.Cylinder:
            case ZoneShape.Capsule:
                var cylR = radius * Math.Sqrt(Random.Shared.NextDouble());
                return new TransformData
                {
                    X = zone.Position.X + cylR * Math.Cos(angle),
                    Y = zone.Position.Y,
                    Z = zone.Position.Z + cylR * Math.Sin(angle)
                };
            default:
                var sphereR = radius * Math.Sqrt(Random.Shared.NextDouble());
                return new TransformData
                {
                    X = zone.Position.X + sphereR * Math.Cos(angle),
                    Y = zone.Position.Y,
                    Z = zone.Position.Z + sphereR * Math.Sin(angle)
                };
        }
    }

    private static TransformData RandomYRotation()
    {
        return new TransformData
        {
            X = 0,
            Y = Random.Shared.NextDouble() * 360,
            Z = 0
        };
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
