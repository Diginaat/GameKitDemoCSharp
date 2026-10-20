# gamekit_world

Level building blocks. Depends on: `gamekit_core`.

| Type | What it is |
| --- | --- |
| `LadderVolume` | Area3D: climbable region; tells `ILadderUser` bodies when they enter and leave |
| `ILadderUser` | Implemented by bodies that climb (`FpsPlayerController` does) |
| `MovingPlatform` | AnimatableBody3D: cosine travel (`Travel`, `Period`) and spin (`SpinDegreesPerSecond`) |

## Ladder setup

1. Area3D with `LadderVolume` and a `BoxShape3D` covering the climbable face.
2. Layer Triggers (4), mask the climbers' layer (Player, 2). `Monitorable` can be off.
3. Rotate it so its local +Z points away from the wall, toward the climber.
4. Make it a little taller than the top ledge so climbers hop off onto it.

## Moving platforms

Any `AnimatableBody3D` (with *Sync to Physics* on) moved by animation or code carries the player.
`MovingPlatform` is a ready-made one; put it on layer World (1).
