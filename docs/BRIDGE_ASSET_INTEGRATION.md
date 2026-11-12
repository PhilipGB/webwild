# Integration contract — modular bridge visuals

## Coordinate convention

Everything uses metres in Godot Y-up coordinates. For the plank models:

- **+X:** along bridge travel direction, plank pitch dimension **0.19–0.24 m**.
- **+Z:** across the bridge width, plank spans **0.66–0.73 m**.
- **+Y:** up; origin at plank centre near top of deck.

Support posts and fixtures have origin at their base and extend along +Y. Knot/lashing origin is its decorative centre. Braided silk reference segment lies along local X, from -0.5 to +0.5 m. Woven panel is approx. 0.23 x 0.59 m in the XZ plane.

### Rendering

Assets are **vertex-coloured GLB 2.0** files with no PNG textures or third-party fonts. Reuse `res://art/materials/vertex_color_material.tres` (`vertex_color_use_as_albedo = true`). Imported scenes typically have `Visual/world/<mesh part>` children; the wrappers set `material_override` per mesh. If the installed Godot importer changes this hierarchy, inspect the imported scene and adjust the wrapper; don't leave broken node paths.

For exported Linux builds, preserve Webwild's existing `ArtMaterials.ApplyToModelInstances` compatibility pass for paths under `res://art/models/`, or verify an equivalent solution. Colours must appear in the rendered export, not only in editor.

### Runtime assembly

Recommended algorithm:

1. Given endpoints A/B and current bridge state, compute the same sagging path as the existing collision deck (prefer a shared function, but don't alter physics without tests).
2. Approximate arc length via samples; place three alternating plank variants at ~0.20–0.24 m spacing by arc length, rotated to tangent. Keep local Z across bridge, upright normal near world +Y.
3. Draw flexible silk rails in two heights at lateral offsets ~±0.34–0.40 m using generated tubular/prism geometry, with sparse vertical tie supports and small knot/fixture assets.
4. Place woven_silk_panel as an optional breathable underlay (not a solid opaque surface). Run each mesh through the correct vertex-colour material, with enough contrast against the pond.
5. Reuse existing environmental anchor posts and authored `BridgeGroundPoint` ramp endpoints; don't replace gameplay anchor instances.
6. Keep scene wrappers **visual-only**. All physical walking remains in `BuildBridgeCollision()`; avoid adding prop collision shapes or collision layers.
7. Handle preview (ghost/invalid) separately. Avoid instantiated collision for preview; reflect damage visuals without mutating bridge durability.
8. Cache resource references, use bounded object counts / MultiMesh where helpful; avoid per-frame rebuilds or allocations.

**Existing implementation to replace visually:** `WebMeshFactory.BuildBridge()` currently makes a yellow ribbon, and `WebVisual.AddWalkwaySegment()` adds a yellow visible BoxMesh over each collision segment. Remove/suppress **both** opaque visual sources, but preserve walking shapes and ramp calculation. `BuildCapture()` stays unchanged.

### Important: sample GLB is NOT the solution

`examples/bridge_4m_reference_only.glb` shows an original example assembled from the actual pieces. This is static reference only; it cannot span arbitrary anchors or adapt sag/collision. The final runtime bridge must be assembled from component GLBs plus dynamically generated silk curves.

### New files to load

- `res://art/models/bridge/<part>.glb`
- `res://scenes/building/bridge/parts/<Part>.tscn`
- `res://scenes/building/bridge/BridgeAssetGallery.tscn`

The existing `WebVisual` remains responsible for integration with simulation-owned `WebState` and collision semantics.

### Performance / visual criteria

- 3 plank variants and supports reused; no new unique mesh generated per plank.
- No opaque yellow bridge ribbon/box remains visible in gameplay.
- Clear small-spider scale, plank gaps bridged with silk, visually plausible route.
- Correct bridge orientation and bank ramps in either anchor order, Y elevation and Z spans.
- Glassy translucency limited to fine silk threads; fully invisible bridge is not acceptable for children.
