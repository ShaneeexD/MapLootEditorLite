using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Common.Models.Logging;
using MapLootEditorLite.Server.Patches;
using WTTServerCommonLib;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace MapLootEditorLite.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.shaneeexd.mapeditorlite";
    public string Name { get; init; } = "MapEditorLite";
    public string Author { get; init; } = "Shane";
    public List<string>? Contributors { get; init; } = null;
    public Version Version { get; init; } = new("2.0.2");
    public Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; } = null;
    public Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("~3.0.2") }
    };
    public string? Url { get; init; } = null;
    public bool HasPrepatcher { get; init; } = false;
    public string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public class ServerPlugin : IOnLoad
{
    public static ISptLogger<ServerPlugin>? Logger { get; private set; }

    private readonly ISptLogger<ServerPlugin> _logger;
    private readonly WTTServerCommonLib.WTTServerCommonLib _wttCommon;
    private readonly LocationTable _locationTable;
    private readonly ProfileHelper _profileHelper;

    public ServerPlugin(ISptLogger<ServerPlugin> logger, WTTServerCommonLib.WTTServerCommonLib wttCommon, LocationTable locationTable, ProfileHelper profileHelper)
    {
        _logger = logger;
        Logger = logger;
        _wttCommon = wttCommon;
        _locationTable = locationTable;
        _profileHelper = profileHelper;
    }

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _logger.Info("[MEL] Map Editor Lite server mod loading");

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var modDirectory = Path.GetDirectoryName(assembly.Location);
            if (string.IsNullOrEmpty(modDirectory))
            {
                _logger.Error("[MEL] Unable to determine mod directory");
                return;
            }

            var packs = PackLoader.LoadPacks(modDirectory);
            PackRegistry.Register(packs);

            var forcedSpawnDirectory = Path.Combine(modDirectory, "db", "CustomLootspawns");
            WttSpawnConverter.WriteForcedSpawns(packs, forcedSpawnDirectory);
            await _wttCommon.CustomLootspawnService.CreateCustomLootSpawns(assembly, Path.Combine("db", "CustomLootspawns"));
            _logger.Info($"[MEL] Registered forced quest spawns with WTT-CommonLib from {forcedSpawnDirectory}");

            var staticSpawnDirectory = Path.Combine(modDirectory, "db", "CustomStaticSpawns");
            WttStaticSpawnConverter.WriteStaticSpawns(packs, staticSpawnDirectory);
            await _wttCommon.CustomStaticSpawnService.CreateCustomStaticSpawns(assembly, Path.Combine("db", "CustomStaticSpawns"));
            _logger.Info($"[MEL] Registered custom static spawns with WTT-CommonLib from {staticSpawnDirectory}");

            QuestFilter.Initialize(_profileHelper);
            LootTransformer.Register(_locationTable);
            InteractiveObjectTransformer.Register(_locationTable);
            new LocationControllerGenerateAllPatch().Enable();
            new MatchControllerStartLocalRaidPatch().Enable();
            _logger.Info("[MEL] Enabled quest filter patches on LocationController.GenerateAll and MatchController.StartLocalRaid");

            _logger.Info($"[MEL] Map Editor Lite server mod loaded. {PackRegistry.TotalSpawnCount()} custom spawns registered across {packs.Count} packs.");
        }
        catch (Exception ex)
        {
            _logger.Error($"[MEL] Failed to load Map Editor Lite server mod: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}
