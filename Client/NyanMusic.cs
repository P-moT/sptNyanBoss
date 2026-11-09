// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections;
using System.IO;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.Networking;

namespace NyanBoss.Client;

// The song isn't shipped with the mod, so it is loaded from a file the user places next to the plugin.
internal static class NyanMusic
{
    private const string FileName = "nyan";

    private static readonly (string Extension, AudioType Type)[] Formats =
    {
        (".ogg", AudioType.OGGVORBIS),
        (".wav", AudioType.WAV),
        (".mp3", AudioType.MPEG),
    };

    private static string _path;
    private static AudioType _type;
    private static string _folder;

    internal static void FindFile(string folder)
    {
        _folder = folder;
        foreach ((string extension, AudioType type) in Formats)
        {
            string path = Path.Combine(folder, FileName + extension);
            if (File.Exists(path))
            {
                _path = path;
                _type = type;
                Plugin.Log.LogInfo($"Found music file {path}");
                return;
            }
        }
        Plugin.Log.LogWarning($"No music file found. Place nyan.ogg, nyan.wav or nyan.mp3 in {folder}");
    }

    internal static void Attach(Player player)
    {
        if (_path == null)
        {
            Plugin.Log.LogWarning($"Boss spawned without music. Place nyan.ogg, nyan.wav or nyan.mp3 in {_folder}");
            return;
        }

        var host = new GameObject("NyanBossMusic");
        host.transform.SetParent(player.Transform.Original, false);
        host.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        host.AddComponent<NyanMusicEmitter>().Begin(_path, _type);
        player.OnPlayerDeadOrUnspawn += _ => UnityEngine.Object.Destroy(host);
    }
}

internal class NyanMusicEmitter : MonoBehaviour
{
    private const float LogInterval = 5f;
    private const float VolumeScale = 0.5f; // Full volume is far too loud in raid, so the slider's whole range covers the usable half

    private static readonly float[] LogSamples = new float[1024];

    private AudioSource _source;
    private AudioClip _clip;
    private AudioMixerGroupHolder _mixer;
    private float _nextLog;

    internal void Begin(string path, AudioType type)
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.loop = true;
        _source.playOnAwake = false;
        _source.spatialBlend = 1f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = 5f;
        _source.dopplerLevel = 0f;
        _source.priority = 0;
        _mixer = new AudioMixerGroupHolder();
        StartCoroutine(Load(path, type));
    }

    // Loaded here rather than at game start: the game resets Unity's audio system while a raid loads, and a clip loaded
    // from a file before that can end up silent
    private IEnumerator Load(string path, AudioType type)
    {
        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
        ((DownloadHandlerAudioClip)request.downloadHandler).compressed = true; // Keeps a long song small in memory
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Log.LogError($"Could not load {path}: {request.error}");
            yield break;
        }

        _clip = DownloadHandlerAudioClip.GetContent(request);
        _clip.name = "nyan";
        _source.clip = _clip;
        Plugin.Log.LogInfo($"Music loaded for this raid: {path} ({_clip.length:F1}s, {_clip.channels} channel(s), {_clip.frequency} Hz)");
    }

    private void Update()
    {
        if (_clip == null)
        {
            return;
        }

        _source.volume = Plugin.MusicVolume.Value * VolumeScale;
        _source.maxDistance = Plugin.MusicRange.Value;
        _source.outputAudioMixerGroup = Plugin.MusicBypassMixer.Value ? null : _mixer.Group;
        if (!_source.isPlaying)
        {
            _source.Play();
        }

        if (Plugin.MusicLogging.Value && Time.time >= _nextLog)
        {
            _nextLog = Time.time + LogInterval;
            LogState();
        }
    }

    private void LogState()
    {
        _source.GetOutputData(LogSamples, 0);
        float sum = 0f;
        foreach (float sample in LogSamples)
        {
            sum += sample * sample;
        }
        float level = Mathf.Sqrt(sum / LogSamples.Length);

        string distance = "unknown";
        if (Singleton<AudioListenerConsistencyManager>.Instantiated)
        {
            distance = $"{Vector3.Distance(transform.position, Singleton<AudioListenerConsistencyManager>.Instance.transform.position):F0}m";
        }

        string group = _source.outputAudioMixerGroup != null ? _source.outputAudioMixerGroup.name : "none";
        Plugin.Log.LogInfo($"Music: playing {_source.isPlaying}, time {_source.time:F1}s, data {_clip.loadState}, level {level:F4}, listener {distance}, range {_source.maxDistance:F0}m, volume {_source.volume:F2}, mixer {group}, paused {AudioListener.pause}");
    }

    private void OnDestroy()
    {
        if (_clip != null)
        {
            Destroy(_clip);
        }
    }

    // The game's mixer may not exist yet when he spawns, so it is looked up until found
    private class AudioMixerGroupHolder
    {
        private UnityEngine.Audio.AudioMixerGroup _group;

        internal UnityEngine.Audio.AudioMixerGroup Group
        {
            get
            {
                if (_group == null && Singleton<BetterAudio>.Instantiated)
                {
                    _group = Singleton<BetterAudio>.Instance.VeryStandartMixerGroup;
                }
                return _group;
            }
        }
    }
}
