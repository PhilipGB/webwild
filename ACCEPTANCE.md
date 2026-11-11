# Acceptance — Webwild v0.1.0

Every row is backed by observed evidence or marked **NOT VERIFIED**. The final
implementation commit and exact commands are in `docs/EVIDENCE.md`.

Status key: **PASS** = observed in this delivery; **PARTIAL** = works with a
documented limitation; **NOT VERIFIED** = not performed on this host.

## Experience and gameplay

| Criterion | Status | Evidence |
|---|---|---|
| Launches from a title menu and enters a real low-poly 3D garden, no editor interaction | PASS | Runtime `title-visible`, `new-game-started`, `title-hidden`; `01-title.png`, `02-garden.png` |
| Walk (WASD/arrows), jump (Space), climb the designated stem, mouse + UI | PASS | `movement-wasd` (4.86 units), `jump-space` (1.03), `climb-stem` (4.20); injected mouse clicks build both web types under Xvfb |
| Select highlighted anchors, preview, build, cancel and dismantle both web types | PASS | `capture-web-built`, `bridge-built`, `cancel-no-charge`, `dismantle-refunds`; `03/06-build-mode.png`, `07-capture-web.png` |
| Invalid pair / low silk / occupied edge cannot charge and explains why | PASS | `invalid-pair-rejected`, `low-silk-rejected`, `occupied-edge-rejected`; unit `PlacementTests` |
| Insects travel routes, intersect webs, get trapped, grant food exactly once | PASS | `insect-trapped`, `food-collected`; unit `CaptureAndEconomyTests`; `08-insect-trapped.png` |
| Placement matters: in-lane catches, off-lane does not create food | PASS | Unit `OutOfLaneWeb_CatchesNothing_WithoutIntersection`, `FlyCrossesTutorialWeb_AndIsTrappedExactlyOnce` |
| Silk/food balance and no permanent tutorial softlock | PASS | Unit `EmergencyRecovery_IsLimitedAndOnlyAtLowSilk`, `SilkProduction_IsBoundedAndRequiresFood`; runtime resource checks |
| Nursery hatches to five total spiders; visible workers; worker crosses a bridge | PASS | Unit `Hatching_StopsAtFiveSpidersIncludingPlayer`; runtime `worker-hatched`, `worker-crossed-bridge`; `09-nursery.png` |
| Workers collect, repair and release unreachable tasks without manual commands | PASS | Unit `WorkerAndMissionTests` (collect, repair, no teleport, route invalidation); runtime `worker-crossed-bridge` |
| Wind is telegraphed; exposed web damaged; player can repair or adapt | PASS | Runtime `wind-completed`, `web-damaged-or-repaired`, `web-restored`; `10-wind-warning.png`; unit `WindTests` |
| M01/M02/M03 completable in a fresh runtime; victory allows continued play; mastery optional | PASS (automated) | Runtime fresh-save flow `m01-complete` → `m02-complete` → `m03-complete` → `victory`; unit `FullStoryMissionFlow_ReachesVictory`; `11-victory.png` |
| New Game, Continue, Pause/Resume, Help, Settings, Quit to Menu | PASS | Runtime `pause-opens/closes`, `help-opens`, `colony-opens`, `continue-loads`, `quit-to-menu`; `03/04/05/14` screenshots |

## Usability and quality

| Criterion | Status | Evidence |
|---|---|---|
| A first-time seven-year-old can follow hints and build the first web unaided | **NOT VERIFIED** | No child playtest was performed; see `docs/PLAYTEST_NOTES.md` |
| Food/Silk, objective, selection, cost and feedback legible at 1280×720 | PASS | Adult visual inspection of `02-garden.png`, `06-build-mode.png`, `12-settings.png` |
| Consistent input: click select, right-click/Esc cancel, E interact, Esc pause, Tab colony; modals block background | PASS | Runtime checks and `_UnhandledInput`/modal stop handling; `04-help.png`, `05-colony.png` |
| Contrast, UI scale, reduced motion and volume settings function and persist | PASS (full-screen actual presentation NOT VERIFIED) | `settings-persist`, `ui-scale-applies` (1.25), `ui-scale-150-applies` (1.50), `low-threat-applies`; `15/16` screenshots. Full-screen toggle persists and calls Godot’s window mode, but no compositor test |
| Sound and visuals communicate capture, damage, build and success even with audio muted | PASS | Visual toasts/status/icons and web colour changes in screenshots; audio buses and WAVs load (`audio-buses`) |
| Cohesive authored detail; no prominent untextured placeholder; camera not clipping main quest | PASS (stylised) | Visual inspection of all 16 screenshots; SpringArm collision mask; procedural ground/path shapes are deliberate authored geometry, not unfinished cubes |

## Engineering and delivery

| Criterion | Status | Evidence |
|---|---|---|
| Godot 4.7.2 .NET + C# / net10.0; clean-checkout launch | PASS | `godot --version`; `godot --headless --path . --editor --quit`; imported and ran from source |
| Pure deterministic tests for validation, capture, economy, workers, missions, wind, recovery, saves | PASS | `dotnet test`: **45 passed, 0 failed, 0 skipped** |
| Godot headless import + C# startup + dedicated runtime acceptance smoke | PASS | Headless import exit 0; headless probe **58/58**; rendered probe **58/58** |
| Versioned save, validation, atomic write, backup recovery, no false “Saved” | PASS | `SaveTests` roundtrip, corrupt main, invalid schema, failed write; runtime `save-succeeds`, `save-loads`, `reload-*` |
| Private repo with source, assets/attribution, instructions, tests and evidence | PASS | `PhilipGB/webwild` private; README/ATTRIBUTION/evidence tracked |
| P0–P8 recorded with commit/PR links and green checks | PENDING | Created during delivery; see final report/PROGRESS rows |
| Linux x64 playable export produced and launched/tested | PASS | `exports/linux/Webwild.x86_64`; headless and rendered Xvfb probes both 58/58 |
| Windows x64 export produced; runtime status explicit | PASS for export / **NOT VERIFIED** for runtime | `exports/windows/Webwild.exe` PE32+ x86-64; no Windows host |
| `v0.1.0` tagged only after acceptance and release checks | PENDING | Tag decision recorded in final report |
| Screenshots show actual Godot game | PASS | 16 real PNGs in `artifacts/screenshots/`; runtime probe reports renderer X11 |

## Sign-off

| Area | Result |
|---|---|
| Automated gameplay acceptance | **PASS** — 58/58 runtime checks, 45/45 unit tests |
| Rendered practical inspection | **PASS** — 16 screenshots and full flow |
| Linux release | **PASS** — exported, launched, probe 58/58 |
| Windows release | **PARTIAL** — export PASS, runtime **NOT VERIFIED** |
| Child usability | **NOT VERIFIED** — no child playtest |
| Release tag | `v0.1.0` after the final evidence commit; Windows/child limitations are documented and are not gameplay blockers |
