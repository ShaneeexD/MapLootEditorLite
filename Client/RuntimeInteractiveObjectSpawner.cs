using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeInteractiveObjectSpawner : MonoBehaviour
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
            if (world == null)
            {
                if (_currentWorld != null)
                {
                    ClearSpawned();
                    _currentWorld = null;
                    _currentMapId = null;
                }
                return;
            }

            _currentWorld = world;

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentMapId = mapId;
                ClearSpawned();
                Plugin.Log.LogInfo($"[MLEL Runtime] Map detected: {mapId}, spawning interactive objects");
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
                Plugin.Log.LogWarning($"[MLEL Runtime] No pack directories found; interactive objects will not be spawned.");
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
                Plugin.Log.LogInfo($"[MLEL Runtime] No interactive objects for map {mapId}");
                return;
            }

            Plugin.Log.LogInfo($"[MLEL Runtime] Spawning {objects.Count} interactive objects for map {mapId}");
            foreach (var obj in objects)
            {
                StartCoroutine(SpawnObjectCoroutine(obj));
            }
        }

        private IEnumerator SpawnObjectCoroutine(InteractiveObject obj)
        {
            if (string.IsNullOrEmpty(obj.sourceObjectName))
            {
                Plugin.Log.LogWarning($"[MLEL Runtime] Interactive object '{obj.name}' has no source object; skipping.");
                yield break;
            }

            GameObject source = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                source = FindSourceObject(obj.sourceObjectName, obj.sourceObjectPosition.ToVector3());
                if (source != null)
                    break;

                if (attempt == 0)
                    Plugin.Log.LogInfo($"[MLEL Runtime] Source object '{obj.sourceObjectName}' for {obj.name} not ready, waiting...");

                yield return new WaitForSeconds(1f);
            }

            if (source == null)
            {
                Plugin.Log.LogWarning($"[MLEL Runtime] Could not find source scene object '{obj.sourceObjectName}' for {obj.name}");
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
            _spawned.Add(instance);

            var wio = instance.GetComponent<WorldInteractiveObject>();
            if (wio != null)
            {
                if (obj.interactiveType == InteractiveObjectType.Door && !string.IsNullOrEmpty(obj.keyId))
                {
                    wio.KeyId = obj.keyId;
                    Plugin.Log.LogInfo($"[MLEL Runtime] Set door '{obj.name}' key to {obj.keyId}");
                }
                else if (obj.interactiveType == InteractiveObjectType.Container && !string.IsNullOrEmpty(obj.containerId))
                {
                    wio.Id = obj.containerId;
                    Plugin.Log.LogInfo($"[MLEL Runtime] Set container '{obj.name}' id to {obj.containerId}");
                }
            }

            Plugin.Log.LogInfo($"[MLEL Runtime] Spawned interactive object {obj.name} (type={obj.interactiveType})");
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
