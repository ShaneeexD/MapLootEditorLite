using System.IO;
using Newtonsoft.Json;

namespace MapLootEditorLite.Client
{
    public static class JsonStorage
    {
        public static string SaveDirectory { get; set; } = string.Empty;

        public static void Initialize(string modDataDirectory)
        {
            SaveDirectory = Path.Combine(modDataDirectory, "editor");
        }

        public static string GetFilePath(string mapId)
        {
            return Path.Combine(SaveDirectory, mapId + ".json");
        }

        public static void Save(MapData data)
        {
            if (string.IsNullOrEmpty(data?.map))
                return;

            Directory.CreateDirectory(SaveDirectory);
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(GetFilePath(data.map), json);
        }

        public static MapData Load(string mapId)
        {
            var path = GetFilePath(mapId);
            if (!File.Exists(path))
                return new MapData { map = mapId };

            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<MapData>(json);
            if (data == null)
                return new MapData { map = mapId };

            data.map = mapId;
            MigrateLegacyItems(data);
            return data;
        }

        public static void MigrateLegacyItems(MapData data)
        {
            if (data == null)
                return;

            foreach (var spawn in data.lootSpawns)
            {
                if (spawn.items == null)
                    spawn.items = new System.Collections.Generic.List<LootItem>();

                if (spawn.items.Count == 0 && spawn.itemTpls != null && spawn.itemTpls.Count > 0)
                {
                    foreach (var tpl in spawn.itemTpls)
                        spawn.items.Add(new LootItem { template = tpl, chance = 100f });

                    spawn.itemTpls.Clear();
                }
            }

            foreach (var zone in data.lootZones)
            {
                if (zone.items == null)
                    zone.items = new System.Collections.Generic.List<LootItem>();

                if (zone.items.Count == 0 && zone.itemTpls != null && zone.itemTpls.Count > 0)
                {
                    foreach (var tpl in zone.itemTpls)
                        zone.items.Add(new LootItem { template = tpl, chance = 100f });

                    zone.itemTpls.Clear();
                }
            }
        }
    }
}
