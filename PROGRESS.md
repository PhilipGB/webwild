# Progress — P0 to P8

Status legend: **DONE** = implemented and evidenced; **PARTIAL** = implemented
with a listed limitation; **NOT VERIFIED** = not performed on this host.

The runtime vertical slice was implemented in one integrated pass, then split
into eight phase commits, branches and PRs with phase-specific deterministic
tests and evidence. All eight PRs had green CI before merge. This is recorded
honestly rather than fabricating eight independent feature implementations.

Merged PRs: [#1](https://github.com/PhilipGB/webwild/pull/1),
[#2](https://github.com/PhilipGB/webwild/pull/2),
[#3](https://github.com/PhilipGB/webwild/pull/3),
[#4](https://github.com/PhilipGB/webwild/pull/4),
[#5](https://github.com/PhilipGB/webwild/pull/5),
[#6](https://github.com/PhilipGB/webwild/pull/6),
[#7](https://github.com/PhilipGB/webwild/pull/7),
[#8](https://github.com/PhilipGB/webwild/pull/8) and
[#9](https://github.com/PhilipGB/webwild/pull/9) (v0.1.1 hardening) and
[#10](https://github.com/PhilipGB/webwild/pull/10) (v0.1.2 review hardening) and
[#11](https://github.com/PhilipGB/webwild/pull/11) (v0.2.0 P0 scene-first world).

| Phase | Scope | Status | Evidence |
|---|---|---|---|
| P0 | Toolchain, private repository, C# solution, pure test scaffold, Godot import | DONE | `docs/EVIDENCE.md#p0`; `00d4f58` |
| P1 | Garden clearing, player spider, camera, jump, assisted climb, HUD shell, title flow | DONE | `docs/EVIDENCE.md#p1`; runtime checks `movement-wasd`, `jump-space`, `climb-stem`; screenshots 01–02 |
| P2 | Anchors, capture web and bridge previews, validation, build/cancel/dismantle | DONE | `docs/EVIDENCE.md#p2`; `PlacementTests`; runtime checks `invalid-pair-rejected`, `low-silk-rejected`, `occupied-edge-rejected`, `cancel-no-charge`, `dismantle-refunds` |
| P3 | Insect routes, intersection capture, food/silk economy, recovery | DONE | `docs/EVIDENCE.md#p3`; `CaptureAndEconomyTests`; runtime checks `insect-trapped`, `food-collected` |
| P4 | Nursery, hatched workers, bridge routes, retrieval and repair | DONE | `docs/EVIDENCE.md#p4`; `WorkerAndMissionTests`; runtime checks `worker-hatched`, `m02-complete`, `worker-crossed-bridge` |
| P5 | Three missions, mastery challenge, hints, victory and replay | DONE | `docs/EVIDENCE.md#p5`; `MissionFlowTests`; runtime checks `m01-complete`, `m03-complete`, `victory`, `continue-loads` |
| P6 | Wind forecast/damage/repair, settings, accessibility, polish | DONE | `docs/EVIDENCE.md#p6`; `WindTests`; checks `wind-completed`, `web-restored`, `ui-scale-125/150`, `low-threat-applies` |
| P7 | Versioned save, validation, atomic writes, backup recovery | DONE | `docs/EVIDENCE.md#p7`; `SaveTests`; checks `save-succeeds`, `save-loads`, `reload-*`, `quit-to-menu` |
| P8 | Runtime playthrough, screenshots, exports, acceptance, release tag | DONE (Windows runtime NOT VERIFIED) | `docs/EVIDENCE.md#p8`; `docs/EXPORTS.md`; 80/80 rendered probe; 19 real screenshots |
| v0.1.1 | Independent-review hardening: bounded save history, safe failed-save quit, real ravine/bridge traversal, silk food cost, difficulty reset, pause physics, settings retry | DONE | `CHANGELOG.md`; `HardeningTests`; runtime checks `pause-freezes-airborne`, `ravine-blocks-without-bridge`, `bridge-enables-player-crossing`, `failed-save-keeps-session`, `settings-save-error-visible`, `low-threat-restores` |
| v0.1.2 | Review hardening: order-independent bridge ramps, order-preserving save histories, settings temp-file cleanup | DONE | `CHANGELOG.md`; `BridgeGeometryTests`, `HistoryOrderTests`, settings failure tests; runtime checks `bridge-reverse-*`, `bridge-zaxis-*`, `bridge-removal-*` |
| v0.2.0 P0 | Scene-first world: reusable environment, terrain, landmark, flora and boundary scenes; anchor/marker coordinate contract; procedural builder removed | DONE (no release yet) | PR [#11](https://github.com/PhilipGB/webwild/pull/11); `CHANGELOG.md`; `docs/SCENE_AUTHORING.md`; `SceneAuthoringTests`; 24 scene checks; runtime `scene-*`; screenshots 01–19 |
| v0.2.0 P1 | Reedwater Bank second region: pond, islands, reeds, water collision, travel, three regional objectives, dragonflies, worker crossing, schema-v2 migration | DONE (no release yet) | PR [#12](https://github.com/PhilipGB/webwild/pull/12); `docs/EVIDENCE.md#v020-p1`; `RegionTests`, `SaveMigrationTests`, `SceneAuthoringTests`; 47 scene checks; 168 runtime checks; screenshots 20–29 |

## Verification summary

- `dotnet test`: **93 passed, 0 failed, 0 skipped**.
- Scene contract validation: **47 passed, 0 failed** across both regions.
- Headless runtime probe: **168 passed, 0 failed**.
- Rendered runtime probe (Xvfb, 1280×720): **168 passed, 0 failed**; 29+ real PNGs.
- Exported Linux build, headless probe: **re-run for this milestone**.
- Exported Linux build, rendered Xvfb probe: **re-run for this milestone**.
- Linux x64 export: built successfully and launched.
- Windows x64 export: built successfully; runtime **NOT VERIFIED** (no Windows host).
- Legacy saves v0.1.0/v0.1.1/v0.1.2: migration tests pass with real fixtures.
- Child playtest: **NOT VERIFIED** — see `docs/PLAYTEST_NOTES.md`.

## Known limitations

- No supervised child playtest was performed.
- Windows export has not been started on Windows.
- Actual full-screen mode on a desktop compositor was not visually tested;
  the setting persists and the window mode is applied through Godot.
- The 150% UI scale toast can briefly overlap the action column; permanent
  controls remain visible.
- The game is a deliberately stylised 3D garden, not a photorealistic or
  biologically accurate simulation.
- Both regions are authored in `.tscn` scenes. Gameplay coordinates remain in
  `WorldLayout`/`Regions.cs` and are validated against scenes rather than
  auto-synchronised by an editor plugin.
- Reedwater scenes are cached after first visit; collision, lights and environment
  are disabled when inactive, but GPU resources stay loaded for instant switching.
- The dragonfly is a static model used as an insect; there is no skeletal flight
  animation.
- The repository was restored to private visibility during the v0.1.1 hardening
  pass.
