using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Every gameplay movement tunable. Profiles (Source Classic, Modern AAA) are saved instances in
/// <c>res://addons/gamekit_fps_controller/resources/</c>; both run through the same motor. Defaults equal the Modern AAA profile.
/// Units are meters, seconds, kilograms. Source values convert at 1 unit = 1 inch (0.0254 m), which gives
/// the 72-unit player hull a realistic 1.83 m height.
/// </summary>
[GlobalClass]
public partial class PlayerMovementSettings : Resource
{
    [Export] public string ProfileName { get; set; } = "Modern AAA";

    [ExportGroup("Body")]
    [Export] public float Radius { get; set; } = 0.4f;
    [Export] public float StandingHeight { get; set; } = 1.83f;
    [Export] public float CrouchHeight { get; set; } = 1.0f;
    [Export] public float StandingEyeHeight { get; set; } = 1.65f;
    [Export] public float CrouchEyeHeight { get; set; } = 0.85f;

    [ExportGroup("Speeds")]
    [Export] public float WalkSpeed { get; set; } = 2.4f;
    [Export] public float RunSpeed { get; set; } = 4.6f;
    [Export] public float SprintSpeed { get; set; } = 7.0f;
    [Export] public float CrouchSpeed { get; set; } = 2.2f;

    [ExportGroup("Ground")]
    /// <summary>Source sv_accelerate. Time to reach wish speed from rest = 1 / acceleration (seconds).</summary>
    [Export] public float GroundAcceleration { get; set; } = 11f;
    /// <summary>Source sv_friction. Exponential decay rate above StopSpeed (1/s).</summary>
    [Export] public float Friction { get; set; } = 6f;
    /// <summary>Source sv_stopspeed. Below this speed friction decelerates at Friction * StopSpeed.</summary>
    [Export] public float StopSpeed { get; set; } = 2.0f;

    [ExportGroup("Air")]
    [Export] public float Gravity { get; set; } = 17f;
    [Export] public float AirAcceleration { get; set; } = 4f;
    /// <summary>Source's 30-unit air wish-speed cap. Bigger = more air control, smaller = more strafe-gain.</summary>
    [Export] public float AirSpeedCap { get; set; } = 0.76f;
    [Export] public float MaxFallSpeed { get; set; } = 55f;

    [ExportGroup("Jump")]
    [Export] public float JumpHeight { get; set; } = 1.0f;
    [Export] public bool VariableJumpHeight { get; set; } = true;
    /// <summary>Upward speed is multiplied by this when jump is released while rising.</summary>
    [Export(PropertyHint.Range, "0,1")] public float JumpCutMultiplier { get; set; } = 0.55f;
    [Export] public float JumpBufferTime { get; set; } = 0.12f;
    [Export] public float CoyoteTime { get; set; } = 0.1f;
    /// <summary>Jumping on the landing tick skips friction, preserving speed (classic bunny hop).</summary>
    [Export] public bool AllowBunnyHop { get; set; } = false;
    /// <summary>Holding jump re-jumps on landing.</summary>
    [Export] public bool AutoBunnyHop { get; set; } = false;
    /// <summary>When bunny hop is off, horizontal speed is capped to this multiple of the current max ground speed at takeoff (HL2 PreventBunnyJumping uses 1.1).</summary>
    [Export] public float BunnyHopSpeedCap { get; set; } = 1.0f;

    [ExportGroup("Sprint and Crouch")]
    [Export] public bool SprintToggle { get; set; } = false;
    /// <summary>Sprint only while the forward input exceeds this (1 = straight forward only, -1 = any direction).</summary>
    [Export(PropertyHint.Range, "-1,1")] public float SprintMinForwardInput { get; set; } = 0.5f;
    [Export] public bool CrouchToggle { get; set; } = false;

    [ExportGroup("Collision")]
    [Export] public float StepHeight { get; set; } = 0.4f;
    [Export] public float MaxSlopeDegrees { get; set; } = 46f;
    /// <summary>Cap on platform velocity added when leaving a moving platform (prevents launches).</summary>
    [Export] public float MaxPlatformInheritSpeed { get; set; } = 8f;
    /// <summary>Maximum force the player can push props with (N).</summary>
    [Export] public float PushForce { get; set; } = 900f;
    /// <summary>Props heavier than this are not pushed at all (kg).</summary>
    [Export] public float MaxPushMass { get; set; } = 250f;

    [ExportGroup("Slide")]
    [Export] public bool SlideEnabled { get; set; } = true;
    [Export] public float SlideMinStartSpeed { get; set; } = 5.5f;
    [Export] public float SlideStartBoost { get; set; } = 1.5f;
    [Export] public float SlideFriction { get; set; } = 0.9f;
    [Export] public float SlideEndSpeed { get; set; } = 2.6f;
    [Export] public float SlideMaxDuration { get; set; } = 1.1f;
    [Export] public float SlideCooldown { get; set; } = 0.4f;

    [ExportGroup("Mantle")]
    [Export] public bool MantleEnabled { get; set; } = true;
    [Export] public float MantleMinHeight { get; set; } = 0.5f;
    /// <summary>Also limits climbing out of water: a floating player's feet are about eye height below the surface.</summary>
    [Export] public float MantleMaxHeight { get; set; } = 1.8f;
    [Export] public float MantleReach { get; set; } = 0.6f;
    [Export] public float MantleSpeed { get; set; } = 4.5f;

    [ExportGroup("Ladder")]
    [Export] public bool LadderEnabled { get; set; } = true;
    [Export] public float LadderClimbSpeed { get; set; } = 3.0f;
    [Export] public float LadderJumpOffSpeed { get; set; } = 4.0f;

    [ExportGroup("Swim")]
    [Export] public bool SwimEnabled { get; set; } = true;
    [Export] public float SwimSpeed { get; set; } = 3.2f;
    [Export] public float SwimAcceleration { get; set; } = 6f;
    [Export] public float WaterFriction { get; set; } = 2.5f;
    /// <summary>Fraction of standing height that must be submerged to swim.</summary>
    [Export(PropertyHint.Range, "0,1")] public float SwimDepthFraction { get; set; } = 0.55f;
}
