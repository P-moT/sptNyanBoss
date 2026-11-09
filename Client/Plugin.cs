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

[BepInPlugin("com.pmot.nyanboss.client", "NyanBoss", "0.4.0")]
[BepInDependency("com.SPT.core", "4.1.0")]
[BepInDependency("xyz.drakia.bigbrain")]
public class Plugin : BaseUnityPlugin
{
    // Above every combat layer, including SAIN's, and below only SAIN's debug layer
    private const int DefendLayerPriority = 96;

    internal static ManualLogSource Log;
    internal static ConfigEntry<float> SpeedMultiplier;
    internal static ConfigEntry<bool> SpeedLogging;
    internal static ConfigEntry<float> MusicVolume;
    internal static ConfigEntry<float> MusicRange;
    internal static ConfigEntry<bool> MusicBypassMixer;
    internal static ConfigEntry<bool> MusicLogging;
    internal static ConfigEntry<bool> TrailEnabled;
    internal static ConfigEntry<float> TrailLifetime;
    internal static ConfigEntry<float> PoiRotationSeconds;
    internal static ConfigEntry<float> ProvokedSeconds;
    internal static ConfigEntry<KeyboardShortcut> LogPositionKey;

    private void Awake()
    {
        Log = Logger;
        SpeedMultiplier = Config.Bind("Movement", "SpeedMultiplier", 2.5f,
            new ConfigDescription("How much faster the boss moves while running or sprinting. Applies immediately", new AcceptableValueRange<float>(1f, 6f)));
        MusicVolume = Config.Bind("Music", "Volume", 0.5f,
            new ConfigDescription("Volume of the boss's music. Applies immediately", new AcceptableValueRange<float>(0f, 1f)));
        MusicRange = Config.Bind("Music", "Range", 120f,
            new ConfigDescription("Distance in metres at which the music fades to silence. Applies immediately", new AcceptableValueRange<float>(20f, 500f)));
        MusicBypassMixer = Config.Bind("Music", "BypassGameMixer", false,
            "Play straight to the speakers instead of through the game's sound mixer. It then ignores the in-game volume setting. Applies immediately");
        MusicLogging = Config.Bind("Debug", "MusicLogging", true,
            "Write the music's playback state to the BepInEx log every five seconds");
        TrailEnabled = Config.Bind("Trail", "Enabled", true, "Show the rainbow trail. Applies immediately");
        TrailLifetime = Config.Bind("Trail", "Lifetime", 5f,
            new ConfigDescription("Seconds each part of the trail lasts. Applies immediately", new AcceptableValueRange<float>(1f, 15f)));
        PoiRotationSeconds = Config.Bind("Behaviour", "PoiRotationSeconds", 180f,
            new ConfigDescription("Seconds between moving to defend a different POI", new AcceptableValueRange<float>(30f, 1800f)));
        ProvokedSeconds = Config.Bind("Behaviour", "ProvokedSeconds", 20f,
            new ConfigDescription("Seconds he keeps fighting after being shot, even against someone outside his area", new AcceptableValueRange<float>(0f, 120f)));
        SpeedLogging = Config.Bind("Debug", "SpeedLogging", true,
            "Write the boss's measured speed to the BepInEx log every two seconds");
        LogPositionKey = Config.Bind("Debug", "LogPositionKey", new KeyboardShortcut(KeyCode.F9),
            "Writes your position to the BepInEx log in pois.json format, for adding POIs");

        string folder = Path.GetDirectoryName(Info.Location);
        NyanPois.Load(folder);

        new BrainSwapPatch().Enable();
        new SpeedPatch().Enable();
        new TeleportLogPatch().Enable();
        BrainManager.AddCustomLayer(typeof(NyanDefendLayer), new List<string> { "Killa" }, DefendLayerPriority, new List<WildSpawnType> { WildSpawnType.bossTest });

        NyanMusic.FindFile(folder);
    }

    private void Update()
    {
        if (!LogPositionKey.Value.IsDown() || !Singleton<GameWorld>.Instantiated)
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
