using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace MapLootEditorLite.Client
{
    public static class ItemNameResolver
    {
        private static Dictionary<string, string> _names;

        public static string GetName(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return null;
            EnsureLoaded();
            if (_names == null)
                return null;
            _names.TryGetValue(templateId, out var name);
            return name;
        }

        public static string GetNameOrId(string templateId)
        {
            var name = GetName(templateId);
            return string.IsNullOrEmpty(name) ? templateId : $"{name} ({templateId})";
        }

        private static void EnsureLoaded()
        {
            if (_names != null)
                return;

            _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var serverRoot = string.IsNullOrEmpty(Plugin.SptServerRoot) ? Plugin.SptRoot : Plugin.SptServerRoot;
            var path = Path.Combine(serverRoot, "SPT_Data", "database", "templates", "items.json");
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<Dictionary<string, ItemEntry>>(json);
                if (file != null)
                {
                    foreach (var kvp in file)
                    {
                        var id = kvp.Value?._id ?? kvp.Key;
                        var name = kvp.Value?._name;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            _names[id] = name;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[MLEL] Failed to load item name database: {ex.Message}");
            }
        }

        private class ItemEntry
        {
            public string _id;
            public string _name;
        }
    }
}
