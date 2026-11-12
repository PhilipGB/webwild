# Playtest notes

## Child playtest — NOT VERIFIED

No supervised session with children aged 7–12 was performed in this build
environment. The acceptance criterion “a first-time seven-year-old can follow
one-line hints and complete the first web” is therefore **NOT VERIFIED**, not
PASS. Hints, costs, large buttons and the first-web flow were reviewed by an
adult developer only.

To run a real session:

1. Start the game and choose **New Garden**.
2. Do not use developer tools or the runtime probe.
3. Observe whether the child can build the Twin Twigs capture web from the
   on-screen hint and the `1` toolbar button.
4. Record the child’s age band, time to first web, where they looked for help,
   and any hint that needed adult explanation.
5. Repeat with the nursery, bridge and wind beats.

## Developer visual inspection

Performed on the rendered game at 1280×720 using Xvfb, including the exported
Linux build. Real Godot screenshots are in `artifacts/screenshots/`. The capture
covers title, garden, pause, help, colony, build mode, finished web, trapped
insect, modular bridge close-ups, damaged bridge, nursery, wind warning, victory,
settings, reloaded save, title with Continue, Reedwater travel and completion,
and UI scales 100%, 125% and 150% with the notification lane active. The v0.2.1
shoreline pass adds regraded start/far shore, island-edge and low-angle views,
plus a labeled before/after comparison; all were inspected at pixel level.

## Known usability limits found during inspection

- Reedwater's water-over-grass defect is resolved: the pond uses an authored
  shoreline/shelf/bed and no longer paints translucent rectangles across grass.
- Toasts stay in the right-hand lane below the mission card. At 150% UI scale
  they cover some world-space labels but no permanent control, the player, the
  mission card, status hint or toolbar; runtime rectangle checks enforce this.
- The settings panel scrolls on smaller logical viewports; the **Done** button is
  pinned outside the scroll area.
- A Windows machine startup was not available; see `docs/EXPORTS.md`.
