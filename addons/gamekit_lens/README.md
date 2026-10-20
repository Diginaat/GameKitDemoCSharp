# gamekit_lens

Photographic camera finish: lens distortion, chromatic aberration, depth of field, film halation,
highlight bloom, grading, LUT, vignette, sensor grain and lens flares (sun + opt-in lights).
Depends on: `gamekit_core`.

| Type | What it is |
| --- | --- |
| `CameraLens` | Node3D, child of a `Camera3D`. Draws the lens pass and the flare overlay for that camera |
| `LensProfile` | Resource: every setting of the look, grouped in the Inspector |
| `profiles/natural.tres` | Subtle default: light vignette, edge fringes, halation, fine sensor noise, sun flare |
| `profiles/film.tres` | Stronger: warm grade, film grain, far depth of field, slight barrel distortion |
| `profiles/clean.tres` | Everything off (accessibility / comparison) |
| `shaders/lens_post.gdshader` | The full-screen lens pass |
| `shaders/lens_flare.gdshader` | Flare overlay (canvas, additive) |

## Setup

1. Add a `Node3D` with `CameraLens` as a child of your `Camera3D`. `fps_player.tscn` already has one
   (`.../Camera3D/Lens`).
2. Set `Profile` to a `.tres` from `profiles/`, or create your own `LensProfile`. On the FPS player,
   the camera settings choose it: `PlayerCameraSettings.Lens` (Modern = natural, Classic and Reduced
   Motion = clean), applied whenever `FpsPlayerController.CameraSettings` changes.
3. Optional: `Sun` (empty = first `DirectionalLight3D` in the scene). Lights that should flare: add them
   to the group `lens_flare` (any `OmniLight3D`/`SpotLight3D` with energy above `FlareEnergyThreshold`).

Change a profile at runtime: `lens.Profile = GD.Load<LensProfile>("res://...")`. After editing
values of the assigned profile from code, call `lens.Refresh()`.

## How it works (and limits)

- The pass is a 2 x 2 quad that its vertex shader stretches over the screen. It is on
  `RenderLayers.Viewmodel` (19), which planar reflections, the rain cover map and the rain particle
  collision exclude, so only the player camera sees it. It draws only while its camera is current.
- It draws first in the transparent pass. Godot copies the screen texture before transparents, so
  transparents (water surface, rain, particles, Label3D) are drawn on top without lens effects. That is
  why distortion is off in `natural.tres`: undistorted transparents would shift against distorted
  geometry near the frame edges.
- The image it reads is linear HDR before the Environment's exposure and tonemapper. Grading multiplies
  (white balance, contrast around middle grey); grain is relative to the pixel. Keep `Lift` tiny.
- It never changes `Camera3D.Attributes`: exposure and engine DOF stay with the environment and
  `gamekit_weather`. The sun flare scales with the sun's energy, so storms dim it.
- Cost: halation and DOF are the expensive parts (many texture taps per pixel). Turn them off in a
  profile for low-end targets.

## Credits

Ported to C# and refined from EpicCameraX by Diginaat / DevKine (MIT, see `LICENSE-EpicCameraX.txt`).
Fixes against the original: distortion applied when enabled (inverted test), reversed-Z depth for DOF
and halation, grading and grain in linear light, unused flare code removed, no scene-wide light scan,
no CameraAttributes override, pass hidden from other cameras.
