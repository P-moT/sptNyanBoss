// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.AI;

namespace NyanBoss.Client;

public class NyanPoi
{
    public string Name;
    public float X;
    public float Y;
    public float Z;
    public float Radius;

    [JsonIgnore]
    public Vector3 Position => new Vector3(X, Y, Z);
}

// POIs are owned by the server, so edits made on the SIC page reach the next raid without restarting the game
internal static class NyanPois
{
    private const string Route = "/nyanboss/pois";

    internal static List<NyanPoi> ForCurrentMap()
    {
        string map = Singleton<GameWorld>.Instance?.LocationId;
        if (map == null)
        {
            return null;
        }

        try
        {
            var byMap = JsonConvert.DeserializeObject<Dictionary<string, List<NyanPoi>>>(RequestHandler.GetJson(Route));
            var lookup = new Dictionary<string, List<NyanPoi>>(byMap, StringComparer.OrdinalIgnoreCase);
            return lookup.TryGetValue(map, out List<NyanPoi> pois) ? pois : null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Could not get POIs from the server, the boss will use his default behaviour: {ex.Message}");
            return null;
        }
    }
}

// SAIN treats a bot as "in combat" while one of its layers is marked active, and in that state it switches off the game's
// mover and blocks sprinting. It only clears the mark from its own layers' IsActive, which stops being called once a
// higher priority layer like ours takes over, so it has to be cleared here or he freezes. Uses reflection so SAIN stays optional.
internal class SainCombatFlag
{
    private static readonly MethodInfo GetSain = AccessTools.Method(AccessTools.TypeByName("SAIN.SAINEnableClass"), "GetSAIN");

    private readonly object[] _args;
    private object _component;
    private PropertyInfo _activeLayer;
    private object _none;

    internal SainCombatFlag(BotOwner bot)
    {
        _args = new object[] { bot.ProfileId, null };
    }

    internal void Clear()
    {
        if (GetSain == null)
        {
            return;
        }
        if (_component == null)
        {
            GetSain.Invoke(null, _args);
            _component = _args[1];
            if (_component == null)
            {
                return; // SAIN may not have set this bot up yet, or may not manage him at all
            }
            _activeLayer = AccessTools.Property(_component.GetType(), "ActiveLayer");
            _none = Enum.Parse(_activeLayer.PropertyType, "None");
        }
        _activeLayer.SetValue(_component, _none);
    }
}

// Shared between the layer, which decides when he patrols, and the logic, which moves him
internal class NyanDefendState
{
    private static readonly ConditionalWeakTable<BotOwner, NyanDefendState> States = new ConditionalWeakTable<BotOwner, NyanDefendState>();

    private const int SpawnAttempts = 30;
    private const float SpawnAreaFraction = 0.8f; // Stays off the edge, where the nearest ground can fall outside the circle
    private const float SpawnSampleRange = 6f;
    private const float CentreSampleRange = 20f;
    private const float SpawnMinPlayerDistance = 40f;
    private const float EyeHeight = 1.5f;

    private readonly List<NyanPoi> _pois;
    private int _current;
    private float _nextRotation;
    private bool _placed;

    internal float ProvokedUntil;

    private NyanDefendState(BotOwner bot)
    {
        _pois = NyanPois.ForCurrentMap();
        if (HasPois)
        {
            _current = UnityEngine.Random.Range(0, _pois.Count);
            _nextRotation = Time.time + Plugin.PoiRotationSeconds.Value;
            bot.GetPlayer.BeingHitAction += (_, _, _) => ProvokedUntil = Time.time + Plugin.ProvokedSeconds.Value;
            Plugin.Log.LogInfo($"Defending {Current.Name} of {_pois.Count} POI(s) on {Singleton<GameWorld>.Instance.LocationId}");
        }
        else
        {
            Plugin.Log.LogWarning($"No POIs for {Singleton<GameWorld>.Instance?.LocationId}, the boss will use his default behaviour");
        }
    }

    internal static NyanDefendState For(BotOwner bot)
    {
        return States.GetValue(bot, b => new NyanDefendState(b));
    }

