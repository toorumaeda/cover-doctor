#!/bin/bash
# 配布用の zip（build/release/CoverDoctor-<版>.zip）と、そのSHA-256を作る。
# 先に Tools/build_libs.sh、Tools/make_icon.sh のあと、build-app.sh でアプリを作っておくこと。
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="$(plutil -extract CFBundleShortVersionString raw build/CoverDoctor.app/Contents/Info.plist)"
OUT="build/release"; rm -rf "$OUT"; mkdir -p "$OUT"
ZIP="$OUT/CoverDoctor-$VERSION.zip"
ditto -c -k --keepParent build/CoverDoctor.app "$ZIP"
( cd "$OUT" && shasum -a 256 "CoverDoctor-$VERSION.zip" > "CoverDoctor-$VERSION.zip.sha256" )
echo "$ZIP"; cat "$OUT/CoverDoctor-$VERSION.zip.sha256"
