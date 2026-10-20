Cover Doctor for Windows 0.1.0 (early version, unofficial)
==========================================================

[English]

Fixes the problem where the Music app on your iPhone shows another album's cover
after you sync music. It overwrites only the wrong cover image files inside the
iPhone with the image embedded in your song files. It never writes to the
database, your songs, or your PC's library. Replaced images are saved on this PC
so you can undo.

Status: this Windows version has NOT been verified on a real iPhone yet.
Automated tests run against a fake iPhone only. Use at your own risk.

You need
  - Windows 10 (22H2) or 11, 64-bit (x64)
  - iTunes for Windows, or the Apple Devices app (Microsoft Store).
    Cover Doctor uses Apple's "Apple Mobile Device Support" that comes with them
    to talk to the iPhone over USB.

How to use
  1. Unzip the whole folder (keep all files together).
  2. Connect your iPhone with a USB cable, unlock it, choose "Trust This Computer".
  3. Double-click CoverDoctor.exe. Windows SmartScreen warns because the program
     is not signed: press "More info", then "Run anyway" (only the first time).
  4. Press Diagnose (read only), check the list, press Fix.
  5. Fully quit the Music app on the iPhone and open it again.

Your data is in %APPDATA%\CoverRepair\<device ID>\ (backups of replaced images,
the list used by Undo).

Files
  CoverDoctor.exe     the app (also a command-line tool: CoverDoctor.exe diagnose | Out-String)
  afcfs.exe           helper that reads/writes the iPhone's media area; it can only
                      overwrite existing cover images
  lib*.dll            libimobiledevice libraries (LGPL-2.1, replaceable; see THIRD_PARTY_NOTICES.md)
  check_amds.ps1      read-only check of Apple Mobile Device Support (for troubleshooting)

Source and reports: https://github.com/toorumaeda/cover-doctor

[日本語]

iPhone のミュージックで、同期のあとに別のアルバムのジャケットが表示される問題を
直します。iPhone 内の誤ったジャケット画像ファイルだけを、曲ファイルに入っている
画像で上書きします。データベース、曲ファイル、PC のライブラリには書き込みません。
置き換える前の画像はこの PC に保存するので、元に戻せます。

状態: この Windows 版は、まだ実機の iPhone で確認していません。自動テストは
偽の iPhone でだけ行っています。自己責任でお使いください。

必要なもの
  - Windows 10（22H2）または 11、64ビット（x64）
  - iTunes for Windows、または「Apple デバイス」アプリ（Microsoft Store）。
    これに含まれる Apple の「Apple Mobile Device Support」を使って、USB で
    iPhone と通信します。

使い方
  1. フォルダごと解凍します（ファイルは同じ場所にまとめておいてください）。
  2. iPhone を USB でつなぎ、ロックを解除して「このコンピュータを信頼」を選びます。
  3. CoverDoctor.exe をダブルクリックします。署名がないため、Windows の SmartScreen が
     警告します。「詳細情報」を押し、「実行」を押します（初回だけ）。
  4. 「診断する」（読み取りのみ）→ 一覧を確認 →「直す」。
  5. iPhone のミュージックアプリを完全に終了して、開き直します。

作業データは %APPDATA%\CoverRepair\<端末のID>\ にあります（置き換える前の画像の
バックアップ、「元に戻す」に使う一覧）。

ソースと報告: https://github.com/toorumaeda/cover-doctor
