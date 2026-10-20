using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Purely visual camera motion on its own transform layer below the pitch node: head bob, landing and
/// jump kicks, strafe roll, acceleration tilt, breathing, recoil punch and sprint FOV. It never touches
/// the view angles or the body, so aim and collision are unaffected. Every effect has its own switch and
/// <see cref="PlayerCameraSettings.MotionEffects"/> disables them all.
/// </summary>
public partial class PlayerCameraEffects : Node3D
{
    private FpsPlayerController _player = null!;
    private Camera3D _camera = null!;

    private float _bobPhase;
    private float _bobWeight;
    private float _kick, _kickVelocity;
    private Vector2 _recoil, _recoilVelocity;
    private float _roll, _tilt, _breathPhase;

    public override void _Ready()
    {
        _player = GetOwner<FpsPlayerController>();
        _camera = GetNode<Camera3D>("Camera3D");
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _player.Motor.Landed += OnLanded;
        _player.Motor.Jumped += OnJumped;
    }

    /// <summary>View punch in degrees (x = pitch up, y = yaw). Visual only; springs back.</summary>
    public void AddRecoil(Vector2 degrees)
    {
        if (_player.CameraSettings.MotionEffects && _player.CameraSettings.Recoil)
            _recoilVelocity += degrees * 30f;
    }

    private void OnLanded(float impactSpeed)
    {
        var cam = _player.CameraSettings;
        if (!cam.MotionEffects || !cam.LandingKick || impactSpeed < 1.5f)
            return;
        // Impulse sized so the spring's peak displacement is roughly the requested kick.
        float kick = Mathf.Min(impactSpeed * cam.LandingKickPerSpeed, cam.MaxLandingKick);
        _kickVelocity -= kick * Mathf.Sqrt(cam.KickStiffness) * 1.6f;
    }

    private void OnJumped()
    {
        var cam = _player.CameraSettings;
        if (cam.MotionEffects && cam.LandingKick)
            _kickVelocity -= cam.JumpKick * Mathf.Sqrt(cam.KickStiffness) * 1.6f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var cam = _player.CameraSettings;
        var motor = _player.Motor;
        var s = _player.Movement;

        // Springs always integrate so a disabled effect settles instead of freezing mid-kick.
        Spring(ref _kick, ref _kickVelocity, cam.KickStiffness, cam.KickDamping, dt);
        SpringVec(ref _recoil, ref _recoilVelocity, cam.RecoilStiffness, cam.RecoilDamping, dt);

        var basis = Basis.FromEuler(new Vector3(0f, _player.ViewYaw, 0f));
        var localVel = basis.Inverse() * motor.HorizontalVelocity;
        float speed = motor.HorizontalVelocity.Length();

        Vector3 offset = Vector3.Zero;
        float roll = 0f, tilt = 0f;
        float fov = cam.VerticalFov;

        if (cam.MotionEffects)
        {
            if (cam.HeadBob)
            {
                bool bobbing = motor.State == MovementState.Grounded && speed > 0.3f;
                float target = bobbing ? Mathf.Clamp(speed / s.RunSpeed, 0f, 1.6f) : 0f;
                _bobWeight = Mathf.Lerp(_bobWeight, target, MoveMath.SmoothFactor(10f, dt));
                // Phase advances with distance, so bob frequency matches footstep rate at any speed.
                _bobPhase += speed * dt / Mathf.Max(cam.BobStrideLength, 0.1f) * Mathf.Tau;
                float amp = cam.BobAmplitude * _bobWeight * (motor.IsSprinting ? cam.SprintBobScale : 1f);
                offset.Y += Mathf.Sin(_bobPhase * 2f) * amp;
                offset.X += Mathf.Sin(_bobPhase) * amp * 0.6f;
            }

            if (cam.Breathing)
            {
                _breathPhase += dt * cam.BreathingRate * Mathf.Tau;
                offset.Y += Mathf.Sin(_breathPhase) * cam.BreathingAmplitude;
            }

            if (cam.LandingKick)
                offset.Y += _kick;

            if (cam.StrafeRoll)
                roll = -Mathf.Clamp(localVel.X / Mathf.Max(s.RunSpeed, 0.1f), -1f, 1f) * cam.MaxRollDegrees;

            if (cam.AccelerationTilt && motor.State == MovementState.Grounded)
            {
                float forwardAccel = -(basis.Inverse() * motor.HorizontalAcceleration).Z;
                tilt = Mathf.Clamp(-forwardAccel * cam.TiltPerAcceleration, -cam.MaxTiltDegrees, cam.MaxTiltDegrees);
            }

            if (cam.SprintFov && motor.IsSprinting && speed > s.RunSpeed)
                fov += cam.SprintFovIncrease * Mathf.Clamp((speed - s.RunSpeed) / Mathf.Max(s.SprintSpeed - s.RunSpeed, 0.1f), 0f, 1f);
        }

        _roll = Mathf.Lerp(_roll, roll, MoveMath.SmoothFactor(cam.RollRate, dt));
        _tilt = Mathf.Lerp(_tilt, tilt, MoveMath.SmoothFactor(cam.TiltRate, dt));
        var recoil = cam.MotionEffects && cam.Recoil ? _recoil : Vector2.Zero;

        Position = offset;
        Rotation = new Vector3(Mathf.DegToRad(_tilt + recoil.X), Mathf.DegToRad(recoil.Y), Mathf.DegToRad(_roll));
        _camera.Fov = Mathf.Lerp(_camera.Fov, fov, MoveMath.SmoothFactor(cam.FovRate, dt));
    }

    // Semi-implicit Euler damped spring toward zero; stable for the stiffness range used at >= 20 fps.
    private static void Spring(ref float x, ref float v, float k, float c, float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        v += (-k * x - c * v) * dt;
        x += v * dt;
    }

    private static void SpringVec(ref Vector2 x, ref Vector2 v, float k, float c, float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        v += (-k * x - c * v) * dt;
        x += v * dt;
    }
}
