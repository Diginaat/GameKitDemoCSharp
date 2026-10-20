using System.Collections.Generic;
using GameKit.Core;
using Godot;

namespace GameKit.Water;

/// <summary>
/// Swimmable water: an Area3D with one <see cref="BoxShape3D"/> child; the box's top is the surface.
/// Gameplay (swimming) uses that flat level. With <see cref="Rendered"/> on, the volume builds its own
/// visuals when it enters the tree: a PhysX GPU water surface (pool_water.gdshader) that bodies in the
/// water ripple, the per-pixel underwater view, and the planar underside reflection. Place it inside four
/// walls: the surface spans exactly the box's top face. Collision mask: swimmers (and props to ripple).
/// Bodies implementing <see cref="IWaterUser"/> are told when they enter and leave.
/// </summary>
[GlobalClass]
public partial class WaterVolume : Area3D
{
    // Ripple tuning, after the example project's buoyant_body.gd: a body disturbs the surface only near
    // it, and more while moving.
    private const float RippleReach = 0.3f;
    // Kept small: a swimmer's head sits in its own proxy, and a deep resting dent put the eye above the
    // drawn surface while it was below the still level.
    private const float RestingWake = 0.03f;
    private const float WakeReferenceSpeed = 5f;
    private const float SplashReferenceSpeed = 8f;
    // The ripple solver does not conserve volume: its mean is measured on a grid and the material shifts the
    // drawn surface back to SurfaceY. Measured every few ticks, eased so corrections are not visible.
    private const int LevelSamples = 5;
    private const int LevelInterval = 6;
    private const float LevelEaseRate = 2f;

    private readonly List<Node3D> _bodies = new();
    private float _levelCorrection;
    private float _levelTarget;
    private int _tick;
    private bool _cullBoundsSet;

    private static readonly StringName LevelCorrectionParam = "level_correction";
    // Surface height inputs shared with the underwater view, so its waterline matches the drawn waves.
    private static readonly StringName[] SharedSurfaceParams =
    {
        "ripple_height_tex", "ocean_height_tex", "ripple_domain_size", "ocean_domain_size", "grid_center",
    };

    public float SurfaceY { get; private set; }

    /// <summary>World-space box of the water.</summary>
    public Aabb Bounds { get; private set; }

    /// <summary>Off: an invisible swimmable volume (no surface, underwater view or reflection).</summary>
    [Export] public bool Rendered { get; set; } = true;

    /// <summary>Underside reflection settings; null for no planar reflection.</summary>
    [Export] public PlanarReflectionSettings? Reflection { get; set; }

    /// <summary>Rendered water surface, built in _Ready; null when not rendered or without a RenderingDevice.</summary>
    public PhysXWaterSurface3D? Surface { get; private set; }

    /// <summary>Full-screen underwater pass material (underwater_view.gdshader), built in _Ready.</summary>
    public ShaderMaterial? UnderwaterView { get; private set; }

    public override void _Ready()
    {
        foreach (var child in GetChildren())
        {
            if (child is CollisionShape3D { Shape: BoxShape3D box } shape)
            {
                SurfaceY = shape.GlobalPosition.Y + box.Size.Y * 0.5f * shape.GlobalBasis.Scale.Y;
                var size = box.Size * shape.GlobalBasis.Scale;
                Bounds = new Aabb(shape.GlobalPosition - size * 0.5f, size);
                break;
            }
        }
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
        if (Rendered)
            BuildVisuals();
        SetPhysicsProcess(Surface != null);
    }

