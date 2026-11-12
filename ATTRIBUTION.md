# Asset provenance and attribution

## Original Webwild starter assets

The low-poly `.glb` models, `.svg` icons and UI boards in this repository come
from the Webwild handoff asset pack. They were procedurally generated for this
project from original geometry and are used and modified here as project assets.
No third-party models, textures, music or sound effects are bundled.

- 92 original low-poly GLB models under `art/models/` (creatures, flora, terrain, colony, the 15-model Reedwater Bank pack and the 11-model modular bridge pack)
- Original SVG HUD icons under `art/ui/icons/`
- Original static Reedwater shoreline, shore-shelf and pond-bed meshes under `art/meshes/`
- Reference-only concept art, maps and UI mock-ups under `assets/reference/`;
  these are **not** runtime assets and are never presented as game screenshots.

`assets/reference/model_manifest.json` records the original garden model set.
`asset_manifest.json`, `docs/ASSET_CATALOGUE.md` and
`docs/model_geometry.json` record the supplied Reedwater Bank pack (15 GLBs,
94 component meshes, 9,513 triangles, vertex colours and scene wrappers).
`docs/bridge_asset_manifest.json`, `docs/BRIDGE_ASSET_CATALOGUE.md` and
`SHA256SUMS_BRIDGE.txt` record the 11-model modular bridge pack (original
low-poly GLBs, vertex colours, authored wrappers and an editor gallery). The
static `examples/bridge_4m_reference_only.glb` is reference art only and is never
loaded as gameplay geometry. See `LICENSE_BRIDGE_ASSETS.md`.

## Code and generated content

All C#, Godot scenes, materials, procedural web meshes, authored shoreline
meshes, UI and original synthesised sound effects were created for Webwild
during this build. No
downloaded textures, fonts, music or third-party game assets are used.

## Fonts

The game uses Godot's built-in default font. No font files are redistributed.

## Licence

Project code and original assets are provided for use and modification with the
Webwild project. The handoff pack does not carry trademark clearances for any
external material; none was added.
