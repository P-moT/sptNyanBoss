// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace NyanBoss;

public class NyanPoi
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Radius { get; set; }
    // How likely he is to pick this POI, against the map's others: when he spawns, and each time he moves on. 0 is never.
    public double Weight { get; set; } = 1;

    public NyanPoi Clone()
    {
        return (NyanPoi)MemberwiseClone();
    }
}

// Everything the SIC page sets for one map
public class NyanMapSettings
{
    // Percent chance he spawns on this map at all
    public double Chance { get; set; }
    // The bot zone the game spawns him in before he is moved into a POI. Left empty, the zone nearest the POIs is used.
    public string? Zone { get; set; }
    // Whether he moves between POIs during the raid. Off, he defends the one he spawned in until he dies.
    // Older pois.json files don't have it, so it starts on, as he behaved before it existed.
    public bool Travel { get; set; } = true;
    public List<NyanPoi> Pois { get; set; } = new();

    public NyanMapSettings Clone()
    {
        return new NyanMapSettings { Chance = Chance, Zone = Zone, Travel = Travel, Pois = Pois.Select(poi => poi.Clone()).ToList() };
    }
}

// Owns each map's spawn chance and POIs. They live on the server so the SIC page can edit them and both the server and
// the client pick them up fresh each raid. pois.json is never shipped: it is seeded from pois.default.json once, so
// updating the mod can't wipe a user's settings.
[Injectable(InjectionType.Singleton)]
public class NyanPoiStore
{
    public const double MinRadius = 5;
    public const double MaxRadius = 500;
    public const double MaxWeight = 100;

    // Ground Zero is two locations, one for each player level bracket, and they share one set of settings
    private const string GroundZero = "sandbox";
    private const string GroundZeroHigh = "sandbox_high";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _lock = new();
    private readonly ISptLogger<NyanPoiStore> _logger;
    private readonly string _path;
    private Dictionary<string, NyanMapSettings> _maps = NewTable();

    public NyanPoiStore(ISptLogger<NyanPoiStore> logger, ModHelper modHelper)
    {
        _logger = logger;
        string folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _path = Path.Combine(folder, "pois.json");
        Load(folder);
    }

    // Set when pois.json couldn't be read. Saving is refused then, so a typo in the file can't lose everything else in it.
    public string? LoadError { get; private set; }

    // The game locations that a map on the SIC page stands for
    public static IReadOnlyList<string> LocationsFor(string map)
    {
        return string.Equals(map, GroundZero, StringComparison.OrdinalIgnoreCase) ? [GroundZero, GroundZeroHigh] : [map];
    }

    public Dictionary<string, NyanMapSettings> GetAll()
    {
        lock (_lock)
        {
            return _maps.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
    }

    // Null when nothing is set for this location
    public NyanMapSettings? Get(string location)
    {
        lock (_lock)
        {
            return _maps.TryGetValue(location, out NyanMapSettings? settings) ? settings.Clone() : null;
        }
    }

    public void Save(Dictionary<string, NyanMapSettings> maps)
    {
        lock (_lock)
        {
            if (LoadError != null)
            {
                throw new InvalidOperationException(LoadError);
            }

            // Written to one side first, so a failure part way through can't leave a damaged pois.json
            Dictionary<string, NyanMapSettings> copy = maps.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase);
            ShareGroundZero(copy);

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(copy, WriteOptions));
            File.Move(temp, _path, overwrite: true);
            _maps = copy;
        }
    }

    private void Load(string folder)
    {
        string defaultPath = Path.Combine(folder, "pois.default.json");
        try
        {
            Dictionary<string, NyanMapSettings> defaults = File.Exists(defaultPath) ? Read(defaultPath, NewTable(), NewSpawns()) : NewTable();
            if (!File.Exists(_path) && File.Exists(defaultPath))
            {
                File.Copy(defaultPath, _path);
            }
            if (!File.Exists(_path))
            {
                return;
            }

            bool migrated = false;
            _maps = Read(_path, defaults, OldConfigSpawns(folder), () => migrated = true);

            // Older versions could place POIs on the high level alone, so those carry over rather than being lost
            if (!_maps.ContainsKey(GroundZero) && _maps.TryGetValue(GroundZeroHigh, out NyanMapSettings? highOnly))
            {
                _maps[GroundZero] = highOnly;
            }
            ShareGroundZero(_maps);

            if (migrated)
            {
                Save(_maps);
                _logger.Info("[NyanBoss] pois.json updated to hold spawn chances, which are now set on the SIC page");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadError = $"Could not read {_path}: {ex.Message}";
            _logger.Error($"[NyanBoss] {LoadError}");
        }
    }

    // Reads either format. Before 0.7.6 each map was just a list of POIs, and its chance and zone were in config.json;
    // those maps take their chance and zone from the old config if it still has them, and otherwise from the defaults.
    private static Dictionary<string, NyanMapSettings> Read(string path, Dictionary<string, NyanMapSettings> defaults,
        Dictionary<string, NyanBossSpawn> oldSpawns, Action? onOldFormat = null)
    {
        var maps = NewTable();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonProperty map in document.RootElement.EnumerateObject())
        {
            if (map.Value.ValueKind == JsonValueKind.Array)
            {
                onOldFormat?.Invoke();
                var settings = new NyanMapSettings { Pois = map.Value.Deserialize<List<NyanPoi>>(ReadOptions) ?? new List<NyanPoi>() };
                if (oldSpawns.TryGetValue(map.Name, out NyanBossSpawn? old))
                {
                    settings.Chance = old.Chance;
                    settings.Zone = string.IsNullOrWhiteSpace(old.Zone) ? null : old.Zone;
                }
                else if (defaults.TryGetValue(map.Name, out NyanMapSettings? fallback))
                {
                    settings.Chance = fallback.Chance;
                    settings.Zone = fallback.Zone;
                }
                maps[map.Name] = settings;
            }
            else
            {
                maps[map.Name] = map.Value.Deserialize<NyanMapSettings>(ReadOptions) ?? new NyanMapSettings();
            }
        }
        return maps;
    }

    // The per-map spawn settings config.json held before 0.7.6, used once to carry them over
    private Dictionary<string, NyanBossSpawn> OldConfigSpawns(string folder)
    {
        try
        {
            return new Dictionary<string, NyanBossSpawn>(NyanBossConfig.Load(folder).Spawns, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.Warning($"[NyanBoss] Could not read config.json for old spawn settings: {ex.Message}");
            return NewSpawns();
        }
    }

    private static void ShareGroundZero(Dictionary<string, NyanMapSettings> maps)
    {
        if (maps.TryGetValue(GroundZero, out NyanMapSettings? shared))
        {
            maps[GroundZeroHigh] = shared.Clone();
        }
        else
        {
            maps.Remove(GroundZeroHigh);
        }
    }

    private static Dictionary<string, NyanMapSettings> NewTable()
    {
        return new Dictionary<string, NyanMapSettings>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, NyanBossSpawn> NewSpawns()
    {
        return new Dictionary<string, NyanBossSpawn>(StringComparer.OrdinalIgnoreCase);
    }
}

// The client fetches each map's settings from here when the boss spawns: its POIs and whether he travels between them
[Injectable]
public class NyanPoiRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, NyanPoiStore store) : StaticRouter(jsonUtil,
[
    new RouteAction("/nyanboss/maps",
        (string url, IRequestData info, MongoId sessionId, string? output, CancellationToken cancellationToken) =>
            new ValueTask<object>(httpResponseUtil.NoBody(store.GetAll()))),
]);
