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
        private readonly Color _navMeshAreaColor = new Color(0.2f, 0.8f, 0.3f, 0.25f);
        private readonly Color _navMeshAreaWireColor = new Color(0.2f, 0.8f, 0.3f, 1.0f);
        private readonly Color _botActivityZoneColor = new Color(0.75f, 1f, 0.1f, 0.10f);
        private readonly Color _botActivityZoneWireColor = new Color(0.75f, 1f, 0.1f, 1.0f);
        private readonly Color _botActivityZoneHighlightColor = new Color(0.9f, 1f, 0.3f, 0.28f);
        private readonly Color _botActivityZoneHighlightWireColor = new Color(1f, 1f, 0.55f, 1.0f);
        private readonly Color _botActivityZoneStrongColor = new Color(1f, 0.12f, 0.12f, 0.14f);
        private readonly Color _botActivityZoneStrongWireColor = new Color(1f, 0.15f, 0.15f, 1.0f);
        private static bool IsStrongZone(BotActivityZone zone)
        {
            return zone != null && BotActivityZone.NormalizeLeashMode(zone.leashMode) == BotActivityZone.LeashModeStrong;
        }
        private readonly Color _botActivityZoneDisabledColor = new Color(0.5f, 0.5f, 0.5f, 0.08f);
        private readonly Color _botActivityZoneDisabledWireColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
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
        private readonly Dictionary<string, NavMeshAreaShape> _navMeshAreaShapeCache = new Dictionary<string, NavMeshAreaShape>();
        private readonly Dictionary<string, BotActivityZoneShape> _activityZoneShapeCache = new Dictionary<string, BotActivityZoneShape>();
        private readonly HashSet<string> _highlightedZoneIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, LineRenderer> _zoneAssociationLines = new Dictionary<string, LineRenderer>();
        private readonly Dictionary<string, PatrolVisual> _patrolVisuals = new Dictionary<string, PatrolVisual>(StringComparer.Ordinal);
        private GameObject _patrolVisualRoot;
        private string _patrolVisualZoneId;
        private readonly List<SceneObjectOutline> _sceneObjectOutlines = new List<SceneObjectOutline>();
        private readonly Dictionary<Transform, GameObject> _sceneObjectsByTransform = new Dictionary<Transform, GameObject>();

        private struct SceneObjectOutline
        {
            public GameObject go;
            public Bounds bounds;
            public Bounds rendererBounds;
            public bool hasRendererBounds;
            public bool selected;
            public bool hovered;
        }

        private sealed class PatrolVisual
        {
            public GameObject Root;
            public LineRenderer RouteLine;
            public TextMesh Label;
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
        public float ObjectRenderDistance { get; set; } = 150f;
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
                if (_manager.IsLocked(marker))
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

            _highlightedZoneIds.Clear();
            if (_manager.Data.botSpawnPoints != null)
            {
                foreach (var sp in _manager.Data.botSpawnPoints)
                {
                    if (sp != null && !string.IsNullOrEmpty(sp.botActivityZoneId) && _manager.IsSelected(sp))
                        _highlightedZoneIds.Add(sp.botActivityZoneId);
                }
            }

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

                if (_manager.IsLocked(marker))
                {
                    if (_visuals.TryGetValue(marker.id, out GameObject lockedVisual))
                    {
                        UnityEngine.Object.Destroy(lockedVisual);
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

                if (marker is NavMeshArea currentNma && _visuals.TryGetValue(marker.id, out GameObject existingNmaVisual))
                {
                    if (_navMeshAreaShapeCache.TryGetValue(marker.id, out NavMeshAreaShape cachedNmaShape) && cachedNmaShape != currentNma.shape)
                    {
                        UnityEngine.Object.Destroy(existingNmaVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (marker is BotActivityZone currentBaz && _visuals.TryGetValue(marker.id, out GameObject existingBazVisual))
                {
                    if (_activityZoneShapeCache.TryGetValue(marker.id, out BotActivityZoneShape cachedBazShape) && cachedBazShape != currentBaz.shape)
                    {
                        UnityEngine.Object.Destroy(existingBazVisual);
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
                    else if (marker is NavMeshArea nma)
                    {
                        ApplyNavMeshAreaScale(visual, nma);
                        _navMeshAreaShapeCache[marker.id] = nma.shape;
                    }
                    else if (marker is BotActivityZone baz)
                    {
                        // Ground-plane boundary: only yaw is honored so the shape stays flat and level.
                        visual.transform.rotation = Quaternion.Euler(0f, marker.rotation.y, 0f);
                        bool circle = baz.shape == BotActivityZoneShape.Circle;
                        // Cylinder primitive is 2 units tall, cube is 1.
                        var width = circle ? Mathf.Max(BotActivityZone.MinSize, baz.radius) * 2f : baz.EffectiveWidth;
                        var depth = circle ? width : baz.EffectiveDepth;
                        visual.transform.localScale = new Vector3(width, circle ? ZoneThickness * 0.5f : ZoneThickness, depth);
                        _activityZoneShapeCache[marker.id] = baz.shape;
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
                    _activityZoneShapeCache.Remove(id);
                }
            }

            UpdateZoneAssociationLines();
            UpdatePatrolVisuals();
            UpdateGizmo();
        }

        private const float ZoneThickness = 0.2f;

        // Editor-only line from a selected custom spawn point to its assigned activity zone center.
        private void UpdateZoneAssociationLines()
        {
            var active = new HashSet<string>();
            if (ShowPackGizmos && _manager?.Data?.botSpawnPoints != null)
            {
                foreach (var sp in _manager.Data.botSpawnPoints)
                {
                    if (sp == null || !_manager.IsSelected(sp))
                        continue;
                    var zone = _manager.FindActivityZone(sp.botActivityZoneId);
                    if (zone == null || _manager.IsLocked(zone))
                        continue;

                    active.Add(sp.id);
                    if (!_zoneAssociationLines.TryGetValue(sp.id, out var lr) || lr == null)
                    {
                        var go = new GameObject("MLE_ZoneAssociation");
                        go.transform.SetParent(_root.transform, false);
                        lr = go.AddComponent<LineRenderer>();
                        lr.useWorldSpace = true;
                        lr.positionCount = 2;
                        lr.startWidth = 0.05f;
                        lr.endWidth = 0.05f;
                        lr.material = GetWireMaterial();
                        lr.startColor = _botActivityZoneHighlightWireColor;
                        lr.endColor = _botActivityZoneHighlightWireColor;
                        SetLayerRecursive(go, 2);
                        _zoneAssociationLines[sp.id] = lr;
                    }

                    lr.SetPosition(0, sp.position.ToVector3());
                    lr.SetPosition(1, zone.position.ToVector3());
                }
            }

            List<string> stale = null;
            foreach (var kvp in _zoneAssociationLines)
            {
                if (!active.Contains(kvp.Key))
                    (stale ??= new List<string>()).Add(kvp.Key);
            }
            if (stale != null)
            {
                foreach (var key in stale)
                {
                    if (_zoneAssociationLines[key] != null)
                        UnityEngine.Object.Destroy(_zoneAssociationLines[key].gameObject);
                    _zoneAssociationLines.Remove(key);
                }
            }
        }

        private void UpdatePatrolVisuals()
            {
                var zone = ShowPackGizmos && _manager?.SelectedIds.Count == 1
                    ? _manager.Selected as BotActivityZone
                    : null;
                if (zone == null || !zone.patrolEnabled || zone.patrolPoints == null || zone.patrolPoints.Count == 0)
                {
                    if (_patrolVisualRoot != null)
                        _patrolVisualRoot.SetActive(false);
                    _patrolVisualZoneId = null;
                    return;
                }

                if (_patrolVisualRoot == null)
                {
                    _patrolVisualRoot = new GameObject("MLE_PatrolRouteVisuals");
                    _patrolVisualRoot.transform.SetParent(_root.transform, false);
                    SetLayerRecursive(_patrolVisualRoot, 2);
                }
                _patrolVisualZoneId = zone.zoneId;
                _patrolVisualRoot.SetActive(true);

                var active = new HashSet<string>(StringComparer.Ordinal);
                var routeStart = zone.position.ToVector3() + Vector3.up * 0.25f;
                foreach (var point in zone.patrolPoints)
                {
                    if (point == null)
                        continue;

                    var key = zone.zoneId + ":" + point.id;
                    active.Add(key);
                    if (!_patrolVisuals.TryGetValue(key, out var visual) || visual?.Root == null)
                    {
                        var pointRoot = new GameObject("MLE_PatrolPoint_" + point.id);
                        pointRoot.transform.SetParent(_patrolVisualRoot.transform, false);
                        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        sphere.name = "Point";
                        var collider = sphere.GetComponent<Collider>();
                        if (collider != null)
                            UnityEngine.Object.Destroy(collider);
                        sphere.transform.SetParent(pointRoot.transform, false);
                        sphere.transform.localScale = Vector3.one * 0.65f;
                        var renderer = sphere.GetComponent<MeshRenderer>();
                        if (renderer != null)
                            renderer.material = new Material(GetGizmoMaterial());
                        var lineObject = new GameObject("Route");
                        lineObject.transform.SetParent(pointRoot.transform, false);
                        var line = lineObject.AddComponent<LineRenderer>();
                        line.useWorldSpace = true;
                        line.positionCount = 2;
                        line.startWidth = 0.035f;
                        line.endWidth = 0.035f;
                        line.material = GetWireMaterial();
                        var labelObject = new GameObject("Label");
                        labelObject.transform.SetParent(pointRoot.transform, false);
                        labelObject.transform.localPosition = Vector3.up * 0.85f;
                        var label = labelObject.AddComponent<TextMesh>();
                        label.anchor = TextAnchor.LowerCenter;
                        label.alignment = TextAlignment.Center;
                        label.fontSize = 24;
                        label.characterSize = 0.09f;
                        visual = new PatrolVisual { Root = pointRoot, RouteLine = line, Label = label };
                        _patrolVisuals[key] = visual;
                        SetLayerRecursive(pointRoot, 2);
                    }

                    var pointPosition = point.position != null ? point.position.ToVector3() : routeStart;
                    visual.Root.transform.position = pointPosition;
                    visual.Root.SetActive(true);
                    visual.RouteLine.SetPosition(0, routeStart);
                    visual.RouteLine.SetPosition(1, pointPosition + Vector3.up * 0.2f);
                    var color = point.enabled
                        ? new Color(1f, 0.72f, 0.12f, 1f)
                        : new Color(0.55f, 0.55f, 0.55f, 0.8f);
                    visual.RouteLine.startColor = color;
                    visual.RouteLine.endColor = color;
                    visual.Label.text = string.IsNullOrWhiteSpace(point.name) ? point.id : point.name;
                    visual.Label.color = color;
                    if (Camera.main != null)
                        visual.Label.transform.rotation = Camera.main.transform.rotation;
                    var pointRenderer = visual.Root.transform.Find("Point")?.GetComponent<MeshRenderer>();
                    if (pointRenderer != null)
                        pointRenderer.material.color = color;
                }

                foreach (var key in _patrolVisuals.Keys.Where(key => !active.Contains(key)).ToList())
                {
                    if (_patrolVisuals[key]?.Root != null)
                        UnityEngine.Object.Destroy(_patrolVisuals[key].Root);
                    _patrolVisuals.Remove(key);
                }
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
                if (_manager.IsLocked(marker))
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
            _navMeshAreaShapeCache.Clear();
            _sceneObjectOutlines.Clear();
            _sceneObjectsByTransform.Clear();
            _hoveredSceneObject = null;
            if (_patrolVisualRoot != null)
                UnityEngine.Object.Destroy(_patrolVisualRoot);
            _patrolVisualRoot = null;
            _patrolVisuals.Clear();
            _patrolVisualZoneId = null;
            DestroySceneObject3DVisuals();
        }

        public void SetSceneObjects(List<GameObject> objects, GameObject selected)
        {
            _sceneObjectOutlines.Clear();
            _hoveredSceneObject = null;
            _sceneObjectsByTransform.Clear();
            DestroySceneObject3DVisuals();
            if (objects == null)
                return;

            foreach (var go in objects)
            {
                if (go == null)
                    continue;

                var hasRendererBounds = TryCalculateRendererBounds(go, out var rendererBounds);
                var bounds = hasRendererBounds ? rendererBounds : CalculateBounds(go);
                if (bounds.size.sqrMagnitude < 0.0001f)
                    continue;

                _sceneObjectOutlines.Add(new SceneObjectOutline
                {
                    go = go,
                    bounds = bounds,
                    hasRendererBounds = hasRendererBounds,
                    rendererBounds = rendererBounds,
                    selected = go == selected,
                    hovered = false
                });
                _sceneObjectsByTransform[go.transform] = go;
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

        // Selected (red) always outranks hover (cyan); everything else discovered stays green.
        private static Color SceneOutlineColor(SceneObjectOutline entry)
        {
            if (entry.selected)
                return new Color(1f, 0.25f, 0.25f, 0.9f);
            if (entry.hovered)
                return new Color(0.2f, 0.9f, 1f, 1f);
            return new Color(0.25f, 0.9f, 0.25f, 0.9f);
        }

        private GameObject _hoveredSceneObject;

        public void SetHoveredSceneObject(GameObject hovered)
        {
            if (_sceneObjectOutlines == null)
                return;
            // Called every frame; only touch the outline list when the target actually changes.
            if (_hoveredSceneObject == hovered)
                return;
            _hoveredSceneObject = hovered;

            for (int i = 0; i < _sceneObjectOutlines.Count; i++)
            {
                var e = _sceneObjectOutlines[i];
                e.hovered = hovered != null && e.go == hovered;
                _sceneObjectOutlines[i] = e;
            }
        }

        // Ray-vs-bounds candidates for visible objects, with no collider required. The cached combined bounds is only a
        // cheap first test; survivors are refined against each live renderer's own bounds so gaps inside a large
        // LOD/parent box do not count as hits. Reports the nearest renderer entry distance along the ray.
        public void CollectRendererRayCandidates(Ray ray, float maxDistance, List<SceneRayCandidate> results, List<Renderer> rendererBuffer, int maxResults)
        {
            if (_sceneObjectOutlines == null || _sceneObjectOutlines.Count == 0)
                return;

            for (int i = 0; i < _sceneObjectOutlines.Count; i++)
            {
                var entry = _sceneObjectOutlines[i];
                if (entry.go == null || !entry.hasRendererBounds)
                    continue;

                var box = entry.rendererBounds;
                // A camera sitting inside a loose foliage box would otherwise "hit" it at distance 0.
                if (box.Contains(ray.origin))
                    continue;
                if (!box.IntersectRay(ray, out var boxDistance) || boxDistance > maxDistance)
                    continue;

                var nearest = float.MaxValue;
                entry.go.GetComponentsInChildren(true, rendererBuffer);
                for (int r = 0; r < rendererBuffer.Count; r++)
                {
                    var rend = rendererBuffer[r];
                    if (rend == null || !rend.enabled || rend.forceRenderingOff || !rend.gameObject.activeInHierarchy)
                        continue;
                    var rb = rend.bounds;
                    if (rb.Contains(ray.origin))
                        continue;
                    if (rb.IntersectRay(ray, out var d) && d <= maxDistance && d < nearest)
                        nearest = d;
                }
                rendererBuffer.Clear();

                if (nearest == float.MaxValue)
                {
                    SceneRayCandidate.RejectedRefine++;
                    continue;
                }

                var size = entry.rendererBounds.size;
                results.Add(new SceneRayCandidate { go = entry.go, distance = nearest, volume = size.x * size.y * size.z });
                if (results.Count >= maxResults)
                    break;
            }
        }

        public struct SceneRayCandidate
        {
            public GameObject go;
            public float distance;
            public float volume;
            // Diagnostics only: number of box hits dropped because no live renderer's own bounds was hit.
            public static int RejectedRefine;
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

        public GameObject FindSceneObjectAncestor(Transform hitTransform)
        {
            for (var current = hitTransform; current != null; current = current.parent)
            {
                if (!_sceneObjectsByTransform.TryGetValue(current, out var candidate))
                    continue;

                return candidate;
            }

            return null;
        }

        public GameObject PickSceneObjectNearScreen(Camera camera, Vector2 screenPosition, Ray ray, float maxDistance, float maxPixels, Func<GameObject, bool> predicate, out float pickedDepth, out bool ambiguous)
        {
            pickedDepth = float.MaxValue;
            ambiguous = false;
            if (camera == null || _sceneObjectOutlines.Count == 0)
                return null;

            GameObject best = null;
            float bestScreenDistance = maxPixels;
            foreach (var entry in _sceneObjectOutlines)
            {
                if (entry.go == null || !entry.hasRendererBounds)
                    continue;

                var bounds = entry.rendererBounds;
                var projectedCenter = camera.WorldToScreenPoint(bounds.center);
                if (projectedCenter.z <= 0f)
                    continue;

                var pixelsPerWorldUnit = camera.orthographic
                    ? camera.pixelHeight / Mathf.Max(0.001f, 2f * camera.orthographicSize)
                    : camera.pixelHeight / Mathf.Max(0.001f, 2f * projectedCenter.z * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad));
                var coarseRadius = bounds.extents.magnitude * pixelsPerWorldUnit + maxPixels;
                if (Vector2.Distance(screenPosition, new Vector2(projectedCenter.x, projectedCenter.y)) > coarseRadius)
                    continue;

                var nearestDepth = Vector3.Dot(bounds.center - ray.origin, ray.direction)
                    - Mathf.Abs(ray.direction.x) * bounds.extents.x
                    - Mathf.Abs(ray.direction.y) * bounds.extents.y
                    - Mathf.Abs(ray.direction.z) * bounds.extents.z;
                if (nearestDepth <= 0f || nearestDepth > maxDistance)
                    continue;

                var minX = float.MaxValue;
                var minY = float.MaxValue;
                var maxX = float.MinValue;
                var maxY = float.MinValue;
                var visible = true;
                for (int corner = 0; corner < 8; corner++)
                {
                    var worldPoint = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    var projected = camera.WorldToScreenPoint(worldPoint);
                    if (projected.z <= 0f)
                    {
                        visible = false;
                        break;
                    }

                    minX = Mathf.Min(minX, projected.x);
                    minY = Mathf.Min(minY, projected.y);
                    maxX = Mathf.Max(maxX, projected.x);
                    maxY = Mathf.Max(maxY, projected.y);
                }
                if (!visible)
                    continue;

                var dx = Mathf.Max(minX - screenPosition.x, 0f, screenPosition.x - maxX);
                var dy = Mathf.Max(minY - screenPosition.y, 0f, screenPosition.y - maxY);
                var screenDistance = Mathf.Sqrt(dx * dx + dy * dy);
                if (screenDistance > bestScreenDistance)
                    continue;
                if (predicate != null && !predicate(entry.go))
                    continue;

                if (screenDistance < bestScreenDistance - 2f)
                {
                    best = entry.go;
                    bestScreenDistance = screenDistance;
                    pickedDepth = nearestDepth;
                    ambiguous = false;
                }
                else if (best != null && Mathf.Abs(screenDistance - bestScreenDistance) <= 2f &&
                         !entry.go.transform.IsChildOf(best.transform) &&
                         !best.transform.IsChildOf(entry.go.transform))
                {
                    ambiguous = true;
                }
            }

            return ambiguous ? null : best;
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

        private static bool TryCalculateRendererBounds(GameObject go, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (go == null)
                return false;

            bool hasBounds = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
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

                if (!entry.selected && !entry.hovered && !ShowSceneObjectOutlines)
                    continue;

                if (ObjectRenderDistance > 0f && Vector3.Distance(cameraPos, entry.bounds.center) > ObjectRenderDistance)
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

                GUI.color = SceneOutlineColor(entry);

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

                if (!entry.selected && !entry.hovered && !ShowSceneObjectOutlines)
                    continue;

                if (ObjectRenderDistance > 0f && Vector3.Distance(cameraPos, entry.bounds.center) > ObjectRenderDistance)
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

            var color = SceneOutlineColor(entry);
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
                case NavMeshArea nma:
                    visual = CreateNavMeshAreaVisual(nma);
                    break;
                case BotActivityZone baz:
                    visual = CreateBotActivityZoneVisual(baz);
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

        private GameObject CreateBotActivityZoneVisual(BotActivityZone zone)
        {
            bool circle = zone.shape == BotActivityZoneShape.Circle;
            var go = GameObject.CreatePrimitive(circle ? PrimitiveType.Cylinder : PrimitiveType.Cube);

            // The cylinder's default capsule collider turns into a large sphere when flattened; use a trigger mesh instead.
            var existing = go.GetComponent<Collider>();
            if (circle)
            {
                var mesh = go.GetComponent<MeshFilter>()?.sharedMesh;
                if (existing != null)
                    UnityEngine.Object.Destroy(existing);
                var meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = true;
                meshCollider.isTrigger = true;
            }
            else if (existing != null)
            {
                existing.isTrigger = true;
            }

            var wire = new GameObject(circle ? "wire_sphere" : "wire_box");
            wire.transform.SetParent(go.transform, false);
            var lr = wire.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.startWidth = 0.08f;
            lr.endWidth = 0.08f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _botActivityZoneWireColor;
            lr.endColor = _botActivityZoneWireColor;
            DrawFlatOutline(lr, circle);
            return go;
        }

        // Outline in the XZ plane at local half-extent 0.5, so the parent's scale sets the real size.
        private static void DrawFlatOutline(LineRenderer lr, bool circle)
        {
            if (circle)
            {
                const int segments = 64;
                lr.positionCount = segments + 1;
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f));
                }
            }
            else
            {
                lr.positionCount = 5;
                lr.SetPosition(0, new Vector3(-0.5f, 0f, -0.5f));
                lr.SetPosition(1, new Vector3(0.5f, 0f, -0.5f));
                lr.SetPosition(2, new Vector3(0.5f, 0f, 0.5f));
                lr.SetPosition(3, new Vector3(-0.5f, 0f, 0.5f));
                lr.SetPosition(4, new Vector3(-0.5f, 0f, -0.5f));
            }
        }

        private GameObject CreateNavMeshAreaVisual(NavMeshArea nma)
        {
            var shape = nma.shape == NavMeshAreaShape.Capsule ? ZoneShape.Capsule : ZoneShape.Box;
            return CreateZoneVisualWithColor(shape, _navMeshAreaWireColor);
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

        private void ApplyNavMeshAreaScale(GameObject visual, NavMeshArea nma)
        {
            var scale = nma.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            visual.transform.localScale = new Vector3(scale.x, scale.y, scale.z);
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
            else if (marker is NavMeshArea)
            {
                color = _navMeshAreaColor;
            }
            else if (marker is BotActivityZone bazFill)
            {
                color = !bazFill.enabled ? _botActivityZoneDisabledColor
                    : _highlightedZoneIds.Contains(bazFill.zoneId) ? _botActivityZoneHighlightColor
                    : IsStrongZone(bazFill) ? _botActivityZoneStrongColor
                    : _botActivityZoneColor;
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
                    else if (marker is NavMeshArea)
                        wireColor = _navMeshAreaWireColor;
                    else if (marker is BotActivityZone bazWire)
                        wireColor = !bazWire.enabled ? _botActivityZoneDisabledWireColor
                            : _highlightedZoneIds.Contains(bazWire.zoneId) ? _botActivityZoneHighlightWireColor
                            : IsStrongZone(bazWire) ? _botActivityZoneStrongWireColor
                            : _botActivityZoneWireColor;
                    else
                        wireColor = _zoneWireColor;

                    lr.startColor = wireColor;
                    lr.endColor = wireColor;

                    if (marker is BotActivityZone bazWidth)
                    {
                        float width = _highlightedZoneIds.Contains(bazWidth.zoneId) || selected ? 0.16f : 0.08f;
                        lr.startWidth = width;
                        lr.endWidth = width;
                    }
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
            MarkerBase bestZone = null;
            float bestZoneDist = float.MaxValue;
            foreach (var hit in hits)
            {
                var visual = hit.collider != null ? hit.collider.GetComponentInParent<MarkerVisual>() : null;
                if (visual == null)
                    continue;
                var marker = _manager.FindById(visual.markerId);
                if (marker == null)
                    continue;
                // Activity zones are large background areas: only chosen when nothing else was hit.
                if (marker is BotActivityZone)
                {
                    if (hit.distance < bestZoneDist)
                    {
                        bestZoneDist = hit.distance;
                        bestZone = marker;
                    }
                    continue;
                }
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
                if (marker is BotActivityZone)
                    continue;
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
            return best ?? bestZone;
        }

        public GizmoAxis PickGizmoAxis(Camera camera, Vector2 screenPos, float maxPx)
        {
            HoveredAxis = GizmoAxis.None;
            if ((_manager?.Selected == null && _manager?.SelectedChild == null) || !ShowGizmo || camera == null)
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
                    targetPoint = GetRotationHandlePosition(dir, pos, camera, scale);
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

        // ---- Activity zone resize handles (independent of the Move/Rotate/Scale gizmo) ----
        private const int ResizeHandleCount = 8;
        private const float ResizeHandleScreenPx = 14f;
        private static readonly Color _resizeHandleColor = new Color(1f, 0.55f, 0.05f, 1f);
        private static readonly Color _resizeHandleHotColor = new Color(1f, 0.95f, 0.3f, 1f);
        private readonly List<GameObject> _resizeHandles = new List<GameObject>();
        private GameObject _resizeRoot;
        public int HoveredResizeHandle { get; set; } = -1;
        public int ActiveResizeHandle { get; set; } = -1;

        // Resize handles exist only for a single, editable (non-vanilla) activity zone.
        public bool TryGetResizeTarget(out BotActivityZone zone)
        {
            zone = null;
            if (_manager == null || !ShowGizmo || _manager.SelectedChild != null || _manager.SelectedIds.Count != 1)
                return false;
            zone = _manager.Selected as BotActivityZone;
            return zone != null && !zone.isVanilla && !_manager.IsLocked(zone);
        }

        public bool TryPickPatrolPoint(
            Camera camera, Vector2 screenPosition, float maxPixels, out BotActivityZone zone, out BotActivityPatrolPoint point)
        {
            zone = _manager?.SelectedIds.Count == 1 ? _manager.Selected as BotActivityZone : null;
            point = null;
            if (camera == null || zone == null || zone.isVanilla || _manager.IsLocked(zone) ||
                !zone.patrolEnabled || zone.patrolPoints == null || !ShowPackGizmos)
            {
                zone = null;
                return false;
            }

            var bestDistance = maxPixels;
            foreach (var candidate in zone.patrolPoints)
            {
                if (candidate?.position == null)
                    continue;
                var projected = camera.WorldToScreenPoint(candidate.position.ToVector3());
                if (projected.z <= 0f)
                    continue;
                var distance = Vector2.Distance(screenPosition, new Vector2(projected.x, projected.y));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    point = candidate;
                }
            }

            if (point != null)
                return true;
            zone = null;
            return false;
        }

        public static int GetResizeHandleCount(BotActivityZone zone)
        {
            return zone != null && zone.shape == BotActivityZoneShape.Circle ? 1 : ResizeHandleCount;
        }

        // Square handle order: four corners (+X+Z, -X+Z, -X-Z, +X-Z), then +X, +Z, -X, -Z edges.
        public static Vector3 GetResizeHandleDirection(BotActivityZone zone, int index)
        {
            var yaw = Quaternion.Euler(0f, zone.rotation != null ? zone.rotation.y : 0f, 0f);
            if (zone.shape == BotActivityZoneShape.Circle)
                return yaw * Vector3.right;
            if (index >= 4)
            {
                switch (index)
                {
                    case 4: return yaw * Vector3.right;
                    case 5: return yaw * Vector3.forward;
                    case 6: return yaw * Vector3.left;
                    default: return yaw * Vector3.back;
                }
            }
            switch (index & 3)
            {
                case 0: return yaw * new Vector3(1f, 0f, 1f).normalized;
                case 1: return yaw * new Vector3(-1f, 0f, 1f).normalized;
                case 2: return yaw * new Vector3(-1f, 0f, -1f).normalized;
                default: return yaw * new Vector3(1f, 0f, -1f).normalized;
            }
        }

        private static Vector3 GetResizeHandleLocalPosition(BotActivityZone zone, int index)
        {
            if (zone.shape == BotActivityZoneShape.Circle)
                return Vector3.right * Mathf.Max(BotActivityZone.MinSize, zone.radius);

            var halfWidth = Mathf.Max(BotActivityZone.MinSize, zone.EffectiveWidth) * 0.5f;
            var halfDepth = Mathf.Max(BotActivityZone.MinSize, zone.EffectiveDepth) * 0.5f;
            switch (index)
            {
                case 0: return new Vector3(halfWidth, 0f, halfDepth);
                case 1: return new Vector3(-halfWidth, 0f, halfDepth);
                case 2: return new Vector3(-halfWidth, 0f, -halfDepth);
                case 3: return new Vector3(halfWidth, 0f, -halfDepth);
                case 4: return new Vector3(halfWidth, 0f, 0f);
                case 5: return new Vector3(0f, 0f, halfDepth);
                case 6: return new Vector3(-halfWidth, 0f, 0f);
                default: return new Vector3(0f, 0f, -halfDepth);
            }
        }

        public static Vector3 GetResizeHandlePosition(BotActivityZone zone, int index)
        {
            var yaw = Quaternion.Euler(0f, zone.rotation != null ? zone.rotation.y : 0f, 0f);
            return zone.position.ToVector3() + yaw * GetResizeHandleLocalPosition(zone, index);
        }

        public int PickResizeHandle(Camera camera, Vector2 screenPos, float maxPx)
        {
            if (camera == null || !TryGetResizeTarget(out var zone))
                return -1;

            var best = -1;
            var bestDist = maxPx;
            for (var i = 0; i < GetResizeHandleCount(zone); i++)
            {
                var projected = camera.WorldToScreenPoint(GetResizeHandlePosition(zone, i));
                if (projected.z <= 0f)
                    continue;
                var dist = Vector2.Distance(screenPos, new Vector2(projected.x, projected.y));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }
            return best;
        }

        private void UpdateResizeHandles()
        {
            var camera = Camera.main;
            if (camera == null || !TryGetResizeTarget(out var zone))
            {
                if (_resizeRoot != null)
                    _resizeRoot.SetActive(false);
                return;
            }

            if (_resizeRoot == null)
            {
                _resizeRoot = new GameObject("MLE_ResizeHandles");
                _resizeRoot.transform.SetParent(_root.transform, false);
                _resizeHandles.Clear();
                for (var i = 0; i < ResizeHandleCount; i++)
                {
                    var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    handle.name = $"MLE_ResizeHandle_{i}";
                    var collider = handle.GetComponent<Collider>();
                    if (collider != null)
                        UnityEngine.Object.Destroy(collider);
                    handle.transform.SetParent(_resizeRoot.transform, false);
                    var mr = handle.GetComponent<MeshRenderer>();
                    if (mr != null)
                        mr.material = new Material(GetGizmoMaterial());
                    _resizeHandles.Add(handle);
                }
            }

            _resizeRoot.SetActive(true);
            var yaw = Quaternion.Euler(0f, zone.rotation != null ? zone.rotation.y : 0f, 0f);
            for (var i = 0; i < _resizeHandles.Count; i++)
            {
                var handle = _resizeHandles[i];
                if (i >= GetResizeHandleCount(zone))
                {
                    handle.SetActive(false);
                    continue;
                }
                handle.SetActive(true);
                var pos = GetResizeHandlePosition(zone, i);
                var distance = Vector3.Distance(camera.transform.position, pos);
                var worldPerPixel = camera.orthographic
                    ? 2f * camera.orthographicSize / Mathf.Max(1f, camera.pixelHeight)
                    : 2f * Mathf.Max(distance, 0.01f) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, camera.pixelHeight);
                var hot = i == ActiveResizeHandle || i == HoveredResizeHandle;
                handle.transform.position = pos;
                handle.transform.rotation = yaw * Quaternion.Euler(0f, 45f, 0f);
                handle.transform.localScale = Vector3.one * worldPerPixel * (hot ? 14f : 10f);

                var mr = handle.GetComponent<MeshRenderer>();
                if (mr != null && mr.material != null)
                    mr.material.color = hot ? _resizeHandleHotColor : _resizeHandleColor;
            }
        }

        private Vector3 GetGizmoPosition()
        {
            if (_manager?.SelectedChild != null)
                return _manager.SelectedChild.position;
            if (_manager?.Selected == null)
                return Vector3.zero;
            if (_manager.SelectedIds.Count > 1)
                return _manager.SelectionCenter;
            return _manager.Selected.position.ToVector3();
        }

        private Quaternion GetGizmoRotation()
        {
            if (_manager?.SelectedChild != null)
                return _manager.SelectedChild.rotation;
            if (_manager?.Selected == null)
                return Quaternion.identity;
            if (_manager.SelectedIds.Count > 1)
                return Quaternion.identity;
            return _manager.Selected.rotation.ToQuaternion();
        }

        private void UpdateGizmo()
        {
            UpdateResizeHandles();
            if (!ShowGizmo || (_manager?.SelectedChild == null && (_manager?.Selected == null || _manager.Selected.isVanilla)))
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
                    handlePos = GetRotationHandlePosition(dir, pos, Camera.main, gizmoScale);
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

            var isSingleLootSelection = _manager.SelectedIds.Count <= 1 &&
                                        _manager.SelectedChild == null &&
                                        _manager.Selected is LooseLootSpawn;
            var targetScreenSize = isSingleLootSelection
                ? Mathf.Clamp(_gizmoScreenSize * 1.28f, 56f, 72f)
                : _gizmoScreenSize;
            var targetWorldSize = camera.orthographic
                ? 2f * camera.orthographicSize * targetScreenSize / Mathf.Max(1f, camera.pixelHeight)
                : 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * targetScreenSize / Mathf.Max(1f, camera.pixelHeight);
            var targetScale = targetWorldSize / _gizmoSize;
            return isSingleLootSelection ? targetScale : Mathf.Max(targetScale, 1f);
        }

        private Vector3 GetRotationHandlePosition(Vector3 normal, Vector3 center, Camera camera, float scale)
        {
            if (camera == null)
            {
                var fallback = Mathf.Abs(normal.y) < 0.9f
                    ? Vector3.Cross(normal, Vector3.up)
                    : Vector3.Cross(normal, Vector3.forward);
                return center + fallback * _gizmoSize * scale;
            }

            var rightOnRing = Vector3.ProjectOnPlane(camera.transform.right, normal);
            var upOnRing = Vector3.ProjectOnPlane(camera.transform.up, normal);
            var handleDirection = rightOnRing.sqrMagnitude >= upOnRing.sqrMagnitude ? rightOnRing : upOnRing;
            if (handleDirection.sqrMagnitude < 0.0001f)
                handleDirection = Vector3.Cross(normal, camera.transform.forward);
            return center + handleDirection.normalized * _gizmoSize * scale;
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
