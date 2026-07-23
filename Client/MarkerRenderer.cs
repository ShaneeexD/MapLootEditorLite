using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public enum GizmoMode { Translate, Rotate, Scale }
    public enum GizmoAxis { None, X, Y, Z }

    public class MarkerRenderer
    {
        private readonly MarkerManager _manager;
        private readonly GameObject _root;
        private readonly Dictionary<string, GameObject> _visuals = new Dictionary<string, GameObject>();
        private readonly Dictionary<GameObject, GameObject> _sceneObject3DVisuals = new Dictionary<GameObject, GameObject>();
        private bool _use3DSceneObjectOutlines = true;

        private readonly Color _looseColor = new Color(0f, 1f, 0f, 0.6f);
        private readonly Color _zoneColor = new Color(1f, 1f, 0f, 0.15f);
        private readonly Color _zoneWireColor = new Color(1f, 1f, 0f, 1.0f);
        private readonly Color _objectColor = new Color(0f, 0.4f, 1f, 0.6f);
        private readonly Color _objectWireColor = new Color(0.4f, 0.8f, 1f, 1.0f);
        private readonly Color _wttZoneColor = new Color(0.2f, 0.8f, 0.2f, 0.25f);
        private readonly Color _wttZoneWireColor = new Color(0.2f, 0.8f, 0.2f, 1.0f);
        private readonly Color _wttStaticColor = new Color(0.8f, 0.4f, 0.2f, 0.25f);
        private readonly Color _wttStaticWireColor = new Color(0.8f, 0.4f, 0.2f, 1.0f);
        private readonly Color _interactiveColor = new Color(1f, 0.5f, 0f, 0.25f);
        private readonly Color _interactiveWireColor = new Color(1f, 0.5f, 0f, 1.0f);
        private readonly Color _extractZoneColor = new Color(0.2f, 1f, 0.4f, 0.25f);
        private readonly Color _extractZoneWireColor = new Color(0.2f, 1f, 0.4f, 1.0f);
        private readonly Color _botSpawnPointColor = new Color(1f, 0.2f, 0.2f, 0.6f);
        private readonly Color _botSpawnPointWireColor = new Color(1f, 0.2f, 0.2f, 1.0f);
        private readonly Color _botSpawnZoneColor = new Color(1f, 0.2f, 0.2f, 0.15f);
        private readonly Color _botSpawnZoneWireColor = new Color(1f, 0.2f, 0.2f, 1.0f);
        private readonly Color _pmcSpawnZoneColor = new Color(0.2f, 0.4f, 1f, 0.2f);
        private readonly Color _pmcSpawnZoneWireColor = new Color(0.2f, 0.4f, 1f, 1.0f);
        private readonly Color _lightZoneColor = new Color(1f, 1f, 0.2f, 0.6f);
        private readonly Color _lightZoneWireColor = new Color(1f, 1f, 0.2f, 1.0f);
        private readonly Color _triggerZoneColor = new Color(1f, 0.2f, 1f, 0.15f);
        private readonly Color _triggerZoneWireColor = new Color(1f, 0.2f, 1f, 1.0f);
        private readonly Color _occlusionRepairColor = new Color(0.2f, 1f, 1f, 0.15f);
        private readonly Color _occlusionRepairWireColor = new Color(0.2f, 1f, 1f, 1.0f);
        private readonly Color _cutVolumeColor = new Color(1f, 0.2f, 0.2f, 0.15f);
        private readonly Color _cutVolumeWireColor = new Color(1f, 0.2f, 0.2f, 1.0f);
        private readonly Color _blockerColor = new Color(0.8f, 0.2f, 0.8f, 0.25f);
        private readonly Color _blockerWireColor = new Color(0.8f, 0.2f, 0.8f, 1.0f);
        private readonly Color _selectedColor = new Color(0.2f, 0.6f, 1f, 0.25f);
        private readonly Color _selectedWireColor = new Color(0.2f, 0.6f, 1f, 1.0f);
        private readonly Color _vanillaColor = new Color(0.8f, 0.8f, 0.8f, 0.35f);
        private readonly Color _vanillaWireColor = new Color(0.8f, 0.8f, 0.8f, 0.85f);
        private readonly Color _gizmoXColor = new Color(1f, 0.2f, 0.2f, 0.9f);
        private readonly Color _gizmoYColor = new Color(0.2f, 1f, 0.2f, 0.9f);
        private readonly Color _gizmoZColor = new Color(0.2f, 0.4f, 1f, 0.9f);

        private Material _wireMaterial;
        private Material _gizmoMaterial;
        private readonly Dictionary<string, ZoneShape> _zoneShapeCache = new Dictionary<string, ZoneShape>();
        private readonly List<SceneObjectOutline> _sceneObjectOutlines = new List<SceneObjectOutline>();

        private struct SceneObjectOutline
        {
            public GameObject go;
            public Bounds bounds;
            public bool selected;
        }

        public GizmoMode GizmoMode { get; set; } = GizmoMode.Translate;
        public bool ShowGizmo { get; set; } = true;
        public bool ShowVanillaGizmos { get; set; } = true;
        public bool ShowPackGizmos { get; set; } = true;
        public bool ShowSceneObjectOutlines { get; set; }
        public bool Use3DSceneObjectOutlines
        {
            get => _use3DSceneObjectOutlines;
            set
            {
                if (_use3DSceneObjectOutlines == value) return;
                _use3DSceneObjectOutlines = value;
                if (!_use3DSceneObjectOutlines)
                    DestroySceneObject3DVisuals();
            }
        }
        public float VanillaRenderDistance { get; set; } = 150f;
        public GizmoAxis HoveredAxis { get; private set; }
        public GizmoAxis ActiveAxis { get; set; }

        private GameObject _gizmoRoot;
        private GizmoMode _gizmoVisualMode;
        private readonly Dictionary<GizmoAxis, LineRenderer> _gizmoLines = new Dictionary<GizmoAxis, LineRenderer>();
        private readonly Dictionary<GizmoAxis, GameObject> _gizmoHandles = new Dictionary<GizmoAxis, GameObject>();
        private readonly Dictionary<GizmoAxis, LineRenderer> _gizmoRings = new Dictionary<GizmoAxis, LineRenderer>();
        private const float _gizmoSize = 0.5f;
        private const float _gizmoHandleSize = 0.08f;
        private const int _gizmoRingSegments = 32;
        private const float _gizmoScreenSize = 50f;

        public MarkerRenderer(MarkerManager manager, GameObject root)
        {
            _manager = manager;
            _root = root;
        }

        public void Rebuild()
        {
            Clear();

            if (_manager?.Data == null)
                return;

            var camera = Camera.main;
            var cameraPos = camera != null ? camera.transform.position : Vector3.zero;

            foreach (var marker in _manager.GetAllMarkersIncludingVanilla())
            {
                if (marker.isVanilla && !ShowVanillaGizmos)
                    continue;
                if (!marker.isVanilla && !ShowPackGizmos)
                    continue;
                if (marker.isVanilla && VanillaRenderDistance > 0f && camera != null)
                {
                    var distance = Vector3.Distance(cameraPos, marker.position.ToVector3());
                    if (distance > VanillaRenderDistance)
                        continue;
                }

                CreateVisual(marker);
            }
        }

        public void Update()
        {
            if (_manager?.Data == null)
                return;

            var camera = Camera.main;
            var cameraPos = camera != null ? camera.transform.position : Vector3.zero;

            foreach (var marker in _manager.GetAllMarkersIncludingVanilla())
            {
                if (marker.isVanilla && !ShowVanillaGizmos)
                {
                    if (_visuals.TryGetValue(marker.id, out GameObject existingVanillaVisual))
                    {
                        UnityEngine.Object.Destroy(existingVanillaVisual);
                        _visuals.Remove(marker.id);
                        _zoneShapeCache.Remove(marker.id);
                    }
                    continue;
                }

                if (!marker.isVanilla && !ShowPackGizmos)
                {
                    if (_visuals.TryGetValue(marker.id, out GameObject existingPackVisual))
                    {
                        UnityEngine.Object.Destroy(existingPackVisual);
                        _visuals.Remove(marker.id);
                        _zoneShapeCache.Remove(marker.id);
                    }
                    continue;
                }

                if (marker.isVanilla && VanillaRenderDistance > 0f && camera != null)
                {
                    var distance = Vector3.Distance(cameraPos, marker.position.ToVector3());
                    if (distance > VanillaRenderDistance)
                    {
                        if (_visuals.TryGetValue(marker.id, out GameObject farVisual))
                        {
                            if (farVisual.activeSelf)
                                farVisual.SetActive(false);
                        }
                        continue;
                    }
                }

                if (marker is LootZone currentZone && _visuals.TryGetValue(marker.id, out GameObject existingVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedShape) && cachedShape != currentZone.shape)
                    {
                        UnityEngine.Object.Destroy(existingVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is ExtractZone currentEz && _visuals.TryGetValue(marker.id, out GameObject existingEzVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedShape) && cachedShape != currentEz.shape)
                    {
                        UnityEngine.Object.Destroy(existingEzVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is BotSpawnZone currentBz && _visuals.TryGetValue(marker.id, out GameObject existingBzVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedBzShape) && cachedBzShape != currentBz.shape)
                    {
                        UnityEngine.Object.Destroy(existingBzVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is PmcSpawnZone currentPz && _visuals.TryGetValue(marker.id, out GameObject existingPzVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedPzShape) && cachedPzShape != currentPz.shape)
                    {
                        UnityEngine.Object.Destroy(existingPzVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is TriggerZone currentTz && _visuals.TryGetValue(marker.id, out GameObject existingTzVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedTzShape) && cachedTzShape != currentTz.shape)
                    {
                        UnityEngine.Object.Destroy(existingTzVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is OcclusionRepairVolume currentOrv && _visuals.TryGetValue(marker.id, out GameObject existingOrvVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedOrvShape) && cachedOrvShape != currentOrv.shape)
                    {
                        UnityEngine.Object.Destroy(existingOrvVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is CutVolume currentCv && _visuals.TryGetValue(marker.id, out GameObject existingCvVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedCvShape) && cachedCvShape != currentCv.shape)
                    {
                        UnityEngine.Object.Destroy(existingCvVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is Blocker currentBlocker && _visuals.TryGetValue(marker.id, out GameObject existingBlockerVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedBlockerShape) && cachedBlockerShape != currentBlocker.shape)
                    {
                        UnityEngine.Object.Destroy(existingBlockerVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (!_visuals.TryGetValue(marker.id, out GameObject visual))
                {
                    visual = CreateVisual(marker);
                }

                if (visual != null)
                {
                    if (!visual.activeSelf)
                        visual.SetActive(true);

                    visual.transform.position = marker.position.ToVector3();
                    visual.transform.rotation = marker.rotation.ToQuaternion();

                    if (marker is LootZone zone)
                    {
                        ApplyZoneScale(visual, zone);
                        _zoneShapeCache[marker.id] = zone.shape;
                    }
                    else if (marker is ExtractZone ez)
                    {
                        ApplyZoneScale(visual, ez);
                        _zoneShapeCache[marker.id] = ez.shape;
                    }
                    else if (marker is StaticObject so)
                    {
                        visual.transform.localScale = so.scale.ToVector3();
                    }
                    else if (marker is WTTQuestZone qz)
                    {
                        visual.transform.localScale = qz.scale.ToVector3();
                    }
                    else if (marker is WTTStaticObject wso)
                    {
                        visual.transform.localScale = wso.scale.ToVector3();
                    }
                    else if (marker is InteractiveObject io)
                    {
                        visual.transform.localScale = io.scale.ToVector3();
                    }
                    else if (marker is ExtractZone extractZone)
                    {
                        ApplyZoneScale(visual, extractZone);
                    }
                    else if (marker is BotSpawnZone bz)
                    {
                        ApplyZoneScale(visual, bz);
                        _zoneShapeCache[marker.id] = bz.shape;
                    }
                    else if (marker is PmcSpawnZone pz)
                    {
                        ApplyZoneScale(visual, pz);
                        _zoneShapeCache[marker.id] = pz.shape;
                    }
                    else if (marker is LightZone lz)
                    {
                        ApplyLightZoneVisual(visual, lz);
                    }
                    else if (marker is TriggerZone tz)
                    {
                        ApplyZoneScale(visual, tz);
                        _zoneShapeCache[marker.id] = tz.shape;
                    }
                    else if (marker is OcclusionRepairVolume orv)
                    {
                        ApplyZoneScale(visual, orv);
                        _zoneShapeCache[marker.id] = orv.shape;
                    }
                    else if (marker is CutVolume cv)
                    {
                        ApplyZoneScale(visual, cv);
                        _zoneShapeCache[marker.id] = cv.shape;
                    }
                    else if (marker is Blocker b)
                    {
                        ApplyZoneScale(visual, b);
                        _zoneShapeCache[marker.id] = b.shape;
                    }

                    bool isSelected = _manager.IsSelected(marker);
                    ApplyColor(visual, marker, isSelected);
                }
            }

            // Remove visuals for deleted markers
            var ids = new List<string>(_visuals.Keys);
            foreach (var id in ids)
            {
                if (_manager.FindById(id) == null)
                {
                    UnityEngine.Object.Destroy(_visuals[id]);
                    _visuals.Remove(id);
                    _zoneShapeCache.Remove(id);
                }
            }

            UpdateGizmo();
        }

        public void DrawLabels(bool editorOpen)
        {
            if (!editorOpen || _manager?.Data == null)
                return;

            var camera = Camera.main;
            if (camera == null)
                return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            foreach (var marker in _manager.GetAllMarkersIncludingVanilla())
            {
                if (marker.isVanilla && !ShowVanillaGizmos)
                    continue;
                if (!marker.isVanilla && !ShowPackGizmos)
                    continue;
                if (marker.isVanilla && VanillaRenderDistance > 0f && camera != null)
                {
                    var distance = Vector3.Distance(camera.transform.position, marker.position.ToVector3());
                    if (distance > VanillaRenderDistance)
                        continue;
                }

                var screenPos = camera.WorldToScreenPoint(marker.position.ToVector3());
                if (screenPos.z <= 0)
                    continue;
                if (CustomEditorUI.IsScreenPointOverEditorUI(new Vector2(screenPos.x, screenPos.y + 20f)))
                    continue;

                var rect = new Rect(screenPos.x - 50f, Screen.height - screenPos.y - 30f, 100f, 20f);
                GUI.Label(rect, GetMarkerLabel(marker), style);
            }
        }

        private static string GetMarkerLabel(MarkerBase marker)
        {
            if (!marker.isVanilla)
                return marker.name;

            if (marker is LooseLootSpawn spawn && spawn.items != null && spawn.items.Count > 0)
                return ItemNameResolver.GetNameOrId(spawn.items[0].template ?? marker.name);

            if (marker is InteractiveObject obj)
                return ItemNameResolver.GetNameOrId(obj.containerTemplate ?? marker.name);

            return marker.name;
        }

        public void Clear()
        {
            foreach (var kvp in _visuals)
            {
                if (kvp.Value != null)
                    UnityEngine.Object.Destroy(kvp.Value);
            }
            _visuals.Clear();
            _zoneShapeCache.Clear();
            _sceneObjectOutlines.Clear();
            DestroySceneObject3DVisuals();
        }

        public void SetSceneObjects(List<GameObject> objects, GameObject selected)
        {
            _sceneObjectOutlines.Clear();
            DestroySceneObject3DVisuals();
            if (objects == null)
                return;

            foreach (var go in objects)
            {
                if (go == null)
                    continue;

                var bounds = CalculateBounds(go);
                if (bounds.size.sqrMagnitude < 0.0001f)
                    continue;

                _sceneObjectOutlines.Add(new SceneObjectOutline
                {
                    go = go,
                    bounds = bounds,
                    selected = go == selected
                });
            }
        }

        public void SetSelectedSceneObject(GameObject selected)
        {
            if (_sceneObjectOutlines == null)
                return;

            for (int i = 0; i < _sceneObjectOutlines.Count; i++)
            {
                var e = _sceneObjectOutlines[i];
                e.selected = e.go == selected;
                _sceneObjectOutlines[i] = e;
            }
        }

        public GameObject PickSceneObject(Ray ray, float maxDistance, Func<GameObject, bool> predicate)
        {
            if (_sceneObjectOutlines == null || _sceneObjectOutlines.Count == 0)
                return null;

            GameObject closest = null;
            float closestDist = float.MaxValue;

            foreach (var entry in _sceneObjectOutlines)
            {
                if (entry.go == null)
                    continue;
                if (predicate != null && !predicate(entry.go))
                    continue;
                if (entry.bounds.IntersectRay(ray, out float dist) && dist <= maxDistance && dist < closestDist)
                {
                    closestDist = dist;
                    closest = entry.go;
                }
            }

            return closest;
        }

        private static Bounds CalculateBounds(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (colliders.Length == 0 && renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.zero);

            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            bool has = false;
            foreach (var c in colliders)
            {
                if (c == null)
                    continue;
                if (!has)
                {
                    b = c.bounds;
                    has = true;
                }
                else
                {
                    b.Encapsulate(c.bounds);
                }
            }

            if (has)
                return b;

            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                if (!has)
                {
                    b = r.bounds;
                    has = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            return b;
        }

        public void DrawSceneObjectOutlines(bool editorOpen)
        {
            if (Use3DSceneObjectOutlines || !editorOpen || _sceneObjectOutlines == null || _sceneObjectOutlines.Count == 0)
                return;

            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;

            var camera = Camera.main;
            if (camera == null)
                return;

            var cameraPos = camera.transform.position;
            var oldColor = GUI.color;
            const float thickness = 1f;
            var whiteTexture = Texture2D.whiteTexture;

            foreach (var entry in _sceneObjectOutlines)
            {
                if (entry.go == null)
                    continue;

                if (!entry.selected && !ShowSceneObjectOutlines)
                    continue;

                if (VanillaRenderDistance > 0f && Vector3.Distance(cameraPos, entry.bounds.center) > VanillaRenderDistance)
                    continue;

                var bmin = entry.bounds.min;
                var bmax = entry.bounds.max;
                var corners = new Vector3[]
                {
                    new Vector3(bmin.x, bmin.y, bmin.z),
                    new Vector3(bmin.x, bmin.y, bmax.z),
                    new Vector3(bmin.x, bmax.y, bmin.z),
                    new Vector3(bmin.x, bmax.y, bmax.z),
                    new Vector3(bmax.x, bmin.y, bmin.z),
                    new Vector3(bmax.x, bmin.y, bmax.z),
                    new Vector3(bmax.x, bmax.y, bmin.z),
                    new Vector3(bmax.x, bmax.y, bmax.z)
                };

                Vector2? minS = null;
                Vector2? maxS = null;
                bool valid = true;
                foreach (var corner in corners)
                {
                    var s = camera.WorldToScreenPoint(corner);
                    if (s.z <= 0f)
                    {
                        valid = false;
                        break;
                    }
                    var screen = new Vector2(s.x, Screen.height - s.y);
                    if (minS == null)
                    {
                        minS = screen;
                        maxS = screen;
                    }
                    else
                    {
                        minS = Vector2.Min(minS.Value, screen);
                        maxS = Vector2.Max(maxS.Value, screen);
                    }
                }

                if (!valid || !minS.HasValue || !maxS.HasValue)
                    continue;

                var min = minS.Value;
                var max = maxS.Value;
                if (CustomEditorUI.IsScreenPointOverEditorUI(new Vector2((min.x + max.x) * 0.5f, Screen.height - (min.y + max.y) * 0.5f)))
                    continue;

                GUI.color = entry.selected ? new Color(1f, 0.25f, 0.25f, 0.9f) : new Color(0.25f, 0.9f, 0.25f, 0.9f);

                GUI.DrawTexture(new Rect(min.x, min.y, max.x - min.x, thickness), whiteTexture, ScaleMode.StretchToFill, true, 1f);
                GUI.DrawTexture(new Rect(min.x, max.y, max.x - min.x, thickness), whiteTexture, ScaleMode.StretchToFill, true, 1f);
                GUI.DrawTexture(new Rect(min.x, min.y, thickness, max.y - min.y), whiteTexture, ScaleMode.StretchToFill, true, 1f);
                GUI.DrawTexture(new Rect(max.x, min.y, thickness, max.y - min.y), whiteTexture, ScaleMode.StretchToFill, true, 1f);
            }

            GUI.color = oldColor;
        }

        public void Update3DSceneObjectOutlines(Camera camera)
        {
            if (!Use3DSceneObjectOutlines || camera == null || _sceneObjectOutlines == null)
                return;

            var cameraPos = camera.transform.position;
            var wanted = new HashSet<GameObject>();

            foreach (var entry in _sceneObjectOutlines)
            {
                if (entry.go == null)
                    continue;

                if (!entry.selected && !ShowSceneObjectOutlines)
                    continue;

                if (VanillaRenderDistance > 0f && Vector3.Distance(cameraPos, entry.bounds.center) > VanillaRenderDistance)
                    continue;

                wanted.Add(entry.go);

                if (!_sceneObject3DVisuals.TryGetValue(entry.go, out var visual) || visual == null)
                {
                    visual = CreateSceneObject3DVisual(entry);
                    _sceneObject3DVisuals[entry.go] = visual;
                }

                UpdateSceneObject3DVisual(visual, entry);
            }

            var toRemove = new List<GameObject>();
            foreach (var kvp in _sceneObject3DVisuals)
            {
                if (kvp.Value == null || !wanted.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }

            foreach (var key in toRemove)
            {
                if (_sceneObject3DVisuals.TryGetValue(key, out var visual) && visual != null)
                    UnityEngine.Object.Destroy(visual);
                _sceneObject3DVisuals.Remove(key);
            }
        }

        private GameObject CreateSceneObject3DVisual(SceneObjectOutline entry)
        {
            var go = new GameObject($"MLE_SceneOutline_{entry.go.name}");
            go.transform.SetParent(_root.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            DrawWireBox(lr, 0.5f);
            SetLayerRecursive(go, 2);
            return go;
        }

        private void UpdateSceneObject3DVisual(GameObject visual, SceneObjectOutline entry)
        {
            visual.SetActive(true);
            visual.transform.position = entry.bounds.center;
            visual.transform.rotation = Quaternion.identity;
            visual.transform.localScale = entry.bounds.size;

            var color = entry.selected ? new Color(1f, 0.25f, 0.25f, 0.9f) : new Color(0.25f, 0.9f, 0.25f, 0.9f);
            var lr = visual.GetComponent<LineRenderer>();
            if (lr != null)
            {
                lr.startColor = color;
                lr.endColor = color;
            }
        }

        private void DestroySceneObject3DVisuals()
        {
            foreach (var kvp in _sceneObject3DVisuals)
            {
                if (kvp.Value != null)
                    UnityEngine.Object.Destroy(kvp.Value);
            }
            _sceneObject3DVisuals.Clear();
        }

        private GameObject CreateVisual(MarkerBase marker)
        {
            GameObject visual;
            switch (marker)
            {
                case LooseLootSpawn _:
                    visual = CreatePrimitiveVisual(PrimitiveType.Sphere, 0.15f);
                    break;
                case LootZone zone:
                    visual = CreateZoneVisual(zone);
                    break;
                case StaticObject _:
                    visual = CreateStaticObjectVisual();
                    break;
                case WTTQuestZone _:
                    visual = CreateWTTQuestZoneVisual();
                    break;
                case WTTStaticObject _:
                    visual = CreateWTTStaticObjectVisual();
                    break;
                case InteractiveObject _:
                    visual = CreateInteractiveObjectVisual();
                    break;
                case ExtractZone ez:
                    visual = CreateExtractZoneVisual(ez);
                    break;
                case BotSpawnPoint bp:
                    visual = CreateBotSpawnPointVisual();
                    break;
                case BotSpawnZone bz:
                    visual = CreateBotSpawnZoneVisual(bz);
                    break;
                case PmcSpawnZone pz:
                    visual = CreatePmcSpawnZoneVisual(pz);
                    break;
                case LightZone lz:
                    visual = CreateLightZoneVisual(lz);
                    break;
                case TriggerZone tz:
                    visual = CreateTriggerZoneVisual(tz);
                    break;
                case OcclusionRepairVolume orv:
                    visual = CreateOcclusionRepairVisual(orv);
                    break;
                case CutVolume cv:
                    visual = CreateCutVolumeVisual(cv);
                    break;
                case Blocker b:
                    visual = CreateBlockerVisual(b);
                    break;
                default:
                    return null;
            }

            visual.name = $"MLE_Gizmo_{marker.id}";
            visual.transform.SetParent(_root.transform, false);
            visual.transform.position = marker.position.ToVector3();
            visual.transform.rotation = marker.rotation.ToQuaternion();
            SetLayerRecursive(visual, 2);

            var visualTag = visual.GetComponent<MarkerVisual>() ?? visual.AddComponent<MarkerVisual>();
            visualTag.markerId = marker.id;

            _visuals[marker.id] = visual;
            ApplyColor(visual, marker, false);
            return visual;
        }

        private GameObject CreatePrimitiveVisual(PrimitiveType type, float baseScale)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;
            go.transform.localScale = Vector3.one * baseScale;
            return go;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private GameObject CreateZoneVisual(LootZone zone)
        {
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    return CreateBoxVisual();
                case ZoneShape.Cylinder:
                    return CreateCylinderVisual();
                case ZoneShape.Capsule:
                    return CreateCapsuleVisual();
                default:
                    return CreateSphereVisual();
            }
        }

        private GameObject CreateSphereVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Sphere, 1f);
            var wire = new GameObject("wire_sphere");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _zoneWireColor;
            lr.endColor = _zoneWireColor;

            DrawWireSphere(lr, 0.5f);
            return go;
        }

        private GameObject CreateBoxVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Cube, 1f);
            var wire = new GameObject("wire_box");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _zoneWireColor;
            lr.endColor = _zoneWireColor;

            DrawWireBox(lr, 0.5f);
            return go;
        }

        private GameObject CreateStaticObjectVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Cube, 0.5f);
            var wire = new GameObject("wire_box");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _objectWireColor;
            lr.endColor = _objectWireColor;

            DrawWireBox(lr, 0.5f);
            return go;
        }

        private GameObject CreateInteractiveObjectVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Cube, 0.5f);
            var wire = new GameObject("wire_box");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _interactiveWireColor;
            lr.endColor = _interactiveWireColor;

            DrawWireBox(lr, 0.5f);
            return go;
        }

        private GameObject CreateWTTQuestZoneVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Cube, 1f);
            var wire = new GameObject("wire_box");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _wttZoneWireColor;
            lr.endColor = _wttZoneWireColor;

            DrawWireBox(lr, 0.5f);
            return go;
        }

        private GameObject CreateWTTStaticObjectVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Cube, 0.5f);
            var wire = new GameObject("wire_box");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _wttStaticWireColor;
            lr.endColor = _wttStaticWireColor;

            DrawWireBox(lr, 0.5f);
            return go;
        }

        private GameObject CreateCylinderVisual()
        {
            return CreatePrimitiveVisual(PrimitiveType.Cylinder, 1f);
        }

        private GameObject CreateCapsuleVisual()
        {
            return CreatePrimitiveVisual(PrimitiveType.Capsule, 1f);
        }

        private GameObject CreateExtractZoneVisual(ExtractZone ez)
        {
            return CreateZoneVisualWithColor(ez.shape, _extractZoneWireColor);
        }

        private GameObject CreateBotSpawnZoneVisual(BotSpawnZone bz)
        {
            return CreateZoneVisualWithColor(bz.shape, _botSpawnZoneWireColor);
        }

        private GameObject CreatePmcSpawnZoneVisual(PmcSpawnZone pz)
        {
            return CreateZoneVisualWithColor(pz.shape, _pmcSpawnZoneWireColor);
        }

        private GameObject CreateTriggerZoneVisual(TriggerZone tz)
        {
            return CreateZoneVisualWithColor(tz.shape, _triggerZoneWireColor);
        }

        private GameObject CreateOcclusionRepairVisual(OcclusionRepairVolume orv)
        {
            return CreateZoneVisualWithColor(orv.shape, _occlusionRepairWireColor);
        }

        private GameObject CreateCutVolumeVisual(CutVolume cv)
        {
            return CreateZoneVisualWithColor(cv.shape, _cutVolumeWireColor);
        }

        private GameObject CreateBlockerVisual(Blocker blocker)
        {
            return CreateZoneVisualWithColor(blocker.shape, _blockerWireColor);
        }

        private GameObject CreateZoneVisualWithColor(ZoneShape shape, Color wireColor)
        {
            GameObject visual;
            switch (shape)
            {
                case ZoneShape.Box:
                    visual = CreateBoxVisual();
                    break;
                case ZoneShape.Cylinder:
                    visual = CreateCylinderVisual();
                    break;
                case ZoneShape.Capsule:
                    visual = CreateCapsuleVisual();
                    break;
                default:
                    visual = CreateSphereVisual();
                    break;
            }

            var wire = visual.transform.Find("wire_sphere") ?? visual.transform.Find("wire_box");
            if (wire != null)
            {
                var lr = wire.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.startColor = wireColor;
                    lr.endColor = wireColor;
                }
            }
            return visual;
        }

        private GameObject CreateBotSpawnPointVisual()
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Capsule, 0.3f);
            var wire = new GameObject("wire_sphere");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _botSpawnPointWireColor;
            lr.endColor = _botSpawnPointWireColor;

            DrawWireSphere(lr, 0.5f);
            return go;
        }

        private GameObject CreateLightZoneVisual(LightZone lz)
        {
            var go = CreatePrimitiveVisual(PrimitiveType.Sphere, 0.5f);
            var wire = new GameObject("wire_sphere");
            wire.transform.SetParent(go.transform, false);
            wire.transform.localScale = Vector3.one;
            wire.transform.localPosition = Vector3.zero;

            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _lightZoneWireColor;
            lr.endColor = _lightZoneWireColor;

            DrawWireSphere(lr, 0.5f);

            var dir = new GameObject("LightDirection");
            dir.transform.SetParent(go.transform, false);
            dir.transform.localScale = Vector3.one;
            dir.transform.localPosition = Vector3.zero;
            var dirLr = dir.AddComponent<LineRenderer>();
            dirLr.useWorldSpace = false;
            dirLr.material = GetWireMaterial();
            dirLr.startColor = _lightZoneWireColor;
            dirLr.endColor = _lightZoneWireColor;
            dirLr.positionCount = 0;
            DrawLightDirection(dirLr, lz);

            return go;
        }

        private void ApplyLightZoneVisual(GameObject visual, LightZone lz)
        {
            visual.transform.localScale = Vector3.one * Mathf.Max(0.1f, lz.range * 0.1f);
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                var c = lz.color.ToColor();
                c.a = 0.6f;
                renderer.material.color = c;
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", c * 2f);
            }

            var dirLr = visual.transform.Find("LightDirection")?.GetComponent<LineRenderer>();
            if (dirLr != null)
                DrawLightDirection(dirLr, lz);
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
            lr.startWidth = 0.04f;
            lr.endWidth = 0.04f;
        }

        private Material GetWireMaterial()
        {
            if (_wireMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
                _wireMaterial = new Material(shader);
            }
            return _wireMaterial;
        }

        private Material GetGizmoMaterial()
        {
            if (_gizmoMaterial == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                _gizmoMaterial = new Material(shader);
                _gizmoMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                _gizmoMaterial.SetInt("_ZWrite", 0);
                _gizmoMaterial.renderQueue = 5000;
            }
            return _gizmoMaterial;
        }

        private void DrawWireSphere(LineRenderer lr, float radius)
        {
            const int segments = 48;
            const int circles = 3;
            var positions = new List<Vector3>(segments * circles);

            for (int c = 0; c < circles; c++)
            {
                Vector3 axisA, axisB;
                if (c == 0) { axisA = Vector3.right; axisB = Vector3.up; }
                else if (c == 1) { axisA = Vector3.right; axisB = Vector3.forward; }
                else { axisA = Vector3.up; axisB = Vector3.forward; }

                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    var point = axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle);
                    positions.Add(point * radius);
                }
            }

            lr.positionCount = positions.Count;
            lr.SetPositions(positions.ToArray());
        }

        private void DrawWireBox(LineRenderer lr, float halfSize)
        {
            var corners = new Vector3[]
            {
                new Vector3(-halfSize, -halfSize, -halfSize),
                new Vector3(halfSize, -halfSize, -halfSize),
                new Vector3(halfSize, halfSize, -halfSize),
                new Vector3(-halfSize, halfSize, -halfSize),
                new Vector3(-halfSize, -halfSize, halfSize),
                new Vector3(halfSize, -halfSize, halfSize),
                new Vector3(halfSize, halfSize, halfSize),
                new Vector3(-halfSize, halfSize, halfSize),
            };

            var indices = new int[]
            {
                0, 1, 1, 2, 2, 3, 3, 0,
                4, 5, 5, 6, 6, 7, 7, 4,
                0, 4, 1, 5, 2, 6, 3, 7
            };

            var positions = new List<Vector3>();
            foreach (var index in indices)
                positions.Add(corners[index]);

            lr.positionCount = positions.Count;
            lr.SetPositions(positions.ToArray());
        }

        private void ApplyZoneScale(GameObject visual, LootZone zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * zone.radius * 2f * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, ExtractZone zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * zone.radius * 2f * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, BotSpawnZone zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * zone.radius * 2f * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, PmcSpawnZone zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(zone.radius * 2f * scale.x, scale.y, zone.radius * 2f * scale.x);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * zone.radius * 2f * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, TriggerZone zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, OcclusionRepairVolume zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, CutVolume zone)
        {
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * scale.x;
                    break;
            }
        }

        private void ApplyZoneScale(GameObject visual, Blocker blocker)
        {
            var scale = blocker.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (blocker.shape)
            {
                case ZoneShape.Box:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Cylinder:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                case ZoneShape.Capsule:
                    visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
                    break;
                default:
                    visual.transform.localScale = Vector3.one * scale.x;
                    break;
            }
        }

        private void ApplyColor(GameObject visual, MarkerBase marker, bool selected)
        {
            var renderer = visual.GetComponent<Renderer>();
            if (renderer == null)
                return;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            var mat = renderer.material;
            if (mat.shader != shader)
            {
                mat = new Material(shader);
                renderer.material = mat;
            }

            Color color;
            if (selected)
            {
                color = _selectedColor;
            }
            else if (marker.isVanilla)
            {
                color = _vanillaColor;
            }
            else if (marker is LooseLootSpawn)
            {
                color = _looseColor;
            }
            else if (marker is LootZone)
            {
                color = _zoneColor;
            }
            else if (marker is WTTQuestZone)
            {
                color = _wttZoneColor;
            }
            else if (marker is WTTStaticObject)
            {
                color = _wttStaticColor;
            }
            else if (marker is InteractiveObject)
            {
                color = _interactiveColor;
            }
            else if (marker is ExtractZone)
            {
                color = _extractZoneColor;
            }
            else if (marker is BotSpawnPoint)
            {
                color = _botSpawnPointColor;
            }
            else if (marker is BotSpawnZone)
            {
                color = _botSpawnZoneColor;
            }
            else if (marker is PmcSpawnZone)
            {
                color = _pmcSpawnZoneColor;
            }
            else if (marker is LightZone)
            {
                color = _lightZoneColor;
            }
            else if (marker is TriggerZone)
            {
                color = _triggerZoneColor;
            }
            else if (marker is OcclusionRepairVolume)
            {
                color = _occlusionRepairColor;
            }
            else if (marker is CutVolume)
            {
                color = _cutVolumeColor;
            }
            else if (marker is Blocker)
            {
                color = _blockerColor;
            }
            else
            {
                color = _objectColor;
            }

            mat.color = color;

            var wire = visual.transform.Find("wire_sphere") ?? visual.transform.Find("wire_box");
            if (wire != null)
            {
                var lr = wire.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    Color wireColor;
                    if (selected)
                        wireColor = _selectedWireColor;
                    else if (marker.isVanilla)
                        wireColor = _vanillaWireColor;
                    else if (marker is StaticObject)
                        wireColor = _objectWireColor;
                    else if (marker is WTTQuestZone)
                        wireColor = _wttZoneWireColor;
                    else if (marker is WTTStaticObject)
                        wireColor = _wttStaticWireColor;
                    else if (marker is InteractiveObject)
                        wireColor = _interactiveWireColor;
                    else if (marker is ExtractZone)
                        wireColor = _extractZoneWireColor;
                    else if (marker is BotSpawnPoint)
                        wireColor = _botSpawnPointWireColor;
                    else if (marker is BotSpawnZone)
                        wireColor = _botSpawnZoneWireColor;
                    else if (marker is PmcSpawnZone)
                        wireColor = _pmcSpawnZoneWireColor;
                    else if (marker is LightZone)
                        wireColor = _lightZoneWireColor;
                    else if (marker is TriggerZone)
                        wireColor = _triggerZoneWireColor;
                    else if (marker is OcclusionRepairVolume)
                        wireColor = _occlusionRepairWireColor;
                    else if (marker is CutVolume)
                        wireColor = _cutVolumeWireColor;
                    else if (marker is Blocker)
                        wireColor = _blockerWireColor;
                    else
                        wireColor = _zoneWireColor;

                    lr.startColor = wireColor;
                    lr.endColor = wireColor;
                }
            }
        }

        public MarkerBase PickFromScreenCenter(Camera camera, Vector2 screenCenter, float maxDistance)
        {
            return PickFromScreenPosition(camera, screenCenter, maxDistance);
        }

        public MarkerBase PickFromScreenPosition(Camera camera, Vector2 screenPos, float maxDistance)
        {
            if (_manager?.Data == null || camera == null)
                return null;

            // First try raycasting against the marker visuals (whole box/sphere/wireframe).
            var ray = camera.ScreenPointToRay(screenPos);
            const int markerLayerMask = 1 << 2;
            var hits = Physics.RaycastAll(ray, maxDistance, markerLayerMask, QueryTriggerInteraction.Collide);
            MarkerBase best = null;
            float bestDist = float.MaxValue;
            foreach (var hit in hits)
            {
                var visual = hit.collider != null ? hit.collider.GetComponentInParent<MarkerVisual>() : null;
                if (visual == null)
                    continue;
                var marker = _manager.FindById(visual.markerId);
                if (marker == null)
                    continue;
                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    best = marker;
                }
            }
            if (best != null)
                return best;

            // Fallback: distance from the projected center point.
            float bestScore = float.MaxValue;
            foreach (var marker in _manager.GetAllMarkersIncludingVanilla())
            {
                var worldPos = marker.position.ToVector3();
                var projected = camera.WorldToScreenPoint(worldPos);
                if (projected.z <= 0)
                    continue;

                var dist2d = Vector2.Distance(screenPos, new Vector2(projected.x, projected.y));
                var worldDist = Vector3.Distance(camera.transform.position, worldPos);

                if (worldDist > maxDistance)
                    continue;

                if (dist2d < 40f && dist2d < bestScore)
                {
                    bestScore = dist2d;
                    best = marker;
                }
            }
            return best;
        }

        public GizmoAxis PickGizmoAxis(Camera camera, Vector2 screenPos, float maxPx)
        {
            HoveredAxis = GizmoAxis.None;
            if (_manager?.Selected == null || !ShowGizmo || camera == null)
                return GizmoAxis.None;

            var pos = GetGizmoPosition();
            var rot = GetGizmoRotation();
            var scale = GetGizmoDynamicScale(camera);
            GizmoAxis best = GizmoAxis.None;
            float bestDist = maxPx;

            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                var dir = GetGizmoAxisDirection(axis, rot);
                Vector3 targetPoint;

                if (GizmoMode == GizmoMode.Rotate)
                {
                    // In rotation mode handles sit on the ring of the same color.
                    Vector3 tangent;
                    switch (axis)
                    {
                        case GizmoAxis.X: tangent = GetGizmoAxisDirection(GizmoAxis.Y, rot); break;
                        case GizmoAxis.Y: tangent = GetGizmoAxisDirection(GizmoAxis.Z, rot); break;
                        case GizmoAxis.Z: tangent = GetGizmoAxisDirection(GizmoAxis.X, rot); break;
                        default: tangent = dir; break;
                    }
                    targetPoint = pos + tangent * _gizmoSize * scale;
                }
                else
                {
                    targetPoint = pos + dir * _gizmoSize * scale;
                }

                var projected = camera.WorldToScreenPoint(targetPoint);
                if (projected.z <= 0)
                    continue;

                var dist = Vector2.Distance(screenPos, new Vector2(projected.x, projected.y));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = axis;
                }
            }

            HoveredAxis = best;
            return best;
        }

        private Vector3 GetGizmoPosition()
        {
            if (_manager?.Selected == null)
                return Vector3.zero;
            if (_manager.SelectedIds.Count > 1)
                return _manager.SelectionCenter;
            return _manager.Selected.position.ToVector3();
        }

        private Quaternion GetGizmoRotation()
        {
            if (_manager?.Selected == null)
                return Quaternion.identity;
            if (_manager.SelectedIds.Count > 1)
                return Quaternion.identity;
            return _manager.Selected.rotation.ToQuaternion();
        }

        private void UpdateGizmo()
        {
            if (_manager?.Selected == null || !ShowGizmo || _manager.Selected.isVanilla)
            {
                DestroyGizmo();
                return;
            }

            EnsureGizmo();
            var pos = GetGizmoPosition();
            var rot = GetGizmoRotation();
            var gizmoScale = GetGizmoDynamicScale(Camera.main);

            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                var dir = GetGizmoAxisDirection(axis, rot);
                var end = pos + dir * _gizmoSize * gizmoScale;
                var color = GetGizmoColor(axis);
                var isActive = ActiveAxis == axis || HoveredAxis == axis;

                var line = _gizmoLines[axis];
                if (line.material != null)
                    line.material.color = color;
                line.startColor = Color.white;
                line.endColor = Color.white;
                line.startWidth = (isActive ? 0.04f : 0.025f) * gizmoScale;
                line.endWidth = (isActive ? 0.04f : 0.025f) * gizmoScale;

                if (GizmoMode == GizmoMode.Rotate)
                {
                    line.enabled = false;
                }
                else
                {
                    line.enabled = true;
                    line.positionCount = 2;
                    line.SetPosition(0, pos);
                    line.SetPosition(1, end);
                }

                var handle = _gizmoHandles[axis];
                Vector3 handlePos;
                if (GizmoMode == GizmoMode.Rotate)
                {
                    // Place the handle on the ring of the same color.
                    Vector3 tangent;
                    switch (axis)
                    {
                        case GizmoAxis.X: tangent = GetGizmoAxisDirection(GizmoAxis.Y, rot); break;
                        case GizmoAxis.Y: tangent = GetGizmoAxisDirection(GizmoAxis.Z, rot); break;
                        case GizmoAxis.Z: tangent = GetGizmoAxisDirection(GizmoAxis.X, rot); break;
                        default: tangent = dir; break;
                    }
                    handlePos = pos + tangent * _gizmoSize * gizmoScale;
                }
                else
                {
                    handlePos = end;
                }

                handle.transform.position = handlePos;
                handle.transform.rotation = Quaternion.identity;
                var handleScale = (isActive ? _gizmoHandleSize * 1.5f : _gizmoHandleSize) * gizmoScale;
                handle.transform.localScale = Vector3.one * handleScale;

                var renderer = handle.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                    renderer.material.color = color;

                if (_gizmoRings.TryGetValue(axis, out var ring))
                {
                    if (GizmoMode == GizmoMode.Rotate)
                    {
                        ring.enabled = true;
                        if (ring.material != null)
                            ring.material.color = color;
                        ring.startColor = Color.white;
                        ring.endColor = Color.white;
                        ring.startWidth = (isActive ? 0.04f : 0.025f) * gizmoScale;
                        ring.endWidth = (isActive ? 0.04f : 0.025f) * gizmoScale;
                        DrawGizmoRing(ring, pos, dir, _gizmoSize * gizmoScale);
                    }
                    else
                    {
                        ring.enabled = false;
                    }
                }
            }
        }

        private float GetGizmoDynamicScale(Camera camera)
        {
            if (camera == null)
                return 1f;

            var position = GetGizmoPosition();
            var distance = Vector3.Distance(camera.transform.position, position);
            if (distance <= 0.001f)
                return 1f;

            var worldHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var targetWorldSize = _gizmoScreenSize / Mathf.Max(1f, camera.pixelHeight) * worldHeight;
            return Mathf.Max(targetWorldSize / _gizmoSize, 1f);
        }

        private void EnsureGizmo()
        {
            if (_gizmoRoot == null)
            {
                _gizmoRoot = new GameObject("MLE_GizmoRoot");
                _gizmoRoot.transform.SetParent(_root.transform, false);
            }

            if (_gizmoVisualMode != GizmoMode)
            {
                DestroyGizmoObjects();
                _gizmoVisualMode = GizmoMode;
            }

            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                if (!_gizmoLines.TryGetValue(axis, out var lr))
                {
                    var go = new GameObject($"MLE_GizmoLine_{axis}");
                    go.transform.SetParent(_gizmoRoot.transform, false);
                    lr = go.AddComponent<LineRenderer>();
                    lr.material = new Material(GetGizmoMaterial());
                    lr.useWorldSpace = true;
                    lr.positionCount = 2;
                    _gizmoLines[axis] = lr;
                }

                if (!_gizmoHandles.TryGetValue(axis, out var handle))
                {
                    handle = CreateGizmoHandle();
                    handle.name = $"MLE_GizmoHandle_{axis}";
                    handle.transform.SetParent(_gizmoRoot.transform, false);
                    var renderer = handle.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        renderer.material = new Material(GetGizmoMaterial());
                    _gizmoHandles[axis] = handle;
                }

                if (!_gizmoRings.TryGetValue(axis, out var ring))
                {
                    var go = new GameObject($"MLE_GizmoRing_{axis}");
                    go.transform.SetParent(_gizmoRoot.transform, false);
                    ring = go.AddComponent<LineRenderer>();
                    ring.material = new Material(GetGizmoMaterial());
                    ring.useWorldSpace = true;
                    ring.positionCount = _gizmoRingSegments + 1;
                    ring.loop = true;
                    _gizmoRings[axis] = ring;
                }
            }
        }

        private GameObject CreateGizmoHandle()
        {
            var type = GizmoMode == GizmoMode.Scale ? PrimitiveType.Cube : PrimitiveType.Sphere;
            return CreatePrimitiveVisual(type, _gizmoHandleSize);
        }

        private void DestroyGizmo()
        {
            if (_gizmoRoot != null)
            {
                UnityEngine.Object.Destroy(_gizmoRoot);
                _gizmoRoot = null;
                _gizmoLines.Clear();
                _gizmoHandles.Clear();
                _gizmoRings.Clear();
                _gizmoVisualMode = default;
            }
        }

        private void DestroyGizmoObjects()
        {
            if (_gizmoRoot == null)
                return;

            foreach (Transform child in _gizmoRoot.transform)
                UnityEngine.Object.Destroy(child.gameObject);

            _gizmoLines.Clear();
            _gizmoHandles.Clear();
            _gizmoRings.Clear();
        }

        private void DrawGizmoRing(LineRenderer lr, Vector3 center, Vector3 normal, float radius)
        {
            Vector3 axisA;
            if (Mathf.Abs(normal.y) < 0.9f)
                axisA = Vector3.Cross(normal, Vector3.up).normalized;
            else
                axisA = Vector3.Cross(normal, Vector3.forward).normalized;
            var axisB = Vector3.Cross(normal, axisA);

            for (int i = 0; i <= _gizmoRingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / _gizmoRingSegments;
                var point = center + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
                lr.SetPosition(i, point);
            }
        }

        private Vector3 GetGizmoAxisDirection(GizmoAxis axis, Quaternion localRotation)
        {
            Vector3 dir;
            switch (axis)
            {
                case GizmoAxis.X: dir = Vector3.right; break;
                case GizmoAxis.Y: dir = Vector3.up; break;
                case GizmoAxis.Z: dir = Vector3.forward; break;
                default: return Vector3.zero;
            }
            return localRotation * dir;
        }

        private Color GetGizmoColor(GizmoAxis axis)
        {
            switch (axis)
            {
                case GizmoAxis.X: return _gizmoXColor;
                case GizmoAxis.Y: return _gizmoYColor;
                case GizmoAxis.Z: return _gizmoZColor;
                default: return Color.white;
            }
        }
    }
}
