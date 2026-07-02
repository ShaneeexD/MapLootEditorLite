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

        public GizmoMode GizmoMode { get; set; } = GizmoMode.Translate;
        public bool ShowGizmo { get; set; } = true;
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

            foreach (var marker in _manager.GetAllMarkersIncludingVanilla())
            {
                CreateVisual(marker);
            }
        }

        public void Update()
        {
            if (_manager?.Data == null)
                return;

            foreach (var marker in _manager.GetAllMarkersIncludingVanilla())
            {
                if (marker is LootZone currentZone && _visuals.TryGetValue(marker.id, out GameObject existingVisual))
                {
                    if (_zoneShapeCache.TryGetValue(marker.id, out ZoneShape cachedShape) && cachedShape != currentZone.shape)
                    {
                        UnityEngine.Object.Destroy(existingVisual);
                        _visuals.Remove(marker.id);
                    }
                }

                if (!_visuals.TryGetValue(marker.id, out GameObject visual))
                {
                    visual = CreateVisual(marker);
                }

                if (visual != null)
                {
                    visual.transform.position = marker.position.ToVector3();
                    visual.transform.rotation = marker.rotation.ToQuaternion();

                    if (marker is LootZone zone)
                    {
                        ApplyZoneScale(visual, zone);
                        _zoneShapeCache[marker.id] = zone.shape;
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
                var screenPos = camera.WorldToScreenPoint(marker.position.ToVector3());
                if (screenPos.z <= 0)
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
        }

        private GameObject CreateVisual(MarkerBase marker)
        {
            GameObject visual;
            switch (marker)
            {
                case LooseLootSpawn _:
                    visual = CreatePrimitiveNoCollider(PrimitiveType.Sphere, 0.15f);
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
                default:
                    return null;
            }

            visual.name = $"MLE_Gizmo_{marker.name}";
            visual.transform.SetParent(_root.transform, false);
            visual.transform.position = marker.position.ToVector3();
            visual.transform.rotation = marker.rotation.ToQuaternion();

            _visuals[marker.id] = visual;
            ApplyColor(visual, marker, false);
            return visual;
        }

        private GameObject CreatePrimitiveNoCollider(PrimitiveType type, float baseScale)
        {
            var go = GameObject.CreatePrimitive(type);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * baseScale;
            return go;
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
            var go = CreatePrimitiveNoCollider(PrimitiveType.Sphere, 1f);
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
            var go = CreatePrimitiveNoCollider(PrimitiveType.Cube, 1f);
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
            var go = CreatePrimitiveNoCollider(PrimitiveType.Cube, 0.5f);
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
            var go = CreatePrimitiveNoCollider(PrimitiveType.Cube, 0.5f);
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
            var go = CreatePrimitiveNoCollider(PrimitiveType.Cube, 1f);
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
            var go = CreatePrimitiveNoCollider(PrimitiveType.Cube, 0.5f);
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
            return CreatePrimitiveNoCollider(PrimitiveType.Cylinder, 1f);
        }

        private GameObject CreateCapsuleVisual()
        {
            return CreatePrimitiveNoCollider(PrimitiveType.Capsule, 1f);
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

            MarkerBase best = null;
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
                    targetPoint = pos + tangent * _gizmoSize;
                }
                else
                {
                    targetPoint = pos + dir * _gizmoSize;
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

            foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
            {
                var dir = GetGizmoAxisDirection(axis, rot);
                var end = pos + dir * _gizmoSize;
                var color = GetGizmoColor(axis);
                var isActive = ActiveAxis == axis || HoveredAxis == axis;

                var line = _gizmoLines[axis];
                if (line.material != null)
                    line.material.color = color;
                line.startColor = Color.white;
                line.endColor = Color.white;
                line.startWidth = isActive ? 0.04f : 0.025f;
                line.endWidth = isActive ? 0.04f : 0.025f;

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
                    handlePos = pos + tangent * _gizmoSize;
                }
                else
                {
                    handlePos = end;
                }

                handle.transform.position = handlePos;
                handle.transform.rotation = Quaternion.identity;
                var scale = isActive ? _gizmoHandleSize * 1.5f : _gizmoHandleSize;
                handle.transform.localScale = Vector3.one * scale;

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
                        ring.startWidth = isActive ? 0.04f : 0.025f;
                        ring.endWidth = isActive ? 0.04f : 0.025f;
                        DrawGizmoRing(ring, pos, dir, _gizmoSize);
                    }
                    else
                    {
                        ring.enabled = false;
                    }
                }
            }
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
            return CreatePrimitiveNoCollider(type, _gizmoHandleSize);
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
