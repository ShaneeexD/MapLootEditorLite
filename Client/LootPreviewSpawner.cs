using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using Diz.LanguageExtensions;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    public class LootPreviewSpawner
    {
        public const string DefaultItemTpl = "544fb45d4bdc2dee738b4568";
        private readonly GameObject _root;
        private readonly CoroutineRunner _runner;
        private readonly List<GameObject> _previews = new List<GameObject>();
        private readonly List<GameObject> _staticPreviews = new List<GameObject>();
        private readonly List<GameObject> _effectPreviews = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _staticSources = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> _fallbackCache = new Dictionary<string, GameObject>();
        // Latest single-preview request per marker; older async requests compare against this and drop out.
        private readonly Dictionary<string, int> _markerGeneration = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _inflightSignature = new Dictionary<string, string>();
        private int _generationCounter;
        private int _requestCounter;
        private readonly HashSet<int> _staleLoggedRequests = new HashSet<int>();
        private readonly HashSet<string> _duplicateWarningSignatures = new HashSet<string>();
        private readonly HashSet<int> _observedFactoryPreviewInstances = new HashSet<int>();

        public LootPreviewSpawner(GameObject root, MonoBehaviour runner)
        {
            _root = root;
            _runner = runner.gameObject.GetComponent<CoroutineRunner>() ?? runner.gameObject.AddComponent<CoroutineRunner>();
        }

        public void SpawnAtMarker(LooseLootSpawn marker, int itemIndex = 0)
        {
            var tpl = GetItemTpl(marker.items, itemIndex);
            var item = GetItem(marker.items, itemIndex);
            var pos = marker.position.ToVector3();
            var surface = marker.useGravity ? GetPreviewGroundPosition(pos) : pos;
            var spawnPos = surface ?? pos;
            var rotation = marker.rotation.ToQuaternion();
            SpawnPreviewInternal(tpl, item, spawnPos, rotation, pos, rotation, true, marker.name, marker.id, true, true);
            SetMarkerPreviewsVisible(marker.id, surface.HasValue);
        }

        private void SetMarkerPreviewsVisible(string markerId, bool visible)
        {
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;
                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta != null && meta.sourceMarkerId == markerId && preview.activeSelf != visible)
                    preview.SetActive(visible);
            }
        }

        // Nearest first surface within 10 m straight up or straight down from the marker.
        // The ray down starts just above the marker so a surface at the marker's own height is still found.
        // Upward search is done as a downward scan from 10 m above: Unity raycasts ignore back faces, so a ray
        // fired upward would pass through single-sided floor meshes seen from underneath.
        private static readonly RaycastHit[] _upScanHits = new RaycastHit[32];

        // An upward candidate further above the marker than this loses to the downward surface.
        private const float UpSnapTolerance = 0.3f;

        private static Vector3? GetPreviewGroundPosition(Vector3 markerPos)
        {
            const float maxDistance = 10f;
            const float downStartOffset = 0.1f;

            var hasDown = Physics.Raycast(markerPos + Vector3.up * downStartOffset, Vector3.down, out RaycastHit downHit, maxDistance + downStartOffset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            var hasUp = false;
            var upPoint = Vector3.zero;
            RaycastHit upHit = default(RaycastHit);
            var count = Physics.RaycastNonAlloc(markerPos + Vector3.up * maxDistance, Vector3.down, _upScanHits, maxDistance - downStartOffset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var minY = markerPos.y + downStartOffset;
            for (int i = 0; i < count; i++)
            {
                var p = _upScanHits[i].point;
                if (p.y <= minY)
                    continue;
                if (!hasUp || p.y < upPoint.y)
                {
                    upPoint = p;
                    upHit = _upScanHits[i];
                    hasUp = true;
                }
            }

            Vector3? result;
            string reason;
            Collider selected;
            if (hasDown && hasUp)
            {
                var upGap = upPoint.y - markerPos.y;
                var forcedDown = upGap > UpSnapTolerance;
                var downWins = forcedDown || (downHit.distance - downStartOffset) <= upGap;
                result = downWins ? downHit.point : upPoint;
                selected = downWins ? downHit.collider : upHit.collider;
                reason = forcedDown
                    ? $"both hit; up surface > {UpSnapTolerance:F1} m above marker; preferring down surface"
                    : downWins ? "both hit; down surface is closer or equal" : "both hit; up surface is closer";
            }
            else if (hasDown)
            {
                result = downHit.point;
                selected = downHit.collider;
                reason = "only down ray hit";
            }
            else if (hasUp)
            {
                result = upPoint;
                selected = upHit.collider;
                reason = "only up scan hit (down ray found nothing)";
            }
            else
            {
                result = null;
                selected = null;
                reason = "no surface within 10 m";
            }

            DiagGroundSelection(markerPos, maxDistance, downStartOffset, count, hasDown, downHit, hasUp, upHit, result, selected, reason);
            return result;
        }

        // TEMPORARY gravity ground-detection diagnostics. Logging only; never affects which surface is chosen.
        private static int _diagGroundLastColliderId = int.MinValue;
        private static float _diagGroundLastTime = -10f;

        private static string DiagPath(Transform t)
        {
            var sb = new System.Text.StringBuilder();
            int guard = 0;
            while (t != null && guard++ < 12)
            {
                sb.Insert(0, "/" + t.name);
                t = t.parent;
            }
            return sb.ToString();
        }

        private static string DiagHit(RaycastHit h)
        {
            var c = h.collider;
            if (c == null)
                return "<null collider>";
            return $"'{c.name}' path={DiagPath(c.transform)} layer={LayerMask.LayerToName(c.gameObject.layer)}({c.gameObject.layer}) trigger={c.isTrigger} type={c.GetType().Name} point={h.point.ToString("F2")} normal={h.normal.ToString("F2")} dist={h.distance:F2}";
        }

        private static void DiagGroundSelection(Vector3 markerPos, float maxDistance, float downStartOffset, int upCount, bool hasDown, RaycastHit downHit, bool hasUp, RaycastHit upHit, Vector3? result, Collider selected, string reason)
        {
            try
            {
                const float LargeSnap = 0.75f;
                var now = Time.unscaledTime;
                var selId = selected != null ? selected.GetInstanceID() : 0;
                var snap = result.HasValue ? result.Value.y - markerPos.y : 0f;

                // Log only when the selected surface changes; throttled so drags don't flood the log.
                if (selId == _diagGroundLastColliderId || now - _diagGroundLastTime < 0.25f)
                    return;
                if (result.HasValue && Mathf.Abs(snap) < LargeSnap && _diagGroundLastColliderId == int.MinValue)
                {
                    _diagGroundLastColliderId = selId;
                    _diagGroundLastTime = now;
                    return;
                }
                _diagGroundLastColliderId = selId;
                _diagGroundLastTime = now;

                var sb = new System.Text.StringBuilder();
                sb.Append($"[GroundDiag] marker={markerPos.ToString("F2")} {(Mathf.Abs(snap) >= LargeSnap ? "LARGE_SNAP " : "")}reason={reason}\n");
                sb.Append($"  downRay origin={(markerPos + Vector3.up * downStartOffset).ToString("F2")} dir=down len={maxDistance + downStartOffset:F1} hit={hasDown}\n");
                sb.Append($"  upScan origin={(markerPos + Vector3.up * maxDistance).ToString("F2")} dir=down len={maxDistance - downStartOffset:F1} rawHits={upCount}{(upCount >= _upScanHits.Length ? " (BUFFER FULL, hits may be dropped)" : "")} anyAboveMarker={hasUp}\n");
                if (hasDown)
                    sb.Append($"  downHit: {DiagHit(downHit)}\n");
                if (hasUp)
                    sb.Append($"  lowestUpHit: {DiagHit(upHit)}\n");
                if (result.HasValue)
                    sb.Append($"  SELECTED y={result.Value.y:F2} verticalDiff={snap:+0.00;-0.00} ({(snap > 0.001f ? "ABOVE" : snap < -0.001f ? "BELOW" : "AT")} marker) collider={(selected != null ? selected.name : "<none>")}\n");
                else
                    sb.Append("  SELECTED none (preview hidden)\n");

                sb.Append("  scan hits (lowest first, max 8):\n");
                foreach (var h in _upScanHits.Take(upCount).OrderBy(x => x.point.y).Take(8))
                    sb.Append($"    y={h.point.y:F2} {(h.point.y > markerPos.y + downStartOffset ? "above" : "at/below")} {DiagHit(h)}\n");

                Plugin.Log.LogWarning(sb.ToString());
            }
            catch
            {
            }
        }

        public void SpawnInZone(LootZone marker, int itemIndex = 0)
        {
            var tpl = GetItemTpl(marker.items, itemIndex);
            var item = GetItem(marker.items, itemIndex);
            var pos = GetRandomPointInZone(marker);
            pos.y += item?.yOffset ?? 0f;
            var rotation = GetZoneItemRotation(marker.items, itemIndex);
            var markerPos = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            SpawnPreview(tpl, item, pos, rotation, markerPos, markerRot, item?.randomRotation != true, marker.name, marker.id);
        }

        public void SpawnAllInZone(LootZone marker)
        {
            if (marker.items == null || marker.items.Count == 0)
                return;

            ClearByMarkerId(marker.id);
            var markerPos = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            var pool = marker.surfacePoints != null && marker.surfacePoints.Count > 0 ? new List<TransformData>(marker.surfacePoints) : null;
            for (int i = 0; i < marker.items.Count; i++)
            {
                var tpl = GetItemTpl(marker.items, i);
                var item = GetItem(marker.items, i);
                Vector3 pos;
                if (pool != null && pool.Count > 0)
                {
                    var idx = UnityEngine.Random.Range(0, pool.Count);
                    pos = pool[idx].ToVector3();
                    pool.RemoveAt(idx);
                }
                else
                {
                    pos = GetRandomPointInZone(marker);
                }
                pos.y += item?.yOffset ?? 0f;
                var rotation = GetZoneItemRotation(marker.items, i);
                SpawnPreviewInternal(tpl, item, pos, rotation, markerPos, markerRot, item?.randomRotation != true, marker.name, marker.id, false);
            }
        }

        public void SpawnLightPreview(LightZone marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var pos = marker.position.ToVector3();
            var go = new GameObject($"LightPreview_{marker.name}");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = pos;
            go.transform.rotation = marker.rotation.ToQuaternion();

            var light = go.AddComponent<Light>();
            light.type = ParseLightType(marker.lightType);
            light.color = marker.color.ToColor().linear;
            light.intensity = marker.intensity;
            light.range = marker.range;
            light.enabled = marker.enabled;
            light.shadows = ParseLightShadows(marker.shadows);
            light.shadowStrength = marker.shadowStrength;
            light.shadowBias = marker.shadowBias;
            light.shadowNormalBias = marker.shadowNormalBias;
            if (light.type == LightType.Spot)
                light.spotAngle = marker.spotAngle;

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "LightPreviewVisual";
            sphere.transform.SetParent(go.transform, false);
            sphere.transform.localScale = Vector3.one * 0.25f;
            UnityEngine.Object.Destroy(sphere.GetComponent<Collider>());
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
                mat.color = light.color;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", light.color * 2f);
                renderer.material = mat;
            }

            var dir = new GameObject("LightPreviewDirection");
            dir.transform.SetParent(go.transform, false);
            var dirLr = dir.AddComponent<LineRenderer>();
            dirLr.useWorldSpace = false;
            dirLr.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
            dirLr.startWidth = 0.04f;
            dirLr.endWidth = 0.04f;
            DrawLightDirection(dirLr, marker);

            var meta = go.AddComponent<PreviewEffectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.markerName = marker.name;

            _effectPreviews.Add(go);
        }

        private void DrawLightDirection(LineRenderer lr, LightZone lz)
        {
            var type = lz.lightType ?? "Point";
            var color = lz.color.ToColor();
            color.a = 1f;
            var positions = new List<Vector3>();

            if (type == "Spot")
            {
                var length = Mathf.Clamp(lz.range * 0.2f, 0.5f, 4f);
                var radius = length * Mathf.Tan(lz.spotAngle * 0.5f * Mathf.Deg2Rad);
                var apex = Vector3.zero;
                var center = Vector3.forward * length;
                positions.Add(apex);
                positions.Add(center);
                const int segments = 24;
                for (int i = 0; i <= segments; i++)
                {
                    var angle = i * Mathf.PI * 2f / segments;
                    var point = center + Vector3.right * Mathf.Cos(angle) * radius + Vector3.up * Mathf.Sin(angle) * radius;
                    positions.Add(apex);
                    positions.Add(point);
                }
            }
            else if (type == "Directional")
            {
                positions.Add(Vector3.zero);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.right * 0.2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.left * 0.2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.up * 0.2f);
                positions.Add(Vector3.forward * 2f);
                positions.Add(Vector3.forward * 1.6f + Vector3.down * 0.2f);
            }
            else
            {
                positions.Add(Vector3.zero);
                positions.Add(Vector3.forward * 0.3f);
            }

            lr.positionCount = positions.Count;
            lr.SetPositions(positions.ToArray());
            lr.startColor = color;
            lr.endColor = color;
        }

        public void SpawnBotSpawnPreview(BotSpawnPoint marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var go = CreateBotPreviewObject(marker.position.ToVector3(), marker.rotation.ToQuaternion(), marker.name, marker.id, marker.preset.ToString(), marker.side.ToString(), marker.category.ToString());
            _effectPreviews.Add(go);
        }

        public void SpawnBotSpawnZonePreview(BotSpawnZone marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var center = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            var scale = marker.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            var count = Mathf.Max(1, marker.spawnCount);
            var positions = new List<Vector3>();

            for (int i = 0; i < count; i++)
            {
                var point = GetRandomPointInZone(center, marker.shape, marker.radius, scale, markerRot);
                positions.Add(point);
                var go = CreateBotPreviewObject(point, markerRot, marker.name, marker.id, marker.preset.ToString(), marker.side.ToString(), marker.category.ToString(), i);
                _effectPreviews.Add(go);
            }
        }

        public void SpawnPmcSpawnZonePreview(PmcSpawnZone marker)
        {
            if (marker == null)
                return;

            ClearByMarkerId(marker.id);
            var center = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            var scale = marker.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            var count = Mathf.Max(1, marker.maxGroupSize);

            for (int i = 0; i < count; i++)
            {
                var point = GetRandomPointInZone(center, marker.shape, marker.radius, scale, markerRot);
                var go = CreateBotPreviewObject(point, markerRot, marker.name, marker.id, marker.preset.ToString(), marker.side.ToString(), marker.category.ToString(), i);
                _effectPreviews.Add(go);
            }
        }

        private GameObject CreateBotPreviewObject(Vector3 position, Quaternion rotation, string markerName, string markerId, string preset, string side, string category, int index = -1)
        {
            var go = new GameObject($"BotPreview_{markerName}" + (index >= 0 ? $"_{index}" : ""));
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            go.transform.rotation = rotation;

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = "BotPreviewVisual";
            capsule.transform.SetParent(go.transform, false);
            capsule.transform.localScale = new Vector3(0.4f, 1f, 0.4f);
            capsule.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            UnityEngine.Object.Destroy(capsule.GetComponent<Collider>());
            var renderer = capsule.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
                mat.color = GetBotPreviewColor(side, category);
                renderer.material = mat;
            }

            var meta = go.AddComponent<PreviewEffectMarker>();
            meta.sourceMarkerId = markerId;
            meta.markerName = markerName;
            meta.label = $"{Locale.Get(preset)}\n{Locale.Get(side)} {Locale.Get(category)}";

            return go;
        }

        private static Color GetBotPreviewColor(string side, string category)
        {
            if (side == "Usec" || side == "Bear" || side == "Pmc")
                return new Color(0.2f, 0.4f, 1f, 0.8f);
            if (category == "Boss")
                return new Color(1f, 0.2f, 0.2f, 0.8f);
            if (category == "BotPmc")
                return new Color(1f, 0.8f, 0.2f, 0.8f);
            return new Color(0.4f, 0.8f, 0.2f, 0.8f);
        }

        private static Vector3 GetRandomPointInZone(Vector3 center, ZoneShape shape, float radius, TransformData scale, Quaternion rotation)
        {
            switch (shape)
            {
                case ZoneShape.Box:
                    return center + rotation * new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f) * scale.x,
                        0f,
                        UnityEngine.Random.Range(-0.5f, 0.5f) * scale.z);
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var r = radius * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f)) * scale.x;
                    return center + rotation * new Vector3(r * Mathf.Cos(angle), 0f, r * Mathf.Sin(angle));
                default:
                    var point = center + rotation * UnityEngine.Random.insideUnitSphere * radius * scale.x;
                    point.y = center.y;
                    return point;
            }
        }

        private static LightType ParseLightType(string type)
        {
            if (Enum.TryParse<LightType>(type, true, out var result))
                return result;
            return LightType.Point;
        }

        private static LightShadows ParseLightShadows(string shadows)
        {
            if (Enum.TryParse<LightShadows>(shadows, true, out var result))
                return result;
            return LightShadows.None;
        }

        public static Vector3 GetRandomPointInZone(LootZone zone)
        {
            var center = zone.position.ToVector3();
            var scale = zone.scale ?? new TransformData { x = 1f, y = 1f, z = 1f };
            switch (zone.shape)
            {
                case ZoneShape.Box:
                    return new Vector3(
                        center.x + UnityEngine.Random.Range(-0.5f, 0.5f) * scale.x,
                        center.y,
                        center.z + UnityEngine.Random.Range(-0.5f, 0.5f) * scale.z);
                case ZoneShape.Cylinder:
                case ZoneShape.Capsule:
                    var angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var r = zone.radius * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f)) * scale.x;
                    return new Vector3(center.x + r * Mathf.Cos(angle), center.y, center.z + r * Mathf.Sin(angle));
                default:
                    var point = center + UnityEngine.Random.insideUnitSphere * zone.radius * scale.x;
                    point.y = center.y;
                    return point;
            }
        }

        public void SpawnAtZoneCenter(LootZone marker, int itemIndex = 0)
        {
            var tpl = GetItemTpl(marker.items, itemIndex);
            var item = GetItem(marker.items, itemIndex);
            var pos = marker.position.ToVector3();
            pos.y += item?.yOffset ?? 0f;
            var rot = marker.rotation.ToQuaternion();
            var rotation = GetZoneItemRotation(marker.items, itemIndex);
            SpawnPreview(tpl, item, pos, rotation, pos, rot, item?.randomRotation != true, marker.name, marker.id);
        }

        private string GetItemTpl(List<LootItem> items, int index)
        {
            if (items != null && index >= 0 && index < items.Count && !string.IsNullOrEmpty(items[index].template))
                return items[index].template;
            return DefaultItemTpl;
        }

        private LootItem GetItem(List<LootItem> items, int index)
        {
            if (items != null && index >= 0 && index < items.Count)
                return items[index];
            return null;
        }

        private Quaternion GetZoneItemRotation(List<LootItem> items, int index = 0)
        {
            var item = GetItem(items, index);
            if (item == null)
                return Quaternion.identity;
            if (item.randomRotation)
                return RandomYRotation();
            return item.rotation.ToQuaternion();
        }

        private static Quaternion RandomYRotation()
        {
            return Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        }

        public void SpawnPreview(string itemTpl, LootItem loot, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId)
        {
            SpawnPreviewInternal(itemTpl, loot, position, rotation, markerPosition, markerRotation, syncRotation, markerName, markerId, true);
        }

        private void SpawnPreviewInternal(string itemTpl, LootItem loot, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId, bool clear, bool reuseIfSame = false)
        {
            string signature = null;
            int generation = 0;
            var requestId = ++_requestCounter;
            var keepOld = false;

            if (clear && !string.IsNullOrEmpty(markerId))
            {
                signature = BuildLootSignature(itemTpl, loot);
                LogDuplicateMarkerPreviews(markerId, itemTpl);
                if (reuseIfSame && TryReuseMarkerPreview(markerId, signature, position, rotation, markerPosition, markerRotation, syncRotation))
                {
                    LogPreviewDiagnostic($"Preview reuse marker={markerId} tpl={itemTpl} request={requestId}; existing matching preview retained.");
                    return;
                }

                keepOld = HasRealPreview(markerId);
                // Keep a valid existing model on screen until the replacement is ready.
                if (!keepOld)
                    ClearByMarkerId(markerId);
                generation = ++_generationCounter;
                _markerGeneration[markerId] = generation;
                _inflightSignature[markerId] = signature;
            }
            else if (clear)
            {
                ClearByMarkerId(markerId);
            }

            LogPreviewDiagnostic($"Preview request marker={markerId ?? "<none>"} name='{markerName}' tpl={itemTpl} request={requestId} generation={generation} requestedPosition={position.ToString("F3")} keepOld={keepOld} existingInstances={CountMarkerPreviews(markerId)}.");
            GameObject fallback = null;
            if (!keepOld)
            {
                fallback = CreateFallbackPreview(itemTpl, position, rotation);
                AttachMeta(fallback, itemTpl, markerName, markerId, true, position - markerPosition, Quaternion.Inverse(markerRotation) * rotation, syncRotation, requestId, generation);
                fallback.GetComponent<PreviewLootMarker>().lootSignature = signature;
                _previews.Add(fallback);
                LogPreviewDiagnostic($"Preview fallback created marker={markerId ?? "<none>"} tpl={itemTpl} request={requestId} generation={generation} instance={fallback.GetInstanceID()} position={fallback.transform.position.ToString("F3")}.");
            }

            _runner.StartCoroutine(LoadRealPreviewCoroutine(itemTpl, loot, position, rotation, markerPosition, markerRotation, syncRotation, markerName, markerId, fallback, generation, signature, requestId));
        }

        private static bool PreviewLifecycleDiagnosticsEnabled =>
            Plugin.EnablePreviewLifecycleDiagnostics != null && Plugin.EnablePreviewLifecycleDiagnostics.Value;

        private static void LogPreviewDiagnostic(string message)
        {
            if (PreviewLifecycleDiagnosticsEnabled)
                Plugin.Log.LogInfo($"[PreviewLifecycle] {message}");
        }

        private int CountMarkerPreviews(string markerId)
        {
            if (string.IsNullOrEmpty(markerId))
                return 0;
            return _previews.Count(preview =>
            {
                if (preview == null)
                    return false;
                var meta = preview.GetComponent<PreviewLootMarker>();
                return meta != null && meta.sourceMarkerId == markerId;
            });
        }

        private void LogDuplicateMarkerPreviews(string markerId, string itemTpl)
        {
            if (!PreviewLifecycleDiagnosticsEnabled || string.IsNullOrEmpty(markerId))
                return;

            var matches = _previews
                .Where(preview => preview != null)
                .Select(preview => new { Preview = preview, Meta = preview.GetComponent<PreviewLootMarker>() })
                .Where(entry => entry.Meta != null && entry.Meta.sourceMarkerId == markerId && entry.Meta.itemTpl == itemTpl)
                .ToList();
            if (matches.Count > 1)
            {
                var instances = string.Join(", ", matches.Select(entry =>
                    $"{entry.Preview.GetInstanceID()}(request={entry.Meta.requestId},generation={entry.Meta.generation},fallback={entry.Meta.isFallback})"));
                if (_duplicateWarningSignatures.Add($"{markerId}|{itemTpl}|{instances}"))
                    Plugin.Log.LogWarning($"[PreviewLifecycle] Duplicate live previews marker={markerId} tpl={itemTpl}: {instances}.");
            }
        }

        private static void LogPreviewPosition(PreviewLootMarker meta, GameObject preview, Vector3 requestedPosition)
        {
            if (!PreviewLifecycleDiagnosticsEnabled || Time.unscaledTime - meta.lastPositionDiagnosticTime < 1f)
                return;
            meta.lastPositionDiagnosticTime = Time.unscaledTime;
            Plugin.Log.LogInfo($"[PreviewLifecycle] Preview position marker={meta.sourceMarkerId} tpl={meta.itemTpl} request={meta.requestId} generation={meta.generation} instance={preview.GetInstanceID()} requested={requestedPosition.ToString("F3")} actual={preview.transform.position.ToString("F3")}.");
        }

        private void LogStaleRequest(string markerId, int generation, int requestId, string itemTpl, GameObject fallback)
        {
            if (!PreviewLifecycleDiagnosticsEnabled || !_staleLoggedRequests.Add(requestId))
                return;
            var instanceId = fallback != null ? fallback.GetInstanceID().ToString() : "<none>";
            var currentGeneration = !string.IsNullOrEmpty(markerId) && _markerGeneration.TryGetValue(markerId, out var latest)
                ? latest.ToString()
                : "<cleared>";
            Plugin.Log.LogInfo($"[PreviewLifecycle] Stale completion rejected marker={markerId ?? "<none>"} tpl={itemTpl} request={requestId} generation={generation} currentGeneration={currentGeneration} fallbackInstance={instanceId}.");
        }

        private static string BuildLootSignature(string itemTpl, LootItem loot)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(itemTpl);
            AppendChildSignature(sb, loot);
            return sb.ToString();
        }

        private static void AppendChildSignature(System.Text.StringBuilder sb, LootItem loot)
        {
            if (loot?.children == null || loot.children.Count == 0)
                return;
            sb.Append('[');
            foreach (var child in loot.children)
            {
                if (child == null)
                    continue;
                sb.Append(child.template).Append('@').Append(child.slotId);
                AppendChildSignature(sb, child);
                sb.Append(';');
            }
            sb.Append(']');
        }

        private bool HasRealPreview(string markerId)
        {
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;
                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta != null && meta.sourceMarkerId == markerId && !meta.isFallback)
                    return true;
            }
            return false;
        }

        // Returns true when the requested item is already shown (or loading) for this marker, so nothing needs rebuilding.
        private bool TryReuseMarkerPreview(string markerId, string signature, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation)
        {
            PreviewLootMarker only = null;
            GameObject onlyObject = null;
            var count = 0;
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;
                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta == null || meta.sourceMarkerId != markerId)
                    continue;
                count++;
                only = meta;
                onlyObject = preview;
            }

            if (count == 0)
                return false;

            if (_inflightSignature.TryGetValue(markerId, out var inflight) && inflight == signature)
                return true;

            if (count != 1 || only.isFallback || only.lootSignature != signature)
                return false;

            onlyObject.transform.position = position;
            onlyObject.transform.rotation = rotation;
            only.offset = position - markerPosition;
            only.rotationOffset = Quaternion.Inverse(markerRotation) * rotation;
            only.syncRotation = syncRotation;
            return true;
        }

        private bool IsRequestCurrent(string markerId, int generation, GameObject fallback, int requestId, string itemTpl)
        {
            if (generation != 0 && (markerId == null || !_markerGeneration.TryGetValue(markerId, out var current) || current != generation))
            {
                LogStaleRequest(markerId, generation, requestId, itemTpl, fallback);
                return false;
            }
            if (fallback != null && !_previews.Contains(fallback))
            {
                LogStaleRequest(markerId, generation, requestId, itemTpl, fallback);
                return false;
            }
            return true;
        }

        private GameObject TrySpawnRealPreview(string itemTpl, LootItem loot, Vector3 position, Quaternion rotation, string markerId, int requestId, int generation)
        {
            try
            {
                var factory = Singleton<ItemFactory>.Instance;
                if (factory == null)
                    return null;

                var item = loot != null ? RuntimeInteractiveObjectSpawner.BuildItem(loot, factory) : factory.CreateItem(MongoID.Generate(true).ToString(), itemTpl, null);
                if (item == null)
                    return null;

                var pool = Singleton<ObjectsFactory>.Instance;
                if (pool == null)
                    return null;

                var preview = pool.CreateLootPrefab(item, ECameraType.Default, null);
                if (preview == null)
                    return null;
                var existingMeta = preview.GetComponents<PreviewLootMarker>();
                var instanceId = preview.GetInstanceID();
                if (PreviewLifecycleDiagnosticsEnabled)
                {
                    var previouslyObserved = !_observedFactoryPreviewInstances.Add(instanceId);
                    LogPreviewDiagnostic($"Preview factory returned instance={instanceId} marker={markerId ?? "<none>"} tpl={itemTpl} request={requestId} generation={generation} instanceObservation={(previouslyObserved ? "seen-before" : "first-seen")} existingPreviewMetadata={existingMeta.Length}.");
                }

                if (IsPlaceholderPreview(preview))
                {
                    ReturnRejectedPreview(preview);
                    return null;
                }

                preview.transform.position = position;
                preview.transform.rotation = rotation;
                preview.transform.localScale = Vector3.one;
                preview.SetActive(true);
                EnableRenderers(preview);
                DisablePhysics(preview);
                return preview;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Real preview not ready for {itemTpl}: {ex.Message}");
                return null;
            }
        }

        private IEnumerator LoadRealPreviewCoroutine(string itemTpl, LootItem loot, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId, GameObject fallback, int generation, string signature, int requestId)
        {
            try
            {
                var task = loot != null ? PreloadBundles(loot) : PreloadBundles(itemTpl);
                while (!task.IsCompleted)
                {
                    yield return null;
                }

                if (task.IsFaulted)
                {
                    Plugin.Log.LogDebug($"Failed to preload asset for {itemTpl}: {task.Exception?.GetBaseException()?.Message}");
                    if (IsRequestCurrent(markerId, generation, fallback, requestId, itemTpl))
                        ShowFailureFallback(itemTpl, position, rotation, markerPosition, markerRotation, syncRotation, markerName, markerId, fallback, generation, signature, requestId);
                    yield break;
                }

                var bundleTokens = task.Result?.BundleTokens;
                if (!IsRequestCurrent(markerId, generation, fallback, requestId, itemTpl))
                {
                    ReleaseBundleTokens(bundleTokens);
                    yield break;
                }

                var offset = position - markerPosition;
                var rotationOffset = Quaternion.Inverse(markerRotation) * rotation;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    if (!IsRequestCurrent(markerId, generation, fallback, requestId, itemTpl))
                    {
                        ReleaseBundleTokens(bundleTokens);
                        yield break;
                    }

                    var real = TrySpawnRealPreview(itemTpl, loot, position, rotation, markerId, requestId, generation);
                    if (real != null)
                    {
                        var replaced = new List<GameObject>();
                        var currentPos = position;
                        var currentRot = rotation;
                        var wasActive = true;
                        if (fallback != null)
                        {
                            currentPos = fallback.transform.position;
                            currentRot = fallback.transform.rotation;
                            wasActive = fallback.activeSelf;
                            replaced.Add(fallback);
                        }

                        if (generation != 0)
                        {
                            foreach (var preview in _previews)
                            {
                                if (preview == null || replaced.Contains(preview))
                                    continue;
                                var oldMeta = preview.GetComponent<PreviewLootMarker>();
                                if (oldMeta == null || oldMeta.sourceMarkerId != markerId)
                                    continue;
                                if (replaced.Count == 0)
                                    wasActive = preview.activeSelf;
                                replaced.Add(preview);
                            }
                        }

                        AttachMeta(real, itemTpl, markerName, markerId, false, offset, rotationOffset, syncRotation, requestId, generation);
                        var meta = real.GetComponent<PreviewLootMarker>();
                        meta.bundleTokens = bundleTokens;
                        meta.lootSignature = signature;
                        real.transform.position = currentPos;
                        real.transform.rotation = currentRot;
                        if (!wasActive)
                            real.SetActive(false);
                        _previews.Add(real);

                        // New model is already in place; retire the old ones in the same frame.
                        foreach (var old in replaced)
                        {
                            var oldMeta = old != null ? old.GetComponent<PreviewLootMarker>() : null;
                            LogPreviewDiagnostic($"Preview replaced marker={markerId ?? "<none>"} oldInstance={(old != null ? old.GetInstanceID().ToString() : "<none>")} oldRequest={(oldMeta != null ? oldMeta.requestId.ToString() : "<unknown>")} newInstance={real.GetInstanceID()} request={requestId} generation={generation}.");
                            _previews.Remove(old);
                            DestroyPreview(old);
                        }
                        LogPreviewDiagnostic($"Preview real model ready marker={markerId ?? "<none>"} tpl={itemTpl} request={requestId} generation={generation} instance={real.GetInstanceID()} position={real.transform.position.ToString("F3")}.");
                        LogDuplicateMarkerPreviews(markerId, itemTpl);
                        yield break;
                    }

                    yield return null;
                }

                ReleaseBundleTokens(bundleTokens);
                Plugin.Log.LogWarning($"Could not load real preview model for template {itemTpl}; keeping fallback sphere.");
                if (IsRequestCurrent(markerId, generation, fallback, requestId, itemTpl))
                    ShowFailureFallback(itemTpl, position, rotation, markerPosition, markerRotation, syncRotation, markerName, markerId, fallback, generation, signature, requestId);
            }
            finally
            {
                if (generation != 0 && markerId != null && _markerGeneration.TryGetValue(markerId, out var latest) && latest == generation)
                    _inflightSignature.Remove(markerId);
            }
        }

        // A genuine load failure: drop any stale previous model (it shows a different item) and show the placeholder sphere.
        private void ShowFailureFallback(string itemTpl, Vector3 position, Quaternion rotation, Vector3 markerPosition, Quaternion markerRotation, bool syncRotation, string markerName, string markerId, GameObject existingFallback, int generation, string signature, int requestId)
        {
            if (existingFallback != null || generation == 0)
                return;

            var wasActive = true;
            for (int i = _previews.Count - 1; i >= 0; i--)
            {
                var preview = _previews[i];
                if (preview == null)
                    continue;
                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta == null || meta.sourceMarkerId != markerId)
                    continue;
                wasActive = preview.activeSelf;
                _previews.RemoveAt(i);
                DestroyPreview(preview);
            }

            var fallback = CreateFallbackPreview(itemTpl, position, rotation);
            AttachMeta(fallback, itemTpl, markerName, markerId, true, position - markerPosition, Quaternion.Inverse(markerRotation) * rotation, syncRotation, requestId, generation);
            fallback.GetComponent<PreviewLootMarker>().lootSignature = signature;
            if (!wasActive)
                fallback.SetActive(false);
            _previews.Add(fallback);
            LogPreviewDiagnostic($"Preview failure fallback marker={markerId} tpl={itemTpl} request={requestId} generation={generation} instance={fallback.GetInstanceID()} position={fallback.transform.position.ToString("F3")}.");
        }

        private sealed class PreloadResult
        {
            public object BundleTokens;
        }

        private async Task<PreloadResult> PreloadBundles(string itemTpl)
        {
            return await PreloadBundles(new LootItem { template = itemTpl });
        }

        private async Task<PreloadResult> PreloadBundles(LootItem loot)
        {
            var result = new PreloadResult();
            var pool = Singleton<ObjectsFactory>.Instance;
            if (pool == null)
                return result;

            var factory = Singleton<ItemFactory>.Instance;
            if (factory == null)
                return result;

            // Native EFT resolution of every bundle the item (and its children) needs.
            // Approach adapted from Ultimate Loot Editor (ULE) by GoatBoy (MIT License).
            try
            {
                var item = RuntimeInteractiveObjectSpawner.BuildItem(loot, factory);
                if (item != null)
                {
                    var tokens = item.GetAllBundleTokens();
                    result.BundleTokens = tokens;
                    await EasyAssetsExtensions.LoadBundles(tokens);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Native bundle load failed for {loot?.template}: {ex.GetBaseException().Message}");
            }

            var keys = new List<ResourceKey>();
            CollectResourceKeys(loot, factory, keys);
            if (keys.Count == 0)
                return result;

            await pool.LoadBundlesAndCreatePools(
                ObjectsFactory.PoolsCategory.Raid,
                ObjectsFactory.AssemblyType.Local,
                keys.ToArray(),
                JobYieldPriority.Immediate,
                null,
                CancellationToken.None);
            return result;
        }

        private static void ReleaseBundleTokens(object tokens)
        {
            if (tokens == null)
                return;
            try
            {
                tokens.GetType().GetMethod("Release", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(tokens, null);
            }
            catch
            {
            }
        }

        private static bool IsPlaceholderPreview(GameObject go)
        {
            return go != null && go.name != null && go.name.IndexOf("Doge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ReturnRejectedPreview(GameObject go)
        {
            if (go == null)
                return;
            try
            {
                go.SetActive(false);
                AssetPoolObject.ReturnToPool(go, true);
            }
            catch
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        private static bool IsValidKey(ResourceKey key)
        {
            return !string.IsNullOrEmpty(key.path);
        }

        private static void CollectResourceKeys(LootItem loot, ItemFactory factory, List<ResourceKey> keys)
        {
            if (loot == null || string.IsNullOrEmpty(loot.template))
                return;

            if (factory.ItemTemplates.TryGetValue(loot.template, out var template))
            {
                if (template?.Prefab != null && IsValidKey(template.Prefab))
                    keys.Add(template.Prefab);
                if (template?.UsePrefab != null && IsValidKey(template.UsePrefab))
                    keys.Add(template.UsePrefab);
            }

            if (loot.children == null || loot.children.Count == 0)
                return;

            foreach (var child in loot.children)
                CollectResourceKeys(child, factory, keys);
        }

        private void AttachMeta(GameObject preview, string itemTpl, string markerName, string markerId, bool fallback, Vector3 offset, Quaternion rotationOffset, bool syncRotation, int requestId, int generation)
        {
            var existing = preview.GetComponents<PreviewLootMarker>();
            if (existing.Length > 0 && PreviewLifecycleDiagnosticsEnabled)
            {
                var previous = existing[0];
                Plugin.Log.LogWarning($"[PreviewLifecycle] Reused prefab instance={preview.GetInstanceID()} already has {existing.Length} preview metadata component(s); previous marker={previous.sourceMarkerId} tpl={previous.itemTpl} request={previous.requestId}.");
            }
            var meta = preview.AddComponent<PreviewLootMarker>();
            meta.itemTpl = itemTpl;
            meta.markerName = markerName;
            meta.sourceMarkerId = markerId;
            meta.requestId = requestId;
            meta.generation = generation;
            meta.previewOnly = true;
            meta.isFallback = fallback;
            meta.syncRotation = syncRotation;
            meta.offset = offset;
            meta.rotationOffset = rotationOffset;
        }

        private GameObject CreateFallbackPreview(string itemTpl, Vector3 position, Quaternion rotation)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root.transform, false);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one * 0.25f;
            var renderer = go.GetComponent<Renderer>();
            var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = new Color(0.2f, 0.9f, 1f, 0.8f);
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;
            return go;
        }

        private void DisablePhysics(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                rb.isKinematic = true;

            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        private void EnableRenderers(GameObject go)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = true;
        }

        public void DrawLabels(bool editorOpen)
        {
            if (!editorOpen)
                return;

            var camera = Camera.main;
            if (camera == null)
                return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.yellow }
            };

            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewLootMarker>();
                var label = meta != null ? $"{meta.markerName}\n{meta.itemTpl}" : Locale.Get("preview");
                var screenPos = camera.WorldToScreenPoint(preview.transform.position);
                if (screenPos.z <= 0)
                    continue;
                if (CustomEditorUI.IsScreenPointOverEditorUI(new Vector2(screenPos.x, screenPos.y + 30f)))
                    continue;

                var rect = new Rect(screenPos.x - 60f, Screen.height - screenPos.y - 45f, 120f, 30f);
                GUI.Label(rect, label, style);
            }

            foreach (var preview in _effectPreviews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewEffectMarker>();
                var label = meta != null ? $"{meta.markerName}\n{meta.label}" : Locale.Get("effect");
                var screenPos = camera.WorldToScreenPoint(preview.transform.position);
                if (screenPos.z <= 0)
                    continue;
                if (CustomEditorUI.IsScreenPointOverEditorUI(new Vector2(screenPos.x, screenPos.y + 30f)))
                    continue;

                var rect = new Rect(screenPos.x - 60f, Screen.height - screenPos.y - 45f, 120f, 30f);
                GUI.Label(rect, label, style);
            }
        }

        public void UpdateForMarker(MarkerBase marker)
        {
            if (marker == null)
                return;

            var markerPos = marker.position.ToVector3();
            var markerRot = marker.rotation.ToQuaternion();
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta == null || meta.sourceMarkerId != marker.id)
                    continue;

                var requestedPosition = markerPos + markerRot * meta.offset;
                // Loose loot: world-space offset (zero when gravity is off). With gravity on, the ground under the CURRENT marker position is re-queried so the preview tracks the marker live.
                // Zone previews store a marker-local offset.
                if (marker is LooseLootSpawn loose)
                {
                    if (loose.useGravity)
                    {
                        var ground = GetPreviewGroundPosition(markerPos);
                        if (ground.HasValue)
                            meta.offset = ground.Value - markerPos;
                        requestedPosition = markerPos + meta.offset;
                        preview.transform.position = requestedPosition;
                        if (preview.activeSelf != ground.HasValue)
                            preview.SetActive(ground.HasValue);
                    }
                    else
                    {
                        meta.offset = Vector3.zero;
                        requestedPosition = markerPos;
                        preview.transform.position = requestedPosition;
                        if (!preview.activeSelf)
                            preview.SetActive(true);
                    }
                }
                else
                {
                    requestedPosition = markerPos + markerRot * meta.offset;
                    preview.transform.position = requestedPosition;
                }

                if (meta.syncRotation)
                    preview.transform.rotation = markerRot * meta.rotationOffset;
                LogPreviewPosition(meta, preview, requestedPosition);
            }

            foreach (var preview in _staticPreviews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewStaticObjectMarker>();
                if (meta == null || meta.sourceMarkerId != marker.id)
                    continue;

                preview.transform.position = markerPos;
                preview.transform.rotation = markerRot;
                if (marker is StaticObject so)
                    preview.transform.localScale = so.scale.ToVector3();
            }

            foreach (var preview in _effectPreviews)
            {
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewEffectMarker>();
                if (meta == null || meta.sourceMarkerId != marker.id)
                    continue;

                if (marker is LightZone lz)
                {
                    preview.transform.position = markerPos;
                    preview.transform.rotation = markerRot;
                    var light = preview.GetComponent<Light>();
                    if (light != null)
                    {
                        light.type = ParseLightType(lz.lightType);
                        light.color = lz.color.ToColor().linear;
                        light.intensity = lz.intensity;
                        light.range = lz.range;
                        light.shadows = ParseLightShadows(lz.shadows);
                        light.shadowStrength = lz.shadowStrength;
                        light.shadowBias = lz.shadowBias;
                        light.shadowNormalBias = lz.shadowNormalBias;
                        if (light.type == LightType.Spot)
                            light.spotAngle = lz.spotAngle;
                    }
                    var sphere = preview.transform.Find("LightPreviewVisual")?.GetComponent<Renderer>();
                    if (sphere != null)
                    {
                        sphere.material.color = light.color;
                        sphere.material.SetColor("_EmissionColor", light.color * 2f);
                    }
                    var dirLr = preview.transform.Find("LightPreviewDirection")?.GetComponent<LineRenderer>();
                    if (dirLr != null)
                        DrawLightDirection(dirLr, lz);
                }
            }
        }

        public void SpawnAllPreviews(MapData data)
        {
            if (data == null)
                return;
            foreach (var marker in data.lootSpawns ?? Enumerable.Empty<LooseLootSpawn>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.lootZones ?? Enumerable.Empty<LootZone>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.objects ?? Enumerable.Empty<StaticObject>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.wttStaticObjects ?? Enumerable.Empty<WTTStaticObject>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.interactiveObjects ?? Enumerable.Empty<InteractiveObject>())
                SpawnPreviewForMarker(marker);
            foreach (var marker in data.lightZones ?? Enumerable.Empty<LightZone>())
                SpawnPreviewForMarker(marker);
        }

        public void ClearAll(bool clearStaticSources = false)
        {
            foreach (var preview in _previews)
            {
                if (preview == null)
                    continue;

                DestroyPreview(preview);
            }
            _previews.Clear();
            _markerGeneration.Clear();
            _inflightSignature.Clear();
            _staleLoggedRequests.Clear();
            _duplicateWarningSignatures.Clear();

            foreach (var preview in _staticPreviews)
            {
                if (preview != null)
                    UnityEngine.Object.Destroy(preview);
            }
            _staticPreviews.Clear();

            foreach (var preview in _effectPreviews)
            {
                if (preview != null)
                    UnityEngine.Object.Destroy(preview);
            }
            _effectPreviews.Clear();

            if (clearStaticSources)
            {
                _staticSources.Clear();
                foreach (var kvp in _fallbackCache)
                {
                    if (kvp.Value != null)
                        UnityEngine.Object.Destroy(kvp.Value);
                }
                _fallbackCache.Clear();
            }
        }

        public void ClearByMarkerId(string markerId)
        {
            if (string.IsNullOrEmpty(markerId))
                return;

            _markerGeneration.Remove(markerId);
            _inflightSignature.Remove(markerId);
            _duplicateWarningSignatures.RemoveWhere(signature => signature.StartsWith(markerId + "|", StringComparison.Ordinal));

            for (int i = _previews.Count - 1; i >= 0; i--)
            {
                var preview = _previews[i];
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewLootMarker>();
                if (meta != null && meta.sourceMarkerId == markerId)
                {
                    _previews.RemoveAt(i);
                    DestroyPreview(preview);
                }
            }

            for (int i = _staticPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _staticPreviews[i];
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewStaticObjectMarker>();
                if (meta != null && meta.sourceMarkerId == markerId)
                {
                    _staticPreviews.RemoveAt(i);
                    UnityEngine.Object.Destroy(preview);
                }
            }

            for (int i = _effectPreviews.Count - 1; i >= 0; i--)
            {
                var preview = _effectPreviews[i];
                if (preview == null)
                    continue;

                var meta = preview.GetComponent<PreviewEffectMarker>();
                if (meta != null && meta.sourceMarkerId == markerId)
                {
                    _effectPreviews.RemoveAt(i);
                    UnityEngine.Object.Destroy(preview);
                }
            }
        }

        public void SpawnPreviewForMarker(MarkerBase marker)
        {
            if (marker == null)
                return;
            switch (marker.Kind)
            {
                case MarkerKind.LooseLoot:
                    SpawnAtMarker((LooseLootSpawn)marker);
                    break;
                case MarkerKind.LootZone:
                    SpawnAtZoneCenter((LootZone)marker);
                    break;
                case MarkerKind.StaticObject:
                    SpawnStaticPreview((StaticObject)marker);
                    break;
                case MarkerKind.WTTStaticObject:
                    SpawnWTTStaticPreview((WTTStaticObject)marker);
                    break;
                case MarkerKind.InteractiveObject:
                    SpawnInteractivePreview((InteractiveObject)marker);
                    break;
                case MarkerKind.LightZone:
                    SpawnLightPreview((LightZone)marker);
                    break;
                case MarkerKind.PmcSpawnZone:
                    SpawnPmcSpawnZonePreview((PmcSpawnZone)marker);
                    break;
            }
        }

        private static string GetStaticSourceKey(string name, Vector3 position)
        {
            return $"{name}@{position.x:F4},{position.y:F4},{position.z:F4}";
        }

        private void CacheSourceInstance(string key, GameObject source)
        {
            if (source == null || _fallbackCache.Values.Contains(source))
                return;
            if (_fallbackCache.TryGetValue(key, out var existing) && existing != null)
                return;
            var backup = UnityEngine.Object.Instantiate(source);
            backup.name = $"CachedSource_{key}";
            backup.transform.SetParent(_root.transform, false);
            backup.transform.position = Vector3.zero;
            backup.SetActive(false);
            _fallbackCache[key] = backup;
        }

        private void PreparePreviewInstance(GameObject instance)
        {
            if (instance == null)
                return;

            foreach (var behaviour in instance.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour != null && !(behaviour is PreviewStaticObjectMarker))
                    behaviour.enabled = false;
            }

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                if (t != null)
                    t.gameObject.SetActive(true);
            }

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null)
                    renderer.enabled = true;
            }
        }

        public void RegisterStaticSource(string markerId, GameObject source)
        {
            if (source == null) return;
            var key = GetStaticSourceKey(source.name, source.transform.position);
            _staticSources[key] = source;
            CacheSourceInstance(key, source);
        }

        public void SpawnSourcePreview(MarkerBase marker, GameObject source)
        {
            if (marker == null || source == null)
                return;

            ClearByMarkerId(marker.id);

            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"SourcePreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            var scale = Vector3.one;
            if (marker is StaticObject so) scale = so.scale.ToVector3();
            else if (marker is WTTStaticObject wtt) scale = wtt.scale.ToVector3();
            else if (marker is InteractiveObject io) scale = io.scale.ToVector3();
            else if (marker is CutVolume cv) scale = cv.scale.ToVector3();
            instance.transform.localScale = scale;

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = (marker as IHasSourceObject)?.sourceObjectName ?? "";
            meta.isFallback = true;

            _staticPreviews.Add(instance);

            var key = GetStaticSourceKey(source.name, source.transform.position);
            _staticSources[key] = source;
            CacheSourceInstance(key, source);
        }

        public void SpawnStaticPreview(StaticObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview static object: marker is null.");
                return;
            }

            ClearByMarkerId(marker.id);

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnStaticInstance(cached, marker, true);
                    return;
                }
            }
            if (!string.IsNullOrEmpty(marker.prefabPath))
            {
                if (_fallbackCache.TryGetValue(marker.prefabPath, out var cached) && cached != null)
                {
                    SpawnStaticInstance(cached, marker, false);
                    return;
                }
            }

            if (string.IsNullOrEmpty(marker.prefabPath) && string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview static object: no prefab path or source object set.");
                return;
            }

            _runner.StartCoroutine(LoadStaticPreviewCoroutine(marker));
        }

        public void SpawnWTTStaticPreview(WTTStaticObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview WTT static object: marker is null.");
                return;
            }

            ClearByMarkerId(marker.id);

            if (marker.spawnType == "clone" && !string.IsNullOrEmpty(marker.sourceObjectName))
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnWTTStaticInstance(cached, marker, true);
                    return;
                }
            }
            if (marker.spawnType == "bundle" && !string.IsNullOrEmpty(marker.bundleName) && !string.IsNullOrEmpty(marker.prefabName))
            {
                var cacheKey = $"{marker.bundleName}/{marker.prefabName}";
                if (_fallbackCache.TryGetValue(cacheKey, out var cached) && cached != null)
                {
                    SpawnWTTStaticInstance(cached, marker, false);
                    return;
                }
            }

            if (marker.spawnType == "clone" && string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview WTT static object: clone mode but no source object set.");
                return;
            }
            if (marker.spawnType == "bundle" && (string.IsNullOrEmpty(marker.bundleName) || string.IsNullOrEmpty(marker.prefabName)))
            {
                Plugin.Log.LogWarning("Cannot preview WTT static object: bundle mode but bundle/prefab name not set.");
                return;
            }

            _runner.StartCoroutine(LoadWTTStaticPreviewCoroutine(marker));
        }

        private IEnumerator LoadWTTStaticPreviewCoroutine(WTTStaticObject marker)
        {
            if (marker.spawnType == "clone")
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_staticSources.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnWTTStaticInstance(cached, marker, true);
                    yield break;
                }

                var source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (source != null)
                {
                    _staticSources[sourceKey] = source;
                    SpawnWTTStaticInstance(source, marker, true);
                    yield break;
                }

                Plugin.Log.LogWarning($"Could not find source scene object for WTT static preview: {marker.sourceObjectName}.");
                yield break;
            }

            // Bundle mode: load the bundle from the mod's bundles folder.
            string path = Path.Combine(Application.streamingAssetsPath, "Windows", "bundles", "staticspawns", $"{marker.bundleName}.bundle");

            AssetBundle bundle = null;
            bool bundleOwned = false;
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (b.name == marker.bundleName || b.name == $"{marker.bundleName}.bundle")
                {
                    bundle = b;
                    break;
                }
            }

            if (bundle == null)
            {
                var request = AssetBundle.LoadFromFileAsync(path);
                yield return request;
                bundle = request.assetBundle;
                bundleOwned = true;
            }

            if (bundle != null)
            {
                var assetRequest = bundle.LoadAllAssetsAsync<GameObject>();
                yield return assetRequest;

                var prefab = assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault(a => a.name == marker.prefabName);
                prefab ??= assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault();

                if (prefab != null)
                {
                    SpawnWTTStaticInstance(prefab, marker, false);
                }
                else
                {
                    Plugin.Log.LogWarning($"No GameObject asset found in WTT bundle: {path}.");
                }

                if (bundleOwned)
                    bundle.Unload(false);
            }
            else
            {
                Plugin.Log.LogWarning($"Failed to load WTT static object bundle: {path}.");
            }
        }

        public void SpawnInteractivePreview(InteractiveObject marker)
        {
            if (marker == null)
            {
                Plugin.Log.LogWarning("Cannot preview interactive object: marker is null.");
                return;
            }

            ClearByMarkerId(marker.id);

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                {
                    SpawnInteractiveInstance(cached, marker, true);
                    return;
                }
            }

            if (string.IsNullOrEmpty(marker.sourceObjectName))
            {
                Plugin.Log.LogWarning("Cannot preview interactive object: no source object set.");
                return;
            }

            _runner.StartCoroutine(LoadInteractivePreviewCoroutine(marker));
        }

        private IEnumerator LoadInteractivePreviewCoroutine(InteractiveObject marker)
        {
            var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            if (_staticSources.TryGetValue(sourceKey, out var cached) && cached != null)
            {
                SpawnInteractiveInstance(cached, marker, true);
                yield break;
            }

            var source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            if (source != null)
            {
                _staticSources[sourceKey] = source;
                SpawnInteractiveInstance(source, marker, true);
                yield break;
            }

            Plugin.Log.LogWarning($"Could not find source scene object for interactive preview: {marker.sourceObjectName}.");
        }

        private IEnumerator LoadStaticPreviewCoroutine(StaticObject marker)
        {
            GameObject source = null;
            var sourceKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            if (!string.IsNullOrEmpty(marker.sourceObjectName) && _staticSources.TryGetValue(sourceKey, out source) && source != null)
            {
                SpawnStaticInstance(source, marker, true);
                yield break;
            }

            if (!string.IsNullOrEmpty(marker.sourceObjectName))
            {
                source = FindSourceObject(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
                if (source != null)
                {
                    _staticSources[sourceKey] = source;
                    SpawnStaticInstance(source, marker, true);
                    yield break;
                }
                Plugin.Log.LogWarning($"Could not find source scene object: {marker.sourceObjectName}, trying prefab path.");
            }

            if (!string.IsNullOrEmpty(marker.prefabPath))
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Windows", marker.prefabPath.TrimStart('/'));

                AssetBundle bundle = null;
                bool bundleOwned = false;
                string fileName = Path.GetFileName(path);
                foreach (var b in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (b.name == fileName || b.name == marker.prefabPath)
                    {
                        bundle = b;
                            break;
                    }
                }

                if (bundle == null)
                {
                    var request = AssetBundle.LoadFromFileAsync(path);
                    yield return request;

                    bundle = request.assetBundle;
                    bundleOwned = true;
                }

                if (bundle != null)
                {
                    var assetRequest = bundle.LoadAllAssetsAsync<GameObject>();
                    yield return assetRequest;

                    var prefab = assetRequest.allAssets?.OfType<GameObject>().FirstOrDefault();
                    if (prefab != null)
                    {
                        SpawnStaticInstance(prefab, marker, false);
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"No GameObject asset found in bundle: {path}.");
                    }

                    if (bundleOwned)
                        bundle.Unload(false);
                }
                else
                {
                    Plugin.Log.LogWarning($"Failed to load static object bundle: {path}.");
                }
                yield break;
            }

            Plugin.Log.LogWarning($"Cannot preview static object {marker.name}: no prefab path or source object set.");
        }

        public GameObject FindSourceObject(string name, Vector3 position)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var sourceKey = GetStaticSourceKey(name, position);
            if (_fallbackCache.TryGetValue(sourceKey, out var cached) && cached != null)
                return cached;

            GameObject best = null;
            float bestDist = float.MaxValue;
            var sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            int candidates = 0;

            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == _root) continue;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name != name) continue;
                        candidates++;
                        var dist = (t.position - position).sqrMagnitude;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = t.gameObject;
                        }
                    }
                }
            }

            if (best != null)
            {
                return best;
            }

            // Fallback: source object may have moved (e.g., opened door). Try matching by name only.
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == _root) continue;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            return t.gameObject;
                        }
                    }
                }
            }

            Plugin.Log.LogWarning($"No source object named '{name}' found near {position}. Scanned {candidates} candidates.");
            return null;
        }

        public GameObject GetPreviewInstance(MarkerBase marker)
        {
            if (marker == null)
                return null;
            foreach (var go in _staticPreviews)
            {
                if (go == null)
                    continue;
                var meta = go.GetComponent<PreviewStaticObjectMarker>();
                if (meta != null && meta.sourceMarkerId == marker.id)
                    return go;
            }
            return null;
        }

        private void SpawnStaticInstance(GameObject source, StaticObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"StaticPreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = marker.prefabPath;
            meta.isFallback = isFallback;

            PreparePreviewInstance(instance);
            ApplyChildOverrides(instance, marker.childInteractables);
            ComponentHelper.ApplyAddedComponents(instance, marker.addedComponents);

            _staticPreviews.Add(instance);

            var cacheKey = !string.IsNullOrEmpty(marker.prefabPath) ? marker.prefabPath : GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            CacheSourceInstance(cacheKey, source);
        }

        private void SpawnWTTStaticInstance(GameObject source, WTTStaticObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"WTTStaticPreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = $"{marker.bundleName}/{marker.prefabName}";
            meta.isFallback = isFallback;

            PreparePreviewInstance(instance);
            ApplyChildOverrides(instance, marker.childInteractables);
            ComponentHelper.ApplyAddedComponents(instance, marker.addedComponents);

            _staticPreviews.Add(instance);

            var cacheKey = marker.spawnType == "bundle" ? $"{marker.bundleName}/{marker.prefabName}" : GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            CacheSourceInstance(cacheKey, source);
        }

        private void ApplyChildOverrides(GameObject instance, List<ChildInteractableData> overrides)
        {
            if (overrides == null)
                return;

            foreach (var childOverride in overrides)
            {
                if (string.IsNullOrEmpty(childOverride.childPath))
                    continue;

                var child = instance.transform.Find(childOverride.childPath);
                if (child == null)
                    continue;

                if (childOverride.deleted)
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                if (!string.IsNullOrEmpty(childOverride.keyId))
                {
                    var wio = child.GetComponent<WorldInteractiveObject>();
                    if (wio != null)
                        wio.KeyId = childOverride.keyId;
                }

                if (!string.IsNullOrEmpty(childOverride.containerId))
                {
                    var lootable = child.GetComponent<LootableContainer>();
                    if (lootable != null)
                        lootable.Id = childOverride.containerId;
                }

                if (childOverride.position != null)
                    child.localPosition = childOverride.position.ToVector3();
                if (childOverride.rotation != null)
                    child.localEulerAngles = childOverride.rotation.ToVector3();
                if (childOverride.scale != null)
                    child.localScale = childOverride.scale.ToVector3();

                ComponentHelper.ApplyAddedComponents(child.gameObject, childOverride.addedComponents);
            }
        }

        private void SpawnInteractiveInstance(GameObject source, InteractiveObject marker, bool isFallback)
        {
            var instance = UnityEngine.Object.Instantiate(source);
            instance.name = $"InteractivePreview_{marker.name}";
            instance.SetActive(true);
            instance.transform.SetParent(_root.transform, false);
            instance.transform.position = marker.position.ToVector3();
            instance.transform.rotation = marker.rotation.ToQuaternion();
            instance.transform.localScale = marker.scale.ToVector3();

            var wio = instance.GetComponent<WorldInteractiveObject>();
            if (wio != null && marker.interactiveType == InteractiveObjectType.Door && !string.IsNullOrEmpty(marker.keyId))
                wio.KeyId = marker.keyId;

            var lootable = instance.GetComponent<LootableContainer>();
            if (lootable != null && marker.interactiveType == InteractiveObjectType.Container && !string.IsNullOrEmpty(marker.containerId))
                lootable.Id = marker.containerId;

            var meta = instance.AddComponent<PreviewStaticObjectMarker>();
            meta.sourceMarkerId = marker.id;
            meta.prefabPath = marker.sourceObjectName;
            meta.isFallback = isFallback;

            PreparePreviewInstance(instance);
            ComponentHelper.ApplyAddedComponents(instance, marker.addedComponents);

            _staticPreviews.Add(instance);

            var cacheKey = GetStaticSourceKey(marker.sourceObjectName, marker.sourceObjectPosition.ToVector3());
            CacheSourceInstance(cacheKey, source);
        }

        private void DestroyPreview(GameObject preview)
        {
            var meta = preview.GetComponent<PreviewLootMarker>();
            var staticMeta = preview.GetComponent<PreviewStaticObjectMarker>();
            LogPreviewDiagnostic($"Preview destroyed/returned marker={(meta != null ? meta.sourceMarkerId : staticMeta != null ? staticMeta.sourceMarkerId : "<unknown>")} tpl={(meta != null ? meta.itemTpl : "<non-loot>")} request={(meta != null ? meta.requestId.ToString() : "<unknown>")} instance={preview.GetInstanceID()} fallback={(meta != null && meta.isFallback)}.");
            if ((meta != null && meta.isFallback) || (staticMeta != null && staticMeta.isFallback))
            {
                UnityEngine.Object.Destroy(preview);
            }
            else
            {
                try { AssetPoolObject.ReturnToPool(preview, true); }
                catch { UnityEngine.Object.Destroy(preview); }
            }

            if (meta != null && meta.bundleTokens != null)
            {
                ReleaseBundleTokens(meta.bundleTokens);
                meta.bundleTokens = null;
            }
        }
    }

    public class PreviewLootMarker : MonoBehaviour
    {
        public string itemTpl;
        public string markerName;
        public string sourceMarkerId;
        public int requestId;
        public int generation;
        public bool previewOnly = true;
        public bool isFallback = false;
        public bool syncRotation = true;
        public object bundleTokens;
        public string lootSignature;
        public Vector3 offset;
        public Quaternion rotationOffset;
        public float lastPositionDiagnosticTime;
    }

    public class PreviewStaticObjectMarker : MonoBehaviour
    {
        public string sourceMarkerId;
        public string prefabPath;
        public bool isFallback;
    }

    public class PreviewEffectMarker : MonoBehaviour
    {
        public string sourceMarkerId;
        public string markerName;
        public string label;
    }

    public class CoroutineRunner : MonoBehaviour
    {
    }
}
