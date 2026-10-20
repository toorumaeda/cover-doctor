#!/bin/bash
# 配布用の zip（build/release/CoverDoctor-<版>-Windows-x64.zip）と SHA-256 を作る。Linux の開発機で実行する。
# 先に tools/build_libs_mingw.sh で Dependencies/prefix-win64（ライブラリ）を作っておくこと。
# 公開（GitHub のリリースへの登録）は、このスクリプトでは行わない。
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${VERSION:-0.1.0}"
P="$PWD/Dependencies/prefix-win64"
[ -f "$P/bin/libimobiledevice-1.0.dll" ] || { echo "Dependencies/prefix-win64 がありません。先に: bash tools/build_libs_mingw.sh <ソースのフォルダ>"; exit 1; }
NAME="CoverDoctor-$VERSION-Windows-x64"
OUT="build/release/$NAME"
rm -rf "$OUT" "build/release/$NAME.zip"
mkdir -p "$OUT"

# 通信ツール（afcfs.exe）。mingw の実行時ライブラリは静的に取り込み、LGPL の4つは DLL のまま横に置く
x86_64-w64-mingw32-gcc -O2 -Wall native/afcfs.c -I "$P/include" -L "$P/lib" \
    -limobiledevice-1.0 -lplist-2.0 -lusbmuxd-2.0 -static-libgcc -o "$OUT/afcfs.exe"
for n in libplist-2.0 libimobiledevice-glue-1.0 libusbmuxd-2.0 libimobiledevice-1.0; do cp "$P/bin/$n.dll" "$OUT/"; done
x86_64-w64-mingw32-strip "$OUT"/*.dll "$OUT/afcfs.exe"

# 本体（画面とコマンド版）。.NET を別に入れなくても動く1つの exe
# テスト版（版番号に -test を含む）だけ、書き込みの試験用コマンド testswap を入れる
TS=false; case "$VERSION" in *-test*) TS=true;; esac
dotnet publish src/CoverDoctor.App -c Release -p:Version="$VERSION" -p:TestSwap=$TS -o build/app >/dev/null
cp build/app/CoverDoctor.exe "$OUT/"

cp LICENSE THIRD_PARTY_NOTICES.md "$OUT/"
cp -R Licenses "$OUT/Licenses"
sed "1s/0\.1\.0/$VERSION/" docs/README-Windows.txt > "$OUT/README.txt"
cp tools/check_amds.ps1 "$OUT/"

# 同梱の DLL が、Windows の標準のもの以外に依存していないかを確かめる
for f in "$OUT"/*.dll "$OUT/afcfs.exe"; do
  bad=$(x86_64-w64-mingw32-objdump -p "$f" | awk '/DLL Name/ {print $3}' | grep -v -i -E '^(kernel32|msvcrt|advapi32|crypt32|ole32|shell32|user32|ws2_32|iphlpapi|libplist-2\.0|libimobiledevice-glue-1\.0|libusbmuxd-2\.0|libimobiledevice-1\.0)\.dll$' || true)
  [ -z "$bad" ] || { echo "想定外の依存: $(basename "$f"): $bad"; exit 1; }
done

(cd build/release && rm -f "$NAME.zip" && python3 -c "
import os, sys, zipfile
name = sys.argv[1]
with zipfile.ZipFile(name + '.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk(name):
        for f in sorted(files):
            p = os.path.join(root, f); z.write(p, p)
" "$NAME" && sha256sum "$NAME.zip" > "$NAME.zip.sha256")
ls -la build/release
cat "build/release/$NAME.zip.sha256"
