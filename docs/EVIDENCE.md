# Evidence index

All evidence was produced on the build host for the current v0.2.0 P0 milestone;
the v0.1.x sections below are historical. Commands are copy-pasteable from the
repository root unless a working directory is shown. No PASS in this file is
inferred from source alone: every result comes from a command, a runtime check or
an inspected screenshot.

- Host: Linux x64
- `dotnet --version` → `10.0.114`
- `godot --version` → `4.7.2.stable.mono.official.ed1daf0bf`
- Godot .NET export templates 4.7.2 installed
- GitHub repository: `PhilipGB/webwild` (private), `https://github.com/PhilipGB/webwild`
- Current milestone base: `v0.1.2` at `1ee3208`
- Current milestone commit and PR: recorded in the v0.2.0 P0 section below
- Latest published release: `v0.1.2` (no v0.2.0 release is published yet)

## Pull requests

| Phase | PR | Merge commit |
|---|---|---|
| P1 | https://github.com/PhilipGB/webwild/pull/1 | `926fe22` |
| P2 | https://github.com/PhilipGB/webwild/pull/2 | `4760fe2` |
| P3 | https://github.com/PhilipGB/webwild/pull/3 | `3a21a3d` |
| P4 | https://github.com/PhilipGB/webwild/pull/4 | `dc71c95` |
| P5 | https://github.com/PhilipGB/webwild/pull/5 | `1538eb3` |
| P6 | https://github.com/PhilipGB/webwild/pull/6 | `50d8eda` |
| P7 | https://github.com/PhilipGB/webwild/pull/7 | `5c4e83b` |
| P8 | https://github.com/PhilipGB/webwild/pull/8 | `2ef10d7` |
| v0.1.1 hardening | https://github.com/PhilipGB/webwild/pull/9 | `b1587b1` |
| v0.1.2 review hardening | https://github.com/PhilipGB/webwild/pull/10 | `de26fc3` |
| v0.2.0 P0 scene-first world | https://github.com/PhilipGB/webwild/pull/11 | `7af6e48` |

All eight PRs had green **Build and pure tests** and **Godot .NET import and
startup** checks before merge.

## P0 — Toolchain and repository

- `dotnet build -warnaserror` → success, 0 warnings, 0 errors
- `dotnet test` → final result 65 passed, 0 failed, 0 skipped
- `godot --headless --path . --editor --quit` → exit 0, GLBs/audio/SVG imported
- `gh repo view --json nameWithOwner,url,visibility` →
  `{"nameWithOwner":"PhilipGB/webwild","visibility":"PRIVATE"}`

## P1 — Garden, player and title

- `godot --headless --path . -- --runtime-probe` runtime checks:
  - `title-visible`, `title-no-game`, `new-game-started`, `title-hidden`
  - `anchors-visible` = 12, `insects-visible` = 4
  - `movement-wasd` moved 4.86 units
  - `jump-space` rise 1.03
  - `climb-stem` height 4.20
- Screenshots: `artifacts/screenshots/01-title.png`,
  `artifacts/screenshots/02-garden.png`

## P2 — Two web types

- `dotnet test --filter PlacementTests` → all placement tests pass
- Runtime checks:
  - `invalid-pair-rejected` — “Choose two different anchors”; silk unchanged
  - `low-silk-rejected` — “Need 6 silk”; ledger stays at 0 for the attempt
  - `cancel-no-charge` — cancel leaves web count 0 and silk untouched
  - `capture-web-built` — two real injected mouse clicks on Twin Twigs
  - `capture-web-cost` — silk 20 → 14
  - `occupied-edge-rejected` — “Already connected”
  - `bridge-built` — two real injected mouse clicks on the Pebble gap
  - `dismantle-refunds` — removal restores silk and removes the web
- Screenshots: `06-build-mode`, `07-capture-web`

## P3 — Insects and economy

- `dotnet test --filter CaptureAndEconomyTests` → all capture/economy tests pass
- Runtime checks: `insect-trapped`, `food-collected` (5 → 7), `m01-complete`
- Screenshot: `08-insect-trapped.png` shows the fly inside the capture web with
  the “A fly is caught!” toast.

