# Acceptance — Webwild v0.2.0 P2

Every row is backed by observed evidence or marked **NOT VERIFIED**. The final
implementation commit and exact commands are in `docs/EVIDENCE.md`.

Status key: **PASS** = observed in this delivery; **PARTIAL** = works with a
documented limitation; **NOT VERIFIED** = not performed on this host.

## Experience and gameplay

| Criterion | Status | Evidence |
|---|---|---|
| Launches from a title menu and enters a real low-poly 3D garden, no editor interaction | PASS | Runtime `title-visible`, `new-game-started`, `title-hidden`; `01-title.png`, `02-garden.png` |
| Walk (WASD/arrows), jump (Space), climb the designated stem, mouse + UI | PASS | `movement-wasd` (4.86 units), `jump-space` (1.03), `climb-stem` (4.20); injected mouse clicks build both web types under Xvfb |
| Select highlighted anchors, preview, build, cancel and dismantle both web types | PASS | `capture-web-built`, `bridge-built`, `cancel-no-charge`, `dismantle-refunds`; `06-build-mode.png`, `07-capture-web.png` |
| Invalid pair / low silk / occupied edge cannot charge and explains why | PASS | `invalid-pair-rejected`, `low-silk-rejected`, `occupied-edge-rejected`; unit `PlacementTests` |
| Insects travel routes, intersect webs, get trapped, grant food exactly once | PASS | `insect-trapped`, `food-collected`; unit `CaptureAndEconomyTests`; `08-insect-trapped.png` |
| Placement matters: in-lane catches, off-lane does not create food | PASS | Unit `OutOfLaneWeb_CatchesNothing_WithoutIntersection`, `FlyCrossesTutorialWeb_AndIsTrappedExactlyOnce` |
| Silk/food balance and no permanent tutorial softlock | PASS | Unit `EmergencyRecovery_IsLimitedAndOnlyAtLowSilk`, `SilkProduction_IsBoundedAndRequiresFood`; runtime resource checks |
| Nursery hatches to five total spiders; visible workers; worker crosses a bridge | PASS | Unit `Hatching_StopsAtFiveSpidersIncludingPlayer`; runtime `worker-hatched`, `worker-crossed-bridge`; `10-nursery.png` |
| Workers collect, repair and release unreachable tasks without manual commands | PASS | Unit `WorkerAndMissionTests` (collect, repair, no teleport, route invalidation); runtime `worker-crossed-bridge` |
| Wind is telegraphed; exposed web damaged; player can repair or adapt | PASS | Runtime `wind-completed`, `web-damaged-or-repaired`, `web-restored`; `11-wind-warning.png`; unit `WindTests` |
| M01/M02/M03 completable in a fresh runtime; victory allows continued play; mastery optional | PASS (automated) | Runtime fresh-save flow `m01-complete` → `m02-complete` → `m03-complete` → `victory`; unit `FullStoryMissionFlow_ReachesVictory`; `12-victory.png` |
| New Game, Continue, Pause/Resume, Help, Settings, Quit to Menu | PASS | Runtime `pause-opens/closes`, `help-opens`, `colony-opens`, `continue-loads`, `quit-to-menu`; `03/04/05/15` screenshots |

## Usability and quality

| Criterion | Status | Evidence |
|---|---|---|
| A first-time seven-year-old can follow hints and build the first web unaided | **NOT VERIFIED** | No child playtest was performed; see `docs/PLAYTEST_NOTES.md` |
| Food/Silk, objective, selection, cost and feedback legible at 1280×720 | PASS | Adult visual inspection of `02-garden.png`, `06-build-mode.png`, `13-settings.png` |
| Consistent input: click select, right-click/Esc cancel, E interact, Esc pause, Tab colony; modals block background | PASS | Runtime checks and `_UnhandledInput`/modal stop handling; `04-help.png`, `05-colony.png` |
| Contrast, UI scale, reduced motion and volume settings function and persist | PASS (full-screen actual presentation NOT VERIFIED) | `settings-persist`, `ui-scale-applies` (1.25), `ui-scale-150-applies` (1.50), `low-threat-applies`, `low-threat-restores`, `settings-save-error-visible`; `16/17` screenshots. Full-screen toggle persists and calls Godot’s window mode, but no compositor test |
| Sound and visuals communicate capture, damage, build and success even with audio muted | PASS | Visual toasts/status/icons and web colour changes in screenshots; audio buses and WAVs load (`audio-buses`) |
| Cohesive authored detail; no prominent untextured placeholder; camera not clipping main quest | PASS (stylised) | Visual inspection of all 44 screenshots; SpringArm collision mask; ground, paths, flora and landmarks are authored `.tscn` content, not unfinished cubes |

