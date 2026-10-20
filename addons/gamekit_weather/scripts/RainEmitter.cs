using GameKit.Core;
using Godot;
using Godot.Collections;

namespace GameKit.Weather;

/// <summary>
/// GPU rain around the camera: lit, see-through streaks of varying size, distant rain curtains, impacts
/// where drops hit geometry (ring on wet surfaces, motion-blurred droplet crown), a low splash mist, and
/// no rain under roofs.
///
/// The emitter follows the current camera, so only a small volume around the player is simulated. A
/// height-field collider (also following the camera) stops drops on the first surface they hit and
/// spawns an impact there. <see cref="Intensity"/> scales the live particle count via AmountRatio, so
/// changing it never restarts the system.
/// </summary>
[GlobalClass]
public partial class RainEmitter : Node3D
{
    private const float ImpactLifetime = 0.45f;

    private float _intensity;
    private Vector3 _wind;
    private Color _curtainColor = new(0.6f, 0.63f, 0.68f);

    [Export(PropertyHint.Range, "0,1")]
    public float Intensity
    {
        get => _intensity;
        set { _intensity = value; UpdateIntensity(); }
    }

    /// <summary>Horizontal wind in m/s. Slants the rain.</summary>
    [Export]
    public Vector3 Wind
    {
        get => _wind;
        set { _wind = value; UpdateMotion(); }
    }

    /// <summary>Color of the distant rain curtains. WeatherSystem sets it to the fog color so the curtains
    /// match the scene lighting.</summary>
    [Export]
    public Color CurtainColor
    {
        get => _curtainColor;
        set { _curtainColor = value; _curtainMaterial?.SetShaderParameter("curtain_color", value); }
    }

    /// <summary>Node to center the rain on. Defaults to the active camera.</summary>
    [Export] public Node3D? FollowTarget { get; set; }

    [ExportGroup("Volume")]
    /// <summary>Particles at intensity 1. Cost scales with this, mostly as overdraw.</summary>
    [Export] public int MaxDrops { get; set; } = 24000;
    [Export] public float AreaSize { get; set; } = 36f;
    [Export] public float EmitHeight { get; set; } = 14f;
    [Export] public float FallSpeed { get; set; } = 11f;

    [ExportGroup("Look")]
    /// <summary>Long, thin streaks read like real rain (a camera exposure smears each drop).</summary>
    [Export] public float StreakLength { get; set; } = 1.1f;
    [Export] public float StreakWidth { get; set; } = 0.005f;
    [Export(PropertyHint.Range, "0,1")] public float Opacity { get; set; } = 0.26f;

    [ExportGroup("Distance")]
    /// <summary>Draw far-off rain as moving sheets and streaks on a cylinder around the camera.</summary>
    [Export] public bool CurtainsEnabled { get; set; } = true;
    [Export] public float CurtainRadius { get; set; } = 22f;
    /// <summary>Low volumetric haze of spray at ground level that thickens with the rain (needs
    /// Environment volumetric fog).</summary>
    [Export] public bool SplashMistEnabled { get; set; } = true;
    /// <summary>Fog density of the splash mist at intensity 1.</summary>
    [Export(PropertyHint.Range, "0,1")] public float SplashMistDensity { get; set; } = 0.06f;
    /// <summary>Height of the camera above the ground it stands on (the mist sits there).</summary>
    [Export] public float CameraHeight { get; set; } = 1.7f;

    [ExportGroup("Impacts")]
    [Export] public bool ImpactsEnabled { get; set; } = true;
    /// <summary>Impact particles at intensity 1. Each draws a ring and a droplet crown.</summary>
    [Export] public int MaxImpacts { get; set; } = 8000;
    [Export(PropertyHint.Range, "0,12")] public int DropletsPerImpact { get; set; } = 6;
    /// <summary>Faint spray puff at each impact. Off by default: single puffs tend to read as snowflakes.</summary>
    [Export] public bool ImpactMist { get; set; }