## P4 — Nursery and workers

- `dotnet test --filter WorkerAndMissionTests` → all worker/mission tests pass
- Runtime checks: `hatch-started`, `worker-hatched`, `worker-visible`,
  `m02-complete`, `worker-crossed-bridge`
- Screenshots: `09-bridge-crossing.png` shows the ravine and silk bridge;
  `10-nursery.png` shows 1 of 5 spiders, the hatch cost and enabled Hatch button
  before hatching.

## P5 — Missions, UI and victory

- `dotnet test --filter MissionFlowTests` → hint progression test passes
- Runtime checks: `m01-complete`, `m02-complete`, `m03-complete`, `victory`,
  `victory-screen`, `continue-loads`, `return-to-menu`, `quit-to-menu`
- Screenshots: `11-wind-warning`, `12-victory`, `15-title-continue`

## P6 — Wind, settings and polish

- `dotnet test --filter WindTests` → wind damage, repair, rescue and save-validity
  tests pass
- Runtime checks: `wind-completed`, `wind-event`, `web-damaged-or-repaired`,
  `web-restored`, `low-threat-applies`, `ui-scale-applies`,
  `ui-scale-150-applies`, `settings-persist`
- Screenshots: `03-pause`, `04-help`, `05-colony`, `11-wind-warning`,
  `13-settings`, `16-ui-scale-125`, `17-ui-scale-150`

## P7 — Persistence and hardening

- `dotnet test --filter SaveTests` → roundtrip, corrupt-main recovery, invalid
  schema, failed-write and settings tests pass
- Runtime checks: `save-succeeds`, `save-loads`, `save-missions`, `save-webs`,
  `save-workers`, `reload-webs`, `reload-workers`, `reload-victory`,
  `reload-player`
- Screenshot: `14-reloaded.png`

## P8 — End-to-end, exports and release

### Final source/runtime result

```text
$ dotnet test
Passed! - Failed: 0, Passed: 65, Skipped: 0, Total: 65

$ xvfb-run -a -s "-screen 0 1280x720x24" \
    godot --path . --resolution 1280x720 -- --runtime-probe
[PROBE] SUMMARY passes=80 failures=0
```

`artifacts/runtime_probe.json` contains all 80 named checks. The 19 inspected
screenshots are in `artifacts/screenshots/`.

### Exported Linux build

```text
$ godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
exit 0

$ cd exports/linux && ./Webwild.x86_64 --headless -- --runtime-probe
[PROBE] SUMMARY passes=80 failures=0
exit 0

$ cd exports/linux && xvfb-run -a -s "-screen 0 1280x720x24" \
    ./Webwild.x86_64 --resolution 1280x720 -- --runtime-probe
[PROBE] SUMMARY passes=80 failures=0
exit 0
```

This confirms the Linux release export launches, renders and completes the
runtime acceptance flow.

### Exported Windows build

```text
$ godot --headless --path . --export-release "Windows" exports/windows/Webwild.exe
exit 0

$ file exports/windows/Webwild.exe
PE32+ executable for MS Windows 5.02 (GUI), x86-64
```

**Windows runtime verification: NOT VERIFIED.** No Windows host was available.
Export success must not be read as a tested Windows startup.

### Checksums

See `docs/EXPORTS.md`.

## v0.1.1 hardening from independent review

- `dotnet test` → **51 passed, 0 failed, 0 skipped**.
- Headless runtime probe → **66 passed, 0 failed**.
- Rendered Xvfb runtime probe → **66 passed, 0 failed**, 17 screenshots.
- New runtime checks:
  - `pause-freezes-airborne`, `pause-resumes-physics`
  - `ravine-blocks-without-bridge`, `bridge-enables-player-crossing`
  - `silk-producers`
  - `low-threat-restores`
  - `failed-save-keeps-session`
  - `settings-save-error-visible`
