using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public static class VanillaImporter
    {
        public static string DatabaseDirectory
        {
            get
            {
                var serverRoot = string.IsNullOrEmpty(Plugin.SptServerRoot) ? Plugin.SptRoot : Plugin.SptServerRoot;
                return Path.Combine(serverRoot, "SPT_Data", "database", "locations");
            }
        }

        public static MapData Import(string mapId)
        {
            var data = new MapData { map = mapId };
            var dir = Path.Combine(DatabaseDirectory, mapId.ToLowerInvariant());

            ImportLooseLoot(data, dir);
            ImportStaticContainers(data, dir);

            MarkVanilla(data);
            ClearContainerCache();

            Plugin.Log.LogInfo($"[MLEL] Imported vanilla data for {mapId}: {data.lootSpawns.Count} loose loot, {data.interactiveObjects.Count} containers.");
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
        }

        private static void ImportLooseLoot(MapData data, string dir)
        {
            var path = Path.Combine(dir, "looseLoot.json");
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<VanillaLootFile>(json);
                if (file?.spawnpointsForced == null)
                    return;

                ImportLooseSpawnpointList(data, file.spawnpointsForced);
                ImportLooseSpawnpointList(data, file.spawnpoints);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MLEL] Failed to import vanilla loose loot for {dir}: {ex.Message}");
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
                                randomRotation = true
                            });
                        }
                    }

                    data.lootSpawns.Add(marker);
                }
            }

        private static void ImportStaticContainers(MapData data, string dir)
        {
            var path = Path.Combine(dir, "staticContainers.json");
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<VanillaContainerFile>(json);
                if (file == null)
                    return;

                BuildContainerCache();
                ImportStaticContainerList(data, file.staticContainers);
                ImportStaticContainerList(data, file.staticForced);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MLEL] Failed to import vanilla static containers for {dir}: {ex.Message}");
            }
        }

        private static Dictionary<string, LootableContainer> _containerCache;

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
        }

        private static LootableContainer FindContainer(string id)
        {
            if (_containerCache == null)
                BuildContainerCache();
            if (id != null && _containerCache.TryGetValue(id, out var container))
                return container;
            return null;
        }

        private static void ImportStaticContainerList(MapData data, List<VanillaSpawnpoint> list)
        {
            if (list == null)
                return;

            foreach (var sp in list)
            {
                if (sp?.template == null)
                    continue;

                var root = sp.template.Items?.FirstOrDefault(i => string.IsNullOrEmpty(i.parentId));
                var containerTpl = root?._tpl ?? "578f87a3245977356274f2cb";

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
                            randomRotation = true
                        });
                    }
                }

                data.interactiveObjects.Add(marker);
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
            // Ignored for now
        }
    }
}
