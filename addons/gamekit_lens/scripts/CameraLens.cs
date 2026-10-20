using System;
using GameKit.Core;
using Godot;

namespace GameKit.Lens;

/// <summary>
/// Photographic camera finish for a <see cref="Camera3D"/>: a full-screen lens pass (distortion,
/// chromatic aberration, depth of field, halation, grading, vignette, grain) and a lens flare overlay
/// for the sun and for lights in the <see cref="FlareGroup"/> group. The look is a <see cref="LensProfile"/>.
///
/// Setup: add as a child of the Camera3D (or set <see cref="Camera"/>). Ported and refined from
/// EpicCameraX (MIT, Diginaat / DevKine).
///
/// The pass only draws while its camera is the current one, and it sits on
/// <see cref="RenderLayers.Viewmodel"/> (first-person only), so mirror, rain-cover and other capture
/// cameras never see it. It doesn't touch the camera's CameraAttributes (exposure stays with the
/// environment and the weather).
/// </summary>
[GlobalClass]
public partial class CameraLens : Node3D
{
    /// <summary>Lights (any Light3D) in this group get a lens flare. Opt-in, so big maps don't pay for
    /// every lamp.</summary>
    public const string FlareGroup = "lens_flare";

    private const int MaxSources = 16;
    private const int MaxFlares = 12; // lens_flare.gdshader MAX_FLARES
    private const float SourceRescanInterval = 0.5f;
    private const float SunDistance = 2000f;
    private const float ScreenEntryMargin = 0.08f;
    private const float ScreenEdgeSoftness = 0.03f;
    private const float EdgePresence = 0.2f;

    private LensProfile? _profile;

    /// <summary>The look. Null or a profile with everything off = no lens effect (the pass is hidden).</summary>
    [Export]
    public LensProfile? Profile
    {
        get => _profile;
        set { _profile = value; Refresh(); }
    }

    /// <summary>Camera to finish. Empty = the parent Camera3D.</summary>
    [Export] public Camera3D? Camera { get; set; }
    /// <summary>Sun for the sun flare. Empty = the first DirectionalLight3D in the scene.</summary>
    [Export] public DirectionalLight3D? Sun { get; set; }
    /// <summary>Physics layers that block a flare source (a wall in front of a lamp hides its flare).</summary>
    [Export(PropertyHint.Layers3DPhysics)] public uint OcclusionMask { get; set; } = CollisionLayers.World;
    /// <summary>Canvas layer of the flare overlay: above the 3D view, below HUDs on layer 1 and up.</summary>
    [Export] public int FlareCanvasLayer { get; set; }

    private MeshInstance3D _quad = null!;
    private ShaderMaterial _post = null!;
    private CanvasLayer _flareLayer = null!;
    private ColorRect _flareRect = null!;
    private ShaderMaterial _flare = null!;
    private bool _ready;
    private Camera3D? _camera;
    private bool _sunSearched;
    private float _rescan;
    private readonly System.Collections.Generic.List<Light3D> _lights = new();
    private PhysicsRayQueryParameters3D? _ray;

    // Flare sources with temporal fade, keyed by instance id. Fixed arrays: no per-frame allocation.
    private readonly ulong[] _ids = new ulong[MaxSources];
    private readonly Vector4[] _state = new Vector4[MaxSources]; // uv.x, uv.y, size, intensity
    private readonly Vector4[] _tint = new Vector4[MaxSources];  // rgb, visibility gate
    private readonly bool[] _seen = new bool[MaxSources];
    private readonly Vector4[] _flareData = new Vector4[MaxFlares];
    private readonly Vector4[] _flareTint = new Vector4[MaxFlares];

