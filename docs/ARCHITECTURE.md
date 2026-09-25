# Architecture

Drone Star Studio is split into an **engine-free core** (pure C#, `noEngineReferences`) and a thin
**Unity layer**. Everything that decides where a drone is, what colour it shows and whether the show
is safe lives in the core, which compiles under plain .NET and is tested in seconds.

```
 ShowEditSession ──edit──▶ ShowDocument ──CompileJob (one step per frame)──▶ CompiledShow ──▶ SafetyValidator
   undo/redo, merge          cues, limits,       │ FormationGenerator  (slots per cue)         (time-sliced)
   dirty tracking             launch pad         │ AssignmentSolver    (Hungarian, cached)
                                                 └ timeline: Ground ▸ Takeoff ▸ (Transit ▸ Hold)* ▸ Return ▸ Landing ▸ Ground
 Unity:  ShowStudioApp ─ samples CompiledShow each frame ─▶ DroneSwarmRenderer / DemoDirector / StudioUI
```

## Core (`Assets/DroneStar/Core`)

| Area | Types | Notes |
|---|---|---|
| Model | `ShowDocument`, `Cue`, `FormationSpec`, `LightSpec`, `MotionSpec`, `SafetyLimits`, `LaunchPadSpec`, `ShowSanitizer` | Every edit and every load is clamped to `ShowBounds`, so NaN/negative/huge values never reach the planner. |
| Formations | `FormationGenerator`, `StrokeFont`, `SpatialGrid` | Always returns exactly *N* slots, pairwise ≥ spacing. Filled shapes use a hex lattice whose pitch is bisected to fit *N*; outlines use arc-length sampling capped by spacing; parametric shapes (sphere, torus, grid, wave) binary-search the largest count that keeps spacing. A greedy thinning pass is the final guarantee. Surplus drones park dark on a horizontal grid behind the formation, outside the sphere the formation can sweep while it moves. |
| Planning | `AssignmentSolver`, `ShowCompiler`, `CompileJob`, `CompiledShow`, `HoldMotion` | Slot-to-slot permutations (not drone-to-slot) are solved and cached by a fingerprint of both position sets, so they do not depend on drone identities and survive unrelated edits. The timeline is shared by all drones; sampling an instant is a binary search plus O(N). |
| Lighting | `LightEngine`, `LedColor` | Pure function of (effect, slot, drone, time). Transits cross-fade between the outgoing and incoming cue's effect. Parked slots are black. |
| Safety | `SafetyValidator`, `ValidationReport` | Samples at 20 Hz; for every pair found in a spatial hash sized to the separation plus twice the largest step, computes the closest approach of the two linear motions between samples. Violations are merged into time intervals per kind. |
| I/O | `JsonValue`, `ShowSerializer`, `ShowExporter` | Reflection-free JSON (identical in the editor, IL2CPP/WebGL and .NET). Floats are written by shortest round-trip text. |
| Editing | `ShowEditSession` | Snapshot undo (compact JSON), merge keys for slider drags, rollback on exceptions, dirty flag compared with the last save. |

### Why the transitions are safe

For point robots of radius *R*, if start and goal positions are at least 2√2·R apart, the assignment
that minimises the sum of squared distances, flown along synchronised straight lines with the same time
parameterisation, keeps every pair at least 2R apart (CAPT). The studio uses 2R = minimum separation and
lays formations, the hover grid and the pads out at ≥ √2 × that. Hold motions preserve the guarantee:
turntable and roll are rigid rotations, breathe only expands, and parked drones stay outside the swept
sphere. The validator checks all of it independently anyway (for instance when a designer shortens a
transition by hand).

## Unity layer (`Assets/DroneStar/Runtime`)

- **ShowStudioApp** owns the session, compiles incrementally (10 ms budget per frame; the previous show
  keeps playing until the new one is ready), validates in 5 ms slices, drives the `PlaybackClock` and
  feeds the renderer. It also computes the brightness-weighted centroid the demo camera frames.
- **DroneSwarmRenderer** — one dynamic mesh of camera-facing sprites (`DroneGlow.shader`) for the
  LEDs; the same mesh drawn with `_REFLECTION` mirrors each LED about the water level and projects it
  onto the lake along the line of sight, so shore geometry still occludes it. Light trails are a line
  mesh fed by a 14-sample ring buffer; airframes are drawn with `Graphics.RenderMeshInstanced`.
- **NightEnvironment** — procedural lake, shore, trees, lanterns, launch barge with pad lights, city
  skyline (window shader with metre-space UVs) and hills; fixed seed, no downloaded assets.
- **ShowCameraRig** — orbit/pan/zoom (mouse and touch), audience and aerial views, and cinematic poses.
- **DemoDirector** — title card, one shot per scene (orbit, shore push-in, lake-level, aerial, dolly)
  with cuts at scene changes, captions, soundtrack (`AmbientScore`, synthesised in chunks) and end card.
- **StudioUI** — UI Toolkit built in code, styled by `Studio.uss`; icons are drawn with `Painter2D`.
- **ShowLibrary / WebBridge** — saves in `persistentDataPath` on desktop and `PlayerPrefs` (IndexedDB)
  on the web; downloads and file picking in the browser go through `DroneStarBridge.jslib`.

## Generated assets

`ProjectBuilder.RebuildAll` recreates every material, the post-processing profile (bloom, neutral
tonemapping, colour adjustments, vignette), the UI panel settings and the scene, and configures URP
(HDR, 4× MSAA, no shadows) and the player. Nothing in the scene is hand-placed, so the project can be
regenerated from source at any time.
