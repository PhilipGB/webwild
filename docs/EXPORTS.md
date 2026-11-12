# Exports

## Linux x64

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Runtime tested headlessly: **PASS**, 80/80 probe checks.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 80/80 probe checks,
  all 19 screenshots written.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
cb51c68be209cd7c5e488c860e515a9a5bd42499c30133877eb51731e26d8e28  Webwild.pck
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
22a0171d58bd8557b9c42873c89780628284e622867441cc0e67b03ae53c1524  Webwild.exe
dc850e97f696d9cdbbc98898f19a937aa1a41edbac14cc4240f81f6483038651  Webwild.pck
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
