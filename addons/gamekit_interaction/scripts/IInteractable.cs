using Godot;

namespace GameKit.Interaction;

/// <summary>
/// Implemented by anything that can be used (doors, buttons, switches, terminals). The interaction ray
/// finds the collider and walks up to two parents looking for this interface, so the script can sit on
/// the body or on its parent. <c>interactor</c> is whoever used it (a player, an AI); cast it to your own
/// game type when the object needs to know more (inventory, team, permissions).
/// </summary>
public interface IInteractable
{
    /// <summary>Prompt text, e.g. "Open door". Called only while focused.</summary>
    string GetPrompt(Node interactor);

    bool CanInteract(Node interactor);

    void Interact(Node interactor);
}

/// <summary>Optional: visual feedback while an interactor is looking at the object.</summary>
public interface IHighlightable
{
    void SetHighlighted(bool highlighted);
}

/// <summary>
/// What an <see cref="InteractionRay"/> needs from the body that carries it: where it looks from and how it
/// moves (held objects follow that motion). Implemented by the first-person controller; any other
/// character (third person, vehicle seat, AI) can implement it too.
/// </summary>
public interface IInteractionViewer
{
    Vector3 ViewOrigin { get; }

    /// <summary>View orientation; -Z is forward.</summary>
    Basis ViewBasis { get; }

    Vector3 ViewerVelocity { get; }
}
