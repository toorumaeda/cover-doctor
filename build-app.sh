#!/bin/bash
# Cover Doctor.app（build/CoverDoctor.app）をビルドする。Apple Silicon / macOS 14 以降。署名は ad-hoc（Developer ID なし）。
# 先に Tools/build_libs.sh で Dependencies/prefix（ライブラリ）を作っておくこと。
# LGPL のライブラリ（libimobiledevice など4つ）は、アプリの Contents/Frameworks に、別ファイル（.dylib）として入れる。
# 利用者は、これを自分のビルドと入れ替えられる（入れ替えたら、codesign --force --sign - で署名し直す）。
set -euo pipefail
cd "$(dirname "$0")"
APP="$PWD/build/CoverDoctor.app"
P="$PWD/Dependencies/prefix"
[ -f "$P/lib/libimobiledevice-1.0.dylib" ] || { echo "Dependencies/prefix がありません。先に: bash Tools/build_libs.sh <ソースのフォルダ>"; exit 1; }
TARGET="arm64-apple-macos14.0"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Frameworks" "$APP/Contents/Resources/ja.lproj" "$APP/Contents/Resources/en.lproj" build/ModuleCache

# 動的ライブラリ4つを同梱し、参照先を @rpath（アプリ内）に書き換える
NAMES=(libplist-2.0 libimobiledevice-glue-1.0 libusbmuxd-2.0 libimobiledevice-1.0)
FILES=()
for n in "${NAMES[@]}"; do
    f=$(basename "$(readlink -f "$P/lib/$n.dylib")")      # 実体（例: libplist-2.0.4.dylib）
    cp -f "$(readlink -f "$P/lib/$n.dylib")" "$APP/Contents/Frameworks/$f"
    chmod u+w "$APP/Contents/Frameworks/$f"
    FILES+=("$f")
done
for f in "${FILES[@]}"; do
    install_name_tool -id "@rpath/$f" "$APP/Contents/Frameworks/$f" 2>/dev/null
    for g in "${FILES[@]}"; do
        [ "$f" = "$g" ] && continue
        base="${g%%.*}"; base="${base%-[0-9]*}"
        old=$(otool -L "$APP/Contents/Frameworks/$f" | awk -v d="${g%.dylib}" 'index($1, d) && $1 !~ /^@rpath/ {print $1}' | head -1)
        [ -n "$old" ] && install_name_tool -change "$old" "@rpath/$g" "$APP/Contents/Frameworks/$f" 2>/dev/null || true
    done
done

# 通信ツール（afcfs）。リンクのためだけに、一時的な別名（libX.dylib）を置く。rpath は、アプリ内の Frameworks
for n in "${NAMES[@]}"; do ln -sf "$(basename "$(readlink -f "$P/lib/$n.dylib")")" "$APP/Contents/Frameworks/$n.dylib"; done
clang -O2 -target "$TARGET" Sources/afcfs.c -I "$P/include" -L "$APP/Contents/Frameworks" -limobiledevice-1.0 -lplist-2.0 \
    -Wl,-rpath,@executable_path/../Frameworks -o "$APP/Contents/MacOS/afcfs"
for n in "${NAMES[@]}"; do rm -f "$APP/Contents/Frameworks/$n.dylib"; done
swiftc -O -swift-version 5 -parse-as-library -target "$TARGET" -module-cache-path build/ModuleCache \
    Sources/Engine.swift Sources/App.swift -o "$APP/Contents/MacOS/CoverDoctor" -lsqlite3

# アイコン（Tools/make_icon.sh で作った Assets/AppIcon.icns）
cp Assets/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# ライセンス文面（アプリ本体 + 同梱ライブラリ）
cp LICENSE THIRD_PARTY_NOTICES.md "$APP/Contents/Resources/"
cp -R Licenses "$APP/Contents/Resources/Licenses"

# 翻訳（英語は基準言語＝コード中の文言そのもの）
cp Resources/ja.lproj/Localizable.strings "$APP/Contents/Resources/ja.lproj/"
cp Resources/en.lproj/Localizable.stringsdict "$APP/Contents/Resources/en.lproj/"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>CoverDoctor</string>
<key>CFBundleIdentifier</key><string>local.coverdoctor</string>
<key>CFBundleIconFile</key><string>AppIcon</string>
<key>CFBundleName</key><string>Cover Doctor</string>
<key>CFBundleDisplayName</key><string>Cover Doctor</string>
<key>CFBundleDevelopmentRegion</key><string>en</string>
<key>CFBundleLocalizations</key><array><string>en</string><string>ja</string></array>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>0.1.0</string>
<key>CFBundleVersion</key><string>1</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST

# install_name_tool で署名が無効になるため、ad-hoc で署名し直す
for f in "$APP/Contents/Frameworks/"*.dylib "$APP/Contents/MacOS/afcfs"; do codesign --force --sign - "$f" >/dev/null 2>&1; done
codesign --force --sign - "$APP" >/dev/null 2>&1
echo "$APP"
