using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Climbs onto ledges between <see cref="PlayerMovementSettings.MantleMinHeight"/> and MantleMaxHeight
/// while jump and forward are held. Detection sweeps the real capsule: up (headroom), forward (space on
/// top), down (landing), then rays the landing for a walkable normal. The climb itself moves the body
/// with <see cref="PhysicsBody3D.MoveAndCollide"/>, up then forward, and aborts if anything blocks it,
/// so it can never pass through geometry.
/// </summary>
[GlobalClass]
public sealed partial class MantleMovement : MovementAbility
{
    private const float LandingClearance = 0.02f;

    private readonly KinematicCollision3D _hit = new();
    private readonly PhysicsRayQueryParameters3D _ray = new();
    private Vector3 _target;
    private Vector3 _forward;
    private bool _climbing;
    private float _elapsed;

    public override MovementState State => MovementState.Mantling;

    public override bool CanBegin(PlayerMotor motor, in PlayerCommand cmd)
    {
        var s = motor.Settings;
        if (!s.MantleEnabled || cmd.Move.Y < 0.5f || motor.Stance.IsCrouched)
            return false;
        // Grounded: on the jump press. Airborne: while jump is held and not still rising fast.
        bool trigger = motor.IsOnFloor ? cmd.JumpPressed : cmd.JumpHeld && motor.Velocity.Y < motor.JumpVelocity * 0.6f;
        return trigger && FindLedge(motor, cmd);
    }

    private bool FindLedge(PlayerMotor motor, in PlayerCommand cmd)
    {
        var s = motor.Settings;
        var body = motor.Body;
        var from = body.GlobalTransform;
        _forward = Basis.FromEuler(new Vector3(0f, cmd.Yaw, 0f)) * Vector3.Forward;

        // A wall right in front?
        if (!body.TestMove(from, _forward * s.MantleReach, _hit) || _hit.GetNormal().Y > motor.MinFloorNormalY)
            return false;
        float wallDistance = _hit.GetTravel().Length();

        // Headroom straight up.
        float up = body.TestMove(from, Vector3.Up * s.MantleMaxHeight, _hit) ? _hit.GetTravel().Y : s.MantleMaxHeight;
        if (up < s.MantleMinHeight)
            return false;
        var raised = from.Translated(Vector3.Up * up);

        // Over the ledge: move past the wall by the capsule radius so the landing is on top, not on the edge.
        var over = _forward * (wallDistance + s.Radius + 0.05f);
        if (body.TestMove(raised, over, _hit))
            return false;
        var above = raised.Translated(over);

        // Down onto the top.
        if (!body.TestMove(above, Vector3.Down * up, _hit))
            return false;
        float ledgeHeight = up + _hit.GetTravel().Y;
        if (ledgeHeight < Mathf.Max(s.MantleMinHeight, s.StepHeight) || ledgeHeight > s.MantleMaxHeight)
            return false;
        if (!IsWalkable(body, _hit.GetPosition() + _forward * 0.05f, motor.MinFloorNormalY))
            return false;

        _target = from.Origin + Vector3.Up * (ledgeHeight + LandingClearance) + over;
        return true;
    }

    private bool IsWalkable(CharacterBody3D body, Vector3 point, float minNormalY)
    {
        _ray.From = point + Vector3.Up * 0.15f;
        _ray.To = point + Vector3.Down * 0.15f;
        _ray.CollisionMask = body.CollisionMask;
        _ray.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
        var result = body.GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        return result.Count > 0 && result["normal"].AsVector3().Y >= minNormalY;
    }

    public override void Begin(PlayerMotor motor, in PlayerCommand cmd)
    {
        _climbing = true;
        _elapsed = 0f;
        motor.ConsumeJumpBuffer();
        motor.Velocity = Vector3.Zero;
    }

    public override bool Tick(PlayerMotor motor, in PlayerCommand cmd, float dt)
    {
        var s = motor.Settings;
        var body = motor.Body;
        _elapsed += dt;
        float step = s.MantleSpeed * dt;
        var pos = body.GlobalPosition;

        // Up first until level with the target, then forward.
        Vector3 motion;
        if (pos.Y < _target.Y - 0.001f)
            motion = Vector3.Up * Mathf.Min(step, _target.Y - pos.Y);
        else
        {
            var flat = new Vector3(_target.X - pos.X, 0f, _target.Z - pos.Z);
            if (flat.Length() <= 0.001f)
                return Finish(motor);
            motion = flat.LimitLength(step);
        }

        var collision = body.MoveAndCollide(motion);
        // Blocked (something moved into the way) or taking far too long: abort and fall back to normal movement.
        if ((collision != null && collision.GetRemainder().Length() > 0.01f) || _elapsed > 2f)
        {
            _climbing = false;
            return false;
        }
        motor.Velocity = motion / dt;
        return true;
    }

    private bool Finish(PlayerMotor motor)
    {
        _climbing = false;
        motor.Velocity = _forward * Mathf.Min(motor.Settings.WalkSpeed, 2f);
        return false;
    }

    public override void End(PlayerMotor motor)
    {
        if (_climbing)
            motor.Velocity = Vector3.Zero;
        _climbing = false;
    }
}
