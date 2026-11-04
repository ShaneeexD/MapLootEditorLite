#region RuntimeLooseLootProvider.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using UltimateLootEditor.Shared;
using UnityEngine;

namespace ULE.SpawnEditor
{
    internal static class RuntimeLooseLootProvider
    {
        private const string RoutePrefix = "/ule/runtime/loose-loot";
        private static readonly MapLoadCache<RuntimeMapLoadResult> Cache =
            new MapLoadCache<RuntimeMapLoadResult>(LoadMapSnapshot, result => result != null && result.Success);

        internal static void WarmMap(string mapId)
        {
            if (!string.IsNullOrWhiteSpace(mapId))
            {
                Cache.Get(mapId);
            }
        }

        internal static Task<RuntimeMapLoadResult> LoadMapAsync(string mapId)
        {
            return string.IsNullOrWhiteSpace(mapId)
                ? Task.FromResult(RuntimeMapLoadResult.Failure("Map id is missing."))
                : Cache.Get(mapId);
        }

        internal static void ReleaseMap(string mapId, Task<RuntimeMapLoadResult> expectedTask = null)
        {
            Cache.Release(mapId, expectedTask);
        }

        private static RuntimeMapLoadResult LoadMapSnapshot(string mapId, CancellationToken token)
        {
            var route = $"{RoutePrefix}/{Uri.EscapeDataString(mapId)}/snapshot";
            try
            {
                token.ThrowIfCancellationRequested();
                var json = RequestHandler.GetJson(route);
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    return RuntimeMapLoadResult.Failure("Runtime loose-loot snapshot returned no data.");
                }

                if (TryReadError(json, out var error))
                {
                    return RuntimeMapLoadResult.Failure(error);
                }

                token.ThrowIfCancellationRequested();
                var snapshot = JsonConvert.DeserializeObject<RuntimeLooseLootMapSnapshot>(json);
                if (snapshot?.Spawns == null)
                {
                    return RuntimeMapLoadResult.Failure("Runtime loose-loot snapshot returned invalid data.");
                }

                token.ThrowIfCancellationRequested();
                var spawns = snapshot.Spawns
                    .Where(spawn => spawn != null && !string.IsNullOrWhiteSpace(spawn.Id))
                    .Select(spawn => { token.ThrowIfCancellationRequested(); return ConvertSpawn(spawn); })
                    .ToList();
                return RuntimeMapLoadResult.Successful(spawns, route);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return RuntimeMapLoadResult.Failure($"Runtime loose-loot snapshot request failed: {ex.Message}");
            }
        }

        private static SpawnPointData ConvertSpawn(RuntimeLooseLootSpawnSnapshot source)
        {
            var items = (source.Items ?? new List<UleItemEntry>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Tpl))
                .Select(ConvertItem)
                .ToList();

            var id = NormalizeIdentifier(source.Id);
            return new SpawnPointData
            {
                Id = id,
                DetailKey = id,
                Name = string.IsNullOrWhiteSpace(source.Name) ? id : source.Name,
                Position = ToUnity(source.Position),
                Rotation = ToUnity(source.Rotation),
                SpawnChance = Mathf.Clamp01((float)source.SpawnChance),
                HasAlwaysSpawnFlag = true,
                IsAlwaysSpawn = source.IsAlwaysSpawn,
                UseGravity = source.UseGravity,
                ItemCountSummary = source.ItemCount > 0 ? source.ItemCount : items.Count,
                DetailsLoaded = true,
                DataVersion = 1,
                Items = items
            };
        }

        private static LootItem ConvertItem(UleItemEntry source)
        {
            return new LootItem
            {
                Tpl = NormalizeIdentifier(source.Tpl),
                ComposedKey = source.ComposedKey,
                Weight = (float)source.Weight,
                SlotId = source.SlotId,
                LocationJson = source.LocationJson,
                UpdJson = source.UpdJson,
                StackMin = source.StackMin,
                StackMax = source.StackMax,
                PresetId = source.PresetId,
                PresetName = source.PresetName,
                Children = (source.Children ?? new List<UleItemNode>())
                    .Where(child => child != null && !string.IsNullOrWhiteSpace(child.Tpl))
                    .Select(ConvertNode)
                    .ToList()
            };
        }

        private static LootItemNode ConvertNode(UleItemNode source)
        {
            return new LootItemNode
            {
                Tpl = NormalizeIdentifier(source.Tpl),
                SlotId = source.SlotId,
                LocationJson = source.LocationJson,
                UpdJson = source.UpdJson,
                StackMin = source.StackMin,
                StackMax = source.StackMax,
                Children = (source.Children ?? new List<UleItemNode>())
                    .Where(child => child != null && !string.IsNullOrWhiteSpace(child.Tpl))
                    .Select(ConvertNode)
                    .ToList()
            };
        }

        private static Vector3 ToUnity(UleVector3 source)
        {
            return new Vector3(
                (float)(source?.X ?? 0d),
                (float)(source?.Y ?? 0d),
                (float)(source?.Z ?? 0d));
        }

        private static string NormalizeIdentifier(string value)
        {
            var identifier = value?.Trim() ?? string.Empty;
            return identifier.Length >= 2 && identifier[0] == '"' && identifier[identifier.Length - 1] == '"'
                ? identifier.Substring(1, identifier.Length - 2)
                : identifier;
        }

        private static bool TryReadError(string json, out string error)
        {
            error = string.Empty;
            try
            {
                var value = JObject.Parse(json);
                if (!value.TryGetValue("error", StringComparison.OrdinalIgnoreCase, out var errorToken) ||
                    errorToken == null ||
                    errorToken.Type == JTokenType.Null)
                {
                    return false;
                }

                error = errorToken.ToString();
                return !string.IsNullOrWhiteSpace(error);
            }
            catch (JsonReaderException)
            {
                return false;
            }
        }

        internal sealed class RuntimeMapLoadResult
        {
            public bool Success { get; private set; }
            public List<SpawnPointData> Spawns { get; private set; }
            public string SourceLabel { get; private set; }
            public string Error { get; private set; }

            public static RuntimeMapLoadResult Successful(List<SpawnPointData> spawns, string sourceLabel)
            {
                return new RuntimeMapLoadResult
                {
                    Success = true,
                    Spawns = spawns ?? new List<SpawnPointData>(),
                    SourceLabel = sourceLabel ?? string.Empty
                };
            }

            public static RuntimeMapLoadResult Failure(string error)
            {
                return new RuntimeMapLoadResult { Error = error ?? string.Empty };
            }
        }
    }
}
#endregion
