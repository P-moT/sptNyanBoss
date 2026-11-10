// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common;
using BodyPart = SPTarkov.Server.Core.Models.Eft.Common.Tables.BodyPart;
using BotType = SPTarkov.Server.Core.Models.Eft.Common.Tables.BotType;
using GenerationData = SPTarkov.Server.Core.Models.Eft.Common.Tables.GenerationData;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Enums;
using Preset = SPTarkov.Server.Core.Models.Spt.Tables.Preset;
using Prefab = SPTarkov.Server.Core.Models.Eft.Common.Tables.Prefab;
using TemplateItemProperties = SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItemProperties;
using SPTarkov.Server.Core.Services.Modding.Custom;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Models.Spt.Bots;
using BotBaseInventory = SPTarkov.Server.Core.Models.Eft.Common.Tables.BotBaseInventory;
using Item = SPTarkov.Server.Core.Models.Eft.Common.Tables.Item;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Reflection.Patching;
using HarmonyLib;
using SPTarkov.Server.Web;

namespace NyanBoss;

public record ModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.pmot.nyanboss";
    public string Name { get; init; } = "NyanBoss";
    public string Author { get; init; } = "P_moT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.7.6");
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
    // Before 0.7.6 each map's spawn chance and zone were set here. They now live in pois.json and are set on the SIC page;
    // this is only read once, to carry old settings over.
    public Dictionary<string, NyanBossSpawn> Spawns { get; set; } = new();

    // Per body part, e.g. "Head": 50. Parts left out keep the gear source's values.
    public Dictionary<string, double> Health { get; set; } = new();
    public List<NyanBossTrophy> Trophies { get; set; } = new();
    // Older single-trophy setting, still read so existing configs keep working
    public NyanBossTrophy? Trophy { get; set; }
    public NyanBossLoadout? Loadout { get; set; }

    public static NyanBossConfig Load(string modFolder)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<NyanBossConfig>(File.ReadAllText(Path.Combine(modFolder, "config.json")), options) ?? new NyanBossConfig();
    }
}

public class NyanBossLoadout
{
    // Slot name to item ids, one picked at random per spawn. A listed slot with no items is left empty; unlisted slots keep the gear source's.
    public Dictionary<string, List<string>> Equipment { get; set; } = new();
    // Weapon builds from the game's preset list, used whole so the gun spawns with every attachment
    public List<string> WeaponPresets { get; set; } = new();
    // Replaces the ammo for each of these rounds' calibers
    public List<string> Ammo { get; set; } = new();
}

// Rolled on its own for every boss: Chance percent to drop, then Min to Max copies
public class NyanBossTrophy
{
    public string Item { get; set; } = "";
    public double Chance { get; set; }
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
}

public class NyanBossSpawn
{
    public double Chance { get; set; }
    public string Zone { get; set; } = "";
}

// Items with their own models, shipped in this mod's bundles. Each is a copy of a vanilla item with only the model swapped.
internal static class NyanItems
{
    internal const string UlachTpl = "6a5c9d1e7f3b2a4c8d0e1f21";
    private const string VanillaUlachBlack = "5b40e1525acfc4771e1c6611";

    // Custom item to the vanilla item it copies, so its armour plates can be borrowed from bots that wear the original
    internal static readonly Dictionary<string, string> CloneSources = new() { [UlachTpl] = VanillaUlachBlack };

    internal static void Create(CustomItemService customItems, ISptLogger<NyanItemsLoader> logger)
    {
        var details = new NewItemFromCloneDetails
        {
            ItemTplToClone = VanillaUlachBlack,
            NewId = UlachTpl,
            NewItemName = "item_equipment_helmet_ulach_nyan",
            ParentId = "5a341c4086f77401f2541505",
            HandbookParentId = "5b47574386f77428ca22b330",
            HandbookPriceRoubles = 10524,
            FleaPriceRoubles = 103755,
            OverrideProperties = new TemplateItemProperties
            {
                Prefab = new Prefab { Path = "nyanboss/item_equipment_helmet_ulach_nyan.bundle", Rcid = "" },
            },
            Locales = new Dictionary<string, LocaleDetails>
            {
                ["en"] = new()
                {
                    Name = "HighCom Striker ULACH IIIA helmet (Nyan)",
                    ShortName = "Nyan ULACH",
                    Description = "A ULACH helmet in Nyan grey, with a pair of pixel cat ears. Same protection as the original, considerably more style.",
                },
            },
        };
        var result = customItems.CreateItemFromClone(details, Assembly.GetExecutingAssembly());
        if (result.Success != true)
        {
            logger.Error($"[NyanBoss] Could not create the Nyan ULACH: {string.Join("; ", result.Errors ?? [])}");
        }
    }
}

