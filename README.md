# Webwild

**A child-friendly spider colony adventure across an endless low-poly meadow.**

Webwild is an original, offline 3D game built with **Godot 4.7.2 .NET**, **C#** and
**.NET 10 (`net10.0`)**. You play a friendly garden spider: spin capture webs to
catch flying insects, build silk bridges, hatch helper spiders, weather a gust of
wind, forage wild food and complete the garden story before following the
meadows to Reedwater Bank.

This repository is the **v0.3.0 "Never-Ending World"** build. The two authored
landmarks now sit in one continuous, deterministically generated world: three
biomes, carved rivers with old plank crossings, a durable exploration map,
Return Home, wild food that regrows, and helpers that physically walk between
landmarks, keep simulating off screen and can be summoned. All deterministic
rules live in `src/Core` with no `Godot.*` dependency; C# adapts those rules to
3D presentation, physics, input and UI.

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

Final verified result: **231 passed, 0 failed, 0 skipped**.

## Run from source

```bash
godot --path . --editor          # open in the Godot .NET editor
godot --path .                    # play the game
```

If `godot` points at the non-.NET build, call the actual .NET executable, e.g.
`Godot_v4.7.2-stable_mono_linux.x86_64 --path .`.

## Runtime checks

```bash
godot --headless --path . --editor --quit       # import and C# build
godot --headless --path . -- --scene-validate   # authored scene contract
godot --headless --path . -- --runtime-probe    # headless acceptance
```

For rendered acceptance and real screenshots:

```bash
xvfb-run -a -s "-screen 0 1280x720x24" \
  godot --path . --resolution 1280x720 -- --runtime-probe
```

Final verified result: **57/57 scene checks**, **432/432 headless runtime
checks** and **433/433 rendered runtime checks**. The probe writes
`artifacts/runtime_probe.json` and real PNGs in `artifacts/screenshots/`.

## How to play

- **WASD / arrow keys** move, **Space** jumps, mouse wheel zooms; right-drag
  orbits the camera when no build tool is active.
- **1** capture web, **2** silk bridge. Click two glowing anchors; right-click or
  **Esc** cancels without spending silk. Green preview means safe, coral means
  the reason is shown.
- **E** interacts: collect a trapped insect, forage ripe wild food, use the
  nursery, or repair a damaged web. The action column also provides mouse
  buttons for Interact, Remove web and Pause.
- **X** dismantles a nearby web. **Tab** shows the colony. **M** opens the
  wilderness map and Return Home. **Esc** pauses or cancels a build.
- Walk into the tall stem's climb volume and push forward to climb.

Story: build a capture web and collect food, build a silk bridge and hatch a
helper, then protect a web through the wind. After the three garden missions the
bramble gate east of the garden opens; follow the meadows to **Reedwater Bank**
on foot. In the marsh, build two silk bridges to reach the lily island, catch
three insects at the reed flyways, then connect the far bank and let a helper
collect prey there. The optional **Clever Weaver** and **Clever Crossing**
badges are never required for victory.

Helpers are physical colony members: they walk between landmarks through the
hedge gates and generated river bridges, keep making progress while off screen,
and can be called to you from the colony panel (**Tab**). The wilderness map
records explored chunks and offers Return Home from anywhere.

## World systems

- **One continuous world.** The Garden Clearing (origin) and Reedwater Bank
  (world x = 96) are embedded at constant world origins and always resident.
  Procedural terrain connects them through deterministic 24 m chunks.
- **Three biomes** (meadow, moss, bloom) with distinct colours, height offsets
  and dressing, blended continuously across chunk borders.
- **Rivers and crossings.** Meandering east-west rivers are carved into the
  terrain with hazard water and wet banks; stable low plank bridges with woven
  silk rails cross them every 150 m. Pre-river saves that land in water recover
  to the nearest dry bank.
- **Wild food.** Deterministic berries, mushrooms and sunflower seed heads grow
  on dry generated land. Harvesting is once per generation with a tick-based
  ledger key; depletion is stored only in that chunk's small change record and
  regrows on the simulation clock, even while unloaded.
- **Exploration map.** A bounded first-visit record of explored chunks is saved
  once in the world manifest and drawn with biome tint, rivers, visited
  landmarks and the player. Return Home is available from anywhere.
