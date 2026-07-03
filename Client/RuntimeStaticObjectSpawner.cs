using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using EFT.Quests;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeStaticObjectSpawner : MonoBehaviour
    {
        public static RuntimeStaticObjectSpawner Instance { get; private set; }

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
                    Plugin.Log.LogInfo("[MLEL Runtime] GameWorld changed or ended, clearing static objects.");
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
                Plugin.Log.LogInfo($"[MLEL Runtime] Map detected: {mapId}, spawning static objects");
                SpawnForMap(mapId);
            }
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);
            if (!string.IsNullOrEmpty(Plugin.ServerModExportsDirectory) && Directory.Exists(Plugin.ServerModExportsDirectory))
                directories.Add(Plugin.ServerModExportsDirectory);

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning($"[MLEL Runtime] No pack directories found; static objects will not be spawned.");
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
                            Plugin.Log.LogInfo($"[MLEL Runtime] Loaded pack '{pack.name}' from {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[MLEL Runtime] Failed to load pack {file}: {ex.Message}");
                    }
                }
            }
        }

        private void SpawnForMap(string mapId)
        {
            var objects = new List<StaticObject>();
            var wttClones = new List<WTTStaticObject>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    foreach (var obj in map.objects ?? new List<StaticObject>())
                    {
                        if (!QuestConditionsMet(obj.questOnly, obj.questCompleted, obj.questId))
                        {
                            Plugin.Log.LogInfo($"[MLEL Runtime] Skipping quest-gated static object '{obj.name}' (quest {obj.questId} not active/completed).");
                            continue;
                        }
                        objects.Add(obj);
                    }

                    foreach (var obj in map.wttStaticObjects ?? new List<WTTStaticObject>())
                    {
                        if (obj.spawnType != "clone")
                            continue;

                        if (!QuestConditionsMet(obj.questOnly, obj.questCompleted, obj.questId))
                        {
                            Plugin.Log.LogInfo($"[MLEL Runtime] Skipping quest-gated WTT clone '{obj.name}' (quest {obj.questId} not active/completed).");
                            continue;
                        }
                        wttClones.Add(obj);
                    }
                }
            }

            var total = objects.Count + wttClones.Count;
            if (total == 0)
            {
                Plugin.Log.LogInfo($"[MLEL Runtime] No static objects for map {mapId}");
                return;
            }

            Plugin.Log.LogInfo($"[MLEL Runtime] Spawning {total} static objects for map {mapId} ({objects.Count} regular, {wttClones.Count} WTT clone)");
            foreach (var obj in objects)
            {
                StartCoroutine(SpawnObjectCoroutine(obj));
            }
            foreach (var obj in wttClones)
            {
                StartCoroutine(SpawnWTTCloneObjectCoroutine(obj));
            }
        }

        private IEnumerator SpawnObjectCoroutine(StaticObject obj)
        {
            yield return SpawnCloneObjectCoroutine(obj, obj, obj.scale, obj.prefabPath);
        }

        private IEnumerator SpawnWTTCloneObjectCoroutine(WTTStaticObject obj)
        {
            yield return SpawnCloneObjectCoroutine(obj, obj, obj.scale, null);
        }

        private IEnumerator SpawnCloneObjectCoroutine(IHasSourceObject sourceObj, MarkerBase marker, TransformData scale, string fallbackPrefabPath)
        {
            bool spawned = false;

            if (!string.IsNullOrEmpty(sourceObj.sourceObjectName))
            {
                GameObject source = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    source = FindSourceObject(sourceObj.sourceObjectName, sourceObj.sourceObjectPosition.ToVector3());
                    if (source != null)
                        break;

                    if (attempt == 0)
                        Plugin.Log.LogInfo($"[MLEL Runtime] Source object '{sourceObj.sourceObjectName}' for {marker.name} not ready, waiting...");

                    yield return new WaitForSeconds(1f);
                }

                if (source != null)
                {
                    SpawnObjectInstance(source, marker, scale, sourceObj.sourceObjectName, isFallback: true);
                    spawned = true;
                }
                else
                {
                    Plugin.Log.LogWarning($"[MLEL Runtime] Could not find source scene object '{sourceObj.sourceObjectName}' for {marker.name}");
                }
            }

            if (!spawned && !string.IsNullOrEmpty(fallbackPrefabPath))
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Windows", fallbackPrefabPath.TrimStart('/'));
                Plugin.Log.LogInfo($"[MLEL Runtime] Loading static object bundle: {path}");

                AssetBundle bundle = null;
                bool bundleOwned = false;
                string fileName = Path.GetFileName(path);
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b.name == fileName || b.name == fallbackPrefabPath)
                    {
                        bundle = b;
                        break;
                    }
                }

                if (bundle == null)
                {
                    var request = AssetBundle.LoadFromFileAsync(path);
                    yield return request;

                    bundle = request.assetBundle;
                    bundleOwned = true;
                }

                if (bundle != null)
                {
                    var assetRequest = bundle.LoadAllAssetsAsync<GameObject>();
                    yield return assetRequest;

                    var prefab = assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault();
                    if (prefab != null)
                    {
                        SpawnObjectInstance(prefab, marker, scale, fallbackPrefabPath, isFallback: false);
                        spawned = true;
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"[MLEL Runtime] No GameObject in bundle: {path}");
                    }

                    if (bundleOwned)
                        bundle.Unload(false);
                }
                else
                {
                    Plugin.Log.LogWarning($"[MLEL Runtime] Failed to load bundle: {path}");
                }
            }

            if (!spawned)
            {
                Plugin.Log.LogWarning($"[MLEL Runtime] Static object '{marker.name}' could not be spawned from bundle or source object.");
            }
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

        private void SpawnObjectInstance(GameObject source, StaticObject obj, bool isFallback)
        {
            var sourceName = isFallback ? obj.sourceObjectName : obj.prefabPath;
            SpawnObjectInstance(source, obj, obj.scale, sourceName, isFallback);
        }

        private void SpawnObjectInstance(GameObject source, MarkerBase marker, TransformData scale, string sourceName, bool isFallback)
        {
            var instance = Instantiate(source);
            instance.name = $"StaticObject_{marker.name}";
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = scale.ToVector3();
            _spawned.Add(instance);
            Plugin.Log.LogInfo($"[MLEL Runtime] Spawned static object {marker.name} (fallback={isFallback}, source={sourceName})");
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

        private void OnDestroy()
        {
            Instance = null;
            ClearSpawned();
        }
    }
}
