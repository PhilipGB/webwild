# Visual direction — art bible for first playable

## Art intent
Stylised, hand-crafted, low-poly miniature garden. A garden leaf looks like a balcony, a log like a tunnel, a pebble like a hill. The spider is characterful without looking like a plush toy pasted into a realistic photo. **Friendly and adventurous**, not scary, slimy or realistic.

## Shapes and materials
- Simple faceted geometry, slight roundness on focal characters, colour blocks rather than heavy photo textures.
- Flowers with 5–7 readable petals, large clover, bent stems, smooth pebble clusters, curled leaves and exaggerated droplets.
- Most surfaces matte with restrained highlights; silk is bright translucent ivory/blue, more visible than real spider thread; webs show subtle sway and glints.
- Spider eyes: large but not all eight equally huge; 2 expressive main eyes and small secondary spots. Eight separate legs with purposeful alternating motion. Main player teal/violet; workers orange, violet, sage, amber.
- Daylight with warm green shadows, readable silhouettes. Do not make the whole scene uniformly neon-bright; dark soil and bark improve contrast.

## Palette
| Use | Colour | Hex |
|---|---|---|
| Leaf / grass base | Clover | `#78BF66` |
| Light leaf | Fresh Leaf | `#A3D981` |
| Flower violet | Petal Purple | `#AD7DE2` |
| Flower yellow | Sun Gold | `#F6C34B` |
| Spider primary | Silk Teal | `#41B8B2` |
| Secondary / twilight | Violet | `#795E9C` |
| Soil | Warm Earth | `#986947` |
| Web | Dew White | `#DBF6F3` |
| HUD background | Cream | `#F7F1DE` |
| Dark ink | Pine | `#263F39` |
| Warning | Coral | `#E66F62` |
| Sky backdrop | Day Sky | `#93D7EC` |

## Layout rules
Create a non-repeating skyline of giant flowers and leafy arches; place web anchors with small gold highlights as a visual gameplay language; slightly exaggerate their size for clickability. At distance, the player silhouette and intended landmark should remain separate from same-coloured background objects.

## Camera and readability
Elevated third-person 3D at approximately 35–45° apparent downward view, adjustable zoom, no obligatory twitch aiming. Do not let foliage block the web-building interaction; use temporary transparency/fading or collision avoidance. Build ghost should be readable against bright vegetation, with dark rim or subtle shadow.

## Concept vs deliverable
- `assets/concept/hero_garden.png`: reference mood painting, NOT evidence of game quality nor directly usable 3D level assets.
- `assets/concept/handoff_board.png`: layout inspiration, may contain illustrative details; do not treat every tiny word as authoritative.
- `assets/models/*.glb`: actual original **starting meshes**; may be improved/replaced but not silently left unused.
- `assets/icons/*.svg`: actual scalable original icons; Godot can import rasterized or converted versions as needed.
- `assets/ui/*.svg`: design mock-ups, to recreate with `Control` nodes.

## Animation / FX
Minimal budget: idle breathing, alternating leg cycles, a short bounce for successful jump, little web-making circular motion, fly wing flutter, trapped wing tremble, worker head turn, gentle gust, success sparkle. Use a colour plus animation signal for catching/repairing; limit screen shake and flashes.

## Sound guidance
If audio is created, use simple synthesised/original SFX with recorded provenance. Do not download or include uncleared game soundtracks. A mute user must see every important state change.