- New deterministic tests:
  - `LongSession_BoundsHistories_AndStillValidatesForSaving`
  - `Ledger_TrimsOldKeys_ButKeepsRecentIdempotency`
  - `SilkProduction_ConsumesFood_AndStopsWhenFoodRunsOut`
  - `SilkProduction_ScalesWithHatchedHelpers`
  - `GentleWeather_RestoresBaselineWhenSwitchedOff`
  - `Settings_FailingWrite_IsReported`
- Repository visibility rechecked with `gh repo view` → `PRIVATE`.
- Exported Linux v0.1.1 package was launched and passed the 66/66 probe.

## v0.1.2 review hardening

- `dotnet test` → **65 passed, 0 failed, 0 skipped**.
- Headless runtime probe → **80 passed, 0 failed**.
- Rendered Xvfb runtime probe → **80 passed, 0 failed**, 19 screenshots.
- Exported Linux build, headless → **80 passed, 0 failed**.
- Exported Linux build, rendered Xvfb → **80 passed, 0 failed**.
- New runtime checks:
  - `bridge-ramps-west-east`, `bridge-walkway-collision`
  - `bridge-reverse-built`, `bridge-reverse-ramps`, `bridge-reverse-collision`
  - `bridge-reverse-enables-crossing`
  - `bridge-removal-clears-collision`, `bridge-removal-removes-web`,
    `bridge-removal-blocks-crossing`
  - `bridge-zaxis-built`, `bridge-zaxis-ramps`, `bridge-zaxis-collision`
  - `bridge-regression-quit`
- New deterministic tests:
  - `BridgeGeometryTests` (ravine both orders, Z-axis, authored bank point,
    coincident-anchor safety)
  - `HistoryOrderTests.OrderedHistories_SurviveSerialisedSaveReload_AndContinueEvictingOldestFirst`
  - `HistoryOrderTests.LegacyUnorderedHistory_LoadsAndDeduplicatesWithoutDiverging`
  - `HistoryOrderTests.OversizedLegacyHistory_TrimsFromTheFrontAndKeepsNewestKeys`
  - `WorkerAndMissionTests.ReversedBridgeSelection_StillConnectsTheWorkerRoute`
  - Settings partial-write, copy-failure, move-failure, retry and cleanup-failure
    tests in `SaveTests`
- Exact-head PR CI: https://github.com/PhilipGB/webwild/actions/runs/43130691851 —
  **Build and pure tests** and **Godot .NET import and startup** both green.
- Repository visibility remains `PRIVATE`.

## v0.2.0 P0 — scene-first world

- PR: https://github.com/PhilipGB/webwild/pull/11
- Implementation commits: `fc57b97` (scene architecture), `87c2fb3`
  (contract tests) and `9c9b970` (documentation/evidence)
- Merge commit on `main`: `7af6e48`
- Exact-head PR CI: https://github.com/PhilipGB/webwild/actions/runs/43135905492
  — both checks green on `9c9b970`
- Merged `main` CI: https://github.com/PhilipGB/webwild/actions/runs/43136134224
  — success on `7af6e48`
- Branch: `feat/v0.2.0-p0-scene-world`
- No v0.2.0 release is published; this is the first development milestone.
- Persistent world moved from `scripts/world/GardenScenery.cs` into:
  - `scenes/world/GardenWorld.tscn`
  - `GardenEnvironment.tscn`, `GardenTerrain.tscn`, `GardenPaths.tscn`,
    `GardenLandmarks.tscn`, `GardenFlora.tscn`, `GardenBoundaries.tscn`
  - eight `scenes/world/landmarks/*.tscn` scenes
  - `GardenClearing.tscn` and `TitleBackdrop.tscn` share the same world
- `GardenScenery.cs` and its `.uid` were deleted. There is no procedural
  fallback; startup validation fails loudly instead.
- The scene contract is documented in `docs/SCENE_AUTHORING.md`.

### Commands and results

```text
$ dotnet build -warnaserror
0 warnings, 0 errors

$ dotnet test
Passed! - Failed: 0, Passed: 71, Skipped: 0, Total: 71

$ godot --headless --path . -- --scene-validate
[SCENE] SUMMARY passes=24 failures=0

$ godot --headless --path . -- --runtime-probe
[PROBE] SUMMARY passes=105 failures=0
```

