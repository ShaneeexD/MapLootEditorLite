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

        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private List<GameObject> _spawned = new List<GameObject>();

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
                SpawnForMap(mapId);
            }
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            // Interactive objects should only spawn from published packs, not from exports used for editing.

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning($"No pack directories found; interactive objects will not be spawned.");
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
                            Plugin.Log.LogInfo($"Loaded pack '{pack.name}' from {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Failed to load pack {file}: {ex.Message}");
                    }
                }
            }
        }

        private void SpawnForMap(string mapId)
        {
            var objects = new List<InteractiveObject>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    objects.AddRange(map.interactiveObjects ?? new List<InteractiveObject>());
                }
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

                StartCoroutine(SpawnObjectCoroutine(obj));
            }
        }

        private IEnumerator SpawnObjectCoroutine(InteractiveObject obj)
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

            SpawnObjectInstance(source, obj);
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

        private void SpawnObjectInstance(GameObject source, InteractiveObject obj)
        {
            var instance = Instantiate(source);
            instance.name = $"InteractiveObject_{obj.name}";
            instance.transform.position = obj.position.ToVector3();
            instance.transform.rotation = obj.rotation.ToQuaternion();
            instance.transform.localScale = obj.scale.ToVector3();

            var wio = instance.GetComponentInChildren<WorldInteractiveObject>(true);
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
                wio.Id = obj.id;

                if (obj.interactiveType == InteractiveObjectType.Door)
                {
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

                RegisterWorldInteractiveObjectWhenReady(wio, obj.name);
            }
            else if (obj.interactiveType == InteractiveObjectType.StationaryWeapon)
            {
                var stationaryWeapon = instance.GetComponentInChildren<StationaryWeapon>(true);
                if (stationaryWeapon != null)
                {
                    stationaryWeapon.IdEditable = obj.id;
                    if (!string.IsNullOrEmpty(obj.weaponTemplate))
                        stationaryWeapon.Template = obj.weaponTemplate;
                    RecalculateStationaryWeaponLimits(stationaryWeapon);
                    StartCoroutine(InitializeStationaryWeaponCoroutine(obj, stationaryWeapon));
                }
                else
                {
                    Plugin.Log.LogWarning($"Spawned stationary weapon '{obj.name}' has no StationaryWeapon component; interaction will not work.");
                }
            }
            else
            {
                Plugin.Log.LogWarning($"Spawned interactive object '{obj.name}' has no WorldInteractiveObject component in its prefab; interaction will not work.");
            }

            Plugin.Log.LogInfo($"Spawned interactive object {obj.name} (type={obj.interactiveType})");
        }

        private void RegisterWorldInteractiveObjectWhenReady(WorldInteractiveObject wio, string name)
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld != null && gameWorld.World_0 != null)
            {
                gameWorld.RegisterWorldInteractionObject(wio);
                Plugin.Log.LogInfo($"Registered interactive object '{name}' (id={wio.Id}) with world.");
            }
            else
            {
                StartCoroutine(RegisterWorldInteractiveObjectCoroutine(wio, name));
            }
        }

        private IEnumerator RegisterWorldInteractiveObjectCoroutine(WorldInteractiveObject wio, string name)
        {
            while (true)
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld != null && gameWorld.World_0 != null)
                {
                    gameWorld.RegisterWorldInteractionObject(wio);
                    Plugin.Log.LogInfo($"Registered interactive object '{name}' (id={wio.Id}) with world.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private static string GenerateItemId()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 24);
        }

        private IEnumerator InitializeStationaryWeaponCoroutine(InteractiveObject obj, StationaryWeapon stationaryWeapon)
        {
            var timeout = 60f;
            var elapsed = 0f;
            LootItemPositionClass lootData = null;
            while (elapsed < timeout)
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld != null && gameWorld.AllLoot != null)
                {
                    lootData = gameWorld.AllLoot.FirstOrDefault(x => x.Id == obj.id);
                    if (lootData != null)
                        break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
                elapsed += 0.5f;
            }

            var finalGameWorld = Singleton<GameWorld>.Instance;
            if (finalGameWorld == null)
            {
                Plugin.Log.LogWarning($"GameWorld not available for stationary weapon '{obj.name}'; cannot initialize.");
                yield break;
            }

            if (lootData == null)
            {
                Plugin.Log.LogWarning($"Stationary weapon '{obj.name}' not found in AllLoot after {timeout}s.");
                yield break;
            }

            var item = lootData.Item;
            if (item == null)
            {
                Plugin.Log.LogWarning($"Stationary weapon '{obj.name}' loot entry has no item.");
                yield break;
            }

            if (stationaryWeapon.ItemController != null)
            {
                Plugin.Log.LogInfo($"Stationary weapon '{obj.name}' already initialized by the game; skipping manual init.");
                yield break;
            }

            var controller = new TraderControllerClass(item, item.Id, item.ShortName.Localized(null), true, EOwnerType.Profile);
            stationaryWeapon.Init(controller);
            finalGameWorld.RegisterLoot<StationaryWeapon>(stationaryWeapon);
            Plugin.Log.LogInfo($"Initialized stationary weapon '{obj.name}' id={obj.id}.");
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

            Item item = null;
            var source = "unknown";

            if (obj.lootMode == ContainerLootMode.Custom)
            {
                item = itemFactory.CreateItem(obj.containerId, lootable.Template, null);
                source = "custom";
            }
            else
            {
                var timeout = 15f;
                var elapsed = 0f;
                LootItemPositionClass lootData = null;
                while (elapsed < timeout)
                {
                    var currentWorld = Singleton<GameWorld>.Instance;
                    if (currentWorld != null && currentWorld.AllLoot != null)
                    {
                        lootData = currentWorld.AllLoot.FirstOrDefault(x => x.Id == obj.containerId);
                        if (lootData != null)
                            break;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                    elapsed += 0.5f;
                }

                if (lootData != null)
                {
                    if (lootData.Item != null)
                    {
                        item = lootData.Item;
                        source = "generated";
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"Container '{obj.name}' found in AllLoot but Item is null; falling back to empty item.");
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"Container '{obj.name}' not found in AllLoot after {timeout}s; falling back to empty item.");
                }

                if (item == null)
                {
                    item = itemFactory.CreateItem(obj.containerId, lootable.Template, null);
                    source = "fallback";
                }
            }

            if (item == null)
            {
                Plugin.Log.LogWarning($"Failed to create item for container '{obj.name}'.");
                yield break;
            }

            int addedItems = 0;
            if (obj.lootMode == ContainerLootMode.Hybrid || obj.lootMode == ContainerLootMode.Custom)
            {
                addedItems = InjectMarkerItems(obj, item as CompoundItem);
            }

            var controller = new TraderControllerClass(item, item.Id, item.ShortName.Localized(null), true, EOwnerType.Profile);
            lootable.Init(controller);
            var finalGameWorld = Singleton<GameWorld>.Instance;
            if (finalGameWorld != null)
                finalGameWorld.RegisterLoot<LootableContainer>(lootable);
            else
                Plugin.Log.LogWarning($"GameWorld not available for container '{obj.name}' loot registration; container initialized but not registered with world.");
            Plugin.Log.LogInfo($"Initialized lootable container '{obj.name}' id={obj.containerId}, template={lootable.Template}, itemId={item.Id}, source={source}, injectedItems={addedItems}");
        }

        private int InjectMarkerItems(InteractiveObject obj, CompoundItem compoundItem)
        {
            if (compoundItem == null || obj.items == null)
                return 0;

            var itemFactory = Singleton<ItemFactoryClass>.Instance;
            if (itemFactory == null)
                return 0;

            int added = 0;
            foreach (var loot in obj.items)
            {
                if (string.IsNullOrWhiteSpace(loot.template))
                    continue;

                if (!QuestConditionsMet(loot.questOnly, loot.questCompleted, loot.questId))
                {
                    Plugin.Log.LogInfo($"Skipping quest-gated item {loot.template} for container '{obj.name}' (quest {loot.questId} not active/completed).");
                    continue;
                }

                if (loot.chance < 100f)
                {
                    var roll = UnityEngine.Random.Range(0f, 100f);
                    if (roll >= loot.chance)
                    {
                        Plugin.Log.LogInfo($"Item {loot.template} chance roll {roll:F1} >= {loot.chance}; skipping for container '{obj.name}'.");
                        continue;
                    }
                }

                var childItem = itemFactory.CreateItem(GenerateItemId(), loot.template, null);
                if (childItem == null)
                {
                    Plugin.Log.LogWarning($"Failed to create item {loot.template} for container '{obj.name}'");
                    continue;
                }

                bool placed = false;
                if (compoundItem.Grids != null)
                {
                    foreach (var grid in compoundItem.Grids)
                    {
                        var result = grid.AddAnywhere(childItem, EErrorHandlingType.Ignore);
                        if (result.Succeeded)
                        {
                            placed = true;
                            break;
                        }
                    }
                }

                if (placed)
                    added++;
                else
                    Plugin.Log.LogWarning($"Could not place item {loot.template} in container '{obj.name}'");
            }

            return added;
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
    }
}
