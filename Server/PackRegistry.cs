using System.Collections.Generic;
using System.Linq;

namespace MapLootEditorLite.Server;

public static class PackRegistry
{
    private static readonly List<PackData> Packs = [];

    public static void Register(IEnumerable<PackData> packs)
    {
        Packs.AddRange(packs);
    }

    public static IEnumerable<MapData> GetMapsForLocation(string locationId)
    {
        var normalized = NormalizeLocationId(locationId);
        foreach (var pack in Packs)
        {
            foreach (var (key, map) in pack.Maps)
            {
                if (NormalizeLocationId(key) == normalized)
                {
                    yield return map;
                }
            }
        }
    }

    public static int TotalSpawnCount()
    {
        return Packs.Sum(p => p.Maps.Sum(m => m.Value.LootSpawns.Count + m.Value.LootZones.Count));
    }

    private static string NormalizeLocationId(string locationId)
    {
        return locationId.ToLowerInvariant().Replace("_", "").Replace(" ", "");
    }
}
