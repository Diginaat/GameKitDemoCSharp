# gamekit_demo

A playable demo map for the GameKit modules. Every feature has a **station**: a small scene made
only of GameKit nodes, with a sign that tells you which nodes and settings it uses.

Uses: every other `gamekit_*` module. Nothing in GameKit uses this folder, so you can delete it.

## What is in the folder

| Path | What it is |
| --- | --- |
| `scenes/gamekit_demo.tscn` | The demo map: player, HUD, weather, sun, paths, trees, lamps and all the stations |
| `stations/*.tscn` | One scene per feature. Drag any of them into your own map |
| `materials/*.tres` | Ready-made surfaces (grass, stone, wood, metal, ice, mud and more). They all get wet in rain |
| `shaders/demo_surface.gdshader` | The one shader behind those materials: colour + pattern (plain, tiles, planks, stone blocks, grass), world space, wet in rain |
| `scripts/DemoHud.cs` | Crosshair, prompt, ammo, weather and lens names, controls panel. Game UI example |
| `scripts/PoolCaustics.cs` | Gives the pool wall material its water position, so the caustics stay right when the pool is moved or turned |

## Stations

| Scene | Feature | Main nodes |
| --- | --- | --- |
| `surfaces.tscn` | Footstep surfaces, friction | Colliders with metadata `surface` |
| `stairs.tscn` | Stairs and a 25° slope | Plain `StaticBody3D` |
| `interaction.tscn` | Door, button, crates | `SlidingDoor`, `InteractableButton`, `RigidBody3D` on layer Props |
| `ladder.tscn` | Ladder tower | `LadderVolume` |
| `platforms.tscn` | Elevator and turntable | `MovingPlatform` |
| `abilities.tscn` | Mantle blocks, slide beams | Plain geometry (the abilities are nodes on the player) |
| `weather.tscn` | Shelter that stays dry in rain | Materials with weather wetness |
| `pool.tscn` | Swimmable pool | `WaterVolume`, `pool_tile.gdshader`, `PoolCaustics` |

The front of a station (where its sign stands) is its local **+Z**. You can move and turn a station
as one piece, and you can open it and change anything inside.

## Use it in your project

1. Copy `addons/gamekit_*` (all of them, including this one) into your project.
2. Copy these sections from this project's `project.godot` into yours:
   - `[physics]`: the PhysX engine and physics interpolation;
   - `[layer_names]`: the collision layer names;
   - `[shader_globals]`: the weather globals used by the wet materials;
   - `[input]`: `cycle_weather` (the other actions are added by the player at runtime).
3. Build the C# project once (Build button, or `dotnet build`).
4. Open `addons/gamekit_demo/scenes/gamekit_demo.tscn` and press **F6**.

To use one station only, drag its `.tscn` from `stations/` into your map. A map needs a player
(`gamekit_viewmodel/scenes/fps_player_armed.tscn`). It also needs a `WorldEnvironment` and a sun.
Add a `Weather` node too if you want rain on the materials. The demo map shows all of this.

## Change it

- **Sign text:** open a station, select `Sign > Title` or `Sign > Text`, and edit **Text**.
- **Look of a surface:** open a material in `materials/` and change its shader parameters.
  `Surface Pattern` picks plain, tiles, planks, stone blocks or grass. `Surface Scale` sets the size of
  a tile, plank or block in metres. The pattern is in world space, so you can resize boxes freely.
- **New surface:** duplicate a material `.tres` and change it.
- **Footsteps and friction:** set metadata `surface` on the collider (1 Concrete, 2 Metal, 3 Wood,
  4 Grass, 5 Ice, 6 Mud).
- **HUD:** `DemoHud` is an example. Copy it into your game and change it. **L** cycles its
  `Lens Profiles`, **H** hides the controls panel. You can rebind both in the Input Map
  (`demo_cycle_lens`, `demo_toggle_help`).

## Controls

WASD move, mouse look, Space jump, Ctrl crouch, Shift sprint, Alt walk. Shift + Ctrl slides.
Space + W at a ledge mantles. E uses or picks up, LMB fires or throws, R reloads, 1/2/3 pick a weapon.
F7 changes the weather, L changes the lens look, H hides help, Esc releases the mouse.
