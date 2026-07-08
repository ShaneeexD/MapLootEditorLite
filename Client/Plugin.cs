using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    [BepInPlugin("com.shaneeexd.mapeditorlite", "Map Editor Lite", "1.0.0")]
    [BepInDependency("com.wtt.commonlib", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static KeyCode ToggleKey { get; private set; } = KeyCode.F8;
        public static string GameRoot { get; private set; } = string.Empty;
        public static string ModDataDirectory { get; private set; } = string.Empty;
        public static string ServerModDirectory { get; private set; } = string.Empty;
        public static string ServerModPacksDirectory { get; private set; } = string.Empty;
        public static string ServerModExportsDirectory { get; private set; } = string.Empty;
        public static ConfigEntry<bool> EnableEditor;
        public static ConfigEntry<bool> EnableDebugVisuals;
        public static ConfigEntry<float> UIScale;
        public static ConfigEntry<float> VanillaRenderDistance;
        public static ConfigEntry<int> UnlockCursorMouseButton;
        public static MapEditorController Controller { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = BepInEx.Logging.Logger.CreateLogSource("MEL");
            Log.LogInfo("Map Editor Lite client plugin loaded");

            GameRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(base.Config.ConfigFilePath)));
            var serverRoot = Path.Combine(GameRoot, "SPT");
            if (!Directory.Exists(Path.Combine(serverRoot, "user")))
                serverRoot = GameRoot;
            ServerModDirectory = Path.Combine(serverRoot, "user", "mods", "MapEditorLite");
            ModDataDirectory = ServerModDirectory;
            ServerModPacksDirectory = Path.Combine(ServerModDirectory, "packs");
            ServerModExportsDirectory = Path.Combine(ServerModDirectory, "exports");

            Log.LogInfo($"Detected game root: {GameRoot}");
            Log.LogInfo($"Server mod directory: {ServerModDirectory}");
            Log.LogInfo($"Exports directory: {ServerModExportsDirectory}");

            Directory.CreateDirectory(ModDataDirectory);
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "editor"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "spawns"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "imports"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "cache"));
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "prefabs"));
            Directory.CreateDirectory(ServerModPacksDirectory);
            Directory.CreateDirectory(ServerModExportsDirectory);

            JsonStorage.Initialize(ModDataDirectory);
            PrefabStorage.Initialize(ModDataDirectory);

            var toggleConfig = base.Config.Bind("General", "ToggleKey", KeyCode.F8, "Hotkey that opens/closes the editor window");
            ToggleKey = toggleConfig.Value;

            EnableEditor = base.Config.Bind("General", "EnableEditor", false, "Enable the in-raid F8 editor");
            EnableDebugVisuals = base.Config.Bind("General", "EnableDebugVisuals", false, "Show debug visuals in raid");
            UIScale = base.Config.Bind("General", "UIScale", 1.0f, new ConfigDescription("Scale of the editor UI window", new AcceptableValueRange<float>(0.5f, 2.0f)));
            VanillaRenderDistance = base.Config.Bind("General", "VanillaRenderDistance", 50f, new ConfigDescription("Maximum distance to render vanilla gizmos (0 = unlimited)", new AcceptableValueRange<float>(0f, 500f)));
            UnlockCursorMouseButton = base.Config.Bind("General", "UnlockCursorMouseButton", 2, new ConfigDescription("Mouse button that unlocks the editor cursor (2 = middle, 3 = button 4, 4 = button 5)", new AcceptableValueRange<int>(2, 4)));

            if (EnableEditor.Value)
            {
                Controller = gameObject.AddComponent<MapEditorController>();
                Log.LogInfo("Editor enabled");
            }
            else
            {
                Log.LogInfo("Editor is disabled in BepInEx config; set EnableEditor to true to use the F8 editor");
            }

            gameObject.AddComponent<RuntimeStaticObjectSpawner>();
            Log.LogInfo("Runtime static object spawner attached");
            gameObject.AddComponent<RuntimeInteractiveObjectSpawner>();
            Log.LogInfo("Runtime interactive object spawner attached");
            gameObject.AddComponent<RuntimeExtractZoneSpawner>();
            Log.LogInfo("Runtime extract zone spawner attached");
            gameObject.AddComponent<RuntimeBotSpawnSpawner>();
            Log.LogInfo("Runtime bot spawn spawner attached");
            gameObject.AddComponent<RuntimeLightZoneSpawner>();
            Log.LogInfo("Runtime light zone spawner attached");
            gameObject.AddComponent<RuntimeOcclusionRepairSpawner>();
            Log.LogInfo("Runtime occlusion repair spawner attached");

            var raidResetHarmony = new Harmony("com.shane.mapeditorlite.raidreset");
            raidResetHarmony.PatchAll();
            Log.LogInfo("Raid reset patch applied");

            StartCoroutine(ItemNameResolver.LoadApiNames());
        }

    }
}
