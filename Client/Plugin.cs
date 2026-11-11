// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace NyanBoss.Client;

[BepInPlugin("com.pmot.nyanboss.client", "NyanBoss", "0.8.5")]
[BepInDependency("com.SPT.core", "4.1.0")]
[BepInDependency("xyz.drakia.bigbrain")]
public class Plugin : BaseUnityPlugin
{
    // Above every combat layer, including SAIN's, and below only SAIN's debug layer
    private const int DefendLayerPriority = 96;

    internal static ManualLogSource Log;
    internal static ConfigEntry<float> SpeedMultiplier;
    // Debug logging is switched off and hidden from F12. To bring an option back, uncomment its Config.Bind in Awake
    // and change its field here back to ConfigEntry<bool>
    internal static readonly DebugOff SpeedLogging = default;
    internal static ConfigEntry<float> MusicVolume;
    internal static ConfigEntry<float> MusicRange;
    internal static ConfigEntry<bool> MusicBypassMixer;
    internal static readonly DebugOff MusicLogging = default;
    internal static ConfigEntry<bool> TrailEnabled;
    internal static ConfigEntry<float> TrailLifetime;
    internal static ConfigEntry<bool> PoptartEnabled;
    internal static ConfigEntry<float> PoptartScale;
    internal static ConfigEntry<bool> PoptartHidesArmour;
    internal static ConfigEntry<float> PoptartDepth;
    internal static ConfigEntry<float> PoptartHeight;
    internal static ConfigEntry<float> PoptartForwardOffset;
    internal static ConfigEntry<float> PoptartUpOffset;
    internal static ConfigEntry<float> PoptartBrightness;
    internal static ConfigEntry<bool> PoptartLit;
    internal static ConfigEntry<bool> BulletTrailEnabled;
    internal static ConfigEntry<float> BulletTrailLength;
    internal static ConfigEntry<float> BulletTrailThickness;
    internal static ConfigEntry<float> BulletTrailLinger;
    internal static readonly DebugOff BulletTrailLogging = default;
    internal static ConfigEntry<float> PoiRotationSeconds;
    internal static ConfigEntry<float> ProvokedSeconds;
    internal static ConfigEntry<bool> HostileToAI;
    internal static readonly DebugOff HostilityLogging = default;
    internal static ConfigEntry<KeyboardShortcut> LogPositionKey = null;

