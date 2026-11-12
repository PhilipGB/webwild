# Exports

## Linux x64

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Scene contract in the exported build: **PASS**, 51/51 checks across both
  regions, including all eleven modular bridge parts.
- Runtime tested headlessly: **PASS**, 236/236 probe checks including Reedwater
  Bank, durable supply claim, modular bridge visuals and the toast lane.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 236/236 probe checks.
- Published GitHub release asset re-downloaded, SHA-256 matched, extracted and
  rerun: **PASS**, 51/51 scene checks and 236/236 runtime checks both headless
  and rendered.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
1c6454c87fc3de74efe9eeaefa9f91d70b2fcbb883b56b0b7a09eda3af3bab31  Webwild.pck
ec1077f4d343e0c07635ccb087b34130e1aca0519b7405c131312e9759e07afd  Webwild-v0.2.0-linux-x64.zip
```

## Windows x64

- Path: `exports/windows/Webwild.exe`
- Payload: `exports/windows/Webwild.pck` and
  `exports/windows/data_Webwild_windows_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Windows" exports/windows/Webwild.exe
```

- File type verified: `PE32+ executable for MS Windows 5.02 (GUI), x86-64`.
- The export includes both regions, all bridge assets and the schema-v3 save
  code; it was not launched on Windows.
- **Windows runtime: NOT VERIFIED.** No Windows host or Wine installation was
  available on the build machine. Export success is not a Windows startup test.
- SHA-256:

```text
4760d3b83d01498d13ea6ff66b7867e4bf81fc5016f404c7472a5bc4cdc15647  Webwild.exe
0cb3a6b44a4071c7efcc15372461fba3155ca873d26e634acb6563b6c64fc4ce  Webwild.pck
a44cc84f6e423cb9c6ecfb6d0638d2c6c592c5557e791d9c201cdd5f21bafd3b  Webwild-v0.2.0-windows-x64.zip
```

## Release archive contents

Both archives contain the exported game, `README_RELEASE.md` with controls and
verification limitations, `ATTRIBUTION.md`, `LICENSE_BRIDGE_ASSETS.md` and an
internal `SHA256SUMS.txt` for the executable and PCK.

The top-level release `SHA256SUMS` records both archive hashes. Linux was tested
from a fresh extraction after re-download. Windows was not launched.

## Template and host

- Godot `4.7.2.stable.mono.official.ed1daf0bf`
- .NET SDK `10.0.114`
- Godot .NET export templates `4.7.2.stable.mono`
- Linux x64 build host

## Notes

The `exports/` directory is deliberately excluded from Git because it contains
large binaries. Release archives live under the ignored `builds/` workspace and
are attached to the GitHub release. Rebuild with the commands above.
