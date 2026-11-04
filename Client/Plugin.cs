using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MapLootEditorLite.Client
{
    [BepInPlugin("com.shaneeexd.mapeditorlite", "Map Editor Lite", "2.1.0")]
    [BepInDependency("com.wtt.commonlib", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("xyz.drakia.bigbrain", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static ConfigEntry<KeyCode> ToggleKey { get; private set; }
        public static string GameRoot { get; private set; } = string.Empty;
        public static string ModDataDirectory { get; private set; } = string.Empty;
        public static string ServerModDirectory { get; private set; } = string.Empty;
        public static string ServerModPacksDirectory { get; private set; } = string.Empty;
        public static string ServerModExportsDirectory { get; private set; } = string.Empty;
        public static string LocalizationDirectory { get; private set; } = string.Empty;
        public static ConfigEntry<bool> EnableEditor;
        public static ConfigEntry<bool> EnableDebugVisuals;
        public static ConfigEntry<bool> EnablePreviewLifecycleDiagnostics;
        public static ConfigEntry<bool> EnableBotActivityZoneDiagnostics;
        public static ConfigEntry<bool> EnableScenePickDiagnostics;
        public static ConfigEntry<bool> EnableLootZoneDiagnostics;
        public static ConfigEntry<bool> SuppressVanillaBossWaves;
        public static ConfigEntry<bool> SprintToZone;
        public static ConfigEntry<bool> StrongLeashCombatContainment;
        public static ConfigEntry<string> BotGenBackendUrl;
        public static ConfigEntry<float> FreeCamSpeed;
        public static ConfigEntry<float> FreeCamFastMultiplier;
        public static ConfigEntry<KeyCode> DoorDiagnosticKey;
        public static ConfigEntry<float> UIScale;
        public static ConfigEntry<float> VanillaRenderDistance;
        public static ConfigEntry<float> ObjectRenderDistance;
        public static ConfigEntry<int> UnlockCursorMouseButton;
        public static ConfigEntry<string> Language;
        public static MapEditorController Controller { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = BepInEx.Logging.Logger.CreateLogSource("MEL");
            Log.LogInfo("Map Editor Lite client plugin loaded");

            GameRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(base.Config.ConfigFilePath)));
            var serverRoot = Path.Combine(GameRoot, "SPT_Runtime");
            if (!Directory.Exists(Path.Combine(serverRoot, "user")))
            {
                serverRoot = Path.Combine(GameRoot, "SPT");
                if (!Directory.Exists(Path.Combine(serverRoot, "user")))
                    serverRoot = GameRoot;
            }
            ServerModDirectory = Path.Combine(serverRoot, "user", "mods", "MapEditorLite");
            ModDataDirectory = ServerModDirectory;
            ServerModPacksDirectory = Path.Combine(ServerModDirectory, "packs");
            ServerModExportsDirectory = Path.Combine(ServerModDirectory, "exports");
            LocalizationDirectory = Path.Combine(ServerModDirectory, "lang");

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

            BundleInjector.Init(Log);
            StartCoroutine(BundleInjector.InjectWhenReady());

            ToggleKey = base.Config.Bind("General", "ToggleKey", KeyCode.F8, "Hotkey that opens/closes the editor window");

            EnableEditor = base.Config.Bind("General", "EnableEditor", false, "Enable the in-raid F8 editor");
            EnableDebugVisuals = base.Config.Bind("General", "EnableDebugVisuals", false, "Show debug visuals in raid");
            EnablePreviewLifecycleDiagnostics = base.Config.Bind("Diagnostics", "EnablePreviewLifecycleDiagnostics", false, "Log preview requests, instance associations, stale completions, and movement checks.");
            EnableBotActivityZoneDiagnostics = base.Config.Bind("Diagnostics", "EnableBotActivityZoneDiagnostics", false, "Track exact MEL-spawned bot profile IDs and log containment in assigned Bot Activity Zones.");
            EnableScenePickDiagnostics = base.Config.Bind("Diagnostics", "EnableScenePickDiagnostics", false, "Log every viewport scene-object click: collider and renderer candidates, distances, rejection reasons and the final pick.");
            EnableLootZoneDiagnostics = base.Config.Bind("Diagnostics", "EnableLootZoneDiagnostics", false, "Log every candidate tested when baking Loose Loot Zone surface points: ray origin, hit object, normal/slope and accept/reject reason (can be verbose).");
            SuppressVanillaBossWaves = base.Config.Bind("Bots", "SuppressVanillaBossWaves", true, "When a MEL custom spawn point will spawn a boss, remove that map's vanilla spawn waves for the same boss type so a second squad (e.g. a second Goons group) does not appear elsewhere. Only boss types MEL itself is spawning are affected.");
            SprintToZone = base.Config.Bind("Bots", "SprintToZone", true, "Bots with an assigned Bot Activity Zone sprint (stamina permitting) while travelling back to their zone from outside it, and walk once inside.");
            StrongLeashCombatContainment = base.Config.Bind("Bots", "StrongLeashCombatContainment", true, "Strong Leash zones only: validate SAIN combat movement destinations (pursuit, cover, regroup) for MEL-owned bots and replace destinations outside the zone with a reachable point inside it. Grenade avoidance and flash recovery are never constrained. Takes effect at game start; disable if it causes problems with SAIN.");
            BotGenBackendUrl = base.Config.Bind("Integrations", "BotGenBackendUrl", "",
                "Optional trusted SPT backend base URL for BotGen. Empty uses SPT's configured backend (recommended). SPT session credentials are sent to this server. POST /botgen/registry is appended automatically; no credentials, query, or fragment allowed.");
            var legacyRegistryUrl = base.Config.Bind("Integrations", "BotGenRegistryUrl", "",
                "Deprecated. Migrated to BotGenBackendUrl on first upgrade; the old hardcoded default is replaced with automatic SPT discovery.");
            if (string.IsNullOrWhiteSpace(BotGenBackendUrl.Value) && !string.IsNullOrWhiteSpace(legacyRegistryUrl.Value))
            {
                BotGenBackendUrl.Value = BotGenRegistryTransport.MigrateLegacyUrl(legacyRegistryUrl.Value);
                Log.LogInfo("[MEL BotGen] Migrated legacy registry setting to " +
                    (string.IsNullOrWhiteSpace(BotGenBackendUrl.Value) ? "automatic SPT discovery." : "manual backend override (validated on refresh)."));
            }
            if (!string.IsNullOrEmpty(legacyRegistryUrl.Value))
                legacyRegistryUrl.Value = "";
            FreeCamSpeed = base.Config.Bind("Camera", "FreeCamSpeed", 5f, new ConfigDescription("Free camera travel speed in metres/second. In the free camera, [ and ] or Alt+mouse wheel change it on the fly.", new AcceptableValueRange<float>(0.5f, 300f)));
            FreeCamFastMultiplier = base.Config.Bind("Camera", "FreeCamFastMultiplier", 3f, new ConfigDescription("Speed multiplier while holding Shift in the free camera.", new AcceptableValueRange<float>(1f, 20f)));
            DoorDiagnosticKey = base.Config.Bind("Diagnostics", "DoorDiagnosticKey", KeyCode.F10, "With EnableScenePickDiagnostics on and the editor open, press this key while aiming at a door to log the door hierarchy under the cursor (one-shot).");
            UIScale = base.Config.Bind("General", "UIScale", 1.0f, new ConfigDescription("Scale of the editor UI window", new AcceptableValueRange<float>(0.5f, 2.0f)));
            VanillaRenderDistance = base.Config.Bind("General", "VanillaRenderDistance", 50f, new ConfigDescription("Maximum distance to render vanilla gizmos (0 = unlimited)", new AcceptableValueRange<float>(0f, 500f)));
            ObjectRenderDistance = base.Config.Bind("General", "ObjectRenderDistance", 50f, new ConfigDescription("Maximum distance to render scene object picker outlines (0 = unlimited)", new AcceptableValueRange<float>(0f, 500f)));
            UnlockCursorMouseButton = base.Config.Bind("General", "UnlockCursorMouseButton", 2, new ConfigDescription("Mouse button that unlocks the editor cursor (2 = middle, 3 = button 4, 4 = button 5)", new AcceptableValueRange<int>(2, 4)));
            Language = base.Config.Bind("General", "Language", "en", "Language code for UI localization (e.g. en, fr, cn). Place .json files in the lang folder.");
            Locale.Initialize(Language.Value, LocalizationDirectory);

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
            gameObject.AddComponent<RuntimeCutVolumeSpawner>();
            Log.LogInfo("Runtime cut volume spawner attached");
            gameObject.AddComponent<RuntimeBlockerSpawner>();
            Log.LogInfo("Runtime blocker spawner attached");
            gameObject.AddComponent<RuntimeNavMeshBuilder>();
            Log.LogInfo("Runtime navmesh builder attached");

            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("xyz.drakia.bigbrain"))
                    ActivityZoneLeashRegistrar.Register();
                else
                    Log.LogInfo("[MEL leash] BigBrain not found; activity-zone leash disabled.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[MEL leash] Registration failed; leash disabled: {ex.Message}");
            }

            try
            {
                // Strong SAIN destination containment is independent of BigBrain layer selection.
                StrongLeashContainment.EnsureInstalled();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[MEL StrongLeash] SAIN containment setup failed: {ex.Message}");
            }

            var raidResetHarmony = new Harmony("com.shane.mapeditorlite.raidreset");
            raidResetHarmony.PatchAll();
            Log.LogInfo("Raid reset patch applied");

            StartCoroutine(ItemNameResolver.LoadApiNames());
            StartCoroutine(NpcProviderRegistry.RefreshAtStartup(success =>
            {
                if (!success)
                    Log.LogWarning("[MEL BotGen] Initial registry refresh failed; NPC choices will remain unavailable until a successful refresh.");
            }));

            var decalProbe = base.Config.Bind("Diagnostics", "EnableDecalFeasibilityProbe", false, "Temporary M0 diagnostic: log shader availability and PNG loading results. Does not modify the world.");
            if (decalProbe.Value)
                StartCoroutine(DecalFeasibilityProbe.Run());
        }

    }
}
