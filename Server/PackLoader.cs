using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MapLootEditorLite.Server;

public static class PackLoader
{
    private sealed record PackCandidate(string Path, int Priority, string FileIdentity, string LogicalIdentity, string ContentHash, PackData Pack);

    public static List<PackData> LoadPacks(string rootDirectory)
    {
        var candidates = new List<PackCandidate>();
        var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string sptRoot;
        try
        {
            sptRoot = Path.GetFullPath(Path.Combine(rootDirectory, "..", "..", ".."));
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MEL packs] Failed to resolve pack root: {ex.Message}");
            return [];
        }

        AddDirectory(Path.Combine(rootDirectory, "MapLoot"), 2, candidates, discoveredPaths);
        AddDirectory(Path.Combine(sptRoot, "user", "mods", "MapEditorLite", "exports"), 0, candidates, discoveredPaths);
        AddDirectory(Path.Combine(sptRoot, "user", "mods", "MapEditorLite", "packs"), 1, candidates, discoveredPaths);
        AddDirectory(Path.Combine(sptRoot, "BepInEx", "config", "MapEditorLite", "exports"), 3, candidates, discoveredPaths);
        AddDirectory(Path.Combine(sptRoot, "user", "mods", "MapLootEditorLite", "packs"), 4, candidates, discoveredPaths);
        AddDirectory(Path.Combine(sptRoot, "BepInEx", "config", "MapEditorLite", "spawns"), 5, candidates, discoveredPaths);

        var userMods = Path.Combine(sptRoot, "user", "mods");
        if (Directory.Exists(userMods))
        {
            try
            {
                foreach (var modDirectory in Directory.GetDirectories(userMods).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    AddDirectory(Path.Combine(modDirectory, "MapLoot"), 6, candidates, discoveredPaths);
            }
            catch (Exception ex)
            {
                ServerPlugin.Logger?.Warning($"[MEL packs] Could not discover legacy MapLoot directories: {ex.Message}");
            }
        }

        var selected = SelectCandidates(candidates);
        var merged = MergePacks(selected);
        if (merged == null)
        {
            ServerPlugin.Logger?.Warning("[MEL packs] No valid packs found; pack-driven server content will not be loaded.");
            return [];
        }
        return [merged];
    }

    private static void AddDirectory(string directory, int priority, List<PackCandidate> candidates, HashSet<string> discoveredPaths)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Warning($"[MEL packs] Could not scan '{directory}': {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(file);
            }
            catch (Exception ex)
            {
                ServerPlugin.Logger?.Warning($"[MEL packs] Could not resolve candidate '{file}': {ex.Message}");
                continue;
            }
            if (!discoveredPaths.Add(fullPath))
                continue;

