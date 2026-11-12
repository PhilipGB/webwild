## Webwild v0.2.1 — Reedwater shoreline correction (unreleased)

Fixes the visible water-over-grass defect in Reedwater Bank without changing
gameplay, missions, bridge geometry, economy or save schema.

### Geometry

- Replaced the 24 × 20 m `PlaneMesh` pond surface with a static authored
  `ArrayMesh` (`art/meshes/reedwater_water_surface.tres`) whose wavy,
  superellipse-like outline follows the open water between the banks and around
  the islands. It is 13.33 × 20.41 m in bounds and tucks slightly under the
  opaque banks so there is no seam.
- Added a continuous mossy shore shelf
  (`art/meshes/reedwater_shore_shelf.tres`) between the water edge and the banks.
  It gives the shoreline a wet, irregular rim and prevents exposed gaps.
- Replaced the oversized pond bed with `art/meshes/reedwater_pond_bed.tres`.
  It sits at Y −1.45 under the water and shelf, so transparent water has a dark
  bed instead of showing the world background.
- Lowered the water top to Y −0.03, just below the bank top at Y 0. The bank
  remains opaque and the water cannot render over grass. This is combined with
  the reshaped mesh and shelf; lowering alone was not used as the fix.
- Removed the two long `ShoreMossWest`/`ShoreMossEast` boxes that previously
  crossed the waterline. The shelf now provides the shoreline trim.
- Preserved the existing water colour, emission and low-poly aesthetic.

### Water hazards

- Kept four authored `Area3D` water volumes and the existing recovery rule.
- Refitted the shallow pond volume and three tall jump-guard channels to the new
  shoreline. The volumes stay inside the water footprint and the tall guards
  remain in the channels between landmasses so island standing/jumping stays
  safe.
- Verified safe bank and island points, hazardous open-water points, direct
  water recovery, bridge traversal and the no-bridge rejection through runtime
  physics queries.

### Verification

- 106/106 pure .NET tests.
- 55/55 scene checks, adding authored-surface, water-below-banks,
  water-versus-bank-bounds and hazard-inside-shoreline checks.
- 246/246 headless and rendered runtime checks, adding shoreline dry/wet,
  island, open-water, recovery and shoreline screenshot checks.
- Exported Linux scene/runtime probes rerun headless and rendered.
- New real screenshots: `22d-start-shoreline`, `22e-far-shoreline`,
  `22f-island-edges` and `22g-low-angle-shoreline`; `21`, `22b`–`22c`, `23`–`24`
  and the other runtime captures were regenerated.
- Windows x64 export rebuilt; Windows runtime remains **NOT VERIFIED**.
- No v0.2.1 release is published.

## Webwild v0.2.0 — modular silk bridges, reliability and release polish

Final technical hardening and presentation pass after the two-region Reedwater
Bank implementation. The v0.2.0 release itself is described at the end of this
section once its gates are complete.

### Durable one-time supply cache

- Fixed a real replay flaw: the Reedwater outpost cache was keyed only through
  the bounded 512-entry ledger history, so later visits could grant +6 food and
  +24 silk again after the key was evicted.
- Schema v3 adds a durable `ClaimedSupplyCaches` set. Claims survive interleaved
  travel, save/JSON/load, unlimited later transactions and history eviction.
- Added explicit schema-1 → schema-2 → schema-3 migration. P1 schema-2 saves
  that already list Reedwater are migrated as already claimed, preventing a
  duplicate grant; P1 garden saves and all v0.1.x fixtures migrate with an empty
  claim. Real v0.2.0 P1 fixtures are tested alongside the legacy fixtures.
- The rolling 512-key ledger retention policy is unchanged.

### Modular silk-and-bark bridges

- Replaced the flat opaque ribbon and yellow walkway boxes with a reusable
  `BridgeVisualAssembler` built from the supplied 11 original bridge GLBs:
  three bark planks, two knots, wooden eyelet, forked rail fixture, silk-wrapped
  support, cross lashing, woven panel and reference braid.
- Added the pure `Webwild.Core.BridgeLayout` plan. It places alternating planks
  by arc length, orients local +X along travel and +Z across the bridge, includes
  authored bank ramps, and keeps visuals on the same sag contract as the
  existing walkable collision.
- Generated curved ivory/silver-blue silk rails, lower threads and sparse
  vertical lashings. Supports, eyelets, forks, cinches, hitches and cross
  lashings are sparsely instanced; no prop owns collision.
- Reworked previews to use the same assembler as a translucent green/coral ghost
  with no walkway collision. Applied the authored vertex-colour material at
  runtime so colours also appear in the exported Linux build.