Rendered source verification:

```text
$ xvfb-run -a -s "-screen 0 1280x720x24" \
    godot --path . --resolution 1280x720 -- --runtime-probe
[PROBE] SUMMARY passes=105 failures=0
```

Exported Linux verification:

```text
$ ./Webwild.x86_64 --headless -- --scene-validate
[SCENE] SUMMARY passes=24 failures=0

$ ./Webwild.x86_64 --headless -- --runtime-probe
[PROBE] SUMMARY passes=105 failures=0

$ xvfb-run -a -s "-screen 0 1280x720x24" \
    ./Webwild.x86_64 --resolution 1280x720 -- --runtime-probe
[PROBE] SUMMARY passes=105 failures=0
```

The exported build needed a narrow runtime vertex-colour material pass because
Godot omits imported-child instance overrides when packing. It applies materials
only to nodes whose `SceneFilePath` is under `res://art/models/`; it constructs
no geometry. Source and exported screenshots confirm identical palettes.

### Scene content counts

- 8 landmark scene instances, each exactly once.
- 12 `AnchorSocket.tscn` instances with unique `AnchorId` values.
- 123 imported model instances and 934 meshes resolve at runtime.
- 6 world collision shapes (2 ground bodies + 4 hedge walls) and 12 anchor
  spheres, all with validated layers/masks.
- 24 scene-contract checks, including anchor/marker/coordinate agreement,
  nursery/climb/bridge markers, ravine width, environment, player spawn, model
  resources and absence of the procedural builder.

### Editor usability evidence

Baseline scene inspection before any edit:

```text
[EDIT] HomeCache local=(-1.6, 0.02, 1.2) global=(-15.200001, 0.02, 8.2)
[EDIT] HomeLeaf scene=res://scenes/world/landmarks/HomeLeaf.tscn
[EDIT] WestMeadow global=(-11, -0.3, 0) layer=1 mask=0
[EDIT] collision_shapes=6 nodes=1226
```

A temporary text edit to `HomeLeaf.tscn` (the same change the editor scene view
performs on save) moved `HomeCache` to local x `-1.25`. Re-opening the world
reported the new persisted transform without any C# change:

```text
[EDIT] HomeCache local=(-1.25, 0.02, 1.2) global=(-14.85, 0.02, 8.2)
```

The edit was reverted and the original transform returned. This demonstrates
editable decorative objects, inspector-visible collision, reusable scene
instances and persisted scene ownership.

A temporary anchor drift moved `Anchor_twig_west_a` from x `-9.5` to `-9` in
`GardenClearing.tscn`. Scene validation failed with the exact expected/actual
coordinates and a non-zero exit:

```text
[SCENE] FAIL anchor-transforms-match-simulation — twig_west_a=(-9, 1.25, -1.4) expected (-9.5, 1.25, -1.4)
[SCENE] FAIL landmark-anchor-markers — 12 markers; twig_west_a marker/socket mismatch
[SCENE] SUMMARY passes=22 failures=2
exit 1
```

After restoring the authored position, validation returned to 24/24 and exit 0.

### Visual comparison

- 19 rendered screenshots were regenerated from the migrated scene.
- `02-garden.png` and `09-bridge-crossing.png` match v0.1.2 scene composition and
  palette; `01-title.png` differs only in the version label.
- `18-bridge-reversed.png` and `19-bridge-z-axis.png` retain the v0.1.2 bridge
  evidence.

### Remaining limits

- Gameplay coordinates remain in `WorldLayout` and are validated against the
  authored scene; there is no editor auto-sync plugin.
- Windows runtime, supervised child playtesting and real full-screen desktop
  presentation remain **NOT VERIFIED**.
- The second garden region and v0.2.0 release remain future work.

## Honest non-results

- Child playtest: **NOT VERIFIED** (`docs/PLAYTEST_NOTES.md`).
- Windows runtime: **NOT VERIFIED**.
- Actual full-screen presentation on a desktop compositor: **NOT VERIFIED**;
  the setting persists and calls Godot’s window-mode API.