            try
            {
                var json = File.ReadAllText(fullPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    ServerPlugin.Logger?.Info($"[MEL packs] Rejected non-pack JSON: {fullPath}");
                    continue;
                }

                var pack = JsonSerializer.Deserialize<PackData>(doc.RootElement.GetRawText());
                if (pack?.Maps == null)
                {
                    ServerPlugin.Logger?.Warning($"[MEL packs] Rejected invalid pack (missing maps object): {fullPath}");
                    continue;
                }

                pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? Path.GetFileNameWithoutExtension(fullPath) : pack.Name.Trim();
                MigrateLegacyItems(pack);
                var fileIdentity = Path.GetFileNameWithoutExtension(fullPath).Trim();
                candidates.Add(new PackCandidate(
                    fullPath,
                    priority,
                    fileIdentity,
                    pack.Name,
                    ComputeHash(json),
                    pack));
            }
            catch (Exception ex)
            {
                ServerPlugin.Logger?.Error($"[MEL packs] Rejected unreadable or malformed pack '{fullPath}': {ex.Message}");
            }
        }
    }

    private static List<PackCandidate> SelectCandidates(List<PackCandidate> candidates)
    {
        var selected = new List<PackCandidate>();
        var claimedIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contentHashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in candidates.OrderBy(item => item.Priority)
                     .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            var fileIdentity = "file:" + candidate.FileIdentity;
            var nameIdentity = "name:" + candidate.LogicalIdentity;
            if (claimedIdentities.Contains(fileIdentity) || claimedIdentities.Contains(nameIdentity))
            {
                ServerPlugin.Logger?.Info($"[MEL packs] Ignored duplicate logical pack '{candidate.LogicalIdentity}': {candidate.Path}; a higher-precedence valid candidate was selected.");
                continue;
            }
            if (!contentHashes.Add(candidate.ContentHash))
            {
                ServerPlugin.Logger?.Info($"[MEL packs] Ignored byte-identical duplicate: {candidate.Path}");
                claimedIdentities.Add(fileIdentity);
                claimedIdentities.Add(nameIdentity);
                continue;
            }

            claimedIdentities.Add(fileIdentity);
            claimedIdentities.Add(nameIdentity);
            selected.Add(candidate);
            ServerPlugin.Logger?.Info($"[MEL packs] Selected pack '{candidate.Pack.Name}'; source='{candidate.Path}', priority={candidate.Priority}, reason=highest-priority valid candidate.");
        }
        return selected;
    }

    private static PackData? MergePacks(List<PackCandidate> candidates)
    {
        if (candidates.Count == 0)
            return null;

        var result = new PackData
        {
            Name = "Resolved MEL packs",
            Version = "1.1.0"
        };
        var mapIdentities = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            foreach (var entry in candidate.Pack.Maps)
            {
                if (entry.Value == null)
                    continue;
                var identity = NormalizeMapIdentity(entry.Key);
                if (identity.Length == 0)
                    continue;

                if (!mapIdentities.TryGetValue(identity, out var destination))
                {
                    destination = new MapData { Map = string.IsNullOrWhiteSpace(entry.Value.Map) ? entry.Key : entry.Value.Map };
                    mapIdentities.Add(identity, destination);
                    result.Maps[entry.Key] = destination;
                }
                MergeMapData(destination, entry.Value);
            }
        }
        return result;
    }

    private static void MergeMapData(MapData destination, MapData source)
    {
        foreach (var property in typeof(MapData).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.Name == nameof(MapData.Map) || !property.CanRead || !property.CanWrite)
                continue;
            if (!(property.GetValue(destination) is IList target))
                continue;
            if (!(property.GetValue(source) is IList incoming))
                continue;

            foreach (var item in incoming)
            {
                if (item != null && !HasSameEntryId(target, item))
                    target.Add(item);
            }
        }
    }

    private static bool HasSameEntryId(IList entries, object candidate)
    {
        var identity = GetEntryIdentity(candidate);
        if (identity == null)
            return false;
        foreach (var existing in entries)
        {
            if (existing != null && string.Equals(GetEntryIdentity(existing), identity, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string? GetEntryIdentity(object entry)
    {
        if (entry is string text)
            return text;
        var identity = entry.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry) as string;
        return string.IsNullOrWhiteSpace(identity) ? null : identity;
    }

    private static void MigrateLegacyItems(PackData pack)
    {
        foreach (var map in pack.Maps.Values.Where(map => map != null))
        {
            EnsureLists(map);
            foreach (var spawn in map.LootSpawns)
            {
                if (spawn.Items.Count == 0 && spawn.ItemTpls.Count > 0)
                {
                    spawn.Items = spawn.ItemTpls.Select(template => new LootItem { Template = template, Chance = 100 }).ToList();
                    spawn.ItemTpls.Clear();
                }
            }

            foreach (var zone in map.LootZones)
            {
                if (zone.Items.Count == 0 && zone.ItemTpls.Count > 0)
                {
                    zone.Items = zone.ItemTpls.Select(template => new LootItem { Template = template, Chance = 100 }).ToList();
                    zone.ItemTpls.Clear();
                }
            }
        }
    }

    private static void EnsureLists(MapData map)
    {
        foreach (var property in typeof(MapData).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || !property.CanWrite || !property.PropertyType.IsGenericType
                || property.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
                continue;
            if (property.GetValue(map) == null)
                property.SetValue(map, Activator.CreateInstance(property.PropertyType));
        }
    }

    private static string NormalizeMapIdentity(string mapId)
    {
        return new string(mapId.Where(character => !char.IsWhiteSpace(character) && character != '_').ToArray()).ToLowerInvariant();
    }

    private static string ComputeHash(string content)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}
