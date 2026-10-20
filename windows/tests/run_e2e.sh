#!/bin/bash
# 偽の iPhone を使った、診断 → 修復 → 再診断 → 元に戻す の通しテスト（Mac 版 Tests/run_e2e.sh と同じ内容）。実機には一切触れない。
# Linux / macOS の開発機用。Windows では tests/run_e2e.ps1 を使う。必要なもの: .NET 8 SDK, gcc, python3, ffmpeg
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p build/test
gcc -O2 -DAFCFS_FAKE_ONLY native/afcfs.c -o build/test/afcfs
dotnet build src/CoverDoctor.Cli -c Release -o build/test/cli >/dev/null
T="${TMPDIR:-/tmp}/coverdoctor-e2e"
python3 tests/make_fixture.py "$T" "${SCALE:-0}"
export COVERDOCTOR_FAKE_ROOT="$T/iphone" COVERDOCTOR_AFCFS="$PWD/build/test/afcfs" COVERDOCTOR_LANG="${COVERDOCTOR_LANG:-en}"
W="$T/work"
BIN=build/test/cli/coverdoctor
AFCFS=build/test/afcfs
. tests/e2e_steps.sh
