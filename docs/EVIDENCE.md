# Evidence index

All evidence was produced on the build host for v0.1.1. Commands are copy-pasteable from the
repository root unless a working directory is shown. No PASS in this file is
inferred from source alone: every result comes from a command, a runtime check or
an inspected screenshot.

- Host: Linux x64
- `dotnet --version` → `10.0.114`
- `godot --version` → `4.7.2.stable.mono.official.ed1daf0bf`
- Godot .NET export templates 4.7.2 installed
- GitHub repository: `PhilipGB/webwild` (private), `https://github.com/PhilipGB/webwild`
- Final merged implementation commit: `__RELEASE_COMMIT__`
- Release tag: `v0.1.1` (created after the final green checks; see the final report for the tag SHA)

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

All eight PRs had green **Build and pure tests** and **Godot .NET import and
startup** checks before merge.

## P0 — Toolchain and repository

- `dotnet build -warnaserror` → success, 0 warnings, 0 errors
- `dotnet test` → final result 51 passed, 0 failed, 0 skipped
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
Passed! - Failed: 0, Passed: 51, Skipped: 0, Total: 51

$ xvfb-run -a -s "-screen 0 1280x720x24" \
    godot --path . --resolution 1280x720 -- --runtime-probe
[PROBE] SUMMARY passes=66 failures=0
```

`artifacts/runtime_probe.json` contains all 66 named checks. The 17 inspected
screenshots are in `artifacts/screenshots/`.

### Exported Linux build

```text
$ godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
exit 0

$ cd exports/linux && ./Webwild.x86_64 --headless -- --runtime-probe
[PROBE] SUMMARY passes=66 failures=0
exit 0

$ cd exports/linux && xvfb-run -a -s "-screen 0 1280x720x24" \
    ./Webwild.x86_64 --resolution 1280x720 -- --runtime-probe
[PROBE] SUMMARY passes=66 failures=0
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

## Honest non-results

- Child playtest: **NOT VERIFIED** (`docs/PLAYTEST_NOTES.md`).
- Windows runtime: **NOT VERIFIED**.
- Actual full-screen presentation on a desktop compositor: **NOT VERIFIED**;
  the setting persists and calls Godot’s window-mode API.
