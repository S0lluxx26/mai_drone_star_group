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
