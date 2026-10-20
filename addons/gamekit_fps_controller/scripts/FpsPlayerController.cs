using GameKit.Interaction;
using GameKit.Water;
using GameKit.World;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Root of the first-person player. Owns the CharacterBody3D configuration, the view angles and the
/// <see cref="PlayerMotor"/>, and runs the simulation once per physics tick. Presentation (camera,
/// viewmodel, audio, interaction) lives in child nodes that read state and subscribe to motor events.
/// Water, ladders and the interaction ray reach the player only through the GameKit interfaces below.
/// </summary>
[GlobalClass]
public partial class FpsPlayerController : CharacterBody3D, IWaterUser, ILadderUser, IInteractionViewer
{
    [Export] public PlayerMovementSettings Movement { get; set; } = new();
    [Export] public PlayerCameraSettings CameraSettings { get; set; } = new();

    /// <summary>When true, <see cref="PlayerInput"/> is ignored and <see cref="Command"/> is supplied externally (tests, AI, cutscenes).</summary>
    public bool UseExternalCommand { get; set; }

    /// <summary>The intent simulated on the next physics tick.</summary>
    public PlayerCommand Command;

    public float ViewYaw { get; private set; }
    public float ViewPitch { get; private set; }

    private Node? _abilities;
    private readonly System.Collections.Generic.List<MovementAbility> _abilityBuffer = new();

    public PlayerMotor Motor { get; private set; } = null!;
    public PlayerInput? Input { get; private set; }
    public InteractionRay? Interaction { get; private set; }

    /// <summary>Wall-clock cost of the last simulation tick, for the debug overlay (microseconds).</summary>
    public double LastTickMicroseconds { get; private set; }

    public override void _EnterTree()
    {
        // Built here rather than in _Ready so child components can subscribe to motor events in their own _Ready.
        if (Motor != null)
            return;
        var shape = GetNode<CollisionShape3D>("CollisionShape3D");
        Motor = new PlayerMotor(this, shape, GetNodeOrNull<RayCast3D>("GroundCheck"));
        ApplyBodySettings();
        _abilities = GetNodeOrNull("Abilities");
        if (_abilities != null)
        {
            _abilities.ChildOrderChanged += CollectAbilities;
            _abilities.ChildEnteredTree += _ => CollectAbilities();
            _abilities.ChildExitingTree += OnAbilityExiting;
        }
        CollectAbilities();
    }

    public override void _Ready()
    {
        Input = GetNodeOrNull<PlayerInput>("Input");
        Interaction = GetNodeOrNull<InteractionRay>("Interaction");
        SetLook(Rotation.Y, 0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!UseExternalCommand && Input != null)
        {
            Command = Input.BuildCommand();
            if (Interaction != null && Input.MouseCaptured)
            {
                if (Godot.Input.IsActionJustPressed(InputActions.Interact))
                    Interaction.Use();
                else if (Interaction.Held != null && Godot.Input.IsActionJustPressed(InputActions.Fire))
                    Interaction.Throw();
            }
        }
        Command.Yaw = ViewYaw;
        Command.Pitch = ViewPitch;
        Motor.Tick(Command, (float)delta);
        if (UseExternalCommand)
        {
            // Edge-triggered inputs last one tick.
            Command.JumpPressed = Command.CrouchPressed = Command.SprintPressed = false;
        }
        LastTickMicroseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMicroseconds;
    }

    /// <summary>Sets view angles (radians). Pitch is clamped to the camera limit.</summary>
    public void SetLook(float yaw, float pitch)
    {
        float limit = Mathf.DegToRad(CameraSettings.MaxPitchDegrees);
        ViewYaw = Mathf.Wrap(yaw, -Mathf.Pi, Mathf.Pi);
        ViewPitch = Mathf.Clamp(pitch, -limit, limit);
    }

    public void AddLook(float yawDelta, float pitchDelta) => SetLook(ViewYaw + yawDelta, ViewPitch + pitchDelta);

    /// <summary>Swaps the movement profile at runtime. The same motor runs every profile.</summary>
    public void ApplyMovementSettings(PlayerMovementSettings settings)
    {
        Movement = settings;
        ApplyBodySettings();
        Motor.Stance.Configure(settings);
    }

    // ---- GameKit interfaces -----------------------------------------------------------------

    public void OnWaterEntered(WaterVolume water) => Motor.GetAbility<SwimMovement>()?.EnterVolume(water);

    public void OnWaterExited(WaterVolume water) => Motor.GetAbility<SwimMovement>()?.ExitVolume(water);

    public (Vector3 Center, float HalfHeight, float Radius) WaterExtent
    {
        get
        {
            float height = Motor.Stance.Height;
            return (GlobalPosition + Vector3.Up * height * 0.5f, height * 0.5f, Movement.Radius);
        }
    }

    public void OnLadderEntered(LadderVolume ladder) => Motor.GetAbility<LadderMovement>()?.EnterVolume(ladder);

    public void OnLadderExited(LadderVolume ladder) => Motor.GetAbility<LadderMovement>()?.ExitVolume(ladder);

    public Vector3 ViewOrigin => GlobalPosition + Vector3.Up * Motor.Stance.EyeHeight;

    public Basis ViewBasis => Basis.FromEuler(new Vector3(ViewPitch, ViewYaw, 0f));

    public Vector3 ViewerVelocity => Motor.Velocity;

    private void ApplyBodySettings()
    {
        MotionMode = MotionModeEnum.Grounded;
        UpDirection = Vector3.Up;
        FloorMaxAngle = Mathf.DegToRad(Movement.MaxSlopeDegrees);
        // Snap follows stairs down, and must exceed the motor's pre-move floor lift.
        FloorSnapLength = Mathf.Max(Movement.StepHeight, PlayerMotor.GroundLift * 2f);
        FloorStopOnSlope = true;
        FloorConstantSpeed = true;
        FloorBlockOnWall = true;
        // The motor adds a clamped platform velocity itself; letting the engine add it too would double it.
        PlatformOnLeave = PlatformOnLeaveEnum.DoNothing;
        PlatformFloorLayers = uint.MaxValue;
    }

    /// <summary>
    /// Abilities are the <see cref="MovementAbility"/> children of the <c>Abilities</c> node, in child order
    /// (priority). Re-read whenever children are added, removed or moved.
    /// </summary>
    private void CollectAbilities()
    {
        _abilityBuffer.Clear();
        if (_abilities != null)
            foreach (var child in _abilities.GetChildren())
                if (child is MovementAbility ability && !ability.IsQueuedForDeletion())
                    _abilityBuffer.Add(ability);
        Motor.SetAbilities(_abilityBuffer);
    }

    private void OnAbilityExiting(Node child)
    {
        // Still a child while exiting: rebuild without it.
        _abilityBuffer.Clear();
        foreach (var other in _abilities!.GetChildren())
            if (other != child && other is MovementAbility ability)
                _abilityBuffer.Add(ability);
        Motor.SetAbilities(_abilityBuffer);
    }
}
