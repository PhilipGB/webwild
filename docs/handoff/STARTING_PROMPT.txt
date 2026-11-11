# STARTING PROMPT — PASTE THIS INTO YOUR CODING AGENT

You are the autonomous lead C#/.NET + Godot game-development agent for **Webwild: Spider Colony Simulator**. Your task is to **create a NEW GitHub repository using the `gh` CLI, then design, implement, test, polish, and deliver a complete first playable game end to end** from the accompanying handoff folder. This is not a prototype-only request. Finish P0 through P8 in sequence and give reproducible evidence of the completed game.

## Handoff and authority
- Read `README.md`, `02_GAME_DESIGN.md`, `03_IMPLEMENTATION_MILESTONES.md`, `04_TECHNICAL_ARCHITECTURE.md`, `05_GAMEPLAY_SYSTEMS.md`, `06_LEVEL_AND_FLOW.md`, `07_UI_UX_ACCESSIBILITY.md`, `08_ART_DIRECTION.md`, `09_ASSET_MANIFEST.md`, `10_TEST_STRATEGY.md`, `11_ACCEPTANCE_CRITERIA.md`, `12_GIT_GITHUB_WORKFLOW.md`, `13_SAVE_AND_DATA.md`, `14_RELEASE_READINESS.md`, `16_RISKS_AND_NON_GOALS.md`, `17_PLAYTEST.md` and the accompanying `assets/` and `data/`.
- Treat written specs and acceptance criteria as binding; concept images are visual cues, NOT a pixel-perfect game specification. Where conflicts exist: acceptance > gameplay systems > architecture > art and illustrations.
- Work in UK English, with simple concise child-facing labels. Do not invent unfinished features and report them as implemented.

## Expanded assets and project scaffold (mandatory update)
- First read `21_READ_THIS_EXPANDED_REVISION.md`, `19_GODOT_FOLDER_STRUCTURE_AND_NAMING.md`, `20_EXPANDED_3D_ASSET_GUIDE.md` and `assets/model_manifest.json`.
- At P0 **copy contents** of `godot_starter/` to the root of the new GitHub repo so `res://art/models/<group>/` paths work immediately. Use the real `.glb` files, not screenshots or decorative proxies.
- Use `scenes/boot/AssetPreview.tscn` only for initial model import inspection; **replace it with a working boot/title/menu game**, not a static preview, before P1 can pass.
- Use the provided node wrapper naming/conventions; every GLB is a visual child. Add simple collisions and authored `Marker3D` anchors in Godot. No mesh contains collision, rigs, complete game scenes or sockets.
- Integrate at least the player spider, worker spider, fly, leaf nursery, flower, log, mushroom, clover, twig anchor, and rock models in the playable garden. The extra variants are optional decoration unless required by existing milestones.
- Preserve any concept art as reference only. Accept asset changes only with verified gameplay and visual checks.

## Target and constraints
- Use **Godot 4.7.2 .NET stable** + **C#** + **.NET 10 (`net10.0`)**. Use Godot native nodes for scenes/physics/rendering/input; place deterministic rules, economies, mission state, validation and save rules in pure C# code with no `Godot.*` dependency.
- Linux development workflow, Linux x64 game, Windows x64 export. Use Godot's **.NET** editor and matching export templates. Check actual installed versions first. Never quietly switch to GDScript or a web app.
- Build ONE attractive connected **garden clearing**; no sprawling procedural world. Polished movement, web-building, insect capture, 2 resources, up to 5 spiders, leaf nursery/hatching, worker autonomy, simple storm/wind threat, three story goals + an optional mastery challenge, settings and robust save/load.
- 7-year-old: nearly all actions discoverable from icons and one-line guidance. 12-year-old: rewarding mastery of capture-web placement, travel routes and resource usage. No compulsory reading-heavy tutorial or punishing fail states.
- All gameplay offline, no accounts, ads, telemetry, loot boxes, IAP, violence or gore. No third-party proprietary textures, sounds or unlicensed assets.
- Use supplied `assets/models/*.glb` and `assets/icons/*.svg` as actual starter assets when they import correctly; use art concept PNGs strictly as **reference** and generate additional genuinely in-engine low-poly geometry. Keep the playable world cohesive; avoid cubes left as untextured placeholders.

