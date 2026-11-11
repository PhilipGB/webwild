# Evidence index

All evidence is recorded against concrete commits. Commands were run on the
build host documented in P0.

## P0 — Toolchain and repository

- Host: Linux x64, Ubuntu 26.04, AMD Radeon 890M
- `dotnet --version` → 10.0.114; SDKs 8.0.427 and 10.0.114
- `godot --version` → `4.7.2.stable.mono.official.ed1daf0bf`
- `gh --version` → 2.103.3, authenticated as `PhilipGB`
- `dotnet build` → succeeded, 0 warnings, 0 errors
- `dotnet test` → 1 passed, 0 failed, 0 skipped
- `godot --headless --path . --editor --quit` → exit 0, all GLBs imported
- `godot --path . --quit-after 90` → exit 0; audio falls back to dummy driver
  because the build host has no sound device (not a gameplay error)
- Repository created privately via `gh` (URL and SHA recorded after push)
