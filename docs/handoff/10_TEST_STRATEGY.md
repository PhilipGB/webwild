# Test strategy — fail first, deterministic, run inside Godot

## 1. Pure engine-independent tests
Use xUnit/NUnit and pure C# models to test at least:
- Anchor validation: duplicate/self/too far/too steep/capacity/full, wrong web type, disabled anchor, invalid IDs.
- Cost preview = atomic spend; cancel no spend; failed build no spend; dismantle refund bounded and idempotent.
- Capture tests: crossing path catches, miss does not catch, crossing after destroyed web does not catch, multiple frames of one insect yield exactly one reward.
- Resource ledger: no negatives, exactly-once ledger events, recovery not exploitable to generate arbitrary silk.
- Worker graph: connected path via bridge, removal invalidates path, unreachable task released/reassigned, no worker duplication, capacity <=5.
- Wind: exposed vs sheltered damage; repair consumes correct silk; recovered web resumes capture; no unwinnable state.
- Missions: sequential lock, event idempotency, cannot skip, late worker event after completion doesn't re-reward.
- Saves: version schema, valid roundtrip, data validation, atomic temp-file rename, backup recovery, stale/unknown IDs, no success notification on failed write.

## 2. Godot runtime smoke tests
Build a deterministic Godot scene test runner (headless, fixed seed, controlled mock clock if possible) and prove:
- Main scene starts, title button enters level, player scene and HUD load without script errors.
- Anchor area selection works through actual Godot input/raycast layers; `CaptureWeb.tscn` is visible and area/mesh aligned.
- An insect traversing its intended route triggers one real capture and resource update.
- A worker physically moves across the bridge to a reserved target.
- Wind modifies mesh/indicator, repair restores capture functionality.
- Save, exit, restart, load recovers progress and generated runtime entities.
- Pause prevents simulation input/advancement; modal input not sent to world.

## 3. Real manual playthrough
From a fresh game, without the editor: complete M01→M03, deliberately try invalid anchors, build bridge, hatch, wait for worker retrieval, observe weather and repair, save/reload, complete victory. Check UI at 1280×720 and 1920×1080; verify mouse targets and camera obstruction. Screenshot **actual gameplay**, not a concept image.

## Commands (adapt actual Godot .NET binary)
```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build --logger 'console;verbosity=normal'
"$GODOT_DOTNET_BIN" --headless --path . --editor --quit
"$GODOT_DOTNET_BIN" --headless --path . --quit-after 120
```
`--quit-after 120` means **frames**, not 120 seconds. Use a dedicated runtime runner/smoke scene to assert game state; passive startup alone does not test gameplay.

## CI recommended gates
- Main and PR: format/compile + pure tests + headless editor import + Godot smoke. Fail on errors and count assertion tests.
- P8 release: Linux export, Windows export, readme and file manifest, screenshots; do not claim Windows runtime test on a Linux host unless actually performed (Windows VM/Wine test described with evidence if used).

## Evidence format
For every phase log SHA and command, tool versions, total/pass/fail/skip count, screenshot paths, known exceptions, source branch and PR URL. `NOT RUN` never equals `PASS`. Preserve logs under `docs/evidence/` selectively; don't commit enormous ephemeral build outputs.