// The server refuses new items once player profiles have loaded, so they go in right after the database, well before the boss
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class NyanItemsLoader(ISptLogger<NyanItemsLoader> logger, CustomItemService customItemService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        NyanItems.Create(customItemService, logger);
        return Task.CompletedTask;
    }
}

// The boss borrows the "bossTest" role: the client already treats it as a boss, but vanilla ships no bot data for it and
// spawns it nowhere, so nothing else is affected. The client plugin swaps its stub brain for a real one.
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class NyanBossLoader(
    ISptLogger<NyanBossLoader> logger,
    ModHelper modHelper,
    BotTable botTable,
    LocationTable locationTable,
    ItemHelper itemHelper,
    GlobalTable globalTable,
    BotGeneratorHelper botGeneratorHelper,
    NyanPoiStore poiStore,
    ICloner cloner) : IOnLoad
{
    internal const string RoleName = "bossTest";
    private const string BotTypeKey = "bosstest";

    private static readonly string[] BodyParts = ["Head", "Chest", "Stomach", "LeftArm", "RightArm", "LeftLeg", "RightLeg"];

    private void ApplyHealth(BotType botType, Dictionary<string, double> health)
    {
        foreach ((string name, double value) in health)
        {
            string? part = BodyParts.FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));
            if (part == null || value < 1)
            {
                logger.Warning($"[NyanBoss] Ignoring health entry '{name}': {value}. Body parts are {string.Join(", ", BodyParts)}, each at least 1");
                continue;
            }
            foreach (BodyPart parts in botType.BotHealth.BodyParts)
            {
                MinMax<double> range = part switch
                {
                    "Head" => parts.Head,
                    "Chest" => parts.Chest,
                    "Stomach" => parts.Stomach,
                    "LeftArm" => parts.LeftArm,
                    "RightArm" => parts.RightArm,
                    "LeftLeg" => parts.LeftLeg,
                    _ => parts.RightLeg,
                };
                range.Min = value;
                range.Max = value;
            }
        }
    }

    // Bot types to borrow armour plate and insert setups from, after the gear source. These wear fixed Granit plates, unlike
    // the bear and usec types, which roll random plates up to class 5.
    private static readonly string[] ModDonors = ["pmcbot", "pmcbear", "pmcusec"];

    private bool IsKnownItem(string id, string what)
    {
        if (MongoId.IsValidMongoId(id) && itemHelper.IsValidItem(new MongoId(id)))
        {
            return true;
        }
        logger.Warning($"[NyanBoss] {what} '{id}' is not a valid item, skipped");
        return false;
    }

    private void ApplyLoadout(BotType botType, NyanBossLoadout? loadout, string gearSource)
    {
        if (loadout == null)
        {
            return;
        }
        var inventory = botType.BotInventory;
        var chances = botType.BotChances;

        foreach ((string slotName, List<string> ids) in loadout.Equipment)
        {
            if (!Enum.TryParse(slotName, ignoreCase: true, out EquipmentSlots slot))
            {
                logger.Warning($"[NyanBoss] Unknown equipment slot '{slotName}', skipped. Slots are {string.Join(", ", Enum.GetNames<EquipmentSlots>())}");
                continue;
            }
            var options = new Dictionary<MongoId, double>();
            foreach (string id in ids.Where(id => IsKnownItem(id, $"{slot} item")))
            {
                options[new MongoId(id)] = 1;
                BorrowEquipmentMods(botType, new MongoId(id), gearSource);
            }
            inventory.Equipment[slot] = options;
            chances.EquipmentChances[slot.ToString()] = options.Count > 0 ? 100 : 0;
        }

        Dictionary<MongoId, Preset> presets = globalTable.ItemPresets;
        foreach (string presetId in loadout.WeaponPresets)
        {
            if (!MongoId.IsValidMongoId(presetId) || !presets.TryGetValue(new MongoId(presetId), out Preset? preset))
            {
                logger.Warning($"[NyanBoss] Weapon preset '{presetId}' not found, skipped");
                continue;
            }
            ApplyWeaponPreset(botType, preset);
        }

        foreach (string ammoId in loadout.Ammo.Where(id => IsKnownItem(id, "Ammo")))
        {
            string? caliber = itemHelper.GetItem(new MongoId(ammoId)).Value?.Properties?.Caliber;
            if (string.IsNullOrEmpty(caliber))
            {
                logger.Warning($"[NyanBoss] Ammo '{ammoId}' has no calibre, skipped");
                continue;
            }
            inventory.Ammo[caliber] = new Dictionary<MongoId, double> { [new MongoId(ammoId)] = 1 };
        }
    }

    // Armour needs its plates and soft inserts listed, or it spawns as an empty shell. Copied from a bot that already wears it.
    private void BorrowEquipmentMods(BotType botType, MongoId item, string gearSource)
    {
        var mods = botType.BotInventory.Mods;
        if (mods.ContainsKey(item))
        {
            return;
        }
        // A custom copy of a vanilla item is worn by nobody, so look for bots wearing the original instead
        MongoId lookup = NyanItems.CloneSources.TryGetValue(item.ToString(), out string? original) ? new MongoId(original) : item;
        IEnumerable<string> donors = new[] { gearSource }.Concat(ModDonors).Concat(botTable.Types.Keys.OrderBy(key => key, StringComparer.Ordinal));
        foreach (string donor in donors)
        {
            if (botTable.Types.TryGetValue(donor, out BotType? source) && source?.BotInventory?.Mods != null
                && source.BotInventory.Mods.TryGetValue(lookup, out var slots))
            {
                mods[item] = slots.ToDictionary(pair => pair.Key, pair => new HashSet<MongoId>(pair.Value));
                foreach (string slot in slots.Keys)
                {
                    botType.BotChances.EquipmentModsChances[slot.ToLowerInvariant()] = 100;
                }
                return;
            }
        }
    }

    // Turns a preset into a fixed attachment tree, so every slot it fills is always filled with exactly that part
    private void ApplyWeaponPreset(BotType botType, Preset preset)
    {
        var mods = botType.BotInventory.Mods;
        Dictionary<string, MongoId> templates = preset.Items.ToDictionary(item => item.Id.ToString(), item => item.Template);
        var children = preset.Items.Where(item => item.ParentId != null && templates.ContainsKey(item.ParentId) && item.SlotId != null);
        foreach (var group in children.GroupBy(item => templates[item.ParentId!]))
        {
            mods[group.Key] = group.GroupBy(item => item.SlotId!).ToDictionary(slot => slot.Key, slot => slot.Select(item => item.Template).ToHashSet());
            foreach (string slot in group.Select(item => item.SlotId!))
            {
                botType.BotChances.WeaponModsChances[slot] = 100;
            }
        }
    }

    // Checks the trophy list once at startup. The drops themselves are rolled per boss by NyanTrophyPatch.
    private List<NyanBossTrophy> ValidTrophies(NyanBossConfig config)
    {
        IEnumerable<NyanBossTrophy> listed = config.Trophies.Count > 0 || config.Trophy == null ? config.Trophies : [config.Trophy];
        var valid = new List<NyanBossTrophy>();
        foreach (NyanBossTrophy trophy in listed)
        {
            if (trophy.Chance <= 0 || !IsKnownItem(trophy.Item, "Trophy"))
            {
                continue;
            }
            if (trophy.Min < 1 || trophy.Max < trophy.Min)
            {
                logger.Warning($"[NyanBoss] Trophy '{trophy.Item}' needs min at least 1 and max at least min (got {trophy.Min}-{trophy.Max}), skipped");
                continue;
            }
            valid.Add(new NyanBossTrophy { Item = trophy.Item, Chance = Math.Min(trophy.Chance, 100), Min = trophy.Min, Max = trophy.Max });
        }
        return valid;
    }

    // The gear source's own special loot would roll on top of the trophies, so it is switched off
    private static void ClearSpecialLoot(BotType botType)
    {
        botType.BotInventory.Items.SpecialLoot = new Dictionary<MongoId, double>();
        botType.BotGeneration.Items.SpecialItems = new GenerationData
        {
            Weights = new Dictionary<double, double> { [0] = 1 },
            Whitelist = new Dictionary<MongoId, double>(),
        };
    }

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
        ApplyHealth(botType, config.Health);
        ApplyLoadout(botType, config.Loadout, config.GearSourceBotType);
        ClearSpecialLoot(botType);
        botTable.Types[BotTypeKey] = botType;
        List<NyanBossTrophy> trophies = ValidTrophies(config);
        NyanTrophyPatch.Configure(trophies, botGeneratorHelper, logger);
        new NyanTrophyPatch().Enable();

        // Spawn chances and POIs are set on the SIC page and read fresh for every raid, so edits apply without a restart
        NyanRaidSpawnPatch.Configure(poiStore, logger);
        new NyanRaidSpawnPatch().Enable();
        int maps = poiStore.GetAll().Count(pair => pair.Value.Chance > 0);
        logger.Success($"[NyanBoss] Registered '{RoleName}' with gear from '{config.GearSourceBotType}', spawning on {maps} map(s)");
        return Task.CompletedTask;
    }
}

