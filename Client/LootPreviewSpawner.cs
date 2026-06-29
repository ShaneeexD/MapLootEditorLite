using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.InventoryLogic;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class LootPreviewSpawner
    {
        public const string DefaultItemTpl = "544fb45d4bdc2dee738b4568";
        private readonly GameObject _root;
        private readonly CoroutineRunner _runner;
        private readonly List<GameObject> _previews = new List<GameObject>();
        private readonly List<GameObject> _staticPreviews = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _staticSources = new Dictionary<string, GameObject>();

        public LootPreviewSpawner(GameObject root)
        {
            _root = root;
            _runner = root.GetComponent<CoroutineRunner>() ?? root.AddComponent<CoroutineRunner>();
        }

        public void SpawnAtMarker(LooseLootSpawn marker, int itemIndex = 0)
        {
            var tpl = GetItemTpl(marker.items, itemIndex);
            var pos = marker.position.ToVector3();
            var ground = MarkerManager.GetGroundPosition(pos);
            var rotation = marker.rotation.ToQuaternion();
            SpawnPreview(tpl, ground ?? pos, rotation, pos, rotation, true, marker.name, marker.id);
        }

        public void SpawnInZone(LootZone marker, int itemIndex = 0)
        {
            var tpl = GetItemTpl(marker.items, itemIndex);
            var item = GetItem(marker.items, itemIndex);
            var pos = GetRandomPointInZone(marker);
            pos.y += item?.yOffset ?? 0f;
            var rotation = GetZoneItemRotation(marker.items, itemIndex);
            var markerPos = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            SpawnPreview(tpl, pos, rotation, markerPos, markerRot, item?.randomRotation != true, marker.name, marker.id);
        }

        public void SpawnAllInZone(LootZone marker)
        {
            if (marker.items == null || marker.items.Count == 0)
                return;

            ClearByMarkerId(marker.id);
            var markerPos = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            for (int i = 0; i < marker.items.Count; i++)
            {
                var tpl = GetItemTpl(marker.items, i);
                var item = GetItem(marker.items, i);
                var pos = GetRandomPointInZone(marker);
                pos.y += item?.yOffset ?? 0f;
                var rotation = GetZoneItemRotation(marker.items, i);
                SpawnPreviewInternal(tpl, pos, rotation, markerPos, markerRot, item?.randomRotation != true, marker.name, marker.id, false);
            }
        }

        public static Vector3 GetRandomPointInZone(LootZone zone)
        {
            var center = zone.position.ToVector3();
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    return new Vector3(
                        center.x + UnityEngine.Random.Range(-0.5f, 0.5f) * scale.x,
                        center.y,
                        center.z + UnityEngine.Random.Range(-0.5f, 0.5f) * scale.z);
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var r = zone.radius * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f)) * scale.x;
                    return new Vector3(center.x + r * Mathf.Cos(angle), center.y, center.z + r * Mathf.Sin(angle));
                default:
                    var point = center + UnityEngine.Random.insideUnitSphere * zone.radius * scale.x;
                    point.y = center.y;
                    return point;
            }
        }

        public void SpawnAtZoneCenter(LootZone marker, int itemIndex = 0)
        {
            var tpl = GetItemTpl(marker.items, itemIndex);
            var item = GetItem(marker.items, itemIndex);
            var pos = marker.position.ToVector3();
            pos.y += item?.yOffset ?? 0f;
            var rot = marker.rotation.ToQuaternion();
            var rotation = GetZoneItemRotation(marker.items, itemIndex);
            SpawnPreview(tpl, pos, rotation, pos, rot, item?.randomRotation != true, marker.name, marker.id);
        }

        private string GetItemTpl(List<LootItem> items, int index)
        {
            if (items != null && index >= 0 && index < items.Count && !string.IsNullOrEmpty(items[index].template))
                return items[index].template;
            return DefaultItemTpl;
        }

        private LootItem GetItem(List<LootItem> items, int index)
        {
            if (items != null && index >= 0 && index < items.Count)
                return items[index];
            return null;
        }

        private Quaternion GetZoneItemRotation(List<LootItem> items, int index = 0)
        {
            var item = GetItem(items, index);
            if (item == null)
                return Quaternion.identity;
            if (item.randomRotation)
                return RandomYRotation();
            return item.rotation.ToQuaternion();
        }

        private static Quaternion RandomYRotation()
        {
            return Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        }

        public void SpawnPreview(string itemTpl, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId)
        {
            SpawnPreviewInternal(itemTpl, position, rotation, markerPosition, markerRotation, syncRotation, markerName, markerId, true);
        }

        private void SpawnPreviewInternal(string itemTpl, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId, bool clear)
        {
            if (clear)
                ClearByMarkerId(markerId);
            var fallback = CreateFallbackPreview(itemTpl, position, rotation);
            AttachMeta(fallback, itemTpl, markerName, markerId, true, position - markerPosition, Quaternion.Inverse(markerRotation) * rotation, syncRotation);
            _previews.Add(fallback);
            Plugin.Log.LogInfo($"Spawned fallback preview for {markerName} using tpl {itemTpl}; loading real asset...");

            _runner.StartCoroutine(LoadRealPreviewCoroutine(itemTpl, position, rotation, markerPosition, markerRotation, syncRotation, markerName, markerId, fallback));
        }

        private GameObject TrySpawnRealPreview(string itemTpl, Vector3 position, Quaternion rotation)
        {
            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory == null)
                    return null;

                var item = factory.CreateItem(MongoID.Generate(true).ToString(), itemTpl, null);
                if (item == null)
                    return null;

                var pool = Singleton<PoolManagerClass>.Instance;
                if (pool == null)
                    return null;

                var preview = pool.CreateLootPrefab(item, ECameraType.Default, null);
                if (preview == null)
                    return null;

                preview.transform.position = position;
                preview.transform.rotation = rotation;
                preview.transform.localScale = Vector3.one;
                preview.SetActive(true);
                EnableRenderers(preview);
                DisablePhysics(preview);
                return preview;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Real preview not ready for {itemTpl}: {ex.Message}");
                return null;
            }
        }

        private IEnumerator LoadRealPreviewCoroutine(string itemTpl, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId, GameObject fallback)
        {
            var task = PreloadBundles(itemTpl);
            while (!task.IsCompleted)
            {
                yield return null;
            }

            if (task.IsFaulted)
            {
                Plugin.Log.LogDebug($"Failed to preload asset for {itemTpl}: {task.Exception?.GetBaseException()?.Message}");
                yield break;
            }

            if (!_previews.Contains(fallback))
                yield break;

            var offset = position - markerPosition;
            var rotationOffset = Quaternion.Inverse(markerRotation) * rotation;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var real = TrySpawnRealPreview(itemTpl, position, rotation);
                if (real != null)
                {
                    var currentPos = fallback.transform.position;
                    var currentRot = fallback.transform.rotation;
                    _previews.Remove(fallback);
                    UnityEngine.Object.Destroy(fallback);
                    AttachMeta(real, itemTpl, markerName, markerId, false, offset, rotationOffset, syncRotation);
                    real.transform.position = currentPos;
                    real.transform.rotation = currentRot;
                    _previews.Add(real);
                    Plugin.Log.LogInfo($"Real preview loaded for {markerName} using tpl {itemTpl}");
                    yield break;
                }

                yield return null;
            }

            Plugin.Log.LogInfo($"Keeping fallback preview for {markerName} using tpl {itemTpl}");
        }

        private async Task PreloadBundles(string itemTpl)
        {
            var pool = Singleton<PoolManagerClass>.Instance;
            if (pool == null)
                return;

            var factory = Singleton<ItemFactoryClass>.Instance;
            if (factory == null)
                return;

            if (!factory.ItemTemplates.TryGetValue(itemTpl, out var template))
                return;

            var keys = new List<ResourceKey>();
            if (template.Prefab != null)
                keys.Add(template.Prefab);
            if (template.UsePrefab != null)
                keys.Add(template.UsePrefab);
            if (keys.Count == 0)
                return;

            await pool.LoadBundlesAndCreatePools(
                0,
                PoolManagerClass.AssemblyType.Local,
                keys.ToArray(),
                JobPriorityClass.Immediate,
                null,
                CancellationToken.None);
        }

        private void AttachMeta(GameObject preview, string itemTpl, string markerName, string markerId, bool fallback, Vector3 offset, Quaternion rotationOffset, bool syncRotation)
        {
            var meta = preview.AddComponent<PreviewLootMarker>();
            meta.itemTpl = itemTpl;
            meta.markerName = markerName;
            meta.sourceMarkerId = markerId;
            meta.previewOnly = true;
            meta.isFallback = fallback;
            meta.syncRotation = syncRotation;
            meta.offset = offset;
            meta.rotationOffset = rotationOffset;
        }

        private GameObject CreateFallbackPreview(string itemTpl, Vector3 position, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one * 0.25f;
            var renderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;
            return go;
        }

        private void DisablePhysics(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = true;

            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        private void EnableRenderers(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
        }

        public void DrawLabels(bool editorOpen)
        {
            if (!editorOpen)
                return;

            var camera = Camera.main;
            if (camera == null)
                return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.yellow }
            };

            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewLootMarker>();
                var label = meta != null ? $"{meta.markerName}\n{meta.itemTpl}" : "preview";
                var screenPos = camera.WorldToScreenPoint(preview.transform.position);
                if (screenPos.z <= 0)
                    continue;

                var rect = new Rect(screenPos.x - 60f, Screen.height - screenPos.y - 45f, 120f, 30f);
                GUI.Label(rect, label, style);
            }
        }

        public void UpdateForMarker(MarkerBase marker)
        {
            if (marker == null)
                return;

            var markerPos = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta == null || meta.sourceMarkerId != marker.id)
                    continue;

                preview.transform.position = markerPos + markerRot * meta.offset;

                if (meta.syncRotation)
                    preview.transform.rotation = markerRot * meta.rotationOffset;
            }

            foreach (var preview in _staticPreviews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewStaticObjectMarker>();
                if (meta == null || meta.sourceMarkerId != marker.id)
                    continue;

                preview.transform.position = markerPos;
                preview.transform.rotation = markerRot;
                if (marker is StaticObject so)
                    preview.transform.localScale = so.scale.ToVector3();
            }
        }

        public void SpawnAllPreviews(MapData data)
        {
            if (data == null)
                return;
            foreach (var marker in data.lootSpawns ?? Enumerable.Empty<LooseLootSpawn>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.lootZones ?? Enumerable.Empty<LootZone>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.objects ?? Enumerable.Empty<StaticObject>())
                SpawnPreviewForMarker(marker);
        }

        public void ClearAll()
        {
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

                DestroyPreview(preview);
            }
            _previews.Clear();

            foreach (var preview in _staticPreviews)
            {
                if (preview != null)
                    UnityEngine.Object.Destroy(preview);
            }
            _staticPreviews.Clear();
            _staticSources.Clear();
        }

        public void ClearByMarkerId(string markerId)
        {
            if (string.IsNullOrEmpty(markerId))
                return;

            for (int i = _previews.Count - 1; i >= 0; i--)
            {
                var preview = _previews[i];
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta != null && meta.sourceMarkerId == markerId)
                {
                    _previews.RemoveAt(i);
                    DestroyPreview(preview);
                }
            }

            for (int i = _staticPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _staticPreviews[i];
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewStaticObjectMarker>();
                if (meta != null && meta.sourceMarkerId == markerId)
                {
                    _staticPreviews.RemoveAt(i);
                    UnityEngine.Object.Destroy(preview);
                }
            }

            _staticSources.Remove(markerId);
        }

        public void SpawnPreviewForMarker(MarkerBase marker)
        {
            if (marker == null)
                return;
            switch (marker.Kind)
            {
                case MarkerKind.LooseLoot:
                    SpawnAtMarker((LooseLootSpawn)marker);
                    break;
                case MarkerKind.LootZone:
                    SpawnAtZoneCenter((LootZone)marker);
                    break;
                case MarkerKind.StaticObject:
                    SpawnStaticPreview((StaticObject)marker);
                    break;
            }
        }

        public void RegisterStaticSource(string markerId, GameObject source)
        {
            if (string.IsNullOrEmpty(markerId) || source == null) return;
            _staticSources[markerId] = source;
        }

        public void SpawnStaticPreview(StaticObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview static object: marker is null.");
                return;
            }

            if (string.IsNullOrEmpty(marker.prefabPath) && string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview static object: no prefab path or source object set.");
                return;
            }

            ClearByMarkerId(marker.id);
            _runner.StartCoroutine(LoadStaticPreviewCoroutine(marker));
        }

        private IEnumerator LoadStaticPreviewCoroutine(StaticObject marker)
        {
            GameObject source = null;
            if (_staticSources.TryGetValue(marker.id, out source) && source != null)
            {
                Plugin.Log.LogInfo($"Using cached source for static preview: {marker.name} ({source.name})");
                SpawnStaticInstance(source, marker, true);
                yield break;
            }

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (source != null)
                {
                    Plugin.Log.LogInfo($"Found source by name/position for static preview: {marker.name} ({source.name})");
                    SpawnStaticInstance(source, marker, true);
                    yield break;
                }
                Plugin.Log.LogWarning($"Could not find source scene object: {marker.sourceObjectName}, trying prefab path.");
            }

            if (!string.IsNullOrEmpty(marker.prefabPath))
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Windows", marker.prefabPath.TrimStart('/'));
                Plugin.Log.LogInfo($"Loading static object bundle: {path}");

                AssetBundle bundle = null;
                bool bundleOwned = false;
                string fileName = Path.GetFileName(path);
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b.name == fileName || b.name == marker.prefabPath)
                    {
                        bundle = b;
                        Plugin.Log.LogInfo($"Using already loaded bundle: {b.name}");
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
                        SpawnStaticInstance(prefab, marker, false);
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"No GameObject asset found in bundle: {path}");
                    }

                    if (bundleOwned)
                        bundle.Unload(false);
                }
                else
                {
                    Plugin.Log.LogWarning($"Failed to load static object bundle: {path}");
                }
                yield break;
            }

            Plugin.Log.LogWarning($"Cannot preview static object {marker.name}: no prefab path or source object set.");
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
                    if (root == _root) continue;
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

        private void SpawnStaticInstance(GameObject source, StaticObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"StaticPreview_{marker.name}";
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = marker.prefabPath;
            meta.isFallback = isFallback;

            _staticPreviews.Add(instance);
            Plugin.Log.LogInfo($"Spawned static object preview for {marker.name} (fallback={isFallback})");
        }

        private void DestroyPreview(GameObject preview)
        {
            var meta = preview.GetComponent<PreviewLootMarker>();
            if (meta != null && meta.isFallback)
            {
                UnityEngine.Object.Destroy(preview);
            }
            else
            {
                try { AssetPoolObject.ReturnToPool(preview, true); }
                catch { UnityEngine.Object.Destroy(preview); }
            }
        }
    }

    public class PreviewLootMarker : MonoBehaviour
    {
        public string itemTpl;
        public string markerName;
        public string sourceMarkerId;
        public bool previewOnly = true;
        public bool isFallback = false;
        public bool syncRotation = true;
        public Vector3 offset;
        public Quaternion rotationOffset;
    }

    public class PreviewStaticObjectMarker : MonoBehaviour
    {
        public string sourceMarkerId;
        public string prefabPath;
        public bool isFallback;
    }

    public class CoroutineRunner : MonoBehaviour
    {
    }
}
