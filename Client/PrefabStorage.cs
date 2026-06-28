using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MapLootEditorLite.Client
{
    public static class PrefabStorage
    {
        private static string _prefabDir;

        public static void Initialize(string baseDir)
        {
            _prefabDir = Path.Combine(baseDir, "prefabs");
            Directory.CreateDirectory(_prefabDir);
        }

        public static string PrefabPath(string name)
        {
            var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_prefabDir, $"{safe}.json");
        }

        public static void Save(PrefabData prefab)
        {
            var path = PrefabPath(prefab.name);
            File.WriteAllText(path, JsonConvert.SerializeObject(prefab, Formatting.Indented));
        }

        public static PrefabData Load(string name)
        {
            var path = PrefabPath(name);
            if (!File.Exists(path))
                return null;
            return JsonConvert.DeserializeObject<PrefabData>(File.ReadAllText(path));
        }

        public static List<string> ListPrefabNames()
        {
            if (!Directory.Exists(_prefabDir))
                return new List<string>();
            return Directory.GetFiles(_prefabDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
        }
    }
}