- Damage adds a restrained coral overlay without changing durability, repair,
  silk costs, worker graph logic or bridge physics.
- Added bridge-asset scene validation and runtime checks for planks, all three
  variants, silk rails, detail fixtures, vertex materials, reversed/Z-axis spans,
  preview validity cues, damage presentation and removal/repair parity.

### Notification lane

- Moved brief toasts out of the player/camera centre into a compact right-hand
  lane below the mission card. Toasts are width-limited, duration-capped,
  single-slot, non-interactive and never accept mouse input.
- Rect-based runtime checks prove the lane avoids the mission card, left action
  column, status hint and bottom toolbar at 1280×720 with 100%, 125% and 150% UI
  scale.
- Long actions remain in the objective/hint UI rather than blocking toasts.

### Verification

- 106 pure .NET tests passed, including history-eviction, P1 migration and
  bridge-layout direction/orientation tests.
- Scene contract: 51/51 across both regions, including the eleven bridge parts.
- Headless, rendered and exported Linux runtime probes: 236/236 checks.
- Linux x64 export re-run headless and rendered under Xvfb. Windows x64 PE32+
  export built; Windows runtime remains **NOT VERIFIED**.
- New real screenshots include modular bridge close-ups, damaged bridge, and
  100/125/150% toast layouts.
- Released as annotated tag `v0.2.0` on verified `main`; Linux and Windows x64
  archives and `SHA256SUMS` are attached to the GitHub release. Windows runtime,
  real-compositor full-screen and supervised child playtesting remain
  **NOT VERIFIED**.

# Changelog

## Webwild v0.2.2 — gameplay reliability and usability hardening (unreleased)

Follow-up to the six-mission real-input playtest. No new region or mechanics.

### PR 1 — bridge traversal and save safety

- Replaced the segmented box collision on every modular bridge with a single
  triangle-strip walkway built from the same centerline and sag as the visible
  deck. The old box end caps formed a ~60° wall at each ramp/deck joint, which
  wedged the 0.72 m player capsule; the surface has no end caps.
- Widened the walkable deck to 1.5 m (0.75 m each side) and rendered the same
  width as a woven silk mat under the authored bark planks, with the silk rails
  moved to the deck edge. The visual bridge and the walkable width now agree.
- Made approach ramps continue away from their span instead of doubling back
  under a shared island post. Authored bank-level ground points still always
  win; elevated island landings are used only when they point away from the deck.
- Moved two decorative Reedwater posts off the bridge centreline (mossy island A
  and the far bank) that were physically blocking the deck.
- Added step-friendly floor tolerances and kept the player from needing a jump
  at any joint.
- New Garden now asks before replacing a saved garden, defaults to **Cancel**,
  and keeps the replaced garden in a durable `.previous` archive that normal
  autosaves never touch. The title screen offers **Restore previous garden**
  while that archive is valid.
- Added deterministic geometry tests (width, upward faces, continuity, reversed
  anchor order, sag, degenerate spans) and bridge walking checks that start on
  bank or island ground and walk naturally in both directions, including
  stopping, resuming and turning on the deck, after save/reload, and as a worker.
- Verification: 115/115 .NET tests, 55/55 scene checks, 264/264 headless and
  264/264 rendered runtime checks.

### PR 2 — anchor interaction, nursery and progression

- Recommended anchors now carry a bright gold star halo and label; unrelated
  sockets are dimmed and only show labels when the player is close, hovered or
  selected. Reedwater's overlapping sockets no longer form a label cluster.
- Cursor selection uses screen-space nearest-target within a forgiving radius
  and rejects sockets hidden behind foreground bodies.
- The second anchor click no longer spends silk. A From → To · cost bar shows
  **Build** and **Cancel**; Enter/Space confirms, right-click/Esc cancels, and
  a second click can change the pair. Nothing is spent until Build.
- The nursery panel updates live: an advancing percentage and remaining time,
  a neutral "hatching" button, a separate pending-egg count, and a neutral
  refresh after completion. Living spider counts exclude unhatched eggs.
- Completion/victory modals close any open panel first, so they cannot stack.
- Clever Crossing is awarded only when the player physically reaches the lily
  island with at most two standing bridges. Building is not enough; the mission
  card explains the challenge and the current bridge count. Existing saves keep
  an earned badge.
- Verification: 119/119 .NET tests, 55/55 scene checks, 295/295 headless and
  295/295 rendered runtime checks.


## Webwild v0.2.0 P1 — Reedwater Bank second region (unreleased milestone)

Expands the scene-first world into a connected two-region colony game. The
v0.2.0 release itself is still not published.

### Region architecture and travel

