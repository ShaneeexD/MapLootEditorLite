using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;

namespace MapLootEditorLite.Server;

public static class InteractiveObjectTransformer
{
    // Tracks which StaticLoot/StaticContainers instances have already had transformers attached, so Register() can be safely called again when locations are regenerated.
    private static readonly ConditionalWeakTable<object, object> RegisteredStaticLoot = new ConditionalWeakTable<object, object>();
    private static readonly ConditionalWeakTable<object, object> RegisteredStaticContainers = new ConditionalWeakTable<object, object>();
    private static readonly Dictionary<string, SpawnpointTemplate> _weaponDonors = new(StringComparer.OrdinalIgnoreCase);
    private static DatabaseService? _databaseService;

    public static void Register(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        RegisterInternal();
    }

    public static void Register()
    {
        RegisterInternal();
    }

    private static void RegisterInternal()
    {
        if (_databaseService is null)
        {
            ServerPlugin.Logger?.Warning("[MEL] InteractiveObjectTransformer.Register() called before DatabaseService was set; skipping.");
            return;
        }

        var databaseService = _databaseService;
        var locations = databaseService!.GetLocations().GetDictionary();

        PreloadWeaponDonors(locations.Select(x => new KeyValuePair<string, object>(x.Key, x.Value)));

        var registered = 0;

        foreach (var (locationId, location) in locations)
        {
            var maps = PackRegistry.GetMapsForLocation(locationId).ToList();
            if (maps.Count == 0)
            {
                continue;
            }

            var interactiveObjects = maps.SelectMany(m => m.InteractiveObjects ?? new List<InteractiveObject>())
                .Where(ShouldSpawnObject)
                .ToList();

            var containers = interactiveObjects
                .Where(o => o.InteractiveType == InteractiveObjectType.Container && !string.IsNullOrWhiteSpace(o.ContainerId))
                .ToList();

            var weapons = interactiveObjects
                .Where(o => o.InteractiveType == InteractiveObjectType.StationaryWeapon && !string.IsNullOrWhiteSpace(o.WeaponTemplate))
                .ToList();

            if (containers.Count == 0 && weapons.Count == 0)
            {
                continue;
            }

            RegisterStaticLoot(location, containers, locationId);
            RegisterStaticContainers(location, containers, weapons, locationId);
            registered++;
        }

        ServerPlugin.Logger?.Info($"[MEL] Registered interactive object transformers for {registered} locations");
    }

