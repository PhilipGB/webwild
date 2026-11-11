# Implementation plan — P0 to P8

## Global operating rules
- Every milestone ends with a committed, testable, runnable state. Stop regressions before moving on. One feature branch and PR per P1–P8; P0 bootstraps and creates the `gh` repository.
- Each PR description: scope, gameplay demonstration, test commands/results (exact counts), screenshot/video evidence where applicable, intentional deviations, known issues.
- Merge only green PRs to keep a stable main. No fake stubs used to tick acceptance criteria. Prefer complete thin vertical slices to extensive disconnected systems.

| Phase | Outcome | Concrete acceptance gate |
|---|---|---|
| **P0 — Toolchain + repository** | Clean Godot 4.7.2 .NET project, net10.0 solution, original design docs, basic CI/test scaffolding; create private `webwild` via `gh`. | `gh repo view` verifies private, `dotnet build` green, headless Godot launch displays empty-but-intentional garden/title scene; README commands accurate. |
| **P1 — Spider + garden** | Attractive small clearing, core camera, controllable spider, jump, assisted climb, interaction highlights, game HUD shell. | From title menu, can walk/jump/climb to 3 landmarks and return without getting stuck; mouse and keyboard tested; runtime asserts 3 destinations. |
| **P2 — Web infrastructure** | Typed anchor sockets, capture-web and bridge previews, validation, costs, construction, dismantle, visible mesh, simple path graph. | Build 2 web types between legal anchors, reject illegal/crossed anchors with clear feedback, bridge changes reachable worker graph, cancellation never charges. |
| **P3 — Prey and economy** | Insect flight paths, intersection-based captures, retrieval, food/silk economy, resource display, softlock-proof fallback. | Capture volume only catches passing insects; exactly one reward per catch, move a web to improve catch rate, deterministic tests and in-scene smoke pass. |
| **P4 — Nursery and workers** | Nursery, hatch (max 5 total), animated autonomous workers, route selection, retrieval and repair priorities. | Hatch a worker, observe it cross bridge, retrieve and repair; no unreachable-task deadlock; core worker tests pass. |
| **P5 — Tutorial + UI + objectives** | 3 linear mission beats, optional mastery, child-friendly hints and action menus, victory and replay. | Fresh save can complete tutorial without dev input, events award once, no dead ends on menu or objective state transitions. |
| **P6 — Wind event + polish** | Forecast warning, deterministic exposed-web damage, repair/shelter strategy, animations and feedback, accessible settings. | Wind interrupts an exposed web; player can recover; reduced motion and difficulty toggles function; no punitive softlock. |
| **P7 — Persistence + hardening** | Versioned JSON save with validation, atomic write/backup, menu resume, autosave, error notifications, tests. | Restart resumes correctly; corrupted main recovers from backup; invalid data rejected gracefully; never false 'Saved' success. |
| **P8 — End-to-end acceptance + release** | Full runtime playthrough, release notes, CI evidence, Linux and Windows export attempts, tagged delivery. | All acceptance criteria explicitly checked; repeatable build/test/export; no missing assets; real play screenshots and final report; `v0.1.0` only if release gates green. |

## Required engineering sequence within every phase
1. State the acceptance case in a test or Godot runtime probe that fails on current HEAD.
2. Implement the narrow system using pure rules + Godot adapters.
3. Exercise interactive flow and inspect screenshot/actual rendered output for visual phases.
4. Run focused and full suites; fix failures.
5. Commit, push, PR, record evidence, merge only when green.

## Recommended branch names
`p1-garden-movement`, `p2-web-building`, `p3-insect-economy`, `p4-colony-workers`, `p5-missions-ui`, `p6-wind-polish`, `p7-save-recovery`, `p8-release`.

## Agent autonomy rules
If a detail is ambiguous, choose the smallest complete solution preserving player-facing behaviour and document it. Do not add later features instead of fixing current failures. When repository credentials/export toolchains are genuinely unavailable, keep implementing and present a truthful blocker plus locally verified evidence.
