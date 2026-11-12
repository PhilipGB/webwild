# OpenCode P1 handoff — integrate Reedwater assets

The supplied pack contains **finished new assets**. Do not regenerate them, unless you deliberately need to revise a specific model. Add files into the existing Webwild repository preserving paths.

1. Import `art/models/reedwater/*.glb` and `art/materials/reedwater_vertex_color_material.tres`.
2. Import `scenes/world/reedwater/props/*.tscn` and `scenes/world/reedwater/ReedwaterAssetGallery.tscn`.
3. Confirm all 15 files import in the Godot editor without missing resources or GLB warnings.
4. Use authored prop scene instances to assemble the Reedwater Bank pond/islands/reed beds. Make pond terrain/water in authored `.tscn` scenes, not runtime C# geometry.
5. Keep the existing `WorldLayout` + `SceneContentValidator` approach. Add new region IDs, safe arrival points and authored bridge-bank markers. Use new `pond_anchor_post`/`exposed_root_anchor` model instances at the authored anchor sites.
6. Add `dragonfly.glb` to a new insect route only if you choose to implement corresponding insect behaviour and tests. Do not imply the static model is already animated.
7. The models use `COLOR_0`; prop scenes include material overrides. For exported builds run a rendered probe to verify colour and retain the existing `ArtMaterials.ApplyToModelInstances` compatibility pass when needed.
8. Add region acceptance assertions for water collision, both bridge direction orders, player and worker traversal, save/load and return travel. Validate all previously existing acceptance checks.

**Treat the asset pack as content, not executable game logic.** Do not remove or alter original Webwild art. Test all new assets in editor and Linux export before claiming the region is playable.
