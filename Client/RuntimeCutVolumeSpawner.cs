using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapLootEditorLite.Client
{
    public class RuntimeCutVolumeSpawner : MonoBehaviour
    {
        public static RuntimeCutVolumeSpawner Instance { get; private set; }

        private readonly List<PackData> _packs = new List<PackData>();
        private readonly List<CutState> _cuts = new List<CutState>();
        private GameWorld _currentWorld;
        private string _currentMapId;

        private class CutState
        {
            public GameObject Target;
            public MeshFilter MeshFilter;
            public MeshCollider MeshCollider;
            public Mesh OriginalMesh;
            public Mesh OriginalColliderMesh;
            public Mesh GeneratedMesh;
            public Mesh CutMesh;
            public Collider DisabledCollider;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LoadPacks();
        }

        public void ResetState()
        {
            RestoreAllCuts();
            _currentWorld = null;
            _currentMapId = null;
        }

        private void Update()
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null || _currentWorld != world)
            {
                if (_currentWorld != null)
                {
                    Plugin.Log.LogInfo("GameWorld changed, restoring cut meshes.");
                    RestoreAllCuts();
                }
                _currentWorld = world;
                _currentMapId = null;
            }

            if (world == null)
                return;

            var mapId = world.LocationId;
            if (string.IsNullOrEmpty(mapId) && world.MainPlayer != null)
                mapId = world.MainPlayer.Location;

            if (!string.IsNullOrEmpty(mapId) && mapId != _currentMapId)
            {
                _currentMapId = mapId;
                RestoreAllCuts();
                StartCoroutine(ApplyCutVolumesForMap(mapId));
            }
        }

        private void LoadPacks()
        {
            if (string.IsNullOrEmpty(Plugin.ServerModPacksDirectory) || !Directory.Exists(Plugin.ServerModPacksDirectory))
            {
                Plugin.Log.LogWarning("Cut volumes disabled: no pack directory found.");
                return;
            }

            foreach (var file in Directory.GetFiles(Plugin.ServerModPacksDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var pack = JsonConvert.DeserializeObject<PackData>(json);
                    if (pack?.maps != null)
                    {
                        _packs.Add(pack);
                        Plugin.Log.LogInfo($"Loaded pack '{pack.name}' for cut volumes.");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Failed to load pack {file}: {ex.Message}");
                }
            }
        }

        private IEnumerator ApplyCutVolumesForMap(string mapId)
        {
            yield return new WaitForSecondsRealtime(2f);

            var volumes = new List<CutVolume>();
            foreach (var pack in _packs)
            {
                if (pack.maps.TryGetValue(mapId, out var map))
                    volumes.AddRange(map.cutVolumes ?? new List<CutVolume>());
            }

            if (volumes.Count == 0)
                yield break;

            Plugin.Log.LogInfo($"Applying {volumes.Count} cut volumes for map {mapId}.");
            foreach (var volume in volumes)
            {
                if (volume == null) continue;
                yield return StartCoroutine(ApplyCut(volume));
                yield return null;
            }
            Plugin.Log.LogInfo("Cut volumes applied.");
        }

        private IEnumerator ApplyCut(CutVolume volume)
        {
            if (string.IsNullOrEmpty(volume.sourceObjectName))
            {
                Plugin.Log.LogWarning($"Cut volume '{volume.name}' has no source object.");
                yield break;
            }

            var target = FindSourceObject(volume.sourceObjectName, volume.sourceObjectPosition.ToVector3());
            if (target == null)
            {
                Plugin.Log.LogWarning($"Cut volume '{volume.name}' could not find source object '{volume.sourceObjectName}'.");
                yield break;
            }

            Bounds volumeAabb = GetVolumeAabb(volume);
            var visualProcessed = new HashSet<GameObject>();
            var physicsProcessed = new HashSet<GameObject>();
            int visualCuts = 0;
            int physicsCuts = 0;

            // 1. Visual meshes
            foreach (var mf in target.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject;
                if (visualProcessed.Contains(go) || mf.sharedMesh == null)
                    continue;

                if (!Intersects(TransformBounds(mf.sharedMesh.bounds, mf.transform), volumeAabb))
                    continue;

                Mesh originalMesh = mf.sharedMesh;
                Mesh newMesh = null;
                Exception error = null;
                yield return CutMeshSafe(originalMesh, mf.transform, volume, (m, ex) => { newMesh = m; error = ex; });

                if (newMesh == null)
                {
                    Plugin.Log.LogWarning($"Could not cut mesh on '{go.name}': {(error != null ? error.Message : "unknown failure")}");
                    continue;
                }

                mf.sharedMesh = newMesh;

                var mc = go.GetComponent<MeshCollider>();
                if (mc != null && (mc.sharedMesh == originalMesh || mc.sharedMesh == null))
                {
                    mc.enabled = false;
                    mc.sharedMesh = newMesh;
                    mc.enabled = true;
                    physicsProcessed.Add(go);
                }

                _cuts.Add(new CutState
                {
                    Target = go,
                    MeshFilter = mf,
                    OriginalMesh = originalMesh,
                    CutMesh = newMesh
                });
                visualProcessed.Add(go);
                visualCuts++;
            }

            // 2. Existing mesh colliders not already updated
            foreach (var mc in target.GetComponentsInChildren<MeshCollider>(true))
            {
                var go = mc.gameObject;
                if (physicsProcessed.Contains(go) || mc.sharedMesh == null)
                    continue;

                if (!mc.bounds.Intersects(volumeAabb))
                    continue;

                Mesh originalMesh = mc.sharedMesh;
                Mesh newMesh = null;
                Exception error = null;
                yield return CutMeshSafe(originalMesh, go.transform, volume, (m, ex) => { newMesh = m; error = ex; });

                if (newMesh == null)
                {
                    Plugin.Log.LogWarning($"Could not cut mesh collider on '{go.name}': {(error != null ? error.Message : "unknown failure")}");
                    continue;
                }

                mc.enabled = false;
                mc.sharedMesh = newMesh;
                mc.enabled = true;

                _cuts.Add(new CutState
                {
                    Target = go,
                    MeshCollider = mc,
                    OriginalColliderMesh = originalMesh,
                    CutMesh = newMesh
                });
                physicsProcessed.Add(go);
                physicsCuts++;
            }

            // 3. Primitive colliders - turn box colliders into cut mesh colliders
            foreach (var box in target.GetComponentsInChildren<BoxCollider>(true))
            {
                var go = box.gameObject;
                if (physicsProcessed.Contains(go) || box.isTrigger)
                    continue;

                if (!box.bounds.Intersects(volumeAabb))
                    continue;

                Mesh generated;
                try { generated = CreateBoxMesh(box.size, box.center); }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Could not generate box mesh for '{go.name}': {ex.Message}");
                    continue;
                }

                Mesh newMesh = null;
                Exception error = null;
                yield return CutMeshSafe(generated, go.transform, volume, (m, ex) => { newMesh = m; error = ex; });

                if (newMesh == null)
                {
                    Plugin.Log.LogWarning($"Could not cut box collider on '{go.name}': {(error != null ? error.Message : "unknown failure")}");
                    continue;
                }

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = newMesh;
                mc.convex = false;
                box.enabled = false;

                _cuts.Add(new CutState
                {
                    Target = go,
                    MeshCollider = mc,
                    GeneratedMesh = generated,
                    CutMesh = newMesh,
                    DisabledCollider = box
                });
                physicsProcessed.Add(go);
                physicsCuts++;
            }

            Plugin.Log.LogInfo($"Cut volume '{volume.name}' cut {visualCuts} visual mesh(s) and {physicsCuts} collider mesh(s) on '{target.name}'.");
        }

        private static IEnumerator CutMeshSafe(Mesh mesh, Transform meshTransform, CutVolume volume, Action<Mesh, Exception> onComplete)
        {
            if (mesh.isReadable)
            {
                try
                {
                    var result = CutMeshCpu(mesh, meshTransform, volume);
                    onComplete(result, null);
                    yield break;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogInfo($"CPU mesh cut failed for '{mesh.name}', trying GPU readback: {ex.Message}");
                }
            }

            yield return CutMeshGpuReadback(mesh, meshTransform, volume, onComplete);
        }

        private static Mesh CutMeshCpu(Mesh mesh, Transform meshTransform, CutVolume volume)
        {
            var dataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            try
            {
                var data = dataArray[0];
                int vCount = data.vertexCount;

                var vertices = new Vector3[vCount];
                using (var na = new NativeArray<Vector3>(vCount, Allocator.Temp))
                {
                    data.GetVertices(na);
                    na.CopyTo(vertices);
                }

                var newMesh = new Mesh();
                newMesh.name = mesh.name + "_cut";
                newMesh.indexFormat = mesh.indexFormat;
                newMesh.SetVertices(vertices);

                if (data.HasVertexAttribute(VertexAttribute.Normal))
                {
                    using (var na = new NativeArray<Vector3>(vCount, Allocator.Temp))
                    {
                        data.GetNormals(na);
                        newMesh.SetNormals(na);
                    }
                }

                if (data.HasVertexAttribute(VertexAttribute.Tangent))
                {
                    using (var na = new NativeArray<Vector4>(vCount, Allocator.Temp))
                    {
                        data.GetTangents(na);
                        newMesh.SetTangents(na);
                    }
                }

                if (data.HasVertexAttribute(VertexAttribute.Color))
                {
                    using (var na = new NativeArray<Color>(vCount, Allocator.Temp))
                    {
                        data.GetColors(na);
                        newMesh.SetColors(na);
                    }
                }

                if (data.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    using (var na = new NativeArray<Vector2>(vCount, Allocator.Temp))
                    {
                        data.GetUVs(0, na);
                        newMesh.SetUVs(0, na);
                    }
                }

                if (data.HasVertexAttribute(VertexAttribute.TexCoord1))
                {
                    using (var na = new NativeArray<Vector2>(vCount, Allocator.Temp))
                    {
                        data.GetUVs(1, na);
                        newMesh.SetUVs(1, na);
                    }
                }

                var worldMatrix = meshTransform.localToWorldMatrix;
                int submeshCount = data.subMeshCount;
                var submeshTriangles = new List<int>[submeshCount];
                bool is16Bit = data.indexFormat == IndexFormat.UInt16;

                for (int s = 0; s < submeshCount; s++)
                {
                    var sub = data.GetSubMesh(s);
                    int start = sub.indexStart;
                    int count = sub.indexCount;
                    var list = new List<int>(count);

                    if (is16Bit)
                    {
                        var indices = data.GetIndexData<ushort>();
                        for (int i = start; i < start + count; i++)
                            list.Add(indices[i]);
                    }
                    else
                    {
                        var indices = data.GetIndexData<int>();
                        for (int i = start; i < start + count; i++)
                            list.Add(indices[i]);
                    }
                    submeshTriangles[s] = list;
                }

                newMesh.subMeshCount = submeshCount;
                for (int s = 0; s < submeshCount; s++)
                {
                    var tris = submeshTriangles[s];
                    var keep = new List<int>(tris.Count);
                    for (int i = 0; i < tris.Count; i += 3)
                    {
                        int i0 = tris[i];
                        int i1 = tris[i + 1];
                        int i2 = tris[i + 2];

                        var v0 = worldMatrix.MultiplyPoint3x4(vertices[i0]);
                        var v1 = worldMatrix.MultiplyPoint3x4(vertices[i1]);
                        var v2 = worldMatrix.MultiplyPoint3x4(vertices[i2]);
                        var centroid = (v0 + v1 + v2) / 3f;

                        bool inside = IsPointInVolume(centroid, volume);
                        bool remove = volume.invert ? !inside : inside;
                        if (!remove)
                        {
                            keep.Add(i0);
                            keep.Add(i1);
                            keep.Add(i2);
                        }
                    }
                    newMesh.SetTriangles(keep, s);
                }

                return newMesh;
            }
            finally
            {
                dataArray.Dispose();
            }
        }

        private static IEnumerator CutMeshGpuReadback(Mesh mesh, Transform meshTransform, CutVolume volume, Action<Mesh, Exception> onComplete)
        {
            int vBufferCount = 0;
            try { vBufferCount = mesh.vertexBufferCount; } catch { }
            if (vBufferCount == 0)
            {
                onComplete(null, new InvalidOperationException("No vertex buffer"));
                yield break;
            }

            GraphicsBuffer vBuffer = null;
            GraphicsBuffer iBuffer = null;
            try
            {
                vBuffer = mesh.GetVertexBuffer(0);
                iBuffer = mesh.GetIndexBuffer();
            }
            catch (Exception ex)
            {
                onComplete(null, ex);
                yield break;
            }

            if (vBuffer == null || iBuffer == null)
            {
                onComplete(null, new InvalidOperationException("Could not acquire GPU vertex/index buffers"));
                yield break;
            }

            AsyncGPUReadbackRequest vRequest = AsyncGPUReadback.Request(vBuffer);
            AsyncGPUReadbackRequest iRequest = AsyncGPUReadback.Request(iBuffer);

            while (!vRequest.done || !iRequest.done)
                yield return null;

            if (vRequest.hasError || iRequest.hasError)
            {
                onComplete(null, new InvalidOperationException("GPU readback failed"));
                yield break;
            }

            NativeArray<byte> vBytes;
            NativeArray<byte> iBytes;
            try
            {
                vBytes = vRequest.GetData<byte>();
                iBytes = iRequest.GetData<byte>();
            }
            catch (Exception ex)
            {
                onComplete(null, ex);
                yield break;
            }

            var attrs = mesh.GetVertexAttributes();
            VertexAttributeDescriptor posAttr = default;
            int vertexStride = mesh.GetVertexBufferStride(0);
            bool foundPos = false;
            foreach (var attr in attrs)
            {
                if (attr.attribute == VertexAttribute.Position)
                {
                    posAttr = attr;
                    foundPos = true;
                    break;
                }
            }

            if (!foundPos || posAttr.format != VertexAttributeFormat.Float32 || posAttr.dimension != 3)
            {
                onComplete(null, new NotSupportedException($"Unsupported position format: {posAttr.format} dim {posAttr.dimension}"));
                yield break;
            }

            int posOffset = mesh.GetVertexAttributeOffset(VertexAttribute.Position);
            int vCount = mesh.vertexCount;
            byte[] vBytesArr = vBytes.ToArray();
            var vertices = new Vector3[vCount];
            for (int i = 0; i < vCount; i++)
            {
                int idx = i * vertexStride + posOffset;
                vertices[i] = new Vector3(
                    BitConverter.ToSingle(vBytesArr, idx),
                    BitConverter.ToSingle(vBytesArr, idx + 4),
                    BitConverter.ToSingle(vBytesArr, idx + 8));
            }

            bool is16Bit = mesh.indexFormat == IndexFormat.UInt16;
            byte[] iBytesArr = iBytes.ToArray();
            int submeshCount = mesh.subMeshCount;
            var submeshTriangles = new List<int>[submeshCount];

            for (int s = 0; s < submeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                int start = sub.indexStart;
                int count = sub.indexCount;
                var list = new List<int>(count);
                for (int i = start; i < start + count; i++)
                {
                    if (is16Bit)
                        list.Add(BitConverter.ToUInt16(iBytesArr, i * 2));
                    else
                        list.Add(BitConverter.ToInt32(iBytesArr, i * 4));
                }
                submeshTriangles[s] = list;
            }

            var newMesh = new Mesh();
            newMesh.name = mesh.name + "_cut";
            newMesh.indexFormat = mesh.indexFormat;

            newMesh.SetVertexBufferParams(vCount, attrs);
            newMesh.SetVertexBufferData(vBytes, 0, 0, vBytes.Length, 0);

            int totalIndices = is16Bit ? iBytes.Length / 2 : iBytes.Length / 4;
            newMesh.SetIndexBufferParams(totalIndices, mesh.indexFormat);
            newMesh.SetIndexBufferData(iBytes, 0, 0, iBytes.Length);

            newMesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                newMesh.SetSubMesh(s, new SubMeshDescriptor(sub.indexStart, sub.indexCount, sub.topology));
            }

            var worldMatrix = meshTransform.localToWorldMatrix;
            newMesh.subMeshCount = submeshCount;
            for (int s = 0; s < submeshCount; s++)
            {
                var tris = submeshTriangles[s];
                var keep = new List<int>(tris.Count);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int i0 = tris[i];
                    int i1 = tris[i + 1];
                    int i2 = tris[i + 2];

                    var v0 = worldMatrix.MultiplyPoint3x4(vertices[i0]);
                    var v1 = worldMatrix.MultiplyPoint3x4(vertices[i1]);
                    var v2 = worldMatrix.MultiplyPoint3x4(vertices[i2]);
                    var centroid = (v0 + v1 + v2) / 3f;

                    bool inside = IsPointInVolume(centroid, volume);
                    bool remove = volume.invert ? !inside : inside;
                    if (!remove)
                    {
                        keep.Add(i0);
                        keep.Add(i1);
                        keep.Add(i2);
                    }
                }
                newMesh.SetTriangles(keep, s);
            }

            onComplete(newMesh, null);
        }

        private static Mesh CreateBoxMesh(Vector3 size, Vector3 center)
        {
            var mesh = new Mesh();
            Vector3 h = size * 0.5f;
            Vector3 c = center;
            Vector3[] verts = new Vector3[8]
            {
                c + new Vector3(-h.x, -h.y, -h.z),
                c + new Vector3( h.x, -h.y, -h.z),
                c + new Vector3( h.x,  h.y, -h.z),
                c + new Vector3(-h.x,  h.y, -h.z),
                c + new Vector3(-h.x, -h.y,  h.z),
                c + new Vector3( h.x, -h.y,  h.z),
                c + new Vector3( h.x,  h.y,  h.z),
                c + new Vector3(-h.x,  h.y,  h.z)
            };
            mesh.vertices = verts;
            mesh.triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                0, 4, 7, 0, 7, 3,
                1, 2, 6, 1, 6, 5
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        private static bool IsPointInVolume(Vector3 worldPos, CutVolume volume)
        {
            var center = volume.position.ToVector3();
            var rotation = volume.rotation.ToQuaternion();
            var scale = volume.scale.ToVector3();
            var localPos = Quaternion.Inverse(rotation) * (worldPos - center);

            switch (volume.shape)
            {
                case ZoneShape.Box:
                    return Mathf.Abs(localPos.x) <= scale.x / 2f &&
                           Mathf.Abs(localPos.y) <= scale.y / 2f &&
                           Mathf.Abs(localPos.z) <= scale.z / 2f;
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    float radius = Mathf.Max(scale.x, scale.z) / 2f;
                    float distXZ = Mathf.Sqrt(localPos.x * localPos.x + localPos.z * localPos.z);
                    return distXZ <= radius && Mathf.Abs(localPos.y) <= scale.y / 2f;
                default:
                    return localPos.magnitude <= scale.x / 2f;
            }
        }

        private static Bounds GetVolumeAabb(CutVolume volume)
        {
            var center = volume.position.ToVector3();
            var rotation = volume.rotation.ToQuaternion();
            var scale = volume.scale.ToVector3();

            Vector3 min = center;
            Vector3 max = center;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 local = new Vector3(x * scale.x / 2f, y * scale.y / 2f, z * scale.z / 2f);
                Vector3 world = rotation * local + center;
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }

            Bounds b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        private static Bounds TransformBounds(Bounds local, Transform t)
        {
            Vector3 min = local.min;
            Vector3 max = local.max;
            Vector3 worldMin = t.TransformPoint(min);
            Vector3 worldMax = worldMin;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 world = t.TransformPoint(corner);
                worldMin = Vector3.Min(worldMin, world);
                worldMax = Vector3.Max(worldMax, world);
            }
            Bounds b = new Bounds();
            b.SetMinMax(worldMin, worldMax);
            return b;
        }

        private static bool Intersects(Bounds a, Bounds b)
        {
            return a.min.x <= b.max.x && a.max.x >= b.min.x &&
                   a.min.y <= b.max.y && a.max.y >= b.min.y &&
                   a.min.z <= b.max.z && a.max.z >= b.min.z;
        }

        private GameObject FindSourceObject(string name, Vector3 position)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;
            var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != name)
                            continue;
                        var dist = (t.position - position).sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = t.gameObject;
                        }
                    }
                }
            }

            return best;
        }

        private void RestoreAllCuts()
        {
            foreach (var cut in _cuts)
            {
                if (cut.MeshFilter != null && cut.OriginalMesh != null)
                    cut.MeshFilter.sharedMesh = cut.OriginalMesh;

                if (cut.MeshCollider != null)
                {
                    cut.MeshCollider.enabled = false;
                    if (cut.OriginalColliderMesh != null)
                        cut.MeshCollider.sharedMesh = cut.OriginalColliderMesh;
                    else if (cut.GeneratedMesh != null)
                        cut.MeshCollider.sharedMesh = null;
                    cut.MeshCollider.enabled = true;
                }

                if (cut.DisabledCollider != null)
                    cut.DisabledCollider.enabled = true;

                if (cut.CutMesh != null)
                    Destroy(cut.CutMesh);
                if (cut.GeneratedMesh != null)
                    Destroy(cut.GeneratedMesh);
            }
            _cuts.Clear();
        }

        private void OnDestroy()
        {
            Instance = null;
            RestoreAllCuts();
        }
    }
}
