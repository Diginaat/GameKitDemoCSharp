using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Source-inspired acceleration and friction (see Source SDK 2013 <c>gamemovement.cpp</c>:
/// <c>Friction</c>, <c>Accelerate</c>, <c>AirAccelerate</c>). Pure functions on horizontal
/// velocity so they can be tested without a scene.
/// </summary>
public static class MoveMath
{
    /// <summary>
    /// Source ground friction, integrated exactly instead of per-tick so stopping distance does not
    /// depend on the physics tick rate. Source's rule is <c>drop = max(speed, stopSpeed) * friction * dt</c>:
    /// above <paramref name="stopSpeed"/> that is exponential decay, below it a constant deceleration.
    /// </summary>
    public static Vector3 ApplyFriction(Vector3 velocity, float friction, float stopSpeed, float dt)
    {
        float speed = velocity.Length();
        if (speed < 1e-4f)
            return Vector3.Zero;
        if (friction <= 0f || dt <= 0f)
            return velocity;

        float newSpeed = speed;
        float remaining = dt;
        if (stopSpeed <= 0f)
        {
            newSpeed *= Mathf.Exp(-friction * remaining);
            remaining = 0f;
        }
        else if (newSpeed > stopSpeed)
        {
            float timeToStopSpeed = Mathf.Log(newSpeed / stopSpeed) / friction;
            if (timeToStopSpeed >= remaining)
            {
                newSpeed *= Mathf.Exp(-friction * remaining);
                remaining = 0f;
            }
            else
            {
                newSpeed = stopSpeed;
                remaining -= timeToStopSpeed;
            }
        }

        if (remaining > 0f)
            newSpeed = Mathf.Max(0f, newSpeed - friction * stopSpeed * remaining);

        return velocity * (newSpeed / speed);
    }

    /// <summary>
    /// Source <c>Accelerate</c>: adds speed along <paramref name="wishDir"/> until the projection of
    /// velocity onto it reaches <paramref name="wishSpeed"/>. Speed in other directions is untouched,
    /// which is what preserves momentum while still giving instant directional control.
    /// </summary>
    public static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float addSpeed = wishSpeed - velocity.Dot(wishDir);
        if (addSpeed <= 0f)
            return velocity;
        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    /// <summary>
    /// Source <c>AirAccelerate</c>: like <see cref="Accelerate"/> but the projected-speed target is
    /// capped at <paramref name="airSpeedCap"/> while the acceleration rate still uses the full wish speed.
    /// Strafing perpendicular to velocity therefore turns (and can gain speed: air strafing).
    /// </summary>
    public static Vector3 AirAccelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float airSpeedCap, float dt)
    {
        float cappedWish = Mathf.Min(wishSpeed, airSpeedCap);
        float addSpeed = cappedWish - velocity.Dot(wishDir);
        if (addSpeed <= 0f)
            return velocity;
        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    /// <summary>Clamps the horizontal (XZ) speed, keeping the vertical component.</summary>
    public static Vector3 ClampHorizontal(Vector3 velocity, float maxSpeed)
    {
        var h = new Vector3(velocity.X, 0f, velocity.Z);
        float speed = h.Length();
        if (speed <= maxSpeed || speed < 1e-4f)
            return velocity;
        h *= maxSpeed / speed;
        return new Vector3(h.X, velocity.Y, h.Z);
    }

    /// <summary>Launch speed that reaches <paramref name="height"/> under <paramref name="gravity"/>.</summary>
    public static float JumpVelocity(float height, float gravity) => Mathf.Sqrt(2f * gravity * height);

    /// <summary>Frame-rate-independent exponential smoothing factor for <c>Lerp(a, b, factor)</c>.</summary>
    public static float SmoothFactor(float rate, float dt) => 1f - Mathf.Exp(-rate * dt);
}