    [ExportGroup("Collision")]
    [Export] public GpuParticlesCollisionHeightField3D.ResolutionEnum CollisionResolution { get; set; } =
        GpuParticlesCollisionHeightField3D.ResolutionEnum.Resolution512;
    /// <summary>Re-capture the collision height field this often (seconds) even when standing still, so
    /// moved objects don't leave invisible surfaces that drops splash on. 0 = only on move.</summary>
    [Export(PropertyHint.Range, "0,2")] public float CollisionRefreshInterval { get; set; } = 0.2f;

    private GpuParticles3D? _drops;
    private GpuParticles3D _impacts = null!;
    private GpuParticlesCollisionHeightField3D _collision = null!;
    private ParticleProcessMaterial? _processMaterial;
    private MeshInstance3D? _curtain;
    private ShaderMaterial? _curtainMaterial;
    private FogVolume? _splashMist;
    private FogMaterial? _splashMistMaterial;
    private float _collisionTimer;

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        Build();
        UpdateMotion();
        UpdateIntensity();
    }

    public override void _Process(double delta)
    {
        Node3D? target = FollowTarget ?? GetViewport().GetCamera3D();
        if (target != null)
        {
            GlobalPosition = target.GlobalPosition;
            if (_splashMist != null)
            {
                // World-snapped, not glued to the camera: a fog volume that moves every frame smears the
                // volumetric fog's temporal filter.
                Vector3 s = target.GlobalPosition.Snapped(new Vector3(2f, 0.5f, 2f));
                _splashMist.GlobalPosition = new Vector3(s.X, s.Y - CameraHeight + 0.6f, s.Z);
            }
        }
        if (CollisionRefreshInterval > 0f && _intensity > 0.001f)
        {
            _collisionTimer -= (float)delta;
            if (_collisionTimer <= 0f)
            {
                _collisionTimer = CollisionRefreshInterval;
                RenderingServer.ParticlesCollisionHeightFieldUpdate(_collision.GetBase());
            }
        }
    }

    private void Build()
    {
        _processMaterial = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(AreaSize * 0.5f, 0.5f, AreaSize * 0.5f),
            Gravity = Vector3.Zero,
            Spread = 1.5f,
            // Drop size variation (the streak shader also varies each drop).
            ScaleMin = 0.7f,
            ScaleMax = 1.3f,
            ParticleFlagAlignY = true,
            CollisionMode = ParticleProcessMaterial.CollisionModeEnum.HideOnContact,
        };
        if (ImpactsEnabled)
        {
            _processMaterial.SubEmitterMode = ParticleProcessMaterial.SubEmitterModeEnum.AtCollision;
            _processMaterial.SubEmitterAmountAtCollision = 1;
        }

        var dropMaterial = new ShaderMaterial { Shader = LoadShader("rain_streak") };
        dropMaterial.SetShaderParameter("streak_length", StreakLength);
        dropMaterial.SetShaderParameter("streak_width", StreakWidth);
        dropMaterial.SetShaderParameter("opacity", Opacity);
        dropMaterial.SetShaderParameter("far_fade", AreaSize * 0.9f);

        _drops = MakeParticles(MaxDrops, (EmitHeight + 6f) / FallSpeed, _processMaterial);
        _drops.DrawPass1 = new QuadMesh { Material = dropMaterial };
        _drops.VisibilityAabb = new Aabb(new Vector3(-AreaSize, -EmitHeight - 10f, -AreaSize),
            new Vector3(AreaSize * 2f, EmitHeight + 12f, AreaSize * 2f));
        _drops.CollisionBaseSize = 0.02f;
        // Start pre-simulated: no "front" of rain falling from the sky when it begins.
        _drops.Preprocess = _drops.Lifetime;
        AddChild(_drops);

        // Impacts don't move; their shader animates the ring and droplets.
        var impactProcess = new ParticleProcessMaterial { Gravity = Vector3.Zero, InitialVelocityMin = 0f, InitialVelocityMax = 0f };
        _impacts = MakeParticles(MaxImpacts, ImpactLifetime, impactProcess);
        _impacts.VisibilityAabb = _drops.VisibilityAabb;
        var passes = new System.Collections.Generic.List<Mesh>
        {
            new PlaneMesh { Size = Vector2.One, Material = MakeImpactMaterial(0) },
            MakeCrownSheetMesh(24, MakeImpactMaterial(3)),
        };
        if (DropletsPerImpact > 0)
            passes.Add(MakeCrownMesh(DropletsPerImpact, MakeImpactMaterial(1)));
        if (ImpactMist)
            passes.Add(new QuadMesh { Material = MakeImpactMaterial(2) });
        _impacts.DrawPasses = passes.Count;
        for (int i = 0; i < passes.Count; i++)
            _impacts.SetDrawPassMesh(i, passes[i]);
        AddChild(_impacts);
        if (ImpactsEnabled)
            _drops.SubEmitter = _drops.GetPathTo(_impacts);

        if (CurtainsEnabled)
        {
            _curtainMaterial = new ShaderMaterial { Shader = LoadShader("rain_curtain") };
            _curtainMaterial.SetShaderParameter("fall_speed", FallSpeed);
            _curtainMaterial.SetShaderParameter("curtain_color", _curtainColor);
            _curtain = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = CurtainRadius,
                    BottomRadius = CurtainRadius,
                    Height = 30f,
                    RadialSegments = 48,
                    Rings = 1,
                    CapTop = false,
                    CapBottom = false,
                    Material = _curtainMaterial,
                },
                Position = new Vector3(0f, 6f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                GIMode = GeometryInstance3D.GIModeEnum.Disabled,
                Layers = RenderLayers.Weather,
            };
            AddChild(_curtain);
        }

        if (SplashMistEnabled)
        {
            _splashMistMaterial = new FogMaterial
            {
                Density = 0f,
                Albedo = new Color(0.85f, 0.88f, 0.92f),
                HeightFalloff = 2.5f, // densest at the ground, thinning out upward
                EdgeFade = 0.4f,
            };
            _splashMist = new FogVolume
            {
                Shape = RenderingServer.FogVolumeShape.Box,
                // Larger than the rain area with soft edges, so its 2 m position steps never show.
                Size = new Vector3(AreaSize * 1.5f, 1.2f, AreaSize * 1.5f),
                TopLevel = true,
                Material = _splashMistMaterial,
            };
            AddChild(_splashMist);
        }

        _collision = new GpuParticlesCollisionHeightField3D
        {
            Size = new Vector3(AreaSize, EmitHeight + 12f, AreaSize),
            Resolution = CollisionResolution,
            UpdateMode = GpuParticlesCollisionHeightField3D.UpdateModeEnum.WhenMoved,
            FollowCameraEnabled = true,
            // CullMask = which particles collide; HeightfieldMask = which geometry is recorded.
            CullMask = RenderLayers.Weather,
            HeightfieldMask = RenderLayers.All & ~(RenderLayers.Weather | RenderLayers.WeatherCapture | RenderLayers.Viewmodel),
        };
        AddChild(_collision);
    }

    private static Shader LoadShader(string name) => GD.Load<Shader>($"res://addons/gamekit_weather/shaders/{name}.gdshader");

    private static GpuParticles3D MakeParticles(int amount, float lifetime, ParticleProcessMaterial process) => new()
    {
        Amount = amount,
        Lifetime = lifetime,
        LocalCoords = false,
        FixedFps = 0,
        ProcessMaterial = process,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        Layers = RenderLayers.Weather,
    };

    private static ShaderMaterial MakeImpactMaterial(int mode)
    {
        var material = new ShaderMaterial { Shader = LoadShader("rain_impact") };
        material.SetShaderParameter("mode", mode);
        material.SetShaderParameter("lifetime", ImpactLifetime);
        return material;
    }

    /// <summary>Open cylinder for the crown sheet; UV.x = angle (0..1), UV.y = height (0..1). The shader
    /// shapes and animates it.</summary>
    private static ArrayMesh MakeCrownSheetMesh(int segments, Material material)
    {
        var vertices = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[vertices.Length];
        var indices = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float u = (float)i / segments;
            for (int v = 0; v < 2; v++)
            {
                vertices[i * 2 + v] = new Vector3(Mathf.Cos(u * Mathf.Tau), v, Mathf.Sin(u * Mathf.Tau));
                uvs[i * 2 + v] = new Vector2(u, v);
            }
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            indices[i * 6 + 0] = a;
            indices[i * 6 + 1] = a + 2;
            indices[i * 6 + 2] = a + 1;
            indices[i * 6 + 3] = a + 1;
            indices[i * 6 + 4] = a + 2;
            indices[i * 6 + 5] = a + 3;
        }
        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        mesh.CustomAabb = new Aabb(new Vector3(-0.2f, -0.05f, -0.2f), new Vector3(0.4f, 0.3f, 0.4f));
        return mesh;
    }

    /// <summary>One small quad per droplet; COLOR.r is the droplet index (0..1) for the shader.</summary>
    private static ArrayMesh MakeCrownMesh(int droplets, Material material)
    {
        Vector2[] corners = { new(-0.5f, -0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f), new(-0.5f, 0.5f) };
        var vertices = new Vector3[droplets * 4];
        var uvs = new Vector2[droplets * 4];
        var colors = new Color[droplets * 4];
        var indices = new int[droplets * 6];
        for (int i = 0; i < droplets; i++)
        {
            var indexColor = new Color((float)i / droplets, 0f, 0f);
            int b = i * 4;
            for (int c = 0; c < 4; c++)
            {
                vertices[b + c] = new Vector3(corners[c].X, corners[c].Y, 0f);
                uvs[b + c] = corners[c] + new Vector2(0.5f, 0.5f);
                colors[b + c] = indexColor;
            }
            int k = i * 6;
            indices[k + 0] = b;
            indices[k + 1] = b + 1;
            indices[k + 2] = b + 2;
            indices[k + 3] = b;
            indices[k + 4] = b + 2;
            indices[k + 5] = b + 3;
        }
        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        // The shader moves droplets up to ~0.4 m from the particle; keep them from being culled.
        mesh.CustomAabb = new Aabb(new Vector3(-0.5f, -0.1f, -0.5f), new Vector3(1f, 0.6f, 1f));
        return mesh;
    }

    private void UpdateMotion()
    {
        if (_processMaterial == null || _drops == null)
            return;
        var velocity = new Vector3(_wind.X, -FallSpeed, _wind.Z);
        _processMaterial.Direction = velocity.Normalized();
        _processMaterial.InitialVelocityMin = velocity.Length() * 0.95f;
        _processMaterial.InitialVelocityMax = velocity.Length() * 1.05f;
        _curtainMaterial?.SetShaderParameter("wind", new Vector2(_wind.X, _wind.Z));
        // Spawn upwind so slanted rain still lands around the camera.
        var horizontal = new Vector3(_wind.X, 0f, _wind.Z);
        _drops.Position = new Vector3(0f, EmitHeight, 0f) - horizontal * (EmitHeight / FallSpeed);
    }

    private void UpdateIntensity()
    {
        if (_drops == null)
            return;
        bool active = _intensity > 0.001f;
        _drops.AmountRatio = _intensity;
        _impacts.AmountRatio = _intensity;
        _drops.Emitting = active;
        if (_curtain != null)
        {
            _curtain.Visible = active;
            _curtainMaterial!.SetShaderParameter("intensity", _intensity);
        }
        if (_splashMist != null)
        {
            _splashMist.Visible = active;
            // Spray grows faster than linearly: light rain barely mists, heavy rain does.
            _splashMistMaterial!.Density = SplashMistDensity * _intensity * _intensity;
        }
    }
}
