using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace MapLootEditorLite.Client
{
    public static class ItemNameResolver
    {
        private static Dictionary<string, string> _names;
        private static Dictionary<string, string> _parents;
        private static Dictionary<string, int> _stackMaxSizes;
        private static Dictionary<string, string> _apiNames;
        private static bool _apiLoadAttempted;

        public static string GetName(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return null;

            // Prefer the local API names if they have been loaded.
            if (_apiNames != null && _apiNames.TryGetValue(templateId, out var apiName))
                return apiName;

            // Fall back to the local items.json database.
            EnsureLoaded();
            if (_names != null && _names.TryGetValue(templateId, out var name))
                return name;

            return null;
        }

        public static string GetNameOrId(string templateId)
        {
            var name = GetName(templateId);
            return string.IsNullOrEmpty(name) ? templateId : $"{name} ({templateId})";
        }

        public static IEnumerator LoadApiNames()
        {
            if (_apiLoadAttempted)
                yield break;
            _apiLoadAttempted = true;

            var url = "https://db.sp-tarkov.com/api/item/names";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 15;
                request.SetRequestHeader("User-Agent", "MapEditorLite/1.0");
                request.SetRequestHeader("Accept", "application/json");
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Plugin.Log.LogWarning($"Failed to load API names: {request.error}; trying offline cache.");
                    LoadOfflineCache();
                    yield break;
                }

                try
                {
                    var json = request.downloadHandler.text;
                    var items = JsonConvert.DeserializeObject<List<ApiNameEntry>>(json);
                    if (items == null)
                        yield break;

                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in items)
                    {
                        var id = item.item?._id;
                        var name = item.locale?.Name;
                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            dict[id] = name;
                    }

                    _apiNames = dict;
                    Plugin.Log.LogInfo($"Loaded {dict.Count} names from API.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Failed to parse API names: {ex.Message}; trying offline cache.");
                    LoadOfflineCache();
                }
            }
        }

        private static void LoadOfflineCache()
        {
            try
            {
                var json = ReadOfflineCacheJson();
                if (string.IsNullOrEmpty(json))
                    return;

                var file = JsonConvert.DeserializeObject<Dictionary<string, OfflineNameEntry>>(json);
                if (file == null)
                    return;

                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in file)
                {
                    var id = kvp.Key;
                    var name = kvp.Value?.Name;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        dict[id] = name;
                }

                _apiNames = dict;
                Plugin.Log.LogInfo($"Loaded {dict.Count} names from offline cache.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load offline cache: {ex.Message}");
            }
        }

        private static string ReadOfflineCacheJson()
        {
            var path = Path.Combine(Plugin.ModDataDirectory, "api_item_names.json");
            if (File.Exists(path))
                return File.ReadAllText(path);

            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream("api_item_names.json"))
            {
                if (stream == null)
                    return null;
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static void EnsureLoaded()
        {
            if (_names != null)
                return;

            _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _stackMaxSizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var path = Path.Combine(Plugin.GameRoot, "SPT_Data", "database", "templates", "items.json");
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
                        var parent = kvp.Value?._parent;
                        if (!string.IsNullOrEmpty(id))
                        {
                            if (!string.IsNullOrEmpty(name))
                                _names[id] = name;
                            if (!string.IsNullOrEmpty(parent))
                                _parents[id] = parent;
                            _stackMaxSizes[id] = Math.Max(kvp.Value?._props?.StackMaxSize ?? 1, 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load item name database: {ex.Message}");
            }
        }

        public static string GetParent(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return null;
            EnsureLoaded();
            return _parents != null && _parents.TryGetValue(templateId, out var parent) ? parent : null;
        }

        public static int GetStackMaxSize(string templateId)
        {
            if (string.IsNullOrEmpty(templateId))
                return 1;
            EnsureLoaded();
            return _stackMaxSizes != null && _stackMaxSizes.TryGetValue(templateId, out var size) ? size : 1;
        }

        private class ApiNameEntry
        {
            public ApiItem item;
            public ApiLocale locale;
        }

        private class ApiItem
        {
            public string _id;
        }

        private class ApiLocale
        {
            public string Name;
            public string ShortName;
        }

        private class OfflineNameEntry
        {
            public string Name;
            public string ShortName;
        }

        private class ItemEntry
        {
            public string _id;
            public string _name;
            public string _parent;
            public ItemProps _props;
        }

        private class ItemProps
        {
            public int StackMaxSize;
        }
    }
}
