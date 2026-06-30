using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using MapLootEditorLite.Server.Patches;
using WTTServerCommonLib;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace MapLootEditorLite.Server;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.shane.maplooteditorlite";
    public override string Name { get; init; } = "MapLootEditorLite";
    public override string Author { get; init; } = "Shane";
    public override List<string>? Contributors { get; init; } = null;
    public override Version Version { get; init; } = new("1.0.0");
    public override Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; } = null;
    public override Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("~2.0.0") }
    };
    public override string? Url { get; init; } = null;
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public class ServerPlugin : IOnLoad
{
    public static ISptLogger<ServerPlugin>? Logger { get; private set; }

    private readonly ISptLogger<ServerPlugin> _logger;
    private readonly WTTServerCommonLib.WTTServerCommonLib _wttCommon;
    private readonly DatabaseService _databaseService;
    private readonly ProfileHelper _profileHelper;

    public ServerPlugin(ISptLogger<ServerPlugin> logger, WTTServerCommonLib.WTTServerCommonLib wttCommon, DatabaseService databaseService, ProfileHelper profileHelper)
    {
        _logger = logger;
        Logger = logger;
        _wttCommon = wttCommon;
        _databaseService = databaseService;
        _profileHelper = profileHelper;
    }

    public async Task OnLoad()
    {
        _logger.Info("[MLEL] MapLootEditorLite server mod loading");

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var modDirectory = Path.GetDirectoryName(assembly.Location);
            if (string.IsNullOrEmpty(modDirectory))
            {
                _logger.Error("[MLEL] Unable to determine mod directory");
                return;
            }

            var packs = PackLoader.LoadPacks(modDirectory);
            PackRegistry.Register(packs);

            var forcedSpawnDirectory = Path.Combine(modDirectory, "db", "CustomLootspawns");
            WttSpawnConverter.WriteForcedSpawns(packs, forcedSpawnDirectory);
            await _wttCommon.CustomLootspawnService.CreateCustomLootSpawns(assembly, Path.Combine("db", "CustomLootspawns"));
            _logger.Info($"[MLEL] Registered forced quest spawns with WTT-CommonLib from {forcedSpawnDirectory}");

            var staticSpawnDirectory = Path.Combine(modDirectory, "db", "CustomStaticSpawns");
            WttStaticSpawnConverter.WriteStaticSpawns(packs, staticSpawnDirectory);
            await _wttCommon.CustomStaticSpawnService.CreateCustomStaticSpawns(assembly, Path.Combine("db", "CustomStaticSpawns"));
            _logger.Info($"[MLEL] Registered custom static spawns with WTT-CommonLib from {staticSpawnDirectory}");

            QuestFilter.Initialize(_profileHelper);
            LootTransformer.Register(_databaseService);
            InteractiveObjectTransformer.Register(_databaseService);
            new LocationControllerGenerateAllPatch().Enable();
            new MatchControllerStartLocalRaidPatch().Enable();
            _logger.Info("[MLEL] Enabled quest filter patches on LocationController.GenerateAll and MatchController.StartLocalRaid");

            _logger.Info($"[MLEL] MapLootEditorLite server mod loaded. {PackRegistry.TotalSpawnCount()} custom spawns registered across {packs.Count} packs.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[MLEL] Failed to load MapLootEditorLite server mod: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}
