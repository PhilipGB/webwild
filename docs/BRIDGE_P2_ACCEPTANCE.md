# v0.2.0 P2 acceptance — release checklist

> Integration status: all executable checks in this checklist passed in Webwild v0.2.0 P2. Windows runtime, real-compositor full-screen and supervised child playtesting remain explicitly **NOT VERIFIED**; see `docs/EVIDENCE.md#v020-p2` and `docs/EXPORTS.md`.

## Correctness (release blocking)

- [x] Reedwater +6 food/+24 silk supplies cannot be re-granted after 512+ unrelated transaction keys evict their ledger key.
- [x] Save/JSON/load and schema-1→2 legacy fixtures behave identically after the fix.
- [x] Shared colony resources, workers, mission progress, and region persistence remain intact.
- [x] Bridge collision is unchanged or equivalently verified: walkable on Garden and Reedwater, both anchor orders, X and Z orientations, and ramps physically connect to bank ground.
- [x] Removing/damaging a bridge affects passability under existing rules; no solid path exists before construction.
- [x] All old tests pass, plus new pure and runtime regression tests.
- [x] No new silent procedural scenery fallback, no regression in `--scene-validate`.

## Visual / UX (release blocking)

- [x] Authored GLBs load in editor and Linux exports with vertex colour intact.
- [x] At least 3 authored plank variations, knot/fixture/support details and procedural silk rails are visible; no solid gold ribbon or gold box mesh covers the walking deck.
- [x] Planks follow bridge sag, tangent, and reverse anchor orientation; no large gaps, floating pieces, detached rails or underwater decks.
- [x] Rebuilds/refreshes happen on relevant state changes, not every render frame.
- [x] Capture webs and build previews remain legible and distinguishable.
- [x] Toasts no longer cover the player or centre objective, and do not overlap controls at 1280×720, 100% / 125% / 150% UI scale.
- [x] Fresh rendered screenshots checked manually, including both regions, game launch, story success, build mode, bridges, and Reedwater completion.

## Technical release gates

- [x] `dotnet build -warnaserror` passes.
- [x] `dotnet test` retains all P1 tests and adds P2 regressions.
- [x] Godot headless import/start clean.
- [x] `--scene-validate` passes (P1 baseline: 47/47).
- [x] Headless & rendered `--runtime-probe` pass (P1 baseline: 168/168).
- [x] Exported Linux headless & rendered probes pass.
- [x] Exported Windows x64 PE32+ builds (runtime NOT VERIFIED absent Windows host).
- [x] Actual release Linux archive re-downloaded/checksum-matched and run.
- [x] Exact-head PR CI and final-main CI green; no outstanding P1/P2 defects.
- [x] Repository private; annotated v0.2.0 tag on verified final commit; release assets/checksums uploaded.
- [x] Unverified real Windows, real full-screen compositor and supervised child playtest each reported explicitly if not performed.

## Scope and non-goals

No new region, mission, crafting economy, character controller rewrite, procedural terrain generator, multiplayer, combat or complete UI redesign.
