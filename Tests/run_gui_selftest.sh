#!/bin/bash
# 画面側のコード（Model）を、偽の iPhone で自動テストする。画面を操作する代わりに、アプリ内の自己テストを動かす。
# 一瞬ウィンドウが開く。実機には触れない。「はじめに」の確認は、起動引数 -acceptedNotice YES で省く（利用者の設定は書き換えない）。
set -euo pipefail
cd "$(dirname "$0")/.."
./build-app.sh >/dev/null 2>&1
T="${TMPDIR:-/tmp}/coverdoctor-guitest"
python3 Tests/make_fixture.py "$T"
OUT="$T/selftest.txt"; rm -f "$OUT"
rm -rf "$HOME/Library/Application Support/CoverRepair/FAKE-0000"
COVERDOCTOR_FAKE_ROOT="$T/iphone" COVERDOCTOR_SELFTEST_OUT="$OUT" build/CoverDoctor.app/Contents/MacOS/CoverDoctor -acceptedNotice YES >/dev/null 2>&1 &
PID=$!
for i in $(seq 1 120); do [ -f "$OUT" ] && break; kill -0 $PID 2>/dev/null || break; sleep 1; done
kill $PID 2>/dev/null || true
[ -f "$OUT" ] && cat "$OUT" || { echo "FAIL: 結果ファイルが作られなかった"; exit 1; }
head -1 "$OUT" | grep -q PASS
