using System;
using System.Collections.Generic;
using System.Text;
using EFT.Interactive;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    // One-shot, read-only capture of the Unity hierarchy around doors near the cursor ray.
    // Doors are identified by component type (WorldInteractiveObject / Door), never by name.
    // Nothing here modifies scene objects; every step is wrapped so failures only log.
    internal static class DoorDiagnostics
    {
        private const float MaxRayDistance = 100f;
        private const float NearRayRadius = 1.0f;
        private const int MaxDoors = 4;
        private const int MaxNodesPerDump = 250;
        private const int MaxSubtreeForParentRoot = 150;

        public static void Capture(Ray ray, GameObject finalPick, Func<Transform, GameObject> cachedOwner, Func<GameObject, string> validityReason)
        {
            try
            {
                var sb = new StringBuilder(4096);
                sb.AppendLine("[DOOR-DIAG] ===== door hierarchy capture =====");
                sb.AppendLine($"[DOOR-DIAG] ray origin={ray.origin.ToString("F2")} dir={ray.direction.ToString("F3")}");
                sb.AppendLine($"[DOOR-DIAG] picker's final pick: {(finalPick != null ? Path(finalPick) + " id=" + finalPick.GetInstanceID() : "none")}");

                var doors = new List<WorldInteractiveObject>();
                var doorDist = new Dictionary<WorldInteractiveObject, float>();

                // 1) Everything the ray touches, triggers included (the picker ignores triggers; we want to see them).
                var hits = Physics.RaycastAll(ray, MaxRayDistance, ~0, QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                sb.AppendLine($"[DOOR-DIAG] ray hits (all layers, triggers included): {hits.Length}");
                foreach (var h in hits)
                {
                    if (h.collider == null) continue;
                    var wio = h.collider.GetComponentInParent<WorldInteractiveObject>();
                    sb.AppendLine($"[DOOR-DIAG]   d={h.distance:F2} {Path(h.collider.gameObject)} {h.collider.GetType().Name} trigger={h.collider.isTrigger} enabled={h.collider.enabled} layer={LayerName(h.collider.gameObject.layer)}" +
                                  (wio != null ? $" -> door component on {Path(wio.gameObject)}" : ""));
                    if (wio != null && !doorDist.ContainsKey(wio))
                    {
                        doorDist[wio] = 0f;
                        doors.Add(wio);
                    }
                }

                // 2) Door components whose bounds pass close to the ray (catches doors the ray visually misses).
                var all = UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>();
                var near = new List<KeyValuePair<float, WorldInteractiveObject>>();
                foreach (var wio in all)
                {
                    if (wio == null || doorDist.ContainsKey(wio)) continue;
                    if (TryRayDistance(ray, wio.transform, out var dist) && dist <= NearRayRadius)
                        near.Add(new KeyValuePair<float, WorldInteractiveObject>(dist, wio));
                }
                near.Sort((a, b) => a.Key.CompareTo(b.Key));
                foreach (var kv in near)
                {
                    if (doors.Count >= MaxDoors) break;
                    doorDist[kv.Value] = kv.Key;
                    doors.Add(kv.Value);
                }

                // 3) Door components on the picked object's chain or children.
                if (finalPick != null)
                {
                    var up = finalPick.GetComponentInParent<WorldInteractiveObject>();
                    var down = finalPick.GetComponentInChildren<WorldInteractiveObject>(true);
                    foreach (var w in new[] { up, down })
                    {
                        if (w != null && !doorDist.ContainsKey(w) && doors.Count < MaxDoors + 2)
                        {
                            doorDist[w] = TryRayDistance(ray, w.transform, out var d) ? d : -1f;
                            doors.Add(w);
                        }
                    }
                }

                sb.AppendLine($"[DOOR-DIAG] door components found near cursor: {doors.Count} (of {all.Length} WorldInteractiveObject in scene)");
                if (doors.Count == 0)
                    sb.AppendLine("[DOOR-DIAG] none. Aim at the door (or its frame) and capture again; non-door objects are not dumped.");

                foreach (var wio in doors)
                    DumpDoor(sb, ray, wio, doorDist[wio], finalPick, cachedOwner, validityReason);

                sb.AppendLine("[DOOR-DIAG] ===== end capture =====");
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[DOOR-DIAG] capture failed: {ex.Message}");
            }
        }

        private static void DumpDoor(StringBuilder sb, Ray ray, WorldInteractiveObject wio, float rayDist, GameObject finalPick,
            Func<Transform, GameObject> cachedOwner, Func<GameObject, string> validityReason)
        {
            try
            {
                var go = wio.gameObject;
                sb.AppendLine("[DOOR-DIAG] ---------------------------------------------");
                sb.AppendLine($"[DOOR-DIAG] DOOR COMPONENT OWNER: {Path(go)} id={go.GetInstanceID()} type={wio.GetType().Name} isDoor={(wio is Door)}");
                sb.AppendLine($"[DOOR-DIAG] distance from cursor ray to door bounds: {(rayDist < 0 ? "n/a" : rayDist.ToString("F2") + " m")}");

                var parent = go.transform.parent;
                var root = go.transform;
                if (parent != null && parent.GetComponentsInChildren<Transform>(true).Length <= MaxSubtreeForParentRoot)
                    root = parent;
                sb.AppendLine($"[DOOR-DIAG] dumping subtree from {Path(root.gameObject)} (parent of the door component when the subtree is small, so sibling frame/handle objects show)");

                int count = 0;
                DumpNode(sb, root, 0, ray, wio, finalPick, cachedOwner, validityReason, ref count);
                if (count >= MaxNodesPerDump)
                    sb.AppendLine($"[DOOR-DIAG] (truncated at {MaxNodesPerDump} nodes)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[DOOR-DIAG] door dump failed: {ex.Message}");
            }
        }

        private static void DumpNode(StringBuilder sb, Transform t, int depth, Ray ray, WorldInteractiveObject doorOwner, GameObject finalPick,
            Func<Transform, GameObject> cachedOwner, Func<GameObject, string> validityReason, ref int count)
        {
            if (t == null || count >= MaxNodesPerDump) return;
            count++;

            var go = t.gameObject;
            var indent = new string(' ', depth * 2);
            var mark = (go == doorOwner.gameObject ? " <== DOOR COMPONENT OWNER" : "") + (go == finalPick ? " <== PICKER FINAL PICK" : "");
            sb.AppendLine($"[DOOR-DIAG] {indent}- {go.name} id={go.GetInstanceID()} active={go.activeSelf}/{go.activeInHierarchy} layer={LayerName(go.layer)} parent={(t.parent != null ? t.parent.name : "<root>")} children={t.childCount}{mark}");
            sb.AppendLine($"[DOOR-DIAG] {indent}    pos={t.position.ToString("F2")} rot={t.rotation.eulerAngles.ToString("F1")} localPos={t.localPosition.ToString("F2")} scale={t.lossyScale.ToString("F2")}");

            bool hasGeometry = false;
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null || c is Transform) continue;
                try
                {
                    DescribeComponent(sb, indent, c, ray, ref hasGeometry);
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[DOOR-DIAG] {indent}    {c.GetType().Name}: unreadable ({ex.Message})");
                }
            }

            if (hasGeometry)
            {
                var owner = cachedOwner?.Invoke(t);
                var inCache = owner != null;
                var reason = validityReason?.Invoke(owner ?? go);
                sb.AppendLine($"[DOOR-DIAG] {indent}    picker: scan-cache owner={(inCache ? Path(owner) : "NOT in scan cache (renderer path cannot pick it; a collider hit still can)")}" +
                              $" | validity={(string.IsNullOrEmpty(reason) ? "valid" : "rejected: " + reason)}" +
                              (finalPick != null && owner == finalPick ? " | SELECTED" : ""));
            }

            for (int i = 0; i < t.childCount; i++)
                DumpNode(sb, t.GetChild(i), depth + 1, ray, doorOwner, finalPick, cachedOwner, validityReason, ref count);
        }

        private static void DescribeComponent(StringBuilder sb, string indent, Component c, Ray ray, ref bool hasGeometry)
        {
            var pre = $"[DOOR-DIAG] {indent}    ";
            if (c is Collider col)
            {
                hasGeometry = true;
                var b = col.bounds;
                var rayHit = col.Raycast(ray, out var h, MaxRayDistance) ? $"ray d={h.distance:F2}" : "ray miss";
                sb.AppendLine($"{pre}Collider {col.GetType().Name} enabled={col.enabled} isTrigger={col.isTrigger} layer={LayerName(col.gameObject.layer)} boundsCenter={b.center.ToString("F2")} boundsSize={b.size.ToString("F2")} {rayHit}" +
                              (col.isTrigger ? " [ignored by picker: triggers]" : ""));
            }
            else if (c is Renderer r)
            {
                hasGeometry = true;
                var b = r.bounds;
                var dist = b.IntersectRay(ray, out var d) ? $"ray d={d:F2}" : "ray miss";
                sb.AppendLine($"{pre}Renderer {r.GetType().Name} enabled={r.enabled} forceOff={r.forceRenderingOff} materials={r.sharedMaterials.Length} boundsCenter={b.center.ToString("F2")} boundsSize={b.size.ToString("F2")} {dist}");
            }
            else if (c is MeshFilter mf)
            {
                var m = mf.sharedMesh;
                sb.AppendLine($"{pre}MeshFilter mesh={(m != null ? m.name + " verts=" + m.vertexCount + " subMeshes=" + m.subMeshCount + " readable=" + m.isReadable : "<none>")}");
            }
            else if (c is LODGroup lod)
            {
                var lods = lod.GetLODs();
                var sbl = new StringBuilder();
                for (int i = 0; i < lods.Length; i++)
                    sbl.Append($"[LOD{i} screenH={lods[i].screenRelativeTransitionHeight:F3} renderers={(lods[i].renderers != null ? lods[i].renderers.Length : 0)}] ");
                sb.AppendLine($"{pre}LODGroup lodCount={lod.lodCount} {sbl}");
            }
            else if (c is WorldInteractiveObject w)
            {
                string state = "?", extra = "";
                try { state = w.DoorState.ToString(); } catch { }
                try { extra = $" id={w.Id} keyId={(string.IsNullOrEmpty(w.KeyId) ? "-" : w.KeyId)}"; } catch { }
                var door = w as Door;
                if (door != null)
                {
                    try { extra += $" canBreach={door.CanBeBreached}"; } catch { }
                }
                sb.AppendLine($"{pre}WorldInteractiveObject ({w.GetType().Name}) DoorState={state}{extra} hasDoorComponent={(w.GetComponent<Door>() != null)}");
            }
            else if (c is Behaviour || c is MonoBehaviour)
            {
                sb.AppendLine($"{pre}Component {c.GetType().FullName}");
            }
        }

        // Distance from the ray to the nearest point of the object's combined renderer/collider bounds.
        private static bool TryRayDistance(Ray ray, Transform t, out float distance)
        {
            distance = float.MaxValue;
            Bounds b = default;
            var has = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                if (c == null) continue;
                if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds);
            }
            if (!has) return false;

            var along = Mathf.Clamp(Vector3.Dot(b.center - ray.origin, ray.direction), 0f, MaxRayDistance);
            var closest = ray.origin + ray.direction * along;
            distance = Mathf.Sqrt(b.SqrDistance(closest));
            return true;
        }

        private static string LayerName(int layer)
        {
            var n = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(n) ? layer.ToString() : $"{layer}:{n}";
        }

        private static string Path(GameObject go)
        {
            if (go == null) return "<null>";
            var t = go.transform;
            var sb = new StringBuilder(t.name);
            while (t.parent != null)
            {
                t = t.parent;
                sb.Insert(0, t.name + "/");
            }
            return "/" + sb;
        }
    }
}
