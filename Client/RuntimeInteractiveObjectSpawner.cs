using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Quests;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeInteractiveObjectSpawner : MonoBehaviour
    {
        public static RuntimeInteractiveObjectSpawner Instance { get; private set; }

        // Track custom stationary weapons for on-demand spawning
        public static Dictionary<string, InteractiveObject> CustomStationaryWeapons = new Dictionary<string, InteractiveObject>();

        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private List<GameObject> _spawned = new List<GameObject>();

        private static readonly Dictionary<string, Dictionary<string, BundledStaticLootDistribution>> _bundledStaticLootCache = new Dictionary<string, Dictionary<string, BundledStaticLootDistribution>>(StringComparer.OrdinalIgnoreCase);

        private class BundledStaticLootDistribution
        {
            public int minCount = 1;
            public int maxCount = 1;
            public List<LootItem> items = new List<LootItem>();
        }

        private class BundledStaticLootEntry
        {
            public List<BundledCountEntry> itemcountDistribution = new List<BundledCountEntry>();
            public List<BundledItemEntry> itemDistribution = new List<BundledItemEntry>();
        }

        private class BundledCountEntry
        {
            public int count;
            public float relativeProbability;
        }

        private class BundledItemEntry
        {
            public string tpl;
            public float relativeProbability;
            public int count;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LoadPacks();
        }

        public void ResetState()
        {
            ClearSpawned();
            _currentWorld = null;
            _currentMapId = null;
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            var worldChanged = _currentWorld != world;

            if (world == null || worldChanged)
            {
                if (_currentWorld != null)
                {
                    Plugin.Log.LogInfo("GameWorld changed or ended, clearing interactive objects.");
                    ClearSpawned();
                    _currentWorld = null;
                    _currentMapId = null;
                }
            }

            if (world == null)
                return;

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentWorld = world;
                _currentMapId = mapId;
                ClearSpawned();
                Plugin.Log.LogInfo($"Map detected: {mapId}, spawning interactive objects");
                SpawnForMap(mapId, world);
            }
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning($"No pack directories found; interactive objects and object removals will not be applied.");
                return;
            }

            foreach (var dir in directories)
            {
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var pack = JsonConvert.DeserializeObject<PackData>(json);
                        if (pack?.maps != null)
                        {
                            _packs.Add(pack);
                            var removedCount = pack.maps.Values.Sum(m => m.removedObjects?.Count ?? 0);
                            Plugin.Log.LogInfo($"Loaded pack '{pack.name}' from {file} ({removedCount} removedObjects across {pack.maps.Count} maps).");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Failed to load pack {file}: {ex.Message}");
                    }
                }
            }
        }

        private void SpawnForMap(string mapId, GameWorld world)
        {
            var objects = new List<InteractiveObject>();
            var removedObjects = new List<RemovedObject>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    objects.AddRange(map.interactiveObjects ?? new List<InteractiveObject>());
                    removedObjects.AddRange(map.removedObjects ?? new List<RemovedObject>());
                }
            }

            if (removedObjects.Count > 0)
            {
                Plugin.Log.LogInfo($"Queuing {removedObjects.Count} removed objects for map {mapId}.");
                StartCoroutine(RemoveObjectsCoroutine(removedObjects));
            }

            if (objects.Count == 0)
            {
                Plugin.Log.LogInfo($"No interactive objects for map {mapId}");
                return;
            }

            Plugin.Log.LogInfo($"Spawning {objects.Count} interactive objects for map {mapId}");
            foreach (var obj in objects)
            {
                if (!QuestConditionsMet(obj.questOnly, obj.questCompleted, obj.questId))
                {
                    Plugin.Log.LogInfo($"Skipping quest-gated interactive object '{obj.name}' (quest {obj.questId} not active/completed).");
                    continue;
                }

                StartCoroutine(SpawnObjectCoroutine(obj, world));
            }
        }

        private IEnumerator RemoveObjectsCoroutine(List<RemovedObject> removedObjects)
        {
            yield return new WaitForSecondsRealtime(2f);
            Plugin.Log.LogInfo($"RemoveObjectsCoroutine starting for {removedObjects.Count} removed objects.");
            foreach (var removed in removedObjects)
            {
                if (removed == null) continue;
                GameObject target = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    target = FindSourceObject(removed.name, removed.position.ToVector3());
                    if (target != null) break;
                    if (attempt == 0)
                        Plugin.Log.LogInfo($"Removed object '{removed.name}' not found yet, waiting...");
                    yield return new WaitForSecondsRealtime(1f);
                }
                if (target != null && !CustomEditorUI.IsEditorObject(target))
                {
                    var state = RemovedObjectHelper.SoftRemove(target);
                    removed.originalDoorState = state.OriginalDoorState;
                    Plugin.Log.LogInfo($"Soft-removed vanilla object '{removed.name}' at {removed.position.ToVector3()} per pack (renderers/colliders disabled, culling group turned off).");
                }
                else if (target == null)
                {
                    Plugin.Log.LogWarning($"Could not find vanilla object '{removed.name}' at {removed.position.ToVector3()} after multiple attempts.");
                }
                else
                {
                    Plugin.Log.LogWarning($"Skipping removal of editor-owned object '{removed.name}'.");
                }
            }
            Plugin.Log.LogInfo("RemoveObjectsCoroutine finished.");
        }

        private IEnumerator SpawnObjectCoroutine(InteractiveObject obj, GameWorld world)
        {
            if (string.IsNullOrEmpty(obj.sourceObjectName))
            {
                Plugin.Log.LogWarning($"Interactive object '{obj.name}' has no source object; skipping.");
                yield break;
            }

            GameObject source = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                source = FindSourceObject(obj.sourceObjectName, obj.sourceObjectPosition.ToVector3());
                if (source != null)
                    break;

                if (attempt == 0)
                    Plugin.Log.LogInfo($"Source object '{obj.sourceObjectName}' for {obj.name} not ready, waiting...");

                yield return new WaitForSecondsRealtime(1f);
            }

            if (source == null)
            {
                Plugin.Log.LogWarning($"Could not find source scene object '{obj.sourceObjectName}' for {obj.name}");
                yield break;
            }

            SpawnObjectInstance(source, obj, world);
        }

        private GameObject FindSourceObject(string name, Vector3 position)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;
            var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != name) continue;
                        var dist = (t.position - position).sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = t.gameObject;
                        }
                    }
                }
            }

            return best;
        }

        private void SpawnObjectInstance(GameObject source, InteractiveObject obj, GameWorld world)
        {
            var instance = Instantiate(source);
            instance.name = $"InteractiveObject_{obj.name}";
            instance.transform.position = obj.position.ToVector3();
            instance.transform.rotation = obj.rotation.ToQuaternion();
            instance.transform.localScale = obj.scale.ToVector3();

            var wio = instance.GetComponentInChildren<WorldInteractiveObject>(true);
            var stationaryWeapon = instance.GetComponentInChildren<StationaryWeapon>(true);

            if (wio != null && wio.transform.parent == null)
            {
                // EFT requires a parent for CurrentAngle rotation, so wrap the root WIO in a frame.
                var frame = new GameObject($"InteractiveObject_{obj.name}_Frame");
                frame.transform.position = obj.position.ToVector3();
                frame.transform.rotation = obj.rotation.ToQuaternion();
                instance.transform.SetParent(frame.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                _spawned.Add(frame);
                Plugin.Log.LogInfo($"Created interaction frame for '{obj.name}' because WIO was on a root GameObject.");
            }
            else
            {
                _spawned.Add(instance);
            }

            if (wio != null)
            {
                if (obj.interactiveType == InteractiveObjectType.Door)
                {
                    wio.Id = obj.id;
                    if (!string.IsNullOrEmpty(obj.keyId))
                    {
                        wio.KeyId = obj.keyId;
                        wio.DoorState = EDoorState.Locked;
                        wio.InitialDoorState = EDoorState.Locked;
                        wio.FallbackState = EDoorState.Locked;
                        wio.CurrentAngle = wio.GetAngle(EDoorState.Locked);
                        Plugin.Log.LogInfo($"Set door '{obj.name}' key to {obj.keyId}");
                    }
                    else
                    {
                        wio.DoorState = EDoorState.Shut;
                        wio.InitialDoorState = EDoorState.Shut;
                        wio.FallbackState = EDoorState.Shut;
                        wio.CurrentAngle = wio.GetAngle(EDoorState.Shut);
                    }
                    Plugin.Log.LogInfo($"Door '{obj.name}' initial state={wio.DoorState}, angle={wio.CurrentAngle}, openAngle={wio.OpenAngle}, closeAngle={wio.CloseAngle}");
                }
                else if (obj.interactiveType == InteractiveObjectType.Container && !string.IsNullOrEmpty(obj.containerId))
                {
                    var lootable = wio as LootableContainer ?? instance.GetComponentInChildren<LootableContainer>(true);
                    if (lootable != null)
                        wio = lootable;
                    wio.Id = obj.containerId;
                    if (lootable != null)
                    {
                        lootable.Template = string.IsNullOrWhiteSpace(obj.containerTemplate) ? "578f87a3245977356274f2cb" : obj.containerTemplate;
                        StartCoroutine(InitializeContainerLootCoroutine(obj, lootable));
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Container '{obj.name}' has no LootableContainer component; loot interface will not work.");
                    }
                }
                else if (obj.interactiveType == InteractiveObjectType.StationaryWeapon && stationaryWeapon != null)
                {
                    wio.Id = obj.id;
                    stationaryWeapon.IdEditable = obj.id;
                    if (!string.IsNullOrEmpty(obj.weaponTemplate))
                        stationaryWeapon.Template = obj.weaponTemplate;
                    RecalculateStationaryWeaponLimits(stationaryWeapon);
                    StartCoroutine(InitializeStationaryWeaponCoroutine(obj, stationaryWeapon, world));
                }
                else if (obj.interactiveType == InteractiveObjectType.StationaryWeapon)
                {
                    Plugin.Log.LogWarning($"Spawned stationary weapon '{obj.name}' has no StationaryWeapon component; interaction will not work.");
                }
                else
                {
                    wio.Id = obj.id;
                }

                RegisterWorldInteractiveObjectWhenReady(wio, obj.name, world);
            }
            else if (stationaryWeapon != null)
            {
                Plugin.Log.LogInfo($"Stationary weapon '{obj.name}' has StationaryWeapon; setting up.");
                stationaryWeapon.IdEditable = obj.id;
                if (!string.IsNullOrEmpty(obj.weaponTemplate))
                    stationaryWeapon.Template = obj.weaponTemplate;
                RecalculateStationaryWeaponLimits(stationaryWeapon);

                // Initialize the weapon by creating the item locally and calling Init.
                StartCoroutine(InitializeStationaryWeaponCoroutine(obj, stationaryWeapon, world));
            }
            else
            {
                Plugin.Log.LogWarning($"Spawned interactive object '{obj.name}' has no WorldInteractiveObject component in its prefab; interaction will not work.");
            }

            Plugin.Log.LogInfo($"Spawned interactive object {obj.name} (type={obj.interactiveType})");
        }

        private void RegisterWorldInteractiveObjectWhenReady(WorldInteractiveObject wio, string name, GameWorld world)
        {
            if (world != null && world.World_0 != null)
            {
                world.RegisterWorldInteractionObject(wio);
                Plugin.Log.LogInfo($"Registered interactive object '{name}' (id={wio.Id}) with world.");
            }
            else
            {
                StartCoroutine(RegisterWorldInteractiveObjectCoroutine(wio, name, world));
            }
        }

        private IEnumerator RegisterWorldInteractiveObjectCoroutine(WorldInteractiveObject wio, string name, GameWorld world)
        {
            var timeout = 30f;
            var elapsed = 0f;
            while (elapsed < timeout)
            {
                if (world != null && world.World_0 != null)
                {
                    world.RegisterWorldInteractionObject(wio);
                    Plugin.Log.LogInfo($"Registered interactive object '{name}' (id={wio.Id}) with world.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
                elapsed += 0.5f;
            }
            Plugin.Log.LogWarning($"World_0 not available for interactive object '{name}' after {timeout}s; object may not be interactable.");
        }

        private static string GenerateItemId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 24);
        }

        private IEnumerator InitializeStationaryWeaponCoroutine(InteractiveObject obj, StationaryWeapon stationaryWeapon, GameWorld gameWorld)
        {
            Plugin.Log.LogInfo($"Starting stationary weapon initialization coroutine for '{obj.name}'.");

            if (gameWorld == null)
            {
                Plugin.Log.LogWarning($"GameWorld reference is null for stationary weapon '{obj.name}'; cannot initialize.");
                yield break;
            }

            var timeout = 30f;
            var elapsed = 0f;

            // Wait for ItemFactoryClass to be available so we can create the weapon item
            while (elapsed < timeout)
            {
                if (Singleton<ItemFactoryClass>.Instantiated)
                    break;
                yield return new WaitForSecondsRealtime(0.5f);
                elapsed += 0.5f;
            }

            if (!Singleton<ItemFactoryClass>.Instantiated)
            {
                Plugin.Log.LogWarning($"ItemFactoryClass not available for stationary weapon '{obj.name}' after {timeout}s; cannot initialize.");
                yield break;
            }

            try
            {
                var weaponTemplate = string.IsNullOrWhiteSpace(obj.weaponTemplate) ? "5cdeb229d7f00c000e7ce174" : obj.weaponTemplate;
                var item = Singleton<ItemFactoryClass>.Instance.CreateItem(obj.id, weaponTemplate, null);
                if (item == null)
                {
                    Plugin.Log.LogWarning($"ItemFactoryClass returned null for stationary weapon '{obj.name}' template {weaponTemplate}.");
                    yield break;
                }

                var weapon = item as Weapon;
                if (weapon == null)
                {
                    Plugin.Log.LogWarning($"Created item for stationary weapon '{obj.name}' is not a Weapon.");
                    yield break;
                }

                // StationaryWeapon.Init assumes a magazine exists, so create one from the allowed slot filter.
                var magSlot = weapon.GetMagazineSlot();
                if (magSlot != null && magSlot.ContainedItem == null)
                {
                    var magTemplate = magSlot.Filters?.SelectMany(f => f.Filter ?? new MongoID[0])?.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(magTemplate?.ToString()))
                    {
                        var magId = MongoID.Generate(false).ToString();
                        var magazineItem = Singleton<ItemFactoryClass>.Instance.CreateItem(magId, magTemplate.ToString(), null);
                        if (magazineItem != null)
                        {
                            magSlot.ChangeContainedItemDirectly(magazineItem);
                            Plugin.Log.LogInfo($"Installed magazine {magTemplate} into stationary weapon '{obj.name}'.");
                        }
                        else
                        {
                            Plugin.Log.LogWarning($"Failed to create magazine {magTemplate} for stationary weapon '{obj.name}'.");
                        }
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"No magazine filter found for stationary weapon '{obj.name}'.");
                    }
                }

                stationaryWeapon.Init(new TraderControllerClass(item, item.Id, item.ShortName, true, EOwnerType.Profile));
                gameWorld.RegisterLoot<StationaryWeapon>(stationaryWeapon);
                Plugin.Log.LogInfo($"Initialized stationary weapon '{obj.name}' id={obj.id} with item {item.TemplateId}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to initialize stationary weapon '{obj.name}': {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void RecalculateStationaryWeaponLimits(StationaryWeapon stationaryWeapon)
        {
            var hinge = stationaryWeapon.Hinge;
            if (hinge == null)
                return;

            var orientation = stationaryWeapon.Orientation;
            var pitchLimit = new Vector2(orientation.y - stationaryWeapon.PitchToleranceUp, orientation.y + stationaryWeapon.PitchToleranceDown);
            pitchLimit.x = NormalizeAngle(pitchLimit.x);
            pitchLimit.y = NormalizeAngle(pitchLimit.y);
            var yawLimit = new Vector2(orientation.x - stationaryWeapon.YawTolerance, orientation.x + stationaryWeapon.YawTolerance);
            if (yawLimit.x > 360f || yawLimit.y > 360f)
                yawLimit -= 360f * Vector2.one;

            var type = typeof(StationaryWeapon);
            type.GetField("_initialOrientation", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(stationaryWeapon, orientation);
            type.GetField("_pitchLimit", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(stationaryWeapon, pitchLimit);
            type.GetField("_yawLimit", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(stationaryWeapon, yawLimit);
        }

        private static float NormalizeAngle(float angle)
        {
            if (angle > 180f)
                return angle - 360f;
            if (angle < -180f)
                return angle + 360f;
            return angle;
        }

        private bool QuestConditionsMet(bool questOnly, bool questCompleted, string questId)
        {
            if (!questOnly && !questCompleted)
                return true;

            if (string.IsNullOrWhiteSpace(questId))
                return false;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player?.Profile?.QuestsData == null)
                return false;

            var quest = player.Profile.QuestsData.FirstOrDefault(q => q.Id == questId);
            if (quest == null)
                return false;

            var active = quest.Status == EQuestStatus.AvailableForStart
                || quest.Status == EQuestStatus.Started
                || quest.Status == EQuestStatus.AvailableForFinish;

            var completed = quest.Status == EQuestStatus.Success;

            return (questOnly && active) || (questCompleted && completed);
        }

        private IEnumerator InitializeContainerLootCoroutine(InteractiveObject obj, LootableContainer lootable)
        {
            var itemFactory = Singleton<ItemFactoryClass>.Instance;
            if (itemFactory == null)
            {
                Plugin.Log.LogWarning($"ItemFactoryClass not available for container '{obj.name}'.");
                yield break;
            }

            Item item = itemFactory.CreateItem(obj.containerId, lootable.Template, null);
            if (item == null)
            {
                Plugin.Log.LogWarning($"Failed to create item for container '{obj.name}'.");
                yield break;
            }

            int addedItems;
            if (obj.lootMode == ContainerLootMode.Custom)
            {
                addedItems = InjectMarkerItems(obj, item as CompoundItem);
            }
            else if (obj.items != null && obj.items.Any(i => !i.isDistribution))
            {
                // Pre-rolled vanilla items from staticContainers.json (with StackObjectsCount) take priority over distribution.
                addedItems = InjectMarkerItems(obj, item as CompoundItem);
            }
            else
            {
                // Default and Hybrid use the bundled vanilla staticLoot.json so the exact vanilla distribution is respected,
                // independent of whatever the pack's marker items happen to contain.
                var dist = GetBundledStaticLoot(_currentMapId, obj.containerTemplate);
                if (dist == null)
                {
                    Plugin.Log.LogWarning($"No bundled vanilla loot distribution found for '{obj.name}' template={obj.containerTemplate} on map '{_currentMapId}', falling back to marker items.");
                    addedItems = InjectMarkerItems(obj, item as CompoundItem);
                }
                else
                {
                    var marker = new InteractiveObject
                    {
                        name = obj.name,
                        itemCountMin = dist.minCount,
                        itemCountMax = dist.maxCount,
                        items = new List<LootItem>(dist.items)
                    };
                    if (obj.lootMode == ContainerLootMode.Hybrid)
                        marker.items.AddRange(obj.items.Where(i => !i.isDistribution));
                    addedItems = InjectMarkerItems(marker, item as CompoundItem);
                }
            }

            var controller = new TraderControllerClass(item, item.Id, item.ShortName.Localized(null), true, EOwnerType.Profile);
            lootable.Init(controller);
            var finalGameWorld = Singleton<GameWorld>.Instance;
            if (finalGameWorld != null)
            {
                // Imported/Default containers already have an AllLoot entry from SPT's generation, so
                // remove it before registering our injected item to avoid duplicate IDs in the world.
                if (finalGameWorld.AllLoot != null)
                {
                    var existing = finalGameWorld.AllLoot.FirstOrDefault(x => x.Id == obj.containerId);
                    if (existing != null)
                    {
                        finalGameWorld.AllLoot.Remove(existing);
                        Plugin.Log.LogInfo($"Removed existing AllLoot entry for container '{obj.name}' id={obj.containerId} before re-registration.");
                    }
                }
                finalGameWorld.RegisterLoot<LootableContainer>(lootable);
            }
            else
                Plugin.Log.LogWarning($"GameWorld not available for container '{obj.name}' loot registration; container initialized but not registered with world.");
            Plugin.Log.LogInfo($"Initialized lootable container '{obj.name}' id={obj.containerId}, template={lootable.Template}, itemId={item.Id}, mode={obj.lootMode}, injectedItems={addedItems}");
        }

        private string FindBundledStaticLootResourceName(string mapId)
        {
            var marker = $"locations.{mapId}.staticLoot.json";
            var asm = typeof(RuntimeInteractiveObjectSpawner).Assembly;
            return asm.GetManifestResourceNames().FirstOrDefault(n => n.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private BundledStaticLootDistribution GetBundledStaticLoot(string mapId, string containerTemplate)
        {
            if (string.IsNullOrEmpty(mapId) || string.IsNullOrEmpty(containerTemplate))
                return null;

            if (!_bundledStaticLootCache.TryGetValue(mapId, out var mapCache))
            {
                mapCache = LoadBundledStaticLoot(mapId);
                _bundledStaticLootCache[mapId] = mapCache;
            }

            if (mapCache == null)
                return null;
            mapCache.TryGetValue(containerTemplate, out var dist);
            return dist;
        }

        private Dictionary<string, BundledStaticLootDistribution> LoadBundledStaticLoot(string mapId)
        {
            var name = FindBundledStaticLootResourceName(mapId);
            if (name == null)
            {
                Plugin.Log.LogWarning($"Bundled staticLoot.json resource not found for map '{mapId}'");
                return new Dictionary<string, BundledStaticLootDistribution>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var asm = typeof(RuntimeInteractiveObjectSpawner).Assembly;
                string json;
                using (var stream = asm.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                    json = reader.ReadToEnd();

                var file = JsonConvert.DeserializeObject<Dictionary<string, BundledStaticLootEntry>>(json);
                var result = new Dictionary<string, BundledStaticLootDistribution>(StringComparer.OrdinalIgnoreCase);
                if (file == null)
                    return result;

                foreach (var kvp in file)
                {
                    var tpl = kvp.Key;
                    var entry = kvp.Value;
                    if (entry?.itemDistribution == null || entry.itemDistribution.Count == 0)
                        continue;

                    var total = entry.itemDistribution.Sum(d => d.relativeProbability);
                    if (total <= 0)
                        continue;

                    var dist = new BundledStaticLootDistribution();
                    foreach (var item in entry.itemDistribution)
                    {
                        if (string.IsNullOrEmpty(item.tpl))
                            continue;
                        dist.items.Add(new LootItem
                        {
                            template = item.tpl,
                            chance = (item.relativeProbability / total) * 100f,
                            randomRotation = true,
                            isDistribution = true,
                            count = Math.Max(item.count, 1)
                        });
                    }

                    dist.items.Sort((a, b) => b.chance.CompareTo(a.chance));

                    int minCount = 1, maxCount = 1;
                    if (entry.itemcountDistribution != null && entry.itemcountDistribution.Count > 0)
                    {
                        var counts = entry.itemcountDistribution.Select(d => d.count).ToList();
                        minCount = Math.Max(counts.Min(), 1);
                        maxCount = Math.Max(counts.Max(), minCount);
                    }
                    dist.minCount = minCount;
                    dist.maxCount = maxCount;
                    result[tpl] = dist;
                }

                Plugin.Log.LogInfo($"Loaded bundled staticLoot.json for {mapId}: {result.Count} container distributions");
                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load bundled staticLoot.json for {mapId}: {ex.Message}");
                return new Dictionary<string, BundledStaticLootDistribution>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private int InjectMarkerItems(InteractiveObject obj, CompoundItem compoundItem)
        {
            if (compoundItem == null || obj.items == null)
                return 0;

            var itemFactory = Singleton<ItemFactoryClass>.Instance;
            if (itemFactory == null)
                return 0;

            int added = 0;

            // Distribution items (from vanilla staticLoot.json) are weighted together and drawn
            // according to the container's item count distribution, matching SPT's container loot logic.
            // Seed per container so loot varies between raids/containers instead of reusing UnityEngine.Random's deterministic state.
            var rng = new System.Random(Environment.TickCount ^ (obj.containerId?.GetHashCode() ?? 0) ^ (int)(DateTime.UtcNow.Ticks & int.MaxValue));

            var distribution = obj.items.Where(i => i.isDistribution).ToList();
            if (distribution.Count > 0)
            {
                var total = distribution.Sum(i => i.chance);
                if (total > 0)
                {
                    int minCount = Math.Max(obj.itemCountMin, 1);
                    int maxCount = Math.Max(obj.itemCountMax, minCount);
                    int count = rng.Next(minCount, maxCount + 1);
                    for (int n = 0; n < count; n++)
                    {
                        var roll = (float)(rng.NextDouble() * total);
                        var running = 0f;
                        foreach (var loot in distribution)
                        {
                            running += loot.chance;
                            if (roll < running)
                            {
                                if (TryAddItemToContainer(loot, compoundItem, itemFactory, obj, rng))
                                    added++;
                                break;
                            }
                        }
                    }
                }
            }

            // Custom items are independent chance rolls.
            foreach (var loot in obj.items.Where(i => !i.isDistribution))
            {
                if (TryAddItemToContainer(loot, compoundItem, itemFactory, obj, rng))
                    added++;
            }

            return added;
        }

        private bool TryAddItemToContainer(LootItem loot, CompoundItem compoundItem, ItemFactoryClass itemFactory, InteractiveObject obj, System.Random rng)
        {
            if (string.IsNullOrWhiteSpace(loot.template))
                return false;

            if (!QuestConditionsMet(loot.questOnly, loot.questCompleted, loot.questId))
            {
                Plugin.Log.LogInfo($"Skipping quest-gated item {loot.template} for container '{obj.name}' (quest {loot.questId} not active/completed).");
                return false;
            }

            // Only non-distribution items use their own chance as a percentage.
            if (!loot.isDistribution && loot.chance < 100f)
            {
                var roll = (float)(rng.NextDouble() * 100.0);
                if (roll >= loot.chance)
                {
                    Plugin.Log.LogInfo($"Item {loot.template} chance roll {roll:F1} >= {loot.chance}; skipping for container '{obj.name}'.");
                    return false;
                }
            }

            var childItem = itemFactory.CreateItem(GenerateItemId(), loot.template, null);
            if (childItem == null)
            {
                Plugin.Log.LogWarning($"Failed to create item {loot.template} for container '{obj.name}'");
                return false;
            }

            if (loot.count > 1)
                SetItemStackCount(childItem, loot.count);

            if (compoundItem.Grids != null)
            {
                foreach (var grid in compoundItem.Grids)
                {
                    var result = grid.AddAnywhere(childItem, EErrorHandlingType.Ignore);
                    if (result.Succeeded)
                        return true;
                }
            }

            Plugin.Log.LogWarning($"Could not place item {loot.template} in container '{obj.name}'");
            return false;
        }

        private void SetItemStackCount(Item item, int count)
        {
            if (item == null || count <= 1)
                return;

            try
            {
                var itemType = item.GetType();
                var updProp = itemType.GetProperty("Upd", BindingFlags.Public | BindingFlags.Instance);
                if (updProp == null)
                    return;

                var upd = updProp.GetValue(item);
                if (upd == null)
                {
                    var updType = updProp.PropertyType;
                    upd = Activator.CreateInstance(updType);
                    updProp.SetValue(item, upd);
                }

                var stackProp = upd.GetType().GetProperty("StackObjectsCount", BindingFlags.Public | BindingFlags.Instance);
                if (stackProp == null)
                    return;

                var underlying = Nullable.GetUnderlyingType(stackProp.PropertyType) ?? stackProp.PropertyType;
                stackProp.SetValue(upd, Convert.ChangeType(count, underlying));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to set stack count for {item?.Template}: {ex.Message}");
            }
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                    Destroy(go);
            }
            _spawned.Clear();
        }

        private void OnDestroy()
        {
            Instance = null;
            ClearSpawned();
        }

        // Public method to spawn a stationary weapon on-demand when GameWorld.FindStationaryWeapon is called
        public static StationaryWeapon SpawnStationaryWeaponOnDemand(string id)
        {
            if (!CustomStationaryWeapons.TryGetValue(id, out var obj))
            {
                Plugin.Log.LogWarning($"Stationary weapon with id '{id}' not found in custom weapons.");
                return null;
            }

            Plugin.Log.LogInfo($"Spawning stationary weapon '{obj.name}' on-demand for id '{id}'.");

            // Find the source object in the scene (same approach as SpawnObjectCoroutine)
            var source = Instance?.FindSourceObject(obj.sourceObjectName, obj.sourceObjectPosition.ToVector3());
            if (source == null)
            {
                Plugin.Log.LogWarning($"Source object '{obj.sourceObjectName}' not found for stationary weapon '{obj.name}'.");
                return null;
            }

            // Instantiate the source object
            var instance = Instantiate(source);
            instance.name = $"InteractiveObject_{obj.name}";
            instance.transform.position = obj.position.ToVector3();
            instance.transform.rotation = obj.rotation.ToQuaternion();
            instance.transform.localScale = obj.scale.ToVector3();

            var stationaryWeapon = instance.GetComponentInChildren<StationaryWeapon>(true);
            if (stationaryWeapon == null)
            {
                Plugin.Log.LogWarning($"StationaryWeapon component not found in source object '{obj.sourceObjectName}'.");
                Destroy(instance);
                return null;
            }

            // Set the ID to match the server-created item
            stationaryWeapon.IdEditable = id;

            Plugin.Log.LogInfo($"Spawned stationary weapon '{obj.name}' with id '{id}'.");

            // Remove from the dictionary so we don't spawn it again
            CustomStationaryWeapons.Remove(id);

            return stationaryWeapon;
        }
    }
}
