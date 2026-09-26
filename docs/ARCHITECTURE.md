# Architecture

Drone Star Studio is split into an **engine-free core** (pure C#, `noEngineReferences`) and a thin
**Unity layer**. Everything that decides where a drone is, what colour it shows and whether the show
is safe lives in the core, which compiles under plain .NET and is tested in seconds.

```
 ShowEditSession ──edit──▶ ShowDocument ──CompileJob (one step per frame)──▶ CompiledShow ──▶ SafetyValidator
   undo/redo, merge          cues, limits,       │ FormationGenerator  (slots per cue)         (time-sliced)
   dirty tracking             launch pad         │ Hungarian ≤ 600 / AuctionAssignment above (cached)
                                                 └ timeline: Ground ▸ Takeoff ▸ (Transit ▸ Hold)* ▸ Return ▸ Landing ▸ Ground
 Unity:  ShowStudioApp ─ samples CompiledShow each frame ─▶ DroneSwarmRenderer / DemoDirector / StudioUI
```

## Core (`Assets/DroneStar/Core`)

| Area | Types | Notes |
|---|---|---|
| Model | `ShowDocument`, `Cue`, `FormationSpec`, `LightSpec`, `MotionSpec`, `SafetyLimits`, `LaunchPadSpec`, `ShowSanitizer` | Every edit and every load is clamped to `ShowBounds`, so NaN/negative/huge values never reach the planner. |
| Formations | `FormationGenerator`, `ShapeLibrary`, `StrokeFont`, `CustomShapes`, `SpatialGrid` | Always returns exactly *N* slots, pairwise ≥ spacing. Filled shapes use a hex lattice whose pitch is bisected to fit *N*, with a sampled rim for crisp edges (outlined fill); outlines use arc-length sampling capped by spacing; parametric shapes (sphere, torus, grid, wave) binary-search the largest count that keeps spacing. 3D models come from `ShapePack.bytes`: 12,288 coloured points per model (Draw_in_3D's 4,096 plus midpoints between near neighbours) in farthest-point order, so any prefix covers the whole model evenly and greedy spacing still packs well at 4,096 drones; the generator walks that order and keeps points that respect the spacing, carrying each point's colour into the slot. Filled shapes test the rim clearance against a bucketed edge grid, so a 400 m filled shape lays out 4,096 drones in well under 0.1 s. Layouts are runtime-stable: bisected pitches settle away from the knife edge where a lattice point would sit on the region's edge, and arc-length allocation breaks ties between symmetric lines by index, so .NET, Mono and WebAssembly (whose maths differ in the last bit) produce the same slots and share baked assignments. A greedy thinning pass is the final guarantee. Surplus drones park dark on a horizontal grid behind the formation, outside the sphere the formation can sweep while it moves. |
| Planning | `AssignmentSolver`, `AuctionAssignment`, `ShowCompiler`, `CompileJob`, `CompiledShow`, `HoldMotion` | Slot-to-slot permutations (not drone-to-slot) are solved exactly (Hungarian) up to 600 drones and above that by an ε-scaling auction plus a pairwise-swap repair (every pair swap-optimal, which is what the CAPT proof needs), resumable across frames and across compiles (an edit does not restart a half-finished auction). Layouts are cached by a key of their inputs. Permutations are cached twice: by a fingerprint of both position sets (shared by any documents with the same layout, within one runtime) and by a *recipe* key of the inputs that produce them (specs, fleet, spacing, `FormationGenerator.Revision`, shape pack), which is identical on every runtime and is what `ExportCache` ships as `DemoAssignments.bytes`. Each recipe entry records its total cost and is used only if it still costs that on today's geometry; imported entries are never evicted, and tests fail if the baked file stops covering or matching the built-in show. The timeline is shared by all drones; sampling an instant is a binary search plus O(N). |
| Scaling | `ShowScaler` | Resizes a show for a new fleet: sizes grow with √(new/old), centres move about the pad with altitude kept clear of the ground, spins and breathing slow by the same factor (edge speed ω·r stays put, acceleration drops), climbs grow, and ceiling/geofence grow to fit. |
| Lighting | `LightEngine`, `LedColor` | Pure function of (effect, slot, drone, time). *Model colours* shows each slot's baked colour with an optional sparkle. Transits cross-fade between the outgoing and incoming cue's effect. Parked slots are black. |
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
  LEDs, which tighten to the bulb within 55 m so the airframe carrying them stays visible; the same mesh
  drawn with `_REFLECTION` mirrors each LED about the water level and projects it onto the lake along the
  line of sight, so shore geometry still occludes it. Light trails are a line mesh fed by a 14-sample ring
  buffer. Airframes (`DroneAirframe`, after the Draw_in_3D Blender quadcopter) are instanced with
  `Graphics.DrawMeshInstanced`: the nearest 256 drones (128 on the web and phones) within 48 m get the
  ~3,000-triangle model, others out to 190 m a 160-triangle one. `DroneBody.shader` lights them with
  moonlight, sky ambient, the swarm's average colour, a key light riding with the camera and the drone's
  own LED bulb (per-instance colour), spins the blades about their hubs while airborne, and each drone
  leans into its smoothed velocity.
- **NightEnvironment** — procedural lake, shore, trees, lanterns, launch barge with pad lights and hills,
  under a very dark blue sky over black-blue water; fixed seed, no downloaded assets. The skyline is four
  rows of turned buildings: waterfront blocks, mid-rise, and towers with setbacks, spires, pyramid crowns,
  round towers and rooftop plant, plus street lights along the far embankment.
- **CityWindows.shader** — moonlight and sky/ground ambient per face, corner and ground shading, three
  facade types (punched windows, glass curtain wall, ribbon windows), sky reflections in glass, crown
  lighting on some towers. Far away, windows merge into larger groups that stay crisp points of light
  instead of shimmering or washing out.
- **ShowCameraRig** — orbit/pan/zoom (mouse and touch), audience and aerial views, cinematic poses, and
  orbiting a moving point (the close-up camera follows one drone until the user pans or frames).
- **DemoDirector** — title card, a launch shot at eye level among the pads, one shot per scene (orbit,
  shore push-in, lake-level, aerial, dolly; every fifth scene from the third opens a few metres from one
  drone and pulls back to reveal the shape) with cuts at scene changes, captions, soundtrack
  (`AmbientScore`, synthesised in chunks) and end card.
- **StudioUI** — UI Toolkit built in code, styled by `Studio.uss`; icons are drawn with `Painter2D`.
- **ShowLibrary / WebBridge** — saves in `persistentDataPath` on desktop and `PlayerPrefs` (IndexedDB)
  on the web; downloads and file picking in the browser go through `DroneStarBridge.jslib`.

## Generated assets

`ProjectBuilder.RebuildAll` recreates every material, the post-processing profile (bloom, neutral
tonemapping, colour adjustments, vignette), the UI panel settings and the scene, and configures URP
(HDR, 4× MSAA, no shadows) and the player. Nothing in the scene is hand-placed, so the project can be
regenerated from source at any time.
