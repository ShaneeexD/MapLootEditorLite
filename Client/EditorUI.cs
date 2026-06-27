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
            GUILayout.Label("F8 = Toggle | E = Select under crosshair | Arrows = Move | R/F = Rotate");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
                _controller.Save();
            if (GUILayout.Button("Export JSON"))
            {
                try
                {
                    GUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(_manager.Data, Formatting.Indented);
                    Plugin.Log.LogInfo("Exported JSON to clipboard");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Export failed: {ex.Message}");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Paste Import"))
                _importText = GUIUtility.systemCopyBuffer;
            if (GUILayout.Button("Apply Import"))
                ApplyImport();
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

            DrawItemTpls(spawn.itemTpls);
        }

        private void DrawLootZone(LootZone zone)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Radius", GUILayout.Width(90));
            zone.radius = FloatField("", zone.radius);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn Chance", GUILayout.Width(90));
            zone.spawnChance = FloatField("", zone.spawnChance);
            GUILayout.EndHorizontal();

            DrawItemTpls(zone.itemTpls);
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

        private void DrawItemTpls(System.Collections.Generic.List<string> itemTpls)
        {
            GUILayout.Label("Item Tpls (first used for preview):");
            if (itemTpls == null)
                return;

            for (int i = 0; i < itemTpls.Count; i++)
            {
                GUILayout.BeginHorizontal();
                itemTpls[i] = GUILayout.TextField(itemTpls[i]);
                if (GUILayout.Button("-", GUILayout.Width(24)))
                {
                    itemTpls.RemoveAt(i);
                    _manager.IsDirty = true;
                    break;
                }
                GUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Item Tpl"))
            {
                itemTpls.Add("");
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
                _manager.SetMapData(imported);
                _renderer.Rebuild();
                Plugin.Log.LogInfo("Imported marker data from clipboard");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Import failed: {ex.Message}");
            }
        }
    }
}
