# Changelog

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
