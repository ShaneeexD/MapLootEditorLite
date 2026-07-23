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
using EFT.Interactive;
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
        private readonly List<GameObject> _effectPreviews = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _staticSources = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _fallbackCache = new Dictionary<string, GameObject>();

        public LootPreviewSpawner(GameObject root, MonoBehaviour runner)
        {
            _root = root;
            _runner = runner.gameObject.GetComponent<CoroutineRunner>() ?? runner.gameObject.AddComponent<CoroutineRunner>();
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

        public void SpawnLightPreview(LightZone marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var pos = marker.position.ToVector3();
            var go = new GameObject($"LightPreview_{marker.name}");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;
            go.transform.rotation = marker.rotation.ToQuaternion();

            var light = go.AddComponent<Light>();
            light.type = ParseLightType(marker.lightType);
            light.color = marker.color.ToColor().linear;
            light.intensity = marker.intensity;
            light.range = marker.range;
            light.enabled = marker.enabled;
            light.shadows = ParseLightShadows(marker.shadows);
            light.shadowStrength = marker.shadowStrength;
            light.shadowBias = marker.shadowBias;
            light.shadowNormalBias = marker.shadowNormalBias;
            if (light.type == LightType.Spot)
                light.spotAngle = marker.spotAngle;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "LightPreviewVisual";
            sphere.transform.SetParent(go.transform, false);
            sphere.transform.localScale = Vector3.one * 0.25f;
            UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
                mat.color = light.color;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", light.color * 2f);
                renderer.material = mat;
            }

            var dir = new GameObject("LightPreviewDirection");
            dir.transform.SetParent(go.transform, false);
            var dirLr = dir.AddComponent<LineRenderer>();
            dirLr.useWorldSpace = false;
            dirLr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
            dirLr.startWidth = 0.04f;
            dirLr.endWidth = 0.04f;
            DrawLightDirection(dirLr, marker);

            var meta = go.AddComponent<PreviewEffectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.markerName = marker.name;

            _effectPreviews.Add(go);
        }

        private void DrawLightDirection(LineRenderer lr, LightZone lz)
        {
            var type = lz.lightType ?? "Point";
            var color = lz.color.ToColor();
            color.a = 1f;
            var positions = new List<Vector3>();

            if (type == "Spot")
            {
                var length = Mathf.Clamp(lz.range * 0.2f, 0.5f, 4f);
                var radius = length * Mathf.Tan(lz.spotAngle * 0.5f * Mathf.Deg2Rad);
                var apex = Vector3.zero;
                var center = Vector3.forward * length;
                positions.Add(apex);
                positions.Add(center);
                const int segments = 24;
                for (int i = 0; i <= segments; i++)
                {
                    var angle = i * Mathf.PI * 2f / segments;
                    var point = center + Vector3.right * Mathf.Cos(angle) * radius + Vector3.up * Mathf.Sin(angle) * radius;
                    positions.Add(apex);
                    positions.Add(point);
                }
            }
            else if (type == "Directional")
            {
                positions.Add(Vector3.zero);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.right * 0.2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.left * 0.2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.up * 0.2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.down * 0.2f);
            }
            else
            {
                positions.Add(Vector3.zero);
                positions.Add(Vector3.forward * 0.3f);
            }

            lr.positionCount = positions.Count;
            lr.SetPositions(positions.ToArray());
            lr.startColor = color;
            lr.endColor = color;
        }

        public void SpawnBotSpawnPreview(BotSpawnPoint marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var go = CreateBotPreviewObject(marker.position.ToVector3(), marker.rotation.ToQuaternion(), marker.name, marker.id, marker.preset.ToString(), marker.side.ToString(), marker.category.ToString());
            _effectPreviews.Add(go);
        }

        public void SpawnBotSpawnZonePreview(BotSpawnZone marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var center = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            var scale = marker.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            var count = Mathf.Max(1, marker.spawnCount);
            var positions = new List<Vector3>();

            for (int i = 0; i < count; i++)
            {
                var point = GetRandomPointInZone(center, marker.shape, marker.radius, scale, markerRot);
                positions.Add(point);
                var go = CreateBotPreviewObject(point, markerRot, marker.name, marker.id, marker.preset.ToString(), marker.side.ToString(), marker.category.ToString(), i);
                _effectPreviews.Add(go);
            }
        }

        public void SpawnPmcSpawnZonePreview(PmcSpawnZone marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var center = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            var scale = marker.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            var count = Mathf.Max(1, marker.maxGroupSize);

            for (int i = 0; i < count; i++)
            {
                var point = GetRandomPointInZone(center, marker.shape, marker.radius, scale, markerRot);
                var go = CreateBotPreviewObject(point, markerRot, marker.name, marker.id, marker.preset.ToString(), marker.side.ToString(), marker.category.ToString(), i);
                _effectPreviews.Add(go);
            }
        }

        private GameObject CreateBotPreviewObject(Vector3 position, Quaternion rotation, string markerName, string markerId, string preset, string side, string category, int index = -1)
        {
            var go = new GameObject($"BotPreview_{markerName}" + (index >= 0 ? $"_{index}" : ""));
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            go.transform.rotation = rotation;

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "BotPreviewVisual";
            capsule.transform.SetParent(go.transform, false);
            capsule.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
            capsule.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            UnityEngine.Object.Destroy(capsule.GetComponent<Collider>());
            var renderer = capsule.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
                mat.color = GetBotPreviewColor(side, category);
                renderer.material = mat;
            }

            var meta = go.AddComponent<PreviewEffectMarker>();
            meta.sourceMarkerId = markerId;
            meta.markerName = markerName;
            meta.label = $"{preset}\n{side} {category}";

            return go;
        }

        private static Color GetBotPreviewColor(string side, string category)
        {
            if (side == "Usec" || side == "Bear" || side == "Pmc")
                return new Color(0.2f, 0.4f, 1f, 0.8f);
            if (category == "Boss")
                return new Color(1f, 0.2f, 0.2f, 0.8f);
            if (category == "BotPmc")
                return new Color(1f, 0.8f, 0.2f, 0.8f);
            return new Color(0.4f, 0.8f, 0.2f, 0.8f);
        }

        private static Vector3 GetRandomPointInZone(Vector3 center, ZoneShape shape, float radius, TransformData scale, Quaternion rotation)
        {
            switch (shape)
            {
                case ZoneShape.Box:
                    return center + rotation * new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f) * scale.x,
                        0f,
                        UnityEngine.Random.Range(-0.5f, 0.5f) * scale.z);
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var r = radius * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f)) * scale.x;
                    return center + rotation * new Vector3(r * Mathf.Cos(angle), 0f, r * Mathf.Sin(angle));
                default:
                    var point = center + rotation * UnityEngine.Random.insideUnitSphere * radius * scale.x;
                    point.y = center.y;
                    return point;
            }
        }

        private static LightType ParseLightType(string type)
        {
            if (Enum.TryParse<LightType>(type, true, out var result))
                return result;
            return LightType.Point;
        }

        private static LightShadows ParseLightShadows(string shadows)
        {
            if (Enum.TryParse<LightShadows>(shadows, true, out var result))
                return result;
            return LightShadows.None;
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
                    yield break;
                }

                yield return null;
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
                if (CustomEditorUI.IsScreenPointOverEditorUI(new Vector2(screenPos.x, screenPos.y + 30f)))
                    continue;

                var rect = new Rect(screenPos.x - 60f, Screen.height - screenPos.y - 45f, 120f, 30f);
                GUI.Label(rect, label, style);
            }

            foreach (var preview in _effectPreviews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewEffectMarker>();
                var label = meta != null ? $"{meta.markerName}\n{meta.label}" : "effect";
                var screenPos = camera.WorldToScreenPoint(preview.transform.position);
                if (screenPos.z <= 0)
                    continue;
                if (CustomEditorUI.IsScreenPointOverEditorUI(new Vector2(screenPos.x, screenPos.y + 30f)))
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

            foreach (var preview in _effectPreviews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewEffectMarker>();
                if (meta == null || meta.sourceMarkerId != marker.id)
                    continue;

                if (marker is LightZone lz)
                {
                    preview.transform.position = markerPos;
                    preview.transform.rotation = markerRot;
                    var light = preview.GetComponent<Light>();
                    if (light != null)
                    {
                        light.type = ParseLightType(lz.lightType);
                        light.color = lz.color.ToColor().linear;
                        light.intensity = lz.intensity;
                        light.range = lz.range;
                        light.shadows = ParseLightShadows(lz.shadows);
                        light.shadowStrength = lz.shadowStrength;
                        light.shadowBias = lz.shadowBias;
                        light.shadowNormalBias = lz.shadowNormalBias;
                        if (light.type == LightType.Spot)
                            light.spotAngle = lz.spotAngle;
                    }
                    var sphere = preview.transform.Find("LightPreviewVisual")?.GetComponent<Renderer>();
                    if (sphere != null)
                    {
                        sphere.material.color = light.color;
                        sphere.material.SetColor("_EmissionColor", light.color * 2f);
                    }
                    var dirLr = preview.transform.Find("LightPreviewDirection")?.GetComponent<LineRenderer>();
                    if (dirLr != null)
                        DrawLightDirection(dirLr, lz);
                }
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
            foreach (var marker in data.wttStaticObjects ?? Enumerable.Empty<WTTStaticObject>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.interactiveObjects ?? Enumerable.Empty<InteractiveObject>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.lightZones ?? Enumerable.Empty<LightZone>())
                SpawnPreviewForMarker(marker);
        }

        public void ClearAll(bool clearStaticSources = false)
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

            foreach (var preview in _effectPreviews)
            {
                if (preview != null)
                    UnityEngine.Object.Destroy(preview);
            }
            _effectPreviews.Clear();

            if (clearStaticSources)
            {
                _staticSources.Clear();
                foreach (var kvp in _fallbackCache)
                {
                    if (kvp.Value != null)
                        UnityEngine.Object.Destroy(kvp.Value);
                }
                _fallbackCache.Clear();
            }
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

            for (int i = _effectPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _effectPreviews[i];
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewEffectMarker>();
                if (meta != null && meta.sourceMarkerId == markerId)
                {
                    _effectPreviews.RemoveAt(i);
                    UnityEngine.Object.Destroy(preview);
                }
            }
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
                case MarkerKind.WTTStaticObject:
                    SpawnWTTStaticPreview((WTTStaticObject)marker);
                    break;
                case MarkerKind.InteractiveObject:
                    SpawnInteractivePreview((InteractiveObject)marker);
                    break;
                case MarkerKind.LightZone:
                    SpawnLightPreview((LightZone)marker);
                    break;
                case MarkerKind.PmcSpawnZone:
                    SpawnPmcSpawnZonePreview((PmcSpawnZone)marker);
                    break;
            }
        }

        private static string GetStaticSourceKey(string name, Vector3 position)
        {
            return $"{name}@{position.x:F4},{position.y:F4},{position.z:F4}";
        }

        private void CacheSourceInstance(string key, GameObject source)
        {
            if (source == null || _fallbackCache.Values.Contains(source))
                return;
            if (_fallbackCache.TryGetValue(key, out var existing) && existing != null)
                return;
            var backup = UnityEngine.Object.Instantiate(source);
            backup.name = $"CachedSource_{key}";
            backup.transform.SetParent(_root.transform, false);
            backup.transform.position = Vector3.zero;
            backup.SetActive(false);
            _fallbackCache[key] = backup;
        }

        public void RegisterStaticSource(string markerId, GameObject source)
        {
            if (source == null) return;
            var key = GetStaticSourceKey(source.name, source.transform.position);
            _staticSources[key] = source;
            CacheSourceInstance(key, source);
        }

        public void SpawnSourcePreview(MarkerBase marker, GameObject source)
        {
            if (marker == null || source == null)
                return;

            ClearByMarkerId(marker.id);

            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"SourcePreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            var scale = Vector3.one;
            if (marker is StaticObject so) scale = so.scale.ToVector3();
            else if (marker is WTTStaticObject wtt) scale = wtt.scale.ToVector3();
            else if (marker is InteractiveObject io) scale = io.scale.ToVector3();
            else if (marker is CutVolume cv) scale = cv.scale.ToVector3();
            instance.transform.localScale = scale;

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = (marker as IHasSourceObject)?.sourceObjectName ?? "";
            meta.isFallback = true;

            _staticPreviews.Add(instance);

            var key = GetStaticSourceKey(source.name, source.transform.position);
            _staticSources[key] = source;
            CacheSourceInstance(key, source);
        }

        public void SpawnStaticPreview(StaticObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview static object: marker is null.");
                return;
            }

            ClearByMarkerId(marker.id);

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnStaticInstance(cached, marker, true);
                    return;
                }
            }
            if (!string.IsNullOrEmpty(marker.prefabPath))
            {
                if (_fallbackCache.TryGetValue(marker.prefabPath, out var cached) && cached != null)
                {
                    SpawnStaticInstance(cached, marker, false);
                    return;
                }
            }

            if (string.IsNullOrEmpty(marker.prefabPath) && string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview static object: no prefab path or source object set.");
                return;
            }

            _runner.StartCoroutine(LoadStaticPreviewCoroutine(marker));
        }

        public void SpawnWTTStaticPreview(WTTStaticObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview WTT static object: marker is null.");
                return;
            }

            ClearByMarkerId(marker.id);

            if (marker.spawnType == "clone" && !string.IsNullOrEmpty(marker.sourceObjectName))
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnWTTStaticInstance(cached, marker, true);
                    return;
                }
            }
            if (marker.spawnType == "bundle" && !string.IsNullOrEmpty(marker.bundleName) && !string.IsNullOrEmpty(marker.prefabName))
            {
                var cacheKey = $"{marker.bundleName}/{marker.prefabName}";
                if (_fallbackCache.TryGetValue(cacheKey, out var cached) && cached != null)
                {
                    SpawnWTTStaticInstance(cached, marker, false);
                    return;
                }
            }

            if (marker.spawnType == "clone" && string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview WTT static object: clone mode but no source object set.");
                return;
            }
            if (marker.spawnType == "bundle" && (string.IsNullOrEmpty(marker.bundleName) || string.IsNullOrEmpty(marker.prefabName)))
            {
                Plugin.Log.LogWarning("Cannot preview WTT static object: bundle mode but bundle/prefab name not set.");
                return;
            }

            _runner.StartCoroutine(LoadWTTStaticPreviewCoroutine(marker));
        }

        private IEnumerator LoadWTTStaticPreviewCoroutine(WTTStaticObject marker)
        {
            if (marker.spawnType == "clone")
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_staticSources.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnWTTStaticInstance(cached, marker, true);
                    yield break;
                }

                var source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (source != null)
                {
                    _staticSources[sourceKey] = source;
                    SpawnWTTStaticInstance(source, marker, true);
                    yield break;
                }

                Plugin.Log.LogWarning($"Could not find source scene object for WTT static preview: {marker.sourceObjectName}.");
                yield break;
            }

            // Bundle mode: load the bundle from the mod's bundles folder.
            string path = Path.Combine(Application.streamingAssetsPath, "Windows", "bundles", "staticspawns", $"{marker.bundleName}.bundle");

            AssetBundle bundle = null;
            bool bundleOwned = false;
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b.name == marker.bundleName || b.name == $"{marker.bundleName}.bundle")
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

                var prefab = assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault(a => a.name == marker.prefabName);
                prefab ??= assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault();

                if (prefab != null)
                {
                    SpawnWTTStaticInstance(prefab, marker, false);
                }
                else
                {
                    Plugin.Log.LogWarning($"No GameObject asset found in WTT bundle: {path}.");
                }

                if (bundleOwned)
                    bundle.Unload(false);
            }
            else
            {
                Plugin.Log.LogWarning($"Failed to load WTT static object bundle: {path}.");
            }
        }

        public void SpawnInteractivePreview(InteractiveObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview interactive object: marker is null.");
                return;
            }

            ClearByMarkerId(marker.id);

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnInteractiveInstance(cached, marker, true);
                    return;
                }
            }

            if (string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview interactive object: no source object set.");
                return;
            }

            _runner.StartCoroutine(LoadInteractivePreviewCoroutine(marker));
        }

        private IEnumerator LoadInteractivePreviewCoroutine(InteractiveObject marker)
        {
            var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            if (_staticSources.TryGetValue(sourceKey, out var cached) && cached != null)
            {
                SpawnInteractiveInstance(cached, marker, true);
                yield break;
            }

            var source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            if (source != null)
            {
                _staticSources[sourceKey] = source;
                SpawnInteractiveInstance(source, marker, true);
                yield break;
            }

            Plugin.Log.LogWarning($"Could not find source scene object for interactive preview: {marker.sourceObjectName}.");
        }

        private IEnumerator LoadStaticPreviewCoroutine(StaticObject marker)
        {
            GameObject source = null;
            var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            if (!string.IsNullOrEmpty(marker.sourceObjectName) && _staticSources.TryGetValue(sourceKey, out source) && source != null)
            {
                SpawnStaticInstance(source, marker, true);
                yield break;
            }

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (source != null)
                {
                    _staticSources[sourceKey] = source;
                    SpawnStaticInstance(source, marker, true);
                    yield break;
                }
                Plugin.Log.LogWarning($"Could not find source scene object: {marker.sourceObjectName}, trying prefab path.");
            }

            if (!string.IsNullOrEmpty(marker.prefabPath))
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Windows", marker.prefabPath.TrimStart('/'));

                AssetBundle bundle = null;
                bool bundleOwned = false;
                string fileName = Path.GetFileName(path);
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b.name == fileName || b.name == marker.prefabPath)
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
                        SpawnStaticInstance(prefab, marker, false);
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"No GameObject asset found in bundle: {path}.");
                    }

                    if (bundleOwned)
                        bundle.Unload(false);
                }
                else
                {
                    Plugin.Log.LogWarning($"Failed to load static object bundle: {path}.");
                }
                yield break;
            }

            Plugin.Log.LogWarning($"Cannot preview static object {marker.name}: no prefab path or source object set.");
        }

        private GameObject FindSourceObject(string name, Vector3 position)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var sourceKey = GetStaticSourceKey(name, position);
            if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                return cached;

            GameObject best = null;
            float bestDist = float.MaxValue;
            var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            int candidates = 0;

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
                        candidates++;
                        var dist = (t.position - position).sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = t.gameObject;
                        }
                    }
                }
            }

            if (best != null)
            {
                return best;
            }

            // Fallback: source object may have moved (e.g., opened door). Try matching by name only.
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
                        if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return t.gameObject;
                        }
                    }
                }
            }

            Plugin.Log.LogWarning($"No source object named '{name}' found near {position}. Scanned {candidates} candidates.");
            return null;
        }

        private void SpawnStaticInstance(GameObject source, StaticObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"StaticPreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = marker.prefabPath;
            meta.isFallback = isFallback;

            _staticPreviews.Add(instance);

            var cacheKey = !string.IsNullOrEmpty(marker.prefabPath) ? marker.prefabPath : GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            CacheSourceInstance(cacheKey, source);
        }

        private void SpawnWTTStaticInstance(GameObject source, WTTStaticObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"WTTStaticPreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = $"{marker.bundleName}/{marker.prefabName}";
            meta.isFallback = isFallback;

            _staticPreviews.Add(instance);

            var cacheKey = marker.spawnType == "bundle" ? $"{marker.bundleName}/{marker.prefabName}" : GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            CacheSourceInstance(cacheKey, source);
        }

        private void SpawnInteractiveInstance(GameObject source, InteractiveObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"InteractivePreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var wio = instance.GetComponent<WorldInteractiveObject>();
            if (wio != null && marker.interactiveType == InteractiveObjectType.Door && !string.IsNullOrEmpty(marker.keyId))
                wio.KeyId = marker.keyId;

            var lootable = instance.GetComponent<LootableContainer>();
            if (lootable != null && marker.interactiveType == InteractiveObjectType.Container && !string.IsNullOrEmpty(marker.containerId))
                lootable.Id = marker.containerId;

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = marker.sourceObjectName;
            meta.isFallback = isFallback;

            _staticPreviews.Add(instance);

            var cacheKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            CacheSourceInstance(cacheKey, source);
        }

        private void DestroyPreview(GameObject preview)
        {
            var meta = preview.GetComponent<PreviewLootMarker>();
            var staticMeta = preview.GetComponent<PreviewStaticObjectMarker>();
            if ((meta != null && meta.isFallback) || (staticMeta != null && staticMeta.isFallback))
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

    public class PreviewEffectMarker : MonoBehaviour
    {
        public string sourceMarkerId;
        public string markerName;
        public string label;
    }

    public class CoroutineRunner : MonoBehaviour
    {
    }
}
