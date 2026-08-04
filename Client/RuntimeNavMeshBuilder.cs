using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace MapLootEditorLite.Client
{
    public class RuntimeNavMeshBuilder : MonoBehaviour
    {
        public static RuntimeNavMeshBuilder Instance { get; private set; }

        private readonly List<PackData> _packs = new List<PackData>();
        private bool _loaded;
        private bool _injected;
        private Mesh _originalNavMeshMesh;
        private NavMeshData _builtNavMeshData;
        private NavMeshDataInstance _navMeshDataInstance;
        private bool _hasCustomNavMesh;
        private readonly List<Mesh> _areaMeshes = new List<Mesh>();

        private void Awake()
        {
            Instance = this;
            LoadPacks();

            try
            {
                var harmony = new Harmony("com.shane.mapeditorlite.navmesh");
                harmony.Patch(AccessTools.Method(typeof(BotsController), nameof(BotsController.Init)),
                    postfix: new HarmonyMethod(typeof(RuntimeNavMeshBuilder), nameof(OnBotsControllerInit)));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Failed to patch BotsController.Init for NavMesh injection: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        [HarmonyPriority(Priority.First)]
        private static void OnBotsControllerInit(BotsController __instance)
        {
            Instance?.Inject();
        }

        private void LoadPacks()
        {
            if (_loaded)
                return;
            _loaded = true;

            var directories = new List<string>();
            if (!string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) && Directory.Exists(Plugin.ServerModPacksDirectory))
                directories.Add(Plugin.ServerModPacksDirectory);
            if (!string.IsNullOrEmpty(Plugin.ServerModExportsDirectory) && Directory.Exists(Plugin.ServerModExportsDirectory))
                directories.Add(Plugin.ServerModExportsDirectory);

            foreach (var dir in directories)
            {
                foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var trimmed = json.TrimStart();
                        if (trimmed.Length > 0 && trimmed[0] == '[')
                            continue;
                        var pack = JsonConvert.DeserializeObject<PackData>(json, PackData.InvariantSettings);
                        if (pack?.maps != null)
                            _packs.Add(pack);
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Failed to load pack {file}: {ex.Message}");
                    }
                }
            }
        }

        private void Inject()
        {
            if (_injected)
                return;
            _injected = true;

            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                Plugin.Log.LogWarning("Cannot inject NavMesh: GameWorld not available.");
                return;
            }

            var mapId = gameWorld.LocationId.ToLower();
            var areas = new List<NavMeshArea>();
            foreach (var pack in _packs)
            {
                if (pack.maps == null)
                    continue;
                if (pack.maps.TryGetValue(mapId, out var mapData) && mapData.navMeshAreas != null)
                    areas.AddRange(mapData.navMeshAreas);
            }

            if (areas.Count == 0)
                return;

            BuildAndAddNavMesh(areas);
            SetupDoorObstacles();
        }

        private void BuildAndAddNavMesh(List<NavMeshArea> areas)
        {
            if (areas == null || areas.Count == 0)
                return;

            // Clean up any temporary area meshes from a previous build.
            foreach (var m in _areaMeshes)
            {
                if (m != null)
                    Destroy(m);
            }
            _areaMeshes.Clear();

            // Remove the previously added custom NavMesh tile, but leave the original baked data intact.
            if (_hasCustomNavMesh)
            {
                NavMesh.RemoveNavMeshData(_navMeshDataInstance);
                _hasCustomNavMesh = false;
            }

            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(areas[0].position.ToVector3(), Vector3.zero);

            foreach (var area in areas)
            {
                var pos = area.position.ToVector3();
                var scale = area.scale ?? new TransformData { x = 2f, y = 0.1f, z = 2f };

                var areaScale = new Vector3(scale.x, 1f, scale.z);
                var areaMesh = CreateAreaMesh(area, pos, area.rotation.ToQuaternion(), areaScale);
                _areaMeshes.Add(areaMesh);

                var source = new NavMeshBuildSource
                {
                    transform = Matrix4x4.TRS(pos, area.rotation.ToQuaternion(), areaScale),
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = areaMesh,
                    area = area.area
                };
                sources.Add(source);

                bounds.Encapsulate(new Bounds(pos, new Vector3(scale.x, 0.1f, scale.z)));
            }

            var settings = NavMesh.GetSettingsByIndex(0);
            bounds.Expand(2f);
            _builtNavMeshData = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (_builtNavMeshData != null)
            {
                _navMeshDataInstance = NavMesh.AddNavMeshData(_builtNavMeshData);
                _hasCustomNavMesh = true;
                Plugin.Log.LogInfo($"Added custom NavMesh tile with {areas.Count} NavMeshArea markers for {Singleton<GameWorld>.Instance.LocationId}.");
            }
            else
            {
                Plugin.Log.LogError("Failed to build NavMeshData from NavMeshArea markers.");
            }
        }

        private Mesh CreateAreaMesh(NavMeshArea area, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var mesh = new Mesh();
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Sample the original baked floor height directly under the marker so the added area is flat
            // and flush with the actual ground. A ramp between two different floor heights can produce
            // one-way navmesh movement, so we keep the patch horizontal.
            float floorY = 0f;
            if (NavMesh.SamplePosition(position + Vector3.up, out var hit, 2f, NavMesh.AllAreas))
                floorY = hit.position.y - position.y;

            switch (area.shape)
            {
                case NavMeshAreaShape.Capsule:
                    const int segments = 16;
                    vertices.Add(new Vector3(0f, floorY, 0f));
                    for (int i = 0; i < segments; i++)
                    {
                        float angle = i * Mathf.PI * 2f / segments;
                        float x = Mathf.Cos(angle) * 0.5f;
                        float z = Mathf.Sin(angle) * 0.5f;
                        vertices.Add(new Vector3(x, floorY, z));
                    }
                    for (int i = 0; i < segments - 1; i++)
                    {
                        triangles.Add(0);
                        triangles.Add(i + 1);
                        triangles.Add(i + 2);
                    }
                    triangles.Add(0);
                    triangles.Add(segments);
                    triangles.Add(1);
                    break;

                case NavMeshAreaShape.Mesh:
                default:
                    vertices.Add(new Vector3(-0.5f, floorY, -0.5f));
                    vertices.Add(new Vector3(-0.5f, floorY, 0.5f));
                    vertices.Add(new Vector3(0.5f, floorY, 0.5f));
                    vertices.Add(new Vector3(0.5f, floorY, -0.5f));
                    triangles.Add(0); triangles.Add(1); triangles.Add(2);
                    triangles.Add(0); triangles.Add(2); triangles.Add(3);
                    break;
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetupDoorObstacles()
        {
            foreach (var door in FindObjectsOfType<Door>())
            {
                if (door == null)
                    continue;

                if (door.GetComponent<NavMeshDoorLink>() != null)
                    continue;

                var obstacles = new List<NavMeshObstacle>();
                foreach (var col in door.GetComponentsInChildren<Collider>(true))
                {
                    if (col == null || col.isTrigger)
                        continue;
                    if (col.GetComponent<NavMeshObstacle>() != null)
                        continue;

                    var obstacle = col.gameObject.AddComponent<NavMeshObstacle>();
                    obstacle.shape = NavMeshObstacleShape.Box;
                    obstacle.size = col.bounds.size;
                    obstacle.carving = true;
                    obstacle.enabled = door.DoorState != EDoorState.Open;
                    obstacles.Add(obstacle);
                }

                if (obstacles.Count == 0)
                    continue;

                door.OnDoorStateChanged += (d, prev, next) =>
                {
                    var open = next == EDoorState.Open;
                    foreach (var o in obstacles)
                    {
                        if (o != null)
                            o.enabled = !open;
                    }
                };
            }
        }
    }
}
