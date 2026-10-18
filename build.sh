#!/bin/bash
# 開発用のコマンド版（build/coverrepair と build/afcfs）をビルドする。
# 先に Tools/build_libs.sh で Dependencies/prefix（ライブラリ）を作っておくこと。
set -euo pipefail
cd "$(dirname "$0")"
P="$PWD/Dependencies/prefix"
[ -f "$P/lib/libimobiledevice-1.0.dylib" ] || { echo "Dependencies/prefix がありません。先に: bash Tools/build_libs.sh <ソースのフォルダ>"; exit 1; }
mkdir -p build/ModuleCache
TARGET="arm64-apple-macos14.0"
# 開発用: ライブラリの場所(rpath)は Dependencies/prefix/lib を指す（配布用の build-app.sh とは別）
clang -O2 -target "$TARGET" Sources/afcfs.c -I "$P/include" -L "$P/lib" \
    -limobiledevice-1.0 -lplist-2.0 -Wl,-rpath,"$P/lib" -o build/afcfs
swiftc -O -swift-version 5 -parse-as-library -target "$TARGET" -module-cache-path build/ModuleCache \
    Sources/Engine.swift Sources/CLI.swift -o build/coverrepair -lsqlite3
echo "build/coverrepair, build/afcfs"
