using System.Collections.Generic;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class MarkerRenderer
    {
        private readonly MarkerManager _manager;
        private readonly GameObject _root;
        private readonly Dictionary<string, GameObject> _visuals = new Dictionary<string, GameObject>();

        private readonly Color _looseColor = new Color(0f, 1f, 0f, 0.6f);
        private readonly Color _zoneColor = new Color(1f, 1f, 0f, 0.15f);
        private readonly Color _zoneWireColor = new Color(1f, 1f, 0f, 0.8f);
        private readonly Color _objectColor = new Color(0f, 0.4f, 1f, 0.6f);
        private readonly Color _selectedColor = Color.white;

        private Material _wireMaterial;
        private readonly Dictionary<string, ZoneShape> _zoneShapeCache = new Dictionary<string, ZoneShape>();

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

            foreach (var marker in _manager.GetAllMarkers())
            {
                CreateVisual(marker);
            }
        }

        public void Update()
        {
            if (_manager?.Data == null)
                return;

            foreach (var marker in _manager.GetAllMarkers())
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

                    bool isSelected = _manager.Selected != null && _manager.Selected.id == marker.id;
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

            foreach (var marker in _manager.GetAllMarkers())
            {
                var screenPos = camera.WorldToScreenPoint(marker.position.ToVector3());
                if (screenPos.z <= 0)
                    continue;

                var rect = new Rect(screenPos.x - 50f, Screen.height - screenPos.y - 30f, 100f, 20f);
                GUI.Label(rect, marker.name, style);
            }
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
                    visual = CreatePrimitiveNoCollider(PrimitiveType.Cube, 0.5f);
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
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
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
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.positionCount = 0;
            lr.material = GetWireMaterial();
            lr.startColor = _zoneWireColor;
            lr.endColor = _zoneWireColor;

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
            else if (marker is LooseLootSpawn)
            {
                color = _looseColor;
            }
            else if (marker is LootZone)
            {
                color = _zoneColor;
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
                    lr.startColor = selected ? _selectedColor : _zoneWireColor;
                    lr.endColor = selected ? _selectedColor : _zoneWireColor;
                }
            }
        }

        public MarkerBase PickFromScreenCenter(Camera camera, Vector2 screenCenter, float maxDistance)
        {
            if (_manager?.Data == null || camera == null)
                return null;

            MarkerBase best = null;
            float bestScore = float.MaxValue;

            foreach (var marker in _manager.GetAllMarkers())
            {
                var worldPos = marker.position.ToVector3();
                var screenPos = camera.WorldToScreenPoint(worldPos);
                if (screenPos.z <= 0)
                    continue;

                var dist2d = Vector2.Distance(screenCenter, new Vector2(screenPos.x, screenPos.y));
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
    }
}
