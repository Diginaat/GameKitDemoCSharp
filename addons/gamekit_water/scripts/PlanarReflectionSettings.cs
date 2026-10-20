using Godot;

namespace GameKit.Water;

/// <summary>
/// Tunables for <see cref="PlanarReflection"/> (the pool mirrored in the water's underside). Edits apply
/// live, also while the game runs (each setter raises <see cref="Resource.Changed"/>).
/// </summary>
[GlobalClass]
public partial class PlanarReflectionSettings : Resource
{
    private bool _enabled = true;
    private float _strength = 1f;
    private float _resolutionScale = 0.5f;
    private float _distortion = 0.04f;
    private float _edgeFade = 0.05f;
    private Color _fogColor = new(0.1f, 0.42f, 0.47f);
    private float _fogDensity = 0.2f;

    /// <summary>Off: no extra render; the underside shows the plain water colour.</summary>
    [Export] public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    /// <summary>Blend of the reflection over the plain water colour (0..1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Strength { get => _strength; set => Set(ref _strength, value); }

    /// <summary>Reflection render size relative to the screen; higher is sharper and costlier.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float ResolutionScale { get => _resolutionScale; set => Set(ref _resolutionScale, value); }

    /// <summary>How far waves bend the reflection, in reflection-texture units.</summary>
    [Export(PropertyHint.Range, "0,0.2,0.005")] public float Distortion { get => _distortion; set => Set(ref _distortion, value); }

    /// <summary>Fade to the water colour near the reflection's screen edges, in texture units.</summary>
    [Export(PropertyHint.Range, "0.001,0.3,0.005")] public float EdgeFade { get => _edgeFade; set => Set(ref _edgeFade, value); }

    /// <summary>Underwater fog of the mirrored path (match underwater_view.gdshader water_color and density).</summary>
    [Export] public Color FogColor { get => _fogColor; set => Set(ref _fogColor, value); }

    [Export(PropertyHint.Range, "0,1,0.01")] public float FogDensity { get => _fogDensity; set => Set(ref _fogDensity, value); }

    private void Set<T>(ref T field, T value)
    {
        field = value;
        EmitChanged();
    }
}