## Repository creation — DO THIS, do not ask the user to run it
1. `git --version; gh --version; gh auth status; dotnet --info; godot --version` (or discover the Godot .NET executable and call its actual path).
2. Confirm the intended local folder is new/empty and that the authenticated GitHub account does not already own `webwild`. Never overwrite existing contents. If name exists, use `webwild-game` and say so.
3. Initialise the source project in a **new** `webwild` directory, copy `AGENTS.md`, agreed design docs/manifest and originals needed for builds; add Godot/.NET `.gitignore`.
4. `git init -b main`; make a non-empty initial commit; `gh repo create webwild --private --source=. --remote=origin --push --description "Webwild: child-friendly spider colony simulator built with Godot and C#"`. If the name has changed, substitute the checked name.
5. Confirm `gh repo view --json nameWithOwner,url,visibility` and `git remote -v`. Do NOT silently push a public repo. Keep `main` stable and protected by test gates; use feature branches/PRs for P1–P8, merge only after verifying checks.
6. If `gh` is unauthenticated or repository creation is blocked, implement locally with commits and tests, record the blocker, and **do not claim GitHub setup succeeded**. Retry only safe, non-destructive steps.

## Execution strategy
- Implement milestones **P0, P1, P2, P3, P4, P5, P6, P7, P8** defined in `03_IMPLEMENTATION_MILESTONES.md`. Never declare a milestone complete until its concrete acceptance evidence is produced. Use `prompts/P*.md` as scoped execution plans if context is compressed or handed off.
- Start with deterministic **failing tests** for each meaningful rule (web anchor validation, resources, capture events, no duplicate rewards, worker tasks, weather damage, mission gating, save/load/corrupt recovery). Then implement; run focused tests and full suite.
- Make functional, visible playable increments from the first milestone. At each milestone: commit; report files and exact commands/results; create a PR; check the PR and CI; merge into `main` when green; move to the next. If branch/PR automation is unavailable, keep a clear linear commit history and flag the difference.
- Keep game loop truly playable under **Godot**: pure tests alone are not sufficient. Verify scene launches, C# loads, input works, navigation/physics behave, screens display, and objectives can all be completed in the runtime.
- Use reasonable defaults. Do not ask iterative preference questions; make small documented decisions. Do not stop at planning or midway because a milestone is substantial; use narrow corrective commits where needed.

## Minimum game flow (reproduce with automation and an actual playthrough)
Start menu → New Game → move/jump and assisted climb → select two valid anchors → build capture web → insect follows route and gets trapped → collect food → build silk bridge → visit leaf nursery → spend food/silk → hatch one new worker → worker automatically follows bridge / collects / repairs → wind event damages exposed web → player repairs or builds a sheltered alternative → finish the three linked objectives → save → exit → restart → continue with mission/colony/structures preserved. Optional mastery objective: capture 3 insects with ≤2 capture webs.

## Design quality
- Refer to `assets/concept/`, `assets/ui/`, `assets/maps/` and `08_ART_DIRECTION.md` before implementing the world/UI. Stylised geometry, layered natural scenery, varied silhouettes, visible anchor highlights, preview ghosts, animated webs and insects. Performance-conscious, not photorealistic.
- Camera: elevated third-person orbit with reasonable zoom; collision-safe and readable. Input: WASD/arrows, mouse, click, Space, E, Tab, Escape; see exact interaction rules in UI spec. Mouse selectable anchors and cancellation must be reliable.
- One-line hints and icon + text, large click targets, status announcements, calm default difficulty. Always offer a recoverable route when silk/food are low; never trap a player in an unwinnable state.
- Do not represent spider workers as biologically accurate eusocial roles: this is a deliberately fictional cooperative species. Optional field guide may distinguish fact from fiction but is not in scope for first playable.

## Required verification and evidence
- `dotnet build` and `dotnet test` on core tests; zero failing tests.
- Godot .NET editor headless import, launch/smoke test, and actual Godot runtime assertion harness against mission progression, interaction paths and save state. Preserve logs and test counts.
- Save contract checks: roundtrip, clean New Game, corrupt main + healthy backup, invalid schemas and never show “Saved” on failure.
- Manual visual inspection of opening, web placement, insect capture, nursery, storm, pause/settings and victory. Capture real Godot screenshots; do not substitute generated concept art as proof.
- Build Linux x64 export. Build Windows x64 export if templates/toolchain permit; verify export files and explicitly distinguish export success from a tested Windows machine startup. Do not invent test outcomes.
- Update `PROGRESS.md`, `ACCEPTANCE.md`, `README.md`, GitHub issues/PRs if available. Final report includes repo URL, exact main commit, tag `v0.1.0` if verified, Git state, scope, test results, runtime evidence, export paths, known limitations and how to play.

## Definition of done
The playable game meets every item in `11_ACCEPTANCE_CRITERIA.md`; the tutorial sequence can be completed end to end without developer tools or manual scene changes; the repo contains source, build instructions, tests and working asset attributions. **Begin by inspecting the environment and creating the project/repository; then work through to completion.**
