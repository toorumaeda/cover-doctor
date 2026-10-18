#!/bin/bash
# 偽の iPhone に接続した状態で、アプリの画面を開く（実機には触れない）。
# 画面で「診断する」→「直す」→「元に戻す」を試せる。言語は引数 en / ja で選ぶ（省略すると ja）。
set -euo pipefail
cd "$(dirname "$0")/.."
LANG_CODE="${1:-ja}"
./build-app.sh >/dev/null 2>&1
T="${TMPDIR:-/tmp}/coverdoctor-gui"
python3 Tests/make_fixture.py "$T"
pkill -x CoverDoctor 2>/dev/null || true
open -n --env COVERDOCTOR_FAKE_ROOT="$T/iphone" build/CoverDoctor.app --args -AppleLanguages "($LANG_CODE)"
echo "偽の iPhone で起動しました（言語: ${LANG_CODE}）。作業フォルダ: ~/Library/Application Support/CoverRepair/FAKE-0000"
