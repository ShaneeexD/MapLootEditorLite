using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Path = System.IO.Path;

namespace MapLootEditorLite.Server;

public static class WttStaticSpawnConverter
{
    public static void WriteStaticSpawns(List<PackData> packs, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            ServerPlugin.Logger?.Warning("[MLEL] WTT static spawn output directory is empty; skipping static spawn export.");
            return;
        }

        var normalized = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.GetFileName(normalized).Equals("CustomStaticSpawns", StringComparison.OrdinalIgnoreCase))
        {
            ServerPlugin.Logger?.Warning($"[MLEL] WTT static spawn output directory '{outputDirectory}' is not the expected CustomStaticSpawns folder; skipping static spawn export.");
            return;
        }

        var configsDir = Path.Combine(outputDirectory, "CustomSpawnConfigs");
        if (Directory.Exists(configsDir))
        {
            try
            {
                Directory.Delete(configsDir, true);
            }
            catch (Exception ex)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Failed to clean WTT static spawn config directory: {ex.Message}");
            }
        }

        Directory.CreateDirectory(configsDir);

        var configsByMap = new Dictionary<string, List<WttStaticConfig>>();

        foreach (var pack in packs)
        {
            foreach (var (packMapKey, map) in pack.Maps)
            {
                if (map.WttStaticObjects == null || map.WttStaticObjects.Count == 0)
                    continue;

                foreach (var wttMapId in ToWttMapIds(packMapKey))
                {
                    configsByMap.TryAdd(wttMapId, []);
                    foreach (var obj in map.WttStaticObjects.Where(o => o.SpawnType == "bundle"))
                    {
                        configsByMap[wttMapId].Add(CreateWttStaticConfig(obj, wttMapId));
                    }
                }
            }
        }

        foreach (var (mapId, configs) in configsByMap)
        {
            var filePath = Path.Combine(configsDir, $"{mapId}_static.json");
            var json = JsonSerializer.Serialize(configs, WttJsonOptions);
            File.WriteAllText(filePath, json);
        }

        ServerPlugin.Logger?.Info($"[MLEL] Wrote WTT static spawn configs for {configsByMap.Count} maps to {configsDir}");
    }

    private static WttStaticConfig CreateWttStaticConfig(WTTStaticObject obj, string locationId)
    {
        return new WttStaticConfig
        {
            QuestId = string.IsNullOrEmpty(obj.QuestId) ? null : obj.QuestId,
            LocationID = locationId,
            BundleName = obj.BundleName,
            PrefabName = obj.PrefabName,
            Position = new WttXyz { X = obj.Position.X, Y = obj.Position.Y, Z = obj.Position.Z },
            Rotation = new WttXyz { X = obj.Rotation.X, Y = obj.Rotation.Y, Z = obj.Rotation.Z },
            RequiredQuestStatuses = obj.RequiredQuestStatuses?.Count > 0 ? obj.RequiredQuestStatuses : null,
            ExcludedQuestStatuses = obj.ExcludedQuestStatuses?.Count > 0 ? obj.ExcludedQuestStatuses : null,
            QuestMustExist = obj.QuestMustExist ? true : null,
            LinkedQuestId = string.IsNullOrEmpty(obj.LinkedQuestId) ? null : obj.LinkedQuestId,
            LinkedRequiredStatuses = obj.LinkedRequiredStatuses?.Count > 0 ? obj.LinkedRequiredStatuses : null,
            LinkedExcludedStatuses = obj.LinkedExcludedStatuses?.Count > 0 ? obj.LinkedExcludedStatuses : null,
            LinkedQuestMustExist = obj.LinkedQuestMustExist,
            RequiredItemInInventory = string.IsNullOrEmpty(obj.RequiredItemInInventory) ? null : obj.RequiredItemInInventory,
            RequiredLevel = obj.RequiredLevel > 0 ? obj.RequiredLevel : null,
            RequiredFaction = string.IsNullOrEmpty(obj.RequiredFaction) ? null : obj.RequiredFaction,
            RequiredBossSpawned = string.IsNullOrEmpty(obj.RequiredBossSpawned) ? null : obj.RequiredBossSpawned
        };
    }

    private static List<string> ToWttMapIds(string packMapKey)
    {
        return WttMapIds.ToWttMapIds(packMapKey).ToList();
    }

    private static JsonSerializerOptions WttJsonOptions => new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null
    };
}

public record WttStaticConfig
{
    [JsonPropertyName("questId")]
    public string? QuestId { get; set; }

    [JsonPropertyName("locationID")]
    public string LocationID { get; set; } = string.Empty;

    [JsonPropertyName("bundleName")]
    public string BundleName { get; set; } = string.Empty;

    [JsonPropertyName("prefabName")]
    public string PrefabName { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public WttXyz Position { get; set; } = new();

    [JsonPropertyName("rotation")]
    public WttXyz Rotation { get; set; } = new();

    [JsonPropertyName("requiredQuestStatuses")]
    public List<string>? RequiredQuestStatuses { get; set; }

    [JsonPropertyName("excludedQuestStatuses")]
    public List<string>? ExcludedQuestStatuses { get; set; }

    [JsonPropertyName("questMustExist")]
    public bool? QuestMustExist { get; set; }

    [JsonPropertyName("linkedQuestId")]
    public string? LinkedQuestId { get; set; }

    [JsonPropertyName("linkedRequiredStatuses")]
    public List<string>? LinkedRequiredStatuses { get; set; }

    [JsonPropertyName("linkedExcludedStatuses")]
    public List<string>? LinkedExcludedStatuses { get; set; }

    [JsonPropertyName("linkedQuestMustExist")]
    public bool? LinkedQuestMustExist { get; set; }

    [JsonPropertyName("requiredItemInInventory")]
    public string? RequiredItemInInventory { get; set; }

    [JsonPropertyName("requiredLevel")]
    public int? RequiredLevel { get; set; }

    [JsonPropertyName("requiredFaction")]
    public string? RequiredFaction { get; set; }

    [JsonPropertyName("requiredBossSpawned")]
    public string? RequiredBossSpawned { get; set; }
}

public record WttXyz
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }
}
