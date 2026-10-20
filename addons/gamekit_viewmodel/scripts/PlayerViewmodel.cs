using System.Collections.Generic;
using GameKit.FpsController;
using GameKit.Interaction;
using Godot;

namespace GameKit.Viewmodel;

/// <summary>
/// First-person hands/weapon presentation under the camera. Holds <see cref="Weapon"/> children,
/// switches between them (1/2, 3 holsters), forwards fire/reload, and adds procedural motion:
/// look sway (inertia), movement bob, landing dip, sprint and crouch poses, and lowering while a prop
/// is carried. Reads player state only; the controller works identically without this node.
/// </summary>
public partial class PlayerViewmodel : Node3D
{
    [Export] public Vector3 RestPosition { get; set; } = new(0.17f, -0.17f, -0.22f);
    [Export] public float SwayAmount { get; set; } = 0.02f;
    [Export] public float SwayRotation { get; set; } = 0.06f;
    [Export] public float SwayRate { get; set; } = 9f;
    [Export] public float BobAmount { get; set; } = 0.012f;
    [Export] public float KickBack { get; set; } = 0.025f;

    private FpsPlayerController _player = null!;
    private PlayerCameraEffects? _cameraEffects;
    private InteractionRay? _interaction;
    private readonly List<Weapon> _weapons = new();
    private int _current = -1;

    private float _prevYaw, _prevPitch;
    private Vector2 _sway;
    private float _bobPhase;
    private float _dip, _dipVelocity;
    private float _kick, _kickVelocity;
    private float _poseBlend; // 0 = ready, 1 = lowered (sprint/carry)

    public Weapon? CurrentWeapon => _current >= 0 ? _weapons[_current] : null;

    public override void _Ready()
    {
        _player = GetOwner<FpsPlayerController>();
        _cameraEffects = GetParent().GetParentOrNull<PlayerCameraEffects>();
        _interaction = _player.GetNodeOrNull<InteractionRay>("Interaction");
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        foreach (var child in GetChildren())
        {
            if (child is Weapon weapon)
            {
                weapon.Visible = false;
                weapon.Fired += OnFired;
                _weapons.Add(weapon);
            }
        }
        _player.Motor.Landed += speed => _dipVelocity -= Mathf.Min(speed * 0.02f, 0.12f) * 12f;
        _prevYaw = _player.ViewYaw;
        _prevPitch = _player.ViewPitch;
        if (_weapons.Count > 0)
            Select(0);
    }

    public void Select(int index)
    {
        if (index == _current || index >= _weapons.Count)
            return;
        CurrentWeapon?.Holster();
        _current = index;
        CurrentWeapon?.Equip();
    }

    public void Holster()
    {
        CurrentWeapon?.Holster();
        _current = -1;
    }

    private bool Lowered => _player.Motor.IsSprinting && _player.Motor.HorizontalVelocity.Length() > _player.Movement.RunSpeed * 0.9f
        || _interaction?.Held != null
        || _player.Motor.State is MovementState.Climbing or MovementState.Mantling or MovementState.Swimming;

    public override void _Process(double delta)
    {
        HandleInput();
        Animate((float)delta);
    }

    private void HandleInput()
    {
        if (_player.UseExternalCommand || _player.Input is not { MouseCaptured: true })
            return;
        if (Input.IsActionJustPressed(InputActions.Weapon1)) Select(0);
        if (Input.IsActionJustPressed(InputActions.Weapon2)) Select(1);
        if (Input.IsActionJustPressed(InputActions.Holster)) Holster();
        var weapon = CurrentWeapon;
        if (weapon == null || Lowered)
            return;
        if (Input.IsActionJustPressed(InputActions.Reload))
            weapon.Reload();
        bool trigger = weapon.Automatic ? Input.IsActionPressed(InputActions.Fire) : Input.IsActionJustPressed(InputActions.Fire);
        if (trigger)
        {
            var aim = GetViewport().GetCamera3D().GlobalTransform;
            weapon.TryFire(aim.Origin, -aim.Basis.Z, _player.GetRid(), _player.GetWorld3D().DirectSpaceState);
        }
    }

    private void OnFired(Weapon weapon)
    {
        _kickVelocity += KickBack * 40f;
        var r = weapon.RecoilDegrees;
        _cameraEffects?.AddRecoil(new Vector2(r.X, (float)GD.RandRange(-r.Y, r.Y)));
    }

    private void Animate(float dt)
    {
        var motor = _player.Motor;
        var cam = _player.CameraSettings;
        bool effects = cam.MotionEffects;

        // Look sway: the weapon lags behind view rotation, then catches up (inertia).
        float dYaw = Mathf.AngleDifference(_prevYaw, _player.ViewYaw);
        float dPitch = _player.ViewPitch - _prevPitch;
        _prevYaw = _player.ViewYaw;
        _prevPitch = _player.ViewPitch;
        var swayTarget = effects ? new Vector2(-dYaw, -dPitch) / Mathf.Max(dt, 1e-4f) * 0.01f : Vector2.Zero;
        swayTarget = swayTarget.LimitLength(1f);
        _sway = _sway.Lerp(swayTarget, MoveMath.SmoothFactor(SwayRate, dt));

        float speed = motor.HorizontalVelocity.Length();
        float bobWeight = effects && motor.State == MovementState.Grounded ? Mathf.Clamp(speed / _player.Movement.RunSpeed, 0f, 1.5f) : 0f;
        _bobPhase += speed * dt / 2.4f * Mathf.Tau;

        SpringStep(ref _dip, ref _dipVelocity, 160f, 20f, dt);
        SpringStep(ref _kick, ref _kickVelocity, 260f, 26f, dt);
        _poseBlend = Mathf.Lerp(_poseBlend, Lowered ? 1f : 0f, MoveMath.SmoothFactor(10f, dt));

        var position = RestPosition
            + new Vector3(_sway.X * SwayAmount, _sway.Y * SwayAmount, 0f)
            + new Vector3(Mathf.Sin(_bobPhase) * BobAmount, Mathf.Abs(Mathf.Sin(_bobPhase)) * -BobAmount, 0f) * bobWeight
            + new Vector3(0f, effects ? _dip : 0f, _kick)
            + new Vector3(-0.04f, -0.06f, 0.03f) * _poseBlend
            + (motor.Stance.IsCrouched ? new Vector3(-0.015f, 0.01f, 0f) : Vector3.Zero);
        var rotation = new Vector3(
            _sway.Y * SwayRotation - _poseBlend * 0.5f,
            _sway.X * SwayRotation + _poseBlend * 0.6f,
            _sway.X * SwayRotation * 0.5f + _poseBlend * 0.25f);

        Position = position;
        Rotation = rotation;
    }

    private static void SpringStep(ref float x, ref float v, float k, float c, float dt)
    {
        dt = Mathf.Min(dt, 0.05f);
        v += (-k * x - c * v) * dt;
        x += v * dt;
    }
}
