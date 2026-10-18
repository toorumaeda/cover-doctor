# Roadmap

Cover Doctor is at version 0.1. This list is what is known to be missing.

## Not verified yet (help wanted)
- **Other macOS versions.** The app is built for macOS 14 or later (all binaries report `minos 14.0`), but it has only been run on macOS 27.0.1. Reports from macOS 14, 15 and 26 are very welcome.
- **Other iOS versions.** Only iOS 27.0 has been tested. The way iOS stores cover images may differ in other versions.
- **Other library sizes and setups** (for example, very small or very large libraries).

## Not supported yet
- **Intel Macs** (the build is Apple Silicon only; a universal build is a possible next step).
- **Windows** (it would need Apple's USB driver, "Apple Mobile Device Support").
- **Albums with no cover record on the iPhone.** The app only replaces existing image files; it never creates files or edits the database, so albums that have no cover record cannot be fixed.
- **Songs with no embedded cover image.** There is nothing to copy from.

## Ideas
- Show a warning when the iOS version differs from the verified one.
- Signed and notarized builds, so that macOS does not show the first-launch warning (requires an Apple Developer ID).
- More languages (English and Japanese are included).
