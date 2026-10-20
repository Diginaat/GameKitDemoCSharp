# gamekit_fps_controller

Half-Life 2 / Source-style first-person controller on `CharacterBody3D`.
Depends on: `gamekit_core`, `gamekit_interaction`, `gamekit_world`, `gamekit_water`.

## Use it

Instance `scenes/fps_player.tscn` (no weapons) or `gamekit_viewmodel/scenes/fps_player_armed.tscn`.
Set `Movement` (a `PlayerMovementSettings` `.tres`) and `CameraSettings` (`PlayerCameraSettings`).

```
FpsPlayer (FpsPlayerController)   layer Player (2), mask World | Props
├── CollisionShape3D   capsule, origin at the feet
├── GroundCheck        RayCast3D (surface lookup)
├── Abilities          MovementAbility children; order = priority
│   ├── Mantle  ├── Swim  ├── Ladder  └── Slide
├── Input              PlayerInput (mouse/keyboard -> PlayerCommand)
├── Interaction        InteractionRay (gamekit_interaction)
├── Footsteps          PlayerFootsteps
└── CameraRig          PlayerCameraController > Pitch > Effects (PlayerCameraEffects) > Camera3D
```

| Resource | Contents |
| --- | --- |
| `resources/modern_aaa.tres`, `source_classic.tres` | Movement profiles |
| `resources/camera_modern.tres`, `camera_classic.tres`, `camera_accessible.tres` | Camera presets (accessible = no motion effects) |

## Extension points

- **Abilities:** subclass `MovementAbility`, add it under `Abilities`. See `docs/INTEGRATION.md`.
- **AI, network, replays:** set `UseExternalCommand = true` and write `Command` each physics tick.
- **Events:** `Motor.Jumped`, `Landed`, `StateChanged`, `VisualStep`; `Stance.Changed`; `SwimMovement.UnderwaterChanged`.
- **Remove parts:** Footsteps, Interaction, camera effects, and any ability node can be deleted.

Input actions are added at runtime if missing (`InputActions.EnsureDefaults`); define them in the
Input Map to rebind.
