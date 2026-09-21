# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository overview

This repo contains two things:

1. A Unity project (`Assets/`, `Packages/`, `ProjectSettings/`) implementing a 3D Flappy-Bird-style game (a seagull flying forward, dodging pillar obstacles, with optional webcam head/hand motion control).
2. `python_game/`: a from-scratch **port** of that Unity game to Python (Panda3D), built by reading the Unity `.cs` scripts and the scene YAML directly (there is no Unity→Python auto-converter — every value was manually re-derived from the C# source and `Assets/Scenes/SampleScene.unity`).

When changing gameplay behavior, the Unity C# scripts under `Assets/New Folder/` are the source of truth for what a value *should* be — check them (and the scene file) before changing a constant in `python_game/`, rather than guessing.

## Running the Python port

Deps are pinned in `python_game/requirements.txt` and installed into an isolated
venv at `.venv/` (gitignored) — `mediapipe` previously force-downgraded system-wide
`numpy`/`protobuf` when installed globally, which broke unrelated tools (streamlit,
tensorboard, wandb, aider-chat), so don't install these packages into the system
Python again.

Setup (once):
```
python -m venv .venv
.venv/Scripts/python.exe -m pip install -r python_game/requirements.txt
```

Run from the repo root (not from inside `python_game/`, since it's a package with relative imports):

```
.venv/Scripts/python.exe -m python_game.main
```

Controls: Space = start/jump, Left/Right arrows = steer, `M` = toggle webcam motion control, `R` = restart.

There is no build step, linter, or test suite in this repo — `.venv/Scripts/python.exe -m py_compile python_game/*.py` is the only sanity check currently used before considering a change done.

### Windows path gotcha

Panda3D's loaders (`loadTexture`, `loadSfx`, `loadFont`) reject native Windows backslash paths and print a warning + fail silently/throw. Always convert with `panda3d.core.Filename.from_os_specific(path)` before passing a `os.path.join(...)`-built path to any Panda loader call.

## Architecture of `python_game/`

Each module is a fairly direct port of one Unity script (or one concern split out of `BirdController.cs`). None of them import Panda3D except `main.py`, `geometry.py`, and `seagull_loader.py` — the physics/gameplay logic is kept engine-agnostic and unit-testable in isolation.

- `bird.py` — port of `BirdController.cs`'s physics/state (gravity, flap, side-move, tilt, score/best-score). Pure Python, no Panda3D dependency; `main.py` reads `Bird.position`/`.roll` each frame and pushes them onto the Panda `NodePath`.
- `wall.py` — port of `NewMonoBehaviourScript.cs` (**not** `BoundaryWall.cs` — the actual obstacle spawner in the Unity project is the oddly-named `NewMonoBehaviourScript`). Spawns 9-column pillar "chunks" ahead of the player with a random per-column gap, despawns chunks behind the player.
- `boundary.py` — port of `BoundaryWall.cs`: clamps the player's X position and computes the left/right shield fade alpha.
- `motion_capture.py` — webcam motion control, running **in-process** (no subprocess, no UDP — an earlier design used a separate `motion_tracker.py` process talking over a UDP socket, which was scrapped because it made debugging camera issues much harder and forced a second, separate preview window). Uses MediaPipe Pose (`mediapipe.solutions.pose`) to track the nose (steer) and both wrists (jump gesture: rapid up/down wrist oscillation *specifically next to the head* within `NEAR_HEAD_X`/`FLAP_WINDOW_SEC`/`FLAP_MIN_PEAKS`/`FLAP_MIN_RANGE`, tuned in that file). This is a **deliberate deviation** from the original `MotionController.cs`, which inferred jump from a head-bob velocity spike — replaced with an explicit hand-flap-beside-head gesture per product decision. `mediapipe` pins `numpy`/`protobuf` to versions that conflict with other tools if installed system-wide — this is why deps live in `.venv/` now (see "Running the Python port" above); don't reintroduce a global install.

  Steering/flap sign conventions (head angle → move_x → roll → flap direction) got flipped back and forth multiple times during development — see `python_game/CONVENTIONS.md` before touching any of that chain again.
  - `main.py` calls `MotionCapture.update(learning_rate, dt)` every frame (only while motion mode is on), which returns a `jumped` bool and updates `.last_frame` (a BGR numpy frame with the MediaPipe skeleton drawn on it) — `main.py` blits that frame onto a small `OnscreenImage` in the corner of the game window as a live preview, so there is exactly one window, not two.
  - Camera backend gotcha found the hard way: on this machine, forcing `cv2.CAP_DSHOW` and/or forcing `CAP_PROP_FOURCC` to `MJPG` produced a corrupted/garbled frame or a solid-black frame respectively. Plain `cv2.VideoCapture(0)` with no backend/format overrides is what actually works — don't reintroduce backend/codec forcing speculatively; if a capture problem comes up again, get an actual screenshot of the failure before changing capture parameters.
- `seagull_loader.py` — a hand-rolled parser for `Assets/New Folder/Seagull.fbx`. This is an **ASCII FBX 6.1** file (old Blender exporter format), which neither Panda3D nor the `assimp-py` binding installed here can load (assimp errors with "FBX-DOM unsupported, old format version"). The loader regex-extracts the `Vertices`/`PolygonVertexIndex`/`Normals`/`UV`/`UVIndex` arrays directly from the text and builds a Panda3D `Geom` by hand (fan-triangulating each polygon). It ignores the skeleton/skin/animation data in the file — only the static bind-pose mesh is used. There is no texture (`Seagull.png` is referenced by the FBX via an absolute path but was never actually committed to this repo), so the model renders as flat-shaded grey.
- `geometry.py` — procedural box mesh builder (`make_box`) used for pillars, ground-less scenery, and UI-adjacent 3D shapes, since there's no reusable primitive-cube asset available the way Unity's `GameObject.CreatePrimitive(PrimitiveType.Cube)` provides. Includes UVs so a texture can be applied (used for the boundary shield texture).
- `prefs.py` — JSON-file-backed replacement for Unity's `PlayerPrefs` (`get_int`/`set_int`/`get_float`/`set_float`/`save()`), stored at `python_game/save.json` (gitignored).
- `main.py` — the Panda3D `ShowBase` app tying everything together: scene/lighting setup, camera (fixed offset behind+above the bird, matching the Unity Main Camera's child-transform offset and FOV), the DirectGUI start-menu/HUD/restart-button UI (positions are derived from the real `RectTransform` anchor/anchoredPosition/sizeDelta values read out of `SampleScene.unity`, converted to Panda `aspect2d` coordinates — see the conversion comment/`k` constant near `_build_ui`), and audio (jump SFX + looping BGM, volumes wired to prefs).

## Known intentional gaps vs. the Unity original

These were explicitly decided against, not overlooked — don't "fix" them without checking with the user first:

- Decorative scenery (BOXOPHOBIC skybox/nature pack, Mellow Fox building kit, Enxemac nature pack) is **not** ported: these are paid Unity Asset Store packages, and reproducing their meshes/textures in a separate non-Unity codebase was judged out of scope on licensing grounds, independent of whether the files are locally present.
- The seagull has no wing-flap animation (skeleton/animation data in the FBX is parsed-and-discarded, not applied) — same licensing reasoning (the model is a paid asset, `SailCharacterPack`), plus the source file is on OneDrive on-demand storage and isn't always locally hydrated.
- The `BoundaryWall.cs` shield visual uses a real texture from `Assets/Shield Shader FREE/Textures/Shader/Hex Tile.png` (this pack is free) rather than its actual custom shield shader/material, which was judged too complex to reimplement faithfully.
