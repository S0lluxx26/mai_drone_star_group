# Review log

## Round 1 — 2026-09-25

Two independent reviews (show-core logic; Unity runtime, UI, shaders and tooling), plus visual review of
automated captures from the Windows player. Every finding below was fixed; regression tests are in
`Assets/DroneStar/Tests/EditMode/Core/ReviewRegressionTests.cs`.

### Show core

| # | Finding | Fix |
|---|---|---|
| 1 | Acceleration over the limit was only a warning, so an unsafe show could "pass" (e.g. a 1 s spinning hold at 20 m/s²). | Above 1.1 × the limit it is an error; the interval escalates if it worsens. |
| 2 | Custom points were unbounded: huge values crashed the assignment or made the validator run for hours. | Sanitizer clamps custom points to ±1.5 (normalised); sample counts and CSV buffers are overflow-safe. |
| 3 | Custom points were dropped from files and undo snapshots whenever the cue's shape was not Custom. | Points are always written when present. |
| 4 | `Clone` crashed on null cues/specs before the sanitizer could repair them. | Null-tolerant clones. |
| 5 | The Wave motion's smoothstep envelope started with an acceleration jump. | Minimum-jerk envelope (zero velocity and acceleration at the start). |
| 6 | A NaN validator step made the check take one sample and pass. | Non-finite steps fall back to the default. |
| 7 | Saving did not close an open slider-drag merge, so the saved state became unreachable by undo. | `MarkSaved` ends the merge group. |
| 8 | Colour edits below 8-bit precision were invisible to undo and dirty tracking. | Colours are rounded to file precision on edit. |
| 9 | JSON accepted `\u` escapes with whitespace, non-integer versions and comma-joined enum names; unpaired surrogates survived. | Strict hex parsing, integer versions, name-only enums, surrogate stripping. |
| 10 | Violations past 40 per kind were silently dropped. | A summary finding reports how many more exist. |
| — | Mono throws `OverflowException` for `1e999` where .NET returns ∞ (found by running the suite inside Unity). | Runtime-independent `TryParse`. |

### Unity layer

| # | Finding | Fix |
|---|---|---|
| 1 | The soundtrack clip was generated but never assigned, so only chimes played. | `music.clip = loop`. |
| 2 | Focused buttons and sliders also received Space/arrow shortcuts (double undo, play toggling back). | Buttons are not focusable; shortcuts pause while a control has focus; clicking the view releases focus. |
| 3 | On D3D intermediate targets `UNITY_MATRIX_P._m11` is negative, disabling the minimum sprite size on Windows. | `abs(...)`. |
| 4 | Trails drew streaks to stale positions after a recompile while paused. | Trails reset on every compile. |
| 5 | "Save first" discarded the show even if saving failed; web saves used PlayerPrefs (1 MB cap). | Save reports success; web saves are files in IndexedDB-backed persistentDataPath. |
| 6 | Large CSV exports ran unguarded and could exhaust WebGL memory. | Built inside the error handler; sample rate adapts to keep ≤ 2 M rows. |
| 7 | The cue list was rebuilt on every slider tick and labels allocated strings every frame. | Rebuild only when card content changes; labels update only on change. |
| 8 | The unsaved-changes dot stayed after saving. | `Saved` event refreshes the top bar. |
| 9 | The hex colour field was overwritten while typing. | Skipped while focused. |
| 10 | A failed compile left the loading screen spinning; validator exceptions repeated every frame. | Compile error state with message; validation wrapped. |
| 11 | Demo end card persisted on replay; loop flag not restored; demo could start on a stale show. | Fixed all three. |
| 12 | Airframes were drawn at any distance (≈1 M lit vertices at 1000 drones). | Drawn only within 160 m; paused frames skip re-sampling and uploads. |
| 13 | `pow()` with a negative base in the sky shader. | `x * x`. |

### Visual review (automated captures)

- Top bar and timeline were squeezed by the overflowing cue panel → fixed flex shrink/basis.
- Default-theme text fields rendered white → dark skin for fields, dropdowns, toggles, sliders, scrollers.
- Shore lanterns became large blobs next to the audience camera → smaller sprites.
- Text scenes filmed from a side dolly read skewed → flat formations get frontal shots only.
- The galaxy was tilted too far to read as a spiral → faces the audience more.

## Round 2 — 2026-09-26: 4,096-drone fleets, 3D models, detailed drones

Two more independent reviews (show core; Unity layer) of the large-fleet work, each finding checked with a
probe before it was fixed. Regression tests are in `LargeFleetTests.cs`, `BakedDemoTests.cs`,
`ProjectAssetTests.cs` and the PlayMode suite.

### Show core