    private void BuildVisuals()
    {
        var size = Bounds.Size;
        var center = Bounds.GetCenter();
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://addons/gamekit_water/shaders/pool_water.gdshader") };
        // The PhysX water node needs a RenderingDevice: it crashes on its first tick without one (headless),
        // although its docs promise a no-op (docs/PHYSX_INTEGRATION.md, issue 3). Without one, draw the
        // same material on a still plane.
        if (RenderingServer.GetRenderingDevice() == null)
        {
            material.SetShaderParameter("water_level", SurfaceY);
            AddChild(new MeshInstance3D
            {
                Name = "Surface",
                TopLevel = true,
                Position = center with { Y = 0f },
                Mesh = new PlaneMesh { Size = new Vector2(size.X, size.Z) },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = RenderLayers.Water,
                // Lifted to the surface by the vertex shader: keep it from being culled at its rest height.
                ExtraCullMargin = Mathf.Abs(SurfaceY) + 1f,
            });
        }
        else
        {
            var footprint = new Vector2(size.X, size.Z);
            Surface = new PhysXWaterSurface3D
            {
                Name = "Surface",
                // Grids are centred on the node; at world Y 0 the mean level is simply WaterLevel.
                TopLevel = true,
                Position = center with { Y = 0f },
                WaterLevel = SurfaceY,
                Depth = size.Y,
                DomainSize = footprint,
                GridResolution = StableRippleResolution(Mathf.Min(size.X, size.Z), size.Y),
                Damping = 1.2f,
                RippleAmplitude = 0.6f,
                // A sheltered pool: faint wind chop, no whitecaps.
                OceanDomainSize = footprint,
                OceanGridResolution = 64,
                WindSpeed = 4f,
                WindDirection = new Vector2(1f, 0.4f),
                Fetch = Mathf.Max(size.X, size.Z),
                WaveAmplitude = 1f,
                Choppiness = 0.3f,
                FoamEnabled = false,
                // Caustics come from the pool tile shader (see pool_tile.gdshader for why).
                CausticsEnabled = false,
                WaterMaterial = material,
            };
            AddChild(Surface);
            if (Reflection != null)
                AddChild(new PlanarReflection { Name = "Reflection", Water = this, Material = material, Settings = Reflection });
        }
        // Underwater view for any camera inside the box (fog along the underwater part of each view ray).
        UnderwaterView = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://addons/gamekit_water/shaders/underwater_view.gdshader"),
            RenderPriority = (int)Material.RenderPriorityMax,
        };
        UnderwaterView.SetShaderParameter("surface_y", SurfaceY);
        UnderwaterView.SetShaderParameter("box_min", Bounds.Position);
        UnderwaterView.SetShaderParameter("box_max", Bounds.End);
        AddChild(new MeshInstance3D
        {
            Name = "UnderwaterView",
            Layers = RenderLayers.Water,
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
            MaterialOverride = UnderwaterView,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Positioned in clip space by the shader: never frustum-culled.
            ExtraCullMargin = 16384f,
        });
    }

    /// <summary>
    /// Ripple cells per side that keep the engine's explicit wave solver stable: the wave speed
    /// sqrt(g * depth) may cross at most half a cell per 60 Hz tick. At 2 m depth, 128 cells over 10 m
    /// (0.94 cells per tick) diverged to NaN within seconds of swimming; 64 (0.47) stays stable.
    /// </summary>
    private static int StableRippleResolution(float side, float depth)
    {
        const float MaxCellsPerTick = 0.5f;
        float speed = Mathf.Sqrt(9.81f * depth);
        return Mathf.Clamp(Mathf.FloorToInt(side * MaxCellsPerTick * 60f / speed), 16, 128);
    }

    public override void _PhysicsProcess(double delta)
    {
        CorrectLevel((float)delta);
        foreach (var body in _bodies)
        {
            var (center, halfHeight, radius) = Extent(body);
            float gap = Mathf.Abs(center.Y - SurfaceY) - halfHeight;
            float proximity = Mathf.Clamp(1f - gap / RippleReach, 0f, 1f);
            int id = RippleId(body);
            if (proximity <= 0f)
            {
                Surface!.ClearSphere(id);
                continue;
            }
            var velocity = VelocityOf(body);
            float speed = new Vector2(velocity.X, velocity.Z).Length();
            float strength = Mathf.Clamp(RestingWake + (1f - RestingWake) * speed / WakeReferenceSpeed, RestingWake, 1f) * proximity;
            Surface!.SubmitSphere(id, new Vector3(center.X, SurfaceY, center.Z), radius, strength);
        }
    }

    private void CorrectLevel(float dt)
    {
        var surface = Surface!;
        if (!_cullBoundsSet)
            _cullBoundsSet = WidenCullBounds(surface);
        if (++_tick % LevelInterval == 0)
        {
            var shape = (BoxShape3D)GetChild<CollisionShape3D>(0).Shape;
            var half = new Vector2(shape.Size.X, shape.Size.Z) * 0.45f;
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < LevelSamples; i++)
            {
                for (int j = 0; j < LevelSamples; j++)
                {
                    var t = new Vector2(i, j) / (LevelSamples - 1) * 2f - Vector2.One;
                    float h = surface.SampleHeight(GlobalPosition + new Vector3(t.X * half.X, 0f, t.Y * half.Y));
                    if (float.IsFinite(h))
                    {
                        sum += h;
                        count++;
                    }
                }
            }
            if (count > 0)
                _levelTarget = SurfaceY - sum / count;
        }
        _levelCorrection = Mathf.Lerp(_levelCorrection, _levelTarget, 1f - Mathf.Exp(-LevelEaseRate * dt));
        if (surface.WaterMaterial is not ShaderMaterial water)
            return;
        water.SetShaderParameter(LevelCorrectionParam, _levelCorrection);
        if (UnderwaterView == null)
            return;
        UnderwaterView.SetShaderParameter(LevelCorrectionParam, _levelCorrection);
        // The node binds (and may rebind) these on its own material only.
        foreach (var name in SharedSurfaceParams)
            UnderwaterView.SetShaderParameter(name, water.GetShaderParameter(name));
    }

    /// <summary>
    /// The node's surface mesh is a flat plane at the node's height (Y 0); the water material lifts it to
    /// the water level in the vertex shader, which culling does not see. Without a wider margin the whole
    /// surface was culled whenever the camera looked up from under water.
    /// </summary>
    private bool WidenCullBounds(Node surface)
    {
        bool found = false;
        foreach (var child in surface.GetChildren(includeInternal: true))
        {
            if (child is GeometryInstance3D mesh)
            {
                mesh.ExtraCullMargin = Mathf.Abs(SurfaceY - mesh.GlobalPosition.Y) + 1f;
                // Kept out of the planar reflection.
                mesh.Layers = RenderLayers.Water;
                found = true;
            }
        }
        return found;
    }

    public override void _ExitTree()
    {
        if (Surface != null)
            foreach (var body in _bodies)
                Surface.ClearSphere(RippleId(body));
        _bodies.Clear();
    }

    private void OnBodyEntered(Node3D body)
    {
        (body as IWaterUser)?.OnWaterEntered(this);
        if (Surface == null)
            return;
        _bodies.Add(body);
        // Splash scaled by the entry speed.
        float speed = Mathf.Abs(VelocityOf(body).Y);
        if (speed > 1f)
            Surface.SubmitImpulse(body.GlobalPosition with { Y = SurfaceY }, Extent(body).Radius * 1.5f, Mathf.Min(speed / SplashReferenceSpeed, 1f));
    }

    private void OnBodyExited(Node3D body)
    {
        (body as IWaterUser)?.OnWaterExited(this);
        if (Surface != null && _bodies.Remove(body))
            Surface.ClearSphere(RippleId(body));
    }

    private static int RippleId(Node body) => (int)body.GetInstanceId();

    private static Vector3 VelocityOf(Node3D body) => body switch
    {
        CharacterBody3D c => c.Velocity,
        RigidBody3D r => r.LinearVelocity,
        _ => Vector3.Zero,
    };

    /// <summary>Vertical centre, half height and horizontal radius of a body, for ripple proxies.</summary>
    private static (Vector3 Center, float HalfHeight, float Radius) Extent(Node3D body) =>
        body is IWaterUser user ? user.WaterExtent : (body.GlobalPosition, 0.3f, 0.4f);
}
