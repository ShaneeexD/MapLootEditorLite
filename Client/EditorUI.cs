using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT.InventoryLogic;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class EditorUI
    {
        private readonly MapEditorController _controller;
        private readonly MarkerManager _manager;
        private readonly MarkerRenderer _renderer;
        private readonly LootPreviewSpawner _previews;

        private Rect _windowRect = new Rect(20, 20, 460, 540);
        private Rect _confirmRect = new Rect(0, 0, 300, 120);
        private Vector2 _markerListScrollPos;
        private Vector2 _groupScrollPos;
        private Rect _resizeStartRect;
        private Vector2 _resizeStartMouse;
        private bool _resizeHeld;
        private string _packName = "MyLootPack";
        private string _searchText = "";
        private string _newGroupName = "";
        private bool _deletePending;
        private bool _pickingSource;
        private StaticObject _pickingSourceTarget;
        private bool _pickUseParent;
        private readonly Dictionary<string, string> _itemNameCache = new Dictionary<string, string>();

        public Rect WindowRect => _windowRect;
        public string PackName => _packName;
        public bool IsPickingSource => _pickingSource;
        public bool IsDeletePending => _deletePending;

        public EditorUI(MapEditorController controller, MarkerManager manager, MarkerRenderer renderer, LootPreviewSpawner previews)
        {
            _controller = controller;
            _manager = manager;
            _renderer = renderer;
            _previews = previews;
        }

        public void Draw()
        {
            var scale = GetScale();
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            _windowRect = GUILayout.Window(12345, _windowRect, DrawWindow, "Map Loot Editor Lite");
            if (_deletePending)
            {
                _confirmRect.x = Screen.width / 2f / scale - 150;
                _confirmRect.y = Screen.height / 2f / scale - 60;
                _confirmRect = GUILayout.Window(12346, _confirmRect, DrawConfirmDelete, "Confirm Delete");
            }

            GUI.matrix = prevMatrix;
        }

        private float GetScale()
        {
            return Plugin.UIScale?.Value ?? 1f;
        }

        public StaticObject PickingSourceTarget => _pickingSourceTarget;

        public void ClearPickingSource()
        {
            _pickingSource = false;
            _pickingSourceTarget = null;
        }

        public GameObject TryPickSourceSceneObject()
        {
            var cam = Camera.main;
            if (cam == null)
                return null;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                var picked = hit.transform.gameObject;
                if (_pickUseParent && picked.transform.parent != null)
                    picked = picked.transform.parent.gameObject;
                Plugin.Log.LogInfo($"Picked scene object: {picked.name} at {picked.transform.position}");
                return picked;
            }

            Plugin.Log.LogWarning("Scene picker raycast did not hit anything.");
            return null;
        }

        public void RequestDelete()
        {
            if (_manager.Selected != null)
                _deletePending = true;
        }

        private void DrawConfirmDelete(int id)
        {
            var count = _manager.SelectedIds.Count;
            var label = count > 1 ? $"Delete {count} selected markers?" : $"Delete '{_manager.Selected?.name}'?";
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yes"))
            {
                _controller.DeleteSelected();
                _deletePending = false;
            }
            if (GUILayout.Button("No"))
                _deletePending = false;
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            DrawHeader();
            GUILayout.Space(8);
            DrawCreateButtons();
            GUILayout.Space(8);
            DrawUtilityButtons();
            GUILayout.Space(8);
            DrawSelectedMarker();
            GUILayout.Space(8);
            DrawSearchGroups();
            GUILayout.Space(8);
            DrawMarkerList();

            GUILayout.EndVertical();
            DrawResizeHandle();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawResizeHandle()
        {
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.RepeatButton("\u25E2", GUILayout.Width(16), GUILayout.Height(16)))
            {
                var ev = Event.current;
                if (!_resizeHeld)
                {
                    _resizeHeld = true;
                    _resizeStartRect = _windowRect;
                    _resizeStartMouse = ev.mousePosition;
                }
                var delta = ev.mousePosition - _resizeStartMouse;
                _windowRect.width = Mathf.Max(260, _resizeStartRect.width + delta.x);
                _windowRect.height = Mathf.Max(300, _resizeStartRect.height + delta.y);
            }
            else
            {
                _resizeHeld = false;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawHeader()
        {
            GUILayout.Label($"Map: {_manager.Data?.map ?? "none"} | Markers: {_manager.GetAllMarkers().Count()}");
            GUILayout.Label("F8 = Toggle | MMB = Cursor | 1=T 2=R 3=S | E = Select | Arrows = Move | WASD = Cam | Space/Ctrl = Up/Down | Shift = Fast");
            GUILayout.Label("Ctrl+C = Copy | Ctrl+V = Paste | Ctrl+Z = Undo | Ctrl+Y = Redo | Delete = Delete selected");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Pack:", GUILayout.Width(40));
            _packName = GUILayout.TextField(_packName);
            if (GUILayout.Button("Export Pack"))
                _controller.ExportPack(_packName);
            GUILayout.EndHorizontal();
        }

        private void DrawCreateButtons()
        {
            GUILayout.Label("Create at crosshair:", GUILayout.Height(20));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Loot Spawn"))
                _controller.CreateLootSpawn();
            if (GUILayout.Button("Loot Zone"))
                _controller.CreateLootZone();
            if (GUILayout.Button("Static Object"))
                _controller.CreateStaticObject();
            GUILayout.EndHorizontal();
        }

        private void DrawUtilityButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Snap to Ground"))
                _controller.SnapSelected();
            if (GUILayout.Button("Duplicate"))
                _controller.DuplicateSelected();
            if (GUILayout.Button("Delete"))
                RequestDelete();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Previews"))
                _controller.ClearPreviews();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_controller.IsFreeCam ? "Exit Editor Mode" : "Editor Mode"))
                _controller.ToggleFreeCam();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Gizmo", GUILayout.Width(45));
            if (GUILayout.Button("T")) _controller.SetGizmoMode(GizmoMode.Translate);
            if (GUILayout.Button("R")) _controller.SetGizmoMode(GizmoMode.Rotate);
            if (GUILayout.Button("S")) _controller.SetGizmoMode(GizmoMode.Scale);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Pos"))
            {
                if (_manager.Selected != null)
                    GUIUtility.systemCopyBuffer = MarkerManager.PositionToJson(_manager.Selected.position.ToVector3());
            }
            if (GUILayout.Button("Copy Rot"))
            {
                if (_manager.Selected != null)
                    GUIUtility.systemCopyBuffer = MarkerManager.RotationToJson(_manager.Selected.rotation.ToVector3());
            }
            if (GUILayout.Button("Copy Transform"))
            {
                if (_manager.Selected != null)
                    GUIUtility.systemCopyBuffer = MarkerManager.TransformToJson(_manager.Selected.position.ToVector3(), _manager.Selected.rotation.ToVector3());
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSelectedMarker()
        {
            var selected = _manager.Selected;
            if (selected == null)
            {
                GUILayout.Label("No marker selected.");
                return;
            }

            var selectedCount = _manager.SelectedIds.Count;
            GUILayout.Label(selectedCount > 1 ? $"Selected {selectedCount} markers (primary: {selected.name})" : $"Selected: {selected.Kind} - {selected.name}");

            selected.name = GUILayout.TextField(selected.name);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Group", GUILayout.Width(60));
            var group = GUILayout.TextField(selected.group ?? "");
            if (group != selected.group)
            {
                selected.group = group;
                _manager.IsDirty = true;
            }
            GUILayout.EndHorizontal();

            if (selectedCount > 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Assign Group to Selection"))
                    _manager.SetGroupOnSelection(selected.group);
                if (GUILayout.Button("Clear Group from Selection"))
                    _manager.SetGroupOnSelection("");
                GUILayout.EndHorizontal();
            }

            var pos = Vector3Field("Position", selected.position.ToVector3());
            if (pos != selected.position.ToVector3())
            {
                selected.position = TransformData.FromVector3(pos);
                _manager.IsDirty = true;
            }

            var rot = Vector3Field("Rotation", selected.rotation.ToVector3());
            if (rot != selected.rotation.ToVector3())
            {
                selected.rotation = TransformData.FromVector3(rot);
                _manager.IsDirty = true;
            }

            switch (selected)
            {
                case LooseLootSpawn spawn:
                    DrawLooseLootSpawn(spawn);
                    break;
                case LootZone zone:
                    DrawLootZone(zone);
                    break;
                case StaticObject obj:
                    DrawStaticObject(obj);
                    break;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Preview Item"))
            {
                if (selected is LooseLootSpawn s)
                    _previews.SpawnAtMarker(s);
                else if (selected is LootZone z)
                    _previews.SpawnAtZoneCenter(z);
            }
            if (selected is LootZone lz && GUILayout.Button("Preview Random In Zone"))
            {
                _previews.SpawnAllInZone(lz);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLooseLootSpawn(LooseLootSpawn spawn)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Respawnable", GUILayout.Width(90));
            spawn.respawnable = GUILayout.Toggle(spawn.respawnable, "");
            GUILayout.EndHorizontal();

            DrawItems(spawn.items, false, (i) => _previews.SpawnAtMarker(spawn, i));
        }

        private void DrawLootZone(LootZone zone)
        {
            if (zone.scale == null)
                zone.scale = new TransformData { x = 1f, y = 1f, z = 1f };

            var shapes = new[] { "Sphere", "Box", "Cylinder", "Capsule" };
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shape", GUILayout.Width(90));
            zone.shape = (ZoneShape)GUILayout.SelectionGrid((int)zone.shape, shapes, 2, GUILayout.Width(240));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Radius", GUILayout.Width(90));
            zone.radius = FloatField("", zone.radius);
            GUILayout.EndHorizontal();

            GUILayout.Label("Scale:");
            var scale = Vector3Field("Scale", zone.scale.ToVector3());
            if (scale != zone.scale.ToVector3())
            {
                zone.scale = TransformData.FromVector3(scale);
                _manager.IsDirty = true;
            }

            DrawItems(zone.items, true, (i) => _previews.SpawnAtZoneCenter(zone, i));
        }

        private void DrawStaticObject(StaticObject obj)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prefab Path", GUILayout.Width(90));
            obj.prefabPath = GUILayout.TextField(obj.prefabPath ?? "", GUILayout.MaxWidth(240));
            GUILayout.EndHorizontal();

            GUILayout.Label("Scale:");
            var scale = Vector3Field("Scale", obj.scale.ToVector3());
            if (scale != obj.scale.ToVector3())
            {
                obj.scale = TransformData.FromVector3(scale);
                _manager.IsDirty = true;
            }

            GUILayout.BeginHorizontal();
            if (_pickingSource && _pickingSourceTarget == obj)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Click an object in the world...", GUILayout.ExpandWidth(true));
                _pickUseParent = GUILayout.Toggle(_pickUseParent, "Parent object");
                if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                {
                    _pickingSource = false;
                    _pickingSourceTarget = null;
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("Pick from Scene"))
                {
                    _pickingSource = true;
                    _pickingSourceTarget = obj;
                }
            }
            if (!string.IsNullOrEmpty(obj.sourceObjectName) && GUILayout.Button("Clear Source", GUILayout.Width(90)))
            {
                obj.sourceObjectName = "";
                obj.sourceObjectPosition = new TransformData();
                _manager.IsDirty = true;
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(obj.sourceObjectName))
            {
                GUILayout.Label($"Source: {obj.sourceObjectName} @ {obj.sourceObjectPosition.x:F2}, {obj.sourceObjectPosition.y:F2}, {obj.sourceObjectPosition.z:F2}");
            }

            if (GUILayout.Button("Preview Object"))
                _previews.SpawnStaticPreview(obj);
        }

        private GameObject PickSceneObjectAtMouse()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Plugin.Log.LogWarning("No main camera found for scene picking.");
                return null;
            }

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                var picked = hit.transform.gameObject;
                if (_pickUseParent && picked.transform.parent != null)
                    picked = picked.transform.parent.gameObject;
                Plugin.Log.LogInfo($"Picked scene object: {picked.name} at {picked.transform.position}");
                return picked;
            }

            Plugin.Log.LogWarning("Scene picker raycast did not hit anything.");
            return null;
        }

        private void DrawItems(List<LootItem> items, bool showRotation = false, System.Action<int> onPreview = null)
        {
            if (items == null)
                return;

            GUILayout.Label("Items (chance does not need to add to 100):");
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.rotation == null)
                    item.rotation = new TransformData();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Tpl", GUILayout.Width(30));
                item.template = GUILayout.TextField(item.template ?? "", GUILayout.Width(130));
                var name = GetItemName(item.template);
                GUILayout.Label("%", GUILayout.Width(18));
                item.chance = FloatField("", item.chance);
                if (onPreview != null && GUILayout.Button("Prev", GUILayout.Width(40)))
                {
                    onPreview(i);
                    break;
                }
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    items.RemoveAt(i);
                    _manager.IsDirty = true;
                    break;
                }
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(name))
                    GUILayout.Label($"  {name}", GUILayout.Height(18));

                if (showRotation)
                {
                    GUILayout.BeginHorizontal();
                    item.randomRotation = GUILayout.Toggle(item.randomRotation, "Random Rotation");
                    if (!item.randomRotation)
                    {
                        var rot = Vector3Field("Rot", item.rotation.ToVector3());
                        item.rotation = TransformData.FromVector3(rot);
                    }
                    GUILayout.EndHorizontal();
                }
            }

            if (GUILayout.Button("Add Item"))
            {
                items.Add(new LootItem());
                _manager.IsDirty = true;
            }
        }

        private string GetItemName(string template)
        {
            if (string.IsNullOrEmpty(template))
                return null;
            if (_itemNameCache.TryGetValue(template, out var cached))
                return cached;

            var factory = Singleton<ItemFactoryClass>.Instance;
            if (factory == null || !factory.ItemTemplates.TryGetValue(template, out var itemTemplate))
            {
                _itemNameCache[template] = null;
                return null;
            }

            var name = GetTemplateName(itemTemplate);
            _itemNameCache[template] = name;
            return name;
        }

        private static string GetTemplateName(object itemTemplate)
        {
            if (itemTemplate == null)
                return null;

            var type = itemTemplate.GetType();
            var prop = type.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            if (prop != null)
                return prop.GetValue(itemTemplate) as string;

            var field = type.GetField("_name", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(itemTemplate) as string;

            return null;
        }

        private void DrawSearchGroups()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(45));
            _searchText = GUILayout.TextField(_searchText ?? "", GUILayout.Width(180));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Group", GUILayout.Width(45));
            _newGroupName = GUILayout.TextField(_newGroupName ?? "");
            if (GUILayout.Button("Assign", GUILayout.Width(60)) && _manager.Selected != null)
                _manager.SetGroupOnSelection(_newGroupName);
            GUILayout.EndHorizontal();

            var groups = _manager.GetGroups().ToList();
            if (groups.Count > 0)
            {
                GUILayout.Label("Existing groups:");
                _groupScrollPos = GUILayout.BeginScrollView(_groupScrollPos, GUILayout.Height(60));
                foreach (var group in groups)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(group);
                    if (GUILayout.Button("Select", GUILayout.Width(60)))
                        _manager.SelectGroup(group);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
        }

        private void DrawMarkerList()
        {
            GUILayout.Label("Markers:");
            _markerListScrollPos = GUILayout.BeginScrollView(_markerListScrollPos, GUILayout.Height(120));

            foreach (var marker in _manager.GetAllMarkers())
            {
                if (!MatchesSearch(marker))
                    continue;

                GUILayout.BeginHorizontal();
                bool isSelected = _manager.IsSelected(marker);
                bool newSelected = GUILayout.Toggle(isSelected, "", GUILayout.Width(20));
                if (newSelected != isSelected)
                    _manager.ToggleSelected(marker);

                if (GUILayout.Button("Sel", GUILayout.Width(40)))
                    _manager.SelectOnly(marker);

                var groupLabel = string.IsNullOrWhiteSpace(marker.group) ? "-" : marker.group;
                GUILayout.Label($"{marker.Kind} | {groupLabel} | {marker.name}");
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private bool MatchesSearch(MarkerBase marker)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
                return true;
            var term = _searchText.Trim();
            return (marker.name?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                || (marker.group?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                || (marker.Kind.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private Vector3 Vector3Field(string label, Vector3 value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(60));
            var x = FloatField("X", value.x);
            var y = FloatField("Y", value.y);
            var z = FloatField("Z", value.z);
            GUILayout.EndHorizontal();
            return new Vector3(x, y, z);
        }

        private float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(18));
            var text = GUILayout.TextField(value.ToString("F3", CultureInfo.InvariantCulture), GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;

            return value;
        }

    }
}