- Added stable region ids and an engine-free `RegionCatalog`. The original garden
  remains the default region; Reedwater Bank is a second authored region with its
  own anchors, worker graph, water collision and insect routes.
- Added a discoverable reed passage by the garden's east hedge after the three
  story missions, with a child-readable travel dialog. Travel is explicit (E),
  never proximity-only, and works in both directions.
- Region scenes are cached and switched by `RegionSceneCatalog`; the single
  player, camera, UI, audio, runtime roots and simulation are reused. Inactive
  regions have collision, lights and environment disabled rather than deleted.
- Added safe arrival points, region-specific player position memory, and
  region-aware save/load.

### Reedwater Bank content

- Added fifteen supplied original GLB models and their scene wrappers, using the
  double-sided `reedwater_vertex_color_material.tres`. The authored region has a
  shallow pond with real water volumes, mossy islands, reed beds, cattails, lily
  pads, water lilies, mossy stones, an outpost and a far bank.
- Added three new objectives: **R01 Across the Water**, **R02 The Reed Harvest**
  and **R03 The Far Bank**, plus the optional **Clever Crossing** mastery badge.
- Added three insect routes, including two dragonfly paths. One straightforward
  reed flyway suits younger players; the dragonfly routes reward placement and
  observation.
- Added a one-time Reedwater outpost supply cache so a child who spent silk on the
  story can always begin the first bridge; the ledger key prevents duplication.
- Workers travel with the colony through the marked passage, then must use real
  bridge edges to reach the far bank. They never walk across open water.

### Save schema

- Save schema v2 adds `activeRegionId`, per-region player transforms, region ids
  on webs/workers/insects, regional objective progress, Reedwater bridge counters
  and derived route history.
- Added an explicit schema-1 → schema-2 migration. All v0.1.0, v0.1.1 and v0.1.2
  saves restore the garden, resources, webs, workers, missions and settings with
  safe Reedwater defaults. Real legacy fixtures are tested.
- Atomic writes, backup recovery, bounded histories and explicit corrupt-save
  errors continue to work.

### Verification

- 93 pure tests passed.
- Scene contract 47/47 across both regions.
- 168/168 headless and rendered runtime checks passed.
- Linux exported headless and rendered acceptance re-run for this milestone;
  Windows export built, Windows runtime remains **NOT VERIFIED**.

## Webwild v0.2.0 P0 — scene-first world (unreleased milestone)

Converts the persistent garden from C#-constructed geometry into reusable,
editor-authored Godot scenes. No gameplay, economy or save-schema changes; the
v0.2.0 release itself is not published yet.

### Authored world

- Added `GardenWorld.tscn` with environment, terrain, paths, landmarks, flora and
  boundary scenes. `GardenClearing.tscn` and `TitleBackdrop.tscn` both instance
  the same world, so the menu backdrop cannot diverge from the game.
- Added eight landmark scenes: Home Leaf, Twin Twigs, Pebble Gap, Flower Lane,
  Leaf Nursery, Climbable Stem, Fallen Log and Wind Edge.
- Moved ground, ravine, hedge collision, paths, flora, rocks and lighting into
  `.tscn` files. Collision shapes are ordinary editable editor nodes.
- Model instances reuse the existing `.glb` assets with a shared baked
  vertex-colour material, so the garden looks the same in the editor and at
  runtime.
- Added `scenes/building/webs/AnchorSocket.tscn` ring visuals and an exported
  `AnchorId`, and authored all 12 anchor sockets in `GardenClearing.tscn`.

### Coordinate contract and validation

- Added `SceneContentValidator` (23 checks) and `--scene-validate`. Startup
  aborts with named failures if a mandatory scene, anchor, marker or collision
  contract is missing or has drifted from `WorldLayout`.
- Added landmark markers for all anchors, the nursery, climb base/top and bridge
  bank points. Added `SceneAuthoringTests` to compare the committed `.tscn`
  coordinates with the simulation.
- Deleted `scripts/world/GardenScenery.cs`; there is no procedural fallback.

### Verification

- 71 pure tests passed.
- Scene contract 24/24.
- 105/105 headless and rendered runtime checks passed.
- Linux export and published-package re-test are performed for the milestone;
  Windows runtime remains **NOT VERIFIED**.

## Webwild v0.1.2 — review-hardening release

Fixes the three outstanding findings from the independent v0.1.1 review.

### Bridge approaches

- Bridge approach ramps now use the actual span and authored bank points rather
  than fixed X offsets. A bridge built East → West places both ramps on solid
  ground, and a predominantly Z-axis bridge gets ramps along Z.
