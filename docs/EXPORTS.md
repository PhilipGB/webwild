# Exports

## Linux x64

- Path: `exports/linux/Webwild.x86_64`
- Payload: `exports/linux/Webwild.pck` and
  `exports/linux/data_Webwild_linuxbsd_x86_64/`
- Built with:

```bash
godot --headless --path . --export-release "Linux" exports/linux/Webwild.x86_64
```

- Runtime tested headlessly: **PASS**, 66/66 probe checks.
- Runtime tested rendered under Xvfb at 1280×720: **PASS**, 66/66 probe checks,
  all 17 screenshots written.
- SHA-256:

```text
d776f83ae09d82cfa1295fb3d70ad7ab8a77fe66a4cc4f9d42244fae3665d8cc  Webwild.x86_64
3489659a45f34210549830d7907241405db321e97239ab8f3c485f3d929bb149  Webwild.pck
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
6af5fdd28e0e5895c4f971ea312277bc53b9c1ab3dc9cac872a68ec77643def1  Webwild.exe
d36954d198c39b5b65d03b4c575a83e885dcb204d0b7a034135daa7fef808362  Webwild.pck
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
