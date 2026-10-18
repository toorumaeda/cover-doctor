#!/bin/bash
# 配布用の zip（build/release/CoverDoctor-<版>-macOS-arm64.zip）と、そのSHA-256を作る。
# ファイル名にプラットフォームを入れるのは、将来、Windows 版などを同じリリースに並べられるようにするため。
# 先に Tools/build_libs.sh、Tools/make_icon.sh のあと、build-app.sh でアプリを作っておくこと。
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="$(plutil -extract CFBundleShortVersionString raw build/CoverDoctor.app/Contents/Info.plist)"
OUT="build/release"; rm -rf "$OUT"; mkdir -p "$OUT"
NAME="CoverDoctor-$VERSION-macOS-arm64.zip"
ZIP="$OUT/$NAME"
ditto -c -k --keepParent build/CoverDoctor.app "$ZIP"
( cd "$OUT" && shasum -a 256 "$NAME" > "$NAME.sha256" )
echo "$ZIP"; cat "$OUT/$NAME.sha256"
