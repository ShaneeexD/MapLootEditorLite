using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MapLootEditorLite.Server;

public static class PackLoader
{
    public static List<PackData> LoadPacks(string rootDirectory)
    {
        var packs = new List<PackData>();
        var directories = new List<string>
        {
            Path.Combine(rootDirectory, "MapLoot"),
        };

        try
        {
            var sptRoot = Path.GetFullPath(Path.Combine(rootDirectory, "..", "..", ".."));

            // Final user packs for loading
            var serverModPacks = Path.Combine(sptRoot, "user", "mods", "MapEditorLite", "packs");
            if (Directory.Exists(serverModPacks))
            {
                directories.Add(serverModPacks);
            }

            // Legacy client-side export paths (kept for backwards compatibility)
            var clientExports = Path.Combine(sptRoot, "BepInEx", "config", "MapEditorLite", "exports");
            if (Directory.Exists(clientExports))
            {
                directories.Add(clientExports);
            }

            var clientSpawns = Path.Combine(sptRoot, "BepInEx", "config", "MapEditorLite", "spawns");
            if (Directory.Exists(clientSpawns))
            {
                directories.Add(clientSpawns);
            }

            // Legacy MapLootEditorLite paths (kept for backwards compatibility during rename)
            var legacyServerModPacks = Path.Combine(sptRoot, "user", "mods", "MapLootEditorLite", "packs");
            if (Directory.Exists(legacyServerModPacks))
            {
                directories.Add(legacyServerModPacks);
            }

            var legacyClientExports = Path.Combine(sptRoot, "BepInEx", "config", "MapLootEditorLite", "exports");
            if (Directory.Exists(legacyClientExports))
            {
                directories.Add(legacyClientExports);
            }

            var legacyClientSpawns = Path.Combine(sptRoot, "BepInEx", "config", "MapLootEditorLite", "spawns");
            if (Directory.Exists(legacyClientSpawns))
            {
                directories.Add(legacyClientSpawns);
            }

            // Also load MapLoot folders from the user/mods tree.
            var userModsPath = Path.Combine(sptRoot, "user", "mods");
            if (Directory.Exists(userModsPath))
            {
                foreach (var modDir in Directory.GetDirectories(userModsPath))
                {
                    var mapLootDir = Path.Combine(modDir, "MapLoot");
                    if (Directory.Exists(mapLootDir))
                    {
                        directories.Add(mapLootDir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MLEL] Failed to scan pack directories: {ex.Message}");
        }

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var pack = JsonSerializer.Deserialize<PackData>(json);
                    if (pack is null)
                    {
                        ServerPlugin.Logger?.Warning($"[MLEL] Failed to parse pack: {file}");
                        continue;
                    }

                    pack.Name = string.IsNullOrWhiteSpace(pack.Name) ? Path.GetFileNameWithoutExtension(file) : pack.Name;
                    MigrateLegacyItems(pack);
                    packs.Add(pack);
                    ServerPlugin.Logger?.Info($"[MLEL] Loaded pack '{pack.Name}' from {file}");
                }
                catch (Exception ex)
                {
                    ServerPlugin.Logger?.Error($"[MLEL] Failed to load pack {file}: {ex.Message}");
                }
            }
        }

        return packs;
    }

    private static void MigrateLegacyItems(PackData pack)
    {
        if (pack.Maps == null)
            return;

        foreach (var map in pack.Maps.Values)
        {
            foreach (var spawn in map.LootSpawns)
            {
                if (spawn.Items.Count == 0 && spawn.ItemTpls.Count > 0)
                {
                    spawn.Items = spawn.ItemTpls.Select(t => new LootItem { Template = t, Chance = 100 }).ToList();
                    spawn.ItemTpls.Clear();
                }
            }

            foreach (var zone in map.LootZones)
            {
                if (zone.Items.Count == 0 && zone.ItemTpls.Count > 0)
                {
                    zone.Items = zone.ItemTpls.Select(t => new LootItem { Template = t, Chance = 100 }).ToList();
                    zone.ItemTpls.Clear();
                }
            }
        }
    }
}
