using System;
using System.Collections.Generic;
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
        private readonly List<GameObject> _previews = new List<GameObject>();

        public LootPreviewSpawner(GameObject root)
        {
            _root = root;
        }

        public void SpawnAtMarker(LooseLootSpawn marker)
        {
            var tpl = GetFirstTpl(marker.itemTpls);
            var pos = marker.position.ToVector3();
            var ground = MarkerManager.GetGroundPosition(pos);
            SpawnPreview(tpl, ground ?? pos, marker.name, marker.id);
        }

        public void SpawnInZone(LootZone marker)
        {
            var tpl = GetFirstTpl(marker.itemTpls);
            var center = marker.position.ToVector3();
            var randomPos = center + UnityEngine.Random.insideUnitSphere * marker.radius;
            randomPos.y = center.y;
            var ground = MarkerManager.GetGroundPosition(randomPos);
            SpawnPreview(tpl, ground ?? randomPos, marker.name, marker.id);
        }

        public void SpawnAtZoneCenter(LootZone marker)
        {
            var tpl = GetFirstTpl(marker.itemTpls);
            var pos = marker.position.ToVector3();
            var ground = MarkerManager.GetGroundPosition(pos);
            SpawnPreview(tpl, ground ?? pos, marker.name, marker.id);
        }

        private string GetFirstTpl(List<string> tpls)
        {
            if (tpls != null && tpls.Count > 0 && !string.IsNullOrEmpty(tpls[0]))
                return tpls[0];
            return DefaultItemTpl;
        }

        public void SpawnPreview(string itemTpl, Vector3 position, string markerName, string markerId)
        {
            GameObject preview = null;
            bool real = false;

            try
            {
                var factory = Singleton<ItemFactoryClass>.Instance;
                if (factory != null)
                {
                    var item = factory.CreateItem(MongoID.Generate(true).ToString(), itemTpl, null);
                    if (item != null)
                    {
                        var pool = Singleton<PoolManagerClass>.Instance;
                        if (pool != null)
                        {
                            preview = pool.CreateLootPrefab(item, ECameraType.Default, null);
                            if (preview != null)
                            {
                                real = true;
                                preview.transform.SetParent(_root.transform, false);
                                preview.transform.position = position;
                                preview.transform.rotation = Quaternion.identity;
                                preview.transform.localScale = Vector3.one;
                                DisablePhysics(preview);
                                AttachMeta(preview, itemTpl, markerName, markerId, false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to spawn real item preview for {itemTpl}: {ex.Message}");
                preview = null;
            }

            if (preview == null)
            {
                preview = CreateFallbackPreview(itemTpl, position);
                AttachMeta(preview, itemTpl, markerName, markerId, true);
            }

            _previews.Add(preview);
            Plugin.Log.LogInfo($"Spawned preview for {markerName} using tpl {itemTpl} (real={real})");
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

        private GameObject CreateFallbackPreview(string itemTpl, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.2f;
            var renderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = Color.magenta;
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

        public void ClearAll()
        {
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

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
            _previews.Clear();
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
}