| # | Finding | Fix |
|---|---|---|
| 1 | Growing a show for a bigger fleet kept spin and breathe rates, so edge speed and acceleration grew by √2: the 4,096-drone preset failed the safety check (12.8 m/s, 6.8 m/s²). | Spins and breathing slow by the size factor when a show grows; every preset is now compiled and validated in the tests. |
| 2 | The auction is only ε-optimal, so a pair of drones could be better off swapped, which CAPT's collision-free proof does not allow (closest pass 1.44 m against 1.5 m on a wide show). | Pairwise-swap repair after the auction (resumable); a test checks every pair. |
| 3 | Models had exactly 4,096 points, so a 4,096-drone fleet could never light every drone. | 12,288 points per model (midpoints between neighbours, farthest-point order); presets checked for full lighting. |
| 4 | Filled shapes tested every lattice point against every outline edge inside a binary search: up to 1.4 s in one frame at 4,096 drones. | Bucketed edge grid and a coarser stopping rule: 28–86 ms. Layouts are also cached, so an edit re-lays only its cue. |
| 5 | A baked assignment was trusted on its recipe key alone; a layout change without a re-bake would have shipped stale permutations silently. | Entries record their total cost and are re-solved on a mismatch; tests validate the shows as the app compiles them. |
| 6 | Cache eviction could drop the baked demo entries in a long session. | Imported entries live in their own cache and are never evicted. |
| 7 | Each edit restarted a half-finished 4,096-drone auction from zero. | Pending auctions are kept on the compiler and resumed. |
| 8 | The Rise speed hint assumed at least 1 s of travel. | Same travel time as the motion itself. |
| 9 | Found by the new cost check when the suite ran inside Unity: Mono and .NET laid out the Spiral Galaxy differently (a bisected pitch left a lattice point on the core's edge to within rounding, and four equal-length arms tied for a leftover sample), so shipped assignments missed. | Pitches settle off the knife edge; ties and near-integer floors resolve the same way on every runtime. A Mono-vs-.NET digest of all 48 preset layouts now matches. |
| 10 | New model cues used one default size, so a whale in the 360-drone show lit only 286 drones. | `SizeToLight` sizes each model for the fleet; the inspector offers **Enlarge to light all** for any cue that parks drones. |

### Unity layer

| # | Finding | Fix |
|---|---|---|
| 1 | Grounded airframes sank 5 cm into the barge (the model's origin is the LED bulb). | Drones on the pad stand on their skids; the lift fades out over the first metre of climb. |
| 2 | The reveal shot anchored to a slot's rest position while the formation turned during the hold. | The anchor follows the hold motion. |
| 3 | A two-finger pinch on a phone also panned, which ended the close-up. | While following, two fingers only zoom. |
| 4 | The pre-show ground segment was treated as the end of the show, cutting the new launch shot after 1 s. | Only the ground after landing ends the show. |
| 5 | The followed drone's index survived fleet changes and template loads. | The close-up ends when the show is replaced or its fleet size changes. |
| 6 | Leaving a close-up (another camera mode, a demo run) left the orbit a few metres from empty sky. | The orbit that the close-up interrupted is restored. |
| 7 | Two test assertions could not fail. | The airframe test checks triangle winding against geometry; the tautology is gone. |

## Round 3 — 2026-09-26: four times the drones

*Why do some shapes look like they have fewer drones than expected?* Safety spacing (√2 × the separation)
fixes how many drones a shape of a given size can hold, and the flagship flew 2,048 where the Draw_in_3D
show flies 4,096 in every shape. The flagship now flies 8,192 (4×, about the size of today's record-setting
shows), designed at 2,048 and grown by the fleet scaler, so its shapes are twice as wide and drawn in twice
the detail. Beyond the drone count itself:

| Area | Change |
|---|---|
| Limits | Fleets up to 8,192; an 8K preset. |
| Models | 24,576 points per model (two rounds of midpoints) so greedy spacing lights 8,192 drones; the exporter pins the 12 curated models so new Draw_in_3D shapes do not silently change the pack. |
| Galaxy | Arms are lines, whose capacity grows with size rather than area: filled galaxies add lanes (up to 9 per arm) when the fleet outgrows three. |
| Solver | The auction stops at ε = 10⁻³ (the swap repair supplies the safety condition): 2–2.5× faster, total travel within 0.1 %. The core builds optimised, so the bake takes 1.5 min instead of 15. |
| Models, sizing | A model too small for the fleet reached the end of its point pool 400 points at a time and was then re-tried five times (7 min to size twelve new cues). The pool now grows geometrically and the useless top-up is skipped (4 s); layouts are unchanged. |
| Cameras | The shore, aerial and default orbit views frame the show's actual envelope; demo shots near the water widen their lens when a shape would not fit. |
| Rendering | Light trails use fewer samples for big fleets (same time span), keeping the per-frame upload about constant. |
| Safety check | At 8,192 drones one time sample costs ~4.5 ms on average but up to ~90 ms in crowded transits, which stalled frames (14–17 fps in the browser while the check ran). A sample's pair checks now run 1,024 drones per step inside the 5 ms budget: 55–60 fps in the browser while checking. |
| Phones | Measured in Chrome with the CPU throttled 4×: ~15 fps at 8,192 drones (sampling, colouring and uploading every drone each frame). Phones open the flagship at its pre-solved 2,048-drone preset; desktop browsers fly 8,192 at 55–60 fps. |
| Tests | Flagship tests compile with the shipped baked plans; every preset (1K to 8K) is compiled and safety-checked; runtime-determinism digests cover all 64 preset layouts. |

## Round 4 — 2026-09-30: Demo 2, the Lạc bird festival

What the safety check and the tests caught while building the second demo, and what was done about it:

| Finding | Fix |
|---|---|
| A 22° wingbeat at 0.3 Hz moved the Lạc bird's wing tips at 30–60 m/s (limit 12): at show scale the tips are 80–170 m from the shoulder. | Slow, grand beats (10° at 0.065 Hz for the 2,048 design, slower still at 8,192); the finale rises instead of beating. |
| An amplitude envelope borrowed from the wave motion added 7 m/s² on 175 m wings. | Removed: the beat starts at rest and the motion clock already eases it. |
| The bird held only 5,611 drones at the largest size; the drum face fell short at 4K and 8K. | Four stacked sheets for the bird, two for the drum face: from the front they read as one drawing, and every scene now lights every drone at 1K–8K. |
| Parked (unlit) drones behind an oversized shape flew outside the geofence. | Gone once every drone has a place in the shape; the presets are now all fully lit. |
| Draw_in_3D redrew two models upstream (five balloons, an escorted liner), which silently shrank two flagship scenes by 40 %. | The exporter reads Draw_in_3D at a pinned commit; moving the pin is a deliberate change. |
| The default UI font had no Vietnamese letters (Lạc, Đông Sơn). | Be Vietnam Pro (SIL OFL) for the whole interface. |
| Inside Unity, six baked Demo 2 transitions (1K and 4K presets) were re-solved. The fleet scaler computed the drum's and the sun's height as `size * 0.6f + hover` in float; Mono keeps that intermediate at double precision, so it landed one bit away from .NET's, and the recipe keys (which hash the specs bit-exactly) no longer matched the bake. | The scaler evaluates its compound expressions in double and rounds once, so every runtime produces the same bits; a Mono build of the core now hits all 32 baked Demo 2 transitions, and the WebGL build plans both demos entirely from the bake. |
| The baked-plan cost check (1e-6) left little room over the way runtimes round the hold motions (up to 2e-7, coherently across a formation). | Tolerance 1e-4: runtime rounding passes, a stale bake (off by whole percents) is still re-solved. |

## Round 5 — 2026-09-30: the editor and the demo, stage effects and the audience

The question was how well the editor and the demo connect. The drones did, one to one (the demo plays the
compiled show the editor makes); the festival's effects did not: they were chosen automatically, invisible in
the editor, missing from the saved file and the exports, and unchecked for safety. Findings and fixes:

| Finding | Fix |
|---|---|
| Lasers, fountains and mist were automatic: the author could not say "fire for the drum, calm for the river". | `StageEffects` on every cue (lasers, fountains, flames, steam) in the inspector; *Auto* keeps the old look, and Demo 2 is now authored with them, so opening it in the editor shows exactly what the demo plays. |
| Nothing kept drones away from the stage: the depth slider reaches −200 m and the stage is 205 m in front of the pad. With flames that is a real hazard. | A flame zone in the safety check: while a scene's flames are armed, every drone keeps 20 m from every flame column, with the closest clearance in the report and on the Safety tab. Tested with a formation parked over the stage. |
| The effects were not in the show file or the exports, so a crew could not run them. | Saved per cue (older files open unchanged), a stage cue sheet export with SMPTE timecodes and each flame safety window, and an effects column in the flight report. |
| The editor gave no preview of where effects happen. | Flame and steam lanes on the timeline and tags on the cue cards. |
| The first steam read as a wall of coloured streaks hiding the stage. | Low, soft, mostly white clouds that spread from each vent, at a third of the intensity. |
| Lit by the flames, the crowd turned into orange cut-outs. | Backlit silhouettes: nearly black, with a faint rim in the show's colour. |
| The five-option selectors clipped their last option in the inspector. | Segmented buttons share the width and may shrink. |
| The lake demo never showed its audience: the shots from the crowd were picked only for flat scenes, and its are 3D models. | The lake finale is filmed from among the audience. |
| Fountain patterns snapped when the next cue chose another. | Heights blend over 1.2 s at every segment boundary. |
| Trees stood where the grandstands and lawn crowd go. | The audience area is kept clear of trees. |
