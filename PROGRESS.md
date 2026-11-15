# Progress — P0 to P9 / v0.3.0

Status legend: **DONE** = implemented and evidenced; **PARTIAL** = implemented
with a listed limitation; **NOT VERIFIED** = not performed on this host.

The runtime vertical slice was implemented in one integrated pass, then split
into eight phase commits, branches and PRs with phase-specific deterministic
tests and evidence. All eight PRs had green CI before merge. This is recorded
honestly rather than fabricating eight independent feature implementations.

Merged PRs: [#1](https://github.com/PhilipGB/webwild/pull/1),
[#2](https://github.com/PhilipGB/webwild/pull/2),
[#3](https://github.com/PhilipGB/webwild/pull/3),
[#4](https://github.com/PhilipGB/webwild/pull/4),
[#5](https://github.com/PhilipGB/webwild/pull/5),
[#6](https://github.com/PhilipGB/webwild/pull/6),
[#7](https://github.com/PhilipGB/webwild/pull/7),
[#8](https://github.com/PhilipGB/webwild/pull/8) and
[#9](https://github.com/PhilipGB/webwild/pull/9) (v0.1.1 hardening) and
[#10](https://github.com/PhilipGB/webwild/pull/10) (v0.1.2 review hardening) and
[#11](https://github.com/PhilipGB/webwild/pull/11) (v0.2.0 P0 scene-first world),
[#12](https://github.com/PhilipGB/webwild/pull/12) (v0.2.0 P1 Reedwater Bank) and
[#13](https://github.com/PhilipGB/webwild/pull/13) (v0.2.0 P2 bridge polish and release),
[#16](https://github.com/PhilipGB/webwild/pull/16) (v0.2.1 Reedwater shoreline),
[#18](https://github.com/PhilipGB/webwild/pull/18), [#19](https://github.com/PhilipGB/webwild/pull/19)
and [#20](https://github.com/PhilipGB/webwild/pull/20) (v0.2.2 hardening).

| Phase | Scope | Status | Evidence |
|---|---|---|---|
| P0 | Toolchain, private repository, C# solution, pure test scaffold, Godot import | DONE | `docs/EVIDENCE.md#p0`; `00d4f58` |
| P1 | Garden clearing, player spider, camera, jump, assisted climb, HUD shell, title flow | DONE | `docs/EVIDENCE.md#p1`; runtime checks `movement-wasd`, `jump-space`, `climb-stem`; screenshots 01–02 |
| P2 | Anchors, capture web and bridge previews, validation, build/cancel/dismantle | DONE | `docs/EVIDENCE.md#p2`; `PlacementTests`; runtime checks `invalid-pair-rejected`, `low-silk-rejected`, `occupied-edge-rejected`, `cancel-no-charge`, `dismantle-refunds` |
| P3 | Insect routes, intersection capture, food/silk economy, recovery | DONE | `docs/EVIDENCE.md#p3`; `CaptureAndEconomyTests`; runtime checks `insect-trapped`, `food-collected` |
| P4 | Nursery, hatched workers, bridge routes, retrieval and repair | DONE | `docs/EVIDENCE.md#p4`; `WorkerAndMissionTests`; runtime checks `worker-hatched`, `m02-complete`, `worker-crossed-bridge` |
| P5 | Three missions, mastery challenge, hints, victory and replay | DONE | `docs/EVIDENCE.md#p5`; `MissionFlowTests`; runtime checks `m01-complete`, `m03-complete`, `victory`, `continue-loads` |
| P6 | Wind forecast/damage/repair, settings, accessibility, polish | DONE | `docs/EVIDENCE.md#p6`; `WindTests`; checks `wind-completed`, `web-restored`, `ui-scale-125/150`, `low-threat-applies` |
| P7 | Versioned save, validation, atomic writes, backup recovery | DONE | `docs/EVIDENCE.md#p7`; `SaveTests`; checks `save-succeeds`, `save-loads`, `reload-*`, `quit-to-menu` |
| P8 | Runtime playthrough, screenshots, exports, acceptance, release tag | DONE (Windows runtime NOT VERIFIED) | `docs/EVIDENCE.md#p8`; `docs/EXPORTS.md`; 80/80 rendered probe; 19 real screenshots |
| v0.1.1 | Independent-review hardening: bounded save history, safe failed-save quit, real ravine/bridge traversal, silk food cost, difficulty reset, pause physics, settings retry | DONE | `CHANGELOG.md`; `HardeningTests`; runtime checks `pause-freezes-airborne`, `ravine-blocks-without-bridge`, `bridge-enables-player-crossing`, `failed-save-keeps-session`, `settings-save-error-visible`, `low-threat-restores` |
| v0.1.2 | Review hardening: order-independent bridge ramps, order-preserving save histories, settings temp-file cleanup | DONE | `CHANGELOG.md`; `BridgeGeometryTests`, `HistoryOrderTests`, settings failure tests; runtime checks `bridge-reverse-*`, `bridge-zaxis-*`, `bridge-removal-*` |
| v0.2.0 P0 | Scene-first world: reusable environment, terrain, landmark, flora and boundary scenes; anchor/marker coordinate contract; procedural builder removed | DONE (no release yet) | PR [#11](https://github.com/PhilipGB/webwild/pull/11); `CHANGELOG.md`; `docs/SCENE_AUTHORING.md`; `SceneAuthoringTests`; 24 scene checks; runtime `scene-*`; screenshots 01–19 |
| v0.2.0 P1 | Reedwater Bank second region: pond, islands, reeds, water collision, travel, three regional objectives, dragonflies, worker crossing, schema-v2 migration | DONE (no release yet) | PR [#12](https://github.com/PhilipGB/webwild/pull/12); `docs/EVIDENCE.md#v020-p1`; `RegionTests`, `SaveMigrationTests`, `SceneAuthoringTests`; 47 scene checks; 168 runtime checks; screenshots 20–29 |
| v0.2.0 P2 | Modular bridge art, durable supply-cache claim, non-obstructing toast lane, release polish | RELEASED as v0.2.0 (Windows runtime NOT VERIFIED) | PR [#13](https://github.com/PhilipGB/webwild/pull/13); `docs/EVIDENCE.md#v020-p2`; `BridgeLayoutTests`, `RegionTests`, P1 save fixtures; 51 scene checks; 236 runtime checks; screenshots 09b–09c, 16a, 23b |
| v0.2.1 | Reedwater shoreline: authored water/shelf/bed meshes, refitted water hazards, shoreline screenshots and checks | DONE, merged PR [#16](https://github.com/PhilipGB/webwild/pull/16) (not released; Windows runtime NOT VERIFIED) | `docs/EVIDENCE.md#v021`; 55 scene checks; 246 runtime checks; screenshots 22d–22g |
| v0.2.2 | Gameplay reliability and usability hardening: continuous bridge collision, safe New Garden, clear anchor/build decisions, live nursery, arrival-based mastery, guidance/pacing/worker feedback | **RELEASED**; merged PRs [#18](https://github.com/PhilipGB/webwild/pull/18), [#19](https://github.com/PhilipGB/webwild/pull/19), [#20](https://github.com/PhilipGB/webwild/pull/20), [#22](https://github.com/PhilipGB/webwild/pull/22), [#23](https://github.com/PhilipGB/webwild/pull/23), [#24](https://github.com/PhilipGB/webwild/pull/24), [#25](https://github.com/PhilipGB/webwild/pull/25) and [#26](https://github.com/PhilipGB/webwild/pull/26) (Windows runtime NOT VERIFIED) | `docs/prs/v0.2.2.md`; 125 unit tests; 55 scene checks; 307 headless / 308 rendered runtime checks; `docs/EXPORTS.md` |
| v0.3.0 P0 | Never-ending world foundations: signed 64-bit chunk coordinates, origin rebasing, deterministic terrain sampler, chunk contracts, landmark origins, world-manifest schema 4, per-chunk change store, legacy-save migration, ADR | DONE (no new terrain yet, by design) | `docs/prs/v0.3.0.md`; `docs/adr/0001-never-ending-world.md`; 163 unit tests (125 existing + 38 new); 55 scene checks; 307 headless / 308 rendered runtime checks |
| v0.3.0 P1 | Procedural chunk streaming: deterministic chunk generator and streamer, flat-shaded vertex-coloured chunk terrain with collision, GLB prop dressing, persistent build queue, hedge gates on all four sides, raycast shadow, exploration save bounds | DONE, merged PR [#28](https://github.com/PhilipGB/webwild/pull/28) | `docs/prs/v0.3.0.md`; `WorldStreamingTests`; 180 unit tests (150 existing + 30 new); 55 scene checks; 327 headless / 328 rendered runtime checks; Linux export re-verified |
| v0.3.0 P2 | Seamless world and manifest saves: Reedwater embedded at (96, 0), walking replaces travel, physical bramble gate, dual-landmark streaming/blending, one sky/sun, schema-4 `WorldSaveService`, legacy migration on load, durable manifest archive, per-save world seeds | DONE, merged PR [#29](https://github.com/PhilipGB/webwild/pull/29) | `docs/prs/v0.3.0.md`; `SeamlessWorldTests`; 189 unit tests (180 existing + 9 new); 57 scene checks; 339 headless / 340 rendered runtime checks; Linux export re-verified |
| v0.3.0 P3 | Biomes, landmark aprons and origin rebasing: deterministic three-biome field with walkable height offsets, per-quad biome colour/hash, biome-specific props, generated-style Reedwater aprons, 16 km/64 m presentation rebasing with stable logical saves, wilderness HUD, biome probe tooling | DONE, merged PR [#30](https://github.com/PhilipGB/webwild/pull/30) | `docs/prs/v0.3.0.md`; `BiomeTests`; 195 unit tests (189 existing + 6 new); 57 scene checks; 353 headless / 354 rendered runtime checks; Linux export re-verified; screenshots 33–35 |
| v0.3.0 P4 | Rivers and crossings: deterministic meandering rivers, carved terrain-following channels, per-chunk water quads and wet-bed palette, stable low bridges every 150 m, river hazard recovery and guidance, safe-bank loading for pre-river saves, chunk-local prop placement fix, river probe tooling | DONE, merged PR [#31](https://github.com/PhilipGB/webwild/pull/31) | `docs/prs/v0.3.0.md`; `RiverTests`; 203 unit tests (195 existing + 8 new); 57 scene checks; 370 headless / 371 rendered runtime checks; Linux export re-verified; screenshots 36–37 |
| v0.3.0 P5 | Exploration map and Return Home: bounded exploration record saved once in the manifest, biome/river map modal, M toolbar and key, Return Home from anywhere, landmark visit filtering fix, fresh-save map honesty | DONE, merged PR [#32](https://github.com/PhilipGB/webwild/pull/32) | `docs/prs/v0.3.0.md`; `ExplorationTests`; 210 unit tests (203 existing + 7 new); 57 scene checks; 389 headless / 390 rendered runtime checks; Linux export re-verified; screenshots 38–39 |
| v0.3.0 P6 | Wild food and regeneration: deterministic berries/mushrooms/seeds, tick-based once-per-generation harvest, depleted-only per-chunk records with seed guards, food views/prompts, New-Garden chunk archiving, probe evidence | DONE, merged PR [#33](https://github.com/PhilipGB/webwild/pull/33) | `docs/prs/v0.3.0.md`; `FoodTests`; 219 unit tests (210 existing + 9 new); 57 scene checks; 404 headless / 405 rendered runtime checks; Linux export re-verified; screenshots 40–42 |
| v0.3.0 P7 | Physical worker navigation: hierarchical dry world routes through hedge gates and generated river bridges, no-teleport landmark journeys, recorded/reversed routes, blocked waiting and retries, mid-journey save/load, world-space helper rendering, probe instrumentation | DONE, merged PR [#34](https://github.com/PhilipGB/webwild/pull/34) | `docs/prs/v0.3.0.md`; `WorkerNavigationTests`; 227 unit tests (219 existing + 8 new); 57 scene checks; 420 headless / 421 rendered runtime checks; Linux export re-verified; screenshots 43–44 |
| v0.3.0 P8 | Off-screen helpers and summon: summon to the player's world point, explicit journey destinations, wild summoned arrivals waiting with empty graph nodes, off-screen view culling and snapping, broad saved wild bounds, summon events/probes | DONE, merged PR [#35](https://github.com/PhilipGB/webwild/pull/35) | `docs/prs/v0.3.0.md`; sum contracts; 231 unit tests (227 existing + 4 new); 57 scene checks; 432 headless / 433 rendered runtime checks; Linux export re-verified; screenshot 45 |
| v0.3.0 P9 | Integrated acceptance and release: version 0.3.0, README/CHANGELOG/ACCEPTANCE/EVIDENCE/EXPORTS rewrite, full integrated probe on the release tree, Linux and Windows exports, release record | DONE, merged PR [#36](https://github.com/PhilipGB/webwild/pull/36) | 231 unit tests; 57 scene checks; 432 headless / 433 rendered; Linux export 432/433; Windows export runtime NOT VERIFIED; 70 screenshots |
| v0.3.0 | **Never-Ending World release**: one continuous procedural world, biomes, rivers/crossings, exploration map, Return Home, wild food regeneration, physical/off-screen/summoned helpers, schema-4 saves | **RELEASED as v0.3.0** (merge `4d8faf3`, tag `v0.3.0`) | `docs/prs/v0.3.0.md`; `docs/EVIDENCE.md#v030`; `docs/EXPORTS.md`; Linux verified, Windows/child playtest NOT VERIFIED |

## Verification summary

- `dotnet test`: **227 passed, 0 failed** (219 existing + 8 new worker-navigation tests).
- Scene contract validation: **57 passed, 0 failed** across both regions.
- Headless runtime probe: **420 passed, 0 failed**.
- Rendered runtime probe (Xvfb, 1280×720): **421 passed, 0 failed**.
- Exported Linux build, headless probe: **420 passed, 0 failed**.
- Exported Linux build, rendered Xvfb probe: **421 passed, 0 failed**.
- Linux x64 export: built successfully and launched.
- Windows x64 export: built successfully; runtime **NOT VERIFIED** (no Windows host).
- Legacy saves v0.1.0/v0.1.1/v0.1.2: migration tests pass with real fixtures.
- Child playtest: **NOT VERIFIED** — see `docs/PLAYTEST_NOTES.md`.

## Known limitations

- No supervised child playtest was performed.
- Windows export has not been started on Windows.
- Actual full-screen mode on a desktop compositor was not visually tested;
  the setting persists and the window mode is applied through Godot.
- The game is a deliberately stylised 3D garden, not a photorealistic or
  biologically accurate simulation.
- Both regions are authored in `.tscn` scenes. Gameplay coordinates remain in
  `WorldLayout`/`Regions.cs` and are validated against scenes rather than
  auto-synchronised by an editor plugin.
- Both landmarks are always resident; GPU resources for both authored scenes
  stay loaded.
- A faint material line remains where the authored Reedwater bank meets
  procedural ground.
- River water is a stylised low-poly surface without flow animation or
  reflections.
- The map auto-fits explored ground and windows after 84 chunks; there is no
  free pan/zoom or manual marker placement.
- Helpers do not gather wild food; summoned helpers wait rather than following,
  and summoning has no cooldown or cost.
- The dragonfly is a static model used as an insect; there is no skeletal flight
  animation.
- The repository was restored to private visibility during the v0.1.1 hardening
  pass.

## v0.3.0 — Never-Ending World (release acceptance)

- P0 foundations through P9 integrated acceptance are merged and released as
  **v0.3.0** (merge `4d8faf3`, tag `v0.3.0`); see `docs/prs/v0.3.0.md`.
  Reedwater Bank is walked to through generated meadow; the travel dialog is
  gone and a physical bramble gate opens when the garden story completes.
  Saves are schema-4 world manifests with per-save seeds, legacy migration,
  a durable archive, per-chunk wild food state, and one exploration record.
  The procedural meadow has three biomes, origin rebasing, carved rivers
  with generated crossings, a map with Return Home, regenerating wild food,
  physical helpers with off-screen simulation and summoning, and the six
  original goals intact. Final counts: 231 unit tests, 57 scene checks,
  432 headless / 433 rendered runtime checks; Linux export verified, Windows
  runtime and child playtest NOT VERIFIED.
