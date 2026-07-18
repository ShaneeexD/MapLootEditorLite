using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public static class VanillaImporter
    {
        public static string LocationsDirectory
        {
            get
            {
                var candidates = new[]
                {
                    Path.Combine(Plugin.GameRoot, "SPT_Data", "database", "locations"),
                    Path.Combine(Plugin.GameRoot, "SPT", "SPT_Data", "database", "locations"),
                };

                foreach (var candidate in candidates)
                {
                    if (Directory.Exists(candidate))
                    {
                        Plugin.Log.LogInfo($"[VanillaImporter] Using locations database: {candidate}");
                        return candidate;
                    }
                }

                // Last resort: search one level down for any SPT_Data subfolder.
                try
                {
                    if (Directory.Exists(Plugin.GameRoot))
                    {
                        foreach (var sub in Directory.GetDirectories(Plugin.GameRoot))
                        {
                            var candidate = Path.Combine(sub, "SPT_Data", "database", "locations");
                            if (Directory.Exists(candidate))
                            {
                                Plugin.Log.LogInfo($"[VanillaImporter] Found locations database in nested folder: {candidate}");
                                return candidate;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[VanillaImporter] Failed to search nested folders: {ex.Message}");
                }

                return candidates[0];
            }
        }

        public static MapData Import(string mapId)
        {
            var data = new MapData { map = mapId };
            var locationsDir = LocationsDirectory;
            var dir = Path.Combine(locationsDir, mapId.ToLowerInvariant());

            Plugin.Log.LogInfo($"[VanillaImporter] GameRoot={Plugin.GameRoot}, LocationsDirectory={locationsDir}, Exists={Directory.Exists(locationsDir)}");
            Plugin.Log.LogInfo($"[VanillaImporter] Importing map '{mapId}' from {dir}, Exists={Directory.Exists(dir)}");

            ImportLooseLoot(data, dir);
            ImportStaticContainers(data, dir);
            ImportVanillaSpawnsAndExtracts(data, dir);

            MarkVanilla(data);
            ClearContainerCache();

            Plugin.Log.LogInfo($"Imported vanilla data for {mapId}: {data.lootSpawns.Count} loose loot, {data.interactiveObjects.Count} containers, {data.botSpawnPoints.Count} PMC spawns, {data.extractZones.Count} extracts.");
            return data;
        }

        private static void MarkVanilla(MapData data)
        {
            foreach (var marker in data.lootSpawns) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.lootZones) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.objects) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.wttQuestZones) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.wttStaticObjects) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.interactiveObjects) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.extractZones) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.botSpawnPoints) { marker.isVanilla = true; marker.group = "Vanilla"; }
            foreach (var marker in data.botSpawnZones) { marker.isVanilla = true; marker.group = "Vanilla"; }
        }

        private static void ImportLooseLoot(MapData data, string dir)
        {
            var path = Path.Combine(dir, "looseLoot.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"[VanillaImporter] looseLoot.json not found: {path}");
                LogDirectoryContents(dir);
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<VanillaLootFile>(json);
                if (file == null)
                {
                    Plugin.Log.LogWarning($"[VanillaImporter] Failed to deserialize {path}");
                    return;
                }

                if (file.spawnpointsForced != null)
                    ImportLooseSpawnpointList(data, file.spawnpointsForced);
                if (file.spawnpoints != null)
                    ImportLooseSpawnpointList(data, file.spawnpoints);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to import vanilla loose loot for {dir}: {ex.Message}");
            }
        }

        private static void ImportLooseSpawnpointList(MapData data, List<VanillaSpawnpoint> list)
        {
            if (list == null)
                return;

            foreach (var sp in list)
            {
                if (sp?.template == null)
                    continue;

                var marker = new LooseLootSpawn
                    {
                        id = $"vanilla_loot_{sp.template.Id}",
                        name = sp.template.Id,
                        position = FromVectorData(sp.template.Position),
                        rotation = FromVectorData(sp.template.Rotation),
                        spawnChance = sp.probability * 100f,
                        forced = sp.template.IsAlwaysSpawn,
                        useGravity = sp.template.useGravity,
                        items = new List<LootItem>()
                    };

                    if (sp.template.Items != null)
                    {
                        foreach (var item in sp.template.Items)
                        {
                            if (string.IsNullOrEmpty(item?._tpl))
                                continue;
                            marker.items.Add(new LootItem
                            {
                                template = item._tpl,
                                chance = 100f,
                                randomRotation = true,
                                count = Math.Max(item.upd?.StackObjectsCount ?? 1, 1)
                            });
                        }
                    }

                    data.lootSpawns.Add(marker);
                }
            }

        private static void LogDirectoryContents(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Plugin.Log.LogInfo($"[VanillaImporter] Files in {dir}: {string.Join(", ", Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(f => f))}");
                else if (Directory.Exists(Path.GetDirectoryName(dir)))
                    Plugin.Log.LogInfo($"[VanillaImporter] Contents of parent {Path.GetDirectoryName(dir)}: {string.Join(", ", Directory.GetDirectories(Path.GetDirectoryName(dir)).Select(Path.GetFileName).OrderBy(d => d))}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[VanillaImporter] Failed to list directory contents for {dir}: {ex.Message}");
            }
        }

        private static void ImportStaticContainers(MapData data, string dir)
        {
            var path = Path.Combine(dir, "staticContainers.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"[VanillaImporter] staticContainers.json not found: {path}");
                LogDirectoryContents(dir);
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<VanillaContainerFile>(json);
                if (file == null)
                {
                    Plugin.Log.LogWarning($"[VanillaImporter] Failed to deserialize {path}");
                    return;
                }

                BuildContainerCache();
                if (file.staticContainers != null)
                    ImportStaticContainerList(data, file.staticContainers, dir);
                if (file.staticForced != null)
                    ImportStaticContainerList(data, file.staticForced, dir);
                if (file.staticWeapons != null)
                    ImportStaticContainerList(data, file.staticWeapons, dir);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to import vanilla static containers for {dir}: {ex.Message}");
            }
        }

        private static Dictionary<string, LootableContainer> _containerCache;
        private static Dictionary<string, List<LootItem>> _staticLootCache;
        private static Dictionary<string, (int min, int max)> _staticLootCountCache;

        private static void BuildContainerCache()
        {
            _containerCache = new Dictionary<string, LootableContainer>(StringComparer.OrdinalIgnoreCase);
            var containers = Resources.FindObjectsOfTypeAll<LootableContainer>();
            foreach (var container in containers)
            {
                if (container == null)
                    continue;
                var id = container.Id;
                if (string.IsNullOrEmpty(id))
                    continue;
                if (!_containerCache.ContainsKey(id))
                    _containerCache[id] = container;
            }
        }

        private static void ClearContainerCache()
        {
            _containerCache = null;
            _staticLootCache = null;
            _staticLootCountCache = null;
        }

        private static LootableContainer FindContainer(string id)
        {
            if (_containerCache == null)
                BuildContainerCache();
            if (id != null && _containerCache.TryGetValue(id, out var container))
                return container;
            return null;
        }

        private static void ImportStaticContainerList(MapData data, List<VanillaSpawnpoint> list, string dir)
        {
            if (list == null)
                return;

            foreach (var sp in list)
            {
                if (sp?.template == null)
                    continue;

                var root = sp.template.Items?.FirstOrDefault(i => string.IsNullOrEmpty(i.parentId));
                var containerTpl = root?._tpl ?? sp.template.Root ?? "578f87a3245977356274f2cb";

                var sceneContainer = FindContainer(sp.template.Id);
                var scenePos = sceneContainer != null ? sceneContainer.transform.position : (Vector3?)null;
                var sceneRot = sceneContainer != null ? sceneContainer.transform.eulerAngles : (Vector3?)null;

                var marker = new InteractiveObject
                {
                    id = $"vanilla_container_{sp.template.Id}",
                    name = sp.template.Id,
                    position = scenePos.HasValue ? FromVector3(scenePos.Value) : FromVectorData(sp.template.Position),
                    rotation = sceneRot.HasValue ? FromVector3(sceneRot.Value) : FromVectorData(sp.template.Rotation),
                    interactiveType = InteractiveObjectType.Container,
                    containerTemplate = containerTpl,
                    containerId = root?._id ?? "",
                    spawnChance = sp.probability * 100f,
                    lootMode = ContainerLootMode.Default,
                    items = new List<LootItem>()
                };

                // Use the static data items if they are real forced loot (not just the container root).
                if (sp.template.Items != null)
                {
                    foreach (var item in sp.template.Items)
                    {
                        if (string.IsNullOrEmpty(item?._tpl))
                            continue;
                        if (string.IsNullOrEmpty(item.parentId))
                            continue; // container root item is not loot
                        marker.items.Add(new LootItem
                        {
                            template = item._tpl,
                            chance = 100f,
                            randomRotation = true,
                            count = Math.Max(item.upd?.StackObjectsCount ?? 1, 1)
                        });
                    }
                }

                // Otherwise fall back to the vanilla static loot distribution for this container type.
                if (marker.items.Count == 0)
                    AddStaticLootDistribution(marker, containerTpl, dir);

                data.interactiveObjects.Add(marker);
            }
        }

        private static void AddStaticLootDistribution(InteractiveObject marker, string containerTpl, string dir)
        {
            if (_staticLootCache == null)
                LoadStaticLootCache(dir);

            if (_staticLootCache == null || !_staticLootCache.TryGetValue(containerTpl, out var distribution))
                return;

            marker.items.AddRange(distribution);

            if (_staticLootCountCache != null && _staticLootCountCache.TryGetValue(containerTpl, out var counts))
            {
                marker.itemCountMin = counts.min;
                marker.itemCountMax = counts.max;
            }
        }

        private static void LoadStaticLootCache(string dir)
        {
            _staticLootCache = new Dictionary<string, List<LootItem>>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(dir, "staticLoot.json");
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<Dictionary<string, StaticLootEntry>>(json);
                if (file == null)
                    return;

                _staticLootCache = new Dictionary<string, List<LootItem>>(StringComparer.OrdinalIgnoreCase);
                _staticLootCountCache = new Dictionary<string, (int min, int max)>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in file)
                {
                    var tpl = kvp.Key;
                    var entry = kvp.Value;
                    if (entry?.itemDistribution == null || entry.itemDistribution.Count == 0)
                        continue;

                    var total = entry.itemDistribution.Sum(d => d.relativeProbability);
                    if (total <= 0)
                        continue;

                    var items = new List<LootItem>();
                    foreach (var dist in entry.itemDistribution)
                    {
                        if (string.IsNullOrEmpty(dist.tpl))
                            continue;
                        items.Add(new LootItem
                        {
                            template = dist.tpl,
                            chance = (dist.relativeProbability / total) * 100f,
                            randomRotation = true,
                            isDistribution = true
                        });
                    }

                    items.Sort((a, b) => b.chance.CompareTo(a.chance));
                    _staticLootCache[tpl] = items;

                    int minCount = 1, maxCount = 1;
                    if (entry.itemcountDistribution != null && entry.itemcountDistribution.Count > 0)
                    {
                        var counts = entry.itemcountDistribution.Select(d => d.count).ToList();
                        minCount = Math.Max(counts.Min(), 1);
                        maxCount = Math.Max(counts.Max(), 1);
                    }
                    _staticLootCountCache[tpl] = (minCount, maxCount);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load staticLoot.json from {dir}: {ex.Message}");
            }
        }

        private static TransformData FromVectorData(VectorData v)
        {
            return v == null ? new TransformData() : new TransformData { x = v.x, y = v.y, z = v.z };
        }

        private static TransformData FromVector3(Vector3 v)
        {
            return new TransformData { x = v.x, y = v.y, z = v.z };
        }

        private static void ImportVanillaSpawnsAndExtracts(MapData data, string dir)
        {
            ImportPmcSpawns(data, dir);
            ImportExtracts(data, dir);
        }

        private static void ImportPmcSpawns(MapData data, string dir)
        {
            var path = Path.Combine(dir, "base.json");
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var obj = JObject.Parse(json);
                if (!obj.TryGetValue("SpawnPointParams", StringComparison.OrdinalIgnoreCase, out var token) || token is not JArray array)
                    return;

                data.botSpawnPoints ??= new List<BotSpawnPoint>();
                foreach (var item in array)
                {
                    var categories = item["Categories"]?.ToObject<List<string>>() ?? new List<string>();
                    var sides = item["Sides"]?.ToObject<List<string>>() ?? new List<string>();
                    var catsLower = categories.Select(c => c.ToLowerInvariant()).ToList();
                    var sidesLower = sides.Select(s => s.ToLowerInvariant()).ToList();
                    bool isPlayerSpawn = catsLower.Contains("player") || catsLower.Contains("coop") || catsLower.Contains("opposite");
                    bool isPmcSide = sidesLower.Contains("pmc") || sidesLower.Contains("all");
                    if (!isPlayerSpawn && !isPmcSide)
                        continue;

                    var position = item["Position"];
                    if (position == null)
                        continue;

                    float x = position["x"]?.Value<float>() ?? 0f;
                    float y = position["y"]?.Value<float>() ?? 0f;
                    float z = position["z"]?.Value<float>() ?? 0f;
                    float rotation = item["Rotation"]?.Value<float>() ?? 0f;

                    float radius = 0.5f;
                    if (item["ColliderParams"]?["_props"]?["Radius"] is JValue rv)
                        radius = rv.Value<float>();

                    string id = item["Id"]?.Value<string>() ?? Guid.NewGuid().ToString();
                    string infiltration = item["Infiltration"]?.Value<string>() ?? "pmc";
                    string botZoneName = item["BotZoneName"]?.Value<string>() ?? "";
                    float delay = item["DelayToCanSpawnSec"]?.Value<float>() ?? 4f;

                    data.botSpawnPoints.Add(new BotSpawnPoint
                    {
                        id = id,
                        name = "vanilla_pmc_" + (string.IsNullOrWhiteSpace(infiltration) ? "pmc" : infiltration.ToLowerInvariant()),
                        position = new TransformData { x = x, y = y, z = z },
                        rotation = new TransformData { x = 0, y = rotation, z = 0 },
                        radius = radius,
                        side = BotSpawnSide.Pmc,
                        category = BotSpawnCategory.BotPmc,
                        preset = BotSpawnPreset.PMC,
                        wildSpawnType = "pmcBot",
                        spawnChance = 100f,
                        delayToCanSpawnSec = delay,
                        botZoneName = botZoneName,
                        spawnMode = "Potential"
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[VanillaImporter] Failed to import vanilla PMC spawns from {path}: {ex.Message}");
            }
        }

        private static void ImportExtracts(MapData data, string dir)
        {
            var path = Path.Combine(dir, "allExtracts.json");
            if (!File.Exists(path))
                return;

            var positions = LoadExtractPositions(dir, data.map);

            try
            {
                var json = File.ReadAllText(path);
                var array = JArray.Parse(json);
                data.extractZones ??= new List<ExtractZone>();
                foreach (var item in array)
                {
                    string name = item["Name"]?.Value<string>() ?? "extract";
                    var position = item["Position"] ?? (positions.TryGetValue(name, out var posEntry) ? posEntry["position"] : null);
                    if (position == null)
                        continue;

                    float x = position["x"]?.Value<float>() ?? 0f;
                    float y = position["y"]?.Value<float>() ?? 0f;
                    float z = position["z"]?.Value<float>() ?? 0f;
                    float time = item["ExfiltrationTime"]?.Value<float>() ?? 5f;
                    float chance = item["Chance"]?.Value<float>() ?? 100f;
                    string type = item["ExfiltrationType"]?.Value<string>() ?? "Individual";
                    string side = item["Side"]?.Value<string>() ?? "Pmc";
                    string passage = item["PassageRequirement"]?.Value<string>() ?? "None";
                    string reqTip = item["RequirementTip"]?.Value<string>() ?? "";
                    string reqSlot = item["RequiredSlot"]?.Value<string>() ?? "FirstPrimaryWeapon";
                    int count = item["Count"]?.Value<int>() ?? 0;
                    int playersCount = item["PlayersCount"]?.Value<int>() ?? 0;

                    var requirements = new List<ExtractZoneRequirement>();

                    data.extractZones.Add(new ExtractZone
                    {
                        id = Guid.NewGuid().ToString(),
                        name = "vanilla_extract_" + name,
                        exitName = name,
                        position = new TransformData { x = x, y = y, z = z },
                        rotation = new TransformData(),
                        scale = new TransformData { x = 1f, y = 1f, z = 1f },
                        radius = 2f,
                        shape = ZoneShape.Box,
                        exfiltrationTime = time,
                        spawnChance = chance,
                        exfiltrationType = type,
                        side = side,
                        passageRequirement = passage,
                        requirementTip = reqTip,
                        requiredSlot = reqSlot,
                        count = count,
                        playersCount = playersCount,
                        requirements = requirements
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[VanillaImporter] Failed to import vanilla extracts from {path}: {ex.Message}");
            }
        }

        private static Dictionary<string, JToken> LoadExtractPositions(string dir, string mapId)
        {
            var dict = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

            var path = Path.Combine(dir, "extractPositions.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var array = JArray.Parse(json);
                    foreach (var entry in array)
                    {
                        var name = entry["name"]?.Value<string>();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        dict[name] = entry;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[VanillaImporter] Failed to load extractPositions.json from {path}: {ex.Message}");
                }
            }

            if (dict.Count > 0)
                return dict;

            try
            {
                var asm = typeof(VanillaImporter).Assembly;
                var resource = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.IndexOf($".{mapId}.extractPositions.json", StringComparison.OrdinalIgnoreCase) >= 0);
                if (resource != null)
                {
                    using (var stream = asm.GetManifestResourceStream(resource))
                    using (var reader = new StreamReader(stream))
                    {
                        var json = reader.ReadToEnd();
                        var array = JArray.Parse(json);
                        foreach (var entry in array)
                        {
                            var name = entry["name"]?.Value<string>();
                            if (string.IsNullOrWhiteSpace(name))
                                continue;
                            if (!dict.ContainsKey(name))
                                dict[name] = entry;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[VanillaImporter] Failed to load embedded extractPositions.json for {mapId}: {ex.Message}");
            }

            return dict;
        }

        private class VanillaLootFile
        {
            public VanillaSpawnpointCount spawnpointCount;
            public List<VanillaSpawnpoint> spawnpointsForced = new List<VanillaSpawnpoint>();
            public List<VanillaSpawnpoint> spawnpoints = new List<VanillaSpawnpoint>();
        }

        private class VanillaContainerFile
        {
            public List<VanillaSpawnpoint> staticWeapons = new List<VanillaSpawnpoint>();
            public List<VanillaSpawnpoint> staticContainers = new List<VanillaSpawnpoint>();
            public List<VanillaSpawnpoint> staticForced = new List<VanillaSpawnpoint>();
        }

        private class VanillaSpawnpointCount
        {
            public float mean;
            public float std;
        }

        private class VanillaSpawnpoint
        {
            public string locationId;
            public float probability;
            public VanillaTemplate template;
        }

        private class VanillaTemplate
        {
            public string Id;
            public bool IsContainer;
            public bool useGravity;
            public bool randomRotation;
            public VectorData Position;
            public VectorData Rotation;
            public bool IsGroupPosition;
            public List<VectorData> GroupPositions;
            public bool IsAlwaysSpawn;
            public string Root;
            public List<VanillaItem> Items;
        }

        private class VectorData
        {
            public float x;
            public float y;
            public float z;
        }

        private class VanillaItem
        {
            public string _id;
            public string _tpl;
            public string parentId;
            public string slotId;
            public VanillaUpd upd;
        }

        private class VanillaUpd
        {
            public int StackObjectsCount;
        }

        private class StaticLootEntry
        {
            public List<StaticLootDistribution> itemcountDistribution = new List<StaticLootDistribution>();
            public List<StaticLootDistribution> itemDistribution = new List<StaticLootDistribution>();
        }

        private class StaticLootDistribution
        {
            public string tpl;
            public float relativeProbability;
            public int count;
        }
    }
}
