# Cover Doctor — Windows 版

Mac 版（このリポジトリの本体）と同じ動作・同じ安全装置の Windows 版です。版番号は Mac 版とそろえています。配布用の zip は、[リリース](https://github.com/toorumaeda/cover-doctor/releases) の `CoverDoctor-<版>-Windows-x64.zip` です（0.1.1 から）。

## 確認した環境（作者の 1 組だけ）

| | 確認した値 |
|---|---|
| PC | Windows 11 Pro（ビルド 26200 と 26300） |
| Apple のソフト | Microsoft Store 版の iTunes 12139.11001.62009.0 と「Apple デバイス」1.1540.24088.0 |
| iPhone | iOS 27.0、アルバム 729 件（画像の記録のあるもの 515 件） |
| 診断にかかった時間 | 初回 約 6 分、2 回目以降 約 3 分 |

## 状態（2026-10-20）

| | 状態 |
|---|---|
| 診断・修復・元に戻す（エンジン） | 実装済み。偽の iPhone での通しテストに合格（追加 150 アルバムでも） |
| 画面（WPF、日英） | 実装済み。Windows 用にビルド済み。画面の状態（AppModel）の自己テストは合格。**画面そのものは Windows で未表示**（Wine では WPF の文字描画が動かず、確かめられなかった） |
| 補助ツール afcfs.exe | Mac 版から移植。Windows 用にビルド済み。Wine 上で、偽の iPhone の通しテストに合格（日本語・空白入りのフォルダ）。Windows と同じ経路（TCP 127.0.0.1:27015）で端末一覧を受け取れることを、Apple のサービスのまねで確認 |
| 配布 zip | `tools/make_release.sh` で作成（約 70 MB。ほとんどが .NET を含む CoverDoctor.exe）。配布用の `CoverDoctor.exe` と `afcfs.exe` で、Wine 上の通しテストに合格 |
| しきい値 70 | 合成画像で再測定（`docs/THRESHOLD.md`）。写真に近い画像で Mac と同じ分布。実機の画像では未測定 |
| 実機（Windows の PC + iPhone） | 2026-10-18 に確認: 診断（読み取り）、試験用の入れ替え 10 件 → 診断で同じ 10 件を検出 → 直す。ミュージックで、入れ替わった絵と直った絵を目で確認。2026-10-19 に、0.1.0-test3 で「元に戻す」も確認（入れ替えた絵に戻り、再診断で同じ 10 件を検出 → もう一度直して 0 件）。2026-10-20 に、0.1.0-test4 でプレイリスト 3 件の修復も確認。手順は `docs/REAL_DEVICE_CHECK.md` |

## 仕組み（Windows で違うところ）
- iPhone との通信: libimobiledevice（Mac 版と同じ版、LGPL の DLL として同梱）→ Apple Mobile Device Support（iTunes または「Apple デバイス」アプリに含まれる）の **TCP 127.0.0.1:27015**。ペアリングの記録（「このコンピュータを信頼」）も、このサービスから読む。つながらないときは、画面に「Apple Mobile Device Support に接続できません」と出る。
- 埋め込みジャケットの取り出し: MP3（ID3v2 の APIC/PIC）と M4A（covr）を直接読む（`EmbeddedArt.cs`）。表紙（種類3）を優先。
- 画像: SkiaSharp で読み込みと JPEG 化（長辺 1200・品質 0.9、拡大はしない）。dHash の縮小は自前の面積平均。
- SQLite: Microsoft.Data.Sqlite、読み取り専用。SQL は Mac 版と同じ。
- 作業フォルダ: `%APPDATA%\CoverRepair\<UDID>\`（Mac 版の `~/Library/Application Support/CoverRepair/<UDID>/` と同じ構成: `originals/`, `current/`, `fresh/`, `plan.json`, `applied.json`）。
- `CoverDoctor.exe` に引数を付けると、コマンド版として動く（`diagnose` / `fix --yes` / `restore --yes` / `report` / `version`。出力を見るには `| Out-String` を付ける）。
- 書き込みの試験用コマンド `testswap`（正しいジャケットをわざと入れ替える）は、テスト版（`VERSION=<版>-testN bash tools/make_release.sh`）にだけ入る。正式版には入らない。

## Mac 版と変えたところ（2026-10-20 に作者が承認）
1. **修復の直前に、iPhone 上の画像が診断のときと同じかを確かめる。** 違っていたら（診断のあとに同期した等）、書き込まずに「もう一度診断してください」と知らせる。iPhone をつなぎっぱなしにできない前提で、診断と修復の間が空くことを考えた。
2. **「元に戻す」で書き戻せなかったものは、履歴に残す**（Mac 版は、失敗しても履歴を消す）。もう一度「元に戻す」で再試行できる。
3. **画面の「診断する」は、取り出し済みの曲の画像を使い回す**（Mac 版は、最初の「診断する」だけ毎回取り出し直し、「もう一度診断する」は使い回す）。途中で接続が切れても、続きから再開できるようにするため。曲のジャケットを差し替えた直後は、古い画像が使われる点が欠点。
4. 補助ツールの `devices` が、Apple のサービスにつながらないとき、終了コード 3 と `MUXD\tUNAVAILABLE` を返す（画面の案内用）。
5. 補助ツールの呼び出しに、1回 5 分の上限を付けた（ケーブルが抜けたときに、画面が止まったままにならないように）。

## フォルダ
```
src/CoverDoctor.Core/   エンジン（Engine, Library, Imaging, EmbeddedArt, Afc, AppModel, Cli, L=文言）
src/CoverDoctor.App/    画面（WPF。コードで組み立て）→ CoverDoctor.exe
src/CoverDoctor.Cli/    開発機用のコマンド版（coverdoctor）
native/afcfs.c          補助ツール（Mac 版からの移植。Windows の UTF-16 のパス、偽 iPhone 専用ビルド AFCFS_FAKE_ONLY）
tests/                  偽 iPhone の通しテスト（run_e2e.sh / run_e2e.ps1、make_fixture.py は Mac 版そのまま）、fake_usbmuxd.py
tools/                  build_libs_mingw.sh, make_release.sh, measure_threshold.py, check_amds.ps1
docs/                   BUILD.md, THRESHOLD.md, REAL_DEVICE_CHECK.md, README-Windows.txt（zip に入る説明）
```

## まだのこと
- README 用の画像（SmartScreen の警告、はじめに、診断の結果）。
- 作者の 1 組以外の環境（別の Windows、iTunes のデスクトップ版など）での確認。
