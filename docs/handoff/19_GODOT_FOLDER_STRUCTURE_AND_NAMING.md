# 19 — Godot-ready project structure and naming conventions

## Purpose and authority
Use these conventions for Webwild P0–P8 so the generated game remains maintainable. The Godot-ready source tree is included under `godot_starter/`; its static asset preview is **not gameplay**. The coding agent should copy that tree into the new Git repository at P0, then implement the actual game.

## Folder tree (actual provided scaffold)

```text
webwild/                          # New GitHub repository root; contents copied from godot_starter/
├── project.godot                  # Godot entry point; agent updates to actual main menu
├── .gitignore
├── Webwild.csproj                 # CREATE in P0, Godot .NET target
├── Webwild.sln                    # CREATE in P0 if needed by toolchain
├── art/
│   ├── models/
│   │   ├── creatures/             # player, workers, insects
│   │   ├── flora/                 # flowers, leaves, bushes, vegetation
│   │   ├── terrain/               # logs, rocks, roots, twigs
│   │   └── colony/                # nursery, food, silk, markers
│   ├── ui/icons/                  # *.svg
│   ├── materials/                 # *.tres (create as needed)
│   └── audio/{music,sfx}/         # original/licensed audio only
├── scenes/
│   ├── boot/                      # Boot, MainMenu, pause/startup scenes
│   ├── world/                     # GardenClearing and level nodes
│   ├── actors/{spider,insects}/   # reusable character wrappers
│   ├── building/webs/             # CaptureWeb, SilkBridge, AnchorSocket
│   ├── colony/                    # Nursery, worker stations
│   ├── props/                     # static world prefabs
│   └── ui/                        # reusable HUD & panels
├── scripts/
│   ├── actors/                    # Godot Node C# adapters
│   ├── systems/                   # runtime Godot integration
│   └── ui/                        # UI behaviour
├── src/Core/                      # deterministic engine-free C# rules
├── tests/Webwild.Core.Tests/      # xUnit or NUnit; no Godot reference
├── data/                          # tuning.json, missions.json, scenarios.json
├── docs/                          # documentation; .gdignore
└── tools/                         # build & automation scripts
```

This tree is intentionally practical rather than over-segmented. Godot permits flexible organisation, but keeping scene-owned assets nearby is recommended; shared original GLBs live in `art/models/` because multiple scenes reference them.

## Naming rules

| Item | Convention | Example |
|---|---|---|
| Original models | `snake_case.glb` | `spider_player.glb` |
| Godot scene wrapper | `PascalCase.tscn` | `SpiderPlayer.tscn` |
| C# scene script | Matching `PascalCase.cs` | `SpiderPlayer.cs` |
| Reusable C# class/interface | `PascalCase`/`IName` | `WebNetwork`, `IWebEconomy` |
| Godot scene node | `PascalCase` | `AnchorSocketA` |
| Godot node path | Stable, explicit children | `Visual/PlayerMesh` |
| Scene instance name | `PascalCase` with meaningful suffix | `AnchorSocketFlowerA` |
| Godot signal/event | PascalCase C# event | `WebBuilt` |
| Gameplay identifier | `snake_case` stable key | `clover_flower_anchor_a` |
| Resource / texture | `snake_case.tres/.png/.svg` | `silk_material.tres` |
| C# enum/type | PascalCase | `WebType.Capture` |
| JSON keys | `camelCase` | `foodStored`, `schemaVersion` |
| JSON IDs | stable lowercase snake_case | `first_capture_web` |
| Test class | `FeatureTests.cs` | `WebEconomyTests.cs` |
| Git branch | `pN-kebab-case` | `p2-web-building` |

Avoid renaming model files after scene integration; Godot `res://` references must stay stable. Avoid folders with whitespace or case-only differences (important on Linux and Windows).

## Exact runtime node wrappers

### Character — `scenes/actors/spider/SpiderPlayer.tscn`
```text
SpiderPlayer (CharacterBody3D)  script: scripts/actors/SpiderPlayer.cs
├── CollisionShape3D            capsule/box, manually authored
├── Visual (Node3D)
│   └── PlayerMesh (instanced res://art/models/creatures/spider_player.glb)
├── GroundRay (RayCast3D)
├── InteractRay (RayCast3D)
└── CameraPivot (Node3D)
    └── Camera3D
```
The GLB is a **visual subtree**, not the authoritative physics/body root. Animate limbs or bob in the wrapper; static mesh is not rigged.

### Web anchor — `scenes/building/webs/AnchorSocket.tscn`
```text
AnchorSocket (Marker3D) script: scripts/systems/AnchorSocket.cs
└── SelectionIndicator (MeshInstance3D or Sprite3D, initially hidden)
```
Author socket positions in Godot scenes, with stable IDs and types. **Do not guess valid web attachment points from geometry pivots.**

### Static prop — `scenes/props/TwigAnchor.tscn`
```text
TwigAnchor (StaticBody3D)
├── CollisionShape3D (manually authored simplified shape)
├── Visual (instanced res://art/models/terrain/twig_anchor.glb)
└── AnchorSocket (instanced AnchorSocket.tscn)
```

### Capture web — `scenes/building/webs/CaptureWeb.tscn`
Generated lines/mesh at runtime; authoritative state in C# `WebNetwork` and `WebEconomy`, world view reflects that state. The visual fibres do not own prey capture logic.

## Coordinates and size
- glTF: native metres, **+Y up**, X/Z ground plane.
- Creature front: **-Z**, centred on X/Z with mesh feet touching Y=0.
- Socket `Marker3D` positions authored in wrappers; no world anchors encoded in these GLB files.
- Scene root: `(0, 0, 0)` and scale `(1, 1, 1)` when possible; adjust `Visual` child scale instead of collision body.
- Use simple capsule/box collision rather than per-triangle collision for movers.
- Default gameplay camera: elevated, readable 3D; UI should not depend on image resolution.

## Asset import & scene use
1. Copy `godot_starter/` contents into the new repository.
2. Open in Godot **.NET** editor, run import; check orientation and material colours. All GLBs are actual binary glTF files, but this archive has **not been Godot-runtime tested**.
3. Instantiate GLB under reusable wrapper `*.tscn` (inherited scenes where suitable), add collision to wrappers and unique gameplay IDs to script nodes.
4. Build webs/bridges dynamically in runtime from validated anchor graph; **do not fake web building by showing only pre-placed mesh assets**.
5. Commit original `.glb`, `.tscn`, `.tres`, C#, JSON and referenced icon files; ignore `.godot/`, bin/obj and build outputs.
6. Confirm exported desktop build loads imported assets with `ResourceLoader`/`GD.Load` rather than `FileAccess` on imported file paths.

## Source-of-truth hierarchy
1. `11_ACCEPTANCE_CRITERIA.md` — observable game behaviour.
2. `05_GAMEPLAY_SYSTEMS.md`, `04_TECHNICAL_ARCHITECTURE.md` — mechanics/rules.
3. This document — folder/prefab/import conventions.
4. `assets/model_manifest.json` — model locations and metadata.
5. Concept illustrations — appearance only, not runtime evidence.

## P0 folder/asset acceptance
- [ ] Godot .NET editor opens copied project without import errors.
- [ ] AssetPreview renders actual GLBs with visible coloured meshes.
- [ ] `Webwild.csproj` and pure .NET Core test project exist, build and run.
- [ ] Main game entry point replaces static AssetPreview before P1 completion.
- [ ] At least one `SpiderPlayer.tscn` wrapper exists with collision, Godot C# script and included GLB.
- [ ] Exact CLI/test commands and any deviations are recorded in PR.
