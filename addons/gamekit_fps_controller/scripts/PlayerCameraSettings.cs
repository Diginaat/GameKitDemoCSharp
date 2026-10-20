using GameKit.Lens;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Look input and purely visual camera effects. Nothing here changes gameplay; turning
/// <see cref="MotionEffects"/> off (the accessibility preset) leaves movement identical.
/// </summary>
[GlobalClass]
public partial class PlayerCameraSettings : Resource
{
    [Export] public string PresetName { get; set; } = "Modern";

    [ExportGroup("Look")]
    /// <summary>Source-style sensitivity: degrees per mouse count = 0.022 * sensitivity.</summary>
    [Export] public float MouseSensitivity { get; set; } = 2.5f;
    [Export] public bool InvertY { get; set; } = false;
    [Export(PropertyHint.Range, "45,89.9")] public float MaxPitchDegrees { get; set; } = 89f;
    [Export] public float VerticalFov { get; set; } = 72f;

    [ExportGroup("Smoothing (not motion effects)")]
    /// <summary>Rate (1/s) at which the eye catches up after stair steps.</summary>
    [Export] public float StepSmoothingRate { get; set; } = 14f;
    /// <summary>Rate (1/s) of the eye-height change when crouching.</summary>
    [Export] public float CrouchSmoothingRate { get; set; } = 14f;

    [ExportGroup("Motion Effects")]
    /// <summary>Master switch for every effect below (accessibility).</summary>
    [Export] public bool MotionEffects { get; set; } = true;

    [Export] public bool HeadBob { get; set; } = true;
    [Export] public float BobAmplitude { get; set; } = 0.03f;
    /// <summary>Meters travelled per full bob cycle (two footsteps).</summary>
    [Export] public float BobStrideLength { get; set; } = 2.4f;
    [Export] public float SprintBobScale { get; set; } = 1.5f;

    [Export] public bool LandingKick { get; set; } = true;
    /// <summary>Downward kick (m) per m/s of impact speed.</summary>
    [Export] public float LandingKickPerSpeed { get; set; } = 0.012f;
    [Export] public float MaxLandingKick { get; set; } = 0.16f;
    [Export] public float JumpKick { get; set; } = 0.02f;
    [Export] public float KickStiffness { get; set; } = 170f;
    [Export] public float KickDamping { get; set; } = 22f;

    [Export] public bool StrafeRoll { get; set; } = true;
    [Export] public float MaxRollDegrees { get; set; } = 1.0f;
    [Export] public float RollRate { get; set; } = 8f;

    [Export] public bool AccelerationTilt { get; set; } = true;
    /// <summary>Pitch tilt (degrees) per m/s² of forward acceleration.</summary>
    [Export] public float TiltPerAcceleration { get; set; } = 0.04f;
    [Export] public float MaxTiltDegrees { get; set; } = 1.2f;
    [Export] public float TiltRate { get; set; } = 6f;

    [Export] public bool SprintFov { get; set; } = true;
    [Export] public float SprintFovIncrease { get; set; } = 6f;
    [Export] public float FovRate { get; set; } = 7f;

    [Export] public bool Breathing { get; set; } = true;
    [Export] public float BreathingAmplitude { get; set; } = 0.003f;
    [Export] public float BreathingRate { get; set; } = 0.25f;

    [Export] public bool Recoil { get; set; } = true;
    [Export] public float RecoilStiffness { get; set; } = 120f;
    [Export] public float RecoilDamping { get; set; } = 18f;

    [ExportGroup("Lens")]
    /// <summary>Photographic look of the camera's <see cref="CameraLens"/> (gamekit_lens/profiles/).
    /// Empty = keep the profile set on the Lens node.</summary>
    [Export] public LensProfile? Lens { get; set; }
}