    internal bool HasPois => _pois != null && _pois.Count > 0;

    internal NyanPoi Current => _pois[_current];

    // The game can only spawn him at a zone's fixed points, so he is moved into the POI as soon as he activates.
    // Without this he would sprint across the map from wherever the zone put him.
    internal void PlaceAtSpawnOnce(BotOwner bot)
    {
        if (_placed)
        {
            return;
        }
        _placed = true;

        if (!TryFindSpawn(Current, out Vector3 spawn))
        {
            Plugin.Log.LogWarning($"No reachable spot found inside {Current.Name}, he will run there from his spawn zone instead");
            return;
        }

        // The same steps the game uses to move a bot
        bot.Mover.Stop();
        bot.Mover.Teleport(spawn);
        bot.Transform.position = spawn;
        Plugin.Log.LogInfo($"Spawned inside {Current.Name} at {spawn}, {Vector3.Distance(spawn, Current.Position):F0}m from its centre");
    }

    private static bool TryFindSpawn(NyanPoi poi, out Vector3 spawn)
    {
        spawn = default;
        if (!NavMesh.SamplePosition(poi.Position, out NavMeshHit centre, CentreSampleRange, NavMesh.AllAreas))
        {
            return false;
        }

        List<Player> humans = Singleton<GameWorld>.Instance.AllAlivePlayersList.Where(player => !player.IsAI).ToList();
        var path = new NavMeshPath();
        bool found = false;
        bool bestHidden = false;
        float bestDistance = -1f;
        for (int i = 0; i < SpawnAttempts; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * (poi.Radius * SpawnAreaFraction);
            Vector3 candidate = poi.Position + new Vector3(offset.x, 0f, offset.y);

            // A complete route to the centre rules out roofs and sealed pockets he could never leave
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SpawnSampleRange, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(hit.position, centre.position, NavMesh.AllAreas, path)
                || path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            float distance = humans.Count == 0 ? float.MaxValue : humans.Min(player => Vector3.Distance(player.Position, hit.position));
            bool hidden = humans.All(player => !CanSee(player, hit.position));
            if (hidden && distance >= SpawnMinPlayerDistance)
            {
                spawn = hit.position;
                return true;
            }

            // Nowhere ideal yet: keep the best so far, preferring out of sight, then furthest from players
            if (!found || (hidden && !bestHidden) || (hidden == bestHidden && distance > bestDistance))
            {
                spawn = hit.position;
                found = true;
                bestHidden = hidden;
                bestDistance = distance;
            }
        }
        return found;
    }

    private static bool CanSee(Player player, Vector3 position)
    {
        Vector3 up = Vector3.up * EyeHeight;
        return !Physics.Linecast(player.Position + up, position + up, LayersMaskController.HighPolyWithTerrainMask);
    }

    internal void RotateIfDue()
    {
        if (_pois.Count < 2 || Time.time < _nextRotation)
        {
            return;
        }
        _current = (_current + UnityEngine.Random.Range(1, _pois.Count)) % _pois.Count;
        _nextRotation = Time.time + Plugin.PoiRotationSeconds.Value;
        Plugin.Log.LogInfo($"Moving to defend {Current.Name}");
    }
}

// Sits above the combat layers, including SAIN's. It hands control back to them (by going inactive) only when there is
// someone to fight: an enemy inside the defended area, or whoever just shot him. Everything else is ignored, which is what
// makes him avoidable.
public class NyanDefendLayer : CustomLayer
{
    internal const float ReturnMargin = 1.25f; // Stops him flicking between fighting and returning right on the boundary
    private const float ProvokedLeash = 2f;

    private readonly NyanDefendState _state;

    public NyanDefendLayer(BotOwner botOwner, int priority)
        : base(botOwner, priority)
    {
        _state = NyanDefendState.For(botOwner);
    }

    public override string GetName()
    {
        return "NyanDefend";
    }