## Engineering and delivery

| Criterion | Status | Evidence |
|---|---|---|
| Godot 4.7.2 .NET + C# / net10.0; clean-checkout launch | PASS | `godot --version`; `godot --headless --path . --editor --quit`; imported and ran from source |
| Pure deterministic tests for validation, capture, economy, workers, missions, wind, recovery, saves, bridge layout and scene contracts | PASS | `dotnet test`: **106 passed, 0 failed, 0 skipped** |
| Godot headless import + C# startup + dedicated runtime acceptance smoke | PASS | Headless import exit 0; scene contract **55/55**; headless probe **246/246**; rendered probe **246/246** |
| Versioned save, validation, atomic write, backup recovery, no false “Saved” | PASS | `SaveTests` roundtrip, corrupt main, invalid schema, failed write; runtime `save-succeeds`, `save-loads`, `reload-*` |
| Private repo with source, assets/attribution, instructions, tests and evidence | PASS | `PhilipGB/webwild` private; README/ATTRIBUTION/evidence tracked |
| P0–P8 and v0.1.x hardening recorded with commit/PR links and green checks | PASS | PRs [#1](https://github.com/PhilipGB/webwild/pull/1)–[#10](https://github.com/PhilipGB/webwild/pull/10) merged with green CI; merge commits in `docs/EVIDENCE.md` |
| Linux x64 playable export produced and launched/tested | PASS | `exports/linux/Webwild.x86_64`; headless and rendered Xvfb probes both 246/246 |
| Windows x64 export produced; runtime status explicit | PASS for export / **NOT VERIFIED** for runtime | `exports/windows/Webwild.exe` PE32+ x86-64; no Windows host |
| `v0.2.0` P0 merged only after exact-head checks pass | PASS | Dedicated branch, PR and green CI; no v0.2.0 release is published yet |
| Screenshots show actual Godot game | PASS | 44 real PNGs in `artifacts/screenshots/`; runtime probe reports renderer X11 |

## v0.1.1 independent-review hardening

| Review finding | Status | Evidence |
|---|---|---|
| Long sessions could eventually invalidate saves through unbounded event/ledger history | PASS | `HardeningTests.LongSession_BoundsHistories_AndStillValidatesForSaving`; bounded retention for issued events, ledger keys and mission seen events |
| Quitting after a failed save discarded progress | PASS | Runtime `failed-save-keeps-session`; Save-and-quit keeps the session and offers retry, keep playing, or quit without saving |
| Player could cross the pebble gap without a bridge | PASS | Runtime `ravine-blocks-without-bridge` and `bridge-enables-player-crossing`; real ravine plus walkable bridge ramps/collision; `09-bridge-crossing.png` |
| Silk production ignored food cost and workers | PASS | `HardeningTests.SilkProduction_ConsumesFood_AndStopsWhenFoodRunsOut`, `SilkProduction_ScalesWithHatchedHelpers`; runtime `silk-producers` |
| Gentle weather could not be switched off mid-session | PASS | `HardeningTests.GentleWeather_RestoresBaselineWhenSwitchedOff`; runtime `low-threat-applies`, `low-threat-restores` |
| Pause did not fully freeze player physics | PASS | Runtime `pause-freezes-airborne`, `pause-resumes-physics`; `SpiderPlayer.PhysicsPaused` |
| Settings persistence errors were silent | PASS | `SettingsService` failing-write test; runtime `settings-save-error-visible`; persistent retry affordance |
| Repository visibility differed from the private-release claim | PASS | `gh repo view` now reports `PRIVATE`; corrected during v0.1.1 (PR [#9](https://github.com/PhilipGB/webwild/pull/9)) |
| Persistent garden geometry is constructed in C# rather than authored `.tscn` | RESOLVED in v0.2.0 P0 | Garden geometry moved to `scenes/world/**/*.tscn`; `GardenScenery.cs` removed and guarded by validation |

## v0.1.2 independent-review hardening

| Review finding | Status | Evidence |
|---|---|---|
| Bridge approach ramps used fixed X offsets and broke for reversed selection or Z-axis spans | PASS | `BridgeGeometry.ComputeApproachPoints` uses authored bank points for ravine posts and span-relative inference otherwise; `BridgeGeometryTests`; runtime `bridge-ramps-west-east`, `bridge-reverse-ramps`, `bridge-zaxis-ramps`, `bridge-reverse-enables-crossing`, `bridge-removal-blocks-crossing`; `18-bridge-reversed.png`, `19-bridge-z-axis.png` |
| Save/load lost the chronological order of bounded idempotency histories | PASS | Ordered queues are serialised instead of sets; `HistoryOrderTests` round-trip, continue evicting oldest-first and verify resources, missions and workers; legacy unordered saves still load |
| Failed settings saves left orphaned temporary files | PASS | `SettingsService.Save` tracks and removes its temp file on every failure without masking the original error; injected partial-write, copy, move, retry and cleanup-failure tests |

## v0.2.0 P0 — scene-first world

| Criterion | Status | Evidence |
|---|---|---|
| Persistent garden is authored in reusable `.tscn` scenes | PASS | `scenes/world/GardenWorld.tscn` instances environment, terrain, paths, landmarks, flora and boundaries; 123 model instances and 934 meshes resolve |
| Opening `GardenClearing.tscn` displays the assembled garden | PASS | Scene instantiates through `--scene-validate`; gameplay and rendered probes load the same authored scene; `02-garden.png` matches v0.1.2 |
| Scenery, lighting and collision are editable in the Godot editor | PASS | Authored `WorldEnvironment`, lights, `StaticBody3D`/`CollisionShape3D` and model instances in `scenes/world/**`; documented in `docs/SCENE_AUTHORING.md` |
| Anchor ids and coordinates cannot silently drift from the simulation | PASS | `SceneContentValidator` and `SceneAuthoringTests` compare 12 authored sockets against `WorldLayout`; deliberate drift produces `anchor-transforms-match-simulation` and non-zero exit |
| Nursery, climb and bridge-bank markers align with gameplay | PASS | `nursery-marker`, `climb-markers`, `bridge-bank-markers`, `NurseryClimbAndBridgeMarkers_MatchSimulationCoordinates` |
| Landmarks are grouped and reusable | PASS | Eight named landmark scenes instanced once each under `GardenLandmarks.tscn`; unique-instance and scene-path checks |
| Procedural garden builder is gone | PASS | `scripts/world/GardenScenery.cs` deleted; validator and pure test reject its return |
| v0.1.2 gameplay and saves remain intact | PASS | `reload-*`, `bridge-reverse-*`, `bridge-zaxis-*`, mission flow, unit suites; save schema unchanged |
| Dev can move a decorative prop and reopen without C# | PASS | Temporary edit moved `HomeCache` from global x −15.2 to −14.85, reopened, then reverted; see `docs/EVIDENCE.md` |
| Moving an anchor without updating gameplay data fails clearly | PASS | Temporary anchor drift produced two named validation failures and exit 1, then reverted to 24/24 |

## v0.2.0 P1 — Reedwater Bank second region

| Criterion | Status | Evidence |
|---|---|---|
| Second region is a visually distinct, authored pond/marsh with shoreline, reeds, cattails, lilies, mossy islands and a far bank | PASS | `scenes/world/regions/ReedwaterBank/**`; 47/47 scene checks; `21-reedwater-arrival.png`, `22-reedwater-pond.png`, `22b-reedwater-overview.png`, `24-reedwater-crossing.png` |
| Supplied Reedwater GLBs and `.tres` material import and render with vertex colours; dragonfly used as an insect model | PASS | Godot editor import exit 0; 66 model instances/397 meshes in region validation; `rw-material-resource`; rendered dragonflies visible in `25-reedwater-capture-trapped.png` |
| Travel is explicit, discoverable, keyboard/mouse accessible, and works after story completion and after save/load | PASS | `travel-dialog-opens`, `travel-to-reedwater`, `return-to-garden`, `reenter-reedwater`; `20-travel-dialog.png`, `29-returned-garden.png` |
| No duplicate player/UI/audio/runtime roots; region resources and helpers preserved | PASS | Single session player/UI reused; `garden-state-preserved`, `reload-reedwater-construction`, `reenter-reedwater-objectives` |
| Water blocks traversal without bridges using real collision | PASS | Four authored `Area3D` water volumes; runtime `water-blocks-without-bridge`; `22-reedwater-pond.png` shows splash feedback |
| Two bridge decisions connect the bank to the lily island; reversed selection and removal invalidate/restore the route | PASS | `pond-route-blocked-before-bridges`, `bridge-one-built`, `bridge-two-built`, `pond-route-open-after-bridges`, `critical-bridge-breaks-route`, `critical-bridge-restores-route`; unit `ReversedBridgeSelectionOrder_BuildsTheSameRoute`, `RemovingACriticalBridgeInvalidatesTheWorkerRoute` |
| Player can physically cross using bridge decks/ramps | PASS | `player-crosses-reedwater-bridge` (x=0.12 after crossing); `24-reedwater-crossing.png` |
| R01, R02 and R03 complete through real gameplay events; R03 requires a worker bridge route | PASS | `r01-complete`, `r02-complete`, `r03-complete`, `worker-used-reedwater-bridge`; unit `WorkerCrossesThreeBridgesAndCompletesR03` |
| Reed flyways reward placement; one easy and one harder route; dragonfly paths | PASS | Three authored routes in `ReedwaterLayout`; `reedwater-insects-collected`; `25-reedwater-capture-trapped.png`, `25-reedwater-capture.png` |
| Optional Clever Crossing mastery is deterministic and achievable | PASS | `clever-crossing-earned`; exactly two Reedwater bridge builds; saved idempotently |
| Completion acknowledgement does not end free play | PASS | `reedwater-completion-feedback`; `27-reedwater-complete.png`; Keep Playing/Save buttons |
| Shared vs regional state ownership is explicit; no resource duplication on switching | PASS | Shared ledger/population/objectives/settings; region-owned webs/insects/workers/players; unit `ReedwaterSupplyCache_IsGrantedExactlyOnce`, `SavingInReedwater_PreservesRegionsObjectivesAndResources` |
| Legacy v0.1.0/v0.1.1/v0.1.2 saves load through explicit schema-1→2 migration | PASS | `tests/fixtures/save_v0_*.json`; `SaveMigrationTests`; garden webs/workers/missions preserved |
| Save in Reedwater records active region and restores construction/objectives | PASS | `save-records-active-region`, `save-records-reedwater-webs`, `save-records-reedwater-objectives`, `reload-in-reedwater` |
| Both regions validated (`--scene-validate`), no procedural fallback for a missing mandatory scene | PASS | 47/47 scene checks; `Main.StartGame` throws if `GardenClearing.tscn` is missing; pure test rejects `new GardenClearing()` |
| Existing garden tests/checks remain green | PASS | 93/93 unit tests; 168/168 runtime checks including all original garden checks |
| Rendered screenshots are from the running game | PASS | `20-travel-dialog` through `29-returned-garden`; Xvfb GL renderer |

## v0.2.0 P2 — modular bridge, reliability and polish

| Criterion | Status | Evidence |
|---|---|---|
| Reedwater +6 food/+24 silk cannot be re-granted after 512+ unrelated ledger keys evict the history entry | PASS | Durable `ClaimedSupplyCaches` in schema v3; unit `ReedwaterSupplyCache_SurvivesLedgerHistoryEvictionAndSaveRoundTrip` pushes 602 filler transactions, repeats travel, exports/reloads and repeats travel |
| Save/JSON/load and schema-1/P1 schema-2 saves keep economy, colony, missions and region state intact | PASS | Explicit 1→2→3 migration; `tests/fixtures/save_v0_2_0_p1*.json` are real P1 saves; `SaveMigrationTests`; runtime `save-records-supply-claim`, `reenter-supply-not-regranted` |
| Bridge collision remains unchanged/equally verified on both regions, both anchor orders, X/Z/diagonal spans and ramps | PASS | Shared `BridgeGeometry.EvaluateSpan`/`ComputeSag`; `BridgeLayoutTests`; runtime `bridge-reverse-*`, `bridge-zaxis-*`, `player-crosses-reedwater-bridge`, `bridge-removal-blocks-crossing` |
| Supplied authored GLBs are actually used with three plank variants, fixtures, knots, supports and panels | PASS | `BridgeVisualAssembler` loads all 11 wrappers; runtime signatures `variants=7`, `details>=16`, `panels>0`; `bridge-parts-exist` scene check |
| No opaque gold ribbon/box remains visible over the walking deck | PASS | Legacy `WebMeshFactory.BuildBridge` and walkway BoxMesh removed; `bridge-no-legacy-ribbon`; runtime `*-no-solid-ribbon`; `09b-modular-bridge-detail.png` |
| Planks follow sag/tangent and reversed/Z/diagonal orientation with no NaN or missing geometry | PASS | Pure direction theory tests plus runtime assembled views; generated rails are finite curved tubes |
| Constructed and preview bridges use the same assembler; preview has no collision and keeps green/coral cues | PASS | `bridge-preview-uses-assembler`, `bridge-preview-has-no-collision`, `bridge-preview-valid-cue`, `bridge-preview-invalid-cue` |
| Damage presentation is clear without changing physics/repair rules | PASS | `bridge-damage-visual` checks all rendered meshes receive the restrained overlay; `09c-bridge-damaged.png` |
| Toasts avoid player/camera centre, mission, action column, status hint and toolbar at 100/125/150% | PASS | Rect checks `toast-avoids-*` at all three scales; `16a-ui-toast-100.png`, `16-ui-scale-125.png`, `17-ui-scale-150.png` |
| Vertex colours work in editor and Linux export | PASS | Runtime `*-vertex-materials` reports zero unstyled meshes in exported build; rendered `23b-reedwater-bridge-detail.png` from exported Linux |
| Existing scene/gameplay checks retained and expanded | PASS | 51/51 scene checks; 236/236 runtime checks; 106/106 tests |
| Linux release archive re-downloaded, checksum-matched and run | PASS | See `docs/EXPORTS.md` and `docs/EVIDENCE.md#v020-p2` |
| Windows x64 export produced; runtime status explicit | PASS for export / **NOT VERIFIED** for runtime | `exports/windows/Webwild.exe` PE32+ x86-64; no Windows host |
| Repository private; annotated v0.2.0 tag/release only on verified main | PASS | `gh repo view` private; release workflow recorded in `docs/EVIDENCE.md#v020-p2` |

## v0.2.1 — Reedwater shoreline correction

| Criterion | Status | Evidence |
|---|---|---|
| Pond water occupies only intended water; dry banks are not covered | PASS | Authored water mesh bounds x −7.13..6.20 versus banks at −7..6; `rw-water-shore-bounds`, `rw-water-below-banks`; `22d-start-shoreline`, `22e-far-shoreline` |
| No rectangular water boundary crosses grass | PASS | Wavy static ArrayMesh plus shelf; previous oversized `PlaneMesh` removed; visual inspection of `22b`/`22d`–`22g` |
| Water meets shoreline cleanly without exposed gaps | PASS | Continuous shore shelf; `rw-water-shore-bounds`; visual inspection of low-angle `22g` |
| Islands sit correctly within the pond | PASS | Island props/colliders unchanged; `22f-island-edges`; runtime island-safe queries |
| Water remains continuous where designed | PASS | Single authored surface; bridges and `24-reedwater-crossing` show continuous water beneath |
| Water colour and friendly low-poly style preserved | PASS | Same water material palette/emission; `21`, `22b`, `23b` |
| Water renders from elevated, low-angle and rotated views without sorting artefacts | PASS | `22b-reedwater-overview`, `22d`, `22e`, `22f`, `22g`, `23b`, `24` inspected |
| Water hazard detection remains correct; banks/islands safe; open water recovers | PASS | Runtime `shoreline-start-bank-dry`, `shoreline-far-bank-dry`, `shoreline-island-a/b-safe`, `shoreline-open-water-is-hazardous`, `shoreline-water-recovery` |
| Bridge traversal, bridge ramps, no-bridge rejection and worker navigation remain correct | PASS | Existing `water-blocks-without-bridge`, `player-crosses-reedwater-bridge`, `worker-used-reedwater-bridge`, `critical-bridge-*`; no bridge-collision edits |
| Region transitions, missions, spawn and save/load remain intact | PASS | Existing travel/reload/objective checks and pure tests; scene/save schema unchanged |
| Existing tests retained and new geometry assertions added | PASS | 106/106 unit tests; 55/55 scene checks; 246/246 runtime checks |
| Exported Linux rendering inspected | PASS | Exported headless scene 55/55 and runtime 246/246; exported rendered runtime 246/246 |
| Windows runtime status explicit | PASS for export / **NOT VERIFIED** for runtime | PE32+ export rebuilt; no Windows host |
| v0.2.1 release status | Included in v0.2.2 | No separate v0.2.1 release; the shoreline correction ships in the published v0.2.2 archives |

## v0.2.2 PR 1 — bridge traversal and save safety

| Criterion | Status | Evidence |
|---|---|---|
| Walking from bank to bridge one and onto island A uses ordinary movement | PASS | `player-walks-bank-onto-bridge-one`, `player-walks-bridge-one-to-island-a`; `24-reedwater-crossing.png` |
| Walking from island A to island B and from island B to the far bank | PASS | `player-walks-island-a-onto-bridge-two`, `player-walks-island-b-onto-bridge-three`; `24b-reedwater-far-bank-walk.png` |
| Every bridge walked in reverse without jumping | PASS | `player-walks-bridge-two-back-to-island-a`, `player-walks-bridge-one-back-to-bank`, `player-walks-bridge-three-back-to-island-b` |
| Stopping/restarting at a deck joint and turning on the deck | PASS | `player-stops-and-resumes-at-deck-joint`, `player-turns-around-on-bridge` |
| Crossing still works after save/reload | PASS | `reload-reedwater-bridge-walk` |
| Walkway is 1.4–1.6 m wide and every face is a walkable top face | PASS | `BridgeWalkwayTests.WalkwaySurface_IsWideEnoughForThePlayerCapsule`, `WalkwaySurface_HasOnlyUpwardFaces_AndNoThinEndCaps` |
| Walkway is continuous, follows the visible sag and is anchor-order independent | PASS | `WalkwaySurface_IsContinuousAcrossRampAndDeckJoints`, `WalkwaySurface_FollowsTheSameSagAsTheVisibleDeck`, `WalkwaySurface_IsIndependentOfAnchorSelectionOrder` |
| Island approaches align with their spans; no post blocks a deck | PASS | Conditional `BridgeGeometry.ComputeApproachPoints`; `IslandA.tscn`/`FarBank.tscn` post offsets |
| Water still blocks a crossing without a bridge | PASS | `water-blocks-without-bridge`; existing shoreline checks |
| Worker route remains correct | PASS | `worker-used-reedwater-bridge`; existing unit `WorkerCrossesThreeBridgesAndCompletesR03` |
| New Garden asks before replacing an existing garden | PASS | `new-garden-confirm-opens`, `new-garden-confirm-default-safe`; `30-new-garden-confirm.png` |
| Cancel leaves the existing save unchanged; repeated clicks cannot bypass confirmation | PASS | `new-garden-cancel`, `new-garden-repeat-click-stays-confirming` |
| Confirmed New Garden keeps a durable previous-garden archive across later autosaves | PASS | `new-garden-confirm-archives-previous`, `new-garden-autosave-keeps-archive`; `ArchiveExisting_KeepsPreviousGardenAcrossLaterAutosaves` |
| Previous garden can be restored from the title screen | PASS | `restore-previous-available`, `restore-previous-garden` |
| Backup recovery still works | PASS | `ArchiveExisting_UsesHealthyBackupWhenMainIsCorrupt`; existing corrupt-main tests |

## v0.2.2 PR 2 — anchor interaction, nursery and progression

| Criterion | Status | Evidence |
|---|---|---|
| Recommended anchors are strongly highlighted; unrelated anchors are dimmed | PASS | `anchor-recommended-highlight`, `anchor-unrelated-dimmed`; `06-build-mode.png`, `22h-reedwater-build-anchors.png` |
| Labels appear only for recommended/near/hovered/selected sockets | PASS | `anchor-labels-only-near-or-recommended`; `22h` |
| Screen-space nearest-target selects the intended overlapping socket | PASS | `anchor-screen-nearest-target`; `PickAnchor` ray + 30 px nearest |
| Empty space does not select a socket | PASS | `anchor-screen-empty-space-does-not-select` |
| Foreground bodies block selection | PASS | `PickAnchor` body-distance and occlusion tests; rendered clicks still reach the intended sockets |
| Second anchor click never spends silk; From → To · cost is shown | PASS | `build-confirm-shows-pair`, `build-confirm-does-not-spend-yet`; `06b-build-confirm.png`, `16b-ui-build-confirm-100.png` |
| Cancel leaves silk and the world untouched | PASS | `build-cancel-no-charge` |
| Build/Enter confirms once; repeated confirms cannot double-charge | PASS | `capture-web-built`, `build-confirm-repeated-click-safe`; `Build` button focused |
| Confirmation fits at 100%, 125% and 150% UI scale | PASS | `build-confirm-inside-viewport-*`, `build-confirm-avoids-mission-*`, `build-confirm-avoids-actions-*` |
| Nursery progress advances while open | PASS | `nursery-progress-visible`, `nursery-progress-advances`; `10a-nursery-hatching.png` |
| Nursery shows neutral hatching text and separates the pending egg | PASS | `nursery-neutral-hatching-text`, `nursery-pending-count` |
| Nursery refreshes after hatching with living-only count | PASS | `nursery-refreshes-after-hatch`, `nursery-counts-living-only`; `10b-nursery-hatched.png` |
| Living spider count excludes unhatched eggs | PASS | `PendingEgg_IsNotCountedAsALivingSpider` |
| Completion modals do not stack over the nursery | PASS | `ShowVictory`/`ShowRegionComplete` close an open panel first; code review |
| Clever Crossing is not awarded by building alone | PASS | `clever-crossing-not-yet-earned`; `TwoBridgesOpenR01_AndPhysicalArrivalAwardsCleverCrossing` |
| Clever Crossing is awarded on physical island arrival with two bridges | PASS | `clever-crossing-earned-by-arrival`; `24a-clever-crossing-arrival.png` |
| Three standing bridges prevent the badge | PASS | `CleverCrossing_IsNotAwardedWithThreeStandingBridges` |
| Earned mastery survives save/load | PASS | `CleverCrossing_SurvivesSaveAndReload` |
| The mission card explains the challenge and the current bridge count | PASS | `☆ Clever Crossing` line in `22h-reedwater-build-anchors.png` |

## v0.2.2 PR 3 — guidance, pacing and feedback

| Criterion | Status | Evidence |
|---|---|---|
| Status follows the active step and clears stale prompts | PASS | `status-clears-after-objective`; `GameUI.UpdateHud` live hint |
| Catch/collect feedback names the insect species | PASS | `dragonfly-named-correctly`, `player-collection-names-species`; real-input toasts |
| Worker collections are attributed and visibly marked | PASS | `worker-attributed-collection`, `worker-activity-glow`; `Pip collected a dragonfly · +4 food` in the real-input region-complete frame |
| Far-bank delivery has explicit feedback | PASS | `far-bank-delivery-feedback`; "… from the far bank" toast |
| R02 guidance adapts to the nearest flyway | PASS | `UpdateReedwaterSuggestions` compares reed and lily distances; real run used the lily flyway |
| R03 guidance matches a reachable/remote-build goal | PASS | `GetRegionObjectiveHint` explains building the far web from the root island |
| Wind guidance describes shelter then repair | PASS | `GetHint`, `data/missions.json` M03 hint, HUD warning label |
| E chooses the nearer outpost interaction | PASS | `TryInteract` travel/nursery distance check; real-input return opened the travel dialog |
| Reedwater respawns support regular progress | PASS | `ReedwaterInsectRespawns_StayWithinAChildFriendlyWindow` (10 s); `R02_ProgressesWithinOneMinuteAfterTheCaptureWeb`; `R03_DeliveryDoesNotStallForOverAMinute` |
| First helper patrol reliably demonstrates a bridge crossing | PASS | `FirstHelperPatrol_CrossesABridgeWithoutWaitingForRandomLuck` (≤15 s) |
| Far-bank delivery counts even when the helper is already there | PASS | `FarBankDelivery_CountsWhenTheHelperIsAlreadyOnTheFarBank`; real-input R03 completed |
| Optional reed trail reduces post-R03 backtracking | PASS | `far-bank-trail-visible`, `far-bank-return-shortcut`; `29-reed-trail.png` |
| Bridge construction has a short restrained effect | PASS | `bridge-build-animation-starts`, `bridge-build-animation-finishes`; rendered frames |
| Reduced motion skips the construction effect | PASS | `bridge-animation-respects-reduced-motion` |
| Existing tests remain green | PASS | 124/124 tests; 55/55 scene; 305/305 headless; 306/306 rendered |
| Fresh real-input six-mission acceptance | PASS | New Garden→M03→Reedwater→R03→return→save/quit/continue with no probes or teleports; 0 failures in 447 s. Key timings: M02 helper crossing 18.2 s, longest R02 gap 29.6 s, R03 delivery 13.2 s. |

## v0.2.2 follow-up — Clever Crossing arrival contact

| Criterion | Status | Evidence |
|---|---|---|
| The badge cannot be earned from the bridge approach | PASS | `CleverCrossing_RequiresGroundedIslandSurface`; runtime `clever-crossing-not-awarded-on-bridge` after a real-input walk across both bridges |
| The badge cannot be earned while airborne or falling past the island | PASS | Ground-contact argument required by `TryAwardCleverCrossingOnArrival`; focused unit cases at centre ± Y |
| Nearby water is not arrival | PASS | Surface-height and footprint checks reject water-level points; focused unit case |
| The badge is earned on the island's own walkable surface | PASS | `player-steps-onto-lily-island` walks off the deck; physics raycast reports `IslandB/MossIsland/Collision` at y≈0.13; badge then earned. `24a-clever-crossing-arrival.png` refreshed |
| Existing coverage remains green | PASS | 125/125 tests; 55/55 scene; 307/307 headless; 308/308 rendered; exported Linux repeats all four |

## Sign-off

| Area | Result |
|---|---|
| Automated gameplay acceptance | **PASS** — 307/307 headless and 308/308 rendered runtime checks; 125/125 unit tests; 55/55 scene contracts |
| Rendered practical inspection | **PASS** — v0.2.2 screenshots; targeted real-input Reedwater crossing and island step-off. The full six-mission real-input run remains the preceding build's 0-failure 447 s record; this change touches only the optional mastery gate |
| Linux export | **PASS** — exported, launched, scene 55/55 and probe 307/307 headless, 308/308 rendered |
| Windows release | **PARTIAL** — PE32+ export PASS, runtime **NOT VERIFIED** |
| Child usability | **NOT VERIFIED** — no supervised child playtest |
| Milestone status | **RELEASED as v0.2.2** with Linux and Windows archives. Windows runtime and child playtesting remain documented limitations |
