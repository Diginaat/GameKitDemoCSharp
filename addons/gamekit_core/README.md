# gamekit_core

Shared foundations every other GameKit module may use. No dependencies.

| Type | What it is |
| --- | --- |
| `CollisionLayers` | Physics layer bits: 1 World, 2 Player, 3 Props, 4 Triggers |
| `RenderLayers` | Visual layer bits: 19 Viewmodel, 20 Water (left out of planar reflections) |
| `Surfaces`, `SurfaceType`, `SurfaceInfo` | Surface table (friction, speed, footstep sound) read from collider metadata `surface` |
| `PlaceholderSounds` | Synthesized placeholder audio (cached; call `ClearCache()` before quitting tests) |
| `shaders/prototype_grid.gdshader` | World-space grid for blocking out levels |

## Setup

- Name the physics layers in Project Settings > Layer Names > 3D Physics as in `CollisionLayers`.
  The values are what matters; the names are for the editor.
- Tag a collider with a surface: Inspector > Metadata > add `surface` (int) = a `SurfaceType` value,
  or call `Surfaces.Tag(node, SurfaceType.Metal)` in code.

## Extending

- Add surfaces: append to `SurfaceType` and to the table in `Surfaces.cs` (keep the order). A large
  project can replace the table with a Resource per surface; the lookup stays `Surfaces.FromCollider`.
