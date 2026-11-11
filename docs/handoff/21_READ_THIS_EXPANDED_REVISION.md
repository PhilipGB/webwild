# Webwild — Expanded 3D asset and Godot scaffold revision

**This is the latest and authoritative handoff.** Begin with `01_AGENT_MASTER_PROMPT.md` or `STARTING_PROMPT.txt` and give the agent the entire extracted folder.

## What is new
- 11 creatures assets (Characters & insects)
- 21 flora assets (Flowers, leaves & plants)
- 18 terrain assets (Rocks, wood & traversal)
- 16 colony assets (Nursery, signals & supplies)

- Every GLB is an actual low-poly static 3D mesh, with consistent +Y up, -Z forward for creatures and Y=0 ground pivots.
- `godot_starter/` — a copy-ready Godot project tree with assets categorised at their final `res://` paths and a **static** asset preview scene.
- `19_GODOT_FOLDER_STRUCTURE_AND_NAMING.md` — exact tree, names, Node layouts, collision and import conventions.
- `20_EXPANDED_3D_ASSET_GUIDE.md` — asset guide, generation and verification notes.
- `assets/model_manifest.json` and `.csv` — model metadata and paths.
- `assets/source/generate_expanded_models.py` — full procedural source for every model.
- `assets/previews/*_contact_sheet.png` — readable contact sheets based on actual meshes.

## Mandatory usage for coding agent
1. Run `gh auth status`; safely create a **new private `webwild` repository** using `gh` (see `12_GIT_GITHUB_WORKFLOW.md`). No GitHub repository has been created by this archive maker.
2. Copy the **contents** of `godot_starter/` into the new Git repository in P0, including the bundled assets.
3. Open in the Godot .NET editor, inspect imports and preview, create proper `Webwild.csproj`, C# scenes, Core project and tests.
4. Replace `AssetPreview` with actual gameplay scenes; build the game according to `03_IMPLEMENTATION_MILESTONES.md`, P0–P8.
5. Use authored Godot scene collision and sockets; glTF models are visuals only. Dynamic capture webs and bridges must be implemented as gameplay systems.
6. Do not claim runtime validation until a Godot runtime and the full gameplay flow have actually been tested.

**Important:** The visual preview is not the game; the handoff still targets a complete first playable version. The asset pack is original source geometry, not a third-party licensed collection.
