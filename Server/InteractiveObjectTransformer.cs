using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;

namespace MapLootEditorLite.Server;

public static class InteractiveObjectTransformer
{
    private static readonly HashSet<string> RegisteredContainerIds = new HashSet<string>();

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

            var containers = maps.SelectMany(m => m.InteractiveObjects ?? new List<InteractiveObject>())
                .Where(o => o.InteractiveType == InteractiveObjectType.Container)
                .ToList();

            if (containers.Count == 0)
            {
                continue;
            }

            RegisterStaticLoot(location, containers, locationId);
            RegisterStaticContainers(location, containers, locationId);
            registered++;
        }

        ServerPlugin.Logger?.Info($"[MLEL] Registered interactive object transformers for {registered} locations");
    }

    private static void RegisterStaticLoot(object location, List<InteractiveObject> containers, string locationId)
    {
        try
        {
            var staticLootProp = location.GetType().GetProperty("StaticLoot", BindingFlags.Public | BindingFlags.Instance);
            if (staticLootProp == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' has no StaticLoot property; container loot will not be registered.");
                return;
            }

            var staticLoot = staticLootProp.GetValue(location);
            if (staticLoot == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' StaticLoot is null; container loot will not be registered.");
                return;
            }

            var addTransformer = staticLoot.GetType().GetMethod("AddTransformer", BindingFlags.Public | BindingFlags.Instance);
            if (addTransformer == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' StaticLoot has no AddTransformer method; container loot will not be registered.");
                return;
            }

            var transformedContainers = containers
                .Where(c => !string.IsNullOrWhiteSpace(c.ContainerId) && RegisteredContainerIds.Add(c.ContainerId))
                .ToList();

            if (transformedContainers.Count == 0)
            {
                return;
            }

            var parameterType = addTransformer.GetParameters().First().ParameterType;
            var genericArg = parameterType.GetGenericArguments().FirstOrDefault();
            if (genericArg == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' StaticLoot transformer argument is not a Func<T>; container loot will not be registered.");
                return;
            }

            var createMethod = typeof(InteractiveObjectTransformer).GetMethod(nameof(CreateStaticLootTransform), BindingFlags.NonPublic | BindingFlags.Static)!;
            var createGeneric = createMethod.MakeGenericMethod(genericArg);
            var del = createGeneric.Invoke(null, new object[] { transformedContainers });

            addTransformer.Invoke(staticLoot, new object[] { del });
            ServerPlugin.Logger?.Info($"[MLEL] Registered {transformedContainers.Count} custom container loot entries for {locationId}");
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MLEL] Failed to register static loot for {locationId}: {ex.Message}");
        }
    }

    private static Func<T, T> CreateStaticLootTransform<T>(List<InteractiveObject> containers) where T : class
    {
        return data => TransformStaticLoot(data, containers);
    }

    private static T TransformStaticLoot<T>(T data, List<InteractiveObject> containers) where T : class
    {
        var dict = data as IDictionary;
        if (dict == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] StaticLoot is not a dictionary; cannot add custom container loot.");
            return data;
        }

        foreach (var container in containers)
        {
            if (dict.Contains(container.ContainerId))
            {
                ServerPlugin.Logger?.Debug($"[MLEL] Container {container.ContainerId} already exists in static loot; skipping.");
                continue;
            }

            var details = CreateStaticLootDetails(container);
            if (details != null)
            {
                dict.Add(container.ContainerId, details);
            }
        }

        return data;
    }

    private static object? CreateStaticLootDetails(InteractiveObject container)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "StaticLootDetails");

        if (type == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] StaticLootDetails type not found; cannot create container loot entry.");
            return null;
        }

        var instance = Activator.CreateInstance(type);
        if (instance == null)
        {
            return null;
        }

        var items = container.Items ?? new List<LootItem>();
        var itemDistribution = new List<ItemDistribution>();
        var itemCountDistribution = new List<object>();

        foreach (var item in items)
        {
            itemDistribution.Add(new ItemDistribution
            {
                Tpl = item.Template,
                RelativeProbability = (int)item.Chance
            });
        }

        if (itemDistribution.Count == 0)
        {
            itemDistribution.Add(new ItemDistribution { Tpl = "544fb45d4bdc2dee738b4568", RelativeProbability = 1 });
        }

        itemCountDistribution.Add(new { count = 1, relativeProbability = 1 });

        var itemDistProp = type.GetProperty("ItemDistribution", BindingFlags.Public | BindingFlags.Instance);
        itemDistProp?.SetValue(instance, itemDistribution.ToArray());

        var itemCountProp = type.GetProperty("ItemCountDistribution", BindingFlags.Public | BindingFlags.Instance);
        itemCountProp?.SetValue(instance, itemCountDistribution.ToArray());

        return instance;
    }

    private static void RegisterStaticContainers(object location, List<InteractiveObject> containers, string locationId)
    {
        try
        {
            var staticContainersProp = location.GetType().GetProperty("StaticContainers", BindingFlags.Public | BindingFlags.Instance);
            if (staticContainersProp == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' has no StaticContainers property; custom containers will not be registered.");
                return;
            }

            var staticContainers = staticContainersProp.GetValue(location);
            if (staticContainers == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' StaticContainers is null; custom containers will not be registered.");
                return;
            }

            var addTransformer = staticContainers.GetType().GetMethod("AddTransformer", BindingFlags.Public | BindingFlags.Instance);
            if (addTransformer == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' StaticContainers has no AddTransformer method; custom containers will not be registered.");
                return;
            }

            var parameterType = addTransformer.GetParameters().First().ParameterType;
            var genericArg = parameterType.GetGenericArguments().FirstOrDefault();
            if (genericArg == null)
            {
                ServerPlugin.Logger?.Warning($"[MLEL] Location '{locationId}' StaticContainers transformer argument is not a Func<T>; custom containers will not be registered.");
                return;
            }

            var transformedContainers = containers
                .Where(c => !string.IsNullOrWhiteSpace(c.ContainerId))
                .ToList();

            if (transformedContainers.Count == 0)
            {
                return;
            }

            var createMethod = typeof(InteractiveObjectTransformer).GetMethod(nameof(CreateStaticContainersTransform), BindingFlags.NonPublic | BindingFlags.Static)!;
            var createGeneric = createMethod.MakeGenericMethod(genericArg);
            var del = createGeneric.Invoke(null, new object[] { transformedContainers });

            addTransformer.Invoke(staticContainers, new object[] { del });
            ServerPlugin.Logger?.Info($"[MLEL] Registered {transformedContainers.Count} custom static containers for {locationId}");
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MLEL] Failed to register static containers for {locationId}: {ex.Message}");
        }
    }

    private static Func<T, T> CreateStaticContainersTransform<T>(List<InteractiveObject> containers) where T : class
    {
        return data => TransformStaticContainers(data, containers);
    }

    private static T TransformStaticContainers<T>(T data, List<InteractiveObject> containers) where T : class
    {
        var list = data as IList;
        if (list == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] StaticContainers is not a list; cannot add custom containers.");
            return data;
        }

        var elementType = list.GetType().IsGenericType ? list.GetType().GetGenericArguments().First() : typeof(object);
        var spawnpointType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "Spawnpoint");

        if (spawnpointType == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] Spawnpoint type not found; cannot create custom container entries.");
            return data;
        }

        foreach (var container in containers)
        {
            var existing = list.OfType<object>().FirstOrDefault(x => GetSpawnpointId(x) == container.ContainerId);
            if (existing != null)
            {
                ServerPlugin.Logger?.Debug($"[MLEL] Container {container.ContainerId} already exists in static containers; skipping.");
                continue;
            }

            var spawnpoint = CreateContainerSpawnpoint(container, spawnpointType);
            if (spawnpoint != null)
            {
                list.Add(spawnpoint);
            }
        }

        return data;
    }

    private static string? GetSpawnpointId(object spawnpoint)
    {
        var template = spawnpoint.GetType().GetProperty("Template", BindingFlags.Public | BindingFlags.Instance)?.GetValue(spawnpoint);
        return template?.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template) as string;
    }

    private static object? CreateContainerSpawnpoint(InteractiveObject container, Type spawnpointType)
    {
        var templateType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "SpawnpointTemplate");

        if (templateType == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] SpawnpointTemplate type not found; cannot create custom container entry.");
            return null;
        }

        var template = Activator.CreateInstance(templateType);
        if (template == null)
        {
            return null;
        }

        templateType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, container.ContainerId);
        templateType.GetProperty("IsContainer", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, true);
        templateType.GetProperty("UseGravity", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, false);
        templateType.GetProperty("RandomRotation", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, false);
        templateType.GetProperty("Position", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, new XYZ { X = container.Position.X, Y = container.Position.Y, Z = container.Position.Z });
        templateType.GetProperty("Rotation", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, new XYZ { X = container.Rotation.X, Y = container.Rotation.Y, Z = container.Rotation.Z });
        templateType.GetProperty("IsAlwaysSpawn", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, true);
        templateType.GetProperty("IsGroupPosition", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, false);
        templateType.GetProperty("GroupPositions", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, Array.CreateInstance(typeof(XYZ), 0));

        var rootId = container.ContainerId;
        var itemType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "SptLootItem");

        if (itemType == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] SptLootItem type not found; cannot create custom container root item.");
            return null;
        }

        var item = Activator.CreateInstance(itemType);
        itemType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, rootId);
        var containerTemplate = string.IsNullOrWhiteSpace(container.ContainerTemplate) ? "578f87a3245977356274f2cb" : container.ContainerTemplate;
        itemType.GetProperty("Template", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, containerTemplate);
        itemType.GetProperty("ComposedKey", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, $"{container.ContainerId}_root");
        itemType.GetProperty("Upd", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, new Upd { SpawnedInSession = true });

        templateType.GetProperty("Root", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, rootId);
        templateType.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, Array.CreateInstance(itemType, 1));
        ((IList)templateType.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template)!)[0] = item;

        var spawnpoint = Activator.CreateInstance(spawnpointType);
        spawnpointType.GetProperty("LocationId", BindingFlags.Public | BindingFlags.Instance)?.SetValue(spawnpoint, container.ContainerId);
        spawnpointType.GetProperty("Probability", BindingFlags.Public | BindingFlags.Instance)?.SetValue(spawnpoint, 1.0);
        spawnpointType.GetProperty("Template", BindingFlags.Public | BindingFlags.Instance)?.SetValue(spawnpoint, template);

        return spawnpoint;
    }
}
