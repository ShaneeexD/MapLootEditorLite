using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class EditorUI
    {
        private readonly MapEditorController _controller;
        private readonly MarkerManager _manager;
        private readonly MarkerRenderer _renderer;
        private readonly LootPreviewSpawner _previews;

        private Rect _windowRect = new Rect(20, 20, 420, 640);
        private Vector2 _scrollPos;
        private string _importText = "";
        private string _packName = "MyLootPack";

        public Rect WindowRect => _windowRect;

        public EditorUI(MapEditorController controller, MarkerManager manager, MarkerRenderer renderer, LootPreviewSpawner previews)
        {
            _controller = controller;
            _manager = manager;
            _renderer = renderer;
            _previews = previews;
        }

        public void Draw()
        {
            _windowRect = GUILayout.Window(12345, _windowRect, DrawWindow, "Map Loot Editor Lite");
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
            DrawMarkerList();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        private void DrawHeader()
        {
            GUILayout.Label($"Map: {_manager.Data?.map ?? "none"} | Markers: {_manager.GetAllMarkers().Count()}");
            GUILayout.Label("F8 = Toggle | F9 = Freecam | MMB = Cursor | 1=T 2=R 3=S | E = Select | Arrows = Move | R/F = Rotate");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
                _controller.Save();
            if (GUILayout.Button("Copy JSON"))
            {
                try
                {
                    GUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(_manager.Data, Formatting.Indented);
                    Plugin.Log.LogInfo("Copied JSON to clipboard");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Copy failed: {ex.Message}");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Pack:", GUILayout.Width(40));
            _packName = GUILayout.TextField(_packName);
            if (GUILayout.Button("Export Pack"))
                _controller.ExportPack(_packName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Paste Map JSON"))
                _importText = GUIUtility.systemCopyBuffer;
            if (GUILayout.Button("Apply Map Import"))
                ApplyImport();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Paste Pack JSON"))
                _importText = GUIUtility.systemCopyBuffer;
            if (GUILayout.Button("Apply Pack Import"))
                ApplyPackImport();
            GUILayout.EndHorizontal();
            _importText = GUILayout.TextArea(_importText, GUILayout.Height(40));
        }

        private void DrawCreateButtons()
        {
            GUILayout.Label("Create at player position:", GUILayout.Height(20));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Loot Spawn"))
                _controller.CreateLootSpawn();
            if (GUILayout.Button("Loot Zone"))
                _controller.CreateLootZone();
            if (GUILayout.Button("Static Object"))
                _controller.CreateStaticObject();
            GUILayout.EndHorizontal();

            GUILayout.Label("Create at crosshair:", GUILayout.Height(20));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Loot Spawn"))
                _controller.CreateLootSpawnAtLook();
            GUILayout.EndHorizontal();
        }

        private void DrawUtilityButtons()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Snap to Ground"))
                _controller.SnapSelected();
            if (GUILayout.Button("Duplicate"))
            {
                _manager.DuplicateSelected();
                _renderer.Rebuild();
            }
            if (GUILayout.Button("Delete"))
                _controller.DeleteSelected();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear Previews"))
                _controller.ClearPreviews();
            if (GUILayout.Button("Clear Visuals"))
                _controller.ClearVisuals();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_controller.IsFreeCam ? "Exit Freecam" : "Freecam (F9)"))
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

            GUILayout.Label($"Selected: {selected.Kind} - {selected.name}");

            selected.name = GUILayout.TextField(selected.name);

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
            if (GUILayout.Button("Copy Spawn JSON"))
            {
                try
                {
                    GUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(selected, Formatting.Indented);
                    Plugin.Log.LogInfo("Copied selected marker JSON to clipboard");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to copy marker JSON: {ex.Message}");
                }
            }
            GUILayout.EndHorizontal();

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
                _previews.SpawnInZone(lz);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLooseLootSpawn(LooseLootSpawn spawn)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn Chance", GUILayout.Width(90));
            spawn.spawnChance = FloatField("", spawn.spawnChance);
            GUILayout.EndHorizontal();

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

            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn Chance", GUILayout.Width(90));
            zone.spawnChance = FloatField("", zone.spawnChance);
            GUILayout.EndHorizontal();

            DrawItems(zone.items, true, (i) => _previews.SpawnAtZoneCenter(zone, i));
        }

        private void DrawStaticObject(StaticObject obj)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prefab Path", GUILayout.Width(90));
            obj.prefabPath = GUILayout.TextField(obj.prefabPath ?? "");
            GUILayout.EndHorizontal();

            GUILayout.Label("Scale:");
            var scale = Vector3Field("Scale", obj.scale.ToVector3());
            if (scale != obj.scale.ToVector3())
            {
                obj.scale = TransformData.FromVector3(scale);
                _manager.IsDirty = true;
            }
        }

        private void DrawItems(System.Collections.Generic.List<LootItem> items, bool showRotation = false, System.Action<int> onPreview = null)
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
                item.template = GUILayout.TextField(item.template ?? "");
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

        private void DrawMarkerList()
        {
            GUILayout.Label("Markers:");
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(160));

            foreach (var marker in _manager.GetAllMarkers())
            {
                GUILayout.BeginHorizontal();
                bool isSelected = _manager.Selected != null && _manager.Selected.id == marker.id;
                if (GUILayout.Button(isSelected ? ">>>" : "Sel", GUILayout.Width(40)))
                {
                    _manager.Selected = marker;
                }
                GUILayout.Label($"{marker.Kind} | {marker.name}", GUILayout.Width(220));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
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

        private void ApplyImport()
        {
            try
            {
                var imported = JsonConvert.DeserializeObject<MapData>(_importText);
                if (imported == null)
                {
                    Plugin.Log.LogWarning("Import JSON deserialized to null");
                    return;
                }

                imported.map = _manager.Data?.map ?? imported.map;
                JsonStorage.MigrateLegacyItems(imported);
                _manager.SetMapData(imported);
                _renderer.Rebuild();
                Plugin.Log.LogInfo("Imported marker data from clipboard");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Import failed: {ex.Message}");
            }
        }

        private void ApplyPackImport()
        {
            try
            {
                var imported = JsonConvert.DeserializeObject<PackData>(_importText);
                if (imported == null)
                {
                    Plugin.Log.LogWarning("Pack JSON deserialized to null");
                    return;
                }

                var currentMap = _manager.Data?.map ?? _controller.CurrentMapId;
                if (string.IsNullOrEmpty(currentMap))
                {
                    Plugin.Log.LogWarning("No current map to import into");
                    return;
                }

                if (imported.maps == null || !imported.maps.TryGetValue(currentMap, out var mapData))
                {
                    Plugin.Log.LogWarning($"Pack does not contain map '{currentMap}'");
                    return;
                }

                mapData.map = currentMap;
                JsonStorage.MigrateLegacyItems(mapData);
                _manager.SetMapData(mapData);
                _renderer.Rebuild();
                Plugin.Log.LogInfo($"Imported pack '{imported.name}' data for {currentMap} from clipboard");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Pack import failed: {ex.Message}");
            }
        }
    }
}
