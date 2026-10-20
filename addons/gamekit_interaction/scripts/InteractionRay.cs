using System;
using GameKit.Core;
using Godot;

namespace GameKit.Interaction;

/// <summary>
/// Ray-based use system. Add it as a child of a <see cref="PhysicsBody3D"/> that implements
/// <see cref="IInteractionViewer"/>. Each physics tick it rays from the viewer's eye along the view; the hit
/// collider (or a parent) implementing <see cref="IInteractable"/> becomes <see cref="Focused"/>.
/// <see cref="Use"/> activates it. Without an interactable, Use picks up light rigid bodies, which are then
/// held by impulses (never by setting their transform) so they still collide properly; <see cref="Throw"/>
/// throws them. It reads no input: the owner calls Use/Throw (player input, AI, scripted sequences).
/// </summary>
[GlobalClass]
public partial class InteractionRay : Node
{
    [Export] public float Distance { get; set; } = 2.5f;
    [Export] public float MaxPickupMass { get; set; } = 40f;
    [Export] public float HoldDistance { get; set; } = 1.6f;
    [Export] public float HoldStiffness { get; set; } = 18f;
    [Export] public float MaxHoldSpeed { get; set; } = 12f;
    [Export] public float BreakDistance { get; set; } = 1.2f;
    [Export] public float ThrowSpeed { get; set; } = 9f;
    [Export(PropertyHint.Layers3DPhysics)] public uint Mask { get; set; } = CollisionLayers.World | CollisionLayers.Props;

    private PhysicsBody3D _carrier = null!;
    private IInteractionViewer _viewer = null!;
    private readonly PhysicsRayQueryParameters3D _ray = new();
    private GodotObject? _focusedCollider;

    public IInteractable? Focused { get; private set; }
    public RigidBody3D? Held { get; private set; }
    public Vector3 RayFrom { get; private set; }
    public Vector3 RayTo { get; private set; }

    /// <summary>Prompt for the HUD; null hides it.</summary>
    public event Action<string?>? PromptChanged;
    public event Action<RigidBody3D>? PickedUp;
    public event Action<RigidBody3D>? Dropped;

    public override void _Ready()
    {
        _carrier = GetParent<PhysicsBody3D>();
        _viewer = _carrier as IInteractionViewer
            ?? throw new InvalidOperationException($"{Name}: parent {_carrier.Name} must implement {nameof(IInteractionViewer)}");
        _ray.Exclude = new Godot.Collections.Array<Rid> { _carrier.GetRid() };
    }

    public override void _PhysicsProcess(double delta)
    {
        var view = _viewer.ViewBasis;
        RayFrom = _viewer.ViewOrigin;
        RayTo = RayFrom + view * Vector3.Forward * Distance;
        if (Held != null)
            UpdateHeld((float)delta, view);
        else
            UpdateFocus();
    }

    /// <summary>Use: drop what is held, activate the focused interactable, or pick up a light prop.</summary>
    public void Use()
    {
        if (Held != null)
        {
            Drop();
            return;
        }
        if (Focused != null)
        {
            if (Focused.CanInteract(_carrier))
                Focused.Interact(_carrier);
            return;
        }
        if (_focusedCollider is RigidBody3D body && CanPickUp(body))
            PickUp(body);
    }

    /// <summary>Throws the held object along the view. Does nothing when nothing is held.</summary>
    public void Throw()
    {
        if (Held == null)
            return;
        var body = Held;
        Drop();
        body.ApplyCentralImpulse(_viewer.ViewBasis * Vector3.Forward * ThrowSpeed * body.Mass);
    }

    public void Drop()
    {
        if (Held == null)
            return;
        var body = Held;
        Held = null;
        _focusedCollider = null;
        if (IsInstanceValid(body))
        {
            _carrier.RemoveCollisionExceptionWith(body);
            Dropped?.Invoke(body);
        }
        PromptChanged?.Invoke(null);
    }

    private bool CanPickUp(RigidBody3D body) => !body.Freeze && body.Mass <= MaxPickupMass;

    private void UpdateFocus()
    {
        _ray.From = RayFrom;
        _ray.To = RayTo;
        _ray.CollisionMask = Mask;
        var hit = _carrier.GetWorld3D().DirectSpaceState.IntersectRay(_ray);
        var collider = hit.Count > 0 ? hit["collider"].AsGodotObject() : null;
        if (collider == _focusedCollider)
            return;

        _focusedCollider = collider;
        var interactable = FindInteractable(collider as Node);
        if (interactable == Focused)
            return;
        (Focused as IHighlightable)?.SetHighlighted(false);
        Focused = interactable;
        (Focused as IHighlightable)?.SetHighlighted(true);
        string? prompt = Focused?.GetPrompt(_carrier);
        if (prompt == null && collider is RigidBody3D body && CanPickUp(body))
            prompt = "Pick up";
        PromptChanged?.Invoke(prompt);
    }

    private static IInteractable? FindInteractable(Node? node)
    {
        for (int depth = 0; node != null && depth < 3; depth++, node = node.GetParent())
            if (node is IInteractable interactable)
                return interactable;
        return null;
    }

    private void PickUp(RigidBody3D body)
    {
        Held = body;
        // The carrier must not stand on, or be pushed by, what it is carrying.
        _carrier.AddCollisionExceptionWith(body);
        (Focused as IHighlightable)?.SetHighlighted(false);
        Focused = null;
        PromptChanged?.Invoke("Drop");
        PickedUp?.Invoke(body);
    }

    private void UpdateHeld(float dt, Basis view)
    {
        var body = Held!;
        if (!IsInstanceValid(body))
        {
            Held = null;
            PromptChanged?.Invoke(null);
            return;
        }
        var target = RayFrom + view * Vector3.Forward * HoldDistance;
        var error = target - body.GlobalPosition;
        if (error.Length() > BreakDistance)
        {
            Drop();
            return;
        }
        // Impulse that changes the body's velocity to the one that closes the gap, plus the carrier's motion.
        var desired = (error * HoldStiffness + _viewer.ViewerVelocity).LimitLength(MaxHoldSpeed);
        // Counter gravity this tick so the object does not sag.
        desired += Vector3.Up * (float)PhysicsServer3D.AreaGetParam(_carrier.GetWorld3D().Space, PhysicsServer3D.AreaParameter.Gravity) * dt * body.GravityScale;
        body.ApplyCentralImpulse((desired - body.LinearVelocity) * body.Mass);
        body.ApplyTorqueImpulse(-body.AngularVelocity * 0.5f * body.Mass * 0.1f);
    }
}
