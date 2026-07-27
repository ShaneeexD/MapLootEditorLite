using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    // Injects custom asset bundles from the packs/bundles folders into EFT's IEasyAssets.
    // Also provides a runtime loader so static/interactive objects can use bundle models.
    internal static class BundleInjector
    {
        private static ManualLogSource _log;
        // canonical key (filename no ext) -> full file path
        private static readonly Dictionary<string, string> _bundlePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // canonical key -> loaded bundle
        private static readonly Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        // canonical key + prefabName -> cached GameObject
        private static readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        internal static void Init(ManualLogSource log)
        {
            _log = log;
            DiscoverBundles();
        }

        internal static IEnumerator InjectWhenReady()
        {
            yield return new WaitUntil(() => Singleton<IEasyAssets>.Instance != null);
            yield return InjectAll();
            _log?.LogInfo("MapLootEditorLite packs bundles injected.");
        }

        internal static IEnumerator InjectAll()
        {
            var easyAssets = Singleton<IEasyAssets>.Instance;
            if (easyAssets == null)
            {
                _log?.LogError("IEasyAssets singleton not ready");
                yield break;
            }

            var system = GetSystem(easyAssets);
            if (system == null)
            {
                _log?.LogError("IEasyAssets.System is null");
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _bundlePaths)
            {
                string filePath = kvp.Value;
                if (seen.Contains(filePath))
                    continue;
                seen.Add(filePath);

                string assetPath = Path.GetFileNameWithoutExtension(filePath);
                if (IsInjected(system, assetPath))
                    continue;

                AssetBundleCreateRequest request = null;
                try
                {
                    request = AssetBundle.LoadFromFileAsync(filePath);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"Failed to start load for bundle {filePath}: {ex}");
                    continue;
                }
                if (request == null)
                    continue;

                yield return request;

                var bundle = request.assetBundle;
                if (bundle == null)
                {
                    _log?.LogError($"Failed to load bundle: {filePath}");
                    continue;
                }

                _loadedBundles[assetPath] = bundle;
                var assetRequest = bundle.LoadAllAssetsAsync();
                yield return assetRequest;

                InjectIntoSystem(system, assetPath, assetRequest.allAssets, bundle);
                _log?.LogInfo($"Injected bundle {Path.GetFileName(filePath)} as {assetPath}");
            }
        }

        internal static void InjectSingle(DependencyGraphClass<IEasyBundle> system, string key)
        {
            if (system == null)
                return;

            if (IsInjected(system, key))
                return;

            if (!TryResolvePath(key, out string canonical, out string filePath))
                return;

            try
            {
                AssetBundle bundle;
                if (!_loadedBundles.TryGetValue(canonical, out bundle))
                {
                    bundle = AssetBundle.LoadFromFile(filePath);
                    if (bundle == null)
                    {
                        _log?.LogError($"Failed to load bundle: {filePath}");
                        return;
                    }
                    _loadedBundles[canonical] = bundle;
                }

                var allAssets = bundle.LoadAllAssets();
                InjectIntoSystem(system, key, allAssets, bundle);
                _log?.LogInfo($"On-demand injected bundle {Path.GetFileName(filePath)} as {key}");
            }
            catch (Exception ex)
            {
                _log?.LogError($"On-demand bundle injection failed for {key}: {ex}");
            }
        }

        internal static IEnumerator LoadPrefabCoroutine(string bundleName, string prefabName, Action<GameObject> callback)
        {
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                callback?.Invoke(null);
                yield break;
            }

            if (!TryResolvePath(bundleName, out string canonical, out string path))
            {
                _log?.LogWarning($"Bundle not found: {bundleName}");
                callback?.Invoke(null);
                yield break;
            }

            string cacheKey = (canonical + "|" + (prefabName ?? "")).ToLowerInvariant();
            if (_prefabCache.TryGetValue(cacheKey, out var cached))
            {
                callback?.Invoke(cached);
                yield break;
            }

            AssetBundle bundle;
            if (!_loadedBundles.TryGetValue(canonical, out bundle))
            {
                var request = AssetBundle.LoadFromFileAsync(path);
                if (request == null)
                {
                    _log?.LogError($"Failed to start load for bundle {path}");
                    callback?.Invoke(null);
                    yield break;
                }
                yield return request;

                bundle = request.assetBundle;
                if (bundle == null)
                {
                    _log?.LogError($"Failed to load bundle: {path}");
                    callback?.Invoke(null);
                    yield break;
                }
                _loadedBundles[canonical] = bundle;
            }

            GameObject prefab = null;
            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                var req = bundle.LoadAssetAsync<GameObject>(prefabName);
                yield return req;
                prefab = req.asset as GameObject;
            }
            else
            {
                var req = bundle.LoadAllAssetsAsync<GameObject>();
                yield return req;
                prefab = req.allAssets?.OfType<GameObject>().FirstOrDefault();
            }

            if (prefab == null)
            {
                _log?.LogWarning($"No GameObject found in bundle {bundleName} (prefab={prefabName})");
                callback?.Invoke(null);
                yield break;
            }

            _prefabCache[cacheKey] = prefab;
            callback?.Invoke(prefab);
        }

        private static bool TryResolvePath(string bundleName, out string canonical, out string path)
        {
            canonical = Path.GetFileNameWithoutExtension(bundleName);
            return _bundlePaths.TryGetValue(canonical, out path);
        }

        private static void DiscoverBundles()
        {
            _bundlePaths.Clear();
            _loadedBundles.Clear();
            _prefabCache.Clear();

            var packsDir = Plugin.ServerModPacksDirectory;
            if (string.IsNullOrEmpty(packsDir) || !Directory.Exists(packsDir))
            {
                _log?.LogInfo("No packs directory; skipping bundle discovery.");
                return;
            }

            var dirs = new List<string>();
            var globalBundles = Path.Combine(packsDir, "bundles");
            if (Directory.Exists(globalBundles))
                dirs.Add(globalBundles);

            foreach (var packDir in Directory.GetDirectories(packsDir))
            {
                var sub = Path.Combine(packDir, "bundles");
                if (Directory.Exists(sub))
                    dirs.Add(sub);
            }

            foreach (var dir in dirs)
            {
                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".json" || ext == ".txt" || ext == ".md")
                        continue;

                    string noExt = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(noExt))
                        continue;

                    _bundlePaths[noExt] = file;
                }
            }

            _log?.LogInfo($"Discovered {_bundlePaths.Count} bundle(s) in packs.");
        }

        private static DependencyGraphClass<IEasyBundle> GetSystem(IEasyAssets easyAssets)
        {
            var prop = easyAssets.GetType().GetProperty("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop?.GetValue(easyAssets) as DependencyGraphClass<IEasyBundle>;
        }

        private static object GetNodes(DependencyGraphClass<IEasyBundle> system)
        {
            var prop = system.GetType().GetProperty("Nodes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null) return prop.GetValue(system);
            var field = system.GetType().GetField("Nodes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(system);
        }

        private static bool IsInjected(DependencyGraphClass<IEasyBundle> system, string key)
        {
            var nodes = GetNodes(system);
            if (nodes == null) return false;
            var containsKey = nodes.GetType().GetMethod("ContainsKey");
            return (bool)(containsKey?.Invoke(nodes, new object[] { key }) ?? false);
        }

        private static void InjectIntoSystem(DependencyGraphClass<IEasyBundle> system, string assetPath, UnityEngine.Object[] allAssets, AssetBundle bundle)
        {
            var nodes = GetNodes(system);
            if (nodes == null)
            {
                _log?.LogError("Could not access DependencyGraphClass.Nodes");
                return;
            }

            object existingNode = null;
            var enumerator = ((IEnumerable)nodes).GetEnumerator();
            try
            {
                if (enumerator.MoveNext())
                {
                    var entry = enumerator.Current;
                    var nodeValueProp = entry.GetType().GetProperty("Value");
                    existingNode = nodeValueProp?.GetValue(entry);
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }

            if (existingNode == null)
            {
                _log?.LogError("No existing nodes to use as template");
                return;
            }

            var nodeType = existingNode.GetType();
            var dataField = nodeType.GetField("Data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (dataField == null)
            {
                _log?.LogError("Bundle node Data field not found");
                return;
            }

            var nodeCtor = nodeType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { dataField.FieldType }, null);
            if (nodeCtor == null)
            {
                _log?.LogError("Bundle node ctor(T) not found");
                return;
            }

            var depsField = nodeType.GetField("Dependencies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var existingData = dataField.GetValue(existingNode);
            var bundleDataType = existingData.GetType();

            var existingLoadState = GetProp(bundleDataType, existingData, "LoadState");
            if (existingLoadState == null)
            {
                _log?.LogWarning($"LoadState property not found on {bundleDataType.Name}");
                return;
            }

            var lsType = existingLoadState.GetType();
            var valueProp = lsType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var loadedVal = valueProp != null ? Enum.Parse(valueProp.PropertyType, "Loaded") : null;

            var newBundleData = FormatterServices.GetUninitializedObject(bundleDataType);
            SetProp(bundleDataType, newBundleData, "Key", assetPath);
            SetProp(bundleDataType, newBundleData, "Assets", allAssets);
            SetProp(bundleDataType, newBundleData, "SameNameAsset", allAssets != null && allAssets.Length > 0 ? allAssets[0] : null);
            SetField(bundleDataType, newBundleData, "Bool_0", true);
            SetProp(bundleDataType, newBundleData, "Progress", 1f);

            var newLs = Activator.CreateInstance(lsType);
            if (valueProp != null && loadedVal != null)
                valueProp.SetValue(newLs, loadedVal);
            SetProp(bundleDataType, newBundleData, "LoadState", newLs);

            var newNode = nodeCtor.Invoke(new object[] { newBundleData });
            depsField?.SetValue(newNode, Array.CreateInstance(nodeType, 0));

            var add = nodes.GetType().GetMethod("Add");
            add?.Invoke(nodes, new object[] { assetPath, newNode });
        }

        private static void SetProp(Type type, object obj, string name, object value)
        {
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            p?.SetValue(obj, value);
        }

        private static void SetField(Type type, object obj, string name, object value)
        {
            var f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            f?.SetValue(obj, value);
        }

        private static object GetProp(Type type, object obj, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p?.GetValue(obj);
        }

        [HarmonyPatch(typeof(DependencyGraphClass<IEasyBundle>), "GetNode")]
        private static class GetNodePatch
        {
            static void Prefix(DependencyGraphClass<IEasyBundle> __instance, string key)
            {
                try
                {
                    InjectSingle(__instance, key);
                }
                catch (Exception ex)
                {
                    _log?.LogError($"On-demand bundle injection failed for {key}: {ex.Message}");
                }
            }
        }
    }
}
