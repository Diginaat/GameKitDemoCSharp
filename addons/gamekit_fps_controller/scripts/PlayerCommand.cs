using Godot;

namespace GameKit.FpsController;

/// <summary>
/// One physics tick of player intent, analogous to Source's <c>CUserCmd</c>.
/// Produced by <see cref="PlayerInput"/> from devices, or by tests/AI, and consumed by
/// <see cref="PlayerMotor"/>. Keeping intent separate from devices makes movement
/// deterministic and scriptable.
/// </summary>
public struct PlayerCommand
{
    /// <summary>x = strafe right, y = forward. Length is clamped to 1 (diagonals are normalized).</summary>
    public Vector2 Move;

    /// <summary>View angles in radians at the time of this tick.</summary>
    public float Yaw;
    public float Pitch;

    public bool JumpHeld;
    public bool JumpPressed;
    public bool CrouchHeld;
    public bool CrouchPressed;
    public bool SprintHeld;
    public bool SprintPressed;
    public bool WalkHeld;
}
