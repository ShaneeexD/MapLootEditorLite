using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Location;
using SPTarkov.Server.Core.Services;

namespace MapLootEditorLite.Server;

[HarmonyPatch(typeof(LocationLifecycleService))]
[HarmonyPatch(nameof(LocationLifecycleService.GenerateLocationAndLoot))]
public static class LootInjector
{
    public static void Postfix(ref LocationBase __result)
    {
        if (__result?.IdField is null)
        {
            return;
        }

        var locationId = __result.IdField;
        var customMaps = PackRegistry.GetMapsForLocation(locationId).ToList();
        if (customMaps.Count == 0)
        {
            return;
        }

        var random = new Random();
        var added = new List<SpawnpointTemplate>();

        foreach (var map in customMaps)
        {
            foreach (var spawn in map.LootSpawns)
            {
                if (spawn.Forced)
                {
                    continue;
                }

                if (spawn.SpawnChance < 100 && random.NextDouble() * 100 > spawn.SpawnChance)
                {
                    continue;
                }

                added.Add(CreateSpawnpointTemplate(spawn, false));
            }

            foreach (var zone in map.LootZones)
            {
                if (zone.Forced)
                {
                    continue;
                }

                if (zone.SpawnChance < 100 && random.NextDouble() * 100 > zone.SpawnChance)
                {
                    continue;
                }

                var position = RandomPointInCylinder(zone.Position, zone.Radius, random);
                added.Add(CreateSpawnpointTemplate(zone, position, zone.Rotation));
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        var existing = __result.Loot ?? [];
        __result.Loot = existing.Concat(added).ToList();

        ServerPlugin.Logger?.Info($"[MLEL] Injected {added.Count} custom loot spawns into {locationId}");
    }

    private static SpawnpointTemplate CreateSpawnpointTemplate(LooseLootSpawn spawn, bool isAlwaysSpawn)
    {
        var itemTpl = spawn.ItemTpls.FirstOrDefault() ?? "544fb45d4bdc2dee738b4568";
        var rootId = new MongoId();
        return new SpawnpointTemplate
        {
            Id = spawn.Id,
            IsContainer = false,
            UseGravity = false,
            RandomRotation = false,
            Position = new XYZ { X = spawn.Position.X, Y = spawn.Position.Y, Z = spawn.Position.Z },
            Rotation = new XYZ { X = spawn.Rotation.X, Y = spawn.Rotation.Y, Z = spawn.Rotation.Z },
            IsAlwaysSpawn = isAlwaysSpawn,
            IsGroupPosition = false,
            GroupPositions = [],
            Root = rootId,
            Items =
            [
                new SptLootItem
                {
                    Id = rootId,
                    Template = itemTpl,
                    ComposedKey = $"{spawn.Id}_{itemTpl}",
                    Upd = new Upd { SpawnedInSession = true }
                }
            ]
        };
    }

    private static SpawnpointTemplate CreateSpawnpointTemplate(LootZone zone, TransformData position, TransformData rotation)
    {
        var itemTpl = zone.ItemTpls.FirstOrDefault() ?? "544fb45d4bdc2dee738b4568";
        var rootId = new MongoId();
        return new SpawnpointTemplate
        {
            Id = zone.Id,
            IsContainer = false,
            UseGravity = false,
            RandomRotation = false,
            Position = new XYZ { X = position.X, Y = position.Y, Z = position.Z },
            Rotation = new XYZ { X = rotation.X, Y = rotation.Y, Z = rotation.Z },
            IsAlwaysSpawn = true,
            IsGroupPosition = false,
            GroupPositions = [],
            Root = rootId,
            Items =
            [
                new SptLootItem
                {
                    Id = rootId,
                    Template = itemTpl,
                    ComposedKey = $"{zone.Id}_{itemTpl}",
                    Upd = new Upd { SpawnedInSession = true }
                }
            ]
        };
    }

    private static TransformData RandomPointInCylinder(TransformData center, double radius, Random random)
    {
        var angle = random.NextDouble() * Math.PI * 2;
        var r = radius * Math.Sqrt(random.NextDouble());
        var offsetX = r * Math.Cos(angle);
        var offsetZ = r * Math.Sin(angle);

        return new TransformData
        {
            X = center.X + offsetX,
            Y = center.Y,
            Z = center.Z + offsetZ,
        };
    }
}
