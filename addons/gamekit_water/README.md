# gamekit_water

Swimmable, rendered water for walled pools and basins. Depends on: `gamekit_core`.
**Engine requirement:** the custom Godot build with the PhysX module (`PhysXWaterSurface3D`). Without a
RenderingDevice (headless, Compatibility renderer) it falls back to a still surface with the same shader.

| Type | What it is |
| --- | --- |
| `WaterVolume` | Area3D: swimming volume; builds its own surface, underwater view and reflection |
| `IWaterUser` | Implemented by bodies that react to water (`FpsPlayerController` does) |
| `PlanarReflection`, `PlanarReflectionSettings` | Pool mirrored in the underside; tunables in a `.tres` |
| `shaders/pool_water.gdshader` | Surface: refraction, absorption, Fresnel, Snell's window (`window_ior`) |
| `shaders/underwater_view.gdshader` | Per-pixel underwater fog, exact waterline split |
| `shaders/pool_tile.gdshader` | Tile material with sun-projected caustics |
| `resources/pool_reflection.tres` | Default reflection settings |

## Setup

1. Area3D with `WaterVolume` and one `CollisionShape3D` child with a `BoxShape3D`. The box's top is the
   water surface; keep the node unrotated.
2. Layer Triggers (4); mask Player (2) and Props (3) so swimmers swim and props ripple the surface.
3. Wall it in on all four sides: the surface spans exactly the box's top face.
4. Optional: assign `Reflection` (e.g. `resources/pool_reflection.tres`); `Rendered = false` gives an
   invisible swim volume.
5. Optional caustics on the pool walls/floor: a ShaderMaterial with `pool_tile.gdshader` and
   `water_surface_y`, `water_rect` (min x, min z, max x, max z), `sun_direction` set to match.

## Notes

- Gameplay uses the flat box top (`SurfaceY`); waves are visual.
- Render layer 20 (Water) holds the water meshes; keep it out of other mirror cameras.
- Engine issues and workarounds: `docs/PHYSX_INTEGRATION.md` (issues 3–5).
