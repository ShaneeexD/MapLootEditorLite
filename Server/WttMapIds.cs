using System;
using System.Collections.Generic;
using System.Linq;

namespace MapLootEditorLite.Server;

public static class WttMapIds
{
    private static readonly Dictionary<string, List<string>> MapIdLookup = new(StringComparer.OrdinalIgnoreCase)
    {
        ["customs"] = ["bigmap"],
        ["bigmap"] = ["bigmap"],
        ["woods"] = ["woods"],
        ["factory"] = ["factory4_day", "factory4_night"],
        ["factory4_day"] = ["factory4_day"],
        ["factory4_night"] = ["factory4_night"],
        ["interchange"] = ["interchange"],
        ["lighthouse"] = ["lighthouse"],
        ["reserve"] = ["rezervbase"],
        ["rezervbase"] = ["rezervbase"],
        ["shoreline"] = ["shoreline"],
        ["streets"] = ["tarkovstreets"],
        ["tarkovstreets"] = ["tarkovstreets"],
        ["labs"] = ["laboratory"],
        ["laboratory"] = ["laboratory"],
        ["groundzero"] = ["sandbox"],
        ["sandbox"] = ["sandbox"]
    };

    public static IReadOnlyDictionary<string, List<string>> MapIds => MapIdLookup;

    public static IEnumerable<string> ToWttMapIds(string packMapKey)
    {
        var key = packMapKey.ToLowerInvariant();
        if (MapIdLookup.TryGetValue(key, out var ids))
            return ids;
        return [key];
    }
}
