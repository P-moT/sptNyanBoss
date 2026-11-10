// NyanBoss - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace NyanBoss.Client;

// A frosted pastry built from blocks, worn around the torso with frosting and sprinkles on both faces. It encloses his
// chest rather than replacing it, so his own arms, head and hit zones stay exactly where they were. Placed from bone
// positions each frame, not parented to a bone, so it stays upright whatever the bones' local axes are and in every pose.
internal class NyanPoptart : MonoBehaviour
{
    // Sizes in metres, before the F12 scale. x is across the shoulders, y up the spine, z out of the chest.
    // Height and crust depth come from F12 so they can be tuned in raid.
    private const float Width = 0.5f;
    private const float Pixel = 0.035f; // One "pixel" of the pixel-art look: the corner steps, border and sprinkle size
    private const float FrostingThickness = 0.02f;
    private const float SprinkleThickness = 0.012f;
    private const float GearCheckInterval = 0.5f;

    private static readonly Color32 Crust = new Color32(250, 204, 145, 255);
    private static readonly Color32 Frosting = new Color32(255, 153, 255, 255);
    private static readonly Color32 Sprinkle = new Color32(255, 51, 153, 255);

    // Sprinkle positions as fractions of the frosting, front face. The back face mirrors them.
    private static readonly Vector2[] Sprinkles =
    {
        new Vector2(0.18f, 0.84f), new Vector2(0.52f, 0.9f), new Vector2(0.8f, 0.76f),
        new Vector2(0.34f, 0.64f), new Vector2(0.68f, 0.56f), new Vector2(0.14f, 0.44f),
        new Vector2(0.46f, 0.36f), new Vector2(0.86f, 0.3f), new Vector2(0.26f, 0.16f),
        new Vector2(0.62f, 0.12f),
    };

    // Worn on the torso, so they would poke through the pastry. Hidden only: he still wears them and they can be looted.
    private static readonly EquipmentSlot[] HiddenSlots = { EquipmentSlot.ArmorVest, EquipmentSlot.TacticalVest };

    private Player _owner;
    private MeshFilter _filter;
    private float _builtHeight;
    private float _builtDepth;
    private float _nextGearCheck;
    private bool _gearHidden;

