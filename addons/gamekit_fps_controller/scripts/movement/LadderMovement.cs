using GameKit.World;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Source-style ladders. Inside a <see cref="LadderVolume"/>, pushing toward the ladder grabs it.
/// Forward input climbs in the direction you look (look down + forward descends), back input descends,
/// strafe moves sideways. Jump pushes off; leaving the top of the volume while climbing hops onto the ledge.
/// </summary>
[GlobalClass]
public sealed partial class LadderMovement : MovementAbility
{
    private const float LookDownDescendDegrees = -30f;

    private LadderVolume? _ladder;
    private int _overlaps;
    private Vector3 _lastInward;

    public override MovementState State => MovementState.Climbing;

    public void EnterVolume(LadderVolume ladder)
    {
        _ladder = ladder;
        _overlaps++;
    }

    public void ExitVolume(LadderVolume ladder)
    {
        _overlaps = Mathf.Max(0, _overlaps - 1);
        if (_overlaps == 0)
            _ladder = null;
    }

    public override bool CanBegin(PlayerMotor motor, in PlayerCommand cmd)
    {
        if (!motor.Settings.LadderEnabled || _ladder == null || cmd.JumpHeld)
            return false;
        motor.WishMovement(cmd, out var wishDir, out _);
        // Pushing toward the ladder face grabs it; standing at the bottom and backing away does not.
        return wishDir.Dot(-_ladder.OutwardNormal) > 0.3f;
    }

    public override void Begin(PlayerMotor motor, in PlayerCommand cmd)
    {
        motor.Velocity = Vector3.Zero;
        motor.ConsumeJumpBuffer();
    }

    public override bool Tick(PlayerMotor motor, in PlayerCommand cmd, float dt)
    {
        var s = motor.Settings;
        if (_ladder == null)
        {
            // Climbed past the top of the volume: hop forward onto the ledge.
            if (motor.Velocity.Y > 0.1f)
                motor.Velocity = Vector3.Up * 2.5f + _lastInward * 2.5f;
            return false;
        }

        var normal = _ladder.OutwardNormal;
        _lastInward = -normal;
        if (cmd.JumpPressed)
        {
            motor.ConsumeJumpBuffer();
            motor.Velocity = normal * s.LadderJumpOffSpeed + Vector3.Up * 2f;
            return false;
        }

        float climb = cmd.Move.Y;
        if (climb > 0f && Mathf.RadToDeg(cmd.Pitch) < LookDownDescendDegrees)
            climb = -climb;
        var right = Vector3.Up.Cross(normal).Normalized();
        float side = cmd.Move.X;

        // Stepping off at the bottom.
        if (motor.IsOnFloor && climb < 0f)
            return false;

        motor.Velocity = Vector3.Up * (climb * s.LadderClimbSpeed)
            - right * (side * s.LadderClimbSpeed * 0.5f)
            - normal * 0.4f; // keeps contact with the ladder face
        motor.MoveBody(dt, tryStep: false);
        return true;
    }

    public override void End(PlayerMotor motor) { }
}