    private static bool ShouldSpawnObject(InteractiveObject obj)
    {
        if (obj == null)
            return true;

        return QuestConditionsMet(obj.QuestOnly, obj.QuestCompleted, obj.QuestId);
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

    private static void RegisterStaticLoot(object location, List<InteractiveObject> containers, string locationId)
    {
        try
        {
            var staticLootProp = location.GetType().GetProperty("StaticLoot", BindingFlags.Public | BindingFlags.Instance);
            if (staticLootProp == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' has no StaticLoot property; container loot will not be registered.");
                return;
            }

            var staticLoot = staticLootProp.GetValue(location);
            if (staticLoot == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' StaticLoot is null; container loot will not be registered.");
                return;
            }

            var addTransformer = staticLoot.GetType().GetMethod("AddTransformer", BindingFlags.Public | BindingFlags.Instance);
            if (addTransformer == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' StaticLoot has no AddTransformer method; container loot will not be registered.");
                return;
            }

            if (RegisteredStaticLoot.TryGetValue(staticLoot, out _))
            {
                ServerPlugin.Logger?.Info($"[MEL] StaticLoot transformer already registered for {locationId}; skipping.");
                return;
            }
            RegisteredStaticLoot.Add(staticLoot, true);

            var transformedContainers = containers
                .Where(c => !string.IsNullOrWhiteSpace(c.ContainerId) && c.LootMode != ContainerLootMode.Custom)
                .ToList();

            if (transformedContainers.Count == 0)
            {
                return;
            }

            var parameterType = addTransformer.GetParameters().First().ParameterType;
            var genericArg = parameterType.GetGenericArguments().FirstOrDefault();
            if (genericArg == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' StaticLoot transformer argument is not a Func<T>; container loot will not be registered.");
                return;
            }

            var createMethod = typeof(InteractiveObjectTransformer).GetMethod(nameof(CreateStaticLootTransform), BindingFlags.NonPublic | BindingFlags.Static)!;
            var createGeneric = createMethod.MakeGenericMethod(genericArg);
            var del = createGeneric.Invoke(null, new object[] { transformedContainers });

            addTransformer.Invoke(staticLoot, new object[] { del });
            ServerPlugin.Logger?.Info($"[MEL] Registered {transformedContainers.Count} custom container loot entries for {locationId}");
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MEL] Failed to register static loot for {locationId}: {ex.Message}");
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
            ServerPlugin.Logger?.Warning("[MEL] StaticLoot is not a dictionary; cannot add custom container loot.");
            return data;
        }

        foreach (var container in containers)
        {
            var key = new MongoId(container.ContainerId);
            if (dict.Contains(key))
            {
                ServerPlugin.Logger?.Debug($"[MEL] Container {container.ContainerId} already exists in static loot; skipping.");
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
            ServerPlugin.Logger?.Warning("[MEL] StaticLootDetails type not found; cannot create container loot entry.");
            return null;
        }

        var instance = Activator.CreateInstance(type);
        if (instance == null)
        {
            return null;
        }

        // Default/Hybrid rely on external loot injectors (e.g., AmmoGen). Marker items are injected on the client.
        var items = new List<LootItem>();
        var sptTypes = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes());

        var itemDistributionType = sptTypes.FirstOrDefault(t => t.Name == "ItemDistribution" && t.Namespace?.Contains("Eft.Common") == true);
        var itemCountDistributionType = sptTypes.FirstOrDefault(t => t.Name == "ItemCountDistribution" && t.Namespace?.Contains("Eft.Common") == true);

        if (itemDistributionType == null || itemCountDistributionType == null)
        {
            ServerPlugin.Logger?.Warning("[MEL] ItemDistribution or ItemCountDistribution type not found; cannot create container loot entry.");
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

    private static void RegisterStaticContainers(object location, List<InteractiveObject> containers, List<InteractiveObject> weapons, string locationId)
    {
        try
        {
            var staticContainersProp = location.GetType().GetProperty("StaticContainers", BindingFlags.Public | BindingFlags.Instance);
            if (staticContainersProp == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' has no StaticContainers property; custom containers/weapons will not be registered.");
                return;
            }

            var staticContainers = staticContainersProp.GetValue(location);
            if (staticContainers == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' StaticContainers is null; custom containers/weapons will not be registered.");
                return;
            }

            var addTransformer = staticContainers.GetType().GetMethod("AddTransformer", BindingFlags.Public | BindingFlags.Instance);
            if (addTransformer == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' StaticContainers has no AddTransformer method; custom containers/weapons will not be registered.");
                return;
            }

            if (RegisteredStaticContainers.TryGetValue(staticContainers, out _))
            {
                ServerPlugin.Logger?.Info($"[MEL] StaticContainers transformer already registered for {locationId}; skipping.");
                return;
            }
            RegisteredStaticContainers.Add(staticContainers, true);

            var parameterType = addTransformer.GetParameters().First().ParameterType;
            var genericArg = parameterType.GetGenericArguments().FirstOrDefault();
            if (genericArg == null)
            {
                ServerPlugin.Logger?.Warning($"[MEL] Location '{locationId}' StaticContainers transformer argument is not a Func<T>; custom containers/weapons will not be registered.");
                return;
            }

            var transformedContainers = containers
                .Where(c => !string.IsNullOrWhiteSpace(c.ContainerId))
                .ToList();

            var transformedWeapons = weapons
                .Where(w => !string.IsNullOrWhiteSpace(w.WeaponTemplate))
                .ToList();

            if (transformedContainers.Count == 0 && transformedWeapons.Count == 0)
            {
                return;
            }

            var createMethod = typeof(InteractiveObjectTransformer).GetMethod(nameof(CreateStaticContainersTransform), BindingFlags.NonPublic | BindingFlags.Static)!;
            var createGeneric = createMethod.MakeGenericMethod(genericArg);
            var del = createGeneric.Invoke(null, new object[] { transformedContainers, transformedWeapons });

            addTransformer.Invoke(staticContainers, new object[] { del });
            var containerLog = transformedContainers.Count > 0 ? $"{transformedContainers.Count} custom static containers" : "";
            var weaponLog = transformedWeapons.Count > 0 ? $"{transformedWeapons.Count} custom stationary weapons" : "";
            var logParts = new[] { containerLog, weaponLog }.Where(s => !string.IsNullOrEmpty(s)).ToList();
            ServerPlugin.Logger?.Info($"[MEL] Registered {string.Join(" and ", logParts)} for {locationId}");
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MEL] Failed to register static containers/weapons for {locationId}: {ex.Message}");
        }
    }

    private static Func<T, T> CreateStaticContainersTransform<T>(List<InteractiveObject> containers, List<InteractiveObject> weapons) where T : class
    {
        return data => TransformStaticContainers(data, containers, weapons);
    }

    private static T TransformStaticContainers<T>(T data, List<InteractiveObject> containers, List<InteractiveObject> weapons) where T : class
    {
        var dataType = data.GetType();
        var containerDetailsType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "StaticContainerDetails");

        if (containerDetailsType == null || !containerDetailsType.IsAssignableFrom(dataType))
        {
            ServerPlugin.Logger?.Warning($"[MEL] StaticContainers data type is {dataType.FullName}; expected StaticContainerDetails.");
            return data;
        }

        var staticContainersProp = dataType.GetProperty("StaticContainers", BindingFlags.Public | BindingFlags.Instance);
        if (staticContainersProp != null)
        {
            var existingEnumerable = staticContainersProp.GetValue(data) as IEnumerable<object>;
            var existingList = existingEnumerable?.ToList() ?? new List<object>();
            var itemType = GetEnumerableElementType(staticContainersProp.PropertyType) ?? typeof(object);

            var addedContainers = 0;
            foreach (var container in containers)
            {
                if (existingList.Any(x => GetStaticContainerId(x) == container.ContainerId))
                {
                    ServerPlugin.Logger?.Debug($"[MEL] Container {container.ContainerId} already exists in static containers; skipping.");
                    continue;
                }

                var containerData = CreateStaticContainerData(container, itemType);
                if (containerData != null)
                {
                    existingList.Add(containerData);
                    addedContainers++;
                }
            }

            var listType = typeof(List<>).MakeGenericType(itemType);
            var newList = (System.Collections.IList)Activator.CreateInstance(listType)!;
            foreach (var item in existingList)
                newList.Add(item);
            staticContainersProp.SetValue(data, newList);
            ServerPlugin.Logger?.Info($"[MEL] StaticContainers transformer added {addedContainers} custom containers.");
        }

        var staticWeaponsProp = dataType.GetProperty("StaticWeapons", BindingFlags.Public | BindingFlags.Instance);
        if (staticWeaponsProp != null)
        {
            var existingEnumerable = staticWeaponsProp.GetValue(data) as IEnumerable<object>;
            var existingList = existingEnumerable?.ToList() ?? new List<object>();
            var itemType = GetEnumerableElementType(staticWeaponsProp.PropertyType) ?? typeof(SpawnpointTemplate);

            var addedWeapons = 0;
            foreach (var weapon in weapons)
            {
                if (existingList.Any(x => GetStaticWeaponId(x) == weapon.Id))
                {
                    ServerPlugin.Logger?.Debug($"[MEL] Weapon {weapon.Id} already exists in static weapons; skipping.");
                    continue;
                }

                var weaponData = CreateStaticWeaponData(weapon, itemType);
                if (weaponData != null)
                {
                    existingList.Add(weaponData);
                    addedWeapons++;
                }
            }

            var listType = typeof(List<>).MakeGenericType(itemType);
            var newList = (System.Collections.IList)Activator.CreateInstance(listType)!;
            foreach (var item in existingList)
                newList.Add(item);
            staticWeaponsProp.SetValue(data, newList);
            ServerPlugin.Logger?.Info($"[MEL] StaticWeapons transformer added {addedWeapons} custom stationary weapons.");
        }

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
            ServerPlugin.Logger?.Warning("[MEL] SpawnpointTemplate type not found; cannot create custom container entry.");
            return null;
        }

