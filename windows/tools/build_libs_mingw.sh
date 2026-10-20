#!/bin/bash
# libimobiledevice 一式（LGPL の4つ）を、Windows x64 用の DLL として、OpenSSL（Apache-2.0）を静的ライブラリとして、
# Linux 上の mingw-w64 でクロスビルドする（Mac 版の Tools/build_libs.sh と同じ版・同じ構成）。
# LGPL のライブラリは、アプリのフォルダに別ファイル（.dll）のまま同梱するので、利用者が自分のビルドと入れ替えられる。
# Windows 上で直接ビルドする場合は、MSYS2 の「MINGW64」シェルで、HOST を空にして同じ手順を実行する（docs/BUILD.md）。
# 使い方: bash tools/build_libs_mingw.sh <ソースtarを置いたフォルダ> [出力先(既定: Dependencies/prefix-win64)]
# 必要なソース: libplist-2.7.0 / libimobiledevice-glue-1.3.2 / libusbmuxd-2.1.1 / libimobiledevice-1.4.0 (.tar.bz2), openssl-3.5.0.tar.gz
set -euo pipefail
SRC="$(cd "$1" && pwd)"
PREFIX="${2:-$(cd "$(dirname "$0")/.." && pwd)/Dependencies/prefix-win64}"
HOST="${HOST-x86_64-w64-mingw32}"
WORK="$(mktemp -d)"
mkdir -p "$PREFIX"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"
export CFLAGS="-O2"
# 同梱する DLL が、mingw の実行時ライブラリ（libgcc 等）に依存しないようにする
export LDFLAGS="-static-libgcc"
JOBS=$(nproc)
HOSTARG=(); [ -n "$HOST" ] && HOSTARG=(--host="$HOST")
cd "$WORK"

if [ ! -f "$PREFIX/lib/libcrypto.a" ]; then
echo "== OpenSSL"
tar xf "$SRC/openssl-3.5.0.tar.gz" && cd openssl-3.5.0
XP=(); [ -n "$HOST" ] && XP=(--cross-compile-prefix="$HOST-")
# OpenSSL が埋め込む「設定ファイル等の場所」は、管理者しか書き込めない C:\Windows の下の、存在しないフォルダにする
# （ビルドした PC のパスを埋め込まないため、また、一般の利用者が作れる場所（C:\home など）の設定ファイルを読まないため）。
# 空白を含むパス（Program Files）は、OpenSSL の install が扱えないので使わない。
OSSL_PREFIX="C:/Windows/CoverDoctor/OpenSSL"
./Configure mingw64 "${XP[@]}" no-shared no-tests no-apps no-docs no-module no-engine \
  --prefix="$OSSL_PREFIX" --libdir=lib --openssldir=ssl >/dev/null
make -j"$JOBS" >/dev/null && make install_sw DESTDIR="$WORK/ossl-stage/" >/dev/null
STAGED="$(dirname "$(find "$WORK/ossl-stage" -type d -path "*/include/openssl" | head -1)")/.."
mkdir -p "$PREFIX/lib/pkgconfig" "$PREFIX/include"
cp -R "$STAGED/include/openssl" "$PREFIX/include/"
cp "$STAGED/lib/libcrypto.a" "$STAGED/lib/libssl.a" "$PREFIX/lib/"
for pc in libcrypto libssl openssl; do sed "s#^prefix=.*#prefix=$PREFIX#" "$STAGED/lib/pkgconfig/$pc.pc" > "$PREFIX/lib/pkgconfig/$pc.pc"; done
cd "$WORK"
fi

build() {   # build <tarball> <dir> <追加configure引数...>
  local tb="$1" dir="$2"; shift 2
  echo "== $dir"
  tar xf "$SRC/$tb" && cd "$dir"
  ./configure "${HOSTARG[@]}" --prefix="$PREFIX" --enable-shared --disable-static "$@" >/dev/null
  # -no-undefined: libtool が Windows 用の DLL を作るのに必要（無いと静的ライブラリだけになる）
  make -j"$JOBS" LDFLAGS="$LDFLAGS -no-undefined" >/dev/null && make install LDFLAGS="$LDFLAGS -no-undefined" >/dev/null
  cd "$WORK"
}
# libusbmuxd は Windows のシステムライブラリを "-lIphlpapi" と大文字で指定する。Linux の mingw は小文字のファイル名なので、別名を置く
if [ -n "$HOST" ] && [ ! -e "$PREFIX/lib/libIphlpapi.a" ]; then
  mkdir -p "$PREFIX/lib"; ln -sf "$($HOST-gcc -print-file-name=libiphlpapi.a)" "$PREFIX/lib/libIphlpapi.a"
fi
export LDFLAGS="$LDFLAGS -L$PREFIX/lib"
build libplist-2.7.0.tar.bz2 libplist-2.7.0 --without-cython
build libimobiledevice-glue-1.3.2.tar.bz2 libimobiledevice-glue-1.3.2
build libusbmuxd-2.1.1.tar.bz2 libusbmuxd-2.1.1
# libimobiledevice: ライブラリ本体だけを作る（コマンドラインのツールは GPL で、同梱しない）。
# libtatsu はツール（ideviceimagemounter）だけが使うので、ライブラリには不要。configure の確認だけを空の値で通す。
# OpenSSL は静的に取り込む（Windows のシステム DLL: ws2_32, crypt32 が必要）。
echo "== libimobiledevice-1.4.0"
tar xf "$SRC/libimobiledevice-1.4.0.tar.bz2" && cd libimobiledevice-1.4.0
libtatsu_CFLAGS=" " libtatsu_LIBS=" " LIBS="-lws2_32 -lcrypt32" \
  ./configure "${HOSTARG[@]}" --prefix="$PREFIX" --enable-shared --disable-static --without-cython --with-openssl >/dev/null
for d in 3rd_party common src; do make -C $d -j"$JOBS" LDFLAGS="$LDFLAGS -no-undefined" >/dev/null; done
make -C src install LDFLAGS="$LDFLAGS -no-undefined" >/dev/null
make -C include install >/dev/null
cd "$WORK"

rm -rf "$WORK"
echo "完了: $PREFIX/bin（*.dll）"
ls "$PREFIX/bin"/*.dll
