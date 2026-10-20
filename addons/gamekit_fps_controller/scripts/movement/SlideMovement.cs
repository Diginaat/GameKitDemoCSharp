using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Sprint + crouch starts a slide: crouched capsule, a small speed boost, low friction, gravity pulling
/// along slopes and light steering. Ends when slow, airborne, after crouch is released, or by jumping.
/// </summary>
[GlobalClass]
public sealed partial class SlideMovement : MovementAbility
{
    private float _elapsed;
    private double _cooldownUntil;

    public override MovementState State => MovementState.Sliding;

    public override bool CanBegin(PlayerMotor motor, in PlayerCommand cmd)
    {
        var s = motor.Settings;
        return s.SlideEnabled
            && cmd.CrouchPressed
            && motor.IsOnFloor
            && motor.IsSprinting
            && motor.SimTime >= _cooldownUntil
            && motor.HorizontalVelocity.Length() >= s.SlideMinStartSpeed;
    }

    public override void Begin(PlayerMotor motor, in PlayerCommand cmd)
    {
        _elapsed = 0f;
        motor.Stance.Crouch(grounded: true);
        var h = motor.HorizontalVelocity;
        motor.Velocity += h.Normalized() * motor.Settings.SlideStartBoost;
    }

    public override bool Tick(PlayerMotor motor, in PlayerCommand cmd, float dt)
    {
        var s = motor.Settings;
        _elapsed += dt;

        if (motor.WantsJump(cmd))
        {
            // Slide-jump keeps slide speed (no bunny-hop cap) and stays crouched until the stance code stands up.
            motor.Jump(cmd, applySpeedCap: false);
            motor.ApplyHalfGravity(dt);
            motor.MoveBody(dt, tryStep: false);
            return false;
        }

        bool released = s.CrouchToggle ? cmd.CrouchPressed : !cmd.CrouchHeld;
        if (released || !motor.IsOnFloor)
            return false;

        // Past the max duration friction ramps up to normal ground friction.
        float friction = _elapsed < s.SlideMaxDuration ? s.SlideFriction : s.Friction;
        var h = MoveMath.ApplyFriction(motor.HorizontalVelocity, friction * motor.Surface.Friction, 0.5f, dt);

        // Gravity component along the floor plane: speeds up downhill, slows uphill.
        var n = motor.FloorNormal;
        var downhill = (Vector3.Down - n * Vector3.Down.Dot(n)) * s.Gravity;
        h += new Vector3(downhill.X, 0f, downhill.Z) * dt;

        motor.WishMovement(cmd, out var wishDir, out float wishSpeed);
        h = MoveMath.AirAccelerate(h, wishDir, wishSpeed, s.AirAcceleration, s.AirSpeedCap, dt);

        motor.Velocity = new Vector3(h.X, 0f, h.Z);
        motor.MoveBody(dt, tryStep: true, groundMove: true);
        return motor.IsOnFloor && motor.HorizontalVelocity.Length() > s.SlideEndSpeed;
    }

    public override void End(PlayerMotor motor)
    {
        _cooldownUntil = motor.SimTime + motor.Settings.SlideCooldown;
    }
}
