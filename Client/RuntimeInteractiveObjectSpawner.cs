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
using HarmonyLib;
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
        private static bool _raidStarted;

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

        private static readonly Dictionary<string, Dictionary<string, List<LootItem>>> _bundledStaticContainersCache = new Dictionary<string, Dictionary<string, List<LootItem>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, int>> _bundledItemStackCountsCache = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        private class BundledContainerFile
        {
            public List<BundledContainerSpawn> staticContainers = new List<BundledContainerSpawn>();
            public List<BundledContainerSpawn> staticForced = new List<BundledContainerSpawn>();
            public List<BundledContainerSpawn> staticWeapons = new List<BundledContainerSpawn>();
        }

        private class BundledContainerSpawn
        {
            public string Id;
            public float probability;
            public BundledContainerTemplate template;
        }

        private class BundledContainerTemplate
        {
            public string Id;
            public string Root;
            public List<BundledContainerItem> Items;
        }

        private class BundledContainerItem
        {
            public string _id;
            public string _tpl;
            public string parentId;
            public string slotId;
            public BundledContainerUpd upd;
        }

        private class BundledContainerUpd
        {
            public double StackObjectsCount;
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
            _raidStarted = false;
        }

        public static void MarkRaidStarted()
        {
            _raidStarted = true;
            Plugin.Log.LogInfo("Raid start signal received (GameWorld.OnGameStarted).");
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
                var blockers = removedObjects.Where(r => r != null && r.name.IndexOf("BLOCKER", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var normal = removedObjects.Except(blockers).ToList();

                if (normal.Count > 0)
                    StartCoroutine(RemoveObjectsCoroutine(normal, world));

                if (blockers.Count > 0)
                    StartCoroutine(RemoveBlockersCoroutine(blockers, world));
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

        private IEnumerator RemoveObjectsCoroutine(List<RemovedObject> removedObjects, GameWorld world)
        {
            Plugin.Log.LogInfo($"RemoveObjectsCoroutine waiting for player spawn; {removedObjects.Count} objects queued.");
            while (world == null || world.MainPlayer == null)
                yield return new WaitForSecondsRealtime(2f);
            yield return new WaitForSecondsRealtime(2f);

            var removedSet = new HashSet<GameObject>();
            Plugin.Log.LogInfo($"RemoveObjectsCoroutine starting for {removedObjects.Count} removed objects.");
            var processedKeys = new HashSet<string>();
            foreach (var removed in removedObjects)
            {
                if (removed == null) continue;
                var key = $"{removed.name}|{removed.position.x:F2}|{removed.position.y:F2}|{removed.position.z:F2}";
                if (processedKeys.Contains(key))
                {
                    Plugin.Log.LogInfo($"Skipping duplicate removed object entry '{removed.name}' at {removed.position.ToVector3()}.");
                    continue;
                }
                processedKeys.Add(key);
                GameObject target = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    target = FindRemovedObject(removed, removedSet, 25f);
                    if (target != null) break;
                    if (attempt == 0)
                        Plugin.Log.LogInfo($"Removed object '{removed.name}' not found yet, waiting...");
                    yield return new WaitForSecondsRealtime(2f);
                }
                if (target != null && !CustomEditorUI.IsEditorObject(target))
                {
                    var state = RemovedObjectHelper.SoftRemove(target);
                    removed.originalDoorState = state.OriginalDoorState;
                    removedSet.Add(state.GameObject);
                    Plugin.Log.LogInfo($"Soft-removed vanilla object '{removed.name}' at {removed.position.ToVector3()} per pack (renderers/colliders disabled).");
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

        private IEnumerator RemoveBlockersCoroutine(List<RemovedObject> removedObjects, GameWorld world)
        {
            Plugin.Log.LogInfo($"RemoveBlockersCoroutine waiting for raid start; {removedObjects.Count} blockers queued.");
            while (world != null && !_raidStarted)
                yield return new WaitForSecondsRealtime(2f);
            if (world == null) yield break;
            yield return new WaitForSecondsRealtime(5f);

            // Build a one-time name->transforms index so we don't re-scan the entire scene for every blocker.
            var nameIndex = new Dictionary<string, List<Transform>>();
            var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int s = 0; s < sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < transforms.Length; i++)
                    {
                        var t = transforms[i];
                        if (!nameIndex.TryGetValue(t.name, out var list))
                        {
                            list = new List<Transform>();
                            nameIndex[t.name] = list;
                        }
                        list.Add(t);
                    }
                    yield return null; // spread index build across frames
                }
            }
            Plugin.Log.LogInfo($"Built blocker name index with {nameIndex.Count} names.");

            var removedSet = new HashSet<GameObject>();
            Plugin.Log.LogInfo($"RemoveBlockersCoroutine starting for {removedObjects.Count} blockers.");
            var processedKeys = new HashSet<string>();
            foreach (var removed in removedObjects)
            {
                if (removed == null) continue;
                var key = $"{removed.name}|{removed.position.x:F2}|{removed.position.y:F2}|{removed.position.z:F2}";
                if (processedKeys.Contains(key))
                {
                    Plugin.Log.LogInfo($"Skipping duplicate blocker entry '{removed.name}' at {removed.position.ToVector3()}.");
                    continue;
                }
                processedKeys.Add(key);
                GameObject target = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    target = FindRemovedObject(removed, removedSet, 25f, nameIndex);
                    if (target != null) break;
                    if (attempt == 0)
                        Plugin.Log.LogInfo($"Blocker '{removed.name}' not found yet, waiting...");
                    yield return new WaitForSecondsRealtime(2f);
                }
                if (target != null && !CustomEditorUI.IsEditorObject(target))
                {
                    removedSet.Add(target);
                    UnityEngine.Object.Destroy(target);
                    Plugin.Log.LogInfo($"Destroyed blocker '{removed.name}' at {removed.position.ToVector3()} per pack.");
                    GameObject extra;
                    while ((extra = FindRemovedObject(removed, removedSet, 1f, nameIndex)) != null && !CustomEditorUI.IsEditorObject(extra))
                    {
                        removedSet.Add(extra);
                        UnityEngine.Object.Destroy(extra);
                        Plugin.Log.LogInfo($"Destroyed duplicate blocker '{removed.name}' at {removed.position.ToVector3()} per pack.");
                        yield return null; // spread duplicate destruction across frames
                    }
                }
                else if (target == null)
                {
                    Plugin.Log.LogWarning($"Could not find blocker '{removed.name}' at {removed.position.ToVector3()} after multiple attempts.");
                }
                else
                {
                    Plugin.Log.LogWarning($"Skipping removal of editor-owned blocker '{removed.name}'.");
                }
                yield return null; // spread blocker processing across frames
            }
            Plugin.Log.LogInfo("RemoveBlockersCoroutine finished.");
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

                yield return new WaitForSecondsRealtime(2f);
            }

            if (source == null)
            {
                Plugin.Log.LogWarning($"Could not find source scene object '{obj.sourceObjectName}' for {obj.name}");
                yield break;
            }

            SpawnObjectInstance(source, obj, world);
        }

        private GameObject FindSourceObject(string name, Vector3 position, float maxSqr = float.MaxValue)
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
                        if (dist > maxSqr) continue;
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

        private GameObject FindRemovedObject(RemovedObject removed, HashSet<GameObject> excluded, float maxSqr = float.MaxValue, Dictionary<string, List<Transform>> nameIndex = null)
        {
            GameObject bestPath = null;
            GameObject bestActive = null;
            GameObject bestInactive = null;
            float bestActiveScore = float.MaxValue;
            float bestInactiveScore = float.MaxValue;
            var expectedPos = removed.position.ToVector3();
            var expectedRot = removed.rotation.ToQuaternion();
            var expectedScale = removed.scale.ToVector3();
            bool hasPath = !string.IsNullOrEmpty(removed.path);

            void Check(Transform t)
            {
                if (t == null || t.name != removed.name) return;
                var go = t.gameObject;
                if (go == null || (excluded != null && excluded.Contains(go))) return;
                var dist = (t.position - expectedPos).sqrMagnitude;
                if (dist > maxSqr) return;

                if (hasPath)
                {
                    var path = t.name;
                    var p = t;
                    while (p.parent != null)
                    {
                        p = p.parent;
                        path = p.name + "/" + path;
                    }
                    if (path == removed.path)
                        bestPath = go;
                }

                float rotDiff = Quaternion.Angle(t.rotation, expectedRot);
                float scaleDiff = Vector3.Distance(t.localScale, expectedScale);
                float score = rotDiff * 10f + scaleDiff * 100f + Mathf.Sqrt(dist);

                if (go.activeInHierarchy)
                {
                    if (score < bestActiveScore)
                    {
                        bestActiveScore = score;
                        bestActive = go;
                    }
                }
                else
                {
                    if (score < bestInactiveScore)
                    {
                        bestInactiveScore = score;
                        bestInactive = go;
                    }
                }
            }

            if (nameIndex != null && nameIndex.TryGetValue(removed.name, out var candidates))
            {
                for (int i = 0; i < candidates.Count; i++)
                    Check(candidates[i]);
            }
            else
            {
                var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
                for (int i = 0; i < sceneCount; i++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        var transforms = root.GetComponentsInChildren<Transform>(true);
                        for (int j = 0; j < transforms.Length; j++)
                            Check(transforms[j]);
                    }
                }
            }

            if (bestPath != null)
                return bestPath;
            return bestActive ?? bestInactive;
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
                    var lootables = instance.GetComponentsInChildren<LootableContainer>(true);
                    if (lootables == null || lootables.Length == 0)
                    {
                        Plugin.Log.LogWarning($"Container '{obj.name}' has no LootableContainer component; loot interface will not work.");
                    }
                    else
                    {
                        for (int i = 0; i < lootables.Length; i++)
                        {
                            var lootable = lootables[i];
                            string id = i == 0 ? obj.containerId : GenerateItemId();
                            lootable.Id = id;
                            lootable.Template = string.IsNullOrWhiteSpace(obj.containerTemplate) ? "578f87a3245977356274f2cb" : obj.containerTemplate;
                            StartCoroutine(InitializeContainerLootCoroutine(obj, lootable, id, world));
                        }
                        wio = null;
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
                else if (obj.interactiveType == InteractiveObjectType.Switch)
                {
                    wio.Id = obj.id;
                    var state = obj.switchInitialState ? EDoorState.Open : EDoorState.Shut;
                    wio.SetDoorState(state, true);

                    var controller = instance.GetComponent<CustomSwitchController>() ?? instance.AddComponent<CustomSwitchController>();
                    controller.Data = obj;
                    controller.Wio = wio;
                    Plugin.Log.LogInfo($"Switch '{obj.name}' initial state {(obj.switchInitialState ? "On" : "Off")}.");
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

        private IEnumerator InitializeContainerLootCoroutine(InteractiveObject obj, LootableContainer lootable, string containerId = null, GameWorld world = null)
        {
            var cid = containerId ?? obj.containerId;
            var itemFactory = Singleton<ItemFactoryClass>.Instance;
            if (itemFactory == null)
            {
                Plugin.Log.LogWarning($"ItemFactoryClass not available for container '{obj.name}'.");
                yield break;
            }

            Item item = itemFactory.CreateItem(cid, lootable.Template, null);
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
            else
            {
                // Default/Hybrid: use the exact pre-rolled vanilla staticContainers.json for this
                // container so StackObjectsCount (currency/ammo stacks) is preserved.
                var preRolled = GetBundledStaticContainerItems(_currentMapId, cid);
                if (preRolled != null && preRolled.Count > 0)
                {
                    var marker = new InteractiveObject
                    {
                        name = obj.name,
                        items = new List<LootItem>(preRolled)
                    };
                    if (obj.lootMode == ContainerLootMode.Hybrid)
                        marker.items.AddRange(obj.items.Where(i => !i.isDistribution));
                    addedItems = InjectMarkerItems(marker, item as CompoundItem);
                }
                else if (obj.items != null && obj.items.Any(i => !i.isDistribution) && obj.lootMode == ContainerLootMode.Default)
                {
                    // Pre-rolled vanilla items already stored in the pack (editor import) for Default only.
                    addedItems = InjectMarkerItems(obj, item as CompoundItem);
                }
                else
                {
                    // Fallback to the bundled vanilla staticLoot.json distribution.
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
            }

            if (!string.IsNullOrEmpty(obj.keyId))
            {
                lootable.KeyId = obj.keyId;
                Plugin.Log.LogInfo($"Set container '{obj.name}' key to {obj.keyId}");
            }

            var controller = new TraderControllerClass(item, item.Id, item.ShortName, true, EOwnerType.Profile);
            lootable.Init(controller);
            var finalGameWorld = Singleton<GameWorld>.Instance;
            if (finalGameWorld != null)
            {
                // Imported/Default containers already have an AllLoot entry from SPT's generation, so
                // remove it before registering our injected item to avoid duplicate IDs in the world.
                if (finalGameWorld.AllLoot != null)
                {
                    var existing = finalGameWorld.AllLoot.FirstOrDefault(x => x.Id == cid);
                    if (existing != null)
                    {
                        finalGameWorld.AllLoot.Remove(existing);
                        Plugin.Log.LogInfo($"Removed existing AllLoot entry for container '{obj.name}' id={cid} before re-registration.");
                    }
                }
                finalGameWorld.RegisterLoot<LootableContainer>(lootable);
                RegisterWorldInteractiveObjectWhenReady(lootable, obj.name, finalGameWorld);
            }
            else
                Plugin.Log.LogWarning($"GameWorld not available for container '{obj.name}' loot registration; container initialized but not registered with world.");
            Plugin.Log.LogInfo($"Initialized lootable container '{obj.name}' id={cid}, template={lootable.Template}, itemId={item.Id}, mode={obj.lootMode}, injectedItems={addedItems}");
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

                var stackCounts = LoadBundledItemStackCounts(mapId);

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
                        int itemStack = stackCounts.TryGetValue(item.tpl, out var stack) ? stack : item.count;
                        dist.items.Add(new LootItem
                        {
                            template = item.tpl,
                            chance = (item.relativeProbability / total) * 100f,
                            randomRotation = true,
                            isDistribution = true,
                            count = Math.Max(itemStack, 1)
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

        private string FindBundledItemStackCountsResourceName(string mapId)
        {
            var marker = $"locations.{mapId}.itemStackCounts.json";
            var asm = typeof(RuntimeInteractiveObjectSpawner).Assembly;
            return asm.GetManifestResourceNames().FirstOrDefault(n => n.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private Dictionary<string, int> LoadBundledItemStackCounts(string mapId)
        {
            if (_bundledItemStackCountsCache.TryGetValue(mapId, out var cached))
                return cached;

            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _bundledItemStackCountsCache[mapId] = result;

            var name = FindBundledItemStackCountsResourceName(mapId);
            if (name == null)
            {
                Plugin.Log.LogWarning($"Bundled itemStackCounts.json resource not found for map '{mapId}'");
                return result;
            }

            try
            {
                var asm = typeof(RuntimeInteractiveObjectSpawner).Assembly;
                string json;
                using (var stream = asm.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                    json = reader.ReadToEnd();

                var file = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                if (file != null)
                {
                    foreach (var kvp in file)
                        result[kvp.Key] = kvp.Value;
                }

                Plugin.Log.LogInfo($"Loaded bundled itemStackCounts.json for {mapId}: {result.Count} entries");
                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load bundled itemStackCounts.json for {mapId}: {ex.Message}");
                return result;
            }
        }

        private string FindBundledStaticContainersResourceName(string mapId)
        {
            var marker = $"locations.{mapId}.staticContainers.json";
            var asm = typeof(RuntimeInteractiveObjectSpawner).Assembly;
            return asm.GetManifestResourceNames().FirstOrDefault(n => n.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private List<LootItem> GetBundledStaticContainerItems(string mapId, string containerId)
        {
            if (string.IsNullOrWhiteSpace(mapId) || string.IsNullOrWhiteSpace(containerId))
                return null;

            List<LootItem> items = null;
            if (!_bundledStaticContainersCache.TryGetValue(mapId, out var cache))
            {
                cache = LoadBundledStaticContainers(mapId);
                _bundledStaticContainersCache[mapId] = cache;
            }

            if (cache != null)
                cache.TryGetValue(containerId, out items);

            return items;
        }

        private Dictionary<string, List<LootItem>> LoadBundledStaticContainers(string mapId)
        {
            var name = FindBundledStaticContainersResourceName(mapId);
            if (name == null)
            {
                Plugin.Log.LogWarning($"Bundled staticContainers.json resource not found for map '{mapId}'");
                return new Dictionary<string, List<LootItem>>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var asm = typeof(RuntimeInteractiveObjectSpawner).Assembly;
                string json;
                using (var stream = asm.GetManifestResourceStream(name))
                using (var reader = new StreamReader(stream))
                    json = reader.ReadToEnd();

                var file = JsonConvert.DeserializeObject<BundledContainerFile>(json);
                var result = new Dictionary<string, List<LootItem>>(StringComparer.OrdinalIgnoreCase);
                if (file == null)
                    return result;

                foreach (var list in new[] { file.staticContainers, file.staticForced, file.staticWeapons })
                {
                    if (list == null)
                        continue;
                    foreach (var sp in list)
                    {
                        if (sp?.template?.Items == null)
                            continue;

                        var root = sp.template.Items.FirstOrDefault(i => string.IsNullOrEmpty(i?.parentId));
                        var rootId = root?._id ?? sp.template.Root;
                        if (string.IsNullOrWhiteSpace(rootId))
                            continue;

                        var items = new List<LootItem>();
                        foreach (var item in sp.template.Items)
                        {
                            if (string.IsNullOrWhiteSpace(item?._tpl))
                                continue;
                            if (string.IsNullOrWhiteSpace(item.parentId))
                                continue;
                            items.Add(new LootItem
                            {
                                template = item._tpl,
                                chance = 100f,
                                randomRotation = true,
                                isDistribution = false,
                                count = Math.Max((int)(item.upd?.StackObjectsCount ?? 1), 1)
                            });
                        }

                        if (!result.ContainsKey(rootId))
                            result[rootId] = items;

                        var tplId = sp.template.Id ?? sp.Id;
                        if (!string.IsNullOrWhiteSpace(tplId) && !result.ContainsKey(tplId))
                            result[tplId] = items;
                    }
                }

                Plugin.Log.LogInfo($"Loaded bundled staticContainers.json for {mapId}: {result.Count} containers");
                return result;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to load bundled staticContainers.json for {mapId}: {ex.Message}");
                return new Dictionary<string, List<LootItem>>(StringComparer.OrdinalIgnoreCase);
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

            var stackCount = loot.count;
            if (stackCount <= 1 && loot.isDistribution)
            {
                var parent = ItemNameResolver.GetParent(loot.template);
                if (parent == "5485a8684bdc2da71d8b4567" ||
                    string.Equals(loot.template, "5d235b4d86f7742e017bc88a", StringComparison.OrdinalIgnoreCase))
                {
                    stackCount = ItemNameResolver.GetStackMaxSize(loot.template);
                }
            }

            if (loot.isDistribution)
            {
                if (stackCount > 1)
                    stackCount = rng.Next(1, stackCount + 1);
            }
            else if (loot.maxCount > loot.minCount)
            {
                int min = Math.Max(loot.minCount, 1);
                int max = Math.Max(loot.maxCount, min);
                stackCount = rng.Next(min, max + 1);
            }
            else if (stackCount <= 1)
            {
                if (string.Equals(loot.template, "5d235b4d86f7742e017bc88a", StringComparison.OrdinalIgnoreCase) ||
                    ItemNameResolver.GetParent(loot.template) == "5485a8684bdc2da71d8b4567")
                {
                    stackCount = ItemNameResolver.GetStackMaxSize(loot.template);
                }
            }

            if (stackCount > 1)
                SetItemStackCount(childItem, stackCount);

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

            var name = item?.Id?.ToString() ?? "unknown";
            try
            {
                if (TrySetStackValue(item, "Upd", "StackObjectsCount", count, out _))
                {
                    Plugin.Log.LogInfo($"Set Upd.StackObjectsCount={count} for {name}");
                    return;
                }
                if (TrySetStackValue(item, null, "StackObjectsCount", count, out _))
                {
                    Plugin.Log.LogInfo($"Set StackObjectsCount={count} for {name}");
                    return;
                }
                Plugin.Log.LogWarning($"Could not set stack count for {name}: no accessible StackObjectsCount property/field");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to set stack count for {name}: {ex.Message}");
            }
        }

        private bool TrySetStackValue(object target, string parentName, string memberName, int count, out string reason)
        {
            reason = null;
            if (target == null)
            {
                reason = "target is null";
                return false;
            }

            try
            {
                object current = target;
                if (!string.IsNullOrEmpty(parentName))
                {
                    var parentProp = target.GetType().GetProperty(parentName, BindingFlags.Public | BindingFlags.Instance);
                    if (parentProp == null)
                    {
                        reason = $"no property {parentName}";
                        return false;
                    }
                    current = parentProp.GetValue(target);
                    if (current == null)
                    {
                        var parentType = parentProp.PropertyType;
                        current = Activator.CreateInstance(parentType);
                        var setMethod = parentProp.GetSetMethod(true);
                        if (setMethod == null)
                        {
                            reason = $"{parentName} has no setter";
                            return false;
                        }
                        setMethod.Invoke(target, new[] { current });
                    }
                }

                var prop = current.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var setMethod = prop.GetSetMethod(true);
                    if (setMethod != null)
                    {
                        var value = Convert.ChangeType(count, prop.PropertyType);
                        setMethod.Invoke(current, new[] { value });
                        return true;
                    }
                }

                var field = current.GetType().GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var value = Convert.ChangeType(count, field.FieldType);
                    field.SetValue(current, value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }
            return false;
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

    [HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
    public static class GameWorldOnGameStartedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            RuntimeInteractiveObjectSpawner.MarkRaidStarted();
        }
    }
}
