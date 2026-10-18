#!/bin/bash
# libimobiledevice 一式（LGPL の4つ）を、macOS 14 以上で動く「動的ライブラリ（.dylib）」として、
# OpenSSL（Apache-2.0）を静的ライブラリとして、ビルドする。
# LGPL のライブラリは、アプリの中の別ファイルのまま同梱するので、利用者が自分のビルドと入れ替えられる。
# 使い方: bash Tools/build_libs.sh <ソースtar.gzを置いたフォルダ> [出力先(既定: Dependencies/static)]
# 必要なソース: libplist-2.7.0 / libimobiledevice-glue-1.3.2 / libusbmuxd-2.1.1 / libimobiledevice-1.4.0 (.tar.bz2), openssl-3.5.0.tar.gz
set -euo pipefail
SRC="$(cd "$1" && pwd)"
PREFIX="${2:-$(cd "$(dirname "$0")/.." && pwd)/Dependencies/prefix}"
WORK="$(mktemp -d)"
mkdir -p "$PREFIX"
export MACOSX_DEPLOYMENT_TARGET=14.0
export CFLAGS="-O2 -arch arm64 -mmacosx-version-min=14.0"
export LDFLAGS="-arch arm64 -mmacosx-version-min=14.0"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
JOBS=$(sysctl -n hw.ncpu)
cd "$WORK"

echo "== OpenSSL"
tar xf "$SRC/openssl-3.5.0.tar.gz" && cd openssl-3.5.0
./Configure darwin64-arm64-cc no-shared no-tests no-apps --prefix="$PREFIX" --openssldir="$PREFIX/ssl" -mmacosx-version-min=14.0 >/dev/null
make -j"$JOBS" >/dev/null && make install_sw >/dev/null
cd "$WORK"

build() {   # build <tarball> <dir> <追加configure引数...>
  local tb="$1" dir="$2"; shift 2
  echo "== $dir"
  tar xf "$SRC/$tb" && cd "$dir"
  ./configure --prefix="$PREFIX" --enable-shared --disable-static "$@" >/dev/null
  make -j"$JOBS" >/dev/null && make install >/dev/null
  cd "$WORK"
}
build libplist-2.7.0.tar.bz2 libplist-2.7.0 --without-cython
build libimobiledevice-glue-1.3.2.tar.bz2 libimobiledevice-glue-1.3.2
build libusbmuxd-2.1.1.tar.bz2 libusbmuxd-2.1.1
build libimobiledevice-1.4.0.tar.bz2 libimobiledevice-1.4.0 --without-cython

echo "== 確認（最低 macOS）"
for f in "$PREFIX"/lib/*.a; do printf "%s: " "$(basename "$f")"; vtool -show-build "$f" 2>/dev/null | awk '/minos/ {print $2; exit}' || true; echo; done | head -20
rm -rf "$WORK"
echo "完了: $PREFIX"
