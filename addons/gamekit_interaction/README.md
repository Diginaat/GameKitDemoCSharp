# gamekit_interaction

Use, pick up and throw. Depends on: `gamekit_core`.

| Type | What it is |
| --- | --- |
| `IInteractable` | Anything usable: `GetPrompt`, `CanInteract`, `Interact(Node interactor)` |
| `IHighlightable` | Optional focus feedback |
| `IInteractionViewer` | What the ray needs from its carrier: eye position, view basis, velocity |
| `InteractionRay` | Node: focus ray, `Use()`, `Throw()`, `Drop()`, impulse-based holding; events `PromptChanged`, `PickedUp`, `Dropped` |
| `InteractableButton` | StaticBody3D: calls `Interact` on its `Target` |
| `SlidingDoor` | AnimatableBody3D: slides by `OpenOffset` |

## Setup

1. Add an `InteractionRay` node as a child of a `PhysicsBody3D` that implements `IInteractionViewer`
   (`FpsPlayerController` does).
2. Call `Use()` / `Throw()` from your input code; `InteractionRay` reads no input itself.
3. Show `PromptChanged` text in your HUD.

## Your own interactables

Implement `IInteractable` on the collider's node or on one of its two parents:

```csharp
public partial class Lever : StaticBody3D, IInteractable
{
    public string GetPrompt(Node interactor) => "Pull lever";
    public bool CanInteract(Node interactor) => true;
    public void Interact(Node interactor) { /* ... */ }
}
```

Cast `interactor` to your game's type when the object needs more (inventory, team).

Rigid bodies on the ray's `Mask` (World and Props by default) up to `MaxPickupMass` can be picked up.
