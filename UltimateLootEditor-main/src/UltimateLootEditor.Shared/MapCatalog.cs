using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace UltimateLootEditor.Shared
{
    public sealed class MapDescriptor
    {
        public MapDescriptor(string id, string displayName, string folderName)
        {
            Id = id;
            DisplayName = displayName;
            FolderName = folderName;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string FolderName { get; }
    }

    public static class MapCatalog
    {
        private static readonly IReadOnlyList<MapDescriptor> VanillaMaps = new[]
        {
            new MapDescriptor("bigmap", "Customs", "Customs"),
            new MapDescriptor("factory4_day", "Factory (Day)", "FactoryDay"),
            new MapDescriptor("factory4_night", "Factory (Night)", "FactoryNight"),
            new MapDescriptor("interchange", "Interchange", "Interchange"),
            new MapDescriptor("laboratory", "Labs", "Labs"),
            new MapDescriptor("labyrinth", "Labyrinth", "Labyrinth"),
            new MapDescriptor("lighthouse", "Lighthouse", "Lighthouse"),
            new MapDescriptor("rezervbase", "Reserve", "Reserve"),
            new MapDescriptor("sandbox", "Ground Zero (1-20)", "GroundZero"),
            new MapDescriptor("sandbox_high", "Ground Zero (21+)", "GroundZero21"),
            new MapDescriptor("shoreline", "Shoreline", "Shoreline"),
            new MapDescriptor("tarkovstreets", "Streets of Tarkov", "Streets"),
            new MapDescriptor("woods", "Woods", "Woods"),
        };

        public static bool IcebreakerInstalled { get; private set; }

        public static void ConfigureOptionalMaps(string serverModsRoot)
        {
            IcebreakerInstalled = !string.IsNullOrWhiteSpace(serverModsRoot) &&
                File.Exists(Path.Combine(serverModsRoot, "ManimalIcebreaker", "icebreaker-server.dll"));
        }

        public static bool IsIcebreakerId(string id) => IcebreakerInstalled &&
            (string.Equals(id?.Trim(), "suburbs", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(id?.Trim(), "icebreaker", StringComparison.OrdinalIgnoreCase));

        public static IReadOnlyList<MapDescriptor> All => IcebreakerInstalled
            ? VanillaMaps.Concat(new[] { new MapDescriptor("suburbs", "Icebreaker", "Icebreaker") }).ToArray()
            : VanillaMaps;

        public static string[] SelectorValues =>
            (new[] { "off" }).Concat(All.Select(map => map.DisplayName)).ToArray();

        private static readonly Dictionary<string, MapDescriptor> ById =
            VanillaMaps.ToDictionary(map => map.Id, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, MapDescriptor> ByDisplayName =
            VanillaMaps.ToDictionary(map => map.DisplayName, StringComparer.OrdinalIgnoreCase);

        public static bool TryGetById(string mapId, out MapDescriptor descriptor)
        {
            descriptor = null;
            if (string.IsNullOrWhiteSpace(mapId))
            {
                return false;
            }

            if (IsIcebreakerId(mapId))
            {
                descriptor = new MapDescriptor(mapId.Trim().ToLowerInvariant(), "Icebreaker", "Icebreaker");
                return true;
            }
            return ById.TryGetValue(mapId, out descriptor);
        }

        public static bool TryResolveSelector(string selector, out MapDescriptor descriptor)
        {
            descriptor = null;
            if (string.IsNullOrWhiteSpace(selector))
            {
                return false;
            }

            if (TryGetById(selector, out descriptor))
            {
                return true;
            }

            return ByDisplayName.TryGetValue(selector, out descriptor);
        }

        public static IEnumerable<string> GetLegacyFolderNames(string mapId)
        {
            if (!TryGetById(mapId, out var descriptor))
            {
                yield break;
            }

            yield return descriptor.FolderName;
            yield return descriptor.DisplayName;
            yield return descriptor.Id;
            if (IsIcebreakerId(mapId))
            {
                yield return "suburbs";
                yield return "icebreaker";
            }
        }
    }
}
