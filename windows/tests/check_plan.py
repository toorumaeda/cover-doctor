#!/usr/bin/env python3
"""通しテストの確認（Mac 版 Tests/run_e2e.sh の中の Python と同じ判定）。bash と PowerShell の両方から使う。
使い方: check_plan.py <diagnose|after-fix|after-restore|tamper|after-tamper> <テストフォルダ> <作業フォルダ>"""
import json, sys, hashlib, sqlite3, os
step, T, W = sys.argv[1:4]
meta = json.load(open(os.path.join(T, "expected.json"), encoding="utf-8"))
exp, init = meta["expected"], meta["initial"]
root = os.path.join(T, "iphone")
db = sqlite3.connect(os.path.join(root, "iTunes_Control", "iTunes", "MediaLibrary.sqlitedb"))
def orig_path(tok):
    rel = db.execute("select relative_path from artwork where artwork_token=?", (tok,)).fetchone()[0]
    return os.path.join(root, "iTunes_Control", "iTunes", "Artwork", "Originals", *rel.split("/"))
def sha(p): return hashlib.sha256(open(p, "rb").read()).hexdigest()
def plan(): return json.load(open(os.path.join(W, "plan.json"), encoding="utf-8"))

if step == "diagnose":
    full = plan(); p = {e["token"]: e for e in full["entries"]}
    bad = [(t, v["album"], v["status"], p.get(t, {}).get("status")) for t, v in exp.items() if p.get(t, {}).get("status") != v["status"] and v["status"] != "none"]
    extra = set(p) - set(exp)
    assert full["libraryAlbumCount"] == meta["library_albums"], f"ライブラリのアルバム総数が違う: {full['libraryAlbumCount']} (期待 {meta['library_albums']})"
    assert full["notDiagnosedCount"] == meta["not_diagnosed"], f"診断対象外の数が違う: {full['notDiagnosedCount']} (期待 {meta['not_diagnosed']})"
    assert not extra, f"対象外のアルバムが診断に含まれた: {extra}"
    assert not bad, f"診断の結果が期待と違う: {bad}"
    print("   診断OK:", {v['album']: v['status'] for v in exp.values() if v['status'] != 'none'})
elif step == "after-fix":
    p = {e["token"]: e["status"] for e in plan()["entries"]}
    for t, v in exp.items():
        if v["status"] == "none": continue
        want = {"noEmbeddedArt": "noEmbeddedArt", "custom": "custom"}.get(v["status"], "same")
        assert p[t] == want, f"修復後の状態が違う: {v['album']} {p[t]} (期待 {want})"
    print("   修復後OK")
elif step == "after-restore":
    for tok, v in exp.items():
        if v["status"] == "none": continue
        if v["status"] == "wrong": assert sha(orig_path(tok)) == init[tok], f"元に戻っていない: {v['album']}"
        else: assert sha(orig_path(tok)) == init[tok], f"触れてはいけないファイルが変わった: {v['album']}"
    assert not os.path.exists(os.path.join(W, "applied.json")), "元に戻したあとも履歴が残っている"
    print("   取り消しOK（誤りだったものは元の状態に、正しかったものは変更なし）")
elif step == "tamper":
    # 「直せる」のうち1件を、診断のあとで別の画像に差し替える（同期で iPhone 側が変わった状況のまね）
    wrong = sorted(e["token"] for e in plan()["entries"] if e["status"] == "wrong" and exp[e["token"]].get("kind") != "playlist")
    tok = wrong[0]
    open(os.path.join(W, "tampered.txt"), "w").write(tok)
    with open(os.path.join(T, "images", "H.jpg"), "rb") as src, open(orig_path(tok), "wb") as dst: dst.write(src.read())
    open(os.path.join(W, "tampered.sha"), "w").write(sha(orig_path(tok)))
elif step == "after-tamper":
    tok = open(os.path.join(W, "tampered.txt")).read()
    assert sha(orig_path(tok)) == open(os.path.join(W, "tampered.sha")).read(), "診断のあとに変わった画像を、上書きしてしまった"
    applied = {e["token"] for e in json.load(open(os.path.join(W, "applied.json"), encoding="utf-8"))}
    assert tok not in applied, "上書きしていないのに、取り消しの履歴に入っている"
    # 元に戻せるように、元の画像に戻しておく（テスト後の restore が、他の分を戻す）
    print("   診断のあとに変わった画像は、上書きしなかった")