    public override void _Ready()
    {
        _post = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://addons/gamekit_lens/shaders/lens_post.gdshader"),
            // First in the transparent pass. Godot copies the screen texture before transparents, so a
            // later pass would paint that copy over rain, water and Label3D. Transparents therefore draw
            // on top of the lens pass, without lens effects.
            RenderPriority = (int)Material.RenderPriorityMin,
        };
        _quad = new MeshInstance3D
        {
            Name = "LensPass",
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) }, // vertices = clip-space screen corners
            MaterialOverride = _post,
            Layers = RenderLayers.Viewmodel,
            ExtraCullMargin = 16384f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Position = new Vector3(0f, 0f, -1f),
        };
        AddChild(_quad);

        _flare = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/gamekit_lens/shaders/lens_flare.gdshader") };
        _flareLayer = new CanvasLayer { Name = "LensFlare", Layer = FlareCanvasLayer };
        _flareRect = new ColorRect
        {
            Material = _flare,
            Color = new Color(0f, 0f, 0f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flareRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _flareLayer.AddChild(_flareRect);
        AddChild(_flareLayer);

        _ready = true;
        Refresh();
    }

    /// <summary>Re-applies <see cref="Profile"/> to the shaders. Call after changing profile values from code.</summary>
    public void Refresh()
    {
        if (!_ready)
            return;
        LensProfile? p = _profile;
        _quad.Visible = p is { AnyPostEffect: true };
        if (p == null)
            return;
        p.ApplyPost(_post);
        p.ApplyFlare(_flare);
    }

    public override void _Process(double delta)
    {
        _camera = Camera ?? GetParent() as Camera3D;
        bool active = _profile != null && _camera != null && _camera.IsCurrent();
        _quad.Visible = active && _profile!.AnyPostEffect;
        if (!active)
        {
            _flareRect.Visible = false;
            return;
        }
        _post.SetShaderParameter("post_dof_near_clip", _camera!.Near);
        _post.SetShaderParameter("post_dof_far_clip", _camera.Far);
        UpdateFlares((float)delta);
    }

    private void UpdateFlares(float dt)
    {
        LensProfile p = _profile!;
        Camera3D cam = _camera!;
        Vector2 viewport = cam.GetViewport().GetVisibleRect().Size;
        if (!p.FlareEnabled || viewport.X <= 0f || viewport.Y <= 0f)
        {
            _flareRect.Visible = false;
            Array.Clear(_ids);
            return;
        }

        _rescan -= dt;
        if (_rescan <= 0f)
        {
            _rescan = SourceRescanInterval;
            _lights.Clear();
            foreach (Node n in GetTree().GetNodesInGroup(FlareGroup))
            {
                if (n is Light3D light and not DirectionalLight3D)
                    _lights.Add(light);
            }
        }
        if (Sun == null && !_sunSearched)
        {
            _sunSearched = true;
            Node? scene = GetTree().CurrentScene;
            var found = scene?.FindChildren("*", nameof(DirectionalLight3D), true, false);
            if (found is { Count: > 0 })
                Sun = (DirectionalLight3D)found[0];
        }

        Array.Clear(_seen);
        foreach (Light3D light in _lights)
        {
            if (!IsInstanceValid(light) || !light.IsVisibleInTree() || light.LightEnergy < p.FlareEnergyThreshold)
                continue;
            Vector3 pos = light.GlobalPosition;
            if (!OnScreen(cam, pos, viewport, out Vector2 uv, out float presence) || Occluded(cam, pos))
                continue;
            float distance = cam.GlobalPosition.DistanceTo(pos);
            float distanceFactor = Mathf.Clamp(1f / Mathf.Pow(Mathf.Max(distance, 1f) / 12f, p.FlareDistanceFalloff), 0.18f, 1f);
            float energy = Mathf.Max(light.LightEnergy - p.FlareEnergyThreshold, 0f);
            float intensity = Mathf.Clamp(0.45f + energy * 0.2f, 0f, 8f) * distanceFactor * p.FlareIntensity * presence;
            float size = p.FlareSize * (1f + energy * 0.08f);
            Color c = light.LightColor;
            Track(light.GetInstanceId(), new Vector4(uv.X, uv.Y, size, intensity), new Vector4(c.R, c.G, c.B, 0.2f), dt);
        }

        if (p.SunFlare && Sun != null && IsInstanceValid(Sun) && Sun.IsVisibleInTree())
        {
            Vector3 toSun = Sun.GlobalBasis.Z.Normalized(); // a light shines along -Z
            float facing = (-cam.GlobalBasis.Z).Dot(toSun);
            Vector3 pos = cam.GlobalPosition + toSun * Mathf.Min(SunDistance, cam.Far * 0.5f);
            if (facing > 0f && OnScreen(cam, pos, viewport, out Vector2 uv, out float presence) && !Occluded(cam, pos))
            {
                // Scales with the sun's energy (no floor), so overcast weather that dims the sun dims the flare.
                float intensity = Sun.LightEnergy * p.SunFlareIntensity * p.FlareIntensity * presence
                    * Mathf.Pow(facing, 1.35f);
                Color c = Sun.LightColor;
                Track(Sun.GetInstanceId(), new Vector4(uv.X, uv.Y, p.FlareSize * p.SunFlareSize, intensity),
                    new Vector4(c.R, c.G, c.B, 1f), dt);
            }
        }

        // Sources that disappeared fade out instead of popping.
        for (int i = 0; i < MaxSources; i++)
        {
            if (_ids[i] == 0 || _seen[i])
                continue;
            _state[i].W = Mathf.Lerp(_state[i].W, 0f, 1f - Mathf.Exp(-p.FlareFadeOutSpeed * 1.5f * dt));
            if (_state[i].W <= 0.01f)
                _ids[i] = 0;
        }

        int count = SelectBrightest();
        _flareRect.Visible = count > 0;
        if (count == 0)
            return;
        _flare.SetShaderParameter("flare_count", count);
        _flare.SetShaderParameter("flare_data", _flareData);
        _flare.SetShaderParameter("flare_tint", _flareTint);

        void Track(ulong id, Vector4 target, Vector4 tint, float step)
        {
            int slot = -1, free = -1;
            for (int i = 0; i < MaxSources; i++)
            {
                if (_ids[i] == id) { slot = i; break; }
                if (_ids[i] == 0 && free < 0) free = i;
            }
            if (slot < 0)
            {
                if (free < 0)
                    return;
                slot = free;
                _ids[slot] = id;
                _state[slot] = new Vector4(target.X, target.Y, target.Z, 0f);
            }
            float speed = target.W >= _state[slot].W ? p.FlareFadeInSpeed : p.FlareFadeOutSpeed;
            float k = 1f - Mathf.Exp(-speed * step);
            _state[slot] = new Vector4(target.X, target.Y, Mathf.Lerp(_state[slot].Z, target.Z, k), Mathf.Lerp(_state[slot].W, target.W, k));
            _tint[slot] = tint;
            _seen[slot] = true;
        }
    }

    /// <summary>Copies the strongest sources (intensity × size) into the shader arrays; returns the count.</summary>
    private int SelectBrightest()
    {
        Array.Clear(_flareData);
        Array.Clear(_flareTint);
        int count = 0;
        Span<bool> taken = stackalloc bool[MaxSources];
        while (count < MaxFlares)
        {
            int best = -1;
            float bestScore = 0.001f;
            for (int i = 0; i < MaxSources; i++)
            {
                float score = _state[i].W * _state[i].Z;
                if (_ids[i] != 0 && !taken[i] && _state[i].W > 0.001f && score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }
            if (best < 0)
                break;
            taken[best] = true;
            _flareData[count] = _state[best];
            _flareTint[count] = _tint[best];
            count++;
        }
        return count;
    }

    private static bool OnScreen(Camera3D cam, Vector3 pos, Vector2 viewport, out Vector2 uv, out float presence)
    {
        uv = default;
        presence = 0f;
        if (cam.IsPositionBehind(pos))
            return false;
        Vector2 sp = cam.UnprojectPosition(pos);
        uv = new Vector2(sp.X / viewport.X, sp.Y / viewport.Y);
        presence = AxisPresence(uv.X) * AxisPresence(uv.Y);
        return presence > 0f;
    }

    /// <summary>1 inside the frame, fading to <see cref="EdgePresence"/> at the edge and to 0 just outside,
    /// so a flare slides in as its source nears the frame.</summary>
    private static float AxisPresence(float v)
    {
        if (v < -ScreenEntryMargin || v > 1f + ScreenEntryMargin)
            return 0f;
        if (v < 0f)
            return EdgePresence * Mathf.SmoothStep(-ScreenEntryMargin, 0f, v);
        if (v > 1f)
            return EdgePresence * (1f - Mathf.SmoothStep(1f, 1f + ScreenEntryMargin, v));
        return Mathf.Lerp(EdgePresence, 1f, Mathf.SmoothStep(0f, ScreenEdgeSoftness, Mathf.Min(v, 1f - v)));
    }

    private bool Occluded(Camera3D cam, Vector3 pos)
    {
        if (OcclusionMask == 0)
            return false;
        _ray ??= new PhysicsRayQueryParameters3D { HitFromInside = true };
        _ray.From = cam.GlobalPosition;
        _ray.To = pos;
        _ray.CollisionMask = OcclusionMask;
        return cam.GetWorld3D().DirectSpaceState.IntersectRay(_ray).Count > 0;
    }
}
