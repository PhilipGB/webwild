# Game design specification — first playable slice

## Elevator pitch
Webwild is a joyful third-person miniature garden adventure where a player spider spins webs to catch flying insects, builds silk transport links, and raises a small cooperative spider colony. Every web should have a tangible function: **capture, connection, or shelter**.

## Audience / design pillars
1. **A web is a useful physical object.** Anchor points and web positions visibly matter; web placement creates readable trade-offs.
2. **The spider is fun to control.** Walk, jump, climb assisted surfaces, explore and interact without menus dominating the experience.
3. **A colony is alive without micromanagement.** Other spiders handle routine tasks; seeing them use the player's network is the reward.
4. **One action, optional deeper optimisation.** A seven-year-old can complete the story; a twelve-year-old can pursue efficiency and mastery.
5. **Child-safe, offline and recoverable.** No random irreparable loss, timer pressure, graphic violence, or hidden economy traps.

## Core loop
Explore a short range → choose useful anchor points → invest silk in capture web / bridge → insects enter catchable paths → gain food → nourish/hatch spiders → delegate collection/repair → expand routes and survive a wind event → improve efficiency.

## In scope (the entire first playable)
- One hand-authored approx. **38 × 28 world-unit** garden clearing bounded by friendly foliage; three micro-biomes in the clearing (clover, flower route, leaf nursery).
- Controllable spider with smooth walk/jump, one straightforward assisted climb surface, collision-aware angled camera, cursor selection and zoom.
- Two web builds: **orb-style capture web** and **walkable silk bridge** between tagged anchors; preview, valid/invalid feedback, refund-safe cancellation/dismantling and repair.
- Deterministic insects on visually readable spline/waypoint paths. Captures are collision/event-driven from web capture volumes, not simply periodic unconnected currency generation.
- Food and silk counters, costs, food-to-silk replenishment and rescue mechanism for zero-resource state.
- Friendly leaf nest / nursery; hatch to **five total spiders** including the player. Other spiders automatically navigate, collect trapped insects, and repair accessible webs.
- One foretold wind event with specific damage and recovery. No fights and no spider death.
- Three sequential missions with clear optional mini-challenges; victory and continue-playing mode.
- Pause/settings, autosave/manual save, load/restart, initial controls/help and accessibility toggles.

## Explicitly out of scope
Five region campaign; ant/wasp/bird combat; freeform drawing of every strand; realistic spider wall physics; complex species genetics; online co-op; craft trees; full spider customisation; achievements/cloud; voice; advanced worker specialisations; day/night and seasonal calendars; elaborate web vibration mechanics. These remain later possibilities.

## Session / win condition
15–30 minutes to first completion for a new player. Three missions: Web Builder, Tiny Team, Storm-Smart. On victory, leave the clearing playable and keep optional mastery goals available. No game-over screen.

## Success metric
Would a seven-year-old successfully build a web without an adult explaining a menu? Would a twelve-year-old intentionally alter web placement because of insect paths and wind exposure? If either is 'no', adjust onboarding and placement value before adding more content.
