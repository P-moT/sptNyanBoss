// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
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

internal static class NyanPois
{
    private static Dictionary<string, List<NyanPoi>> _byMap = new Dictionary<string, List<NyanPoi>>(StringComparer.OrdinalIgnoreCase);

    internal static void Load(string folder)
    {
        string path = Path.Combine(folder, "pois.json");
        if (!File.Exists(path))
        {
            Plugin.Log.LogWarning($"{path} not found, the boss will use his default behaviour");
            return;
        }

        try
        {
            var loaded = JsonConvert.DeserializeObject<Dictionary<string, List<NyanPoi>>>(File.ReadAllText(path));
            _byMap = new Dictionary<string, List<NyanPoi>>(loaded, StringComparer.OrdinalIgnoreCase);
            Plugin.Log.LogInfo($"Loaded POIs for {_byMap.Count} map(s)");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Could not read {path}, the boss will use his default behaviour: {ex.Message}");
        }
    }

    internal static List<NyanPoi> ForCurrentMap()
    {
        string map = Singleton<GameWorld>.Instance?.LocationId;
        return map != null && _byMap.TryGetValue(map, out List<NyanPoi> pois) ? pois : null;
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

    private readonly List<NyanPoi> _pois;
    private int _current;
    private float _nextRotation;

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
    private const float ReturnMargin = 1.25f; // Stops him flicking between fighting and returning right on the boundary
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
    private const int PickAttempts = 8;
    private const float SampleRange = 6f;
    private const float ReachDistance = 3f;
    private const float RetryDelay = 0.5f;
    private const float MaxLegSeconds = 15f; // Picks somewhere new if he gets stuck on the way

    private readonly NyanDefendState _state;
    private readonly SainCombatFlag _sainCombatFlag;
    private Vector3 _target;
    private float _nextPick;

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
        for (int i = 0; i < PickAttempts; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * poi.Radius;
            Vector3 candidate = poi.Position + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, SampleRange, NavMesh.AllAreas)
                && BotOwner.GoToPoint(hit.position, slowAtTheEnd: false, reachDist: ReachDistance) == NavMeshPathStatus.PathComplete)
            {
                _target = hit.position;
                return;
            }
        }
    }
}
