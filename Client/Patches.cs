// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace NyanBoss.Client;

internal static class NyanRole
{
    private const WildSpawnType Role = WildSpawnType.bossTest;

    internal static bool Is(Player player)
    {
        return player != null && player.IsAI && player.Profile?.Info?.Settings?.Role == Role;
    }

    internal static bool Is(BotOwner bot)
    {
        return bot != null && bot.Profile?.Info?.Settings?.Role == Role;
    }
}

// The vanilla bossTest brain is a stub that always suppresses or holds position, so it never moves.
// Swap the one constructor call for Killa's brain, leaving the rest of Activate (and BigBrain's postfix on it) untouched.
public class BrainSwapPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(StandartBotBrain), nameof(StandartBotBrain.Activate));
    }

    [PatchTranspiler]
    public static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        ConstructorInfo stub = AccessTools.Constructor(typeof(BossTestLayersStrategy), new[] { typeof(BotOwner) });
        ConstructorInfo replacement = AccessTools.Constructor(typeof(KillaLayersStrategy), new[] { typeof(BotOwner) });
        bool swapped = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Newobj && Equals(instruction.operand, stub))
            {
                instruction.operand = replacement;
                swapped = true;
            }
            yield return instruction;
        }
        if (!swapped)
        {
            Plugin.Log.LogError("Could not find the bossTest brain to replace - the boss will stand still");
        }
    }

    [PatchPostfix]
    public static void Postfix(StandartBotBrain __instance)
    {
        BotOwner bot = __instance._owner;
        if (NyanRole.Is(bot))
        {
            Plugin.Log.LogInfo($"Boss activated: {bot.Profile.Nickname}, brain {__instance.BaseBrain?.ShortName()}, position {bot.Position}");
            // An exception here would break the bot's brain, and music or a trail isn't worth that
            try
            {
                NyanMusic.Attach(bot.GetPlayer);
                NyanTrail.Attach(bot.GetPlayer);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Could not attach music or trail: {ex}");
            }
        }
    }
}

// Every movement state ends in DirectApplyMotion, so scaling here sits below the AI and works whichever mod is steering.
public class SpeedPatch : ModulePatch
{
    private const float LogInterval = 2f;

    private sealed class Sample
    {
        public Vector3 LastPosition;
        public float Time;
        public float BaseDistance;
        public bool Started;
    }

    private static readonly AccessTools.FieldRef<MovementContext, Player> PlayerRef =
        AccessTools.FieldRefAccess<MovementContext, Player>("_player");
    private static readonly ConditionalWeakTable<MovementContext, Sample> Samples = new ConditionalWeakTable<MovementContext, Sample>();

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion));
    }

    [PatchPrefix]
    public static void Prefix(MovementContext __instance, ref Vector3 motion, float deltaTime)
    {
        Player player = PlayerRef(__instance);
        if (!NyanRole.Is(player))
        {
            return;
        }

        if (Plugin.SpeedLogging.Value)
        {
            Record(__instance, player, motion, deltaTime);
        }

        // Grounded movement only, so jumps, vaults and ladders keep their normal arcs
        EPlayerState state = __instance.CurrentState.Name;
        if (state == EPlayerState.Run || state == EPlayerState.Sprint)
        {
            motion *= Plugin.SpeedMultiplier.Value;
        }
    }

    private static void Record(MovementContext context, Player player, Vector3 motion, float deltaTime)
    {
        Sample sample = Samples.GetOrCreateValue(context);
        Vector3 position = context.TransformPosition;
        if (!sample.Started)
        {
            sample.Started = true;
            sample.LastPosition = position;
            return;
        }

        sample.Time += deltaTime;
        sample.BaseDistance += new Vector2(motion.x, motion.z).magnitude;
        if (sample.Time < LogInterval)
        {
            return;
        }

        float actual = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(sample.LastPosition.x, sample.LastPosition.z)) / sample.Time;
        float baseline = sample.BaseDistance / sample.Time;
        BotOwner bot = player.AIData?.BotOwner;
        NyanDefendState defend = bot != null ? NyanDefendState.For(bot) : null;
        string area = defend != null && defend.HasPois
            ? $", {defend.Current.Name} {Vector3.Distance(position, defend.Current.Position):F0}m away"
            : string.Empty;
        string enemy = bot?.Memory?.GoalEnemy is EnemyInfo goal
            ? $"{goal.Person.Profile?.Nickname} {Vector3.Distance(position, goal.CurrPosition):F0}m away"
            : "none";
        Plugin.Log.LogInfo($"Speed: requested {baseline:F1} m/s, travelled {actual:F1} m/s, state {context.CurrentState.Name}, layer {(bot != null ? BrainManager.GetActiveLayerName(bot) : "unknown")}{area}, enemy {enemy}, path {bot?.Mover?.HasPathAndNoComplete}, sprint order {bot?.Mover?.Sprinting}, can sprint {player.Physical?.CanSprint}");
        sample.Time = 0f;
        sample.BaseDistance = 0f;
        sample.LastPosition = position;
    }
}

// Overshooting a corner at high speed can knock a bot off the navmesh, and the game corrects that with a teleport.
// These log lines are how we tell whether the current multiplier is too high.
public class TeleportLogPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BotMover), nameof(BotMover.Teleport));
    }

    [PatchPostfix]
    public static void Postfix(BotOwner ____owner, Vector3 rPosition)
    {
        if (Plugin.SpeedLogging.Value && NyanRole.Is(____owner))
        {
            Plugin.Log.LogWarning($"Teleported back onto the navmesh at {rPosition}");
        }
    }
}
