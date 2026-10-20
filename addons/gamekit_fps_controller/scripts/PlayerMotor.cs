using System;
using System.Collections.Generic;
using GameKit.Core;
using Godot;

namespace GameKit.FpsController;

/// <summary>Mutually exclusive movement states. Crouch and sprint are concurrent modifiers, not states.</summary>
public enum MovementState
{
    Grounded,
    Airborne,
    Sliding,
    Mantling,
    Climbing,
    Swimming,
    Disabled,
    /// <summary>A game-defined ability is active (see <see cref="MovementAbility.StateName"/>).</summary>
    Custom,
}

/// <summary>
/// The movement simulation. Runs once per physics tick from <see cref="FpsPlayerController"/>:
/// view yaw, abilities, stance, ground or air movement, then one <see cref="MoveBody"/>.
/// Horizontal motion uses Source-style accelerate/friction (<see cref="MoveMath"/>); vertical motion is
/// gravity integrated half before and half after the move, which makes jump arcs exact at any tick rate.
/// </summary>
public sealed class PlayerMotor
{
    /// <summary>Height the body is raised before grounded sweeps (see <see cref="LiftOffFloor"/>).</summary>
    public const float GroundLift = 0.02f;

    private readonly FpsPlayerController _body;
    private readonly KinematicCollision3D _probe = new();
    private readonly StepSolver _steps;
    private readonly PenetrationGuard _penetration;
    private readonly List<MovementAbility> _abilities = new();
    private readonly RayCast3D? _groundCheck;
    private MovementAbility? _active;

    private bool _jumpQueued;
    private float _jumpQueueAge;
    private bool _jumpedSinceGrounded;
    private bool _jumpCutAvailable;
    private bool _sprintToggled;
    private GodotObject? _lastFloorCollider;
    private Vector3 _lastHorizontalVelocity;

    public Vector3 Velocity;
    public MovementState State { get; private set; } = MovementState.Airborne;
    public bool IsOnFloor { get; private set; }
    public bool IsSprinting { get; private set; }
    public Vector3 FloorNormal { get; private set; } = Vector3.Up;
    public Vector3 PlatformVelocity { get; private set; }
    public SurfaceInfo Surface { get; private set; } = Surfaces.Get(SurfaceType.Default);
    public PlayerStance Stance { get; }
    public StepSolver Steps => _steps;
    public PenetrationGuard Penetration => _penetration;
    public float TimeSinceGrounded { get; private set; }
    /// <summary>Simulated seconds since spawn (deterministic clock for cooldowns).</summary>
    public double SimTime { get; private set; }
    /// <summary>Horizontal acceleration over the last tick (m/s²), for debug and camera tilt.</summary>
    public Vector3 HorizontalAcceleration { get; private set; }

    public bool JumpBuffered => _jumpQueued;
    public bool CoyoteAvailable => !IsOnFloor && !_jumpedSinceGrounded && TimeSinceGrounded <= Settings.CoyoteTime;

    public PlayerMovementSettings Settings => _body.Movement;
    public FpsPlayerController Body => _body;
    public Vector3 HorizontalVelocity => new(Velocity.X, 0f, Velocity.Z);
    public float JumpVelocity => MoveMath.JumpVelocity(Settings.JumpHeight, Settings.Gravity);
    public float MinFloorNormalY => Mathf.Cos(Mathf.DegToRad(Settings.MaxSlopeDegrees));

    public event Action? Jumped;
    /// <summary>Argument: downward speed at impact (m/s).</summary>
    public event Action<float>? Landed;
    /// <summary>Vertical snap while grounded that is not explained by slope or platform motion (stairs). Argument: dy (m).</summary>
    public event Action<float>? VisualStep;
    public event Action<MovementState, MovementState>? StateChanged;

    public PlayerMotor(FpsPlayerController body, CollisionShape3D shape, RayCast3D? groundCheck)
    {
        _body = body;
        _groundCheck = groundCheck;
        _steps = new StepSolver(body);
        _penetration = new PenetrationGuard(body, shape);
        Stance = new PlayerStance(body, shape, body.Movement);
    }

    /// <summary>Abilities in priority order.</summary>
    public IReadOnlyList<MovementAbility> Abilities => _abilities;

    /// <summary>The ability in control, or null during base ground/air movement.</summary>
    public MovementAbility? ActiveAbility => _active;

    /// <summary>Replaces the ability list (priority order). An active ability that is no longer listed ends.</summary>
    public void SetAbilities(IEnumerable<MovementAbility> abilities)
    {
        _abilities.Clear();
        _abilities.AddRange(abilities);
        if (_active != null && !_abilities.Contains(_active))
            EndActiveAbility();
    }

