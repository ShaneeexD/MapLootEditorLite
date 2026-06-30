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
    [BepInDependency("com.wtt.commonlib", BepInDependency.DependencyFlags.SoftDependency)]
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
        public static ConfigEntry<float> UIScale;
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
            Directory.CreateDirectory(Path.Combine(ModDataDirectory, "prefabs"));
            Directory.CreateDirectory(ServerModPacksDirectory);
            Directory.CreateDirectory(ServerModExportsDirectory);

            JsonStorage.Initialize(ModDataDirectory);
            PrefabStorage.Initialize(ModDataDirectory);

            var toggleConfig = base.Config.Bind("General", "ToggleKey", KeyCode.F8, "Hotkey that opens/closes the editor window");
            ToggleKey = toggleConfig.Value;

            EnableEditor = base.Config.Bind("General", "EnableEditor", true, "Enable the in-raid F8 editor");
            EnableDebugVisuals = base.Config.Bind("General", "EnableDebugVisuals", false, "Show debug visuals in raid");
            UIScale = base.Config.Bind("General", "UIScale", 1.0f, new ConfigDescription("Scale of the editor UI window", new AcceptableValueRange<float>(0.5f, 2.0f)));

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
