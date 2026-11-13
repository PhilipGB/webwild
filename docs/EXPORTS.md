# Exports

## Linux x64 — v0.2.2 release build

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Scene contract in the exported build: **PASS**, 55/55.
- Runtime tested headlessly: **PASS**, 307/307 probe checks including the new
  bridge walking, build confirmation, nursery, mastery, guidance, worker and
  construction-animation checks, plus the grounded island arrival check.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 308/308.
- The executable is the unchanged Godot 4.7.2 .NET template; the game code and
  assets are in the new `.pck`.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
9e0d97e96fe47f15e46573ef3b342fcc40faf59ee2c7c812247db9cc7a333ea7  Webwild.pck
```

## Windows x64 — v0.2.2 release build

- Path: `exports/windows/Webwild.exe`
- Payload: `exports/windows/Webwild.pck` and
  `exports/windows/data_Webwild_windows_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Windows" exports/windows/Webwild.exe
```

- File type verified: `PE32+ executable for MS Windows 5.02 (GUI), x86-64`.
- The export includes both regions, the corrected bridge surfaces, the build
  confirmation, the live nursery, arrival-based mastery and the v0.2.2 guidance.
- **Windows runtime: NOT VERIFIED.** No Windows host or Wine installation was
  available on the build machine.
- SHA-256:

```text
4760d3b83d01498d13ea6ff66b7867e4bf81fc5016f404c7472a5bc4cdc15647  Webwild.exe
85cbb41dda91204f7f9c92afe6e0c49a45101743e4fd44278eda25a1ded35b75  Webwild.pck
```

## Published v0.2.2 release archives

The v0.2.2 GitHub release carries both archives and a `SHA256SUMS` file.

```text
21b3b6ac573578e71bf0e8cd5ab3aa20e09a26682a97143343f67ec97990cd6a  Webwild-v0.2.2-linux-x64.zip
32faec4ec7da3db8cd1e9cb5b964f86f325430dfb8b0213eb1a95873ca8de299  Webwild-v0.2.2-windows-x64.zip
```

The Linux archive was extracted, checksum-matched and rerun: scene 55/55,
headless 307/307 and three consecutive rendered runs of 308/308. The Windows
archive was built and verified as PE32+ but not launched.

## Linux x64 — v0.2.1 shoreline build (not released)

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Scene contract in the exported build: **PASS**, 55/55 checks across both
  regions, including the authored shoreline mesh, water-below-banks,
  hazard-inside-shoreline and all eleven modular bridge parts.
- Runtime tested headlessly: **PASS**, 246/246 probe checks including Reedwater
  shoreline dry/wet positions, island safety, direct water recovery, durable
  supply claim, modular bridges and the toast lane.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 246/246 probe checks.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
5d5d103f0fa1f7681a8afcbe6612d941307f552019ed41a376808d962f74ff98  Webwild.pck
```

## Windows x64 — current v0.2.1 shoreline build (not released)

- Path: `exports/windows/Webwild.exe`
- Payload: `exports/windows/Webwild.pck` and
  `exports/windows/data_Webwild_windows_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Windows" exports/windows/Webwild.exe
```

- File type verified: `PE32+ executable for MS Windows 5.02 (GUI), x86-64`.
- The export includes both regions, the authored shoreline, all bridge assets
  and schema-v3 saves; it was not launched on Windows.
- **Windows runtime: NOT VERIFIED.** No Windows host or Wine installation was
  available on the build machine. Export success is not a Windows startup test.
- SHA-256:

```text
4760d3b83d01498d13ea6ff66b7867e4bf81fc5016f404c7472a5bc4cdc15647  Webwild.exe
dd142432bf9904ac4262b69b95cdf4565fdbaca4df277611a0679e0f7136e498  Webwild.pck
```

## Published v0.2.0 release archives

The published v0.2.0 GitHub release remains the last release. It does not
contain the v0.2.1 shoreline correction.

```text
ec1077f4d343e0c07635ccb087b34130e1aca0519b7405c131312e9759e07afd  Webwild-v0.2.0-linux-x64.zip
a44cc84f6e423cb9c6ecfb6d0638d2c6c592c5557e791d9c201cdd5f21bafd3b  Webwild-v0.2.0-windows-x64.zip
```

The v0.2.0 Linux archive was re-downloaded after publication, SHA-256 matched,
extracted and rerun. The v0.2.0 Windows archive was built but not launched.

## Template and host

- Godot `4.7.2.stable.mono.official.ed1daf0bf`
- .NET SDK `10.0.114`
- Godot .NET export templates `4.7.2.stable.mono`
- Linux x64 build host

## Notes

The `exports/` directory is deliberately excluded from Git because it contains
large binaries. Rebuild with the commands above. No v0.2.1 release archive is
published for this task.
