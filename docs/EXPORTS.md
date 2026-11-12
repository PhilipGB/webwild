# Exports

## Linux x64

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Scene contract in the exported build: **PASS**, 47/47 checks across both regions.
- Runtime tested headlessly: **PASS**, 168/168 probe checks including Reedwater Bank.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 168/168 probe checks,
  screenshots including the Reedwater Bank travel and objective flow written.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
a6112192c3121488be316cac8cce3c6b5426763c146e57b417de5e9e5f12c774  Webwild.pck
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
- Export includes the Reedwater Bank region scenes and assets; it was not launched
  on Windows.
- **Windows runtime: NOT VERIFIED.** No Windows host or Wine installation was
  available on the build machine. Export success is not a Windows startup test.
- SHA-256:

```text
4760d3b83d01498d13ea6ff66b7867e4bf81fc5016f404c7472a5bc4cdc15647  Webwild.exe
7c52d6d815de60a5fc9bf0a061744f6e9d3ee5cb411fb7a3930ba71931ed1e3d  Webwild.pck
```

## Template and host

- Godot `4.7.2.stable.mono.official.ed1daf0bf`
- .NET SDK `10.0.114`
- Godot .NET export templates `4.7.2.stable.mono`
- Linux x64 build host

## Notes

The `exports/` directory is deliberately excluded from Git because it contains
large binaries. Rebuild with the commands above. The binaries were present in the
delivery workspace when this evidence was recorded.
