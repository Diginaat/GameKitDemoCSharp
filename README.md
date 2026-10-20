# GameKit Demo

A stand-alone Godot 4 (C#) project with a playable showcase of every GameKit module. You can walk
through it, read the signs, and copy any part into your own game.

![Demo map](screenshots/overview.png)

| | |
| --- | --- |
| ![First person](screenshots/first_person.png) | ![Pool](screenshots/pool.png) |
| ![Storm](screenshots/storm.png) | |

## Run it

1. Open this folder with the PhysX Godot editor (`GodotEnginePhysx`, 4.8 mono). The water module
   needs this engine.
2. Press **Build**, then **F5**. The main scene is `addons/gamekit_demo/scenes/gamekit_demo.tscn`.

From a terminal:

```
dotnet build
..\GodotEnginePhysx\godot.windows.editor.x86_64.mono.exe --path .
```

## What is inside

- `addons/gamekit_*`: the GameKit modules, copied from the `GameClient` project.
- `addons/gamekit_demo`: the demo map, one scene per feature station, ready-made materials and the
  demo HUD. See [its README](addons/gamekit_demo/README.md) to use and change it.

## Add it to your project

See [addons/gamekit_demo/README.md](addons/gamekit_demo/README.md#use-it-in-your-project). In short:
copy `addons/gamekit_*`, merge four `project.godot` sections, build, and open the demo scene.

## Keeping the modules up to date

The modules here are a copy. After changing them in `GameClient`, copy the `addons/gamekit_*` folders
again. Copy every module except `gamekit_demo`, which only lives here.
