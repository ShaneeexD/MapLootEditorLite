#region Plugin.cs
using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UltimateLootEditor.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ULE.SpawnEditor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = ModConstants.ClientPluginGuid;
        public const string PluginName = ModConstants.ModDisplayName;
        public const string PluginVersion = ModConstants.Version;

        internal const int VisualizerBuildBudget = 24;
        internal const float VisualizerRenderDistance = 30f;
        internal const bool IncludeUnsafeSearchItems = false;
        internal const int SearchMaxRankedMatches = 200;

        internal static ConfigEntry<KeyCode> OpenEditorKey;
        internal static ConfigEntry<KeyboardShortcut> ToggleVizKey;
        internal static ConfigEntry<KeyboardShortcut> CreateSpawnPointKey;
        internal static ConfigEntry<float> SphereScale;
        internal static ConfigEntry<float> ItemPreviewSphereOpacity;
        internal static ConfigEntry<float> SelectRadius;
        internal static ConfigEntry<float> SelectionGroupRadius;
        internal static ConfigEntry<bool> DrawWorldLabels;
        internal static ConfigEntry<bool> EnableEditorNotifications;
        internal static ConfigEntry<bool> EnableDragValueEditing;

        internal static bool DebugLoggingEnabled => false;

        private GameObject _manager;
        private TarkovLootEditorUI _editorUi;
        private string _activeMapId;
        private float _nextAutoDetectPollAt;
        private float _nextSnapshotPrewarmPollAt;
        private float _nextEditorTemplatePrewarmAt;
        private float _nextBootstrapProbeLogAt;
        private int _activeRaidWorldMissCount;
        private static bool _isInitializing;
        private int _lastToggleHandledFrame = -1;
        private bool _editorTemplatePrewarmComplete;
        private static GameWorld _cachedRaidWorld;
        private static UnityEngine.Object _cachedApplication;
        private static Type _applicationType;
        private static float _nextWorldSearchAt;
        private static float _nextApplicationSearchAt;
        private Harmony _harmony;
        private Coroutine _gameWorldBootstrapRetryCoroutine;

        internal static Plugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            MapCatalog.ConfigureOptionalMaps(System.IO.Path.Combine(BepInEx.Paths.GameRootPath, "SPT_Runtime", "user", "mods"));

            OpenEditorKey = Config.Bind("Input", "OpenEditorKey", KeyCode.Mouse4, MakeConfigDescription("Open the loot editor when near a sphere.", 100));
            CreateSpawnPointKey = Config.Bind("Input", "CreateSpawnPointKey", new KeyboardShortcut(KeyCode.Mouse3), MakeConfigDescription("Create a new loose loot spawn point and enter placement mode.", 95));
            if (CreateSpawnPointKey.Value.MainKey == KeyCode.Insert)
            {
                CreateSpawnPointKey.Value = new KeyboardShortcut(KeyCode.Mouse3);
            }

            ToggleVizKey = Config.Bind("Input", "ToggleVisualizer", new KeyboardShortcut(KeyCode.F9), MakeConfigDescription("Toggle visibility of spawn spheres.", 90));
            SphereScale = Config.Bind("Visualizer", "SphereScale", 0.15f, MakeConfigDescription("Sphere diameter (m).", 80));
            ItemPreviewSphereOpacity = Config.Bind("Visualizer", "ItemPreviewSphereOpacity", 0.08f, MakeConfigDescription("Sphere opacity while previewing an item at a spawn point.", 75));
            SelectRadius = Config.Bind("Visualizer", "SelectRadius", 2.0f, MakeConfigDescription("Max distance to open editor (m).", 70));
            SelectionGroupRadius = Config.Bind("Visualizer", "SelectionGroupRadius", 5.0f, MakeConfigDescription("When a spawn is opened, nearby spawns within this radius can be cycled with the editor arrows.", 60));
            DrawWorldLabels = Config.Bind("Visualizer", "DrawWorldLabels", true, MakeConfigDescription("Draw labels above the nearest visible spheres.", 50));
            EnableEditorNotifications = Config.Bind("Editor", "EnableEditorNotifications", true, MakeConfigDescription("Show native notifications for editor actions such as save, copy, paste, undo, and redo.", 35));
            EnableDragValueEditing = Config.Bind("Editor", "EnableDragValueEditing", true, MakeConfigDescription("Enable click-and-drag left/right editing on position and rotation value fields.", 30));

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");

            RemoveLegacyConfigEntries();
            DbPaths.CleanupPluginRuntimeArtifacts();
            DbPaths.EnsureAllMapEditFolders();
            TplCache.BuildIfMissing(Logger);

            _harmony = new Harmony(PluginGuid);
            SafePatchAll();
            _editorUi = gameObject.AddComponent<TarkovLootEditorUI>();
            _editorUi.Init(null, Logger);

        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private void Update()
        {
            using (RuntimeWorkTiming.Measure("Plugin.Update", Logger)) UpdateCore();
        }

        private void UpdateCore()
        {
            RuntimeItemSourceCatalog.Prewarm();
            // Drain notifications on Unity's thread, including after a raid manager is destroyed.
            while (SaveManager.Queue.TryTakeCompletion(out var save))
            {
                if (save.Error == null)
                {
                    Logger.LogInfo($"[ULE] Saved map edits for '{save.MapId}'.");
                    NativeNotifications.Show("Changes saved");
                }
                else
                {
                    Logger.LogWarning($"[ULE] Failed to save '{save.MapId}': {save.Error}");
                    NativeNotifications.Show("Save failed. Changes retained; press SAVE to retry.");
                }
            }

            try
            {
                PresetPreviewBridge.Tick();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ULE] Preview bridge tick failed: {ex.Message}");
            }

            if (_isInitializing)
            {
                return;
            }

            if (IsToggleVisualizerPressed())
            {
                HandleToggleVisualizerPressed();
                return;
            }

            PrewarmSelectedRaidMap();
            PrewarmNativeEditorTemplates();

            if (_manager != null)
            {
                if (Time.unscaledTime >= _nextAutoDetectPollAt)
                {
                    _nextAutoDetectPollAt = Time.unscaledTime + 1f;
                    if (!TryGetLiveRaidWorldMapId(out var liveMapId))
                    {
                        _activeRaidWorldMissCount++;
                        if (_activeRaidWorldMissCount >= 3)
                        {
                            LogDebug("[ULE] Raid world no longer detected. Releasing manager and in-memory loot data.");
                            TearDownManager();
                        }
                    }
                    else
                    {
                        _activeRaidWorldMissCount = 0;
                        if (!string.Equals(_activeMapId, liveMapId, StringComparison.OrdinalIgnoreCase))
                        {
                            LogDebug($"[ULE] Raid map changed from '{_activeMapId}' to '{liveMapId}'. Releasing current manager.");
                            TearDownManager();
                        }
                    }
                }

                return;
            }

        }

        private void PrewarmSelectedRaidMap()
        {
            if (_manager != null || Time.unscaledTime < _nextSnapshotPrewarmPollAt)
            {
                return;
            }

            _nextSnapshotPrewarmPollAt = Time.unscaledTime + 0.5f;
            if (!TryReadRaidSettingsMapId(out var mapId))
            {
                return;
            }

            RuntimeLooseLootProvider.WarmMap(mapId);
        }

        private void PrewarmNativeEditorTemplates()
        {
            if (_editorTemplatePrewarmComplete || Time.unscaledTime < _nextEditorTemplatePrewarmAt)
            {
                return;
            }

            // Each pass only resolves one existing EFT template reference. No ULE UI is created here.
            _nextEditorTemplatePrewarmAt = Time.unscaledTime + 0.75f;
            _editorTemplatePrewarmComplete = TarkovLootEditorUI.TryPrewarmNativeTemplates();
        }

        private void OnGUI()
        {
            var current = Event.current;
            if (current == null ||
                current.type != EventType.KeyDown ||
                current.keyCode != GetToggleVisualizerMainKey() ||
                !IsToggleVisualizerPressed() ||
                GetToggleVisualizerMainKey() == KeyCode.None)
            {
                return;
            }

            if (HandleToggleVisualizerPressed())
            {
                current.Use();
            }
        }

        private bool HandleToggleVisualizerPressed()
        {
            if (_lastToggleHandledFrame == Time.frameCount)
            {
                return true;
            }

            _lastToggleHandledFrame = Time.frameCount;

            if (_manager != null)
            {
                var visualizer = _manager.GetComponent<SpawnVisualizer>();
                if (visualizer != null)
                {
                    visualizer.SetVisualizationVisible(!visualizer.IsVisualizationVisible);
                    return true;
                }
            }

            if (_isInitializing)
            {
                return true;
            }

            if (TryStartBootstrapForCurrentRaid(
                showVisualizerAfterBootstrap: true,
                source: $"ToggleVisualizer ({ToggleVizKey.Value})"))
            {
                return true;
            }

            Logger.LogWarning($"[ULE] {ToggleVizKey.Value} was pressed, but no live raid map could be detected yet.");
            LogBootstrapProbe(force: true);
            return true;
        }

        private void SafePatchAll()
        {
            var patched = 0;
            var skipped = 0;

            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (!HasHarmonyPatchAttribute(type))
                {
                    continue;
                }

                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                    patched++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Logger.LogWarning($"[ULE] Skipped Harmony patch '{type.FullName}': {UnwrapPatchException(ex).Message}");
                }
            }

            Logger.LogInfo($"[ULE] Harmony patches applied: {patched}, skipped: {skipped}.");
        }

        private static bool HasHarmonyPatchAttribute(Type type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (var attribute in type.GetCustomAttributes(inherit: false))
            {
                if (attribute is HarmonyPatch)
                {
                    return true;
                }
            }

            return false;
        }

        private static Exception UnwrapPatchException(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            return ex is HarmonyException harmonyException && harmonyException.InnerException != null
                ? harmonyException.InnerException
                : ex;
        }

        private static bool IsToggleVisualizerPressed()
        {
            if (ToggleVizKey == null)
            {
                return Input.GetKeyDown(KeyCode.F9);
            }

            var shortcut = ToggleVizKey.Value;
            return shortcut.MainKey != KeyCode.None &&
                shortcut.IsDown();
        }

        private static KeyCode GetToggleVisualizerMainKey()
        {
            return ToggleVizKey?.Value.MainKey ?? KeyCode.None;
        }

        internal void RequestBootstrapFromGameWorld(GameWorld world, string source)
        {
            if (world != null) _cachedRaidWorld = world;
            if (world == null || _manager != null || _isInitializing)
            {
                return;
            }

            if (TryStartBootstrapFromWorld(world, showVisualizerAfterBootstrap: false, source))
            {
                return;
            }

            if (_gameWorldBootstrapRetryCoroutine == null)
            {
                _gameWorldBootstrapRetryCoroutine = StartCoroutine(RetryBootstrapFromGameWorld(world, source));
            }
        }

        private IEnumerator RetryBootstrapFromGameWorld(GameWorld world, string source)
        {
            const int maxAttempts = 40;
            const float retryDelaySeconds = 0.25f;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (_manager != null || _isInitializing)
                {
                    _gameWorldBootstrapRetryCoroutine = null;
                    yield break;
                }

                if (world == null)
                {
                    _gameWorldBootstrapRetryCoroutine = null;
                    yield break;
                }

                if (TryStartBootstrapFromWorld(world, showVisualizerAfterBootstrap: false, source))
                {
                    _gameWorldBootstrapRetryCoroutine = null;
                    yield break;
                }

                yield return new WaitForSecondsRealtime(retryDelaySeconds);
            }

            _gameWorldBootstrapRetryCoroutine = null;
        }

        private bool TryStartBootstrapForCurrentRaid(bool showVisualizerAfterBootstrap, string source)
        {
            if (_manager != null || _isInitializing)
            {
                return false;
            }

            if (!TryGetLiveRaidWorldMapId(out var mapId))
            {
                return false;
            }

            _isInitializing = true;
            StartCoroutine(BootstrapMap(
                mapId,
                $"[ULE] {source} requested bootstrap for map '{mapId}'.",
                showVisualizerAfterBootstrap));
            return true;
        }

        private bool TryStartBootstrapFromWorld(GameWorld world, bool showVisualizerAfterBootstrap, string source)
        {
            if (_manager != null || _isInitializing || world == null)
            {
                return false;
            }

            string mapId;
            if (!TryReadMapIdFromObject(world, out mapId) && !TryReadLoadedSceneMapId(out mapId))
            {
                return false;
            }

            _isInitializing = true;
            StartCoroutine(BootstrapMap(
                mapId,
                $"[ULE] {source} detected map '{mapId}'.",
                showVisualizerAfterBootstrap));
            return true;
        }

        private bool TryGetLiveRaidWorldMapId(out string mapId)
        {
            using (RuntimeWorkTiming.Measure("Raid detection", Logger))
                return TryGetLiveRaidWorldMapIdCore(out mapId);
        }

        private bool TryGetLiveRaidWorldMapIdCore(out string mapId)
        {
            mapId = null;

            try
            {
                var world = GetRaidWorld();
                if (world != null)
                {
                    if (TryNormalizeMapId(world.LocationId, out mapId)) return true;
                    if (TryReadMapIdFromObject(world, out mapId))
                    {
                        return true;
                    }

                    if (TryReadRaidSettingsMapId(out mapId))
                    {
                        return true;
                    }
                }

                if (TryReadLoadedSceneMapId(out mapId))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore timing issues during scene transitions and retry on the next tick.
            }

            return false;
        }

        private static bool TryReadRaidSettingsMapId(out string mapId)
        {
            mapId = null;

            var application = _cachedApplication;
            if (application == null && Time.unscaledTime >= _nextApplicationSearchAt)
            {
                _nextApplicationSearchAt = Time.unscaledTime + 5f;
                _applicationType = _applicationType ?? FindTypeByFullName("EFT.TarkovApplication");
                if (_applicationType != null)
                    application = _cachedApplication = UnityEngine.Object.FindObjectOfType(_applicationType);
            }
            if (application == null)
            {
                return false;
            }

            string[] settingsMemberNames =
            {
                "CurrentRaidSettings",
                "RaidSettings",
                "LocalRaidSettings",
                "_raidSettings",
                "_localRaidSettings"
            };

            foreach (var memberName in settingsMemberNames)
            {
                var settings = ReadMemberValue(application, memberName);
                if (TryReadMapIdFromObject(settings, out mapId))
                {
                    return true;
                }
            }

            return false;
        }

        internal static GameWorld GetRaidWorld()
        {
            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null)
                return _cachedRaidWorld = Singleton<GameWorld>.Instance;
            // Unity's null check also invalidates a cached object when its raid is destroyed.
            if (_cachedRaidWorld != null) return _cachedRaidWorld;
            if (Time.unscaledTime < _nextWorldSearchAt) return null;
            _nextWorldSearchAt = Time.unscaledTime + 5f;
            return _cachedRaidWorld = FindObjectOfType<GameWorld>();
        }

        private static bool TryReadLoadedSceneMapId(out string mapId)
        {
            mapId = null;

            try
            {
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid() && TryNormalizeMapId(activeScene.name, out mapId))
                {
                    return true;
                }

                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.IsValid() && TryNormalizeMapId(scene.name, out mapId))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Scene lookups can briefly fail while EFT swaps scenes.
            }

            return false;
        }

        private static bool TryReadMapIdFromObject(object target, out string mapId)
        {
            mapId = null;
            if (target == null)
            {
                return false;
            }

            if (TryNormalizeMapId(target, out mapId))
            {
                return true;
            }

            string[] directMemberNames =
            {
                "LocationId",
                "locationId",
                "_locationId",
                "MapId",
                "mapId",
                "_mapId",
                "Id",
                "id",
                "_id"
            };

            foreach (var memberName in directMemberNames)
            {
                if (TryNormalizeMapId(ReadMemberValue(target, memberName), out mapId))
                {
                    return true;
                }
            }

            string[] nestedMemberNames =
            {
                "SelectedLocation",
                "selectedLocation",
                "Location",
                "location",
                "_location",
                "LocationSettings",
                "locationSettings",
                "Map",
                "map"
            };

            foreach (var memberName in nestedMemberNames)
            {
                var nested = ReadMemberValue(target, memberName);
                if (TryNormalizeMapId(nested, out mapId) || TryReadLocationObjectMapId(nested, out mapId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadLocationObjectMapId(object target, out string mapId)
        {
            mapId = null;
            if (target == null)
            {
                return false;
            }

            string[] memberNames =
            {
                "Id",
                "id",
                "_id",
                "Name",
                "name",
                "_name",
                "LocationId",
                "locationId",
                "_locationId"
            };

            foreach (var memberName in memberNames)
            {
                if (TryNormalizeMapId(ReadMemberValue(target, memberName), out mapId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeMapId(object value, out string mapId)
        {
            mapId = null;
            if (value == null)
            {
                return false;
            }

            var normalized = DbPaths.NormalizeMapId(value as string ?? value.ToString());
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            mapId = normalized;
            return true;
        }

        private static object ReadMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            while (type != null)
            {
                try
                {
                    var property = type.GetProperty(memberName, flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        return property.GetValue(target, null);
                    }
                }
                catch
                {
                    // Some EFT properties can throw while raid state is still being built.
                }

                try
                {
                    var field = type.GetField(memberName, flags);
                    if (field != null)
                    {
                        return field.GetValue(target);
                    }
                }
                catch
                {
                    // Same as above: ignore transient raid-state access failures.
                }

                type = type.BaseType;
            }

            return null;
        }

        private static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, throwOnError: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // Ignore dynamic assemblies that do not support metadata lookup cleanly.
                }
            }

            return null;
        }

        private void LogBootstrapProbe(bool force)
        {
            if (!DebugLoggingEnabled)
            {
                return;
            }

            if (!force && Time.unscaledTime < _nextBootstrapProbeLogAt)
            {
                return;
            }

            _nextBootstrapProbeLogAt = Time.unscaledTime + 10f;

            string sceneName;
            try
            {
                var activeScene = SceneManager.GetActiveScene();
                sceneName = activeScene.IsValid() ? activeScene.name : "<invalid>";
            }
            catch
            {
                sceneName = "<unknown>";
            }

            bool singletonWorldInstantiated;
            GameWorld world;
            try
            {
                singletonWorldInstantiated = Singleton<GameWorld>.Instantiated;
                world = singletonWorldInstantiated
                    ? Singleton<GameWorld>.Instance
                    : FindObjectOfType<GameWorld>();
            }
            catch (Exception ex)
            {
                LogDebug($"[ULE] Bootstrap probe: scene='{sceneName}', world lookup failed: {ex.Message}");
                return;
            }

            var rawLocationId = world?.LocationId;
            var normalizedMapId = DbPaths.NormalizeMapId(rawLocationId);
            var nextPollDelay = Mathf.Max(0f, _nextAutoDetectPollAt - Time.unscaledTime);
            LogDebug(
                $"[ULE] Bootstrap probe: scene='{sceneName}', singletonWorld={singletonWorldInstantiated}, foundWorld={(world != null)}, " +
                $"locationId='{rawLocationId ?? "<null>"}', normalized='{normalizedMapId ?? "<null>"}', nextPollIn={nextPollDelay:0.00}s.");
        }

        private IEnumerator BootstrapMap(string mapId, string logLine, bool showVisualizerAfterBootstrap = false)
        {
            _isInitializing = true;

            if (string.IsNullOrEmpty(mapId) || mapId == "off" || !DbPaths.IsKnownMapId(mapId))
            {
                _isInitializing = false;
                yield break;
            }

            try
            {
                LogDebug(logLine);

                if (_manager != null)
                {
                    Destroy(_manager);
                }

                _manager = new GameObject("ULE_Manager");
                DontDestroyOnLoad(_manager);

                var visualizer = _manager.AddComponent<SpawnVisualizer>();
                visualizer.Init(mapId, Logger, this);
                if (showVisualizerAfterBootstrap)
                {
                    visualizer.SetVisualizationVisible(true);
                }

                _editorUi.Init(visualizer, Logger);

                _activeMapId = mapId;
                _activeRaidWorldMissCount = 0;
                Logger.LogInfo($"[ULE] Editor ready for map '{mapId}'.");

            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[ULE] Failed to bootstrap manager for map '{mapId}': {ex}");
                TearDownManager();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void TearDownManager()
        {
            if (_gameWorldBootstrapRetryCoroutine != null)
            {
                StopCoroutine(_gameWorldBootstrapRetryCoroutine);
                _gameWorldBootstrapRetryCoroutine = null;
            }

            _editorUi?.DetachRaid();
            PresetPreviewBridge.ResetRaidVisualCaches();

            if (_manager != null)
            {
                Destroy(_manager);
            }

            _manager = null;
            _activeMapId = null;
            _activeRaidWorldMissCount = 0;
            EditorWindowState.Open = false;
        }

        private void RemoveLegacyConfigEntries()
        {
            var removed = false;
            removed |= Config.Remove(new ConfigDefinition("General", "AutoDetectMap"));
            removed |= Config.Remove(new ConfigDefinition("General", "SelectedMap"));
            removed |= Config.Remove(new ConfigDefinition("General", "Reload Selected Map"));
            removed |= Config.Remove(new ConfigDefinition("Performance", "RenderDistance"));
            removed |= Config.Remove(new ConfigDefinition("Visualizer", "RenderDistance"));
            removed |= Config.Remove(new ConfigDefinition("Performance", "BuildBudget"));
            removed |= Config.Remove(new ConfigDefinition("Advanced", "ShowUnsafeItemsInSearch"));
            removed |= Config.Remove(new ConfigDefinition("Advanced", "SearchMaxRankedMatches"));
            removed |= Config.Remove(new ConfigDefinition("Advanced", "BlurPresetPreviewBackground"));
            removed |= Config.Remove(new ConfigDefinition("Input", "PreviewTarkovUiKey"));
            removed |= Config.Remove(new ConfigDefinition("Editor", "UseTarkovStyleEditor"));
            removed |= Config.Remove(new ConfigDefinition("Editor", "UseTarkovUIForEditor"));
            removed |= Config.Remove(new ConfigDefinition("Diagnostics", "DebugLogging"));

            if (removed)
            {
                Config.Save();
            }
        }

        private void LogDebug(string message)
        {
            if (DebugLoggingEnabled)
            {
                Logger.LogInfo(message);
            }
        }

        private static ConfigDescription MakeConfigDescription(string description, int order, bool advanced = false)
        {
            return new ConfigDescription(
                description,
                null,
                new ConfigurationManagerAttributes
                {
                    Order = order,
                    IsAdvanced = advanced
                });
        }
    }

    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order { get; set; }
        public bool? IsAdvanced { get; set; }
    }

    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    internal static class GameWorldOnGameStartedPatch
    {
        private static void Postfix(GameWorld __instance)
        {
            Plugin.Instance?.RequestBootstrapFromGameWorld(__instance, "GameWorld.OnGameStarted");
        }
    }
}
#endregion
