using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class MarkerManager
    {
        public MapData Data { get; private set; }

        private MarkerBase _selected;
        public MarkerBase Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                SelectedIds.Clear();
                if (_selected != null)
                    SelectedIds.Add(_selected.id);
            }
        }

        public List<string> SelectedIds { get; } = new List<string>();
        public bool IsDirty { get; set; }

        public Vector3 SelectionCenter
        {
            get
            {
                var selected = GetAllMarkers().Where(m => SelectedIds.Contains(m.id)).ToList();
                if (selected.Count == 0)
                    return Vector3.zero;
                var sum = Vector3.zero;
                foreach (var m in selected)
                    sum += m.position.ToVector3();
                return sum / selected.Count;
            }
        }

        private readonly List<string> _undoStack = new List<string>();
        private readonly List<string> _redoStack = new List<string>();
        private const int MaxUndo = 50;

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public void LoadMap(string mapId)
        {
            Data = JsonStorage.Load(mapId);
            Selected = null;
            IsDirty = false;
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public void Snapshot()
        {
            if (Data == null)
                return;

            var snap = new MarkerSnapshot { Data = Data, SelectedId = Selected?.id };
            _undoStack.Add(JsonConvert.SerializeObject(snap));
            if (_undoStack.Count > MaxUndo)
                _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (_undoStack.Count == 0)
                return;

            var current = JsonConvert.SerializeObject(new MarkerSnapshot { Data = Data, SelectedId = Selected?.id });
            var json = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            ApplySnapshot(json);
            _redoStack.Add(current);
        }

        public void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            var current = JsonConvert.SerializeObject(new MarkerSnapshot { Data = Data, SelectedId = Selected?.id });
            var json = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            ApplySnapshot(json);
            _undoStack.Add(current);
        }

        private void ApplySnapshot(string json)
        {
            var snap = JsonConvert.DeserializeObject<MarkerSnapshot>(json);
            Data = snap.Data;
            Selected = string.IsNullOrEmpty(snap.SelectedId) ? null : FindById(snap.SelectedId);
            IsDirty = true;
        }

        private class MarkerSnapshot
        {
            public MapData Data;
            public string SelectedId;
        }

        public void SetMapData(MapData data)
        {
            Data = data;
            Selected = null;
            IsDirty = true;
        }

        public void Save()
        {
            if (Data == null)
                return;

            JsonStorage.Save(Data);
            IsDirty = false;
        }

        public IEnumerable<MarkerBase> GetAllMarkers()
        {
            if (Data == null)
                yield break;

            if (Data.lootSpawns != null)
                foreach (var m in Data.lootSpawns)
                    yield return m;
            if (Data.lootZones != null)
                foreach (var m in Data.lootZones)
                    yield return m;
            if (Data.objects != null)
                foreach (var m in Data.objects)
                    yield return m;
        }

        public MarkerBase FindById(string id)
        {
            return GetAllMarkers().FirstOrDefault(m => m.id == id);
        }

        public bool IsSelected(MarkerBase marker)
        {
            return marker != null && SelectedIds.Contains(marker.id);
        }

        public void SelectOnly(MarkerBase marker)
        {
            Selected = marker;
        }

        public void AddToSelection(MarkerBase marker)
        {
            if (marker == null)
                return;
            if (!SelectedIds.Contains(marker.id))
                SelectedIds.Add(marker.id);
            _selected = marker;
        }

        public void RemoveFromSelection(MarkerBase marker)
        {
            if (marker == null)
                return;
            SelectedIds.Remove(marker.id);
            if (_selected != null && _selected.id == marker.id)
                _selected = SelectedIds.Count > 0 ? FindById(SelectedIds[SelectedIds.Count - 1]) : null;
        }

        public void ToggleSelected(MarkerBase marker)
        {
            if (marker == null)
                return;
            if (IsSelected(marker))
                RemoveFromSelection(marker);
            else
                AddToSelection(marker);
        }

        public void ClearSelection()
        {
            SelectedIds.Clear();
            _selected = null;
        }

        public void SetSelection(IEnumerable<MarkerBase> markers)
        {
            SelectedIds.Clear();
            foreach (var m in markers)
            {
                if (m != null)
                    SelectedIds.Add(m.id);
            }
            _selected = SelectedIds.Count > 0 ? FindById(SelectedIds[0]) : null;
        }

        public void AddMarker(MarkerBase marker)
        {
            if (Data == null || marker == null)
                return;
            switch (marker)
            {
                case LooseLootSpawn s:
                    Data.lootSpawns ??= new List<LooseLootSpawn>();
                    Data.lootSpawns.Add(s);
                    break;
                case LootZone z:
                    Data.lootZones ??= new List<LootZone>();
                    Data.lootZones.Add(z);
                    break;
                case StaticObject o:
                    Data.objects ??= new List<StaticObject>();
                    Data.objects.Add(o);
                    break;
            }
            IsDirty = true;
        }

        public void SelectGroup(string group)
        {
            SelectedIds.Clear();
            foreach (var m in GetAllMarkers())
            {
                if (string.Equals(m.group ?? "", group ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    SelectedIds.Add(m.id);
                    if (_selected == null)
                        _selected = m;
                }
            }
        }

        public IEnumerable<string> GetGroups()
        {
            return GetAllMarkers()
                .Select(m => m.group ?? "")
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase);
        }

        public void SetGroupOnSelection(string group)
        {
            foreach (var id in SelectedIds)
            {
                var m = FindById(id);
                if (m != null)
                    m.group = group ?? "";
            }
            IsDirty = true;
        }

        public void DeleteSelection()
        {
            if (Data == null)
                return;

            var toDelete = SelectedIds.ToList();
            foreach (var id in toDelete)
            {
                var m = FindById(id);
                if (m == null)
                    continue;
                switch (m)
                {
                    case LooseLootSpawn s: Data.lootSpawns.Remove(s); break;
                    case LootZone z: Data.lootZones.Remove(z); break;
                    case StaticObject o: Data.objects.Remove(o); break;
                }
            }
            ClearSelection();
            IsDirty = true;
        }

        public void DuplicateSelection()
        {
            if (Data == null)
                return;

            var originals = SelectedIds.Select(FindById).Where(m => m != null).ToList();
            var newIds = new List<string>();
            foreach (var m in originals)
            {
                var json = JsonConvert.SerializeObject(m);
                MarkerBase copy;
                switch (m)
                {
                    case LooseLootSpawn s:
                        copy = JsonConvert.DeserializeObject<LooseLootSpawn>(json);
                        Data.lootSpawns.Add((LooseLootSpawn)copy);
                        break;
                    case LootZone z:
                        copy = JsonConvert.DeserializeObject<LootZone>(json);
                        Data.lootZones.Add((LootZone)copy);
                        break;
                    case StaticObject o:
                        copy = JsonConvert.DeserializeObject<StaticObject>(json);
                        Data.objects.Add((StaticObject)copy);
                        break;
                    default:
                        continue;
                }
                copy.id = Guid.NewGuid().ToString();
                copy.name = copy.name + "_copy";
                copy.position.x += 0.2f;
                newIds.Add(copy.id);
            }
            SelectedIds.Clear();
            SelectedIds.AddRange(newIds);
            _selected = newIds.Count > 0 ? FindById(newIds[0]) : null;
            IsDirty = true;
        }

        public LooseLootSpawn CreateLootSpawn(Vector3 position, Vector3 rotation)
        {
            Data.lootSpawns ??= new List<LooseLootSpawn>();
            var marker = new LooseLootSpawn
            {
                name = "loot_spawn",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(rotation),
                items = new List<LootItem> { new LootItem { template = "544fb45d4bdc2dee738b4568", chance = 100f } }
            };
            Data.lootSpawns.Add(marker);
            IsDirty = true;
            return marker;
        }

        public LootZone CreateLootZone(Vector3 position)
        {
            Data.lootZones ??= new List<LootZone>();
            var marker = new LootZone
            {
                name = "loot_zone",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                radius = 1f,
                scale = new TransformData { x = 1f, y = 1f, z = 1f },
                shape = ZoneShape.Box,
                items = new List<LootItem> { new LootItem { template = "544fb45d4bdc2dee738b4568", chance = 100f } }
            };
            Data.lootZones.Add(marker);
            IsDirty = true;
            return marker;
        }

        public StaticObject CreateStaticObject(Vector3 position, Vector3 rotation)
        {
            Data.objects ??= new List<StaticObject>();
            var marker = new StaticObject
            {
                name = "static_object",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(rotation),
                scale = TransformData.FromVector3(Vector3.one),
                prefabPath = ""
            };
            Data.objects.Add(marker);
            IsDirty = true;
            return marker;
        }

        public void DeleteSelected()
        {
            if (Selected == null || Data == null)
                return;

            switch (Selected)
            {
                case LooseLootSpawn s: Data.lootSpawns.Remove(s); break;
                case LootZone z: Data.lootZones.Remove(z); break;
                case StaticObject o: Data.objects.Remove(o); break;
            }

            Selected = null;
            IsDirty = true;
        }

        public MarkerBase DuplicateSelected()
        {
            if (Selected == null)
                return null;

            var json = JsonConvert.SerializeObject(Selected);
            MarkerBase copy;

            switch (Selected)
            {
                case LooseLootSpawn s:
                    copy = JsonConvert.DeserializeObject<LooseLootSpawn>(json);
                    Data.lootSpawns.Add((LooseLootSpawn)copy);
                    break;
                case LootZone z:
                    copy = JsonConvert.DeserializeObject<LootZone>(json);
                    Data.lootZones.Add((LootZone)copy);
                    break;
                case StaticObject o:
                    copy = JsonConvert.DeserializeObject<StaticObject>(json);
                    Data.objects.Add((StaticObject)copy);
                    break;
                default:
                    return null;
            }

            copy.id = Guid.NewGuid().ToString();
            copy.name = copy.name + "_copy";
            copy.position.x += 0.5f;
            Selected = copy;
            IsDirty = true;
            return copy;
        }

        public void MoveSelected(Vector3 delta)
        {
            if (Selected == null)
                return;

            Selected.position = TransformData.FromVector3(Selected.position.ToVector3() + delta);
            IsDirty = true;
        }

        public void RotateSelected(Vector3 delta)
        {
            if (Selected == null)
                return;

            Selected.rotation = TransformData.FromVector3(Selected.rotation.ToVector3() + delta);
            IsDirty = true;
        }

        public void ChangeRadius(float delta)
        {
            if (Selected is LootZone zone)
            {
                zone.radius = Mathf.Max(0.1f, zone.radius + delta);
                IsDirty = true;
            }
        }

        public void SnapSelectedToGround()
        {
            if (Selected == null)
                return;

            var ground = GetGroundPosition(Selected.position.ToVector3());
            if (ground.HasValue)
            {
                Selected.position = TransformData.FromVector3(ground.Value);
                IsDirty = true;
            }
        }

        public static Vector3? GetGroundPosition(Vector3 origin)
        {
            var rayOrigin = origin + Vector3.up * 3f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 100f))
                return hit.point;

            return null;
        }

        public static string PositionToJson(Vector3 pos)
        {
            return JsonConvert.SerializeObject(new { x = pos.x, y = pos.y, z = pos.z });
        }

        public static string RotationToJson(Vector3 rot)
        {
            return JsonConvert.SerializeObject(new { x = rot.x, y = rot.y, z = rot.z });
        }

        public static string TransformToJson(Vector3 pos, Vector3 rot)
        {
            return JsonConvert.SerializeObject(new
            {
                position = new { x = pos.x, y = pos.y, z = pos.z },
                rotation = new { x = rot.x, y = rot.y, z = rot.z }
            });
        }
    }
}
