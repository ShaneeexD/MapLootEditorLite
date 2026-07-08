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
        public MapData VanillaData { get; set; }
        public bool IsVanillaActive { get; set; }

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
                var selected = GetActiveMarkers().Where(m => SelectedIds.Contains(m.id)).ToList();
                if (selected.Count == 0)
                    return Vector3.zero;
                var sum = Vector3.zero;
                foreach (var m in selected)
                    sum += m.position.ToVector3();
                return sum / selected.Count;
            }
        }

        public bool IsVanilla(MarkerBase marker) => marker?.isVanilla == true;

        public IEnumerable<MarkerBase> GetActiveMarkers()
        {
            if (IsVanillaActive)
                return GetVanillaMarkers();
            return GetAllMarkers();
        }

        public IEnumerable<MarkerBase> GetVanillaMarkers()
        {
            if (VanillaData == null)
                yield break;

            if (VanillaData.lootSpawns != null)
                foreach (var m in VanillaData.lootSpawns)
                    yield return m;
            if (VanillaData.lootZones != null)
                foreach (var m in VanillaData.lootZones)
                    yield return m;
            if (VanillaData.objects != null)
                foreach (var m in VanillaData.objects)
                    yield return m;
            if (VanillaData.wttQuestZones != null)
                foreach (var m in VanillaData.wttQuestZones)
                    yield return m;
            if (VanillaData.wttStaticObjects != null)
                foreach (var m in VanillaData.wttStaticObjects)
                    yield return m;
            if (VanillaData.interactiveObjects != null)
                foreach (var m in VanillaData.interactiveObjects)
                    yield return m;
            if (VanillaData.extractZones != null)
                foreach (var m in VanillaData.extractZones)
                    yield return m;
            if (VanillaData.botSpawnPoints != null)
                foreach (var m in VanillaData.botSpawnPoints)
                    yield return m;
            if (VanillaData.botSpawnZones != null)
                foreach (var m in VanillaData.botSpawnZones)
                    yield return m;
            if (VanillaData.lightZones != null)
                foreach (var m in VanillaData.lightZones)
                    yield return m;
            if (VanillaData.triggerZones != null)
                foreach (var m in VanillaData.triggerZones)
                    yield return m;
            if (VanillaData.occlusionRepairVolumes != null)
                foreach (var m in VanillaData.occlusionRepairVolumes)
                    yield return m;
        }

        public IEnumerable<MarkerBase> GetAllMarkersIncludingVanilla()
        {
            foreach (var m in GetAllMarkers())
                yield return m;
            foreach (var m in GetVanillaMarkers())
                yield return m;
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
            if (Data.wttQuestZones != null)
                foreach (var m in Data.wttQuestZones)
                    yield return m;
            if (Data.wttStaticObjects != null)
                foreach (var m in Data.wttStaticObjects)
                    yield return m;
            if (Data.interactiveObjects != null)
                foreach (var m in Data.interactiveObjects)
                    yield return m;
            if (Data.extractZones != null)
                foreach (var m in Data.extractZones)
                    yield return m;
            if (Data.botSpawnPoints != null)
                foreach (var m in Data.botSpawnPoints)
                    yield return m;
            if (Data.botSpawnZones != null)
                foreach (var m in Data.botSpawnZones)
                    yield return m;
            if (Data.lightZones != null)
                foreach (var m in Data.lightZones)
                    yield return m;
            if (Data.triggerZones != null)
                foreach (var m in Data.triggerZones)
                    yield return m;
            if (Data.occlusionRepairVolumes != null)
                foreach (var m in Data.occlusionRepairVolumes)
                    yield return m;
        }

        public MarkerBase FindById(string id)
        {
            return GetAllMarkersIncludingVanilla().FirstOrDefault(m => m.id == id);
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
            marker.isVanilla = false;
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
                case WTTQuestZone q:
                    Data.wttQuestZones ??= new List<WTTQuestZone>();
                    Data.wttQuestZones.Add(q);
                    break;
                case WTTStaticObject w:
                    Data.wttStaticObjects ??= new List<WTTStaticObject>();
                    Data.wttStaticObjects.Add(w);
                    break;
                case InteractiveObject io:
                    Data.interactiveObjects ??= new List<InteractiveObject>();
                    Data.interactiveObjects.Add(io);
                    break;
                case ExtractZone ez:
                    Data.extractZones ??= new List<ExtractZone>();
                    Data.extractZones.Add(ez);
                    break;
                case BotSpawnPoint bp:
                    Data.botSpawnPoints ??= new List<BotSpawnPoint>();
                    Data.botSpawnPoints.Add(bp);
                    break;
                case BotSpawnZone bz:
                    Data.botSpawnZones ??= new List<BotSpawnZone>();
                    Data.botSpawnZones.Add(bz);
                    break;
                case LightZone lz:
                    Data.lightZones ??= new List<LightZone>();
                    Data.lightZones.Add(lz);
                    break;
                case TriggerZone tz:
                    Data.triggerZones ??= new List<TriggerZone>();
                    Data.triggerZones.Add(tz);
                    break;
                case OcclusionRepairVolume orv:
                    Data.occlusionRepairVolumes ??= new List<OcclusionRepairVolume>();
                    Data.occlusionRepairVolumes.Add(orv);
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
                if (m != null && !IsVanilla(m))
                    m.group = group ?? "";
            }
            IsDirty = true;
        }

        public void DeleteSelection()
        {
            if (Data == null)
                return;

            var toDelete = SelectedIds.Where(id => !IsVanilla(FindById(id))).ToList();
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
                    case WTTQuestZone q: Data.wttQuestZones.Remove(q); break;
                    case WTTStaticObject w: Data.wttStaticObjects.Remove(w); break;
                    case InteractiveObject io: Data.interactiveObjects.Remove(io); break;
                case ExtractZone ez: Data.extractZones.Remove(ez); break;
                case BotSpawnPoint bp: Data.botSpawnPoints.Remove(bp); break;
                case BotSpawnZone bz: Data.botSpawnZones.Remove(bz); break;
                case LightZone lz: Data.lightZones.Remove(lz); break;
                case TriggerZone tz: Data.triggerZones.Remove(tz); break;
                case OcclusionRepairVolume orv: Data.occlusionRepairVolumes.Remove(orv); break;
                }
            }
            ClearSelection();
            IsDirty = true;
        }

        public void DuplicateSelection()
        {
            if (Data == null)
                return;

            var originals = SelectedIds.Select(FindById).Where(m => m != null && !IsVanilla(m)).ToList();
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
                    case WTTQuestZone q:
                        copy = JsonConvert.DeserializeObject<WTTQuestZone>(json);
                        Data.wttQuestZones.Add((WTTQuestZone)copy);
                        break;
                    case WTTStaticObject w:
                        copy = JsonConvert.DeserializeObject<WTTStaticObject>(json);
                        Data.wttStaticObjects.Add((WTTStaticObject)copy);
                        break;
                    case InteractiveObject io:
                        copy = JsonConvert.DeserializeObject<InteractiveObject>(json);
                        Data.interactiveObjects.Add((InteractiveObject)copy);
                        break;
                    case ExtractZone ez:
                        copy = JsonConvert.DeserializeObject<ExtractZone>(json);
                        Data.extractZones.Add((ExtractZone)copy);
                        break;
                    case BotSpawnPoint bp:
                        copy = JsonConvert.DeserializeObject<BotSpawnPoint>(json);
                        Data.botSpawnPoints.Add((BotSpawnPoint)copy);
                        break;
                    case BotSpawnZone bz:
                        copy = JsonConvert.DeserializeObject<BotSpawnZone>(json);
                        Data.botSpawnZones.Add((BotSpawnZone)copy);
                        break;
                    case LightZone lz:
                        copy = JsonConvert.DeserializeObject<LightZone>(json);
                        Data.lightZones.Add((LightZone)copy);
                        break;
                    case TriggerZone tz:
                        copy = JsonConvert.DeserializeObject<TriggerZone>(json);
                        Data.triggerZones.Add((TriggerZone)copy);
                        break;
                    case OcclusionRepairVolume orv:
                        copy = JsonConvert.DeserializeObject<OcclusionRepairVolume>(json);
                        Data.occlusionRepairVolumes.Add((OcclusionRepairVolume)copy);
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

        public WTTQuestZone CreateWTTQuestZone(Vector3 position, Vector3 rotation)
        {
            Data.wttQuestZones ??= new List<WTTQuestZone>();
            var marker = new WTTQuestZone
            {
                name = "wtt_quest_zone",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(rotation),
                scale = TransformData.FromVector3(Vector3.one),
                zoneId = "quest_zone_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                zoneName = "Quest Zone",
                zoneType = "placeitem"
            };
            Data.wttQuestZones.Add(marker);
            IsDirty = true;
            return marker;
        }

        public WTTStaticObject CreateWTTStaticObject(Vector3 position, Vector3 rotation)
        {
            Data.wttStaticObjects ??= new List<WTTStaticObject>();
            var marker = new WTTStaticObject
            {
                name = "wtt_static_object",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(rotation),
                scale = TransformData.FromVector3(Vector3.one),
                spawnType = "bundle",
                bundleName = "",
                prefabName = ""
            };
            Data.wttStaticObjects.Add(marker);
            IsDirty = true;
            return marker;
        }

        public InteractiveObject CreateInteractiveObject(Vector3 position, Vector3 rotation)
        {
            Data.interactiveObjects ??= new List<InteractiveObject>();
            var marker = new InteractiveObject
            {
                name = "interactive_object",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(rotation),
                scale = TransformData.FromVector3(Vector3.one),
                keyId = "",
                containerId = ""
            };
            Data.interactiveObjects.Add(marker);
            IsDirty = true;
            return marker;
        }

        public ExtractZone CreateExtractZone(Vector3 position)
        {
            Data.extractZones ??= new List<ExtractZone>();
            var marker = new ExtractZone
            {
                name = "extract_zone",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                scale = new TransformData { x = 1f, y = 1f, z = 1f },
                radius = 1f,
                shape = ZoneShape.Box,
                exitName = "Custom Extract",
                exfiltrationTime = 5f,
                exfiltrationType = "Individual",
                spawnChance = 100f
            };
            Data.extractZones.Add(marker);
            IsDirty = true;
            return marker;
        }

        public BotSpawnPoint CreateBotSpawnPoint(Vector3 position)
        {
            Data.botSpawnPoints ??= new List<BotSpawnPoint>();
            var marker = new BotSpawnPoint
            {
                name = "bot_spawn_point",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                radius = 1f,
                side = BotSpawnSide.Savage,
                category = BotSpawnCategory.Bot,
                preset = BotSpawnPreset.Scav,
                wildSpawnType = "assault",
                spawnChance = 100f,
                delayToCanSpawnSec = 4f
            };
            Data.botSpawnPoints.Add(marker);
            IsDirty = true;
            return marker;
        }

        public BotSpawnZone CreateBotSpawnZone(Vector3 position)
        {
            Data.botSpawnZones ??= new List<BotSpawnZone>();
            var marker = new BotSpawnZone
            {
                name = "bot_spawn_zone",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                scale = new TransformData { x = 1f, y = 1f, z = 1f },
                radius = 5f,
                shape = ZoneShape.Sphere,
                side = BotSpawnSide.Savage,
                category = BotSpawnCategory.Bot,
                preset = BotSpawnPreset.Scav,
                wildSpawnType = "assault",
                spawnCount = 3,
                spawnChance = 100f,
                delayToCanSpawnSec = 4f
            };
            Data.botSpawnZones.Add(marker);
            IsDirty = true;
            return marker;
        }

        public LightZone CreateLightZone(Vector3 position)
        {
            Data.lightZones ??= new List<LightZone>();
            var marker = new LightZone
            {
                name = "light_zone",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                color = new LightColorData { r = 1f, g = 1f, b = 1f, a = 1f },
                intensity = 1f,
                range = 10f,
                spotAngle = 30f,
                lightType = "Point",
                spawnChance = 100f,
                shadows = "Soft",
                shadowStrength = 1f,
                shadowBias = 0.05f,
                shadowNormalBias = 0.4f
            };
            Data.lightZones.Add(marker);
            IsDirty = true;
            return marker;
        }

        public TriggerZone CreateTriggerZone(Vector3 position)
        {
            Data.triggerZones ??= new List<TriggerZone>();
            var marker = new TriggerZone
            {
                name = "trigger_zone",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                scale = new TransformData { x = 1f, y = 1f, z = 1f },
                shape = ZoneShape.Sphere
            };
            Data.triggerZones.Add(marker);
            IsDirty = true;
            return marker;
        }

        public OcclusionRepairVolume CreateOcclusionRepairVolume(Vector3 position)
        {
            Data.occlusionRepairVolumes ??= new List<OcclusionRepairVolume>();
            var marker = new OcclusionRepairVolume
            {
                name = "occlusion_repair_volume",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(Vector3.zero),
                scale = new TransformData { x = 10f, y = 10f, z = 10f },
                shape = ZoneShape.Box
            };
            Data.occlusionRepairVolumes.Add(marker);
            IsDirty = true;
            return marker;
        }

        public void DeleteSelected()
        {
            if (Selected == null || Data == null || IsVanilla(Selected))
                return;

            switch (Selected)
            {
                case LooseLootSpawn s: Data.lootSpawns.Remove(s); break;
                case LootZone z: Data.lootZones.Remove(z); break;
                case StaticObject o: Data.objects.Remove(o); break;
                case WTTQuestZone q: Data.wttQuestZones.Remove(q); break;
                case WTTStaticObject w: Data.wttStaticObjects.Remove(w); break;
                case InteractiveObject io: Data.interactiveObjects.Remove(io); break;
                case ExtractZone ez: Data.extractZones.Remove(ez); break;
                case BotSpawnPoint bp: Data.botSpawnPoints.Remove(bp); break;
                case BotSpawnZone bz: Data.botSpawnZones.Remove(bz); break;
                case LightZone lz: Data.lightZones.Remove(lz); break;
                case TriggerZone tz: Data.triggerZones.Remove(tz); break;
                case OcclusionRepairVolume orv: Data.occlusionRepairVolumes.Remove(orv); break;
            }

            Selected = null;
            IsDirty = true;
        }

        public MarkerBase DuplicateSelected()
        {
            if (Selected == null || IsVanilla(Selected))
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
                case WTTQuestZone q:
                    copy = JsonConvert.DeserializeObject<WTTQuestZone>(json);
                    Data.wttQuestZones.Add((WTTQuestZone)copy);
                    break;
                case WTTStaticObject w:
                    copy = JsonConvert.DeserializeObject<WTTStaticObject>(json);
                    Data.wttStaticObjects.Add((WTTStaticObject)copy);
                    break;
                case InteractiveObject io:
                    copy = JsonConvert.DeserializeObject<InteractiveObject>(json);
                    Data.interactiveObjects.Add((InteractiveObject)copy);
                    break;
                case ExtractZone ez:
                    copy = JsonConvert.DeserializeObject<ExtractZone>(json);
                    Data.extractZones.Add((ExtractZone)copy);
                    break;
                case BotSpawnPoint bp:
                    copy = JsonConvert.DeserializeObject<BotSpawnPoint>(json);
                    Data.botSpawnPoints.Add((BotSpawnPoint)copy);
                    break;
                case BotSpawnZone bz:
                    copy = JsonConvert.DeserializeObject<BotSpawnZone>(json);
                    Data.botSpawnZones.Add((BotSpawnZone)copy);
                    break;
                case LightZone lz:
                    copy = JsonConvert.DeserializeObject<LightZone>(json);
                    Data.lightZones.Add((LightZone)copy);
                    break;
                case OcclusionRepairVolume orv:
                    copy = JsonConvert.DeserializeObject<OcclusionRepairVolume>(json);
                    Data.occlusionRepairVolumes.Add((OcclusionRepairVolume)copy);
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
            if (Selected == null || IsVanilla(Selected))
                return;

            Selected.position = TransformData.FromVector3(Selected.position.ToVector3() + delta);
            IsDirty = true;
        }

        public void RotateSelected(Vector3 delta)
        {
            if (Selected == null || IsVanilla(Selected))
                return;

            Selected.rotation = TransformData.FromVector3(Selected.rotation.ToVector3() + delta);
            IsDirty = true;
        }

        public void ChangeRadius(float delta)
        {
            if (Selected is LootZone zone && !IsVanilla(zone))
            {
                zone.radius = Mathf.Max(0.1f, zone.radius + delta);
                IsDirty = true;
            }
        }

        public void SnapSelectedToGround()
        {
            if (Selected == null || IsVanilla(Selected))
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
