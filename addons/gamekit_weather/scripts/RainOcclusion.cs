using GameKit.Core;
using Godot;

namespace GameKit.Weather;

/// <summary>
/// Rain occlusion map: a top-down height map around the camera, so surfaces under a roof, an overhang or
/// a table stay dry.
///
/// A small orthographic camera in a SubViewport renders the scene from above; a full-screen pass turns
/// its depth into world height (float texture). The wetness shader (weather_wetness.gdshaderinc) compares
/// each surface with the highest surface above it via the <c>weather_rain_occlusion</c> and
/// <c>weather_occlusion_area</c> shader globals.
///
/// Cost: the map re-renders only when the followed node moves a grid step (snapped, so it never
/// shimmers), not every frame. Call <see cref="Refresh"/> after moving a cover.
/// </summary>
[GlobalClass]
public partial class RainOcclusion : Node3D
{
    /// <summary>Node to center the map on. Defaults to the active camera.</summary>
    [Export] public Node3D? FollowTarget { get; set; }
    /// <summary>Width of the covered square in meters.</summary>
    [Export] public float AreaSize { get; set; } = 64f;
    /// <summary>Map resolution in pixels (AreaSize / Resolution = meters per texel).</summary>
    [Export(PropertyHint.Enum, "256:256,512:512,1024:1024")] public int Resolution { get; set; } = 512;
    /// <summary>Re-render after moving this far (meters). Also the snapping grid.</summary>
    [Export] public float UpdateStep { get; set; } = 4f;
    /// <summary>Capture from this high above the followed node.</summary>
    [Export] public float CaptureHeight { get; set; } = 60f;
    /// <summary>Render layers that count as cover. Rain, water, the viewmodel and the capture pass don't.</summary>
    [Export(PropertyHint.Layers3DRender)]
    public uint CoverLayers { get; set; } =
        RenderLayers.All & ~(RenderLayers.Weather | RenderLayers.WeatherCapture | RenderLayers.Viewmodel | RenderLayers.Water);

    private static readonly StringName OcclusionGlobal = "weather_rain_occlusion";
    private static readonly StringName AreaGlobal = "weather_occlusion_area";

    private SubViewport _viewport = null!;
    private Camera3D _camera = null!;
    private Vector2 _center = new(float.PositiveInfinity, float.PositiveInfinity);

    public override void _Ready()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _viewport = new SubViewport
        {
            Size = new Vector2I(Resolution, Resolution),
            UseHdr2D = true, // float texture: exact heights in meters
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            // Exact depth: no upscaler, no temporal AA jitter, no AA blur.
            Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear,
            Scaling3DScale = 1f,
            UseTaa = false,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            PositionalShadowAtlasSize = 0,
        };
        AddChild(_viewport);

        _camera = new Camera3D
        {
            // Never interpolate: a blend between poses tilts the capture and misplaces covers.
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = AreaSize,
            Near = 0.5f,
            Far = CaptureHeight + 200f,
            CullMask = CoverLayers | RenderLayers.WeatherCapture,
            Rotation = new Vector3(-Mathf.Pi * 0.5f, 0f, 0f), // straight down; screen up = world -Z
            // Plain output: the scene's tonemap, fog, glow and exposure must not touch the heights.
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(-1000f, -1000f, -1000f),
                TonemapMode = Godot.Environment.ToneMapper.Linear,
            },
            Attributes = new CameraAttributesPractical(),
        };
        _viewport.AddChild(_camera);

        var material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://addons/gamekit_weather/shaders/rain_occlusion_capture.gdshader"),
        };
        var quad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Material = material },
            Layers = RenderLayers.WeatherCapture,
            ExtraCullMargin = 16384f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Position = new Vector3(0f, 0f, -1f),
        };
        _camera.AddChild(quad);

        RenderingServer.GlobalShaderParameterSet(OcclusionGlobal, _viewport.GetTexture());
    }

    public override void _ExitTree() => RenderingServer.GlobalShaderParameterSet(AreaGlobal, Vector4.Zero);

    public override void _Process(double delta)
    {
        Node3D? target = FollowTarget ?? GetViewport().GetCamera3D();
        if (target == null || target == _camera)
            return;
        Vector3 p = target.GlobalPosition;
        Vector2 snapped = new Vector2(p.X, p.Z).Snapped(new Vector2(UpdateStep, UpdateStep));
        if (snapped == _center)
            return;
        _center = snapped;
        _camera.GlobalPosition = new Vector3(_center.X, p.Y + CaptureHeight, _center.Y);
        _camera.ResetPhysicsInterpolation();
        Refresh();
    }

    /// <summary>The current height map as an image (float heights in the red channel).</summary>
    public Image GetHeightImage() => _viewport.GetTexture().GetImage();

    /// <summary>Re-renders the map now (e.g. after building or moving a cover).</summary>
    public void Refresh()
    {
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        RenderingServer.GlobalShaderParameterSet(AreaGlobal, new Vector4(_center.X, _center.Y, AreaSize, 1f));
    }
}
