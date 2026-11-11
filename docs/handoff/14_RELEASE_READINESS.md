# Release readiness — P8

## Preconditions
All first-playable functionality on main; no critical/p1 regression; automated tests and Godot runtime tests green; actual mouse/keyboard playthrough finished.

## Build and export verification
1. Record `dotnet --info`, Godot .NET `--version`, `gh --version`, OS/architecture.
2. `dotnet restore && dotnet build && dotnet test` from clean checkout.
3. `"$GODOT_DOTNET_BIN" --headless --path . --editor --quit`, then headless main/runtime acceptance runner; record real exit codes.
4. Ensure **matching .NET export templates** are installed. Use configured `export_presets.cfg` with the actual preset names; invoke Godot `--headless --path . --export-release "Linux/X11" <output>` and `--export-release "Windows Desktop" <output>` (adapt preset names; check actual CLI syntax/version).
5. Test Linux exported executable starts (not merely file exists). Windows exports: verify generated executable and payload; if no Windows environment exists, mark `Windows runtime: NOT VERIFIED`.
6. Produce honest screenshots from running Godot game, not reference art, at main menu, first web, fly trapped, worker crossing bridge, storm, nursery and victory.
7. Update `README.md` with launch/run instructions from clean checkout and exports, `ATTRIBUTION.md`, `ACCEPTANCE.md`, `PROGRESS.md`, `CHANGELOG.md`.
8. Tag `v0.1.0` only when all release-blocking acceptance items pass. Generate release ZIPs outside tracked source folder, optionally attach to GitHub release.

## Final agent report template
```text
Webwild v0.1.0 — Final delivery
Repository: <URL> (private)
Main HEAD: <SHA>; tag: <SHA / not created and why>
PRs: P0 ... P8 links + merge states
Gameplay: M01/M02/M03 human playthrough [PASS/FAIL/NOT VERIFIED]
Core tests: <total/pass/fail/skip> + command
Godot runtime acceptance: <assertions/pass/fail> + command
Godot scene import and smoke: <result>
Linux export: <path> + launch evidence
Windows export: <path> + runtime tested? YES/NO
Actual screenshot paths: <links>
Known issues: <severity, impact, reproducer>
How to play: <3 lines>
Clean Git status: <yes/no>
```

## Release blockers
Can't finish tutorial; missing C# scripts; frame hitch/crash on normal gameplay; silent save corruption; impossible resource dead-end; misleading success states; never-tested critical mechanics; public repository against user intent.
