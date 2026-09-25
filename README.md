# Drone Star Studio

**Design, check and present drone light shows — built in Unity 6 for the Mai Drone Star Group.**

[**▶ Watch the demo run in your browser**](https://s0lluxx26.github.io/mai_drone_star_group/?demo=1) ·
[Open the studio](https://s0lluxx26.github.io/mai_drone_star_group/)

![A Night of Stars — the flagship demo show over the lake](docs/media/hero.png)

Drone Star Studio is a complete show editor: lay out formations, colour them, give them motion,
and the studio plans every flight path, checks the whole show for safety, and plays it back over a
night-time lake with reflections, light trails, a city skyline and a generated soundtrack.
Press **Demo Run** to watch the show as a cinematic presentation with titles, scene captions and camera cuts.

| Editing a show | The demo run | Safety check |
|---|---|---|
| ![Editor](docs/media/editor.png) | ![Demo](docs/media/demo.png) | ![Safety](docs/media/safety.png) |

## What you can do

- **12 formation families** — grid curtain, ring, sphere, star, heart, helix, spiral galaxy, wave, cube,
  text (A–Z, 0–9; accents such as *Hà* fold to *HA*), flower and butterfly — filled or outlined,
  with depth layers, turn/tilt, size and position.
- **9 light effects** — solid, gradient, rainbow, chase, twinkle, pulse, radial, fire, off — from a
  vivid palette or any hex colour, with tempo and brightness.
- **Motion while holding** — turntable, roll, breathe and wave, eased in and out so drones never jerk.
- **Automatic flight planning** — every change is re-planned in the background: shortest legal
  transition times, optimal drone-to-slot matching and parking of surplus drones.
- **Safety check** — the whole show is flown in simulation: closest approach between every pair of
  drones, speed, acceleration, ceiling, ground clearance, geofence and battery time, with each
  finding one click away on the timeline.
- **Undo/redo**, save on the device (in the browser's storage on the web), import/export `.dronestar.json`, export per-drone
  **trajectories (CSV)** and a **flight report (Markdown)**.
- **Demo Run** — title card, a camera shot per scene, captions, soundtrack and an end card.

## How the flight planning works

1. **Formations with guaranteed spacing.** Each shape is sampled so neighbouring drones are at least
   √2 × the minimum separation apart (hexagonal lattices for filled shapes, arc-length sampling for
   outlines, parametric layouts for spheres, tori and cubes). Drones that do not fit park, dark,
   on a grid behind the shape instead of crowding it.
2. **Optimal assignment.** Between two formations, drones are matched to their new slots with the
   Hungarian algorithm, minimising the total *squared* distance. Assignments are cached by geometry,
   so editing one cue only re-solves the two transitions that touch it.
3. **Synchronised straight lines.** All drones leave and arrive together along straight lines with a
   minimum-jerk profile (zero velocity and acceleration at both ends). Optimal assignment + synchronised
   straight lines + √2 spacing is the CAPT condition (Turpin, Michael & Kumar, 2014) that keeps
   transitions collision-free.
4. **Legal timing.** Auto transitions take the shortest time for which the peak speed
   (1.875·d/T) and peak acceleration (5.77·d/T²) stay inside the limits, plus 8 % head-room.
5. **Independent verification.** The safety check does not trust any of the above: it re-flies the
   compiled show at 20 Hz (10 Hz on the web) and tests the closest approach of every nearby pair
   between samples.

The flagship demo (360 drones, 12 scenes, 4 min 5 s) passes with a closest pass of 1.56 m
against a 1.5 m limit, a top speed of 7.4 m/s against 8 m/s and peak acceleration of 3.5 m/s²
against 4 m/s². See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design and
[docs/REVIEW.md](docs/REVIEW.md) for the review log.

## Using the studio

| | |
|---|---|
| **Left** | Show cues — select, reorder, duplicate, delete; add a formation at the bottom |
| **Right** | Inspector: **Cue** (shape, timing, light, motion), **Show** (drones, limits, launch pad), **Safety** |
| **Bottom** | Transport, speed, loop, camera (orbit / audience / aerial), trails, timeline scrubber |

| Keys | Action |
|---|---|
| Space | Play / pause |
| ← → (Shift) | Step 1 s (5 s) |
| ↑ ↓ | Previous / next cue |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+S | Save |
| Del | Delete the selected cue |
| F | Frame the selected formation |
| 1 / 2 / 3 | Orbit / audience / aerial camera |
| T | Light trails |
| D / Esc | Start / leave the demo run |
| Mouse | Drag to orbit, right-drag to pan, wheel to zoom |

## Project layout

```
Assets/DroneStar/
  Core/        Engine-free show model and algorithms (no UnityEngine references)
  Runtime/     Unity app: controller, renderer, venue, camera, demo director, UI, platform I/O
  Shaders/     URP shaders: LED glow + water reflections, night sky, lake, city windows, trails
  Editor/      ProjectBuilder: generates materials, post-processing, UI panel and the scene; builds
  Tests/       EditMode (core + project checks) and PlayMode (the running studio)
  UI/          Studio.uss stylesheet and theme
Assets/WebGLTemplates/DroneStar/   Branded web loader
tests/         dotnet projects that compile Core and its tests outside Unity
docs/          Architecture notes and screenshots
```

## Building and testing

Requires **Unity 6000.6.3f1** (Unity 6.6) with Web and Windows build support, and the .NET 9 SDK
for the fast core tests.

```bash
# Core algorithms (seconds, no Unity needed)
dotnet test tests/DroneStar.Core.Tests

# Regenerate materials, post-processing, UI panel and the scene
Unity -batchmode -quit -projectPath . -executeMethod DroneStar.EditorTools.ProjectBuilder.RebuildAll

# Unity test suites
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml

# Players
Unity -batchmode -quit -projectPath . -executeMethod DroneStar.EditorTools.ProjectBuilder.BuildWebGL -output Builds/WebGL
Unity -batchmode -quit -projectPath . -executeMethod DroneStar.EditorTools.ProjectBuilder.BuildWindows -output Builds/Windows/DroneStarStudio.exe
```

A Windows build can capture screenshots unattended:
`DroneStarStudio.exe -capture shots -captureTimes 20,60,120 [-demo] [-captureCamera audience]`.

### Unity MCP

The project includes [MCP for Unity](https://github.com/CoplayDev/unity-mcp) (`com.coplaydev.unity-mcp`),
so an MCP client such as Claude Code can drive the open editor: in Unity choose
**Window ▸ MCP for Unity ▸ Configure All Detected Clients**, or register it by hand with
`claude mcp add --scope local --transport http UnityMCP http://127.0.0.1:8080/mcp`.

## Show file format

Shows are plain JSON (`*.dronestar.json`, format tag `dronestar-show`, version 1): title, author,
drone count, safety limits, launch pad and an ordered list of cues, each with a formation, light,
motion and timing block. Files are validated and clamped on load; unknown fields are ignored.

## License

MIT — see [LICENSE](LICENSE).