- **Worker navigation.** Helpers follow hierarchical dry world routes through
  landmark gates and generated bridges, record and reverse outbound journeys,
  wait visibly when no route exists, and are simulated off screen. Summon them
  to your current position from the colony panel.
- **Origin rebasing.** Presentation coordinates recentre at 16 km on a 64 m grid;
  saves, simulation, streaming and probe coordinates always stay logical.

## Existing gameplay preserved

- Two authored, editable landmark scenes with the original garden missions
  (M01–M03), the three Reedwater objectives (R01–R03), optional mastery badges,
  insects, webs, bridges, nursery, wind, recovery, settings and title flow.
- Up to five spiders total, named autonomous helpers, capture and bridge webs,
  dismantling, repair, silk economy and food costs.
- Versioned world-manifest saves (schema 4) with per-save seed, atomic writes,
  backup recovery, legacy schema 1–3 migration, a durable previous-garden
  archive, and per-chunk food-changes. Corrupt manifests never silently
  resurrect older legacy progress.
- Accessibility: 100/125/150% UI scale, reduced motion, gentle weather, volume,
  camera sensitivity and persisted settings.
- Original synthesised WAV music/SFX; all gameplay information also appears
  visually. No online services, accounts, ads, telemetry or purchases.

## Repository layout

- `src/Core/` — deterministic simulation, world generation, navigation, saves and validation
- `scenes/world/` — authored garden environment and gameplay composition
- `scenes/world/regions/ReedwaterBank/` — authored second landmark, water, landmarks and anchors
- `scripts/` and other `scenes/` — Godot presentation, input, physics and UI
- `art/models/` — supplied original low-poly GLB assets (see `ATTRIBUTION.md`)
- `art/meshes/` — authored Reedwater water, shore-shelf and pond-bed meshes
- `tests/Webwild.Core.Tests/` — pure deterministic xUnit suite
- `data/` — tuning, missions and scenario definitions
- `artifacts/` — real runtime probe report and rendered screenshots
- `docs/` — handoff specification, ADR, evidence, playtest and export notes

## Exports

Final local export paths (not committed as binaries because they are large):

- Linux x64: `exports/linux/Webwild.x86_64` plus `Webwild.pck` and
  `data_Webwild_linuxbsd_x86_64/`
- Windows x64: `exports/windows/Webwild.exe` plus `Webwild.pck` and
  `data_Webwild_windows_x86_64/`

The v0.3.0 release archives are published on
[GitHub](https://github.com/PhilipGB/webwild/releases/tag/v0.3.0):
`Webwild-v0.3.0-linux-x64.zip` and `Webwild-v0.3.0-windows-x64.zip`. The Linux
archive re-downloads, verifies and passes 432 headless / 433 rendered probes.
The Windows export builds as a PE32+ x86-64 executable, but **Windows runtime
verification is NOT VERIFIED** because no Windows host is available. See
`docs/EXPORTS.md` for checksums and exact commands.

## Evidence and limitations

- `docs/EVIDENCE.md` — commands, exact counts, screenshots and export evidence.
- `ACCEPTANCE.md` — per-criterion PASS / PARTIAL / NOT VERIFIED acceptance.
- `PROGRESS.md` — phase status and merge links.
- `docs/prs/v0.3.0.md` — ten-phase delivery record.
- `docs/PLAYTEST_NOTES.md` — child playtest is explicitly **NOT VERIFIED**.
- Windows startup, supervised child playtesting and actual full-screen mode on
  a desktop compositor remain unverified.
- Known v0.3.0 polish limits: a faint material line where the authored
  Reedwater bank meets procedural ground, low-poly river/water shading without
  flow animation, no map pan/zoom or manual markers, helpers do not gather wild
  food, and summoning has no cooldown or resource cost.

## Safety and scope

Offline only. No accounts, ads, telemetry, purchases, loot boxes, graphic
violence or spider death. This is a deliberately fictional cooperative spider
species, not a biology simulation.

## Licence

Original project code and assets: see `ATTRIBUTION.md`. The supplied starter
assets are original handoff assets and are used with attribution there.
