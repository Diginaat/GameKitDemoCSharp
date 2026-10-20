using GameKit.Core;
using Godot;

namespace GameKit.Water;

/// <summary>
/// Planar reflection of a water's underside (total internal reflection seen from below). While the
/// active camera is under the water, a camera mirrored across the still surface renders the scene into
/// a half-resolution viewport; the water material samples it by projecting each surface point with the
/// mirrored camera. The mirrored view leaves out the water and the viewmodel, and is fogged like the
/// underwater path it stands for. Nothing renders while the camera is above the water or when disabled.
/// Tuned by <see cref="Settings"/>.
/// </summary>
[GlobalClass]
public partial class PlanarReflection : Node
{
    private static readonly StringName TextureParam = "reflection_tex";
    private static readonly StringName ViewProjectionParam = "reflection_view_projection";
    private static readonly StringName StrengthParam = "reflection_strength";
    private static readonly StringName DistortionParam = "reflection_distortion";
    private static readonly StringName EdgeFadeParam = "reflection_edge_fade";

    private PlanarReflectionSettings _settings = new();
    private Environment? _environment;

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;

    [Export] public WaterVolume? Water { get; set; }
    [Export] public ShaderMaterial? Material { get; set; }

    [Export]
    public PlanarReflectionSettings Settings
    {
        get => _settings;
        set
        {
            // Subscribed only while in the tree (_EnterTree/_ExitTree), so a swap re-subscribes here.
            bool live = IsInsideTree();
            if (live)
                _settings.Changed -= Apply;
            _settings = value ?? new PlanarReflectionSettings();
            if (live)
            {
                _settings.Changed += Apply;
                Apply();
            }
        }
    }

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Name = "Viewport",
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            Size = new Vector2I(2, 2),
        };
        _camera = new Camera3D
        {
            Name = "MirrorCamera",
            CullMask = RenderLayers.All & ~(RenderLayers.Water | RenderLayers.Viewmodel),
            Current = true,
        };
        _viewport.AddChild(_camera);
        AddChild(_viewport);
        if (GetViewport().FindWorld3D()?.Environment is { } world)
        {
            _environment = (Environment)world.Duplicate();
            _environment.FogEnabled = true;
            _environment.FogMode = Environment.FogModeEnum.Exponential;
            _environment.FogLightEnergy = 1f;
            _environment.FogSkyAffect = 0f;
            _environment.FogSunScatter = 0f;
            _camera.Environment = _environment;
        }
        Material?.SetShaderParameter(TextureParam, _viewport.GetTexture());
        Apply();
    }

    public override void _EnterTree() => _settings.Changed += Apply;

    public override void _ExitTree() => _settings.Changed -= Apply;

    private void Apply()
    {
        var s = _settings;
        if (_environment != null)
        {
            _environment.FogLightColor = s.FogColor;
            _environment.FogDensity = s.FogDensity;
        }
        if (Material == null)
            return;
        Material.SetShaderParameter(StrengthParam, s.Enabled ? s.Strength : 0f);
        Material.SetShaderParameter(DistortionParam, s.Distortion);
        Material.SetShaderParameter(EdgeFadeParam, s.EdgeFade);
    }

    public override void _Process(double delta)
    {
        var main = GetViewport().GetCamera3D();
        bool active = _settings.Enabled && _settings.Strength > 0f && Water != null && Material != null
            && main != null && IsUnderwater(main.GlobalPosition);
        _viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        if (!active)
            return;

        var size = (Vector2I)(GetViewport().GetVisibleRect().Size * _settings.ResolutionScale);
        if (_viewport.Size != size)
            _viewport.Size = size;

        // Mirror across the plane y = level. Projecting points (not copying the image) makes the handedness
        // flip of a mirror irrelevant: the shader looks up where the mirrored camera saw each surface point.
        float level = Water!.SurfaceY;
        var t = main!.GlobalTransform;
        var origin = t.Origin with { Y = 2f * level - t.Origin.Y };
        var forward = -t.Basis.Z;
        var up = t.Basis.Y;
        forward.Y = -forward.Y;
        up.Y = -up.Y;
        _camera.GlobalTransform = new Transform3D(Basis.LookingAt(forward, up), origin);
        _camera.Fov = main.Fov;
        _camera.KeepAspect = main.KeepAspect;
        _camera.Near = main.Near;
        _camera.Far = main.Far;

        var viewProjection = _camera.GetCameraProjection() * new Projection(_camera.GlobalTransform.AffineInverse());
        Material!.SetShaderParameter(ViewProjectionParam, viewProjection);
    }

    private bool IsUnderwater(Vector3 point) => point.Y < Water!.SurfaceY && Water.Bounds.HasPoint(point);
}
