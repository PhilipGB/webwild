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
[#8](https://github.com/PhilipGB/webwild/pull/8).

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
| P8 | Runtime playthrough, screenshots, exports, acceptance, release tag | DONE (Windows runtime NOT VERIFIED) | `docs/EVIDENCE.md#p8`; `docs/EXPORTS.md`; 66/66 rendered probe; 17 real screenshots |
| v0.1.1 | Independent-review hardening: bounded save history, safe failed-save quit, real ravine/bridge traversal, silk food cost, difficulty reset, pause physics, settings retry | DONE | `CHANGELOG.md`; `HardeningTests`; runtime checks `pause-freezes-airborne`, `ravine-blocks-without-bridge`, `bridge-enables-player-crossing`, `failed-save-keeps-session`, `settings-save-error-visible`, `low-threat-restores` |

## Verification summary

- `dotnet test`: **51 passed, 0 failed, 0 skipped**.
- Headless runtime probe: **66 passed, 0 failed**.
- Rendered runtime probe (Xvfb, 1280×720): **66 passed, 0 failed**; 17 real PNGs.
- Exported Linux build, headless probe: **66 passed, 0 failed**.
- Exported Linux build, rendered Xvfb probe: **66 passed, 0 failed**.
- Linux x64 release export: built successfully and launched.
- Windows x64 release export: built successfully.
- Windows runtime: **NOT VERIFIED** — no Windows host available.
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
- Persistent garden geometry is built in C# for v0.1.x; moving it into authored
  `.tscn` scenes is planned for v0.2.0.
- The repository was restored to private visibility during the v0.1.1 hardening
  pass.