        var template = Activator.CreateInstance(templateType);
        if (template == null)
            return null;

        var rootId = new MongoId(container.ContainerId);
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
            ServerPlugin.Logger?.Warning("[MEL] SptLootItem type not found; cannot create custom container root item.");
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

    private static string? GetStaticWeaponId(object weaponData)
    {
        return weaponData.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?.GetValue(weaponData) as string;
    }

    private static object? CreateStaticWeaponData(InteractiveObject weapon, Type weaponDataType)
    {
        var donor = FindWeaponDonor(weapon.WeaponTemplate);
        if (donor == null)
        {
            ServerPlugin.Logger?.Warning($"[MEL] No donor stationary weapon found for template {weapon.WeaponTemplate}; cannot create weapon entry.");
            return null;
        }

        ServerPlugin.Logger?.Info($"[MEL] Found donor stationary weapon for template {weapon.WeaponTemplate}; cloning for id {weapon.Id}.");

        var spawnpoint = CloneAndRemapSpawnpoint(donor, weapon.Id, weapon.Position, weapon.Rotation);
        if (spawnpoint == null)
        {
            ServerPlugin.Logger?.Warning($"[MEL] Failed to clone donor spawnpoint for weapon {weapon.Id}.");
            return null;
        }

        ServerPlugin.Logger?.Info($"[MEL] Created static weapon data for {weapon.Id} (template {weapon.WeaponTemplate}).");
        return spawnpoint;
    }

