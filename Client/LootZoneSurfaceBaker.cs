using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    // Editor-time placement for Loose Loot Zones. The server only knows the zone's numbers, not the map geometry, so it used to
    // drop every item at the zone marker's Y. This samples the real physics world inside the zone footprint, keeps surfaces that
    // can hold an item (floors, shelves, tables, crates...) and stores them in zone.surfacePoints for the server to hand out.
    // Read-only against the scene: it only raycasts/overlap-tests; it never creates colliders or moves anything.
    public static class LootZoneSurfaceBaker
    {
        public const int DefaultBakeCount = 32;
        public const float DefaultMinSpacing = 0.4f;
        public const float DefaultMaxSlope = 40f;

        private const int AttemptsPerPoint = 24;
        private const int MaxAttempts = 3000;
        private const float VerticalPadding = 0.5f;
        private const float MinHalfHeight = 1f;
        // An item is roughly a 14 cm sphere resting a hair above the surface; it must fit with nothing overlapping it.
        private const float ClearanceRadius = 0.07f;
        private const float SurfaceLift = 0.02f;
        private const float ClearanceHeight = 0.2f;
        private const int MaxRejectedVisuals = 150;

        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static GameObject _vizRoot;

        private enum Reject { NoSurface, OutOfRange, TooSteep, Obstructed, Spacing }

        public static string Bake(LootZone zone)
        {
            ClearVisualization();
            if (zone == null)
                return "No zone.";

            var bakeCount = Mathf.Clamp(zone.bakeCount > 0 ? zone.bakeCount : DefaultBakeCount, 1, 500);
            var spacing = Mathf.Max(0.05f, zone.minSpacing > 0f ? zone.minSpacing : DefaultMinSpacing);
            var maxSlope = Mathf.Clamp(zone.maxSlope > 0f ? zone.maxSlope : DefaultMaxSlope, 5f, 80f);
            var minNormalY = Mathf.Cos(maxSlope * Mathf.Deg2Rad);
            var diag = Plugin.EnableLootZoneDiagnostics != null && Plugin.EnableLootZoneDiagnostics.Value;

            var center = zone.position.ToVector3();
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            var yaw = Quaternion.Euler(0f, zone.rotation != null ? zone.rotation.y : 0f, 0f);
            var halfHeight = zone.shape == ZoneShape.Sphere ? zone.radius * scale.x : scale.y * 0.5f;
            halfHeight = Mathf.Max(halfHeight, MinHalfHeight);
            var top = center.y + halfHeight + VerticalPadding;
            var bottom = center.y - halfHeight - VerticalPadding;

            var accepted = new List<Vector3>();
            var rejectedVisual = new List<Vector3>();
            var counts = new int[5];
            var attempts = 0;
            var maxAttempts = Mathf.Min(MaxAttempts, bakeCount * AttemptsPerPoint);

            while (accepted.Count < bakeCount && attempts < maxAttempts)
            {
                attempts++;
                var world = center + yaw * RandomLocalOffset(zone, scale);
                var origin = new Vector3(world.x, top, world.z);
                var count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, top - bottom, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count == 0)
                {
                    counts[(int)Reject.NoSurface]++;
                    Log(diag, zone, origin, world, null, Reject.NoSurface, "no collider below candidate");
                    continue;
                }

                // Every surface level in the column is a separate candidate: floor, shelf boards, table tops, crate lids.
                for (int i = 0; i < count && accepted.Count < bakeCount; i++)
                {
                    var hit = Hits[i];
                    var p = hit.point;
                    Reject? reason = null;
                    if (p.y < bottom || p.y > top)
                        reason = Reject.OutOfRange;
                    else if (hit.normal.y < minNormalY)
                        reason = Reject.TooSteep;
                    else
                    {
                        var rest = p + Vector3.up * SurfaceLift;
                        if (Physics.CheckCapsule(rest + Vector3.up * ClearanceRadius, rest + Vector3.up * ClearanceHeight, ClearanceRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            reason = Reject.Obstructed;
                        else if (TooClose(accepted, rest, spacing))
                            reason = Reject.Spacing;
                    }

                    if (reason.HasValue)
                    {
                        counts[(int)reason.Value]++;
                        if (rejectedVisual.Count < MaxRejectedVisuals)
                            rejectedVisual.Add(p);
                        Log(diag, zone, origin, world, hit, reason.Value, null);
                        continue;
                    }

                    var point = p + Vector3.up * SurfaceLift;
                    accepted.Add(point);
                    Log(diag, zone, origin, world, hit, null, "accepted");
                }
            }

            zone.surfacePoints = new List<TransformData>(accepted.Count);
            foreach (var a in accepted)
                zone.surfacePoints.Add(TransformData.FromVector3(a));

            BuildVisualization(accepted, rejectedVisual);

            var sb = new StringBuilder();
            sb.Append($"Baked {accepted.Count}/{bakeCount} surface points in {attempts} attempts");
            if (accepted.Count < bakeCount)
                sb.Append($" (stopped at the {maxAttempts}-attempt limit)");
            sb.Append($". Rejected: no surface {counts[0]}, outside zone height {counts[1]}, too steep (> {maxSlope:F0} deg) {counts[2]}, obstructed {counts[3]}, spacing {counts[4]}.");
            var summary = sb.ToString();
            if (diag || accepted.Count < bakeCount)
                Plugin.Log.LogInfo($"[MEL lootzone] Zone '{zone.id}' ({zone.name}): {summary} y-range {bottom:F2}..{top:F2}.");
            return summary;
        }

        public static void ClearVisualization()
        {
            if (_vizRoot != null)
                Object.Destroy(_vizRoot);
            _vizRoot = null;
        }

        private static Vector3 RandomLocalOffset(LootZone zone, TransformData scale)
        {
            if (zone.shape == ZoneShape.Box)
                return new Vector3(Random.Range(-0.5f, 0.5f) * scale.x, 0f, Random.Range(-0.5f, 0.5f) * scale.z);

            var angle = Random.Range(0f, Mathf.PI * 2f);
            var r = zone.radius * scale.x * Mathf.Sqrt(Random.value);
            return new Vector3(r * Mathf.Cos(angle), 0f, r * Mathf.Sin(angle));
        }

        private static bool TooClose(List<Vector3> accepted, Vector3 p, float spacing)
        {
            var sq = spacing * spacing;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - p).sqrMagnitude < sq)
                    return true;
            return false;
        }

        private static void Log(bool diag, LootZone zone, Vector3 origin, Vector3 candidate, RaycastHit? hit, Reject? reject, string note)
        {
            if (!diag)
                return;
            try
            {
                var sb = new StringBuilder();
                sb.Append($"[MEL lootzone] zone={zone.id} candidate=({candidate.x:F2},{candidate.z:F2}) ray origin={origin.ToString("F2")} dir=down");
                if (hit.HasValue && hit.Value.collider != null)
                {
                    var h = hit.Value;
                    var slope = Vector3.Angle(h.normal, Vector3.up);
                    sb.Append($" hit='{h.collider.name}' layer={LayerMask.LayerToName(h.collider.gameObject.layer)} point={h.point.ToString("F2")} normal={h.normal.ToString("F2")} slope={slope:F1}");
                }
                sb.Append(reject.HasValue ? $" REJECT={reject.Value}" : " ACCEPT");
                if (note != null)
                    sb.Append($" ({note})");
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch
            {
            }
        }

        // Temporary editor-only markers: green = baked point, red = rejected hit. Replaced on the next bake, no colliders, layer 2.
        private static void BuildVisualization(List<Vector3> accepted, List<Vector3> rejected)
        {
            try
            {
                _vizRoot = new GameObject("MEL_LootZoneBakeViz") { layer = 2 };
                AddMarkers(accepted, new Color(0.2f, 1f, 0.3f, 1f), 0.12f);
                AddMarkers(rejected, new Color(1f, 0.25f, 0.2f, 1f), 0.08f);
            }
            catch
            {
            }
        }

        private static void AddMarkers(List<Vector3> points, Color color, float size)
        {
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Standard");
            foreach (var p in points)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(s.GetComponent<Collider>());
                s.layer = 2;
                s.transform.SetParent(_vizRoot.transform, false);
                s.transform.position = p;
                s.transform.localScale = Vector3.one * size;
                var r = s.GetComponent<Renderer>();
                if (r != null)
                    r.material = new Material(shader) { color = color };
            }
        }
    }
}
