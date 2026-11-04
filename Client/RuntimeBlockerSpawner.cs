using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeBlockerSpawner : MonoBehaviour
    {
        public static RuntimeBlockerSpawner Instance { get; private set; }

        private List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private List<GameObject> _spawned = new List<GameObject>();
        private List<GameObject> _previewBlockers = new List<GameObject>();

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
            _packs = RuntimePackLoader.LoadPacks("custom blockers");
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
                    Plugin.Log.LogInfo("GameWorld changed or ended, clearing custom blockers.");
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
                SpawnCustomBlockers();
            }
        }

        private void LoadPacks()
        {
            _packs = RuntimePackLoader.LoadPacks("custom blockers");
        }

        private void SpawnCustomBlockers()
        {
            if (string.IsNullOrEmpty(_currentMapId))
                return;

            var blockers = new List<Blocker>();
            foreach (var pack in _packs)
            {
                if (RuntimePackLoader.TryGetMapData(pack, _currentMapId, out var mapData) && mapData.blockers != null)
                    blockers.AddRange(mapData.blockers);
            }

            if (blockers.Count == 0)
                return;

            Plugin.Log.LogInfo($"Spawning {blockers.Count} custom blockers for map {_currentMapId}.");
            foreach (var blocker in blockers)
            {
                try
                {
                    var go = new GameObject($"CustomBlocker_{blocker.name}");
                    go.transform.position = blocker.position.ToVector3();
                    go.transform.rotation = blocker.rotation.ToQuaternion();
                    go.transform.localScale = Vector3.one;

                    AddCollider(go, blocker);
                    _spawned.Add(go);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to create blocker '{blocker.name}': {ex.Message}");
                }
            }
        }

        private void AddCollider(GameObject go, Blocker blocker)
        {
            var scale = blocker.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (blocker.shape)
            {
                case ZoneShape.Box:
                    {
                        var col = go.AddComponent<BoxCollider>();
                        col.size = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        col.isTrigger = false;
                        break;
                    }
                case ZoneShape.Sphere:
                    {
                        var col = go.AddComponent<SphereCollider>();
                        col.radius = Mathf.Abs(scale.x) * 0.5f;
                        col.isTrigger = false;
                        break;
                    }
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    {
                        // Cylinder has no built-in primitive collider; use a capsule as the closest approximation.
                        // Unity capsule/cylinder primitives are 2 units tall and 1 unit in diameter at scale 1.
                        var col = go.AddComponent<CapsuleCollider>();
                        col.height = 2f * Mathf.Abs(scale.y);
                        col.radius = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) * 0.5f;
                        col.direction = 1; // Y-axis
                        col.isTrigger = false;
                        break;
                    }
                default:
                    {
                        var col = go.AddComponent<BoxCollider>();
                        col.size = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        col.isTrigger = false;
                        break;
                    }
            }
        }

        public void SpawnPreviewBlockers(List<Blocker> blockers)
        {
            ClearPreviewBlockers();
            if (blockers == null || blockers.Count == 0)
                return;

            Plugin.Log.LogInfo($"Spawning {blockers.Count} preview blockers.");
            foreach (var blocker in blockers)
            {
                try
                {
                    var go = new GameObject($"PreviewBlocker_{blocker.name}");
                    go.transform.SetParent(transform, false);
                    go.transform.position = blocker.position.ToVector3();
                    go.transform.rotation = blocker.rotation.ToQuaternion();
                    go.transform.localScale = Vector3.one;

                    AddCollider(go, blocker);
                    _previewBlockers.Add(go);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to create preview blocker '{blocker.name}': {ex.Message}");
                }
            }
        }

        public void ClearPreviewBlockers()
        {
            foreach (var go in _previewBlockers.Where(x => x != null))
                UnityEngine.Object.Destroy(go);
            _previewBlockers.Clear();
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned.Where(x => x != null))
                UnityEngine.Object.Destroy(go);
            _spawned.Clear();
        }
    }
}