- The shared geometry lives in `Webwild.Core.BridgeGeometry` and is covered by
  deterministic tests plus runtime checks for reversed order, Z-axis spans,
  physical crossing and removal of collision when a bridge is dismantled.

### Save/load history order

- Issued-event, ledger and mission seen-event histories now serialise their
  ordered retention queues instead of membership sets. After save/load, eviction
  continues to remove the oldest keys first.
- Restore skips blank and duplicate entries and keeps set and queue membership
  consistent. The save schema is unchanged, so existing v0.1.0/v0.1.1 saves still
  load.
- Added long-history round-trip tests that serialise, deserialise, reload, add
  more events, and compare exact order plus resources, missions and workers.

### Settings temporary files

- `SettingsService.Save()` now tracks its temporary filename outside the `try`
  block and deletes it on any failure, without masking the original error.
- Added injected-failure tests for partial writes, backup copy failures, main
  file replacement failures, successful retries and cleanup-failure reporting.

### Verification

- 65 pure tests passed.
- 80/80 headless and rendered runtime checks passed.
- Linux export rebuilt and re-tested; Windows export rebuilt. Windows runtime
  remains **NOT VERIFIED**.

## Webwild v0.1.1 — hardening release

Fixes from the independent v0.1.0 code review.

### Reliability

- Bounded save histories: issued event keys, ledger idempotency keys and mission
  seen-event keys retain recent entries only. Long sessions no longer become
  unsaveable.
- Save-and-quit now keeps the session open on failure and offers **Try saving
  again**, **Keep playing** or **Quit without saving**.
- Settings save failures are shown persistently in-game and in the settings
  panel, with a Retry action, instead of being silently discarded.

### Gameplay and consistency

- Added a real ravine through the garden. The player cannot cross it until a
  silk bridge exists; bridge walkways include ramps and a collision surface that
  follows the deck. Worker graph and player traversal now agree.
- Silk production now consumes the configured food cost and scales with the
  number of spiders (player baseline plus each hatched helper). It stops at zero
  food or the silk cap.
- Gentle weather now derives from an immutable baseline, so switching it off
  restores normal storm values.
- Pausing freezes player physics as well as simulation ticks, including while
  airborne or climbing.

### Maintenance

- Clarified the v0.1.x architecture limitation: persistent garden geometry is
  constructed in C#; moving it into authored `.tscn` scenes is planned for v0.2.
- Restored the repository to private visibility as intended.
- Added regression tests for long-session saves, ledger retention, silk food
  cost/producer scaling, weather toggling, settings failure and bridge traversal.

## Webwild v0.1.0 — first playable

## Added

- One authored 38 × 28 low-poly garden clearing with a starter leaf, twin twigs,
  pebble gap, flower lane, leaf nursery, tall climbable stem, log shelter and
  windy edge.
- Playable spider with camera-relative walking, jumping, assisted stem climbing
  and an orbit/zoom camera.
- Twelve typed anchor sockets with capture-web and silk-bridge previews,
  validation feedback, costs, cancellation, dismantling and bridge collision.
- Four authored insect routes, swept-segment capture, exactly-once food rewards
  and respawns.
- Leaf nursery hatching up to five spiders total, with named autonomous workers
  that collect trapped prey, repair webs and patrol across bridge edges.
- Three story missions (`M01 First Threads`, `M02 Tiny Team`, `M03 Storm-Smart`),
  optional `Clever Weaver` mastery, hints, victory and continued play.
- Forecast wind event with exposed/sheltered damage, repair or sheltered-web
  recovery, leaf particles and audio.
- Title, New Game, Continue, Pause, Help, Colony, Settings and Save-and-Quit flows.
- Versioned JSON saves with validation, atomic writes, healthy backup recovery
  and explicit failure messages.
- Settings for master/music/SFX volume, camera sensitivity, 100/125/150% UI
  scale, reduced motion, gentle weather, full screen and tutorial hints.
- Original synthesised WAV music and effects. All critical information is also
  shown visually.

## Verified

- 45 pure C# tests passed, 0 failed, 0 skipped.
- 58 runtime acceptance checks passed, 0 failed, in headless, rendered Xvfb and
  exported Linux runs.
- Linux x64 release export built, launched and passed the runtime probe.
- Windows x64 release export built as a PE32+ x86-64 executable.

## Known limitations

- Windows runtime verification: **NOT VERIFIED** (no Windows host).
- Supervised child playtest: **NOT VERIFIED**; developer visual playtest done.
- Actual full-screen presentation on a desktop compositor: **NOT VERIFIED**.
- At 150% UI scale a temporary toast can overlap the top action button; it fades
  and permanent controls stay available.
