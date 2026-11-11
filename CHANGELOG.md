# Webwild v0.1.0

First playable vertical slice, 11 November 2026.

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
