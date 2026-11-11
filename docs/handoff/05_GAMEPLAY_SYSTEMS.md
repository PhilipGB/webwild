# Gameplay systems — implementation-level rules

## Player and camera
- Player root: `CharacterBody3D`, friendly stylised spider GLB, simple leg animation (procedural alternating pairs acceptable), collision body that fits body rather than leg span.
- WASD/arrows move relative to camera's ground-plane axes; Space jump with a small buffer/coyote allowance; E interact; left click select in building mode; right click/Esc cancel; mouse wheel zoom; Tab colony overview.
- Camera: slightly elevated 3D follower, zoom limits and obstacle avoidance; cursor remains usable for web point clicking. Do not require constant right-mouse camera dragging to move.
- **Assisted climb only**: designated sloped/vertical stem or log surfaces have explicit climb volumes and attached route positions. Press movement toward climb → smoothly ascend/descend, not full arbitrary inverted surface locomotion. Ground-to-climb transition reliable and safe.

## Anchors and validation
- Each anchor is a placed `Anchor3D` with stable string ID, surface type, world position, normal, group tags, max incident web count, placement highlight and inspector tooltip.
- A web is `{id, type, anchorA, anchorB, durability, exposure, constructedAt}`. Both anchors must exist, differ, be active, not already contain equivalent edge, be within type-specific distance, and have capacity. Reference `data/tuning.json`.
- Capture web: 3D mesh oriented along anchor-to-anchor plane and visually obvious. Choose a deterministic plane normal derived from anchor normals / local authored orientation. Centre capture area intersects insect motion only when actual swept path passes through it. Avoid frame tunnelling via segment-vs-area intersection or appropriately sized trigger.
- Bridge: strong silk line/mesh with a walkable track and worker route edge; prevent unrealistic ramps/gaps. Validate reachable endpoints and safe slope; clicking invalid anchors must not consume silk.
- The preview immediately shows type, cost, projected location, capture area / route outline, legal/illegal colour and brief reason. Cancel = no charge. Dismantle returns a documented partial amount (e.g. 60% silk), unless first tutorial web (100%) to avoid punishing children.

## Capture pipeline and insects
- Two insect classes to start: little fly (common) and moth (slower). Both fly deterministic authored loop routes with waypoints, simple animations and modest variation from a fixed RNG seed.
- The capture check is position-based: every movement segment (previous→next position) is tested against nearby capture web region (broad phase + segment crossing / Area3D event) and web must be active/durable. Do not treat web as a passive timer that generates food independent of where placed.
- State machine: `Flying → Trapped(webId) → Retrieved(workerId/player) → Rewarded → RespawnScheduled`. Record unique capture event ID; ledger grant once; a worker's action is cancellable when target removed. Display fly trapped in visible web until collected.
- A poorly placed web may miss the flight path; a web on the marked busy route should reliably catch within 30–60 seconds. Make route/path visible via moving insects and occasional light visual wisps, not giant marker rails.

## Resource economy
- **Food**: collected once for each rescued/caught insect. **Silk**: produced by rested/fed spiders at a bounded automatic rate, spent on structures, repairs and nursery.
- No negative balances, duplicate transaction IDs, or spending after failed validation. Match preview cost to final spend. Fixed tick avoids resource differences tied to FPS.
- Sample baseline in `data/tuning.json`: initial 5 food / 20 silk, capture web 6 silk, bridge 5 silk, nursery first hatch 4 food + 4 silk, repair 2 silk. Tune with observed real play sessions.
- Recoverability: never allow a state where zero silk AND no active food route permanently prevents progress. Provide a limited free starter-web repair/recovery action at the nursery, unlocked when necessary and clearly labelled; preserve mission validity.

## Colony: <=5 spiders incl player
- The player begins as founder. Up to four AI workers can hatch via nursery; workers use simple coloured variants and visible names, without biological claims.
- Autonomy priority: urgent safety + accessible repairs → retrieve trapped prey → maintenance patrol → idle/playful animation. If a task becomes unreachable, release reservation and find alternative; no permanent reservation leaks.
- Workers can move along graph-connected ground, assisted climb links and silk bridges. Visual interpolation follows route; reachability validated by pure graph tests.
- Jobs mostly automatic for the first playable. Optional small task priority selector for more advanced players (retrieve-first / repair-first), default automatic.

## Wind event
- Trigger only after nursery goal so player has resources and experience. Show clear forecast countdown/visual gusts (~20 seconds) before damaging a small number of exposed webs by predictable rules.
- Sheltered webs take reduced or no damage. Webs at zero durability stop capturing/requiring movement; repair UI states cause/cost and a highlighted route to action.
- Never destroy all starter means to obtain food. No spider death; no random permanent loss. A lost bridge must result in safe worker rerouting or graceful wait, not trapping the player forever.

## Missions and victory
- M01 First Threads: build valid capture web + collect an insect; a short hint reveals which anchors have passing flies.
- M02 Tiny Team: build a bridge and hatch a worker; witness the worker use the bridge or finish one collection task.
- M03 Storm-Smart: endure wind, restore a damaged web or construct a sheltered alternative, and collect one post-storm insect.
- Optional mastery: collect 3 insects with ≤2 capture webs before victory (or in free play); reward a decorative badge, no critical unlock.
- Progress is event-driven and exactly-once; entering a menu or reloading must not re-grant rewards.

## Audio and Web Sense
- Small original SFX: select/cancel, soft silk build, catch ping, worker chirp, gentle gust, success. The sound-off game remains clear through animation, HUD and alerts.
- Web Sense advanced signal taxonomy is out of scope; subtle ripple/pulse on catch is sufficient.
