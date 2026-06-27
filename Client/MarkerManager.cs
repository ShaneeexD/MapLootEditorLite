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
        public MarkerBase Selected { get; set; }
        public bool IsDirty { get; set; }

        public void LoadMap(string mapId)
        {
            Data = JsonStorage.Load(mapId);
            Selected = null;
            IsDirty = false;
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

            foreach (var m in Data.lootSpawns)
                yield return m;
            foreach (var m in Data.lootZones)
                yield return m;
            foreach (var m in Data.objects)
                yield return m;
        }

        public MarkerBase FindById(string id)
        {
            return GetAllMarkers().FirstOrDefault(m => m.id == id);
        }

        public LooseLootSpawn CreateLootSpawn(Vector3 position, Vector3 rotation)
        {
            Data.lootSpawns ??= new List<LooseLootSpawn>();
            var marker = new LooseLootSpawn
            {
                name = "loot_spawn",
                position = TransformData.FromVector3(position),
                rotation = TransformData.FromVector3(rotation),
                itemTpls = new List<string> { "544fb45d4bdc2dee738b4568" }
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
                itemTpls = new List<string> { "544fb45d4bdc2dee738b4568" }
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
