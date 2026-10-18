# Third-party notices

Cover Doctor talks to an iPhone over USB with the libraries below. They are built **unmodified** from the official release tarballs by `Tools/build_libs.sh`.

| Library | Version | License (as stated by the upstream project) | How it is used | Source |
|---|---|---|---|---|
| libimobiledevice | 1.4.0 | LGPL-2.1 | **Dynamic library** (`.dylib`) inside the app | https://github.com/libimobiledevice/libimobiledevice |
| libplist | 2.7.0 | LGPL-2.1 | **Dynamic library** inside the app | https://github.com/libimobiledevice/libplist |
| libimobiledevice-glue | 1.3.2 | LGPL-2.1 | **Dynamic library** inside the app | https://github.com/libimobiledevice/libimobiledevice-glue |
| libusbmuxd | 2.1.1 | LGPL-2.1 | **Dynamic library** inside the app | https://github.com/libimobiledevice/libusbmuxd |
| OpenSSL | 3.5.0 | Apache-2.0 | Built into libimobiledevice's `.dylib` (static) | https://github.com/openssl/openssl |

The command-line tools and the `usbmuxd` daemon of the libimobiledevice project use other licenses (e.g. GPL-2.0); they are **not** built or bundled here. On macOS the system's own `usbmuxd` service is used.

## Replacing the LGPL libraries (your right under the LGPL)

The four LGPL libraries are separate files in `Cover Doctor.app/Contents/Frameworks/`. You can replace them with your own (modified) builds:

1. Build your own versions (this project's `Tools/build_libs.sh` shows how; it uses the upstream source tarballs linked above).
2. Copy the new `.dylib` files over the ones in `Cover Doctor.app/Contents/Frameworks/` (keep the file names, and the install names must be `@rpath/<file name>`; see `build-app.sh` for how that is done).
3. Sign the app again, because changing files invalidates the signature: `codesign --force --deep --sign - "Cover Doctor.app"`.

Cover Doctor's complete source code and build scripts are published together with the app, so you can also rebuild the whole app (`Tools/build_libs.sh`, then `build-app.sh`).

## License texts

The license texts of the libraries are in `Licenses/` (taken unmodified from the upstream release tarballs) and are copied into the app (`Cover Doctor.app/Contents/Resources/Licenses`). For libplist and libimobiledevice the library license is `COPYING.LESSER` (LGPL-2.1); their plain `COPYING` file is the GPL-2.0 text that applies to those projects' command-line tools, which are not built or bundled here.

Cover Doctor's own code is under the MIT License (`LICENSE`).

*This summary reflects what the upstream projects state. It has not been reviewed by a lawyer.*
