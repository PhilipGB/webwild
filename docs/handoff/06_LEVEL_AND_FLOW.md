# Garden level layout, authored beats, fifteen-minute introduction

See `assets/maps/clearing_plan.svg`. Coordinate system is illustrative; geometry must be authored and verified in Godot rather than parsed out of map pixels.

## Clearing landmarks
1. **Starter Leaf / Spawn** (south-west): title-to-world camera reveal, safe home location, recovery prompt.
2. **Twin Twigs** (west-mid): two obviously highlighted anchors with a guaranteed fly route through the expected web plane.
3. **Flower Lane** (north): long plant stems and predictable insect loop; rewards placement strategy.
4. **Pebble Bridge Gap** (centre): small shallow dip and paired anchor points; silk bridge shortcut visibly used by workers.
5. **Leaf Nursery** (east): shelter leaf, eggs, hatching interaction and job hints.
6. **Tall Stem** (south centre): a safe assisted climbing segment with a scenic overlook.
7. **Wind Edge** (north-east): exposed but profitable anchors contrasted with sheltered alternatives beside a large log.
8. **Boundary foliage**: tall flowers/shrubs/colliders keep player in area gently, not invisible arbitrary walls.

## Opening beats
- 0–3 min: spawn and follow 1-line `WASD to move`, `Space to jump`, `E to interact` prompts. Walk to a marker; reach climb stem optional.
- 3–6 min: show two highlighted twigs → Build Capture Web via button or `1`, click both sockets → spin mesh and animation → a fly crosses and is trapped → player E to collect or watch helper demonstration.
- 6–10 min: move to pebble dip, build a silk bridge (button or `2`) → nursery interaction → hatch an AI spider (cost shown explicitly) → its successful bridge traversal / first pickup makes colony feel alive.
- 10–15 min: telegraph wind → one exposed web loses durability → show repair vs shelter choice → collect again → celebration screen / continue playing.

## Child-facing prompts (final-copy examples)
- "Use WASD to explore the garden."
- "Choose the two glowing twigs."
- "A fly is coming. Watch your web!"
- "Press E to collect food."
- "Your spider friend can help."
- "Wind is coming. Protect your web."
- "Nice work! Your colony is growing."

## Environment construction
Use the supplied `assets/models` as base props; hand-place winding path, layered dirt patches, clover, mushrooms, fern clusters, flowers, 2–3 decorative stones, leaf nest and log. Ground is spatially readable: no identical-looking wide empty greens. Create clear sightlines from spawn to first anchors and nest, with landmark colours. Avoid dense vegetation blocking mouse clicks or the camera.

## Mission path testing
The entire path must be tested from a **new save** without editor tools. If the intended web is poorly placed, the game must let the player modify placement rather than simply waiting. Hints escalate in three steps: general hint → highlight relevant object → optional demonstration; never autopass the objective.
