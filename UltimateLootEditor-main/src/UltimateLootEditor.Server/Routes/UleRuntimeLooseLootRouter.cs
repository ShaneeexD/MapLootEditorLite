using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using UltimateLootEditor.Services;
using UltimateLootEditor.Shared;
using System.Collections.Generic;
using System.Linq;

namespace UltimateLootEditor.Routes;

[Injectable]
public sealed class UleRuntimeLooseLootRouter(
    JsonUtil jsonUtil,
    LocationTable locationTable,
    GlobalTable globalTable,
    TemplateTable templateTable,
    LocaleTable localeTable,
    ISptLogger<UleRuntimeLooseLootRouter> logger) : DynamicRouter(jsonUtil, [
    new RouteAction<EmptyRequestData>(
        "/ule/runtime/loose-loot",
        (url, _, _, _, _) =>
        {
            try
            {
                var request = ParseLooseLootRequest(url);
                var locationId = NormalizeLocationId(request.LocationId);
                if (string.IsNullOrWhiteSpace(locationId))
                {
                    return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError("Missing location id.")) ?? "{\"error\":\"Missing location id.\"}");
                }

                if (!request.IsValid)
                {
                    return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError("Invalid loose-loot runtime route.")) ?? "{\"error\":\"Invalid loose-loot runtime route.\"}");
                }

                var mappedLocationId = locationTable.GetMappedKey(locationId);
                if (!string.IsNullOrWhiteSpace(mappedLocationId))
                {
                    locationId = mappedLocationId;
                }

                var dictionary = locationTable.GetDictionary();
                // Resolve the installed runtime entry; do not assume the backport keeps its old ID.
                if (MapCatalog.IsIcebreakerId(locationId) &&
                    (!dictionary.TryGetValue(locationId, out var candidate) || candidate?.LooseLoot == null))
                {
                    foreach (var alias in new[] { "icebreaker", "suburbs" })
                    {
                        var key = locationTable.GetMappedKey(alias);
                        if (string.IsNullOrWhiteSpace(key)) key = alias;
                        if (dictionary.TryGetValue(key, out var alternate) && alternate?.LooseLoot != null)
                        {
                            locationId = key;
                            break;
                        }
                    }
                }
                if (!dictionary.TryGetValue(locationId, out var location) || location?.LooseLoot == null)
                {
                    return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError($"Location '{locationId}' has no loose loot.")) ?? "{\"error\":\"Location has no loose loot.\"}");
                }

                var cache = GetRuntimeLocationCache(locationId, location.LooseLoot.Value);
                return ValueTask.FromResult(cache.GetSerializedSnapshot(jsonUtil));
            }
            catch (Exception ex)
            {
                logger.Warning($"[ULE] Failed to return runtime loose loot for '{url}': {ex.Message}");
                return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError(ex.Message)) ?? "{\"error\":\"Runtime loose loot route failed.\"}");
            }
        }),
    new RouteAction<EmptyRequestData>(
        "/ule/runtime/item-sources",
        (_, _, _, _, _) =>
        {
            try
            {
                var sources = RuntimeTemplateIndex.GetRuntimeItemSources(templateTable, localeTable);
                return ValueTask.FromResult(jsonUtil.Serialize(sources) ?? "{}");
            }
            catch (Exception ex)
            {
                logger.Warning($"[ULE] Failed to return runtime item source map: {ex.Message}");
                return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError(ex.Message)) ?? "{\"error\":\"Runtime item source route failed.\"}");
            }
        }),
    new RouteAction<EmptyRequestData>(
        "/ule/runtime/item-presets",
        (_, _, _, _, _) =>
        {
            try
            {
                return ValueTask.FromResult(jsonUtil.Serialize(globalTable.ItemPresets) ?? "{}");
            }
            catch (Exception ex)
            {
                logger.Warning($"[ULE] Failed to return runtime item presets: {ex.Message}");
                return ValueTask.FromResult(jsonUtil.Serialize(new RuntimeLooseLootError(ex.Message)) ?? "{\"error\":\"Runtime item preset route failed.\"}");
            }
        })
])
{
    private static readonly object RuntimeCacheSync = new();
    private static readonly Dictionary<string, RuntimeLocationCache> RuntimeLocationCaches = new(StringComparer.OrdinalIgnoreCase);

    private sealed record RuntimeLooseLootError(string Error);

    private sealed class RuntimeLocationCache
    {
        public RuntimeLocationCache(
            LooseLoot source,
            Dictionary<string, RuntimeSpawnSource> spawnSources)
        {
            Source = source;
            SpawnSources = spawnSources;
        }

        public LooseLoot Source { get; }
        public Dictionary<string, RuntimeSpawnSource> SpawnSources { get; }
        private string SerializedSnapshotPayload { get; set; }

        public string GetSerializedSnapshot(JsonUtil jsonUtil)
        {
            lock (this)
            {
                if (!string.IsNullOrWhiteSpace(SerializedSnapshotPayload))
                {
                    return SerializedSnapshotPayload;
                }

                var snapshot = new RuntimeLooseLootMapSnapshot
                {
                    Spawns = SpawnSources.Values
                        .OrderBy(source => source.Summary.Id, StringComparer.Ordinal)
                        .Select(source => new RuntimeLooseLootSpawnSnapshot
                        {
                            Id = source.Summary.Id,
                            Name = source.Summary.Name,
                            Position = source.Summary.Position,
                            Rotation = source.Summary.Rotation,
                            SpawnChance = source.Summary.SpawnChance,
                            IsAlwaysSpawn = source.Summary.IsAlwaysSpawn,
                            UseGravity = source.Summary.UseGravity,
                            ItemCount = source.Summary.ItemCount,
                            Items = BuildItems(source.Spawnpoint, jsonUtil)
                        })
                        .ToList()
                };

                SerializedSnapshotPayload = jsonUtil.Serialize(snapshot) ?? "{}";
                return SerializedSnapshotPayload;
            }
        }
    }

    private sealed class RuntimeSpawnSource
    {
        public Spawnpoint Spawnpoint { get; init; }
        public RuntimeLooseLootSpawnSnapshot Summary { get; init; }
    }

    private readonly struct LooseLootRequest
    {
        public LooseLootRequest(string locationId, bool isValid)
        {
            LocationId = locationId;
            IsValid = isValid;
        }

        public string LocationId { get; }
        public bool IsValid { get; }
    }

    private sealed class LooseLootItemRecord
    {
        public string Id { get; init; } = string.Empty;
        public string ParentId { get; init; } = string.Empty;
        public string Tpl { get; init; } = string.Empty;
        public string SlotId { get; init; } = string.Empty;
        public string ComposedKey { get; init; } = string.Empty;
        public string LocationJson { get; init; }
        public string UpdJson { get; init; }
        public int? StackCount { get; init; }
        public List<LooseLootItemRecord> Children { get; } = new();
    }

    private static RuntimeLocationCache GetRuntimeLocationCache(string locationId, LooseLoot looseLoot)
    {
        lock (RuntimeCacheSync)
        {
            if (RuntimeLocationCaches.TryGetValue(locationId, out var cached) &&
                ReferenceEquals(cached.Source, looseLoot))
            {
                return cached;
            }

            var spawnSources = BuildSpawnSources(looseLoot);
            var created = new RuntimeLocationCache(looseLoot, spawnSources);
            RuntimeLocationCaches[locationId] = created;
            return created;
        }
    }

    private static Dictionary<string, RuntimeSpawnSource> BuildSpawnSources(LooseLoot looseLoot)
    {
        var sources = new Dictionary<string, RuntimeSpawnSource>(StringComparer.Ordinal);
        AddSpawnSources(sources, looseLoot?.Spawnpoints, isForced: false);
        AddSpawnSources(sources, looseLoot?.SpawnpointsForced, isForced: true);
        return sources;
    }

    private static void AddSpawnSources(
        Dictionary<string, RuntimeSpawnSource> target,
        IEnumerable<Spawnpoint> spawnpoints,
        bool isForced)
    {
        if (target == null || spawnpoints == null)
        {
            return;
        }

        foreach (var spawnpoint in spawnpoints)
        {
            var template = spawnpoint?.Template;
            var id = NormalizeRuntimeIdentifier(template?.Id ?? spawnpoint?.LocationId);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (target.ContainsKey(id))
            {
                continue;
            }

            target[id] = new RuntimeSpawnSource
            {
                Spawnpoint = spawnpoint,
                Summary = new RuntimeLooseLootSpawnSnapshot
                {
                    Id = id,
                    Name = id,
                    Position = ToUleVector(template?.Position),
                    Rotation = ToUleVector(template?.Rotation),
                    SpawnChance = spawnpoint?.Probability ?? 1d,
                    IsAlwaysSpawn = isForced || template?.IsAlwaysSpawn == true,
                    UseGravity = template?.UseGravity ?? true,
                    ItemCount = spawnpoint?.ItemDistribution?.Count() ?? 0
                }
            };
        }
    }

    private static List<UleItemEntry> BuildItems(Spawnpoint spawnpoint, JsonUtil jsonUtil)
    {
        var records = new List<LooseLootItemRecord>();
        var recordsById = new Dictionary<string, LooseLootItemRecord>(StringComparer.Ordinal);
        var ordinal = 0;
        foreach (var source in spawnpoint?.Template?.Items ?? Enumerable.Empty<SptLootItem>())
        {
            if (source == null)
            {
                continue;
            }

            var tpl = NormalizeRuntimeIdentifier(source.Template);
            if (string.IsNullOrWhiteSpace(tpl))
            {
                continue;
            }

            var id = NormalizeRuntimeIdentifier(source.Id);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = $"runtime_item_{ordinal}";
            }

            var record = new LooseLootItemRecord
            {
                Id = id,
                ParentId = NormalizeRuntimeIdentifier(source.ParentId),
                Tpl = tpl,
                SlotId = source.SlotId ?? string.Empty,
                ComposedKey = source.ComposedKey ?? string.Empty,
                LocationJson = SerializeOptional(jsonUtil, source.Location),
                UpdJson = SerializeOptional(jsonUtil, source.Upd),
                StackCount = ToStackCount(source.Upd?.StackObjectsCount)
            };

            records.Add(record);
            if (!recordsById.ContainsKey(id))
            {
                recordsById[id] = record;
            }

            ordinal++;
        }

        var roots = new List<LooseLootItemRecord>();
        foreach (var record in records)
        {
            if (!string.IsNullOrWhiteSpace(record.ParentId) &&
                recordsById.TryGetValue(record.ParentId, out var parent) &&
                !ReferenceEquals(parent, record))
            {
                parent.Children.Add(record);
            }
            else
            {
                roots.Add(record);
            }
        }

        var weightByKey = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var distribution in spawnpoint?.ItemDistribution ?? Enumerable.Empty<LooseLootItemDistribution>())
        {
            var key = distribution?.ComposedKey?.Key;
            if (!string.IsNullOrWhiteSpace(key))
            {
                weightByKey[key] = distribution.RelativeProbability ?? 1d;
            }
        }

        var items = new List<UleItemEntry>(roots.Count);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root.Tpl))
            {
                continue;
            }

            items.Add(new UleItemEntry
            {
                Tpl = root.Tpl,
                ComposedKey = root.ComposedKey,
                Weight = !string.IsNullOrWhiteSpace(root.ComposedKey) && weightByKey.TryGetValue(root.ComposedKey, out var weight)
                    ? weight
                    : 1d,
                SlotId = root.SlotId,
                LocationJson = root.LocationJson,
                UpdJson = root.UpdJson,
                StackMin = root.StackCount,
                StackMax = root.StackCount,
                Children = root.Children.Select(ToItemNode).ToList()
            });
        }

        return items;
    }

    private static UleItemNode ToItemNode(LooseLootItemRecord record)
    {
        return new UleItemNode
        {
            Tpl = record.Tpl,
            SlotId = record.SlotId,
            LocationJson = record.LocationJson,
            UpdJson = record.UpdJson,
            StackMin = record.StackCount,
            StackMax = record.StackCount,
            Children = record.Children.Select(ToItemNode).ToList()
        };
    }

    private static string SerializeOptional(JsonUtil jsonUtil, object value)
    {
        return value == null ? null : jsonUtil.Serialize(value);
    }

    private static string NormalizeRuntimeIdentifier(object value)
    {
        var identifier = value?.ToString()?.Trim() ?? string.Empty;
        return identifier.Length >= 2 && identifier[0] == '"' && identifier[identifier.Length - 1] == '"'
            ? identifier.Substring(1, identifier.Length - 2)
            : identifier;
    }

    private static int? ToStackCount(double? stackCount)
    {
        if (!stackCount.HasValue || stackCount.Value < 1d || stackCount.Value > int.MaxValue)
        {
            return null;
        }

        return (int)Math.Round(stackCount.Value, MidpointRounding.AwayFromZero);
    }

    private static UleVector3 ToUleVector(SPTarkov.Server.Core.Models.Eft.Common.Vector3? value)
    {
        return new UleVector3
        {
            X = value?.X ?? 0d,
            Y = value?.Y ?? 0d,
            Z = value?.Z ?? 0d
        };
    }

    private static LooseLootRequest ParseLooseLootRequest(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new LooseLootRequest(string.Empty, isValid: false);
        }

        const string routePrefix = "/ule/runtime/loose-loot";
        var path = url.Trim();
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var prefixIndex = path.IndexOf(routePrefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex >= 0)
        {
            path = path[(prefixIndex + routePrefix.Length)..];
        }

        var segments = path.Trim('/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length == 0 || string.IsNullOrWhiteSpace(segments[0]))
        {
            return new LooseLootRequest(string.Empty, isValid: false);
        }

        if (segments.Length == 1 ||
            (segments.Length == 2 && string.Equals(segments[1], "snapshot", StringComparison.OrdinalIgnoreCase)))
        {
            return new LooseLootRequest(segments[0].Trim(), isValid: true);
        }

        return new LooseLootRequest(segments[0].Trim(), isValid: false);
    }

    private static string NormalizeLocationId(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var id = candidate.Trim().ToLowerInvariant();
        switch (id)
        {
            case "customs":
            case "custom":
                return "bigmap";
            case "reserve":
            case "reservbase":
                return "rezervbase";
            case "streets":
                return "tarkovstreets";
            case "groundzero":
            case "gz":
                return "sandbox";
        }

        return MapCatalog.TryGetById(id, out var descriptor)
            ? descriptor.Id
            : id;
    }
}
