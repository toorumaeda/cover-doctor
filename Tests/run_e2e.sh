#!/bin/bash
# 偽の iPhone を使った、診断 → 修復 → 再診断 → 元に戻す の通しテスト。実機には一切触れない。
set -euo pipefail
cd "$(dirname "$0")/.."
./build.sh >/dev/null
T="${TMPDIR:-/tmp}/coverdoctor-e2e"
python3 Tests/make_fixture.py "$T" "${SCALE:-0}"
export COVERDOCTOR_FAKE_ROOT="$T/iphone"
W="$T/work"
BIN=./build/coverrepair

echo "== 1. 診断"; $BIN diagnose --work "$W" | tail -4
python3 - "$T" "$W" <<'PY'
import json, sys
T, W = sys.argv[1:3]
exp = json.load(open(f"{T}/expected.json"))["expected"]
plan = {e["token"]: e for e in json.load(open(f"{W}/plan.json"))["entries"]}
bad = [(t, v["album"], v["status"], plan.get(t, {}).get("status")) for t, v in exp.items() if plan.get(t, {}).get("status") != v["status"]]
extra = set(plan) - set(exp)
full = json.load(open(f"{W}/plan.json"))
meta = json.load(open(f"{T}/expected.json"))
assert full["libraryAlbumCount"] == meta["library_albums"], f"ライブラリのアルバム総数が違う: {full['libraryAlbumCount']} (期待 {meta['library_albums']})"
assert full["notDiagnosedCount"] == meta["not_diagnosed"], f"診断対象外の数が違う: {full['notDiagnosedCount']} (期待 {meta['not_diagnosed']})"
assert not extra, f"対象外のアルバムが診断に含まれた: {extra}"
assert not bad, f"診断の結果が期待と違う: {bad}"
print("   診断OK:", {v['album']: v['status'] for v in exp.values()})
PY

echo "== 2. 修復"; $BIN fix --work "$W" --yes | tail -2
echo "== 3. 再診断（修復後は、直せるものが0になるはず）"; $BIN diagnose --work "$W" --reuse | tail -3
python3 - "$T" "$W" <<'PY'
import json, sys
T, W = sys.argv[1:3]
exp = json.load(open(f"{T}/expected.json"))["expected"]
plan = {e["token"]: e["status"] for e in json.load(open(f"{W}/plan.json"))["entries"]}
for t, v in exp.items():
    want = {"noEmbeddedArt": "noEmbeddedArt", "custom": "custom"}.get(v["status"], "same")
    assert plan[t] == want, f"修復後の状態が違う: {v['album']} {plan[t]} (期待 {want})"
print("   修復後OK")
PY

echo "== 4. 元に戻す"; $BIN restore --work "$W" --yes | tail -1
python3 - "$T" <<'PY'
import json, sys, hashlib, sqlite3, os
T = sys.argv[1]
d = json.load(open(f"{T}/expected.json")); exp, init = d["expected"], d["initial"]
c = sqlite3.connect(f"{T}/iphone/iTunes_Control/iTunes/MediaLibrary.sqlitedb")
for tok, v in exp.items():
    rel = c.execute("select relative_path from artwork where artwork_token=?", (tok,)).fetchone()[0]
    sha = hashlib.sha256(open(f"{T}/iphone/iTunes_Control/iTunes/Artwork/Originals/{rel}", "rb").read()).hexdigest()
    if v["status"] == "wrong":
        assert sha == init[tok], f"元に戻っていない: {v['album']}"
    else:
        assert sha == init[tok], f"触れてはいけないファイルが変わった: {v['album']}"
print("   取り消しOK（誤りだったものは元の状態に、正しかったものは変更なし）")
PY

echo "== 5. 安全装置（書き込み制限）"
set +e
./build/afcfs put - "$T/images/A.jpg" /iTunes_Control/iTunes/MediaLibrary.sqlitedb >/dev/null 2>&1; r1=$?
./build/afcfs put - "$T/images/A.jpg" /iTunes_Control/iTunes/Artwork/Originals/zz/none >/dev/null 2>&1; r2=$?
./build/afcfs put - "$T/images/A.jpg" "/iTunes_Control/iTunes/Artwork/Originals/../MediaLibrary.sqlitedb" >/dev/null 2>&1; r3=$?
set -e
[ $r1 -ne 0 ] && [ $r2 -ne 0 ] && [ $r3 -ne 0 ] && echo "   DB本体・新規作成・../ を使った抜け道への書き込みは、すべて拒否された" || { echo "   NG: 書き込み制限が効いていない"; exit 1; }
echo "== 6. 異常系（ライブラリ情報が無い iPhone）"
EMPTY="$T/empty-iphone"; mkdir -p "$EMPTY"
set +e
msg=$(COVERDOCTOR_FAKE_ROOT="$EMPTY" $BIN diagnose --work "$T/work-empty" 2>&1); rc=$?
set -e
[ $rc -ne 0 ] && echo "   エラー終了（コード ${rc}）、メッセージ: $msg" || { echo "   NG: エラーにならなかった"; exit 1; }
echo "== 7. 修復の事前確認なしの実行は拒否される"
set +e; $BIN fix --work "$W" >/dev/null 2>&1; rc=$?; set -e
[ $rc -ne 0 ] && echo "   --yes なしの fix は実行されない" || { echo "   NG"; exit 1; }
echo "すべて成功"
