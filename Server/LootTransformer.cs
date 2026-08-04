using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace MapLootEditorLite.Server;

public static class LootTransformer
{
    // Tracks which LooseLoot instances have already had a transformer attached, so Register() can be safely called again when locations are regenerated.
    private static readonly ConditionalWeakTable<object, object> RegisteredLooseLoot = new ConditionalWeakTable<object, object>();
    private static LocationTable? _locationTable;

    public static void Register(LocationTable locationTable)
    {
        _locationTable = locationTable;
        RegisterInternal();
    }

    public static void Register()
    {
        RegisterInternal();
    }

    private static void RegisterInternal()
    {
        if (_locationTable is null)
        {
            ServerPlugin.Logger?.Warning("[MEL] LootTransformer.Register() called before LocationTable was set; skipping.");
            return;
        }

        var locations = _locationTable.GetDictionary();
        var registered = 0;
        var skipped = 0;

        foreach (var (locationId, location) in locations)
        {
            var maps = PackRegistry.GetMapsForLocation(locationId).ToList();
            if (maps.Count == 0)
            {
                continue;
            }

            var looseLoot = location.LooseLoot;
            if (looseLoot == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' has no LooseLoot; skipping loot transformer.");
                continue;
            }

            if (RegisteredLooseLoot.TryGetValue(looseLoot, out _))
            {
                skipped++;
                continue;
            }
            RegisteredLooseLoot.Add(looseLoot, true);

            looseLoot.AddTransformer(looseLootObj =>
            {
                if (looseLootObj == null)
                {
                    return looseLootObj;
                }

                var spawnpoints = looseLootObj.Spawnpoints?.ToList() ?? [];
                var existingIds = new HashSet<string>(spawnpoints.Select(s => s.LocationId));
                var random = Random.Shared;
                var added = 0;

                foreach (var map in maps)
                {
                    foreach (var spawn in map.LootSpawns)
                    {
                        if (spawn.Forced)
                        {
                            continue;
                        }

                        if (!QuestConditionsMet(spawn.QuestOnly, spawn.QuestCompleted, spawn.QuestId))
                        {
                            continue;
                        }

                        var filteredItems = spawn.Items.Where(ShouldSpawnItem).ToList();
                        if (filteredItems.Count == 0)
                        {
                            continue;
                        }

                        // Treat SpawnChance as a percentage chance to be included in this raid's loose loot pool.
                        if (spawn.SpawnChance < 100.0 && RandX.Next() * 100.0 >= spawn.SpawnChance)
                        {
                            continue;
                        }

                        var newSpawn = CreateSpawnpoint(spawn, filteredItems);
                        if (existingIds.Contains(spawn.Id))
                        {
                            var idx = spawnpoints.FindIndex(s => s.LocationId == spawn.Id);
                            if (idx >= 0)
                            {
                                spawnpoints[idx] = newSpawn;
                                ServerPlugin.Logger?.Info($"[MEL] Overrode vanilla loose loot spawn {spawn.Id}.");
                            }
                            continue;
                        }

                        if (!existingIds.Add(spawn.Id))
                        {
                            continue;
                        }

                        spawnpoints.Add(newSpawn);
                        added++;
                    }

                    foreach (var zone in map.LootZones)
                    {
                        if (zone.Forced)
                        {
                            continue;
                        }

                        if (!QuestConditionsMet(zone.QuestOnly, zone.QuestCompleted, zone.QuestId))
                        {
                            continue;
                        }

                        // Zone-level spawn chance acts as a master switch for this zone
                        if (zone.SpawnChance < 100.0 && RandX.Next() * 100.0 >= zone.SpawnChance)
                        {
                            continue;
                        }

                        for (int i = 0; i < zone.Items.Count; i++)
                        {
                            var item = zone.Items[i];
                            if (item.Chance < 100.0 && RandX.Next() * 100.0 >= item.Chance)
                            {
                                continue;
                            }

                            if (!ShouldSpawnItem(item))
                            {
                                continue;
                            }

                            var locationId = $"{zone.Id}_{i}";
                            var newSpawn = CreateZoneItemSpawnpoint(zone, item, i, random);
                            if (existingIds.Contains(locationId))
                            {
                                var idx = spawnpoints.FindIndex(s => s.LocationId == locationId);
                                if (idx >= 0)
                                {
                                    spawnpoints[idx] = newSpawn;
                                    ServerPlugin.Logger?.Info($"[MEL] Overrode vanilla loot zone item {locationId}.");
                                }
                                continue;
                            }

                            if (!existingIds.Add(locationId))
                            {
                                continue;
                            }

                            spawnpoints.Add(newSpawn);
                            added++;
                        }
                    }
                }

                if (added > 0)
                    ServerPlugin.Logger?.Info($"[MEL] Added {added} custom loot spawnpoints to {locationId}.");

                looseLootObj.Spawnpoints = spawnpoints;
                return looseLootObj;
            });

            registered++;
        }

        ServerPlugin.Logger?.Info($"[MEL] Registered loot transformers for {registered} locations (skipped {skipped} already-registered).");
    }

