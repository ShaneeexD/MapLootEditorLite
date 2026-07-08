using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using Koenigz.PerfectCulling.EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class RuntimeOcclusionRepairSpawner : MonoBehaviour
    {
        public static RuntimeOcclusionRepairSpawner Instance { get; private set; }

        private readonly List<PackData> _packs = new List<PackData>();
        private GameWorld _currentWorld;
        private string _currentMapId;
        private readonly List<OcclusionRepairVolume> _volumes = new List<OcclusionRepairVolume>();
        private readonly HashSet<OcclusionRepairVolume> _activeVolumes = new HashSet<OcclusionRepairVolume>();
        private int _activeVolumeCount;
        private bool _occlusionSaved;
        private bool _originalOcclusion;
        private Coroutine _manageCoroutine;
        private readonly Dictionary<Renderer, (bool enabled, bool forceOff)> _rendererStates = new Dictionary<Renderer, (bool, bool)>();

        private readonly Dictionary<MonoBehaviour, CullingObjectState> _cullingObjectStates = new Dictionary<MonoBehaviour, CullingObjectState>();
        private readonly Dictionary<OcclusionRepairVolume, List<MonoBehaviour>> _volumeCullingObjects = new Dictionary<OcclusionRepairVolume, List<MonoBehaviour>>();

        private class CullingObjectState
        {
            public bool WasEnabled;
            public int RefCount;
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
            DeactivateAllVolumes();
            _volumes.Clear();
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
                    Plugin.Log.LogInfo("GameWorld changed or ended, clearing occlusion repair volumes.");
                    DeactivateAllVolumes();
                    _volumes.Clear();
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
                DeactivateAllVolumes();
                _volumes.Clear();
                LoadVolumesForMap();
            }

            if (_volumes.Count == 0)
                return;

            var player = _currentWorld?.MainPlayer;
            if (player == null || player.Transform == null)
                return;

            var playerPos = player.Transform.position;
            foreach (var volume in _volumes)
            {
                bool inside = IsPlayerInsideVolume(volume, playerPos);
                bool active = _activeVolumes.Contains(volume);

                if (inside && !active)
                    ActivateVolume(volume);
                else if (!inside && active)
                    DeactivateVolume(volume);
            }
        }

        private void LoadPacks()
        {
            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);

            if (directories.Count == 0)
            {
                Plugin.Log.LogWarning("No pack directories found; occlusion repair volumes will not be spawned.");
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

        private void LoadVolumesForMap()
        {
            var world = Singleton<GameWorld>.Instance;
            var mapId = world?.LocationId;
            if (string.IsNullOrEmpty(mapId) && world?.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (string.IsNullOrEmpty(mapId))
            {
                Plugin.Log.LogWarning("Cannot load occlusion repair volumes: no current map.");
                return;
            }

            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                {
                    foreach (var v in map.occlusionRepairVolumes ?? new List<OcclusionRepairVolume>())
                        _volumes.Add(v);
                }
            }

            Plugin.Log.LogInfo($"Loaded {_volumes.Count} occlusion repair volumes for map {mapId}.");
        }

        private bool IsPlayerInsideVolume(OcclusionRepairVolume volume, Vector3 position)
        {
            var center = volume.position.ToVector3();
            var rotation = volume.rotation.ToQuaternion();
            var scale = volume.scale.ToVector3();
            var localPos = Quaternion.Inverse(rotation) * (position - center);

            switch (volume.shape)
            {
                case ZoneShape.Box:
                    return Mathf.Abs(localPos.x) <= scale.x / 2f && Mathf.Abs(localPos.y) <= scale.y / 2f && Mathf.Abs(localPos.z) <= scale.z / 2f;
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var radius = Mathf.Max(scale.x, scale.z) / 2f;
                    var distXZ = Mathf.Sqrt(localPos.x * localPos.x + localPos.z * localPos.z);
                    return distXZ <= radius && Mathf.Abs(localPos.y) <= scale.y / 2f;
                default:
                    return localPos.magnitude <= scale.x / 2f;
            }
        }

        private void ActivateVolume(OcclusionRepairVolume volume)
        {
            _activeVolumes.Add(volume);
            _activeVolumeCount++;

            Plugin.Log.LogInfo($"Entered occlusion repair volume '{volume.name}'.");

            if (volume.disableCameraOcclusion && _activeVolumeCount == 1)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    _originalOcclusion = cam.useOcclusionCulling;
                    _occlusionSaved = true;
                    cam.useOcclusionCulling = false;
                }
            }

            if (volume.disableCullingObjects)
                DisableCullingObjectsInVolume(volume);

            if (volume.manageRenderers && _manageCoroutine == null)
                _manageCoroutine = StartCoroutine(ManageRenderers());
        }

        private void DeactivateVolume(OcclusionRepairVolume volume)
        {
            _activeVolumes.Remove(volume);
            _activeVolumeCount--;

            Plugin.Log.LogInfo($"Exited occlusion repair volume '{volume.name}'.");

            if (volume.disableCullingObjects)
                RestoreCullingObjectsInVolume(volume);

            if (_activeVolumeCount == 0)
            {
                if (_occlusionSaved && Camera.main != null)
                    Camera.main.useOcclusionCulling = _originalOcclusion;

                if (_manageCoroutine != null)
                {
                    StopCoroutine(_manageCoroutine);
                    _manageCoroutine = null;
                }

                RestoreRenderers();
            }
        }

        private void DeactivateAllVolumes()
        {
            _activeVolumes.Clear();
            _activeVolumeCount = 0;

            if (_occlusionSaved && Camera.main != null)
                Camera.main.useOcclusionCulling = _originalOcclusion;

            if (_manageCoroutine != null)
            {
                StopCoroutine(_manageCoroutine);
                _manageCoroutine = null;
            }

            RestoreRenderers();
            RestoreAllCullingObjects();
        }

        private void DisableCullingObjectsInVolume(OcclusionRepairVolume volume)
        {
            var center = volume.position.ToVector3();
            var radius = volume.cullingObjectRadius;
            var touched = new List<MonoBehaviour>();

            var colliders = Physics.OverlapSphere(center, radius);
            foreach (var col in colliders)
            {
                if (col == null)
                    continue;

                var go = col.gameObject;
                TryDisableCullingObject(go.GetComponent<DisablerCullingObjectBase>(), touched);
                TryDisableCullingObject(go.GetComponent<CullingObject>(), touched);
                TryDisableCullingObject(go.GetComponentInParent<DisablerCullingObjectBase>(), touched);
                TryDisableCullingObject(go.GetComponentInParent<CullingObject>(), touched);
            }

            _volumeCullingObjects[volume] = touched;
            if (touched.Count > 0)
                Plugin.Log.LogInfo($"Disabled {touched.Count} culling objects in volume '{volume.name}'.");
        }

        private void TryDisableCullingObject(MonoBehaviour cull, List<MonoBehaviour> touched)
        {
            if (cull == null)
                return;

            if (!_cullingObjectStates.TryGetValue(cull, out var state))
            {
                bool wasEnabled = cull.enabled;
                if (cull is CullingObject co)
                {
                    try { co.SetVisibility(true); } catch { }
                }
                else if (cull is DisablerCullingObjectBase dcb)
                {
                    try { dcb.SetComponentsEnabled(true); } catch { }
                }
                cull.enabled = false;
                state = new CullingObjectState { WasEnabled = wasEnabled, RefCount = 0 };
                _cullingObjectStates[cull] = state;
            }

            if (state.RefCount == 0)
                touched.Add(cull);
            state.RefCount++;
        }

        private void RestoreCullingObjectsInVolume(OcclusionRepairVolume volume)
        {
            if (!_volumeCullingObjects.TryGetValue(volume, out var list))
                return;

            int restored = 0;
            foreach (var cull in list)
            {
                if (cull == null)
                    continue;
                if (!_cullingObjectStates.TryGetValue(cull, out var state))
                    continue;

                state.RefCount--;
                if (state.RefCount <= 0)
                {
                    cull.enabled = state.WasEnabled;
                    if (cull is DisablerCullingObjectBase dcb)
                    {
                        try { dcb.UpdateComponentsStatusOnUpdate(); } catch { }
                    }
                    _cullingObjectStates.Remove(cull);
                    restored++;
                }
            }

            _volumeCullingObjects.Remove(volume);
            if (restored > 0)
                Plugin.Log.LogInfo($"Restored {restored} culling objects for volume '{volume.name}'.");
        }

        private void RestoreAllCullingObjects()
        {
            foreach (var kvp in _cullingObjectStates)
            {
                var cull = kvp.Key;
                if (cull == null)
                    continue;
                cull.enabled = kvp.Value.WasEnabled;
                if (cull is DisablerCullingObjectBase dcb)
                {
                    try { dcb.UpdateComponentsStatusOnUpdate(); } catch { }
                }
            }
            _cullingObjectStates.Clear();
            _volumeCullingObjects.Clear();
        }

        private IEnumerator ManageRenderers()
        {
            while (_activeVolumes.Count > 0)
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;
                var cam = Camera.main;
                if (player != null && cam != null)
                {
                    var playerPos = player.Transform.position;
                    var camPos = cam.transform.position;
                    var touched = new HashSet<Renderer>();

                    foreach (var volume in _activeVolumes)
                    {
                        var center = volume.position.ToVector3();
                        var radius = volume.rendererRadius;
                        var maxDist = volume.maxVisibleDistance;
                        var mask = string.IsNullOrWhiteSpace(volume.raycastMask)
                            ? ~0
                            : LayerMask.GetMask(volume.raycastMask.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));

                        var colliders = Physics.OverlapSphere(center, radius);
                        foreach (var col in colliders)
                        {
                            if (col == null)
                                continue;

                            var renderers = col.GetComponentsInChildren<Renderer>(false);
                            foreach (var r in renderers)
                            {
                                if (r == null)
                                    continue;

                                if (r.forceRenderingOff && !_rendererStates.ContainsKey(r))
                                    continue;

                                touched.Add(r);
                                if (!_rendererStates.ContainsKey(r))
                                    _rendererStates[r] = (r.enabled, r.forceRenderingOff);

                                float dist = Vector3.Distance(playerPos, r.bounds.center);
                                bool shouldEnable = dist <= maxDist;
                                if (shouldEnable && volume.raycastCull)
                                {
                                    if (Physics.Linecast(camPos, r.bounds.center, out var hit, mask))
                                        shouldEnable = hit.transform.IsChildOf(r.transform) || r.transform.IsChildOf(hit.transform);
                                }

                                r.enabled = shouldEnable;
                                r.forceRenderingOff = !shouldEnable;
                            }
                        }
                    }

                    var toRemove = new List<Renderer>();
                    foreach (var kvp in _rendererStates)
                    {
                        var r = kvp.Key;
                        if (r == null)
                        {
                            toRemove.Add(r);
                            continue;
                        }
                        if (!touched.Contains(r))
                        {
                            r.enabled = kvp.Value.enabled;
                            r.forceRenderingOff = kvp.Value.forceOff;
                            toRemove.Add(r);
                        }
                    }
                    foreach (var r in toRemove)
                        _rendererStates.Remove(r);
                }

                yield return new WaitForSeconds(GetCheckInterval());
            }

            _manageCoroutine = null;
        }

        private float GetCheckInterval()
        {
            float interval = 0.25f;
            foreach (var v in _activeVolumes)
                interval = Mathf.Min(interval, v.checkInterval);
            return Mathf.Max(interval, 0.05f);
        }

        private void RestoreRenderers()
        {
            foreach (var kvp in _rendererStates)
            {
                var r = kvp.Key;
                if (r == null)
                    continue;
                r.enabled = kvp.Value.enabled;
                r.forceRenderingOff = kvp.Value.forceOff;
            }
            _rendererStates.Clear();
        }

        private void OnDestroy()
        {
            Instance = null;
            DeactivateAllVolumes();
        }
    }
}
