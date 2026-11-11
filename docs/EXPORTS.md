# Exports

## Linux x64

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Runtime tested headlessly: **PASS**, 58/58 probe checks.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 58/58 probe checks,
  all 16 screenshots written.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
ebf9b4207f4381741ee51e77fa3c7089bf891ccfa789cabe6c3fa3dddba08af5  Webwild.pck
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
- **Windows runtime: NOT VERIFIED.** No Windows host or Wine installation was
  available on the build machine. Export success is not a Windows startup test.
- SHA-256:

```text
d5d357b454f33ea935d3f204cbc10aed632bc956e89c2aa3c8b59fdae52bb797  Webwild.exe
0240de3f231cf2dce9c29e70c0274cca5da0bc52c365bd79e6d9762c07aa141f  Webwild.pck
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
