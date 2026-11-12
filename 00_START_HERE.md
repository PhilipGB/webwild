# Webwild v0.2.0 P2 — Modular Bridge & Release Handoff

This is a **drop-in handoff bundle** for the existing private `PhilipGB/webwild` Godot 4.7.2 / .NET 10 repository.

It includes 11 original modular bridge GLBs, authored prop `.tscn` wrappers, a Godot editor asset gallery, a static 4 m reference assembly (preview **only**), pictures, the OpenCode master prompt, installation notes and reproducible source scripts.

## Step 1 — Extract into the existing Webwild repository

From your Webwild repository root:

```bash
unzip -n /path/to/Webwild_v0.2.0_P2_Bridge_Handoff.zip -d .
```

`-n` prevents replacing existing files; all supplied model and scene paths are new. Review `unzip -l` first if unsure. Do **not** create a new repository.

## Step 2 — Give OpenCode the prompt

From the same repository root:

```bash
opencode --prompt "$(cat 01_OPENCODE_P2_MASTER_PROMPT.md)"
```

Select the **Build** agent in OpenCode if required. If the installed OpenCode version does not support this form, launch `opencode` and paste the full contents of `01_OPENCODE_P2_MASTER_PROMPT.md`.

## Included

- `art/models/bridge/*.glb` — 11 unique low-poly model assets, metre scale, Godot Y-up
- `scenes/building/bridge/parts/*.tscn` — material-overridden authored wrappers; **visual-only**, no collision
- `scenes/building/bridge/BridgeAssetGallery.tscn` — test composition in Godot editor
- `previews/bridge_asset_contact_sheet.png` — preview of assets and assembled sample
- `examples/bridge_4m_reference_only.glb` — **example**, not a production/runtime replacement for procedural bridge
- `docs/BRIDGE_ASSET_INTEGRATION.md` — runtime bridge design and integration contract
- `docs/BRIDGE_ASSET_CATALOGUE.md` — model inventory and assembly guidance
- `docs/BRIDGE_P2_ACCEPTANCE.md` — tests, release conditions and risks
- `docs/bridge_asset_manifest.json` and `SHA256SUMS_BRIDGE.txt` — verification
- `tools/generate_bridge_assets.py` — optional original-source regeneration script (Python)
- `LICENSE_BRIDGE_ASSETS.md` — provenance / usage

## Important

The current game bridge uses a flat opaque ribbon plus solid yellow walkway meshes. The new design keeps the **existing collision and travel mechanics unchanged**, but replaces the **visible** bridge geometry with authored plank instances, support details and generated curved silk threads.

This bundle is not a finished v0.2.0 release or working feature branch. A coding agent must integrate, validate, test and release it. Static GLB and `.tscn` reference checks have been performed; **Godot editor/runtime import and gameplay acceptance have not** been performed in this package.
