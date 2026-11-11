# 20 — Expanded low-poly GLB asset pack

## What is provided
**66 original, importable binary glTF (`.glb`) static models**, including all 15 previous baseline names.

- **Characters & insects:** 11 models
- **Flowers, leaves & plants:** 21 models
- **Rocks, wood & traversal:** 18 models
- **Nursery, signals & supplies:** 16 models

Every original GLB was regenerated with consistent +Y up and near-ground pivots; material colours are embedded as vertex colours. The pack provides intentionally low-poly, stylised **starter** geometry, not skeletons, animations or final collision.

## Previews
- `assets/previews/creatures_contact_sheet.png`
- `assets/previews/flora_contact_sheet.png`
- `assets/previews/terrain_contact_sheet.png`
- `assets/previews/colony_contact_sheet.png`

These contact sheets are automatically rendered from the actual meshes; they are **not Godot screenshots**.

## Direct Godot import paths
Use `godot_starter/art/models/<group>/<model>.glb` after copying the starter tree into a new repository. The machine-readable `assets/model_manifest.json` maps every model to exact `res://` paths and includes counts, approximate bounds and uses.

## Rebuild models
```bash
python -m pip install numpy trimesh Pillow
python assets/source/generate_expanded_models.py
```
Run from the extracted handoff root. It overwrites the expanded models, manifests and contact sheets. The legacy `assets/source/generate_assets.py` is retained for original 2D concepts; if both are run, **run the expanded generator last** to keep the 3D version consistent. Regenerated assets must be recopied into `godot_starter/art/models` before shipping.

## Runtime notes
- Spider and worker meshes are static; Godot scene wrappers implement actual movement, animation and physics.
- `fly.glb` is the default functional prey visual. Other insects are optional visuals/future variants, not mandatory extra systems.
- `web_anchor_ring.glb`, `twig_anchor.glb` and other props are visual reference only; Godot `Marker3D` anchor locations must be authored in wrappers.
- Capture webs and silk bridges are **procedurally built in the game**. No static GLB replaces those systems.
- Terrain meshes need simplified collisions, not complex convex per-vertex collisions for movers.
- Preserve UK English and bright, readable visuals for ages 7–12.

## Verification scope
The pack is verified as readable non-empty GLB meshes with finite vertices; the copied Godot import paths are checked. The packaging environment lacks a Godot executable, so Godot-specific import and runtime checks remain a mandatory P0 gate for the coding agent.
