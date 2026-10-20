# Third-party notices (Windows version)

Cover Doctor for Windows talks to an iPhone over USB with the libraries below. The libimobiledevice libraries are built **unmodified** from the official release tarballs by `tools/build_libs_mingw.sh` (the same versions as the Mac version).

| Library | Version | License (as stated by the upstream project) | How it is used | Source |
|---|---|---|---|---|
| libimobiledevice | 1.4.0 | LGPL-2.1 | **Dynamic library** (`libimobiledevice-1.0.dll`) next to `CoverDoctor.exe` | https://github.com/libimobiledevice/libimobiledevice |
| libplist | 2.7.0 | LGPL-2.1 | **Dynamic library** (`libplist-2.0.dll`) | https://github.com/libimobiledevice/libplist |
| libimobiledevice-glue | 1.3.2 | LGPL-2.1 | **Dynamic library** (`libimobiledevice-glue-1.0.dll`) | https://github.com/libimobiledevice/libimobiledevice-glue |
| libusbmuxd | 2.1.1 | LGPL-2.1 | **Dynamic library** (`libusbmuxd-2.0.dll`) | https://github.com/libimobiledevice/libusbmuxd |
| OpenSSL | 3.5.0 | Apache-2.0 | Built into `libimobiledevice-1.0.dll` (static) | https://github.com/openssl/openssl |
| .NET runtime | 8.0.31 | MIT | Built into `CoverDoctor.exe` (self-contained) | https://github.com/dotnet/runtime |
| Windows Desktop runtime (WPF) | 8.0.31 | MIT | Built into `CoverDoctor.exe` | https://github.com/dotnet/wpf |
| SkiaSharp (and Skia) | 4.153.1 | MIT (SkiaSharp); BSD-3-Clause and others (Skia, see its notices) | Built into `CoverDoctor.exe` (image decoding and JPEG encoding) | https://github.com/mono/SkiaSharp |
| Microsoft.Data.Sqlite | 10.0.13 | MIT | Built into `CoverDoctor.exe` (reading the library database copy) | https://github.com/dotnet/efcore |
| SQLitePCLRaw | 2.1.13 | Apache-2.0 | Built into `CoverDoctor.exe` | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | (bundled with SQLitePCLRaw) | Public domain | Built into `CoverDoctor.exe` | https://www.sqlite.org/ |

The command-line tools and the `usbmuxd` daemon of the libimobiledevice project use other licenses (e.g. GPL-2.0); they are **not** bundled here. On Windows, Apple's own service (Apple Mobile Device Support, part of iTunes or the Apple Devices app) is used to reach the iPhone; it is not bundled either.

## Replacing the LGPL libraries (your right under the LGPL)

The four LGPL libraries are separate `.dll` files in the same folder as `CoverDoctor.exe` and `afcfs.exe`. You can replace them with your own (modified) builds:

1. Build your own versions (`tools/build_libs_mingw.sh` shows how; it uses the upstream source tarballs linked above, cross-compiled with mingw-w64, or MSYS2 on Windows, see `docs/BUILD.md`).
2. Copy the new `.dll` files over the ones next to `afcfs.exe`, keeping the file names. Windows loads DLLs from the program's own folder first, so no other change is needed.

The complete source code of Cover Doctor for Windows and its build scripts are published together with the program, so you can also rebuild everything (`tools/build_libs_mingw.sh`, then `tools/make_release.sh`).

## License texts

The license texts are in `Licenses/`. For libplist and libimobiledevice the library license is `COPYING.LESSER` (LGPL-2.1); their plain `COPYING` file is the GPL-2.0 text that applies to those projects' command-line tools, which are not bundled here.

Cover Doctor's own code is under the MIT License (`LICENSE`).

*This summary reflects what the upstream projects state. It has not been reviewed by a lawyer.*
