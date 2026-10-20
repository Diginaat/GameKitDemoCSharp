using System;
using Godot;

namespace GameKit.FpsController;

/// <summary>
/// Standing/crouching. Owns the capsule size so the collider always matches the stance.
/// Crouching only ever shrinks the capsule into space it already occupied, so it is always safe;
/// standing is allowed only after a shape query proves the taller capsule fits.
/// The body origin is at the feet.
/// </summary>
public sealed class PlayerStance
{
    // Shrinks the clearance query slightly so resting contact with the floor/walls is not "blocked".
    private const float ClearanceSkin = 0.01f;

    private readonly CharacterBody3D _body;
    private readonly CollisionShape3D _shapeNode;
    private readonly CapsuleShape3D _shape;
    private readonly CapsuleShape3D _queryShape = new();
    private readonly PhysicsShapeQueryParameters3D _query = new();
    private PlayerMovementSettings _settings;
    private bool _toggleWantsCrouch;

    public bool IsCrouched { get; private set; }
    /// <summary>True when the player wants to stand but geometry above prevents it.</summary>
    public bool IsStandBlocked { get; private set; }
    public float Height => IsCrouched ? _settings.CrouchHeight : _settings.StandingHeight;
    public float EyeHeight => IsCrouched ? _settings.CrouchEyeHeight : _settings.StandingEyeHeight;

    /// <summary>Raised when the stance changes. Argument: how far the feet moved up (airborne crouch tucks the legs).</summary>
    public event Action<bool, float>? Changed;

    public PlayerStance(CharacterBody3D body, CollisionShape3D shapeNode, PlayerMovementSettings settings)
    {
        _body = body;
        _shapeNode = shapeNode;
        // A unique shape per player: resizing a shared scene resource would resize every instance.
        _shape = new CapsuleShape3D();
        _shapeNode.Shape = _shape;
        _settings = settings;
        _query.Shape = _queryShape;
        _query.CollisionMask = body.CollisionMask;
        _query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
        ApplySize();
    }

    public void Configure(PlayerMovementSettings settings)
    {
        // Keep the current stance; only the dimensions change. Normal input handling stands the player up later.
        _settings = settings;
        ApplySize();
    }

    /// <summary>Applies crouch input. <paramref name="forceCrouch"/> keeps the player crouched (slides).</summary>
    public void Update(in PlayerCommand cmd, bool grounded, bool forceCrouch = false)
    {
        if (cmd.CrouchPressed)
            _toggleWantsCrouch = !_toggleWantsCrouch;
        bool wantCrouch = forceCrouch || (_settings.CrouchToggle ? _toggleWantsCrouch : cmd.CrouchHeld);

        if (wantCrouch && !IsCrouched)
            Crouch(grounded);
        else if (!wantCrouch && IsCrouched)
            IsStandBlocked = !TryStand(grounded);
        else
            IsStandBlocked = false;
    }

    public void Crouch(bool grounded)
    {
        if (IsCrouched)
            return;
        float delta = _settings.StandingHeight - _settings.CrouchHeight;
        IsCrouched = true;
        _toggleWantsCrouch = true;
        ApplySize();
        float feetShift = 0f;
        if (!grounded)
        {
            // Source-style airborne crouch: keep the head where it is and tuck the feet up.
            feetShift = delta;
            _body.GlobalPosition += Vector3.Up * delta;
        }
        Changed?.Invoke(true, feetShift);
    }

    public bool TryStand(bool grounded)
    {
        if (!IsCrouched)
            return true;
        float delta = _settings.StandingHeight - _settings.CrouchHeight;
        // In the air prefer dropping the feet back down (head fixed); fall back to growing upward.
        float feetShift;
        if (!grounded && CanFit(_settings.StandingHeight, -delta))
            feetShift = -delta;
        else if (CanFit(_settings.StandingHeight, 0f))
            feetShift = 0f;
        else
            return false;

        IsCrouched = false;
        _toggleWantsCrouch = false;
        if (feetShift != 0f)
            _body.GlobalPosition += Vector3.Up * feetShift;
        ApplySize();
        Changed?.Invoke(false, feetShift);
        return true;
    }

    /// <summary>Whether a capsule of <paramref name="height"/> with feet raised by <paramref name="feetOffset"/> overlaps nothing.</summary>
    public bool CanFit(float height, float feetOffset)
    {
        _queryShape.Radius = _settings.Radius - ClearanceSkin;
        _queryShape.Height = Mathf.Max(height - 2f * ClearanceSkin, 2f * _queryShape.Radius);
        var origin = _body.GlobalPosition + Vector3.Up * (feetOffset + height * 0.5f);
        _query.Transform = new Transform3D(Basis.Identity, origin);
        _query.CollisionMask = _body.CollisionMask;
        var space = _body.GetWorld3D().DirectSpaceState;
        return space.IntersectShape(_query, 1).Count == 0;
    }

    private void ApplySize()
    {
        float height = Height;
        _shape.Radius = _settings.Radius;
        _shape.Height = height;
        _shapeNode.Position = new Vector3(0f, height * 0.5f, 0f);
    }
}
