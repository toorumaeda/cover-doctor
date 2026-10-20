# Building Cover Doctor for Windows

Everything can be built on a Linux machine (cross-compiled); this is how the current files were made. Building on Windows is also possible (below).

## On Linux (Ubuntu 24.04, as used for development)

Packages: `dotnet-sdk-8.0`, `mingw-w64`, `autoconf`, `automake`, `libtool`, `pkg-config`, `make`, `python3`, `ffmpeg` (tests only). Optional, for running the Windows build: `wine64`.

```bash
# 1. the libimobiledevice libraries as Windows DLLs (once). Put the source tarballs in one folder:
#    libplist-2.7.0.tar.bz2, libimobiledevice-glue-1.3.2.tar.bz2, libusbmuxd-2.1.1.tar.bz2,
#    libimobiledevice-1.4.0.tar.bz2 (GitHub releases of each project), openssl-3.5.0.tar.gz
bash tools/build_libs_mingw.sh <folder with the tarballs>      # -> Dependencies/prefix-win64

# 2. tests against a fake iPhone (Linux build of the engine and of afcfs in fake-only mode)
bash tests/run_e2e.sh
SCALE=150 bash tests/run_e2e.sh

# 3. the release zip and its SHA-256
bash tools/make_release.sh                                    # -> build/release/CoverDoctor-0.1.0-Windows-x64.zip
```

Notes
- The WPF window is built in code (no XAML) and references only the WPF framework (`Microsoft.WindowsDesktop.App.WPF`), so the Windows app can be built with the Linux .NET SDK (`EnableWindowsTargeting`).
- libimobiledevice 1.4.0's `configure` asks for libtatsu, which only its command-line tool `ideviceimagemounter` uses; the script builds only the library and satisfies that check with empty values.
- `-no-undefined` is passed to libtool so that DLLs are produced, and `-lIphlpapi` (upper case in libusbmuxd) is resolved with a lower-case alias on Linux.

## On Windows

1. Install MSYS2 and open the **MSYS2 MINGW64** shell. Install: `pacman -S base-devel git mingw-w64-x86_64-gcc make libtool autoconf automake-wrapper pkg-config`.
2. `HOST= bash tools/build_libs_mingw.sh <folder with the tarballs>` (an empty `HOST` builds natively).
3. Build `afcfs.exe` as in `tools/make_release.sh` (`gcc ... native/afcfs.c ...`).
4. Install the .NET 8 SDK and run `dotnet publish src/CoverDoctor.App -c Release -o build/app`.
5. Tests (needs Python 3 and ffmpeg on PATH): `powershell -ExecutionPolicy Bypass -File tests\run_e2e.ps1 -App <folder with CoverDoctor.exe and afcfs.exe> -Gui`.

The Windows-native build path has not been tried yet.
