namespace GameKit.Core;

/// <summary>
/// Visual (render) layer bits used by more than one system. Cameras render all layers unless noted; the
/// planar water reflection leaves out the water itself and the first-person viewmodel.
/// </summary>
public static class RenderLayers
{
    /// <summary>Layer 1: ordinary world geometry (Godot's default).</summary>
    public const uint World = 1u << 0;

    /// <summary>Layer 17: the rain occlusion capture pass; only its top-down camera renders it.</summary>
    public const uint WeatherCapture = 1u << 16;

    /// <summary>Layer 18: rain particles and curtains (not recorded as cover or as rain colliders).</summary>
    public const uint Weather = 1u << 17;

    /// <summary>Layer 19: first-person viewmodel (weapons, arms).</summary>
    public const uint Viewmodel = 1u << 18;

    /// <summary>Layer 20: water surfaces and the underwater view pass.</summary>
    public const uint Water = 1u << 19;

    /// <summary>All 20 visual layers.</summary>
    public const uint All = (1u << 20) - 1;
}