    private static void PreloadWeaponDonors(IEnumerable<KeyValuePair<string, object>> locations)
    {
        try
        {
            _weaponDonors.Clear();
            foreach (var (locationId, location) in locations)
            {
                var staticContainersProp = location.GetType().GetProperty("StaticContainers", BindingFlags.Public | BindingFlags.Instance);
                if (staticContainersProp == null)
                    continue;

                var lazyLoad = staticContainersProp.GetValue(location);
                if (lazyLoad == null)
                    continue;

                var valueProp = lazyLoad.GetType().GetProperty("Value");
                if (valueProp == null)
                    continue;

                var data = valueProp.GetValue(lazyLoad);
                if (data == null)
                    continue;

                var staticWeaponsProp = data.GetType().GetProperty("StaticWeapons", BindingFlags.Public | BindingFlags.Instance);
                if (staticWeaponsProp == null)
                    continue;

                var weapons = staticWeaponsProp.GetValue(data) as IEnumerable<SpawnpointTemplate>;
                if (weapons == null)
                    continue;

                foreach (var weapon in weapons)
                {
                    if (weapon == null)
                        continue;

                    var rootItem = weapon.Items?.FirstOrDefault();
                    if (rootItem == null)
                        continue;

                    var template = rootItem.Template.ToString();
                    if (string.IsNullOrWhiteSpace(template))
                        continue;

                    if (!_weaponDonors.ContainsKey(template))
                    {
                        _weaponDonors[template] = weapon;
                        ServerPlugin.Logger?.Info($"[MEL] Cached donor stationary weapon for template {template} from location {locationId}.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ServerPlugin.Logger?.Error($"[MEL] Failed to preload weapon donors: {ex.Message}");
        }
    }

    private static SpawnpointTemplate? FindWeaponDonor(string weaponTemplate)
    {
        if (_weaponDonors.TryGetValue(weaponTemplate, out var donor))
        {
            ServerPlugin.Logger?.Info($"[MEL] Found donor stationary weapon for template {weaponTemplate}.");
            return donor;
        }

        if (_databaseService is not null)
        {
            var databaseService = _databaseService;
            ServerPlugin.Logger?.Info($"[MEL] Donor not cached for {weaponTemplate}; reloading weapon donors from database.");
            PreloadWeaponDonors(databaseService.GetLocations().GetDictionary().Select(x => new KeyValuePair<string, object>(x.Key, x.Value)));
            if (_weaponDonors.TryGetValue(weaponTemplate, out var reloaded))
            {
                ServerPlugin.Logger?.Info($"[MEL] Found donor stationary weapon for template {weaponTemplate} after reload.");
                return reloaded;
            }
        }

        ServerPlugin.Logger?.Warning($"[MEL] No donor stationary weapon found for template {weaponTemplate}; cannot create weapon entry.");
        return null;
    }

    private static SpawnpointTemplate? CloneAndRemapSpawnpoint(SpawnpointTemplate donor, string sceneId, TransformData position, TransformData rotation)
    {
        var json = JsonSerializer.Serialize(donor);
        var spawnpoint = JsonSerializer.Deserialize<SpawnpointTemplate>(json);
        if (spawnpoint == null)
            return null;

        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in spawnpoint.Items ?? Enumerable.Empty<SptLootItem>())
        {
            var newId = new MongoId().ToString();
            idMap[item.Id.ToString()] = newId;
            item.Id = new MongoId(newId);
        }

        foreach (var item in spawnpoint.Items ?? Enumerable.Empty<SptLootItem>())
        {
            if (!string.IsNullOrEmpty(item.ParentId) && idMap.TryGetValue(item.ParentId, out var newParentId))
                item.ParentId = newParentId;
        }

        string? rootId = null;
        var donorRootId = donor.Root ?? donor.Items?.FirstOrDefault()?.Id.ToString();
        if (!string.IsNullOrEmpty(donorRootId) && idMap.TryGetValue(donorRootId, out var mappedRootId))
        {
            rootId = mappedRootId;
        }
        else
        {
            // Fallback: the root item is the one with no parent id.
            var rootItem = spawnpoint.Items?.FirstOrDefault(i => string.IsNullOrEmpty(i.ParentId));
            if (rootItem != null)
            {
                rootId = rootItem.Id.ToString();
                ServerPlugin.Logger?.Info($"[MEL] Remapped root id for stationary weapon clone via item with no parent (original root={donorRootId}).");
            }
        }

        if (string.IsNullOrEmpty(rootId))
        {
            ServerPlugin.Logger?.Warning($"[MEL] Could not remap root id for stationary weapon clone; original root={donorRootId}, items={(spawnpoint.Items?.Count() ?? 0)}.");
            return null;
        }

        spawnpoint.Id = sceneId;
        spawnpoint.Root = rootId;
        spawnpoint.Position = new XYZ { X = position.X, Y = position.Y, Z = position.Z };
        spawnpoint.Rotation = new XYZ { X = rotation.X, Y = rotation.Y, Z = rotation.Z };
        spawnpoint.IsAlwaysSpawn = true;

        return spawnpoint;
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
