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

    public NyanPoi Clone()
    {
        return (NyanPoi)MemberwiseClone();
    }
}

// Owns the POIs. They live on the server so the SIC page can edit them and the client gets them fresh each raid.
// pois.json is never shipped: it is seeded from pois.default.json once, so updating the mod can't wipe a user's placements.
[Injectable(InjectionType.Singleton)]
public class NyanPoiStore
{
    public const double MinRadius = 5;
    public const double MaxRadius = 500;

    // Ground Zero is two locations, one for each player level bracket, and they share one set of POIs
    private const string GroundZero = "sandbox";
    private const string GroundZeroHigh = "sandbox_high";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _lock = new();
    private readonly ISptLogger<NyanPoiStore> _logger;
    private readonly string _path;
    private Dictionary<string, List<NyanPoi>> _pois = NewTable();

    public NyanPoiStore(ISptLogger<NyanPoiStore> logger, ModHelper modHelper)
    {
        _logger = logger;
        string folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        _path = Path.Combine(folder, "pois.json");
        Load(Path.Combine(folder, "pois.default.json"));
    }

    // Set when pois.json couldn't be read. Saving is refused then, so a typo in the file can't lose everything else in it.
    public string? LoadError { get; private set; }

    // The game locations that a map on the SIC page stands for
    public static IReadOnlyList<string> LocationsFor(string map)
    {
        return string.Equals(map, GroundZero, StringComparison.OrdinalIgnoreCase) ? [GroundZero, GroundZeroHigh] : [map];
    }

    public Dictionary<string, List<NyanPoi>> GetAll()
    {
        lock (_lock)
        {
            return _pois.ToDictionary(pair => pair.Key, pair => pair.Value.Select(poi => poi.Clone()).ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save(Dictionary<string, List<NyanPoi>> pois)
    {
        lock (_lock)
        {
            if (LoadError != null)
            {
                throw new InvalidOperationException(LoadError);
            }

            // Written to one side first, so a failure part way through can't leave a damaged pois.json
            Dictionary<string, List<NyanPoi>> copy = pois.ToDictionary(pair => pair.Key, pair => pair.Value.Select(poi => poi.Clone()).ToList(), StringComparer.OrdinalIgnoreCase);
            ShareGroundZero(copy);

            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(copy, WriteOptions));
            File.Move(temp, _path, overwrite: true);
            _pois = copy;
        }
    }

    private void Load(string defaultPath)
    {
        try
        {
            if (!File.Exists(_path) && File.Exists(defaultPath))
            {
                File.Copy(defaultPath, _path);
            }
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<NyanPoi>>>(File.ReadAllText(_path), ReadOptions);
                _pois = new Dictionary<string, List<NyanPoi>>(loaded ?? NewTable(), StringComparer.OrdinalIgnoreCase);

                // Older versions could place POIs on the high level alone, so those carry over rather than being lost
                if (!_pois.ContainsKey(GroundZero) && _pois.TryGetValue(GroundZeroHigh, out List<NyanPoi>? highOnly))
                {
                    _pois[GroundZero] = highOnly;
                }
                ShareGroundZero(_pois);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadError = $"Could not read {_path}: {ex.Message}";
            _logger.Error($"[NyanBoss] {LoadError}");
        }
    }

    private static void ShareGroundZero(Dictionary<string, List<NyanPoi>> pois)
    {
        if (pois.TryGetValue(GroundZero, out List<NyanPoi>? shared))
        {
            pois[GroundZeroHigh] = shared.Select(poi => poi.Clone()).ToList();
        }
        else
        {
            pois.Remove(GroundZeroHigh);
        }
    }

    private static Dictionary<string, List<NyanPoi>> NewTable()
    {
        return new Dictionary<string, List<NyanPoi>>(StringComparer.OrdinalIgnoreCase);
    }
}

// The client fetches its POIs from here when the boss spawns
[Injectable]
public class NyanPoiRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, NyanPoiStore store) : StaticRouter(jsonUtil,
[
    new RouteAction("/nyanboss/pois",
        (string url, IRequestData info, MongoId sessionId, string? output, CancellationToken cancellationToken) =>
            new ValueTask<object>(httpResponseUtil.NoBody(store.GetAll()))),
]);
