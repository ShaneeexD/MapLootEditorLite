using System;
using System.Collections.Generic;
using System.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;

namespace MapLootEditorLite.Server;

public static class LootTransformer
{
    private static readonly HashSet<string> RegisteredSpawnIds = new HashSet<string>();

    public static void Register(DatabaseService databaseService)
    {
        var locations = databaseService.GetLocations().GetDictionary();
        var registered = 0;

        foreach (var (locationId, location) in locations)
        {
            var maps = PackRegistry.GetMapsForLocation(locationId).ToList();
            if (maps.Count == 0)
            {
                continue;
            }

            location.LooseLoot?.AddTransformer(looseLoot =>
            {
                if (looseLoot == null)
                {
                    return looseLoot;
                }

                var spawnpoints = looseLoot.Spawnpoints?.ToList() ?? [];
                var random = new Random();

                foreach (var map in maps)
                {
                    foreach (var spawn in map.LootSpawns)
                    {
                        if (spawn.Forced || !RegisteredSpawnIds.Add(spawn.Id))
                        {
                            continue;
                        }

                        spawnpoints.Add(CreateSpawnpoint(spawn));
                    }

                    foreach (var zone in map.LootZones)
                    {
                        if (zone.Forced)
                        {
                            continue;
                        }

                        for (int i = 0; i < zone.Items.Count; i++)
                        {
                            var locationId = $"{zone.Id}_{i}";
                            if (RegisteredSpawnIds.Add(locationId))
                            {
                                spawnpoints.Add(CreateZoneItemSpawnpoint(zone, zone.Items[i], i, random));
                            }
                        }
                    }
                }

                looseLoot.Spawnpoints = spawnpoints;
                return looseLoot;
            });

            registered++;
        }

        ServerPlugin.Logger?.Info($"[MLEL] Registered loot transformers for {registered} locations");
    }

    private static Spawnpoint CreateSpawnpoint(LooseLootSpawn spawn)
    {
        var totalChance = TotalItemChance(spawn.Items) * spawn.SpawnChance / 100.0;
        var items = BuildItems(spawn.Items, spawn.Id);
        var rootId = items.Count > 0 ? items[0].Id : new MongoId();

        return new Spawnpoint
        {
            LocationId = spawn.Id,
            Probability = Math.Min(totalChance / 100.0, 1.0),
            Template = new SpawnpointTemplate
            {
                Id = spawn.Id,
                IsContainer = false,
                UseGravity = false,
                RandomRotation = false,
                Position = new XYZ { X = spawn.Position.X, Y = spawn.Position.Y, Z = spawn.Position.Z },
                Rotation = new XYZ { X = spawn.Rotation.X, Y = spawn.Rotation.Y, Z = spawn.Rotation.Z },
                IsAlwaysSpawn = false,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items = items
            },
            ItemDistribution = BuildItemDistribution(spawn.Items, items)
        };
    }

    private static Spawnpoint CreateZoneItemSpawnpoint(LootZone zone, LootItem item, int index, Random random)
    {
        var itemTpl = string.IsNullOrWhiteSpace(item.Template) ? "544fb45d4bdc2dee738b4568" : item.Template;
        var locationId = $"{zone.Id}_{index}";
        var rootId = new MongoId();
        var composedKey = $"{zone.Id}_{itemTpl}_{index}";
        var position = RandomPointInShape(zone, random);
        var rotation = item.RandomRotation ? RandomEuler(random) : item.Rotation;

        return new Spawnpoint
        {
            LocationId = locationId,
            Probability = zone.SpawnChance * item.Chance / 10000.0,
            Template = new SpawnpointTemplate
            {
                Id = locationId,
                IsContainer = false,
                UseGravity = false,
                RandomRotation = item.RandomRotation,
                Position = new XYZ { X = position.X, Y = position.Y, Z = position.Z },
                Rotation = new XYZ { X = rotation.X, Y = rotation.Y, Z = rotation.Z },
                IsAlwaysSpawn = false,
                IsGroupPosition = false,
                GroupPositions = [],
                Root = rootId,
                Items =
                [
                    new SptLootItem
                    {
                        Id = rootId,
                        Template = itemTpl,
                        ComposedKey = composedKey,
                        Upd = new Upd { SpawnedInSession = true }
                    }
                ]
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

    private static double TotalItemChance(List<LootItem> items)
    {
        if (items == null || items.Count == 0)
            return 0;

        var total = items.Sum(i => i.Chance);
        return total > 0 ? total : 0;
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

        return items.Select((item, index) =>
        {
            var tpl = string.IsNullOrWhiteSpace(item.Template) ? "544fb45d4bdc2dee738b4568" : item.Template;
            return new SptLootItem
            {
                Id = new MongoId(),
                Template = tpl,
                ComposedKey = $"{markerId}_{tpl}_{index}",
                Upd = new Upd { SpawnedInSession = true }
            };
        }).ToList();
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
        for (int i = 0; i < sptItems.Count; i++)
        {
            var chance = i < sourceItems.Count ? sourceItems[i].Chance : 0;
            distribution.Add(new LooseLootItemDistribution
            {
                ComposedKey = new ComposedKey { Key = sptItems[i].ComposedKey ?? string.Empty },
                RelativeProbability = chance > 0 ? chance : 0
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
}
