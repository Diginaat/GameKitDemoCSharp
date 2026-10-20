using GameKit.Lens;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Places the camera rig every rendered frame. The rig is top-level with physics interpolation off:
/// its position comes from the body's interpolated transform plus the eye height, and its rotation
/// straight from the controller's view angles, so aiming has zero added latency while position stays
/// smooth at any frame rate.
///
/// Eye height (crouch) and stair-step smoothing are advanced per physics tick and interpolated with
/// the same fraction as the body, so they cancel the body's discontinuities instead of fighting them.
/// </summary>
public partial class PlayerCameraController : Node3D
{
    private FpsPlayerController _player = null!;
    private Node3D _pitch = null!;

    private float _eyePrev, _eyeCurr;
    private float _stepPrev, _stepCurr;

    /// <summary>Current rendered eye position (world), useful for interaction rays.</summary>
    public Camera3D Camera { get; private set; } = null!;
    /// <summary>The camera's photographic finish (child of <see cref="Camera"/>), if the scene has one.</summary>
    public CameraLens? Lens { get; private set; }
    private PlayerCameraSettings? _lensFrom;

    public override void _Ready()
    {
        _player = GetParent<FpsPlayerController>();
        _pitch = GetNode<Node3D>("Pitch");
        Camera = GetNode<Camera3D>("Pitch/Effects/Camera3D");
        foreach (Node child in Camera.GetChildren())
            Lens ??= child as CameraLens;
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        // Run before the player each physics tick so offsets added by this tick's events are interpolated from zero.
        ProcessPhysicsPriority = -10;

        _eyePrev = _eyeCurr = _player.Motor.Stance.EyeHeight;
        _player.Motor.VisualStep += dy => _stepCurr -= dy;
        // Airborne crouch moves the feet; shift the eye the other way so the view does not jump.
        _player.Motor.Stance.Changed += (_, feetShift) => _eyeCurr -= feetShift;
        Camera.Fov = _player.CameraSettings.VerticalFov;
        Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var cam = _player.CameraSettings;
        if (!ReferenceEquals(cam, _lensFrom))
        {
            // Settings swapped (profile / reduced motion): apply its lens look.
            _lensFrom = cam;
            if (Lens != null && cam.Lens != null)
                Lens.Profile = cam.Lens;
        }
        _eyePrev = _eyeCurr;
        _stepPrev = _stepCurr;
        _eyeCurr = Mathf.Lerp(_eyeCurr, _player.Motor.Stance.EyeHeight, MoveMath.SmoothFactor(cam.CrouchSmoothingRate, dt));
        float maxLag = _player.Movement.StepHeight * 1.5f;
        _stepCurr = Mathf.Clamp(_stepCurr * (1f - MoveMath.SmoothFactor(cam.StepSmoothingRate, dt)), -maxLag, maxLag);
    }

    public override void _Process(double delta)
    {
        float f = (float)Engine.GetPhysicsInterpolationFraction();
        if (!GetTree().PhysicsInterpolation)
            f = 1f;
        float eye = Mathf.Lerp(_eyePrev, _eyeCurr, f) + Mathf.Lerp(_stepPrev, _stepCurr, f);
        var origin = _player.GetGlobalTransformInterpolated().Origin;
        GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(0f, _player.ViewYaw, 0f)), origin + Vector3.Up * eye);
        _pitch.Rotation = new Vector3(_player.ViewPitch, 0f, 0f);
    }

    /// <summary>Snaps smoothing after teleports so the camera does not glide.</summary>
    public void ResetSmoothing()
    {
        _eyePrev = _eyeCurr = _player.Motor.Stance.EyeHeight;
        _stepPrev = _stepCurr = 0f;
    }
}
