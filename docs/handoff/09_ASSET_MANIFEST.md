# Asset manifest and usage instructions

## Image / diagram references
- `assets/concept/hero_garden.png` — original generated **mood-art reference**; wide image for palette, sunlight, web/nursery relationships.
- `assets/concept/handoff_board.png` — original generated handoff board. Its small labels are visual reference only; ignore any numerical or version discrepancy versus written files.
- `assets/concept/overview_board.svg` — author-controlled SVG brief with exact first-playable scope.
- `assets/maps/clearing_plan.svg` — top-down world guide with labelled playable areas, anchors and routes.
- `assets/ui/hud_mockup.svg` — intended in-game HUD layout; `web_building_mockup.svg`, `nursery_mockup.svg`, `pause_mockup.svg` complement it.
- `assets/diagrams/architecture.svg`, `web_types.svg`, `spider_silhouette.svg` — technical/visual guides.

## Actual starter 3D model files (Godot imports `.glb`)
| File | Suggested purpose | Notes |
|---|---|---|
| `models/spider_player.glb` | Player avatar | Static low-poly starter, teal body, 8 legs, eyes; agent adds animation/state |
| `models/spider_worker_orange.glb` | Worker variant | Same proportions, orange accent |
| `models/spider_worker_violet.glb` | Worker variant | Purple accent |
| `models/fly.glb` | Common insect | Wings are stylised; add flap motion |
| `models/leaf_nursery.glb` | Nursery visual | Leaf cradle and pale eggs |
| `models/flower_purple.glb` | Landmark flowers | Reusable with rotation/scale variation |
| `models/clover_patch.glb` | Ground cover | Instance many |
| `models/mushroom.glb` | Foliage | Small accents |
| `models/pebble.glb` | Stone obstacle | Cluster variants |
| `models/fallen_log.glb` | Shelter / climb landmark | Collision generated in Godot |

**Meshes are intentionally low-poly and not guaranteed to have Godot-ready collision rigs.** Import, inspect scale/orientation, make CharacterBody3D/StaticBody3D collision shapes, and animate in Godot. Keep mesh sources in `assets/source/` if adjusting.

## Icons
`food.svg`, `silk.svg`, `web.svg`, `bridge.svg`, `nest.svg`, `spider.svg`, `wind.svg`, `repair.svg`, `quest.svg`, `settings.svg`. SVGs are unbranded original art suitable for UI and engine import.

## Licensing
Original procedural meshes and diagrams generated for this handoff, and generated reference art. See `LICENSE_ASSETS.md`. Third-party assets used by the agent must include precise creator/license/URL attribution in `ATTRIBUTION.md`; forbid non-permissive unknown sourced assets. For reference art, do not claim it is an in-engine screenshot.


## Revised starter 3D asset pack
The revised handoff expands the usable asset set and adds machine-readable manifests:
- `assets/model_manifest.json` — intended purpose, vertex/face counts, approximate bounds
- `assets/model_manifest.csv` — spreadsheet-friendly version
- `assets/models/README.md` — Godot import guidance
- `assets/previews/starter_models_contact_sheet.png` — quick visual overview

### Additional GLB assets in the revised archive
| File | Suggested purpose | Notes |
|---|---|---|
| `models/twig_anchor.glb` | Anchor point / scenery | Wrap as `StaticBody3D`; useful for web anchor markers |
| `models/leaf_platform.glb` | Path node / broad platform | Good for routes and perches |
| `models/grass_tuft.glb` | Ground dressing | Instance frequently |
| `models/egg_cluster.glb` | Standalone egg prop | Use in nest dressing or tutorial reward scenes |
| `models/dew_drop.glb` | Decorative or collectable prop | Optional resource pickup visual |

### Recommended first-playable usage
Use the supplied models as the **baseline slice art set** rather than only as inspiration. The coding agent should still create dynamic webs, silk bridges, collisions, behaviours, navigation, UI logic, and any needed additional simple geometry directly in Godot/C#.


## Expanded revision (authoritative for 3D)
The latest archive contains a complete categorised pack, replacing the old 15-model table above as a final inventory. See **`20_EXPANDED_3D_ASSET_GUIDE.md`** and machine-readable **`assets/model_manifest.json`** for the exact model count and filenames. Use `godot_starter/art/models/<group>/` directly for the game. The previous lists document early design history only.
