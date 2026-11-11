// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using UnityEngine;

namespace NyanBoss.Client;

// A short rainbow tail behind every Nyan round in flight, whoever fires it. The game moves bullets in its ballistics
// calculator rather than as objects, so this reads the calculator's active shots each frame and draws the last few metres
// of each Nyan round's path. One mesh holds every tail and is rebuilt each frame, which stays cheap because a tail only
// spans a couple of frames of travel.
internal class NyanBulletTrails : MonoBehaviour
{
    internal const string RoundTpl = "6a5c9d1e7f3b2a4c8d0e1f22";

    private const int MaxPointsPerTrail = 64;

    private static readonly Color32[] Bands =
    {
        new Color32(255, 0, 0, 255),
        new Color32(255, 150, 0, 255),
        new Color32(255, 240, 0, 255),
        new Color32(50, 255, 0, 255),
        new Color32(0, 150, 255, 255),
        new Color32(110, 50, 255, 255),
    };

    // Corner offsets as (right, up), the first four at the segment's start and the last four at its end
    private static readonly Vector2[] Corners =
    {
        new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1),
    };

    // Each band is a long box with only its four sides; the ends are too small to see
    private static readonly int[] SideIndices =
    {
        0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5,
        2, 3, 7, 2, 7, 6,
        3, 0, 4, 3, 4, 7,
    };

    private class Trail
    {
        internal readonly List<Vector3> Points = new();
        internal readonly List<float> Distances = new(); // Path length from the muzzle to each point
        internal float LastTimeSinceShot;
        internal float EndedAt = -1f;
    }

    private static NyanBulletTrails _instance;

    private readonly Dictionary<Shot, Trail> _active = new();
    private readonly List<Trail> _ending = new();
    private readonly List<Shot> _gone = new();
    private readonly List<Vector3> _vertices = new();
    private readonly List<Color32> _colors = new();
    private readonly List<int> _indices = new();
    private GameWorld _world;
    private Mesh _mesh;
    private bool _loggedFirst;

    // Lives on the plugin's own object, the one thing the game never cleans up. An object of our own made while plugins
    // load is destroyed before the first raid.
    internal static void Create(GameObject pluginObject)
    {
        pluginObject.AddComponent<NyanBulletTrails>();
    }

    private GameObject _renderer;

    private bool _quitting;

    private void OnApplicationQuit()
    {
        _quitting = true;
    }

    private void Awake()
    {
        _instance = this;
        _mesh = new Mesh { name = "NyanBossBulletTrails", hideFlags = HideFlags.DontSave };
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.MarkDynamic();
    }

    // The visible part is an ordinary object in the raid's scene, made again whenever a scene change has taken it away
    private void EnsureRenderer()
    {
        if (_renderer != null)
        {
            return;
        }
        Material material = NyanTrail.GetMaterial();
        if (material == null)
        {
            return;
        }
        _renderer = new GameObject("NyanBossBulletTrails");
        _renderer.AddComponent<MeshFilter>().sharedMesh = _mesh;
        MeshRenderer meshRenderer = _renderer.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // After the game has moved its bullets for the frame
    private void LateUpdate()
    {
        GameWorld world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (world != _world)
        {
            _active.Clear();
            _ending.Clear();
            _world = world;
            if (world != null && Plugin.BulletTrailLogging.Value)
            {
                Plugin.Log.LogInfo($"Bullet trails watching {world.LocationId}, ballistics {(world.SharedBallisticsCalculator != null ? "found" : "missing")}");
            }
        }

        IBallisticsCalculator calculator = world != null ? world.SharedBallisticsCalculator : null;
        if (calculator != null && Plugin.BulletTrailEnabled.Value)
        {
            EnsureRenderer();
            Track(calculator);
        }
        Retire();
        Draw();
    }

    internal static bool IsNyanRound(Shot shot)
    {
        return shot?.Ammo != null && shot.Ammo.TemplateId.ToString() == RoundTpl;
    }

    // Called by ShootPatch the moment a Nyan round is fired
    internal static void Register(Shot shot)
    {
        if (_instance == null || !Plugin.BulletTrailEnabled.Value)
        {
            return;
        }
        if (!_instance._loggedFirst && Plugin.BulletTrailLogging.Value)
        {
            Plugin.Log.LogInfo($"Nyan round fired from {shot.StartPosition}");
            _instance._loggedFirst = true;
        }
        _instance.Begin(shot);
    }

    private void Begin(Shot shot)
    {
        if (_active.TryGetValue(shot, out Trail old))
        {
            End(old);
        }
        var trail = new Trail { LastTimeSinceShot = shot.TimeSinceShot };
        // Start at the muzzle, or where a fragment or ricochet broke off
        AddPoint(trail, shot.StartPosition);
        _active[shot] = trail;
    }

    private void Track(IBallisticsCalculator calculator)
    {
        // Fragments and ricochets are added to the calculator directly rather than fired, so they are picked up here
        for (int i = 0; i < calculator.ActiveShotsCount; i++)
        {
            Shot shot = calculator.GetActiveShot(i);
            if (!shot.IsShotFinished && !_active.ContainsKey(shot) && IsNyanRound(shot))
            {
                Begin(shot);
            }
        }

        foreach (KeyValuePair<Shot, Trail> pair in _active)
        {
            Shot shot = pair.Key;
            Trail trail = pair.Value;
            // Shots are pooled: one that has gone back in time, or now holds other ammo, is a different bullet
            if (!IsNyanRound(shot) || shot.TimeSinceShot < trail.LastTimeSinceShot)
            {
                _gone.Add(shot);
                continue;
            }
            AddPoint(trail, shot.CurrentPosition);
            trail.LastTimeSinceShot = shot.TimeSinceShot;
            if (shot.IsShotFinished)
            {
                _gone.Add(shot);
            }
        }
        foreach (Shot shot in _gone)
        {
            End(_active[shot]);
            _active.Remove(shot);
        }
        _gone.Clear();
    }

    private static void AddPoint(Trail trail, Vector3 point)
    {
        int last = trail.Points.Count - 1;
        if (last >= 0 && (point - trail.Points[last]).sqrMagnitude < 0.0001f)
        {
            return;
        }
        float distance = last >= 0 ? trail.Distances[last] + Vector3.Distance(trail.Points[last], point) : 0f;
        trail.Points.Add(point);
        trail.Distances.Add(distance);
        if (trail.Points.Count > MaxPointsPerTrail)
        {
            trail.Points.RemoveAt(0);
            trail.Distances.RemoveAt(0);
        }
    }

    private void End(Trail trail)
    {
        trail.EndedAt = Time.time;
        _ending.Add(trail);
    }

    // A bullet crosses a typical fight in a frame or two, so a stopped bullet's tail stays a moment longer, shrinking into the
    // point it stopped at. Otherwise a close shot would show for a single frame.
    private void Retire()
    {
        float linger = Mathf.Max(Plugin.BulletTrailLinger.Value, 0.01f);
        _ending.RemoveAll(trail =>
        {
            float elapsed = Time.time - trail.EndedAt;
            return trail.Points.Count < 2 || elapsed >= linger;
        });
    }

    private void Draw()
    {
        _vertices.Clear();
        _colors.Clear();
        _indices.Clear();

        if (Plugin.BulletTrailEnabled.Value)
        {
            float length = Plugin.BulletTrailLength.Value;
            float band = Plugin.BulletTrailThickness.Value;
            float linger = Mathf.Max(Plugin.BulletTrailLinger.Value, 0.01f);
            foreach (Trail trail in _active.Values)
            {
                AddTrail(trail, length, band, 0f);
            }
            foreach (Trail trail in _ending)
            {
                AddTrail(trail, length, band, length * Mathf.Clamp01((Time.time - trail.EndedAt) / linger));
            }
        }

        _mesh.Clear();
        if (_vertices.Count == 0)
        {
            return;
        }
        _mesh.SetVertices(_vertices);
        _mesh.SetColors(_colors);
        _mesh.SetTriangles(_indices, 0);
        _mesh.RecalculateBounds();
    }

    // Draws the part of the path between (head - length + overrun) and the head, where overrun is how far the tail has
    // shrunk since the bullet stopped
    private void AddTrail(Trail trail, float length, float band, float overrun)
    {
        int count = trail.Points.Count;
        if (count < 2)
        {
            return;
        }
        float head = trail.Distances[count - 1];
        float tail = head - length + overrun;
        if (tail >= head)
        {
            return;
        }

        for (int i = count - 1; i > 0; i--)
        {
            float startDistance = trail.Distances[i - 1];
            float endDistance = trail.Distances[i];
            if (endDistance <= tail)
            {
                break;
            }
            Vector3 start = trail.Points[i - 1];
            Vector3 end = trail.Points[i];
            if (startDistance < tail)
            {
                start = Vector3.Lerp(start, end, (tail - startDistance) / (endDistance - startDistance));
            }
            AddSegment(start, end, band);
        }
    }

    private void AddSegment(Vector3 start, Vector3 end, float band)
    {
        Vector3 forward = end - start;
        if (forward.sqrMagnitude < 0.000001f)
        {
            return;
        }
        forward.Normalize();
        // The bands stack upwards, square to the bullet's path, like the boss's own trail
        Vector3 up = Vector3.up - forward * Vector3.Dot(Vector3.up, forward);
        up = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(up, forward);

        float half = band * 0.5f;
        for (int b = 0; b < Bands.Length; b++)
        {
            Vector3 offset = up * (band * (b - (Bands.Length - 1) * 0.5f));
            int first = _vertices.Count;
            for (int end2 = 0; end2 < 2; end2++)
            {
                Vector3 origin = (end2 == 0 ? start : end) + offset;
                foreach (Vector2 corner in Corners)
                {
                    _vertices.Add(origin + right * (corner.x * half) + up * (corner.y * half));
                    _colors.Add(Bands[b]);
                }
            }
            foreach (int index in SideIndices)
            {
                _indices.Add(first + index);
            }
        }
    }

    private void OnDestroy()
    {
        Destroy(_mesh);
        if (_renderer != null)
        {
            Destroy(_renderer);
        }
        // Normal when the game closes; only worth a warning if it happens while playing
        if (Plugin.BulletTrailLogging.Value && !_quitting)
        {
            Plugin.Log.LogWarning("Bullet trails were destroyed and will not show again until the game restarts");
        }
    }
}

// Every bullet (but not rockets or grenades) is fired through this one method
public class ShootPatch : SPT.Reflection.Patching.ModulePatch
{
    protected override System.Reflection.MethodBase GetTargetMethod()
    {
        return HarmonyLib.AccessTools.Method(typeof(BallisticsCalculator), nameof(BallisticsCalculator.Shoot), new[] { typeof(Shot) });
    }

    [SPT.Reflection.Patching.PatchPostfix]
    public static void Postfix(Shot shot)
    {
        try
        {
            if (NyanBulletTrails.IsNyanRound(shot))
            {
                NyanBulletTrails.Register(shot);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogError($"Could not start a bullet trail: {ex}");
        }
    }
}
