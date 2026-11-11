# Webwild

**A child-friendly spider colony adventure in one garden clearing.**

Webwild is an original, offline 3D game built with **Godot 4.7.2 .NET**, **C#** and
**.NET 10 (`net10.0`)**. You play a friendly garden spider: spin capture webs to
catch flying insects, build silk bridges, hatch helper spiders, weather a gust of
wind and complete three short story goals.

This repository is the complete first-playable vertical slice (P0–P8). The
deterministic game rules live in `src/Core` with no Godot dependency; Godot scenes
and scripts adapt those rules to 3D presentation, physics and UI.

## Requirements

- Godot **4.7.2 .NET** editor/executable (the Mono/.NET build, not the standard build)
- .NET SDK **10.0** and matching Godot .NET export templates (Linux and Windows x64)
- Linux x64 development host (tested), Windows x64 export target

## Build and test

```bash
dotnet restore
dotnet build
dotnet test
```

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
godot --headless --path . -- --runtime-probe  # deterministic runtime assertions
```

See `docs/EVIDENCE.md`, `ACCEPTANCE.md` and `PROGRESS.md` for reproducible evidence.

## How to play

- **WASD / arrow keys** move, **Space** jumps, mouse wheel zooms.
- **1** capture web, **2** silk bridge. Click two glowing anchors; right-click or
  **Esc** cancels without spending silk.
- **E** interacts (collect a trapped insect, use the nursery, repair a web).
- **Tab** colony overview, **Esc** pause.
- Walk into the tall stem's glowing climb volume and push forward to climb.

Your goal: build a capture web, collect food, build a bridge, hatch a helper,
protect the colony through a wind gust, then collect once more.

## Repository layout

- `src/Core/` — deterministic rules, simulation, validation, missions and saves
- `scripts/` and `scenes/` — Godot presentation, input, physics and UI
- `art/models/` — original low-poly GLB starter assets (see `ATTRIBUTION.md`)
- `tests/Webwild.Core.Tests/` — pure deterministic xUnit suite
- `data/` — tuning, missions and scenario definitions
- `docs/` — handoff specification, evidence, playtest and release notes

## Safety and scope

Offline only. No accounts, ads, telemetry, purchases, loot boxes, graphic
violence or spider death. This is a deliberately fictional cooperative spider
species, not a biology simulation.

## Licence

Original project code and assets: see `ATTRIBUTION.md`. The supplied starter
assets are original handoff assets and are used with attribution there.
