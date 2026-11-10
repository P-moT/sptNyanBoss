// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Web;

namespace NyanBoss;

public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.pmot.nyanboss";
    public string Name { get; init; } = "NyanBoss";
    public string Author { get; init; } = "P_moT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.6.2");
    public SemanticVersioning.Range SptVersion { get; init; } = new("4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "GPL-3.0-only";
    public bool HasPrepatcher { get; init; } = false;
    public string? WWWRootUrl { get; init; } = "nyanboss-static";
    public string? HomePage { get; init; } = "/nyanboss";
    public string? HomePageDescription { get; init; } = "Place and resize the POIs NyanBoss defends on each map.";
}

public class NyanBossConfig
{
    public bool Enabled { get; set; } = true;
    public string GearSourceBotType { get; set; } = "bossknight";
    public Dictionary<string, NyanBossSpawn> Spawns { get; set; } = new();

    public static NyanBossConfig Load(string modFolder)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<NyanBossConfig>(File.ReadAllText(Path.Combine(modFolder, "config.json")), options) ?? new NyanBossConfig();
    }
}

public class NyanBossSpawn
{
    public double Chance { get; set; }
    public string Zone { get; set; } = "";
}

// The boss borrows the "bossTest" role: the client already treats it as a boss, but vanilla ships no bot data for it and
// spawns it nowhere, so nothing else is affected. The client plugin swaps its stub brain for a real one.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class NyanBossLoader(
    ISptLogger<NyanBossLoader> logger,
    ModHelper modHelper,
    BotTable botTable,
    LocationTable locationTable,
    ICloner cloner) : IOnLoad
{
    private const string RoleName = "bossTest";
    private const string BotTypeKey = "bosstest";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        NyanBossConfig config = NyanBossConfig.Load(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()));
        if (!config.Enabled)
        {
            logger.Info("[NyanBoss] Disabled in config");
            return Task.CompletedTask;
        }

        if (!botTable.Types.TryGetValue(config.GearSourceBotType, out var source) || source == null)
        {
            logger.Error($"[NyanBoss] Bot type '{config.GearSourceBotType}' not found, boss will not spawn");
            return Task.CompletedTask;
        }

        var botType = cloner.Clone(source)!;
        botType.FirstNames = ["Nyan"];
        botType.LastNames = [];
        botTable.Types[BotTypeKey] = botType;

        Dictionary<string, Location> locations = locationTable.GetDictionary();
        int added = 0;
        foreach ((string map, NyanBossSpawn spawn) in config.Spawns)
        {
            if (spawn.Chance <= 0)
            {
                continue;
            }

            if (!locations.TryGetValue(locationTable.GetMappedKey(map), out var location))
            {
                logger.Warning($"[NyanBoss] Unknown map '{map}' in config, skipped");
                continue;
            }

            location.Base.BossLocationSpawn.Add(new BossLocationSpawn
            {
                BossName = RoleName,
                BossChance = spawn.Chance,
                BossZone = spawn.Zone,
                BossDifficulty = "normal",
                BossEscortType = "followerBully",
                BossEscortDifficulty = "normal",
                BossEscortAmount = "0",
                IsBossPlayer = false,
                IsRandomTimeSpawn = false,
                IgnoreMaxBots = true,
                ShowOnTarkovMap = false,
                ShowOnTarkovMapPvE = false,
                SpawnMode = ["regular", "pve"],
                Supports = null!,
                Time = -1,
                TriggerId = "",
                TriggerName = "",
            });
            added++;
        }

        logger.Success($"[NyanBoss] Registered '{RoleName}' with gear from '{config.GearSourceBotType}', spawn entries added on {added} map(s)");
        return Task.CompletedTask;
    }
}
