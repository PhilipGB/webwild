# Webwild: Spider Colony Simulator — First Playable Handoff

**A self-contained implementation pack for a coding agent.** The archive provides design authority, a copy/paste master prompt, nine independently verifiable milestones, acceptance gates, testing and release guidance, a garden map, UI reference screens, icons, original reference art and a usable low-poly GLB starter asset pack.

**This ZIP is a handoff, not a completed game.** The coding agent creates the GitHub repository and builds the playable Godot project end to end.

## Expanded 3D asset pack + Godot project layout
This revision includes **66 actual coloured `.glb` models**, the import-ready source scaffold under `godot_starter/`, scene/node naming conventions, a machine-readable model manifest and four rendered contact sheets. See `21_READ_THIS_EXPANDED_REVISION.md`, `19_GODOT_FOLDER_STRUCTURE_AND_NAMING.md`, and `20_EXPANDED_3D_ASSET_GUIDE.md`.

**Godot validation caveat:** The mesh files were checked programmatically but could not be imported and played in the Godot editor in this packaging environment. The coding agent must do so at P0. The included preview scene is only a visual sample, not a game.

## Start here
1. Read [`00_START_HERE.md`](00_START_HERE.md).
2. Give the agent the whole extracted folder and paste [`01_AGENT_MASTER_PROMPT.md`](01_AGENT_MASTER_PROMPT.md) as its initial instruction.
3. The agent will verify its environment, create the private GitHub repository using `gh`, import/replace the supplied reference assets as appropriate, and complete P0–P8.
4. Inspect its final acceptance report and play the exported build.

## Non-negotiable result
- A **15–30-minute** self-contained, offline 3D adventure/colony-simulation slice for ages **7–12**.
- Directly control a friendly spider; walk, jump and use assisted climbing in a single garden clearing.
- Build physical capture webs and traversable silk bridges between pre-authored anchor points.
- Watch deterministic insects get caught and a small colony (up to **five spiders total**) collect food, maintain webs and use bridges.
- Food/silk, leaf nursery, hatching, a wind event, three linked goals, optional mastery challenge and validated persistent saves.
- Clear child-friendly UI, accessibility settings, audible/visual feedback, Linux desktop runnable, Windows desktop export verified to the extent the environment permits.
- Evidence-based completion: unit tests, Godot headless smoke tests, manual playtest, documented build and limitations.


## Revised asset pack
This revised archive includes a clearer **starter 3D asset pack** under `assets/models/`, plus `assets/model_manifest.json`, `assets/model_manifest.csv`, `assets/models/README.md`, and `assets/previews/starter_models_contact_sheet.png`. These assets are intended to be imported directly into Godot as the baseline world/character art for the first playable slice.

## Stack
- **Godot 4.7.2 .NET stable**, C# (not GDScript implementation), **.NET 10 SDK / `net10.0`**. A compatible fallback target requires documentation and approval only when unavoidable.
- Target Linux development and Linux + Windows x64 desktop builds. No browser build: Godot 4 C# web export is not supported.
- Original low-poly assets; default offline single player; no login, ads, IAP or network telemetry.

## Suggested extraction layout
Keep this handoff folder next to the future `webwild` code repository, not mixed with generated build outputs. Copy `AGENTS.md`, relevant templates and desired assets into the new repo as part of P0. Preserve a copy of the handoff under `docs/handoff/` or link a versioned manifest.

## Document map
| File | Purpose |
|---|---|
| `01_AGENT_MASTER_PROMPT.md` | Copy/paste start instruction and execution contract |
| `02_GAME_DESIGN.md` | Player fantasy, loop, age-layered decisions, slice rules |
| `03_IMPLEMENTATION_MILESTONES.md` | P0–P8 with gates and dependencies |
| `04_TECHNICAL_ARCHITECTURE.md` | Godot scenes, pure C# core and runtime integration |
| `05_GAMEPLAY_SYSTEMS.md` | Detailed web, insect, colony, economy and weather behaviours |
| `06_LEVEL_AND_FLOW.md` | Clearing layout, 15-minute tutorial, mission content |
| `07_UI_UX_ACCESSIBILITY.md` | Input, HUD, screen behaviour and accessibility |
| `08_ART_DIRECTION.md` | Original art direction and palette |
| `09_ASSET_MANIFEST.md` | Included usable/reference assets and build guidance |
| `10_TEST_STRATEGY.md` | Evidence and test harness design |
| `11_ACCEPTANCE_CRITERIA.md` | Exact conditions for release readiness |
| `12_GIT_GITHUB_WORKFLOW.md` | `gh` private repo, branch/PR/merge sequence |
| `13_SAVE_AND_DATA.md` | Versioned, atomic saves and JSON definitions |
| `14_RELEASE_READINESS.md` | Build, export, verification and final reporting |
| `15_SOURCE_NOTES.md` | Source links and versions checked |
| `16_RISKS_AND_NON_GOALS.md` | Scope and technical risks |
| `17_PLAYTEST.md` | Child usability test protocol |
| `assets/` | Generated concept art, starter 3D models, icons and mock-ups |
| `prompts/P*.md` | Recovery/task prompts for each milestone |
| `data/` | Worked initial parameters, missions, test cases |

**Source of truth:** written design and acceptance docs override illustration labels; specific `data/*.json` numbers are **initial tuning proposals**, not arbitrary unchangeable requirements.
