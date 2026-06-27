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
            Log = Logger;
            Log.LogInfo("Map Loot Editor Lite client plugin loaded");

            JsonStorage.Initialize(Info.Location);

            var toggleConfig = Config.Bind("General", "ToggleKey", KeyCode.F8, "Hotkey that opens/closes the editor window");
            ToggleKey = toggleConfig.Value;

            PlaceLootSpawnHotkey = Config.Bind("Hotkeys", "Place Loot Spawn", new KeyboardShortcut(KeyCode.Insert), "Place a loose loot spawn at your position");
            PlaceLootZoneHotkey = Config.Bind("Hotkeys", "Place Loot Zone", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftShift), "Place a loot zone at your position");
            PlaceStaticObjectHotkey = Config.Bind("Hotkeys", "Place Static Object", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftControl), "Place a static object at your position");
            PlaceLootSpawnAtLookHotkey = Config.Bind("Hotkeys", "Place Loot Spawn at Look", new KeyboardShortcut(KeyCode.Home), "Place a loose loot spawn where you are looking");
            PlaceLootZoneAtLookHotkey = Config.Bind("Hotkeys", "Place Loot Zone at Look", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftShift), "Place a loot zone where you are looking");
            PlaceStaticObjectAtLookHotkey = Config.Bind("Hotkeys", "Place Static Object at Look", new KeyboardShortcut(KeyCode.Home, KeyCode.LeftControl), "Place a static object where you are looking");

            // F12 menu buttons (requires BepInEx ConfigurationManager)
            Config.Bind("F12 Buttons", "Place Loot Spawn at Player", false, new ConfigDescription("Click to place a loose loot spawn where you are standing", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootSpawnAtPlayer, HideSettingName = true }));
            Config.Bind("F12 Buttons", "Place Loot Zone at Player", false, new ConfigDescription("Click to place a loot zone where you are standing", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootZoneAtPlayer, HideSettingName = true }));
            Config.Bind("F12 Buttons", "Place Static Object at Player", false, new ConfigDescription("Click to place a static object where you are standing", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceStaticObjectAtPlayer, HideSettingName = true }));
            Config.Bind("F12 Buttons", "Place Loot Spawn at Look", false, new ConfigDescription("Click to place a loose loot spawn where you are looking", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootSpawnAtLook, HideSettingName = true }));
            Config.Bind("F12 Buttons", "Place Loot Zone at Look", false, new ConfigDescription("Click to place a loot zone where you are looking", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceLootZoneAtLook, HideSettingName = true }));
            Config.Bind("F12 Buttons", "Place Static Object at Look", false, new ConfigDescription("Click to place a static object where you are looking", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonPlaceStaticObjectAtLook, HideSettingName = true }));
            Config.Bind("F12 Buttons", "Save Markers", false, new ConfigDescription("Click to save the current markers to disk", null, new ConfigurationManagerAttributes { CustomDrawer = DrawButtonSaveMarkers, HideSettingName = true }));

            Controller = gameObject.AddComponent<MapEditorController>();
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
    }
}
