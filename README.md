# Webwild

**A child-friendly spider colony adventure in one garden clearing.**

Webwild is an original, offline 3D game built with **Godot 4.7.2 .NET**, **C#** and
**.NET 10 (`net10.0`)**. You play a friendly garden spider: spin capture webs to
catch flying insects, build silk bridges, hatch helper spiders, weather a gust of
wind and complete three short story goals.

This repository is the first-playable vertical slice (P0–P8). The deterministic
game rules live in `src/Core` with no `Godot.*` dependency; Godot scenes and
scripts adapt those rules to 3D presentation, physics, input and UI.

## Requirements

- Godot **4.7.2 .NET** editor/executable (the Mono/.NET build, not the standard build)
- .NET SDK **10.0** (verified with 10.0.114)
- Matching Godot .NET export templates for Linux and Windows x64

## Build and test

```bash
dotnet restore
dotnet build -warnaserror
dotnet test
```

Final verified result: **65 passed, 0 failed, 0 skipped**.

## Run from source

```bash
godot --path . --editor          # open in the Godot .NET editor
godot --path .                    # play the game
```

If `godot` points at the non-.NET build, call the actual .NET executable, e.g.
`Godot_v4.7.2-stable_mono_linux.x86_64 --path .`.

## Runtime checks

```bash
godot --headless --path . --editor --quit     # import and C# build
godot --headless --path . -- --runtime-probe  # headless runtime acceptance
```

For rendered acceptance and real screenshots:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" \
  godot --path . --resolution 1280x720 -- --runtime-probe
```

The probe writes `artifacts/runtime_probe.json` and real PNGs in
`artifacts/screenshots/` when run from the project. Final verified result:
**80 passed, 0 failed**. The exported Linux build is re-tested for this release.

## How to play

- **WASD / arrow keys** move, **Space** jumps, mouse wheel zooms.
- Right-drag orbits the camera when no build tool is active.
- **1** capture web, **2** silk bridge. Click two glowing anchors; right-click or
  **Esc** cancels without spending silk. Green preview means safe, coral means
  the reason is shown.
- **E** interacts (collect a trapped insect, use the nursery, repair a web).
  The action column also provides mouse buttons for Interact, Remove web and Pause.
- **X** dismantles a nearby web. **Tab** shows the colony. **Esc** pauses or
  cancels a build.
- Walk into the tall stem’s climb volume and push forward to climb.

Story: build a capture web and collect food, build a silk bridge and hatch a
helper, then protect a web through the wind and collect food afterwards. The
optional **Clever Weaver** badge asks for three insects with two or fewer capture
webs and is never required for victory.

## Implemented scope

- One authored 38 × 28 garden clearing split by a real ravine, with layered
  flora, rocks, log, nursery, flower lane, pebble bridge crossing, windy edge and
  a tall climbable stem.
- Player walk/jump/assisted climb, orbit camera, collision-safe spring arm.
- Twelve typed anchor sockets, capture-web and bridge previews, validation,
  costs, cancellation, dismantling and walkable bridge ramps/deck collision.
- Bridge approach ramps follow the real span and authored bank points, so they
  stay on solid ground for either anchor selection order and for Z-axis spans.
- Four authored insect routes with swept-segment capture, exactly-once rewards
  and respawns.
- Nursery hatching up to five spiders, named autonomous workers that collect,
  repair and patrol across bridge edges.
- Clearly communicated colony economy: silk production consumes food and scales
  with the player plus each hatched helper.
- Three sequential missions, escalating one-line hints, optional mastery,
  victory screen and continued play.
- Telegraph warning, deterministic exposed/sheltered wind damage, repair or
  sheltered alternative, visible wind leaves and audio feedback.
- Title/new game/continue, pause, help, colony, settings, save and quit flows.
- Versioned JSON save with bounded, order-preserving histories, validation,
  atomic write, healthy backup recovery, save-failed quit confirmation and
  explicit failure messages. Failed settings writes clean up their temp file.
- Accessibility: large labelled controls, 100/125/150% UI scale, reduced motion,
  gentle-weather mode, volume controls, camera sensitivity and persisted settings.
- Original synthesised WAV music/SFX; all gameplay information also appears
  visually. No online services, accounts, ads, telemetry or purchases.

## Repository layout

- `src/Core/` — deterministic rules, simulation, validation, missions and saves
- `scripts/` and `scenes/` — Godot presentation, input, physics and UI
- `art/models/` — supplied original low-poly GLB starter assets (see `ATTRIBUTION.md`)
- `art/audio/` — original synthesised WAV files
- `tests/Webwild.Core.Tests/` — pure deterministic xUnit suite
- `data/` — tuning, missions and scenario definitions
- `artifacts/` — real runtime probe report and rendered screenshots
- `docs/` — handoff specification, evidence, playtest and export notes

## Exports

Final local export paths (not committed as binaries because they are large):

- Linux x64: `exports/linux/Webwild.x86_64` plus `Webwild.pck` and
  `data_Webwild_linuxbsd_x86_64/`
- Windows x64: `exports/windows/Webwild.exe` plus `Webwild.pck` and
  `data_Webwild_windows_x86_64/`

The Linux export was launched, rendered and passed the 80/80 runtime probe. The
Windows export built successfully as a PE32+ x86-64 executable, but **Windows
runtime verification is NOT VERIFIED** because no Windows host was available.
See `docs/EXPORTS.md` for checksums and exact commands.

## Evidence and limitations

- `docs/EVIDENCE.md` — commands, counts, screenshots and export evidence.
- `ACCEPTANCE.md` — per-criterion PASS / FAIL / NOT VERIFIED table.
- `PROGRESS.md` — phase status.
- `docs/PLAYTEST_NOTES.md` — child playtest is explicitly **NOT VERIFIED**.
- Windows startup, child playtests and actual full-screen mode on a desktop
  compositor remain unverified.
- Persistent garden geometry is constructed in `GardenScenery.cs` for v0.1.x;
  moving it into authored `.tscn` scenes is planned for v0.2.0. Runtime webs,
  bridges, insects and workers stay dynamic.

## Safety and scope

Offline only. No accounts, ads, telemetry, purchases, loot boxes, graphic
violence or spider death. This is a deliberately fictional cooperative spider
species, not a biology simulation.

## Licence

Original project code and assets: see `ATTRIBUTION.md`. The supplied starter
assets are original handoff assets and are used with attribution there.
