using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeStaticObjectSpawner : MonoBehaviour
    {
        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private List<GameObject> _spawned = new List<GameObject>();

        private void Start()
        {
            LoadPacks();
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == _currentWorld)
                return;

            _currentWorld = world;
            if (world == null)
            {
                ClearSpawned();
                _currentMapId = null;
                return;
            }

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentMapId = mapId;
                ClearSpawned();
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
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    objects.AddRange(map.objects);
                }
            }

            if (objects.Count == 0)
            {
                Plugin.Log.LogInfo($"[MLEL Runtime] No static objects for map {mapId}");
                return;
            }

            Plugin.Log.LogInfo($"[MLEL Runtime] Spawning {objects.Count} static objects for map {mapId}");
            foreach (var obj in objects)
            {
                StartCoroutine(SpawnObjectCoroutine(obj));
            }
        }

        private IEnumerator SpawnObjectCoroutine(StaticObject obj)
        {
            if (string.IsNullOrEmpty(obj.prefabPath))
            {
                Plugin.Log.LogWarning($"[MLEL Runtime] Static object '{obj.name}' has no prefab path; skipping.");
                yield break;
            }

            string path = Path.Combine(Application.streamingAssetsPath, "Windows", obj.prefabPath.TrimStart('/'));
            Plugin.Log.LogInfo($"[MLEL Runtime] Loading static object bundle: {path}");

            AssetBundle bundle = null;
            bool bundleOwned = false;
            string fileName = Path.GetFileName(path);
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b.name == fileName || b.name == obj.prefabPath)
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
                if (bundle == null)
                {
                    Plugin.Log.LogWarning($"[MLEL Runtime] Failed to load bundle: {path}");
                    yield break;
                }
            }

            var assetRequest = bundle.LoadAllAssetsAsync<GameObject>();
            yield return assetRequest;

            var prefab = assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault();
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"[MLEL Runtime] No GameObject in bundle: {path}");
                if (bundleOwned)
                    bundle.Unload(true);
                yield break;
            }

            var instance = Instantiate(prefab);
            instance.name = $"StaticObject_{obj.name}";
            instance.transform.position = obj.position.ToVector3();
            instance.transform.rotation = obj.rotation.ToQuaternion();
            instance.transform.localScale = obj.scale.ToVector3();

            _spawned.Add(instance);
            Plugin.Log.LogInfo($"[MLEL Runtime] Spawned static object {obj.name} from {path}");

            if (bundleOwned)
                bundle.Unload(false);
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
            ClearSpawned();
        }
    }
}
