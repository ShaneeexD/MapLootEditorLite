using System.IO;
using Newtonsoft.Json;

namespace MapLootEditorLite.Client
{
    public static class JsonStorage
    {
        public static string SaveDirectory { get; set; }

        public static void Initialize(string pluginPath)
        {
            var pluginsDir = Path.GetDirectoryName(pluginPath);
            var bepinexDir = Path.GetDirectoryName(pluginsDir);
            SaveDirectory = Path.Combine(bepinexDir, "config", "MapEditorLite");
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
            return data;
        }
    }
}
