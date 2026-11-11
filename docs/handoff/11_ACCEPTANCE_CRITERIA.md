# Acceptance criteria — release gate for Webwild v0.1.0

Every checkbox must be observed or tested, not presumed from source. Report NOT VERIFIED honestly.

## Experience and gameplay
- [ ] Launches from a title menu, enters a real low-poly 3D garden level, no debug/editor interaction required.
- [ ] Player walks with WASD/arrows, jumps with Space, climbs one designated stem and uses mouse + UI reliably.
- [ ] Player can select highlighted anchor sockets and visibly preview, build, cancel and dismantle capture webs and silk bridges.
- [ ] Invalid anchor pair / low silk / occupied edge cannot charge silk and explains why.
- [ ] Flying insects visually travel the routes, collide/intersect with a capture web, get trapped, then grant food exactly once on collection.
- [ ] Placement matters: a web in the marked flight lane catches; a deliberately out-of-lane web does not automatically generate food.
- [ ] Silk/food balance works and no low-resource combination can permanently softlock the tutorial.
- [ ] Nursery can hatch to five total spiders, with visible worker animations and at least one worker successfully crossing a silk bridge.
- [ ] Workers collect trapped prey and repair accessible webs without per-action manual commands; unreachable tasks are released.
- [ ] Wind is telegraphed; exposed web damaged; player can repair or adapt and resume capture.
- [ ] All M01/M02/M03 objectives can be completed in a fresh human playthrough; victory allows continued play; optional mastery is not compulsory.
- [ ] New Game, Continue, Pause/Resume, Help, Settings and Quit to Menu work without loss of controls or state.

## Usability and quality
- [ ] A first-time seven-year-old can follow one-line hints and complete first web with no text-heavy tutorial.
- [ ] Food/Silk, current objective, current selection, cost and feedback are legible at 1280×720.
- [ ] Input is consistent: click select, right click cancel, E interact, Esc pause/cancel, Tab colony; no accidental background clicks on modals.
- [ ] Readable contrast, UI scale, reduced-motion and volume settings function and persist.
- [ ] Sound and visuals communicate capture, damage, build and success even with audio muted.
- [ ] Garden contains cohesive authored detail, no developer primitives left as prominent placeholder art, no camera clipping that blocks main quest.

## Engineering / delivery
- [ ] Code uses Godot 4.7.2 .NET + C# / net10.0 (or documented tested necessity for deviation) and launches from a clean checkout.
- [ ] Pure C# deterministic tests pass, including validation, capture, economy, worker graph, missions, wind, recovery and saves.
- [ ] Godot headless import + C# scene startup + dedicated runtime acceptance smoke all pass.
- [ ] Save JSON is versioned, validates input, writes atomically with backup, recovers corrupted main, and never misreports failures.
- [ ] Repository created privately via `gh` and contains full source, assets/attribution, instructions, tests and reproducible evidence.
- [ ] P0–P8 are recorded with commit/PR links, green checks, no unresolved critical issues on main.
- [ ] Linux x64 playable export is produced and launched/tested; Windows x64 export is produced and its runtime verification status explicitly stated.
- [ ] Release `v0.1.0` tagged only after acceptance and release checks; screenshots show **actual Godot game**.

## Sign-off
Final report table per criterion: PASS / FAIL / NOT VERIFIED, evidence link/path, exact commit SHA. If a noncritical known limitation remains, quantify user impact and do not label a failing critical item as done.
