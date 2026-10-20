# run_e2e.sh から読み込む、テストの本体。$T $W $BIN $AFCFS が設定済みであること。
echo "== 1. 診断"; $BIN diagnose --work "$W" | tail -4
python3 tests/check_plan.py diagnose "$T" "$W"
echo "== 2. 修復"; $BIN fix --work "$W" --yes | tail -3
echo "== 3. 再診断（修復後は、直せるものが0になるはず）"; $BIN diagnose --work "$W" --reuse | tail -3
python3 tests/check_plan.py after-fix "$T" "$W"
echo "== 4. 元に戻す"; $BIN restore --work "$W" --yes | tail -1
python3 tests/check_plan.py after-restore "$T" "$W"
echo "== 5. 安全装置（書き込み制限）"
set +e
$AFCFS put - "$T/images/A.jpg" /iTunes_Control/iTunes/MediaLibrary.sqlitedb >/dev/null 2>&1; r1=$?
$AFCFS put - "$T/images/A.jpg" /iTunes_Control/iTunes/Artwork/Originals/zz/none >/dev/null 2>&1; r2=$?
$AFCFS put - "$T/images/A.jpg" "/iTunes_Control/iTunes/Artwork/Originals/../MediaLibrary.sqlitedb" >/dev/null 2>&1; r3=$?
set -e
[ $r1 -ne 0 ] && [ $r2 -ne 0 ] && [ $r3 -ne 0 ] && [ ! -e "$T/iphone/iTunes_Control/iTunes/Artwork/Originals/zz/none" ] \
  && echo "   DB本体・新規作成・../ を使った抜け道への書き込みは、すべて拒否された" || { echo "   NG: 書き込み制限が効いていない"; exit 1; }
echo "== 6. 異常系（ライブラリ情報が無い iPhone）"
EMPTY="$T/empty-iphone"; mkdir -p "$EMPTY"
set +e
msg=$(COVERDOCTOR_FAKE_ROOT="$EMPTY" $BIN diagnose --work "$T/work-empty" 2>&1); rc=$?
set -e
[ $rc -ne 0 ] && echo "   エラー終了（コード ${rc}）、メッセージ: $msg" || { echo "   NG: エラーにならなかった"; exit 1; }
echo "== 7. 修復の事前確認なしの実行は拒否される"
set +e; $BIN fix --work "$W" >/dev/null 2>&1; rc=$?; set -e
[ $rc -ne 0 ] && echo "   --yes なしの fix は実行されない" || { echo "   NG"; exit 1; }
echo "== 8. 診断のあとに iPhone 側で画像が変わったものは、書き込まない（Windows 版で追加した安全装置）"
$BIN diagnose --work "$W" >/dev/null
python3 tests/check_plan.py tamper "$T" "$W"
set +e; $BIN fix --work "$W" --yes > "$T/fix8.txt"; rc=$?; set -e
python3 tests/check_plan.py after-tamper "$T" "$W"
[ $rc -ne 0 ] && echo "   変わっていた1件は書き込まず、終了コード ${rc} で知らせた" || { echo "   NG: 終了コードが0"; exit 1; }
$BIN restore --work "$W" --yes >/dev/null
echo "すべて成功"
