# gamekit_viewmodel

First-person weapons. Depends on: `gamekit_core`, `gamekit_interaction`, `gamekit_fps_controller`.

| Type | What it is |
| --- | --- |
| `PlayerViewmodel` | Node3D under the camera: weapon switching (1/2, 3 holsters), sway, bob, landing dip, lowered poses |
| `Weapon` | Node3D: fire/reload, hitscan impulse, recoil to the camera; placeholder mesh and clips unless it has an `AnimationPlayer` |
| `scenes/fps_player_armed.tscn` | `fps_player.tscn` + Viewmodel with a rifle and a pistol |

## Setup

Use `fps_player_armed.tscn`, or add a Node3D with `PlayerViewmodel` under the player's `Camera3D` and
`Weapon` children under it.

## Your own weapons

- **Assets:** give a `Weapon` node your mesh and an `AnimationPlayer` with `equip`, `holster`, `fire`
  and `reload` clips.
- **Render layer:** put weapon meshes on render layer 19 (`RenderLayers.Viewmodel`) so mirror cameras
  skip them.
- **Damage:** subscribe to `Weapon.Fired`, or derive from `Weapon` for projectiles.
