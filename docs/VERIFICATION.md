# Verification status

## Performed

- Re-loaded all 15 Reedwater GLB assets using `trimesh` (glTF 2.0 binary reader).
- Verified 15 GLB headers, finite geometry bounds, and vertex colour `COLOR_0`
  for every mesh.
- Verified 94 named component meshes totalling 9,513 triangles.
- Verified 15 `.tscn` prop scene wrappers and one asset gallery scene exist.
- Verified every local `res://` reference in the supplied Godot scenes resolves in the pack.
- Verified each imported model's component nodes have explicit vertex-colour material overrides in its authored `.tscn` wrapper.
- Generated orthographic previews from the actual GLB mesh data (not illustrative stock imagery).
- Re-ran `sha256sum -c SHA256SUMS_BRIDGE.txt`: all 48 supplied bridge-pack files
  match.

## Integration performed in Webwild v0.2.0 P1

- Godot 4.7.2 .NET editor import completed without missing-resource warnings.
- All fifteen Reedwater GLBs and their prop wrappers load in the authored region.
- Rendered under Xvfb at 1280×720: 168/168 runtime acceptance checks at the P1
  baseline, including the dragonfly routes and Reedwater objectives.
- `--scene-validate`: 47/47 checks at the P1 baseline, including Reedwater model
  and material coverage.
- The region uses authored polygons, collision and water volumes; no runtime
  scenery generator was added.

## Integration performed in Webwild v0.2.0 P2

- Imported all eleven supplied modular bridge GLBs and their eleven `.tscn`
  wrappers plus the editor gallery without broken node paths or missing
  resources. `--scene-validate` now includes 51/51 checks.
- `BridgeVisualAssembler` loads the wrappers at runtime, places three alternating
  plank variants, woven underlays, supports/eyelets/forks/knots/lashings and
  generated silk rails. Runtime signatures confirm all three variants, details,
  panels, rails and zero unstyled meshes.
- The exported Linux build drops nested imported `material_override`
  properties; the runtime vertex-colour pass restores them. `*-vertex-materials`
  checks report zero unstyled bridge meshes in the exported build, and rendered
  exported screenshots show the wood/silk colours.
- Scene validation caught this export-only material difference during the first
  P2 export run. The validator now checks real meshes and the runtime
  material pass, rather than assuming exported nested overrides survive.
- Bridge geometry is verified for west→east, east→west, north→south (Z-axis)
  and diagonal spans; player traversal, worker route invalidation and bridge
  removal are rechecked at runtime.
- The schema-1 → 2 → 3 save migration is tested with real v0.1.x fixtures and
  real P1 schema-2 saves. A 602-transaction history-eviction test proves the
  Reedwater supply cache cannot be re-granted.
- Toast lane rectangles are checked at 1280×720 with 100%, 125% and 150% UI
  scale against the mission card, action column, status hint and toolbar.
- Linux export passed 51/51 scene checks and 236/236 runtime checks headlessly
  and rendered under Xvfb. The published Linux archive was re-downloaded,
  checksum-matched, extracted and rerun.
- Windows x64 export was produced and verified as PE32+.

## Integration performed in Webwild v0.2.1

- Replaced the 24 × 20 m translucent pond plane with a static authored
  shoreline surface, mossy shelf and pond bed. Scene validation computes actual
  mesh AABBs and proves the water is below the bank tops, stays within the bank
  span (with a small hidden underlap) and is surrounded by the shelf.
- Refitted the four water hazard volumes to the new shoreline. Scene validation
  proves every hazard AABB lies inside the authored water outline.
- Runtime physics queries prove both banks and both islands are safe, three
  open-water points are hazardous, direct water entry recovers to the safe bank,
  bridge traversal still works and no-bridge crossing is rejected.
- Inspected real rendered screenshots from source and exported Linux: arrival,
  elevated overview, start shore, far shore, island edges, low angle and bridges.
  No turquoise overlays, rectangular water edges across grass, shoreline gaps or
  sorting artefacts were observed.
- `dotnet test`: 106/106; scene contract 55/55; headless, rendered and exported
  Linux runtime probes 246/246.

## Integration performed in the v0.2.2 hardening pass (PR 1)

- Bridge collision is now one triangle-strip surface per bridge (1.5 m wide)
  instead of segmented 0.9 m boxes. Physics walking checks start on bank or
  island ground and cross all three Reedwater bridges in both directions, stop
  at joints, turn on the deck, cross after reload, and still respect water.
- Two decorative Reedwater posts that intersected the bridge centreline were
  moved off the walkable deck.
- New Garden confirmation, durable `.previous` archive and title-screen restore
  were exercised with pure save tests and runtime probe checks.
- `dotnet test`: 115/115; scene contract 55/55; headless and rendered Linux
  runtime probes 264/264.

## Not performed

- Windows runtime: no Windows host was available. The v0.2.1 Windows export was
  rebuilt as PE32+ but not launched.
- A supervised child playtest of the new region and bridges was not performed.
- Actual desktop full-screen presentation on a real compositor was not visually
  tested.