    public T? GetAbility<T>() where T : MovementAbility
    {
        foreach (var a in _abilities)
            if (a is T t)
                return t;
        return null;
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            EndActiveAbility();
            Velocity = Vector3.Zero;
            SetState(MovementState.Disabled);
        }
        else if (State == MovementState.Disabled)
        {
            SetState(MovementState.Airborne);
        }
    }

    /// <summary>Teleports with a clean state (respawn, tests).</summary>
    public void Teleport(Vector3 position)
    {
        EndActiveAbility();
        _body.GlobalPosition = position;
        _body.ResetPhysicsInterpolation();
        Velocity = Vector3.Zero;
        IsOnFloor = false;
        _jumpQueued = false;
        _jumpedSinceGrounded = true;
        if (State != MovementState.Disabled)
            SetState(MovementState.Airborne);
    }

    public void Tick(in PlayerCommand cmd, float dt)
    {
        _body.Rotation = new Vector3(0f, cmd.Yaw, 0f);
        if (State == MovementState.Disabled || dt <= 0f)
            return;

        SimTime += dt;
        var prevHorizontal = HorizontalVelocity;
        if (cmd.JumpPressed)
        {
            _jumpQueued = true;
            _jumpQueueAge = 0f;
        }

        UpdateSprint(cmd);

        if (_active == null)
        {
            foreach (var ability in _abilities)
            {
                if (ability.CanBegin(this, cmd))
                {
                    _active = ability;
                    ability.Begin(this, cmd);
                    SetState(ability.State);
                    break;
                }
            }
        }

        if (_active != null)
        {
            if (!_active.Tick(this, cmd, dt))
                EndActiveAbility();
        }
        else
        {
            Stance.Update(cmd, IsOnFloor);
            if (IsOnFloor)
                GroundMove(cmd, dt);
            else
                AirMove(cmd, dt);
        }

        HorizontalAcceleration = (HorizontalVelocity - prevHorizontal) / dt;
        _jumpQueueAge += dt;
        if (_jumpQueueAge > Settings.JumpBufferTime)
            _jumpQueued = false;
        if (!IsOnFloor)
            TimeSinceGrounded += dt;
    }

    // ---- Base movement -------------------------------------------------------------------

    private void GroundMove(in PlayerCommand cmd, float dt)
    {
        // Source checks the jump button before friction: a jump on the landing tick skips friction.
        if (WantsJump(cmd))
        {
            Jump(cmd);
            AirMove(cmd, dt);
            return;
        }

        var s = Settings;
        var h = MoveMath.ApplyFriction(HorizontalVelocity, s.Friction * Surface.Friction, s.StopSpeed, dt);
        WishMovement(cmd, out var wishDir, out float wishSpeed);
        h = MoveMath.Accelerate(h, wishDir, wishSpeed, s.GroundAcceleration * Surface.Friction, dt);
        Velocity = new Vector3(h.X, 0f, h.Z);
        MoveBody(dt, tryStep: true, groundMove: true);
    }

    private void AirMove(in PlayerCommand cmd, float dt)
    {
        var s = Settings;
        if (!IsOnFloor && WantsJump(cmd) && CoyoteAvailable)
            Jump(cmd);

        if (s.VariableJumpHeight && _jumpCutAvailable && !cmd.JumpHeld && Velocity.Y > 0f)
        {
            Velocity.Y *= s.JumpCutMultiplier;
            _jumpCutAvailable = false;
        }

        WishMovement(cmd, out var wishDir, out float wishSpeed);
        var h = MoveMath.AirAccelerate(HorizontalVelocity, wishDir, wishSpeed, s.AirAcceleration, s.AirSpeedCap, dt);
        Velocity = new Vector3(h.X, Velocity.Y, h.Z);

        ApplyHalfGravity(dt);
        MoveBody(dt, tryStep: false);
        if (!IsOnFloor)
            ApplyHalfGravity(dt);
    }

    public void ApplyHalfGravity(float dt)
    {
        Velocity.Y = Mathf.Max(Velocity.Y - Settings.Gravity * dt * 0.5f, -Settings.MaxFallSpeed);
    }

    public bool WantsJump(in PlayerCommand cmd) =>
        _jumpQueued || (Settings.AllowBunnyHop && Settings.AutoBunnyHop && cmd.JumpHeld);

    /// <summary>Starts a jump: absolute upward speed (consistent from slopes), plus clamped platform velocity.</summary>
    public void Jump(in PlayerCommand cmd, bool applySpeedCap = true)
    {
        var s = Settings;
        if (applySpeedCap && !s.AllowBunnyHop && s.BunnyHopSpeedCap > 0f)
            Velocity = MoveMath.ClampHorizontal(Velocity, s.BunnyHopSpeedCap * MaxGroundSpeed(cmd));
        bool fromFloor = IsOnFloor;
        Velocity.Y = JumpVelocity;
        if (fromFloor)
            Velocity += InheritedPlatformVelocity();
        _jumpQueued = false;
        _jumpedSinceGrounded = true;
        _jumpCutAvailable = true;
        IsOnFloor = false;
        if (_active == null)
            SetState(MovementState.Airborne);
        Jumped?.Invoke();
    }

    public void ConsumeJumpBuffer() => _jumpQueued = false;

    // ---- Movement helpers shared with abilities -------------------------------------------

    /// <summary>Horizontal wish direction from the view yaw and the wish speed for the current stance/sprint.</summary>
    public void WishMovement(in PlayerCommand cmd, out Vector3 wishDir, out float wishSpeed)
    {
        var basis = Basis.FromEuler(new Vector3(0f, cmd.Yaw, 0f));
        var dir = basis * new Vector3(cmd.Move.X, 0f, -cmd.Move.Y);
        float amount = Mathf.Min(cmd.Move.Length(), 1f);
        wishDir = amount > 1e-4f ? dir.Normalized() : Vector3.Zero;
        wishSpeed = MaxGroundSpeed(cmd) * amount;
    }

    /// <summary>Full-input speed for the current stance, sprint and walk modifiers, and surface.</summary>
    public float MaxGroundSpeed(in PlayerCommand cmd)
    {
        var s = Settings;
        float speed = Stance.IsCrouched ? s.CrouchSpeed
            : IsSprinting ? s.SprintSpeed
            : cmd.WalkHeld ? s.WalkSpeed
            : s.RunSpeed;
        return speed * Surface.SpeedMultiplier;
    }

    private void UpdateSprint(in PlayerCommand cmd)
    {
        var s = Settings;
        if (s.SprintToggle)
        {
            if (cmd.SprintPressed)
                _sprintToggled = !_sprintToggled;
            if (cmd.Move.LengthSquared() < 0.01f)
                _sprintToggled = false;
        }
        bool wants = s.SprintToggle ? _sprintToggled : cmd.SprintHeld;
        // Sprint state only changes on the ground, so air speed keeps the takeoff mode.
        if (IsOnFloor || State == MovementState.Swimming)
            IsSprinting = wants && !Stance.IsCrouched && cmd.Move.Y > s.SprintMinForwardInput;
    }

    /// <summary>
    /// The single place the body moves. Runs the step solver, move_and_slide, then landing,
    /// ceiling, platform, surface and prop-push bookkeeping.
    /// <paramref name="groundMove"/>: walking/sliding. As in Source, these never produce vertical velocity,
    /// so sliding up a step corner or over a ramp crest cannot launch the player.
    /// </summary>
    public void MoveBody(float dt, bool tryStep, bool groundMove = false)
    {
        var s = Settings;
        bool wasOnFloor = IsOnFloor;
        float preVelocityY = Velocity.Y;
        var prePosition = _body.GlobalPosition;

        _lastHorizontalVelocity = HorizontalVelocity;
        if (wasOnFloor)
            LiftOffFloor();
        float climbed = tryStep && wasOnFloor ? _steps.TryStepUp(HorizontalVelocity * dt, s.StepHeight, MinFloorNormalY, prePosition.Y) : 0f;
        bool stepped = climbed > 0f;
        if (stepped)
        {
            // The step already moved the body this tick. A zero-motion slide still updates floor contact
            // and applies platform motion; the player's own velocity is kept for the next tick.
            _body.Velocity = Vector3.Zero;
            _body.MoveAndSlide();
        }
        else
        {
            _body.Velocity = Velocity;
            _body.MoveAndSlide();
            Velocity = _body.Velocity;
        }
        _penetration.Resolve(ref Velocity);
        if (groundMove)
            Velocity.Y = 0f;

        // A step counts as grounded even while the capsule's rim still rests on the step corner.
        bool onFloor = stepped || (_body.IsOnFloor() && Velocity.Y <= JumpVelocity * 0.5f);
        if (!onFloor && tryStep && wasOnFloor && !_jumpedSinceGrounded && Velocity.Y <= 0f
            && _steps.TrySnapDown(s.StepHeight, MinFloorNormalY, GroundLift))
        {
            onFloor = true;
            _penetration.Resolve(ref Velocity);
        }
        if (_body.IsOnCeiling() && Velocity.Y > 0f)
            Velocity.Y = 0f;

        if (onFloor)
        {
            FloorNormal = _body.IsOnFloor() ? _body.GetFloorNormal() : Vector3.Up;
            if (Velocity.Y < 0f)
                Velocity.Y = 0f;
            TimeSinceGrounded = 0f;
            _jumpedSinceGrounded = false;
            _jumpCutAvailable = false;
        }
        else if (wasOnFloor && !_jumpedSinceGrounded)
        {
            // Walked off a ledge or a moving platform: carry its motion, but never launch.
            Velocity += InheritedPlatformVelocity();
        }

        if (stepped)
            VisualStep?.Invoke(climbed);
        else if (onFloor && wasOnFloor)
            DetectVisualStep(prePosition, dt);

        IsOnFloor = onFloor;
        if (!onFloor)
            PlatformVelocity = Vector3.Zero;
        else if (_body.IsOnFloor())
            PlatformVelocity = _body.GetPlatformVelocity();

        if (_active == null)
            SetState(onFloor ? MovementState.Grounded : MovementState.Airborne);
        if (onFloor && !wasOnFloor)
            Landed?.Invoke(Mathf.Max(0f, -preVelocityY));

        UpdateSurface(onFloor);
        if (_body.IsOnWall())
            PushProps(dt);
    }

    /// <summary>
    /// Workaround for a verified PhysX-backend issue (docs/PHYSX_INTEGRATION.md): when the capsule starts a
    /// motion test while touching the floor, the sweep can miss a wall ahead and the body ends up to one
    /// tick of motion inside it. Starting the grounded sweeps a hair above the floor avoids it; floor snap
    /// (always longer than the lift) returns the body to the floor in the same move.
    /// </summary>
    private void LiftOffFloor()
    {
        float lift = _body.TestMove(_body.GlobalTransform, Vector3.Up * GroundLift, _probe) ? _probe.GetTravel().Y : GroundLift;
        if (lift > 0f)
            _body.GlobalPosition += Vector3.Up * lift;
    }

    private Vector3 InheritedPlatformVelocity()
    {
        var v = PlatformVelocity;
        float max = Settings.MaxPlatformInheritSpeed;
        return v.LengthSquared() > max * max ? v.Normalized() * max : v;
    }

    private void DetectVisualStep(Vector3 prePosition, float dt)
    {
        var delta = _body.GlobalPosition - prePosition;
        float dy = delta.Y - PlatformVelocity.Y * dt;
        float horizontal = new Vector2(delta.X, delta.Z).Length();
        float slopeAllowance = horizontal * Mathf.Tan(Mathf.DegToRad(Settings.MaxSlopeDegrees)) + 0.01f;
        if (Mathf.Abs(dy) > slopeAllowance)
            VisualStep?.Invoke(dy);
    }

    private void UpdateSurface(bool onFloor)
    {
        if (!onFloor || _groundCheck == null)
            return;
        _groundCheck.ForceRaycastUpdate();
        var collider = _groundCheck.IsColliding() ? _groundCheck.GetCollider() : null;
        if (collider == _lastFloorCollider)
            return;
        _lastFloorCollider = collider;
        Surface = Surfaces.FromCollider(collider);
    }

    /// <summary>
    /// Pushes rigid props the player walks into with a force-limited impulse. The impulse only makes up
    /// the speed difference along the push direction, scaled down for heavy props, so walking into a crate
    /// moves it at walking pace instead of launching it. Props above MaxPushMass do not move.
    /// </summary>
    private void PushProps(float dt)
    {
        var s = Settings;
        int count = _body.GetSlideCollisionCount();
        for (int i = 0; i < count; i++)
        {
            var collision = _body.GetSlideCollision(i);
            if (collision.GetCollider() is not RigidBody3D prop || prop.Freeze || prop.Mass > s.MaxPushMass)
                continue;
            var dir = -collision.GetNormal();
            dir.Y = 0f;
            if (dir.LengthSquared() < 1e-4f)
                continue;
            dir = dir.Normalized();
            float desiredSpeed = Mathf.Max(0f, _lastHorizontalVelocity.Dot(dir));
            float closing = desiredSpeed - prop.LinearVelocity.Dot(dir);
            if (closing <= 0f)
                continue;
            float impulse = Mathf.Min(prop.Mass * closing, s.PushForce * dt);
            prop.ApplyImpulse(dir * impulse, collision.GetPosition() - prop.GlobalPosition);
        }
    }

    public void SetState(MovementState state)
    {
        if (state == State)
            return;
        var old = State;
        State = state;
        StateChanged?.Invoke(old, state);
    }

    private void EndActiveAbility()
    {
        if (_active == null)
            return;
        var ability = _active;
        _active = null;
        ability.End(this);
        if (State != MovementState.Disabled)
            SetState(IsOnFloor ? MovementState.Grounded : MovementState.Airborne);
    }

    /// <summary>Lets abilities force grounded bookkeeping after moving the body themselves.</summary>
    public void SetFloorState(bool onFloor)
    {
        IsOnFloor = onFloor;
        if (onFloor)
        {
            TimeSinceGrounded = 0f;
            _jumpedSinceGrounded = false;
        }
    }
}