    private static Spawnpoint CreateSpawnpoint(LooseLootSpawn spawn, List<LootItem> filteredItems)
    {
        var items = BuildItems(filteredItems, spawn.Id);
        var rootId = items.Count > 0 ? items[0].Id : new MongoId();

        return new Spawnpoint
        {
            LocationId = spawn.Id,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = spawn.Id,
                IsContainer = false,
                UseGravity = spawn.UseGravity,
                RandomRotation = false,
                Position = new Vector3 { X = (float)spawn.Position.X, Y = (float)spawn.Position.Y, Z = (float)spawn.Position.Z },
                Rotation = new Vector3 { X = (float)spawn.Rotation.X, Y = (float)spawn.Rotation.Y, Z = (float)spawn.Rotation.Z },
                IsAlwaysSpawn = false,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items = items
            },
            ItemDistribution = BuildItemDistribution(filteredItems, items)
        };
    }

    private static Spawnpoint CreateZoneItemSpawnpoint(LootZone zone, LootItem item, int index, Random random)
    {
        var itemTpl = string.IsNullOrWhiteSpace(item.Template) ? "544fb45d4bdc2dee738b4568" : item.Template;
        var locationId = $"{zone.Id}_{index}";
        var composedKey = $"{zone.Id}_{itemTpl}_{index}";
        var position = RandomPointInShape(zone, random);
        var rotation = item.RandomRotation ? RandomYRotation(random) : item.Rotation;

        var items = new List<SptLootItem>();
        var rootId = new MongoId();
        var root = new SptLootItem
        {
            Id = rootId,
            Template = itemTpl,
            ComposedKey = composedKey,
            Upd = new Upd { SpawnedInSession = true }
        };
        items.Add(root);

        if (item.Children != null)
        {
            foreach (var child in item.Children)
                AddZoneChildItemRecursive(child, root, items, composedKey);
        }

        return new Spawnpoint
        {
            LocationId = locationId,
            Probability = 1.0,
            Template = new SpawnpointTemplate
            {
                Id = locationId,
                IsContainer = false,
                UseGravity = zone.UseGravity,
                RandomRotation = false,
                Position = new Vector3 { X = (float)position.X, Y = (float)position.Y, Z = (float)position.Z },
                Rotation = new Vector3 { X = (float)rotation.X, Y = (float)rotation.Y, Z = (float)rotation.Z },
                IsAlwaysSpawn = false,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items = items
            },
            ItemDistribution =
            [
                new LooseLootItemDistribution
                {
                    ComposedKey = new ComposedKey { Key = composedKey },
                    RelativeProbability = 1
                }
            ]
        };
    }

    private static void AddZoneChildItemRecursive(LootItem item, SptLootItem parent, List<SptLootItem> result, string parentComposedKey)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Template))
            return;

        var id = new MongoId();
        var spt = new SptLootItem
        {
            Id = id,
            Template = item.Template,
            ParentId = parent.Id.ToString(),
            SlotId = item.SlotId ?? string.Empty,
            ComposedKey = $"{parentComposedKey}_{item.Template}_{result.Count}",
            Upd = new Upd { SpawnedInSession = true }
        };

        result.Add(spt);

        if (item.Children != null)
        {
            foreach (var child in item.Children)
                AddZoneChildItemRecursive(child, spt, result, parentComposedKey);
        }
    }

    private static List<SptLootItem> BuildItems(List<LootItem> items, string markerId)
    {
        if (items == null || items.Count == 0)
        {
            return
            [
                new SptLootItem
                {
                    Id = new MongoId(),
                    Template = "544fb45d4bdc2dee738b4568",
                    ComposedKey = $"{markerId}_544fb45d4bdc2dee738b4568",
                    Upd = new Upd { SpawnedInSession = true }
                }
            ];
        }

        var result = new List<SptLootItem>();
        for (int i = 0; i < items.Count; i++)
            AddLootItemRecursive(items[i], null, result, markerId, i);
        return result;
    }

    private static void AddLootItemRecursive(LootItem item, SptLootItem? parent, List<SptLootItem> result, string markerId, int index)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Template))
            return;

        var id = new MongoId();
        var tpl = item.Template;
        var spt = new SptLootItem
        {
            Id = id,
            Template = tpl,
            ParentId = parent == null ? null : parent.Id.ToString(),
            SlotId = item.SlotId ?? string.Empty,
            ComposedKey = $"{markerId}_{tpl}_{index}_{result.Count}",
            Upd = new Upd { SpawnedInSession = true }
        };

        result.Add(spt);

        if (item.Children != null)
        {
            foreach (var child in item.Children)
                AddLootItemRecursive(child, spt, result, markerId, index);
        }
    }

    private static bool ShouldSpawnItem(LootItem item)
    {
        if (item == null)
            return true;

        return QuestConditionsMet(item.QuestOnly, item.QuestCompleted, item.QuestId);
    }

    private static bool QuestConditionsMet(bool questOnly, bool questCompleted, string questId)
    {
        if (!questOnly && !questCompleted)
            return true;

        if (string.IsNullOrWhiteSpace(questId))
            return true;

        var active = questOnly && QuestFilter.IsQuestActive(questId);
        var completed = questCompleted && QuestFilter.IsQuestCompleted(questId);

        return active || completed;
    }

    private static List<LooseLootItemDistribution> BuildItemDistribution(List<LootItem> sourceItems, List<SptLootItem> sptItems)
    {
        if (sourceItems == null || sourceItems.Count == 0 || sptItems.Count == 0)
        {
            return
            [
                new LooseLootItemDistribution
                {
                    ComposedKey = new ComposedKey { Key = sptItems[0].ComposedKey ?? string.Empty },
                    RelativeProbability = 100
                }
            ];
        }

        var distribution = new List<LooseLootItemDistribution>();
        var rootIndex = 0;
        foreach (var spt in sptItems)
        {
            // Only create distribution entries for root items (no parent); child items spawn with their parent
            if (!string.IsNullOrEmpty(spt.ParentId))
                continue;

            var chance = rootIndex < sourceItems.Count ? sourceItems[rootIndex].Chance : 0;
            rootIndex++;
            distribution.Add(new LooseLootItemDistribution
            {
                ComposedKey = new ComposedKey { Key = spt.ComposedKey ?? string.Empty },
                RelativeProbability = chance > 0 ? chance : 0
            });
        }

        if (distribution.Count == 0 && sptItems.Count > 0)
        {
            distribution.Add(new LooseLootItemDistribution
            {
                ComposedKey = new ComposedKey { Key = sptItems[0].ComposedKey ?? string.Empty },
                RelativeProbability = 100
            });
        }

        return distribution;
    }

    private static TransformData RandomPointInShape(LootZone zone, Random random)
    {
        var scale = zone.Scale;
        if (scale == null || (scale.X == 0 && scale.Y == 0 && scale.Z == 0))
            scale = new TransformData { X = 1, Y = 1, Z = 1 };

        var angle = random.NextDouble() * Math.PI * 2;
        var radius = zone.Radius * scale.X;

        switch (zone.Shape)
        {
            case ZoneShape.Box:
                return new TransformData
                {
                    X = zone.Position.X + (random.NextDouble() - 0.5) * scale.X,
                    Y = zone.Position.Y,
                    Z = zone.Position.Z + (random.NextDouble() - 0.5) * scale.Z
                };
            case ZoneShape.Cylinder:
            case ZoneShape.Capsule:
                var cylR = radius * Math.Sqrt(random.NextDouble());
                return new TransformData
                {
                    X = zone.Position.X + cylR * Math.Cos(angle),
                    Y = zone.Position.Y,
                    Z = zone.Position.Z + cylR * Math.Sin(angle)
                };
            default:
                var sphereR = radius * Math.Sqrt(random.NextDouble());
                return new TransformData
                {
                    X = zone.Position.X + sphereR * Math.Cos(angle),
                    Y = zone.Position.Y,
                    Z = zone.Position.Z + sphereR * Math.Sin(angle)
                };
        }
    }

    private static TransformData RandomEuler(Random random)
    {
        return new TransformData
        {
            X = random.NextDouble() * 360,
            Y = random.NextDouble() * 360,
            Z = random.NextDouble() * 360
        };
    }

    private static TransformData RandomYRotation(Random random)
    {
        return new TransformData
        {
            X = 0,
            Y = random.NextDouble() * 360,
            Z = 0
        };
    }
}
