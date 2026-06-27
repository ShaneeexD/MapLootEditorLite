using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    [BepInPlugin("com.maplooteditorlite.client", "Map Loot Editor Lite", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static KeyCode ToggleKey { get; private set; } = KeyCode.F8;
        public static string SptRoot { get; private set; } = string.Empty;
        public static string ModDataDirectory { get; private set; } = string.Empty;
        public static string ServerModDirectory { get; private set; } = string.Empty;
        public static string ServerModPacksDirectory { get; private set; } = string.Empty;
        public static string ServerModExportsDirectory { get; private set; } = string.Empty;
        public static ConfigEntry<bool> EnableEditor;
        public static ConfigEntry<bool> EnableDebugVisuals;
        public static ConfigEntry<KeyboardShortcut> PlaceLootSpawnHotkey;
        public static ConfigEntry<KeyboardShortcut> PlaceLootZoneHotkey;
        public static ConfigEntry<KeyboardShortcut> PlaceStaticObjectHotkey;
        public static ConfigEntry<KeyboardShortcut> PlaceLootSpawnAtLookHotkey;
        public static ConfigEntry<KeyboardShortcut> PlaceLootZoneAtLookHotkey;
        public static ConfigEntry<KeyboardShortcut> PlaceStaticObjectAtLookHotkey;
        public static MapEditorController Controller { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = BepInEx.Logging.Logger.CreateLogSource("MLEL");
            Log.LogInfo("Map Loot Editor Lite client plugin loaded");

            SptRoot = FindSptRoot(Info.Location);
            var serverRoot = FindServerRoot(SptRoot);
            ModDataDirectory = Path.Combine(SptRoot, "BepInEx", "config", "MapLootEditorLite");
            ServerModDirectory = Path.Combine(serverRoot, "user", "mods", "MapLootEditorLite");
            ServerModPacksDirectory = Path.Combine(ServerModDirectory, "packs");
            ServerModExportsDirectory = Path.Combine(ServerModDirectory, "exports");

            Log.LogInfo($"Detected client root: {SptRoot}");
            Log.LogInfo($"Detected server root: {serverRoot}");
            Log.LogInfo($"Server mod directory: {ServerModDirectory}");
            Log.LogInfo($"Exports directory: {ServerModExportsDirectory}");

            Directory.CreateDirectory(ModDataDirectory);
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "editor"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "spawns"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "imports"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "cache"));
            Directory.CreateDirectory(ServerModPacksDirectory);
            Directory.CreateDirectory(ServerModExportsDirectory);

            JsonStorage.Initialize(ModDataDirectory);

            var toggleConfig = base.Config.Bind("General", "ToggleKey", KeyCode.F8, "Hotkey that opens/closes the editor window");
            ToggleKey = toggleConfig.Value;

            EnableEditor = base.Config.Bind("General", "EnableEditor", true, "Enable the in-raid F8 editor");
            EnableDebugVisuals = base.Config.Bind("General", "EnableDebugVisuals", false, "Show debug visuals in raid");

            PlaceLootSpawnHotkey = base.Config.Bind("Hotkeys", "Place Loot Spawn", new KeyboardShortcut(KeyCode.Insert), "Place a loose loot spawn at your position");
            PlaceLootZoneHotkey = base.Config.Bind("Hotkeys", "Place Loot Zone", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftShift), "Place a loot zone at your position");
            PlaceStaticObjectHotkey = base.Config.Bind("Hotkeys", "Place Static Object", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftControl), "Place a static object at your position");
            PlaceLootSpawnAtLookHotkey = base.Config.Bind("Hotkeys", "Place Loot Spawn at Look", new KeyboardShortcut(KeyCode.Home), "Place a loose loot spawn where you are looking");
            PlaceLootZoneAtLookHotkey = base.Config.Bind("Hotkeys", "Place Loot Zone at Look", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftShift), "Place a loot zone where you are looking");
            PlaceStaticObjectAtLookHotkey = base.Config.Bind("Hotkeys", "Place Static Object at Look", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftControl), "Place a static object where you are looking");

            // F12 menu buttons (requires BepInEx ConfigurationManager)
            base.Config.Bind("F12 Buttons", "Place Loot Spawn at Player", false, new ConfigDescription("Click to place a loose loot spawn where you are standing", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootSpawnAtPlayer, HideSettingName = true }));
            base.Config.Bind("F12 Buttons", "Place Loot Zone at Player", false, new ConfigDescription("Click to place a loot zone where you are standing", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootZoneAtPlayer, HideSettingName = true }));
            base.Config.Bind("F12 Buttons", "Place Static Object at Player", false, new ConfigDescription("Click to place a static object where you are standing", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceStaticObjectAtPlayer, HideSettingName = true }));
            base.Config.Bind("F12 Buttons", "Place Loot Spawn at Look", false, new ConfigDescription("Click to place a loose loot spawn where you are looking", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootSpawnAtLook, HideSettingName = true }));
            base.Config.Bind("F12 Buttons", "Place Loot Zone at Look", false, new ConfigDescription("Click to place a loot zone where you are looking", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootZoneAtLook, HideSettingName = true }));
            base.Config.Bind("F12 Buttons", "Place Static Object at Look", false, new ConfigDescription("Click to place a static object where you are looking", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceStaticObjectAtLook, HideSettingName = true }));
            base.Config.Bind("F12 Buttons", "Save Markers", false, new ConfigDescription("Click to save the current markers to disk", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonSaveMarkers, HideSettingName = true }));

            if (EnableEditor.Value)
            {
                Controller = gameObject.AddComponent<MapEditorController>();
                Log.LogInfo("Editor enabled");
            }
            else
            {
                Log.LogInfo("Editor is disabled in BepInEx config; set EnableEditor to true to use the F8 editor");
            }
        }

        private static void DrawButtonPlaceLootSpawnAtPlayer(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Place Loot Spawn at Player", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.CreateLootSpawn();
        }

        private static void DrawButtonPlaceLootZoneAtPlayer(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Place Loot Zone at Player", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.CreateLootZone();
        }

        private static void DrawButtonPlaceStaticObjectAtPlayer(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Place Static Object at Player", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.CreateStaticObject();
        }

        private static void DrawButtonPlaceLootSpawnAtLook(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Place Loot Spawn at Look", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.CreateLootSpawnAtLook();
        }

        private static void DrawButtonPlaceLootZoneAtLook(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Place Loot Zone at Look", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.CreateLootZoneAtLook();
        }

        private static void DrawButtonPlaceStaticObjectAtLook(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Place Static Object at Look", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.CreateStaticObjectAtLook();
        }

        private static void DrawButtonSaveMarkers(ConfigEntryBase entry)
        {
            if (GUILayout.Button("Save Markers", GUILayout.ExpandWidth(true)) && Controller != null)
                Controller.Save();
        }

        private static string FindSptRoot(string pluginPath)
        {
            var dir = Path.GetDirectoryName(pluginPath);
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "BepInEx")) && Directory.Exists(Path.Combine(dir, "user")))
                    return dir;

                var parent = Path.GetDirectoryName(dir);
                if (parent == dir)
                    break;
                dir = parent;
            }

            return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(pluginPath)));
        }

        private static string FindServerRoot(string clientRoot)
        {
            // SPT is commonly installed with the server in a subfolder named "SPT" (e.g. C:\SPT\SPT).
            // If that subfolder exists and contains the user/mods directory, use it. Otherwise fall back to the client root.
            var serverCandidate = Path.Combine(clientRoot, "SPT");
            if (Directory.Exists(Path.Combine(serverCandidate, "user")))
                return serverCandidate;

            return clientRoot;
        }
    }
}
