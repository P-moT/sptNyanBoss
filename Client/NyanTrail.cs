// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace NyanBoss.Client;

// A rainbow of solid blocks laid along the boss's path. Blocks rather than a flat ribbon so it reads from every angle,
// including straight behind him. The mesh is a fixed ring of segments: colours and triangles are written once, and only
// the newest and expiring segments change, so a long trail stays cheap.
internal class NyanTrail : MonoBehaviour
{
    private const float SegmentLength = 0.5f;
    private const float BandHeight = 0.11f;
    private const float BandWidth = 0.22f;
    private const float BaseHeight = 0.55f;
    private const float WaveOffset = BandHeight * 0.5f;
    private const int WaveLength = 2; // Segments before the rainbow steps up or down, for the classic staircase edge
    private const float TeleportDistance = 10f;
    private const int MaxSegments = 120;
    private const int MaxSegmentsPerFrame = 8;
    private const int VerticesPerBox = 24;

    private static readonly Color32[] Bands =
    {
        new Color32(110, 50, 255, 255),
        new Color32(0, 150, 255, 255),
        new Color32(50, 255, 0, 255),
        new Color32(255, 240, 0, 255),
        new Color32(255, 150, 0, 255),
        new Color32(255, 0, 0, 255),
    };

    // Corners of a box as (forward, right, up), four per face
    private static readonly Vector3[] BoxCorners =
    {
        new Vector3(0, -1, 0), new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(0, -1, 1),
        new Vector3(1, 1, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1),
        new Vector3(0, 1, 0), new Vector3(0, -1, 0), new Vector3(0, -1, 1), new Vector3(0, 1, 1),
        new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, -1, 1),
        new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
        new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0), new Vector3(0, -1, 0),
    };

    private static Material _material;

    private Player _owner;
    private Mesh _mesh;
    private Vector3[] _vertices;
    private float[] _bornAt;
    private Vector3[] _centres;
    private int _next;
    private int _oldest;
    private int _live;
    private int _emitted;
    private Vector3 _lastPoint;
    private bool _hasLastPoint;
    private bool _dirty;

    internal static void Attach(Player player)
    {
        Material material = GetMaterial();
        if (material == null)
        {
            return;
        }

        var host = new GameObject("NyanBossTrail");
        NyanTrail trail = host.AddComponent<NyanTrail>();
        trail.Build(player, material);
        player.OnPlayerDeadOrUnspawn += _ => trail._owner = null;
    }

    private static Material GetMaterial()
    {
        if (_material != null)
        {
            return _material;
        }

        // The game uses this shader itself, so it is always in the build and no asset bundle is needed
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            Plugin.Log.LogError("Trail shader not found, the boss will have no trail");
            return null;
        }

        _material = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = 2000,
            color = Color.white,
        };
        _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        _material.SetInt("_ZWrite", 1);
        _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        return _material;
    }

    private void Build(Player player, Material material)
    {
        _owner = player;
        int boxes = MaxSegments * Bands.Length;
        _vertices = new Vector3[boxes * VerticesPerBox];
        _bornAt = new float[MaxSegments];
        _centres = new Vector3[MaxSegments];

        var colors = new Color32[_vertices.Length];
        var triangles = new int[boxes * 36];
        for (int box = 0; box < boxes; box++)
        {
            int firstVertex = box * VerticesPerBox;
            Color32 color = Bands[box % Bands.Length];
            for (int v = 0; v < VerticesPerBox; v++)
            {
                colors[firstVertex + v] = color;
            }
            for (int face = 0; face < 6; face++)
            {
                int corner = firstVertex + face * 4;
                int index = box * 36 + face * 6;
                triangles[index] = corner;
                triangles[index + 1] = corner + 1;
                triangles[index + 2] = corner + 2;
                triangles[index + 3] = corner;
                triangles[index + 4] = corner + 2;
                triangles[index + 5] = corner + 3;
            }
        }

        _mesh = new Mesh { name = "NyanBossTrail" };
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.MarkDynamic();
        _mesh.vertices = _vertices;
        _mesh.colors32 = colors;
        _mesh.triangles = triangles;

        gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
        MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void Update()
    {
        float lifetime = Plugin.TrailLifetime.Value;
        if (_owner != null && Plugin.TrailEnabled.Value)
        {
            Emit(lifetime);
        }
        else
        {
            _hasLastPoint = false;
        }

        // Oldest blocks vanish one at a time, which suits the pixel look better than a smooth fade
        while (_live > 0 && Time.time - _bornAt[_oldest] > lifetime)
        {
            Collapse(_oldest);
            _oldest = (_oldest + 1) % MaxSegments;
            _live--;
            _dirty = true;
        }

        if (_dirty)
        {
            _mesh.vertices = _vertices;
            UpdateBounds();
            _dirty = false;
        }

        if (_owner == null && _live == 0)
        {
            Destroy(_mesh);
            Destroy(gameObject);
        }
    }

    private void Emit(float lifetime)
    {
        Vector3 position = _owner.Transform.position;
        if (!_hasLastPoint || Vector3.Distance(position, _lastPoint) > TeleportDistance)
        {
            _lastPoint = position;
            _hasLastPoint = true;
            return;
        }

        for (int i = 0; i < MaxSegmentsPerFrame; i++)
        {
            Vector3 toOwner = position - _lastPoint;
            float distance = toOwner.magnitude;
            if (distance < SegmentLength)
            {
                return;
            }
            Vector3 next = _lastPoint + toOwner / distance * SegmentLength;
            AddSegment(_lastPoint, next);
            _lastPoint = next;
        }
    }

    private void AddSegment(Vector3 from, Vector3 to)
    {
        if (_live == MaxSegments)
        {
            _oldest = (_oldest + 1) % MaxSegments;
            _live--;
        }

        Vector3 forward = to - from;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        right = right.sqrMagnitude > 0.0001f ? right.normalized * (BandWidth * 0.5f) : Vector3.right * (BandWidth * 0.5f);
        float wave = _emitted / WaveLength % 2 == 0 ? WaveOffset : -WaveOffset;
        Vector3 bandUp = Vector3.up * BandHeight;

        for (int band = 0; band < Bands.Length; band++)
        {
            Vector3 origin = from + Vector3.up * (BaseHeight + wave + BandHeight * band);
            int firstVertex = (_next * Bands.Length + band) * VerticesPerBox;
            for (int v = 0; v < VerticesPerBox; v++)
            {
                Vector3 corner = BoxCorners[v];
                _vertices[firstVertex + v] = origin + forward * corner.x + right * corner.y + bandUp * corner.z;
            }
        }

        _centres[_next] = from;
        _bornAt[_next] = Time.time;
        _next = (_next + 1) % MaxSegments;
        _live++;
        _emitted++;
        _dirty = true;
    }

    private void Collapse(int segment)
    {
        int first = segment * Bands.Length * VerticesPerBox;
        int count = Bands.Length * VerticesPerBox;
        Vector3 point = _centres[segment];
        for (int v = first; v < first + count; v++)
        {
            _vertices[v] = point;
        }
    }

    private void UpdateBounds()
    {
        if (_live == 0)
        {
            return;
        }
        var bounds = new Bounds(_centres[_oldest], Vector3.one);
        for (int i = 0; i < _live; i++)
        {
            bounds.Encapsulate(_centres[(_oldest + i) % MaxSegments]);
        }
        bounds.Expand(new Vector3(2f, 4f, 2f));
        _mesh.bounds = bounds;
    }
}