// Spawn mods such as Acid's Bot Placement System rebuild each map's boss list from their own config, on load, whenever
// their config is saved, and after every raid, which drops any boss they don't know about. So instead of adding him to
// the shared map data once, he is added to each raid's own copy as it is generated, after every other mod has had its say.
// Deliberately not [Injectable]: some mods enable every registered patch, and that would enable this one twice.
public class NyanRaidSpawnPatch : AbstractPatch
{
    private static NyanPoiStore? _store;
    private static ISptLogger<NyanBossLoader>? _logger;

    internal static void Configure(NyanPoiStore store, ISptLogger<NyanBossLoader> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot));
    }

    [PatchPostfix]
    public static void Postfix(string name, LocationBase __result)
    {
        NyanMapSettings? settings = _store?.Get(name);
        if (__result == null || settings == null || settings.Chance <= 0)
        {
            return;
        }

        __result.BossLocationSpawn ??= [];
        if (__result.BossLocationSpawn.Any(boss => string.Equals(boss.BossName, NyanBossLoader.RoleName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string? zone = string.IsNullOrWhiteSpace(settings.Zone) ? NearestZone(__result, settings.Pois) : settings.Zone;
        if (zone == null)
        {
            _logger?.Warning($"[NyanBoss] No bot zone found on {name}, so he can't spawn there");
            return;
        }

        double chance = Math.Min(settings.Chance, 100);
        __result.BossLocationSpawn.Add(new BossLocationSpawn
        {
            BossName = NyanBossLoader.RoleName,
            BossChance = chance,
            BossZone = zone,
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
        _logger?.Info($"[NyanBoss] Added to this raid on {name}: chance {chance}, zone {zone}");
    }

    // He is moved into a POI as soon as he appears, so the zone only needs to be a real one, ideally close by.
    // Boss zones are preferred, then any bot zone, closest to the centre of the POIs.
    private static string? NearestZone(LocationBase location, List<NyanPoi> pois)
    {
        var points = (location.SpawnPointParams ?? [])
            .Where(point => !string.IsNullOrEmpty(point.BotZoneName) && point.Position.HasValue)
            .Select(point => (Zone: point.BotZoneName!, Position: point.Position!.Value, Boss: point.Categories?.Contains("Boss") == true))
            .ToList();
        if (points.Count == 0)
        {
            return null;
        }
        if (pois.Count == 0)
        {
            return (points.FirstOrDefault(point => point.Boss).Zone) ?? points[0].Zone;
        }
        double x = pois.Average(poi => poi.X);
        double z = pois.Average(poi => poi.Z);
        return points
            .OrderByDescending(point => point.Boss)
            .ThenBy(point => (point.Position.X - x) * (point.Position.X - x) + (point.Position.Z - z) * (point.Position.Z - z))
            .First().Zone;
    }
}
// Trophies are rolled one by one for each boss, each with its own chance and count, and placed before his regular loot is
// generated, so they get first pick of the space in his bag, rig and pockets. Not [Injectable], for the same reason as above.
public class NyanTrophyPatch : AbstractPatch
{
    private static readonly HashSet<EquipmentSlots> Containers = [EquipmentSlots.Backpack, EquipmentSlots.TacticalVest, EquipmentSlots.Pockets];
    private static List<NyanBossTrophy> _trophies = [];
    private static BotGeneratorHelper? _helper;
    private static ISptLogger<NyanBossLoader>? _logger;

    internal static void Configure(List<NyanBossTrophy> trophies, BotGeneratorHelper helper, ISptLogger<NyanBossLoader> logger)
    {
        _trophies = trophies;
        _helper = helper;
        _logger = logger;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotLootGenerator), nameof(BotLootGenerator.GenerateLoot));
    }

    [PatchPrefix]
    public static void Prefix(MongoId botId, BotGenerationDetails botGenerationDetails, BotBaseInventory botInventory)
    {
        if (_helper == null || _trophies.Count == 0
            || !string.Equals(botGenerationDetails.Role, NyanBossLoader.RoleName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        foreach (NyanBossTrophy trophy in _trophies)
        {
            if (Random.Shared.NextDouble() * 100 >= trophy.Chance)
            {
                continue;
            }
            int count = Random.Shared.Next(trophy.Min, trophy.Max + 1);
            var template = new MongoId(trophy.Item);
            for (int i = 0; i < count; i++)
            {
                var item = new Item { Id = new MongoId(), Template = template };
                ItemAddedResult result = _helper.AddItemWithChildrenToEquipmentSlot(botId, Containers, item.Id, template, [item], botInventory);
                if (result != ItemAddedResult.SUCCESS)
                {
                    _logger?.Warning($"[NyanBoss] No room for trophy '{trophy.Item}' ({result}), {count - i} of {count} not added");
                    break;
                }
            }
        }
    }
}