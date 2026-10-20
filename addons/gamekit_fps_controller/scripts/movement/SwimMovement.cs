using System;
using GameKit.Water;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Swimming inside a <see cref="WaterVolume"/> once enough of the body is submerged. Full 3D movement
/// along the view direction, jump/crouch to rise/sink, water drag, buoyancy that floats the head at the
/// surface, and climbing out onto ledges through <see cref="MantleMovement"/>. Raises <see cref="UnderwaterChanged"/> for
/// camera/audio treatment.
/// </summary>
[GlobalClass]
public sealed partial class SwimMovement : MovementAbility
{
    private const float ExitHysteresis = 0.8f;
    // Standing on ground with the eyes this far above the surface counts as wading, not swimming: buoyancy
    // aims the feet 1.5 m deep and pressed a swimmer into stairs that rise above that, stalling it there.
    private const float WadeHeadClearance = 0.1f;

    private WaterVolume? _water;
    private bool _underwater;

    public override MovementState State => MovementState.Swimming;
    public bool IsUnderwater => _underwater;
    public event Action<bool>? UnderwaterChanged;

    public void EnterVolume(WaterVolume water) => _water = water;

    public void ExitVolume(WaterVolume water)
    {
        if (_water == water)
            _water = null;
    }

    /// <summary>
    /// Water depth at the feet; 0 outside the water's box. The box test does not rely on overlap signals
    /// alone: a missed exit left a stale volume, and dry ground below the pool's surface then read as deep
    /// water, so the player kept swimming on land.
    /// </summary>
    private float Depth(PlayerMotor motor)
    {
        if (_water == null)
            return 0f;
        var feet = motor.Body.GlobalPosition;
        var b = _water.Bounds;
        bool inside = feet.X > b.Position.X && feet.X < b.End.X && feet.Z > b.Position.Z && feet.Z < b.End.Z
            && feet.Y > b.Position.Y - 0.5f;
        return inside ? _water.SurfaceY - feet.Y : 0f;
    }

    private static bool Wading(PlayerMotor motor, float depth) =>
        motor.IsOnFloor && depth < motor.Stance.EyeHeight - WadeHeadClearance;

    public override bool CanBegin(PlayerMotor motor, in PlayerCommand cmd)
    {
        var s = motor.Settings;
        float depth = Depth(motor);
        return s.SwimEnabled && depth >= s.StandingHeight * s.SwimDepthFraction && !Wading(motor, depth);
    }

    public override void Begin(PlayerMotor motor, in PlayerCommand cmd)
    {
        motor.Stance.TryStand(grounded: false);
        // Entering from a fall: water absorbs most of the downward speed.
        if (motor.Velocity.Y < 0f)
            motor.Velocity.Y *= 0.3f;
    }

    public override bool Tick(PlayerMotor motor, in PlayerCommand cmd, float dt)
    {
        var s = motor.Settings;
        float depth = Depth(motor);
        float eyeDepth = depth - motor.Stance.EyeHeight;
        SetUnderwater(eyeDepth > 0f);
        if (_water == null || depth < s.StandingHeight * s.SwimDepthFraction * ExitHysteresis || Wading(motor, depth))
            return false;

        // Head floats just above the surface.
        float floatDepth = motor.Stance.EyeHeight - 0.15f;

        var view = Basis.FromEuler(new Vector3(cmd.Pitch, cmd.Yaw, 0f));
        var wish = view * new Vector3(cmd.Move.X, 0f, -cmd.Move.Y);
        if (cmd.JumpHeld)
            wish += Vector3.Up;
        if (cmd.CrouchHeld)
            wish += Vector3.Down;
        // Can't swim upward out of the water by looking up; the surface holds you at float depth.
        if (depth <= floatDepth && wish.Y > 0f)
            wish.Y = 0f;
        float amount = Mathf.Min(wish.Length(), 1f);
        var wishDir = amount > 1e-4f ? wish.Normalized() : Vector3.Zero;
        float wishSpeed = s.SwimSpeed * amount * (motor.IsSprinting ? 1.3f : 1f);

        // Climbing out: at the surface, jump + forward at a ledge yields to the mantle ability.
        if (cmd.JumpHeld && cmd.Move.Y > 0.5f && depth < floatDepth + 0.3f
            && motor.GetAbility<MantleMovement>() is { } mantle && mantle.CanBegin(motor, cmd))
            return false;

        var v = MoveMath.ApplyFriction(motor.Velocity, s.WaterFriction, 0.5f, dt);
        v = MoveMath.Accelerate(v, wishDir, wishSpeed, s.SwimAcceleration, dt);

        // Buoyancy: with no vertical intent, drift to float depth.
        if (Mathf.Abs(wish.Y) < 0.1f)
        {
            float error = depth - floatDepth;
            v.Y = Mathf.MoveToward(v.Y, Mathf.Clamp(error * 2f, -1.5f, 1.5f), 6f * dt);
        }

        motor.Velocity = v;
        motor.MoveBody(dt, tryStep: true);
        return true;
    }

    public override void End(PlayerMotor motor) => SetUnderwater(false);

    private void SetUnderwater(bool value)
    {
        if (value == _underwater)
            return;
        _underwater = value;
        UnderwaterChanged?.Invoke(value);
    }
}
