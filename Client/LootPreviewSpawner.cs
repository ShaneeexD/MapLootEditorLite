using System;
using System.Collections;
using System.Collections.Generic;
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

        public LootPreviewSpawner(GameObject root)
        {
            _root = root;
            _runner = root.GetComponent<CoroutineRunner>() ?? root.AddComponent<CoroutineRunner>();
        }

        public void SpawnAtMarker(LooseLootSpawn marker)
        {
            var tpl = GetFirstTpl(marker.items);
            var pos = marker.position.ToVector3();
            var ground = MarkerManager.GetGroundPosition(pos);
            var rotation = marker.rotation.ToQuaternion();
            SpawnPreview(tpl, ground ?? pos, rotation, marker.name, marker.id);
        }

        public void SpawnInZone(LootZone marker)
        {
            var tpl = GetFirstTpl(marker.items);
            var pos = RandomPointInZone(marker);
            var rotation = GetZoneItemRotation(marker.items);
            SpawnPreview(tpl, pos, rotation, marker.name, marker.id);
        }

        private Vector3 RandomPointInZone(LootZone zone)
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

        public void SpawnAtZoneCenter(LootZone marker)
        {
            var tpl = GetFirstTpl(marker.items);
            var rotation = GetZoneItemRotation(marker.items);
            SpawnPreview(tpl, marker.position.ToVector3(), rotation, marker.name, marker.id);
        }

        private string GetFirstTpl(List<LootItem> items)
        {
            if (items != null && items.Count > 0 && !string.IsNullOrEmpty(items[0].template))
                return items[0].template;
            return DefaultItemTpl;
        }

        private LootItem GetFirstItem(List<LootItem> items)
        {
            if (items != null && items.Count > 0)
                return items[0];
            return null;
        }

        private Quaternion GetZoneItemRotation(List<LootItem> items)
        {
            var item = GetFirstItem(items);
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

        public void SpawnPreview(string itemTpl, Vector3 position, Quaternion rotation, string markerName, string markerId)
        {
            ClearByMarkerId(markerId);
            var fallback = CreateFallbackPreview(itemTpl, position, rotation);
            AttachMeta(fallback, itemTpl, markerName, markerId, true);
            _previews.Add(fallback);
            Plugin.Log.LogInfo($"Spawned fallback preview for {markerName} using tpl {itemTpl}; loading real asset...");

            _runner.StartCoroutine(LoadRealPreviewCoroutine(itemTpl, position, rotation, markerName, markerId, fallback));
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

                preview.transform.SetParent(_root.transform, false);
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

        private IEnumerator LoadRealPreviewCoroutine(string itemTpl, Vector3 position, Quaternion rotation, string markerName, string markerId, GameObject fallback)
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

            var real = TrySpawnRealPreview(itemTpl, position, rotation);
            if (real != null)
            {
                _previews.Remove(fallback);
                UnityEngine.Object.Destroy(fallback);
                AttachMeta(real, itemTpl, markerName, markerId, false);
                _previews.Add(real);
                Plugin.Log.LogInfo($"Real preview loaded for {markerName} using tpl {itemTpl}");
            }
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

        private void AttachMeta(GameObject preview, string itemTpl, string markerName, string markerId, bool fallback)
        {
            var meta = preview.AddComponent<PreviewLootMarker>();
            meta.itemTpl = itemTpl;
            meta.markerName = markerName;
            meta.sourceMarkerId = markerId;
            meta.previewOnly = true;
            meta.isFallback = fallback;
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

        public void UpdateSelected(LooseLootSpawn marker)
        {
            if (marker == null)
                return;
            var rotation = marker.rotation.ToQuaternion();
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;
                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta != null && meta.sourceMarkerId == marker.id)
                    preview.transform.rotation = rotation;
            }
        }

        public void UpdateSelected(LootZone marker)
        {
            if (marker == null)
                return;
            var item = GetFirstItem(marker.items);
            if (item == null)
                return;
            if (item.randomRotation)
                return;
            var rotation = item.rotation.ToQuaternion();
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;
                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta != null && meta.sourceMarkerId == marker.id)
                    preview.transform.rotation = rotation;
            }
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
    }

    public class CoroutineRunner : MonoBehaviour
    {
    }
}