    public override bool IsActive()
    {
        if (!_state.HasPois || BotOwner.IsDead)
        {
            return false;
        }

        _state.PlaceAtSpawnOnce(BotOwner);

        NyanPoi poi = _state.Current;
        float distanceFromPoi = Vector3.Distance(BotOwner.Position, poi.Position);
        if (Time.time < _state.ProvokedUntil)
        {
            return distanceFromPoi > poi.Radius * ProvokedLeash;
        }
        if (distanceFromPoi > poi.Radius * ReturnMargin)
        {
            return true;
        }

        EnemyInfo enemy = BotOwner.Memory.GoalEnemy;
        return enemy == null || Vector3.Distance(enemy.CurrPosition, poi.Position) > poi.Radius;
    }

    public override Action GetNextAction()
    {
        return new Action(typeof(NyanPatrolLogic), "Patrolling");
    }

    public override bool IsCurrentActionEnding()
    {
        return false;
    }
}

public class NyanPatrolLogic : CustomLogic
{
    private const int PickAttempts = 12;
    private const float SampleRange = 6f;
    private const float ReachDistance = 3f;
    private const float RetryDelay = 0.5f;
    private const float MaxLegSeconds = 15f; // Picks somewhere new if he gets stuck on the way

    private readonly NyanDefendState _state;
    private readonly SainCombatFlag _sainCombatFlag;
    private readonly NavMeshPath _path = new NavMeshPath();
    private const int MaxFailedPicks = 3;

    private Vector3 _target;
    private float _nextPick;
    private int _failedPicks;

    public NyanPatrolLogic(BotOwner botOwner)
        : base(botOwner)
    {
        _state = NyanDefendState.For(botOwner);
        _sainCombatFlag = new SainCombatFlag(botOwner);
    }

    public override void Start()
    {
        BotOwner.PatrollingData.Pause();
        _nextPick = 0f;
    }

    public override void Stop()
    {
        BotOwner.Sprint(false);
        BotOwner.PatrollingData.Unpause();
    }

    public override void Update(CustomLayer.ActionData data)
    {
        _sainCombatFlag.Clear();
        _state.RotateIfDue();

        BotOwner.SetPose(1f);
        BotOwner.SetTargetMoveSpeed(1f);
        BotOwner.Sprint(true);
        BotOwner.Steering.LookToMovingDirection();
        BotOwner.DoorOpener.UpdateDoorInteractionStatus();

        bool arrived = !BotOwner.HasPathAndNotComplete || Vector3.Distance(BotOwner.Position, _target) < ReachDistance;
        if (arrived && Time.time >= _nextPick || Time.time >= _nextPick + MaxLegSeconds)
        {
            Pick();
        }
    }

    private void Pick()
    {
        _nextPick = Time.time + RetryDelay;
        NyanPoi poi = _state.Current;

        // Two points inside the circle can still be joined by a route that goes round a wall far outside it.
        // While he is inside, only routes that stay inside are taken; when he is outside any route back will do.
        float limit = poi.Radius * NyanDefendLayer.ReturnMargin;
        // A circle split by walls may have no such route, so after a few empty rounds he takes any rather than stand still
        bool inside = Flat(BotOwner.Position - poi.Position) <= limit && _failedPicks < MaxFailedPicks;
        _failedPicks++;
        for (int i = 0; i < PickAttempts; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * poi.Radius;
            Vector3 candidate = poi.Position + new Vector3(offset.x, 0f, offset.y);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, SampleRange, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(BotOwner.Position, hit.position, NavMesh.AllAreas, _path)
                || _path.status != NavMeshPathStatus.PathComplete
                || inside && _path.corners.Any(corner => Flat(corner - poi.Position) > limit))
            {
                continue;
            }
            if (BotOwner.GoToPoint(hit.position, slowAtTheEnd: false, reachDist: ReachDistance) == NavMeshPathStatus.PathComplete)
            {
                _target = hit.position;
                _failedPicks = 0;
                return;
            }
        }
    }

    private static float Flat(Vector3 offset)
    {
        return new Vector2(offset.x, offset.z).magnitude;
    }
}