    private void Awake()
    {
        Log = Logger;
        SpeedMultiplier = Config.Bind("Movement", "SpeedMultiplier", 1.75f,
            new ConfigDescription("How much faster the boss moves while running or sprinting. Applies immediately", new AcceptableValueRange<float>(1f, 6f)));
        MusicVolume = Config.Bind("Music", "Volume", 0.5f,
            new ConfigDescription("Volume of the boss's music. Applies immediately", new AcceptableValueRange<float>(0f, 1f)));
        MusicRange = Config.Bind("Music", "Range", 120f,
            new ConfigDescription("Distance in metres at which the music fades to silence. Applies immediately", new AcceptableValueRange<float>(20f, 500f)));
        MusicBypassMixer = Config.Bind("Music", "BypassGameMixer", false,
            "Play straight to the speakers instead of through the game's sound mixer. It then ignores the in-game volume setting. Applies immediately");
        // MusicLogging = Config.Bind("Debug", "MusicLogging", true,
        //     "Write the music's playback state to the BepInEx log every five seconds");
        TrailEnabled = Config.Bind("Trail", "Enabled", true, "Show the rainbow trail. Applies immediately");
        TrailLifetime = Config.Bind("Trail", "Lifetime", 5f,
            new ConfigDescription("Seconds each part of the trail lasts. Applies immediately", new AcceptableValueRange<float>(1f, 15f)));
        PoptartEnabled = Config.Bind("Pop-tart", "Enabled", true, "Wrap his torso in a frosted pastry. Applies immediately");
        PoptartScale = Config.Bind("Pop-tart", "Scale", 1f,
            new ConfigDescription("Size of the pastry. Applies immediately", new AcceptableValueRange<float>(0.5f, 2f)));
        PoptartDepth = Config.Bind("Pop-tart", "Depth", 0.3f,
            new ConfigDescription("Front-to-back thickness of the crust in metres, before Scale. Frosting and sprinkles add about 6.4cm on top. Applies immediately", new AcceptableValueRange<float>(0.1f, 0.6f)));
        PoptartHeight = Config.Bind("Pop-tart", "Height", 0.6f,
            new ConfigDescription("Height up his spine in metres, before Scale. Applies immediately", new AcceptableValueRange<float>(0.3f, 0.9f)));
        PoptartForwardOffset = Config.Bind("Pop-tart", "ForwardOffset", 0f,
            new ConfigDescription("Moves the pastry out of his chest (positive) or towards his back (negative), in metres. Applies immediately", new AcceptableValueRange<float>(-0.2f, 0.2f)));
        PoptartUpOffset = Config.Bind("Pop-tart", "UpOffset", 0f,
            new ConfigDescription("Moves the pastry up (positive) or down (negative) his spine, in metres. Applies immediately", new AcceptableValueRange<float>(-0.2f, 0.2f)));
        PoptartLit = Config.Bind("Pop-tart", "Lit", true,
            "Light the pastry with the scene, like his clothes. Off gives flat, full-bright colours that show even in the dark. Applies immediately");
        PoptartBrightness = Config.Bind("Pop-tart", "Brightness", 1f,
            new ConfigDescription("Multiplies the pastry's colours. Mostly for the unlit look, which ignores the scene's lighting. Applies immediately", new AcceptableValueRange<float>(0.1f, 1f)));
        PoptartHidesArmour = Config.Bind("Pop-tart", "HideArmour", true,
            "Hide his body armour and rig, which would poke through the pastry. Purely visual: he still wears them. Applies immediately");
        BulletTrailEnabled = Config.Bind("Nyan rounds", "Enabled", true,
            "Draw a rainbow trail behind every 5.56x45mm Nyan round in flight, whoever fires it. Applies immediately");
        BulletTrailLength = Config.Bind("Nyan rounds", "Length", 50f,
            new ConfigDescription("Length of the trail behind each bullet, in metres. Applies immediately", new AcceptableValueRange<float>(1f, 100f)));
        BulletTrailLinger = Config.Bind("Nyan rounds", "Linger", 0.4f,
            new ConfigDescription("Seconds the trail stays after its bullet hits something, shrinking into the impact point. Applies immediately", new AcceptableValueRange<float>(0.05f, 3f)));
        BulletTrailThickness = Config.Bind("Nyan rounds", "BandThickness", 0.0075f,
            new ConfigDescription("Thickness of each of the six colour bands, in metres. Applies immediately", new AcceptableValueRange<float>(0.004f, 0.05f)));
        // BulletTrailLogging = Config.Bind("Debug", "BulletTrailLogging", true,
        //     "Write to the BepInEx log the first time a Nyan round is seen in flight");
        PoiRotationSeconds = Config.Bind("Behaviour", "PoiRotationSeconds", 180f,
            new ConfigDescription("Seconds between moving to defend a different POI", new AcceptableValueRange<float>(30f, 1800f)));
        ProvokedSeconds = Config.Bind("Behaviour", "ProvokedSeconds", 20f,
            new ConfigDescription("Seconds he keeps fighting after being shot, wherever he is and whoever shot him, before going back to his POI", new AcceptableValueRange<float>(0f, 120f)));
        HostileToAI = Config.Bind("Behaviour", "HostileToAI", true,
            "He fights every other bot and they all fight him, scavs and other bosses included. Applies from the next raid");
        // HostilityLogging = Config.Bind("Debug", "HostilityLogging", true,
        //     "Write to the BepInEx log each time a bot group is made hostile to or from the boss");
        // SpeedLogging = Config.Bind("Debug", "SpeedLogging", true,
        //     "Write the boss's measured speed to the BepInEx log every two seconds");
        // LogPositionKey = Config.Bind("Debug", "LogPositionKey", new KeyboardShortcut(KeyCode.F9),
        //     "Writes your position to the BepInEx log, for placing POIs precisely");

        string folder = Path.GetDirectoryName(Info.Location);

        new BrainSwapPatch().Enable();
        new SpeedPatch().Enable();
        new TeleportLogPatch().Enable();
        new HostilityPatch().Enable();
        new SpawnHostilityPatch().Enable();
        new ShootPatch().Enable();
        BrainManager.AddCustomLayer(typeof(NyanDefendLayer), new List<string> { "Killa" }, DefendLayerPriority, new List<WildSpawnType> { WildSpawnType.bossTest });

        NyanMusic.FindFile(folder);
        NyanBulletTrails.Create(gameObject);
        if (BulletTrailLogging.Value)
        {
            Log.LogInfo("Bullet trails ready");
        }
    }

    private void Update()
    {
        if (LogPositionKey == null || !LogPositionKey.Value.IsDown() || !Singleton<GameWorld>.Instantiated)
        {
            return;
        }

        GameWorld world = Singleton<GameWorld>.Instance;
        if (world.MainPlayer != null)
        {
            Vector3 p = world.MainPlayer.Transform.position;
            Log.LogInfo($"POI on {world.LocationId}: {{ \"Name\": \"\", \"X\": {p.x:F0}, \"Y\": {p.y:F0}, \"Z\": {p.z:F0}, \"Radius\": 50 }}");
        }
    }
}

/// <summary>Stands in for a disabled debug option, which always reads as off.</summary>
internal readonly struct DebugOff
{
    public bool Value => false;
}
