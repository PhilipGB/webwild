# Reedwater Bank — asset catalogue

All dimensions are metre-scale and taken from actual exported GLB geometry. **Y is up.** Where an asset sits on water, place the model pivot at the chosen pond surface; decorative tops may be just above zero. Island tops are approximately at Y=+0.12–0.15 while the soil and rock descend beneath the waterline.

| Model | Approximate width × height × depth (m) | Use | Collision |
|---|---:|---|---|
| `lily_pad_small.glb` | 1.28 × 0.20 × 1.15 | Small floating decoration; capture-web visual context | None |
| `lily_pad_large.glb` | 2.13 × 0.22 × 1.80 | Broad floating pad or pond waypoint | None |
| `lily_pad_cluster.glb` | 2.10 × 0.66 × 1.74 | Grouped pond foliage, irregular arrangement | None |
| `water_lily_open.glb` | 1.33 × 0.46 × 1.14 | Visible pink-and-cream bloom for pond landmarks | None |
| `water_lily_bud.glb` | 1.17 × 0.70 × 1.04 | Closed water lily for variety and progression cues | None |
| `cattail_single.glb` | 0.79 × 1.59 × 0.81 | Sparse marsh edge vertical landmark | None |
| `cattail_cluster.glb` | 1.25 × 1.86 × 1.13 | Dense pond-bank silhouette and visual framing | None |
| `marsh_grass_tuft.glb` | 1.40 × 0.65 × 1.57 | Ground-edge coverage, marsh islands, shallow banks | None |
| `moss_island_small.glb` | 2.87 × 1.09 × 2.27 | Single small bridge destination / landing island | Scene collider |
| `moss_island_medium.glb` | 3.99 × 1.12 × 3.58 | Mid-route island for multi-bridge route planning | Scene collider |
| `moss_island_large.glb` | 6.49 × 1.54 × 5.47 | Destination island / colony waypoint | Scene collider |
| `pond_anchor_post.glb` | 0.72 × 1.39 × 0.39 | Placed web/bridge anchor post on solid bank | Scene collider |
| `exposed_root_anchor.glb` | 1.51 × 1.75 × 1.29 | Organic anchor landmark and climbing silhouette | Scene collider |
| `dragonfly.glb` | 1.82 × 0.45 × 1.35 | Optional new insect species / harder flight-path challenge | None |
| `stepping_stone.glb` | 1.40 × 0.64 × 1.10 | Small landing point / optional navigation obstacle | Scene collider |

## Art direction

- Low-poly, flat-shaded faceted surfaces with muted garden greens, pastel flowers, amber wood and teal insects.
- Geometry contains actual authored silhouettes: notched lily pads, multi-layered petals, seed-bearing cattails, irregular earthen islands, carved bridge posts, branching roots, four-wing dragonfly.
- Colour is baked into `COLOR_0` attributes; no texture files or online materials are required.
- Keep terrain and water as separate authored scenes. These models do not create a pond or a traversal system by themselves.

## Collision and anchor usage

`moss_island_*` and `stepping_stone` scene wrappers include basic walkable `StaticBody3D` colliders, and `pond_anchor_post`/`exposed_root_anchor` also include solid props. These do not automatically register gameplay anchors: add matching `AnchorSocket.tscn` instances and `WorldLayout` definitions in the game and run `--scene-validate`.

For bridge landing points, position anchor posts entirely on solid ground/islands. Add explicit authored `BridgeGroundPoint` markers and keep both direction and elevation consistent with collision.

## Performance

These are modest-size, independent GLBs. Prefer instancing the same model repeatedly, rather than duplicating imported meshes. Group repeated decorations under the region's `Flora` scene. Dragonfly wing shading uses opaque pastel facets rather than costly transparent surfaces.
