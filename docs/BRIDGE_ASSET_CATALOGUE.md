# Modular bridge asset catalogue

All 11 models are **original**, low-poly, static, Y-up, metre-scale, vertex-coloured GLBs. They are reusable visual pieces; no collision is included in scene wrappers.

| Asset | Approx size (X×Y×Z m) | Use |
|---|---|---|
| `bark_plank_a.glb` | 0.26 × 0.12 × 0.67 | transverse bark plank A; 0.22 m pitch, local Z spans bridge width |
| `bark_plank_b.glb` | 0.23 × 0.11 × 0.77 | transverse bark plank B with chipped end |
| `bark_plank_c.glb` | 0.28 × 0.1 × 0.69 | transverse bark plank C with subtle moss |
| `silk_cinch_knot.glb` | 0.36 × 0.18 × 0.2 | small coiled silk knot for ends and rail ties |
| `silk_double_hitch.glb` | 0.18 × 0.18 × 0.15 | decorative crossed silk hitch |
| `wooden_eyelet.glb` | 0.15 × 0.31 × 0.12 | short wooden attachment peg with silk loop |
| `forked_rail_fixture.glb` | 0.22 × 0.39 × 0.12 | forked twig fixture for rail lines |
| `silk_wrapped_support.glb` | 0.2 × 0.75 × 0.16 | side rail support, origin at base, +Y up |
| `silk_cross_lashing.glb` | 0.28 × 0.14 × 0.23 | woven crossing tie on plank or rope |
| `braided_silk_segment.glb` | 1.01 × 0.05 × 0.05 | 1 m reference silk braid; use procedural curves for arbitrary spans |
| `woven_silk_panel.glb` | 0.23 × 0.03 × 0.6 | airy 0.23 x 0.59 m woven silk segment to layer under planks |

## How many are actually required?

The 3 planks, silk-wrapped support, woven panel and knot/lashing models should all be used in the final assembled visual. The two fixtures and braided reference segment are optional accents, but available for editor and runtime use. Do not force all parts into each bridge or create a cluttered model.

## What remains procedural?

The bridge spline, rope rails, vertical suspender spacing, transforms, visual tint/ghost state and the existing walkable collision. A fixed-length GLB cannot replace that logic.

## Existing assets to reuse

Environmental `web_anchor_post.glb`, `web_anchor_ring.glb`, `pond_anchor_post.glb` and `exposed_root_anchor.glb` from previous handoff packs already exist in the repository. They remain location-specific anchors; this pack provides bridge deck details.
