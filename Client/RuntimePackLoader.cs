using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace MapLootEditorLite.Client
{
    internal static class RuntimePackLoader
    {
        private sealed class PackCandidate
        {
            public string Path;
            public int Priority;
            public string FileIdentity;
            public PackData Pack;
            public string ContentHash;
            public string LogicalIdentity;
        }

        public static List<PackData> LoadPacks(string systemName)
        {
            var candidates = new List<PackCandidate>();
            var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var root = GetServerRoot();

            AddDirectory(Plugin.ServerModExportsDirectory, 0, candidates, discoveredPaths, systemName);
            AddDirectory(Plugin.ServerModPacksDirectory, 1, candidates, discoveredPaths, systemName);
            AddDirectory(Path.Combine(Plugin.ServerModDirectory, "MapLoot"), 2, candidates, discoveredPaths, systemName);

            AddDirectory(Path.Combine(root, "BepInEx", "config", "MapEditorLite", "exports"), 3, candidates, discoveredPaths, systemName);
            AddDirectory(Path.Combine(root, "user", "mods", "MapLootEditorLite", "packs"), 4, candidates, discoveredPaths, systemName);
            AddDirectory(Path.Combine(root, "BepInEx", "config", "MapEditorLite", "spawns"), 5, candidates, discoveredPaths, systemName);

            var userMods = Path.Combine(root, "user", "mods");
            if (Directory.Exists(userMods))
            {
                try
                {
                    foreach (var modDirectory in Directory.GetDirectories(userMods).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                        AddDirectory(Path.Combine(modDirectory, "MapLoot"), 6, candidates, discoveredPaths, systemName);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[MEL packs] Could not discover legacy MapLoot directories for {systemName}: {ex.Message}");
                }
            }

            var selected = SelectCandidates(candidates, systemName);
            var merged = MergePacks(selected);
            if (merged == null)
            {
                Plugin.Log.LogWarning($"[MEL packs] No valid packs found for {systemName}; pack-driven runtime content will not be loaded.");
                return new List<PackData>();
            }
            return new List<PackData> { merged };
        }

        public static bool TryGetMapData(PackData pack, string mapId, out MapData mapData)
        {
            mapData = null;
            if (pack?.maps == null || string.IsNullOrWhiteSpace(mapId))
                return false;

            var identity = NormalizeMapIdentity(mapId);
            foreach (var entry in pack.maps)
            {
                if (NormalizeMapIdentity(entry.Key) != identity)
                    continue;

                mapData = entry.Value;
                return mapData != null;
            }
            return false;
        }

        public static string NormalizeMapIdentity(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
                return string.Empty;
            return new string(mapId.Where(character => !char.IsWhiteSpace(character) && character != '_').ToArray()).ToLowerInvariant();
        }

        private static string GetServerRoot()
        {
            var root = Plugin.ServerModDirectory;
            for (var i = 0; i < 3 && !string.IsNullOrEmpty(root); i++)
                root = Directory.GetParent(root)?.FullName;
            return root ?? Plugin.GameRoot;
        }

        private static void AddDirectory(string directory, int priority, List<PackCandidate> candidates, HashSet<string> discoveredPaths, string systemName)
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
                Plugin.Log.LogWarning($"[MEL packs] Could not scan {systemName} directory '{directory}': {ex.Message}");
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
                    Plugin.Log.LogWarning($"[MEL packs] Could not resolve {systemName} candidate '{file}': {ex.Message}");
                    continue;
                }
                if (!discoveredPaths.Add(fullPath))
                    continue;

                try
                {
                    var json = File.ReadAllText(fullPath);
                    if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    {
                        Plugin.Log.LogInfo($"[MEL packs] Rejected non-pack JSON for {systemName}: {fullPath}");
                        continue;
                    }

                    var pack = JsonConvert.DeserializeObject<PackData>(json, PackData.InvariantSettings);
                    if (pack?.maps == null)
                    {
                        Plugin.Log.LogWarning($"[MEL packs] Rejected invalid pack for {systemName} (missing maps object): {fullPath}");
                        continue;
                    }

                    var fileIdentity = Path.GetFileNameWithoutExtension(fullPath).Trim();
                    var logicalIdentity = string.IsNullOrWhiteSpace(pack.name) ? fileIdentity : pack.name.Trim();
                    candidates.Add(new PackCandidate
                    {
                        Path = fullPath,
                        Priority = priority,
                        FileIdentity = fileIdentity,
                        LogicalIdentity = logicalIdentity,
                        ContentHash = ComputeHash(json),
                        Pack = pack
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[MEL packs] Rejected unreadable or malformed pack for {systemName}: {fullPath}: {ex.Message}");
                }
            }
        }

        private static List<PackCandidate> SelectCandidates(List<PackCandidate> candidates, string systemName)
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
                    Plugin.Log.LogInfo($"[MEL packs] Ignored duplicate logical pack '{candidate.LogicalIdentity}' for {systemName}: {candidate.Path}; a higher-precedence candidate was selected.");
                    continue;
                }

                if (!contentHashes.Add(candidate.ContentHash))
                {
                    Plugin.Log.LogInfo($"[MEL packs] Ignored byte-identical duplicate for {systemName}: {candidate.Path}");
                    claimedIdentities.Add(fileIdentity);
                    claimedIdentities.Add(nameIdentity);
                    continue;
                }

                claimedIdentities.Add(fileIdentity);
                claimedIdentities.Add(nameIdentity);
                selected.Add(candidate);
                Plugin.Log.LogInfo($"[MEL packs] Selected pack '{candidate.Pack.name}' for {systemName}; source='{candidate.Path}', priority={candidate.Priority}, reason=highest-priority valid candidate.");
            }
            return selected;
        }

        private static PackData MergePacks(List<PackCandidate> candidates)
        {
            if (candidates.Count == 0)
                return null;

            var result = new PackData
            {
                name = "Resolved MEL packs",
                author = "",
                version = "1.1.0"
            };
            var mapIdentities = new Dictionary<string, MapData>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                MergeExtensionData(result.extensionData, candidate.Pack.extensionData);
                foreach (var entry in candidate.Pack.maps)
                {
                    if (entry.Value == null)
                        continue;
                    var identity = NormalizeMapIdentity(entry.Key);
                    if (identity.Length == 0)
                        continue;

                    if (!mapIdentities.TryGetValue(identity, out var destination))
                    {
                        destination = new MapData { map = string.IsNullOrWhiteSpace(entry.Value.map) ? entry.Key : entry.Value.map };
                        mapIdentities.Add(identity, destination);
                        result.maps[entry.Key] = destination;
                    }
                    MergeMapData(destination, entry.Value);
                }
            }
            return result;
        }

        private static void MergeMapData(MapData destination, MapData source)
        {
            MergeExtensionData(destination.extensionData, source.extensionData);
            foreach (var field in typeof(MapData).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Name == "map" || field.Name == "extensionData")
                    continue;
                if (!(field.GetValue(destination) is IList target) || !(field.GetValue(source) is IList incoming))
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

        private static string GetEntryIdentity(object entry)
        {
            if (entry is string text)
                return text;
            var type = entry.GetType();
            var idField = type.GetField("id", BindingFlags.Instance | BindingFlags.Public);
            var value = idField != null
                ? idField.GetValue(entry) as string
                : type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)?.GetValue(entry, null) as string;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static void MergeExtensionData(IDictionary<string, Newtonsoft.Json.Linq.JToken> destination,
            IDictionary<string, Newtonsoft.Json.Linq.JToken> source)
        {
            if (destination == null || source == null)
                return;
            foreach (var entry in source)
            {
                if (!destination.ContainsKey(entry.Key))
                    destination.Add(entry.Key, entry.Value);
            }
        }

        private static string ComputeHash(string content)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
                return Convert.ToBase64String(bytes);
            }
        }
    }
}
