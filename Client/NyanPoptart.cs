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

    // Two looks, shared by every pastry. Lit: one material per colour on the game's own gear shader, so it takes the
    // scene's light like his clothes do. Unlit: a copy of the trail's material, flat colour baked into the vertices,
    // kept as the fallback and for comparison.
    private static readonly string[] LitShaders = { "p0/Reflective/Bumped Specular SMap_Decal", "p0/Reflective/Bumped Specular SMap" };
    private static readonly Color32[] PartColours = { Crust, Frosting, Sprinkle };
    private static Material[] _litMaterials;
    private static Material[] _unlitMaterials;
    private static float _appliedBrightness = -1f;

    private bool? _lit; // null until the first look is applied

    private Player _owner;
    private MeshFilter _filter;
    private float _builtHeight;
    private float _builtDepth;
    private float _nextGearCheck;
    private bool _gearHidden;

    internal static void Attach(Player player)
    {
        if (!CreateMaterials())
        {
            return;
        }

        var host = new GameObject("NyanBossPoptart");
        NyanPoptart poptart = host.AddComponent<NyanPoptart>();
        poptart._owner = player;
        poptart._filter = host.AddComponent<MeshFilter>();
        poptart.RebuildIfChanged();
        host.AddComponent<MeshRenderer>();
        poptart.ApplyLook();
    }

    private static bool CreateMaterials()
    {
        if (_unlitMaterials != null)
        {
            return true;
        }
        Material trail = NyanTrail.GetMaterial();
        if (trail == null)
        {
            return false;
        }
        var unlit = new Material(trail) { hideFlags = HideFlags.HideAndDontSave };
        _unlitMaterials = new[] { unlit, unlit, unlit };

        // The game's shaders come from its own shader bundle, which is loaded by the time anyone wears gear in a raid
        Shader shader = null;
        foreach (string name in LitShaders)
        {
            shader = Shader.Find(name);
            if (shader != null)
            {
                break;
            }
        }
        if (shader == null)
        {
            Plugin.Log.LogWarning("Pop-tart: the game's gear shader was not found, using the unlit look");
            return true;
        }
        _litMaterials = new Material[PartColours.Length];
        for (int i = 0; i < PartColours.Length; i++)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = PartColours[i] };
            material.SetFloat("_StencilType", 1f); // Characters, as his gear uses
            // A soft sheen rather than plastic: weak, broad highlights and no mirror reflection
            material.SetColor("_SpecColor", new Color(0.25f, 0.25f, 0.25f, 1f));
            material.SetFloat("_Glossness", 0.3f);
            material.SetFloat("_Specularness", 0.1f);
            material.SetColor("_ReflectColor", Color.black);
            _litMaterials[i] = material;
        }
        return true;
    }

    // Switches between the lit and unlit looks, and applies the brightness to whichever is in use
    private void ApplyLook()
    {
        bool lit = Plugin.PoptartLit.Value && _litMaterials != null;
        float brightness = Plugin.PoptartBrightness.Value;
        var meshRenderer = GetComponent<MeshRenderer>();
        if (lit != _lit)
        {
            _lit = lit;
            meshRenderer.sharedMaterials = lit ? _litMaterials : _unlitMaterials;
            var shadows = lit ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.shadowCastingMode = shadows;
            meshRenderer.receiveShadows = lit;
            meshRenderer.lightProbeUsage = lit ? UnityEngine.Rendering.LightProbeUsage.BlendProbes : UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = lit ? UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes : UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        if (Mathf.Approximately(brightness, _appliedBrightness))
        {
            return;
        }
        _appliedBrightness = brightness;
        _unlitMaterials[0].color = new Color(brightness, brightness, brightness, 1f);
        if (_litMaterials != null)
        {
            for (int i = 0; i < _litMaterials.Length; i++)
            {
                Color colour = PartColours[i];
                _litMaterials[i].color = new Color(colour.r * brightness, colour.g * brightness, colour.b * brightness, 1f);
            }
        }
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
        ApplyLook();

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

    private enum Part { Crust, Frosting, Sprinkle }

    // Everything one mesh needs, so the boxes can be added part by part
    private class MeshData
    {
        internal readonly List<Vector3> Vertices = new List<Vector3>();
        internal readonly List<Vector3> Normals = new List<Vector3>();
        internal readonly List<Vector2> Uvs = new List<Vector2>();
        internal readonly List<Color32> Colors = new List<Color32>();
        internal readonly List<int>[] Triangles = { new List<int>(), new List<int>(), new List<int>() };
    }

    private static Mesh BuildMesh(float height, float depth)
    {
        var data = new MeshData();
        float halfW = Width / 2, halfH = height / 2, halfD = depth / 2;

        // Crust: two overlapping boxes make the stepped pixel corners
        AddBox(data, Part.Crust, (new Vector3(-halfW, -halfH + Pixel, -halfD), new Vector3(halfW, halfH - Pixel, halfD)));
        AddBox(data, Part.Crust, (new Vector3(-halfW + Pixel, -halfH, -halfD), new Vector3(halfW - Pixel, halfH, halfD)));

        // Frosting and sprinkles on both faces, inset from the crust edge
        float frostW = halfW - Pixel * 1.5f, frostH = halfH - Pixel * 1.5f;
        foreach (float side in new[] { 1f, -1f })
        {
            float inner = side * halfD;
            float outer = side * (halfD + FrostingThickness);
            AddBox(data, Part.Frosting, Ordered(-frostW, -frostH + Pixel, inner, frostW, frostH - Pixel, outer));
            AddBox(data, Part.Frosting, Ordered(-frostW + Pixel, -frostH, inner, frostW - Pixel, frostH, outer));

            float top = side * (halfD + FrostingThickness + SprinkleThickness);
            foreach (Vector2 spot in Sprinkles)
            {
                // Mirrored on the back, so the pattern reads the same way round from behind
                float x = Mathf.Lerp(-frostW + Pixel, frostW - Pixel, side > 0 ? spot.x : 1 - spot.x);
                float y = Mathf.Lerp(-frostH + Pixel, frostH - Pixel, spot.y);
                AddBox(data, Part.Sprinkle, Ordered(x - Pixel / 2, y - Pixel / 2, outer, x + Pixel / 2, y + Pixel / 2, top));
            }
        }

        // One submesh per colour, so the lit look can give each its own material. The unlit look uses the vertex colours.
        var mesh = new Mesh { name = "NyanBossPoptart", subMeshCount = data.Triangles.Length };
        mesh.SetVertices(data.Vertices);
        mesh.SetNormals(data.Normals);
        mesh.SetUVs(0, data.Uvs);
        mesh.SetColors(data.Colors);
        for (int i = 0; i < data.Triangles.Length; i++)
        {
            mesh.SetTriangles(data.Triangles[i], i);
        }
        // The game's shader reads its normal map along the surface tangents, so a lit mesh needs them
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static (Vector3 Min, Vector3 Max) Ordered(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        return (new Vector3(Mathf.Min(x1, x2), Mathf.Min(y1, y2), Mathf.Min(z1, z2)), new Vector3(Mathf.Max(x1, x2), Mathf.Max(y1, y2), Mathf.Max(z1, z2)));
    }

    // Each face also gets a fixed shade in its vertex colour, which keeps the edges readable in the unlit look
    private static void AddBox(MeshData data, Part part, (Vector3 Min, Vector3 Max) box)
    {
        Vector3 min = box.Min, max = box.Max;
        AddFace(data, part, Vector3.forward, 1f, new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z));
        AddFace(data, part, Vector3.back, 1f, new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z));
        AddFace(data, part, Vector3.up, 1.08f, new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z));
        AddFace(data, part, Vector3.down, 0.7f, new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z));
        AddFace(data, part, Vector3.right, 0.85f, new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z));
        AddFace(data, part, Vector3.left, 0.85f, new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z));
    }

    // a, b, c, d go round the face's edge. The lit shader skips faces seen from behind, so the winding is chosen
    // from the face's outward direction instead of trusted from the corner order.
    private static void AddFace(MeshData data, Part part, Vector3 outward, float shade, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = data.Vertices.Count;
        data.Vertices.Add(a);
        data.Vertices.Add(b);
        data.Vertices.Add(c);
        data.Vertices.Add(d);
        data.Uvs.Add(new Vector2(0, 0));
        data.Uvs.Add(new Vector2(1, 0));
        data.Uvs.Add(new Vector2(1, 1));
        data.Uvs.Add(new Vector2(0, 1));
        Color32 colour = PartColours[(int)part];
        var shaded = new Color32((byte)Mathf.Min(255, colour.r * shade), (byte)Mathf.Min(255, colour.g * shade), (byte)Mathf.Min(255, colour.b * shade), 255);
        for (int i = 0; i < 4; i++)
        {
            data.Normals.Add(outward);
            data.Colors.Add(shaded);
        }

        // Unity treats a triangle as facing the way of Cross(second - first, third - first)
        bool keepOrder = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) > 0;
        List<int> triangles = data.Triangles[(int)part];
        if (keepOrder)
        {
            triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
        }
        else
        {
            triangles.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
        }
    }
}