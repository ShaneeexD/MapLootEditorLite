using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

namespace MapLootEditorLite.Client
{
    public class ItemCatalogEntry
    {
        public string Id;
        public string Name;
        public string Category;
        public string SearchKey;
    }

    // Local, cached list of items for the item picker. Source: the game's loaded ItemFactory templates
    // (localized names); falls back to the embedded offline name list when the factory is unavailable.
    public static class ItemCatalog
    {
        private static List<ItemCatalogEntry> _entries;
        private static bool _fromFactory;

        public static List<ItemCatalogEntry> Get()
        {
            var factoryReady = Singleton<ItemFactory>.Instantiated;
            // Rebuild once if the first build happened before the factory existed.
            if (_entries == null || (!_fromFactory && factoryReady))
                Build(factoryReady);
            return _entries;
        }

        private static void Build(bool factoryReady)
        {
            var offline = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in ItemNameResolver.GetAllKnownNames())
                offline[kvp.Key] = kvp.Value;

            var list = new List<ItemCatalogEntry>();
            _fromFactory = false;

            if (factoryReady)
            {
                try
                {
                    var templates = Singleton<ItemFactory>.Instance.ItemTemplates;
                    foreach (var kvp in templates)
                    {
                        var t = kvp.Value;
                        if (t == null || !IsItemNode(t))
                            continue;
                        var id = kvp.Key.ToString();
                        if (string.IsNullOrEmpty(id))
                            continue;

                        var name = Localize(t.NameLocalizationKey);
                        if (string.IsNullOrEmpty(name) && !offline.TryGetValue(id, out name))
                            name = null;
                        if (string.IsNullOrEmpty(name))
                            name = t.Name;
                        if (string.IsNullOrEmpty(name))
                            continue;

                        string category = null;
                        try { category = t.Parent != null ? (Localize(t.Parent.NameLocalizationKey) ?? t.Parent.Name) : null; }
                        catch { }

                        list.Add(MakeEntry(id, name, category));
                    }
                    _fromFactory = list.Count > 0;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"ItemCatalog: failed to read ItemFactory templates: {ex.Message}");
                    list.Clear();
                }
            }

            if (list.Count == 0)
            {
                foreach (var kvp in offline)
                    list.Add(MakeEntry(kvp.Key, kvp.Value, null));
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _entries = list;
            Plugin.Log.LogInfo($"ItemCatalog: {list.Count} items loaded ({(_fromFactory ? "game item templates" : "offline name list")}).");
        }

        public static IEnumerable<ItemCatalogEntry> Search(string term, int max)
        {
            var entries = Get();
            if (string.IsNullOrWhiteSpace(term))
                return entries.Take(max);
            term = term.Trim();
            return entries.Where(e => e.SearchKey.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).Take(max);
        }

        public static ItemCatalogEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            return Get().FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private static ItemCatalogEntry MakeEntry(string id, string name, string category)
        {
            return new ItemCatalogEntry
            {
                Id = id,
                Name = name,
                Category = category,
                SearchKey = name + "\n" + id
            };
        }

        private static string Localize(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            var s = key.Localized(EStringCase.None);
            return string.IsNullOrEmpty(s) || s == key ? null : s;
        }

        // Keep only real items, not the category nodes of the template tree.
        private static bool IsItemNode(ItemTemplate t)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var type = t.GetType();
            for (var cur = type; cur != null; cur = cur.BaseType)
            {
                var f = cur.GetField("_type", flags | BindingFlags.DeclaredOnly);
                if (f != null)
                    return f.GetValue(t)?.ToString() == "Item";
                var p = cur.GetProperty("_type", flags | BindingFlags.DeclaredOnly);
                if (p != null)
                    return p.GetValue(t, null)?.ToString() == "Item";
            }
            return true;
        }
    }
}