    internal static void Attach(Player player)
    {
        Material material = NyanTrail.GetMaterial();
        if (material == null)
        {
            return;
        }

        var host = new GameObject("NyanBossPoptart");
        NyanPoptart poptart = host.AddComponent<NyanPoptart>();
        poptart._owner = player;
        poptart._filter = host.AddComponent<MeshFilter>();
        poptart.RebuildIfChanged();
        MeshRenderer meshRenderer = host.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void LateUpdate()
    {
        // Stays on his body after death like the rest of his outfit, and goes when the body is cleaned up or the raid ends
        if (_owner == null || _owner.PlayerBones == null)
        {
            Destroy(_filter.sharedMesh);
            Destroy(gameObject);
            return;
        }

        bool enabled = Plugin.PoptartEnabled.Value;
        GetComponent<MeshRenderer>().enabled = enabled;
        UpdateGear(enabled && Plugin.PoptartHidesArmour.Value);
        if (!enabled)
        {
            return;
        }
        RebuildIfChanged();

        // Runs after animation, so it sits on this frame's pose rather than lagging a frame behind
        PlayerBones bones = _owner.PlayerBones;
        Vector3 pelvis = bones.Pelvis.position;
        Vector3 neck = bones.Neck.position;
        Vector3 up = neck - pelvis;
        Vector3 right = bones.RightShoulder.position - bones.LeftShoulder.position;
        Vector3 forward = Vector3.Cross(right, up);
        if (up.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 centre = Vector3.Lerp(pelvis, neck, 0.55f)
            + forward.normalized * Plugin.PoptartForwardOffset.Value
            + up.normalized * Plugin.PoptartUpOffset.Value;
        transform.SetPositionAndRotation(centre, Quaternion.LookRotation(forward, up));
        transform.localScale = Vector3.one * Plugin.PoptartScale.Value;
    }

    // Rebuilt rather than stretched, so the frosting and sprinkles keep their thickness and only the crust changes
    private void RebuildIfChanged()
    {
        float height = Plugin.PoptartHeight.Value;
        float depth = Plugin.PoptartDepth.Value;
        if (_filter.sharedMesh != null && Mathf.Approximately(height, _builtHeight) && Mathf.Approximately(depth, _builtDepth))
        {
            return;
        }
        Mesh old = _filter.sharedMesh;
        _filter.sharedMesh = BuildMesh(height, depth);
        _builtHeight = height;
        _builtDepth = depth;
        if (old != null)
        {
            Destroy(old);
        }
    }

    // Gear views load in the background and can be rebuilt, so the hiding is reapplied rather than done once
    private void UpdateGear(bool hide)
    {
        if (hide == _gearHidden && Time.time < _nextGearCheck)
        {
            return;
        }
        _nextGearCheck = Time.time + GearCheckInterval;
        _gearHidden = hide;

        PlayerBody body = _owner.PlayerBody;
        if (body == null)
        {
            return;
        }
        foreach (EquipmentSlot slot in HiddenSlots)
        {
            if (!body.SlotViews.TryGetByKey(slot, out PlayerBody.SlotView view) || view?.Renderers == null)
            {
                continue;
            }
            foreach (Renderer renderer in view.Renderers)
            {
                if (renderer != null)
                {
                    // Not .enabled, which the game's own culling switches back on
                    renderer.forceRenderingOff = hide;
                }
            }
        }
    }

    private static Mesh BuildMesh(float height, float depth)
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();

        float halfW = Width / 2, halfH = height / 2, halfD = depth / 2;

        // Crust: two overlapping boxes make the stepped pixel corners
        AddBox(vertices, colors, triangles, new Vector3(-halfW, -halfH + Pixel, -halfD), new Vector3(halfW, halfH - Pixel, halfD), Crust);
        AddBox(vertices, colors, triangles, new Vector3(-halfW + Pixel, -halfH, -halfD), new Vector3(halfW - Pixel, halfH, halfD), Crust);

        // Frosting and sprinkles on both faces, inset from the crust edge
        float frostW = halfW - Pixel * 1.5f, frostH = halfH - Pixel * 1.5f;
        foreach (float side in new[] { 1f, -1f })
        {
            float inner = side * halfD;
            float outer = side * (halfD + FrostingThickness);
            AddBox(vertices, colors, triangles, Ordered(-frostW, -frostH + Pixel, inner, frostW, frostH - Pixel, outer), Frosting);
            AddBox(vertices, colors, triangles, Ordered(-frostW + Pixel, -frostH, inner, frostW - Pixel, frostH, outer), Frosting);

            float top = side * (halfD + FrostingThickness + SprinkleThickness);
            foreach (Vector2 spot in Sprinkles)
            {
                // Mirrored on the back, so the pattern reads the same way round from behind
                float x = Mathf.Lerp(-frostW + Pixel, frostW - Pixel, side > 0 ? spot.x : 1 - spot.x);
                float y = Mathf.Lerp(-frostH + Pixel, frostH - Pixel, spot.y);
                AddBox(vertices, colors, triangles, Ordered(x - Pixel / 2, y - Pixel / 2, outer, x + Pixel / 2, y + Pixel / 2, top), Sprinkle);
            }
        }

        var mesh = new Mesh { name = "NyanBossPoptart" };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static (Vector3 Min, Vector3 Max) Ordered(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        return (new Vector3(Mathf.Min(x1, x2), Mathf.Min(y1, y2), Mathf.Min(z1, z2)), new Vector3(Mathf.Max(x1, x2), Mathf.Max(y1, y2), Mathf.Max(z1, z2)));
    }

    private static void AddBox(List<Vector3> vertices, List<Color32> colors, List<int> triangles, (Vector3 Min, Vector3 Max) box, Color32 color)
    {
        AddBox(vertices, colors, triangles, box.Min, box.Max, color);
    }

    // The shader is unlit, so each face gets its own fixed shade to keep the edges readable
    private static void AddBox(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Vector3 min, Vector3 max, Color32 color)
    {
        AddFace(vertices, colors, triangles, color, 1f, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
        AddFace(vertices, colors, triangles, color, 1f, new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z));
        AddFace(vertices, colors, triangles, color, 1.08f, new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z));
        AddFace(vertices, colors, triangles, color, 0.7f, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z));
        AddFace(vertices, colors, triangles, color, 0.85f, new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z));
        AddFace(vertices, colors, triangles, color, 0.85f, new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z));
    }

    private static void AddFace(List<Vector3> vertices, List<Color32> colors, List<int> triangles, Color32 color, float shade, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        var shaded = new Color32((byte)Mathf.Min(255, color.r * shade), (byte)Mathf.Min(255, color.g * shade), (byte)Mathf.Min(255, color.b * shade), 255);
        for (int i = 0; i < 4; i++)
        {
            colors.Add(shaded);
        }
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 1);
        triangles.Add(first);
        triangles.Add(first + 3);
        triangles.Add(first + 2);
    }
}
