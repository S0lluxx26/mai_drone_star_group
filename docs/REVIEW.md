# Review log — 2026-09-25

Two independent reviews (show-core logic; Unity runtime, UI, shaders and tooling), plus visual review of
automated captures from the Windows player. Every finding below was fixed; regression tests are in
`Assets/DroneStar/Tests/EditMode/Core/ReviewRegressionTests.cs`.

## Show core

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

## Unity layer

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

## Visual review (automated captures)

- Top bar and timeline were squeezed by the overflowing cue panel → fixed flex shrink/basis.
- Default-theme text fields rendered white → dark skin for fields, dropdowns, toggles, sliders, scrollers.
- Shore lanterns became large blobs next to the audience camera → smaller sprites.
- Text scenes filmed from a side dolly read skewed → flat formations get frontal shots only.
- The galaxy was tilted too far to read as a spiral → faces the audience more.
