#!/bin/bash
# Assets/AppIcon.svg から、アプリのアイコン（Assets/AppIcon.icns と、README用の docs/images/icon.png）を作る。
# 角丸の枠、影、余白（macOS のアイコンの規格）は Tools/makeicon.swift が付ける。ロゴ文字は入れない。
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build docs/images
swiftc -O -module-cache-path build/ModuleCache Tools/makeicon.swift -o build/makeicon 2>/dev/null
SET=build/AppIcon.iconset
rm -rf "$SET"; mkdir -p "$SET"
for spec in 16:icon_16x16 32:icon_16x16@2x 32:icon_32x32 64:icon_32x32@2x 128:icon_128x128 256:icon_128x128@2x 256:icon_256x256 512:icon_256x256@2x 512:icon_512x512 1024:icon_512x512@2x; do
  build/makeicon Assets/AppIcon.svg "$SET/${spec#*:}.png" "${spec%%:*}" nologo
done
iconutil -c icns "$SET" -o Assets/AppIcon.icns
build/makeicon Assets/AppIcon.svg docs/images/icon.png 512 nologo
echo "Assets/AppIcon.icns, docs/images/icon.png"
