using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Turns devices into view angles and <see cref="PlayerCommand"/>s. Mouse look is applied to the
/// controller the moment the event arrives (no smoothing, no acceleration), so the camera, which reads
/// the angles every rendered frame, never lags the mouse.
/// </summary>
public partial class PlayerInput : Node
{
    /// <summary>Source's m_yaw/m_pitch: degrees per mouse count at sensitivity 1.</summary>
    private const float DegreesPerCount = 0.022f;

    private FpsPlayerController _player = null!;

    public bool MouseCaptured => Godot.Input.MouseMode == Godot.Input.MouseModeEnum.Captured;

    public override void _Ready()
    {
        InputActions.EnsureDefaults();
        _player = GetParent<FpsPlayerController>();
        if (DisplayServer.GetName() != "headless")
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && MouseCaptured)
        {
            var cam = _player.CameraSettings;
            float scale = Mathf.DegToRad(DegreesPerCount * cam.MouseSensitivity);
            // ScreenRelative is unaffected by window stretch, so sensitivity does not change with resolution.
            var rel = motion.ScreenRelative;
            _player.AddLook(-rel.X * scale, (cam.InvertY ? rel.Y : -rel.Y) * scale);
            GetViewport().SetInputAsHandled();
        }
        else if (e.IsActionPressed(InputActions.ReleaseMouse))
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true } && !MouseCaptured)
        {
            Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Captured;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Samples the current device state. Called from the physics tick, so "just pressed" is per physics frame.</summary>
    public PlayerCommand BuildCommand()
    {
        if (!MouseCaptured)
            return default;
        return new PlayerCommand
        {
            // GetVector clamps the length to 1, which normalizes diagonals.
            Move = Godot.Input.GetVector(InputActions.MoveLeft, InputActions.MoveRight, InputActions.MoveBack, InputActions.MoveForward),
            JumpHeld = Godot.Input.IsActionPressed(InputActions.Jump),
            JumpPressed = Godot.Input.IsActionJustPressed(InputActions.Jump),
            CrouchHeld = Godot.Input.IsActionPressed(InputActions.Crouch),
            CrouchPressed = Godot.Input.IsActionJustPressed(InputActions.Crouch),
            SprintHeld = Godot.Input.IsActionPressed(InputActions.Sprint),
            SprintPressed = Godot.Input.IsActionJustPressed(InputActions.Sprint),
            WalkHeld = Godot.Input.IsActionPressed(InputActions.Walk),
        };
    }
}
