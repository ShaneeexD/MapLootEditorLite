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
            var key = new MongoId(container.ContainerId);
            if (dict.Contains(key))
            {
                ServerPlugin.Logger?.Debug($"[MLEL] Container {container.ContainerId} already exists in static loot; skipping.");
                continue;
            }

            var details = CreateStaticLootDetails(container);
            if (details != null)
            {
                dict.Add(key, details);
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
        var sptTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

        var itemDistributionType = sptTypes.FirstOrDefault(t => t.Name == "ItemDistribution" && t.Namespace?.Contains("Eft.Common") == true);
        var itemCountDistributionType = sptTypes.FirstOrDefault(t => t.Name == "ItemCountDistribution" && t.Namespace?.Contains("Eft.Common") == true);

        if (itemDistributionType == null || itemCountDistributionType == null)
        {
            ServerPlugin.Logger?.Warning("[MLEL] ItemDistribution or ItemCountDistribution type not found; cannot create container loot entry.");
            return null;
        }

        var itemDistributionList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemDistributionType))!;
        var itemDistTplProp = itemDistributionType.GetProperty("Tpl", BindingFlags.Public | BindingFlags.Instance);
        var itemDistRelProp = itemDistributionType.GetProperty("RelativeProbability", BindingFlags.Public | BindingFlags.Instance);

        foreach (var item in items)
        {
            var dist = Activator.CreateInstance(itemDistributionType)!;
            SetDistributionProperty(itemDistTplProp, dist, item.Template);
            SetNumericProperty(itemDistRelProp, dist, item.Chance);
            itemDistributionList.Add(dist);
        }

        if (itemDistributionList.Count == 0)
        {
            var dist = Activator.CreateInstance(itemDistributionType)!;
            SetDistributionProperty(itemDistTplProp, dist, "544fb45d4bdc2dee738b4568");
            SetNumericProperty(itemDistRelProp, dist, 1);
            itemDistributionList.Add(dist);
        }

        var itemCountDistributionList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemCountDistributionType))!;
        var countProp = itemCountDistributionType.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        var countRelProp = itemCountDistributionType.GetProperty("RelativeProbability", BindingFlags.Public | BindingFlags.Instance);
        var countDist = Activator.CreateInstance(itemCountDistributionType)!;
        SetNumericProperty(countProp, countDist, 1);
        SetNumericProperty(countRelProp, countDist, 1);
        itemCountDistributionList.Add(countDist);

        var itemDistProp = type.GetProperty("ItemDistribution", BindingFlags.Public | BindingFlags.Instance);
        itemDistProp?.SetValue(instance, itemDistributionList);

        var itemCountProp = type.GetProperty("ItemCountDistribution", BindingFlags.Public | BindingFlags.Instance);
        itemCountProp?.SetValue(instance, itemCountDistributionList);

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
        var dataType = data.GetType();
        var containerDetailsType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "StaticContainerDetails");

        if (containerDetailsType != null && containerDetailsType.IsAssignableFrom(dataType))
        {
            var staticContainersProp = dataType.GetProperty("StaticContainers", BindingFlags.Public | BindingFlags.Instance);
            if (staticContainersProp == null)
            {
                ServerPlugin.Logger?.Warning("[MLEL] StaticContainerDetails has no StaticContainers property.");
                return data;
            }

            var existingEnumerable = staticContainersProp.GetValue(data) as IEnumerable<object>;
            var existingList = existingEnumerable?.ToList() ?? new List<object>();
            var itemType = GetEnumerableElementType(staticContainersProp.PropertyType) ?? typeof(object);

            foreach (var container in containers)
            {
                if (existingList.Any(x => GetStaticContainerId(x) == container.ContainerId))
                {
                    ServerPlugin.Logger?.Debug($"[MLEL] Container {container.ContainerId} already exists in static containers; skipping.");
                    continue;
                }

                var containerData = CreateStaticContainerData(container, itemType);
                if (containerData != null)
                    existingList.Add(containerData);
            }

            var listType = typeof(List<>).MakeGenericType(itemType);
            var newList = (System.Collections.IList)Activator.CreateInstance(listType)!;
            foreach (var item in existingList)
                newList.Add(item);
            staticContainersProp.SetValue(data, newList);
            return data;
        }

        ServerPlugin.Logger?.Warning($"[MLEL] StaticContainers data type is {dataType.FullName}; expected StaticContainerDetails.");
        return data;
    }

    private static string? GetStaticContainerId(object containerData)
    {
        var template = containerData.GetType().GetProperty("Template", BindingFlags.Public | BindingFlags.Instance)?.GetValue(containerData);
        return template?.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template) as string;
    }

    private static object? CreateStaticContainerData(InteractiveObject container, Type staticContainerDataType)
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
            return null;

        var rootId = new MongoId();
        var containerTemplate = string.IsNullOrWhiteSpace(container.ContainerTemplate) ? "578f87a3245977356274f2cb" : container.ContainerTemplate;

        templateType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, container.ContainerId);
        templateType.GetProperty("IsContainer", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, true);
        templateType.GetProperty("UseGravity", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, false);
        templateType.GetProperty("RandomRotation", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, false);
        templateType.GetProperty("Position", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, new XYZ { X = container.Position.X, Y = container.Position.Y, Z = container.Position.Z });
        templateType.GetProperty("Rotation", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, new XYZ { X = container.Rotation.X, Y = container.Rotation.Y, Z = container.Rotation.Z });
        templateType.GetProperty("IsAlwaysSpawn", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, true);
        templateType.GetProperty("IsGroupPosition", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, false);
        var groupPositionType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "GroupPosition");
        templateType.GetProperty("GroupPositions", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, groupPositionType != null ? Array.CreateInstance(groupPositionType, 0) : Array.Empty<object>());

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
        itemType.GetProperty("Template", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, new MongoId(containerTemplate));
        itemType.GetProperty("ComposedKey", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, $"{container.ContainerId}_root");
        itemType.GetProperty("Upd", BindingFlags.Public | BindingFlags.Instance)?.SetValue(item, new Upd { SpawnedInSession = true });

        templateType.GetProperty("Root", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, rootId.ToString());
        templateType.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)?.SetValue(template, Array.CreateInstance(itemType, 1));
        ((System.Collections.IList)templateType.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template)!)[0] = item;

        var containerData = Activator.CreateInstance(staticContainerDataType);
        if (containerData == null)
            return null;

        staticContainerDataType.GetProperty("Probability", BindingFlags.Public | BindingFlags.Instance)?.SetValue(containerData, 1.0f);
        staticContainerDataType.GetProperty("Template", BindingFlags.Public | BindingFlags.Instance)?.SetValue(containerData, template);

        return containerData;
    }

    private static void SetDistributionProperty(PropertyInfo? prop, object target, string templateId)
    {
        if (prop == null) return;
        object value = prop.PropertyType == typeof(MongoId) ? new MongoId(templateId) : templateId;
        prop.SetValue(target, value);
    }

    private static void SetNumericProperty(PropertyInfo? prop, object target, object value)
    {
        if (prop == null) return;
        var propType = prop.PropertyType;
        if (propType == typeof(float) || propType == typeof(float?))
            value = Convert.ToSingle(value);
        else if (propType == typeof(double) || propType == typeof(double?))
            value = Convert.ToDouble(value);
        else if (propType == typeof(int) || propType == typeof(int?))
            value = Convert.ToInt32(value);
        prop.SetValue(target, value);
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(IEnumerable<>) || genericDef == typeof(List<>) || genericDef == typeof(IList<>) || genericDef == typeof(ICollection<>))
                return type.GetGenericArguments().First();
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments().First();
        }

        return null;
    }
}
