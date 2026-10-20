# 偽の iPhone を使った、診断 → 修復 → 再診断 → 元に戻す の通しテスト（Mac 版 Tests/run_e2e.sh と同じ内容の PowerShell 版）。
# 実機には一切触れない（COVERDOCTOR_FAKE_ROOT があるとき、afcfs.exe は iPhone に接続しない）。
# 必要なもの: Python 3（py または python）と ffmpeg（PATH に入っていること）。
# 使い方（PowerShell、このフォルダの1つ上で）:
#   powershell -ExecutionPolicy Bypass -File tests\run_e2e.ps1 -App <CoverDoctor.exe のフォルダ>
#   追加のアルバムで規模を試す: ... -Scale 150     画面の自己テストも行う: ... -Gui
param(
    [Parameter(Mandatory = $true)][string]$App,
    [int]$Scale = 0,
    [switch]$Gui,
    [string]$Bin,      # 開発機用: コマンド版のパス（既定: <App>\CoverDoctor.exe）
    [string]$Afcfs     # 開発機用: afcfs のパス（既定: <App>\afcfs.exe）
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Bin) { $Bin = Join-Path $App 'CoverDoctor.exe' }
if (-not $Afcfs) { $Afcfs = Join-Path $App 'afcfs.exe' }
$Py = $null
foreach ($n in @('py', 'python3', 'python')) { $c = Get-Command $n -ErrorAction SilentlyContinue; if ($c -and -not $Py) { $Py = $c.Source } }
if (-not $Py) { throw 'Python が見つかりません（py / python3 / python）' }
# Windows PowerShell 5.1 では、外部コマンドの標準エラー出力が「エラー」扱いになるため、ここからは自分で終了コードを見る
$ErrorActionPreference = 'Continue'
$T = Join-Path ([IO.Path]::GetTempPath()) 'coverdoctor-e2e'
$W = Join-Path $T 'work'

function Run([string[]]$a) {
    $ErrorActionPreference = 'Continue'
    # コマンド版を実行し、出力を返す。終了コードは $script:rc に入れる
    $out = & $Bin @a 2>&1 | Out-String
    $script:rc = $LASTEXITCODE
    return $out
}
function Check([string]$step) {
    & $Py (Join-Path $Root 'tests\check_plan.py') $step $T $W
    if ($LASTEXITCODE -ne 0) { throw "確認に失敗: $step" }
}
function Tail([string]$s, [int]$n) { ($s.TrimEnd() -split "`r?`n" | Select-Object -Last $n) -join "`n" }

& $Py (Join-Path $Root 'tests\make_fixture.py') $T $Scale
if ($LASTEXITCODE -ne 0) { throw '偽の iPhone を作れませんでした（ffmpeg が必要です）' }
$env:COVERDOCTOR_FAKE_ROOT = Join-Path $T 'iphone'
$env:COVERDOCTOR_AFCFS = $Afcfs
if (-not $env:COVERDOCTOR_LANG) { $env:COVERDOCTOR_LANG = 'ja' }

"== 1. 診断"; Tail (Run @('diagnose', '--work', $W)) 4; Check 'diagnose'
"== 2. 修復"; Tail (Run @('fix', '--work', $W, '--yes')) 3
"== 3. 再診断（修復後は、直せるものが0になるはず）"; Tail (Run @('diagnose', '--work', $W, '--reuse')) 3; Check 'after-fix'
"== 4. 元に戻す"; Tail (Run @('restore', '--work', $W, '--yes')) 1; Check 'after-restore'

"== 5. 安全装置（書き込み制限）"
$A = Join-Path $T 'images\A.jpg'
& $Afcfs put - $A /iTunes_Control/iTunes/MediaLibrary.sqlitedb 2>$null | Out-Null; $r1 = $LASTEXITCODE
& $Afcfs put - $A /iTunes_Control/iTunes/Artwork/Originals/zz/none 2>$null | Out-Null; $r2 = $LASTEXITCODE
& $Afcfs put - $A '/iTunes_Control/iTunes/Artwork/Originals/../MediaLibrary.sqlitedb' 2>$null | Out-Null; $r3 = $LASTEXITCODE
if ($r1 -ne 0 -and $r2 -ne 0 -and $r3 -ne 0 -and -not (Test-Path (Join-Path $T 'iphone/iTunes_Control/iTunes/Artwork/Originals/zz/none'))) {
    "   DB本体・新規作成・../ を使った抜け道への書き込みは、すべて拒否された"
} else { throw '書き込み制限が効いていない' }

"== 6. 異常系（ライブラリ情報が無い iPhone）"
$empty = Join-Path $T 'empty-iphone'; New-Item -ItemType Directory -Force $empty | Out-Null
$keep = $env:COVERDOCTOR_FAKE_ROOT; $env:COVERDOCTOR_FAKE_ROOT = $empty
$msg = Run @('diagnose', '--work', (Join-Path $T 'work-empty'))
$env:COVERDOCTOR_FAKE_ROOT = $keep
if ($rc -ne 0) { "   エラー終了（コード $rc）、メッセージ: $($msg.Trim())" } else { throw 'エラーにならなかった' }

"== 7. 修復の事前確認なしの実行は拒否される"
Run @('fix', '--work', $W) | Out-Null
if ($rc -ne 0) { "   --yes なしの fix は実行されない" } else { throw '--yes なしで実行された' }

"== 8. 診断のあとに iPhone 側で画像が変わったものは、書き込まない（Windows 版で追加した安全装置）"
Run @('diagnose', '--work', $W) | Out-Null
Check 'tamper'
Run @('fix', '--work', $W, '--yes') | Out-Null; $fixrc = $rc
Check 'after-tamper'
if ($fixrc -ne 0) { "   変わっていた1件は書き込まず、終了コード $fixrc で知らせた" } else { throw '終了コードが0' }
Run @('restore', '--work', $W, '--yes') | Out-Null

if ($Gui) {
    "== 9. 画面の自己テスト（ボタンと同じ経路で、診断 → 修復 → 再診断 → 元に戻す）"
    & $Py (Join-Path $Root 'tests\make_fixture.py') $T 0 | Out-Null
    $out = Join-Path $T 'gui-selftest.txt'
    $env:COVERDOCTOR_SELFTEST_OUT = $out
    $p = Start-Process -FilePath (Join-Path $App 'CoverDoctor.exe') -PassThru -Wait
    Remove-Item Env:COVERDOCTOR_SELFTEST_OUT
    Get-Content $out
    if ($p.ExitCode -ne 0) { throw '画面の自己テストに失敗' }
}
"すべて成功"
