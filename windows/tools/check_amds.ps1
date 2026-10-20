# Apple Mobile Device Support（usbmuxd、TCP 127.0.0.1:27015）に、読み取りだけの問い合わせをする確認用スクリプト。
# iPhone には何も書き込まない。ペアリング記録は「読めたかどうか」と大きさだけを表示し、中身（鍵）は表示も保存もしない。
# 使い方（PowerShell）: powershell -ExecutionPolicy Bypass -File check_amds.ps1
$ErrorActionPreference = 'Stop'
function Send-Muxd([string]$type, [string]$extra = '') {
    $xml = "<?xml version=`"1.0`" encoding=`"UTF-8`"?><!DOCTYPE plist PUBLIC `"-//Apple//DTD PLIST 1.0//EN`" `"http://www.apple.com/DTDs/PropertyList-1.0.dtd`"><plist version=`"1.0`"><dict><key>MessageType</key><string>$type</string><key>ClientVersionString</key><string>coverdoctor-check</string><key>ProgName</key><string>coverdoctor-check</string><key>kLibUSBMuxVersion</key><integer>3</integer>$extra</dict></plist>"
    $body = [Text.Encoding]::UTF8.GetBytes($xml)
    $c = New-Object Net.Sockets.TcpClient
    $c.Connect('127.0.0.1', 27015)
    $s = $c.GetStream()
    $hdr = [BitConverter]::GetBytes([uint32](16 + $body.Length)) + [BitConverter]::GetBytes([uint32]1) + [BitConverter]::GetBytes([uint32]8) + [BitConverter]::GetBytes([uint32]1)
    $s.Write($hdr, 0, 16); $s.Write($body, 0, $body.Length)
    $h = New-Object byte[] 16; $n = 0; while ($n -lt 16) { $n += $s.Read($h, $n, 16 - $n) }
    $len = [BitConverter]::ToUInt32($h, 0) - 16
    $buf = New-Object byte[] $len; $n = 0; while ($n -lt $len) { $n += $s.Read($buf, $n, $len - $n) }
    $c.Close()
    return [xml]([Text.Encoding]::UTF8.GetString($buf) -replace '<!DOCTYPE[^>]*>', '')
}
try { $os = Get-CimInstance Win32_OperatingSystem; "Windows: $($os.Caption) $($os.Version) build $($os.BuildNumber)" } catch { "OS: $([Environment]::OSVersion.VersionString)" }
$ErrorActionPreference = 'Continue'
$svc = Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*Apple Mobile Device*' -or $_.DisplayName -like '*Apple Mobile Device*' }
if ($svc) { $svc | ForEach-Object { "Service: $($_.DisplayName) [$($_.Status)]" } } else { "Service: Apple Mobile Device Service not found" }
Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -match 'Apple Mobile Device Support|iTunes' } | ForEach-Object { "Installed: $($_.DisplayName) $($_.DisplayVersion)" }
Get-AppxPackage -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'AppleDevices|AppleInc' } | ForEach-Object { "Store app: $($_.Name) $($_.Version)" }
try { $l = Get-NetTCPConnection -LocalPort 27015 -State Listen -ErrorAction Stop; "Port 27015: listening on $($l.LocalAddress -join ', ')" } catch { "Port 27015: not listening" }
$ErrorActionPreference = 'Stop'
try {
    $r = Send-Muxd 'ListDevices'
    $udids = @($r.SelectNodes("//key[.='SerialNumber']/following-sibling::string[1]") | ForEach-Object { $_.InnerText })
    $types = @($r.SelectNodes("//key[.='ConnectionType']/following-sibling::string[1]") | ForEach-Object { $_.InnerText })
    "ListDevices: OK, $($udids.Count) device(s), connection: $($types -join ', ')"
    foreach ($u in ($udids | Select-Object -Unique)) {
        $masked = $u.Substring(0, [Math]::Min(4, $u.Length)) + '...'
        $p = Send-Muxd 'ReadPairRecord' "<key>PairRecordID</key><string>$u</string>"
        $data = $p.SelectSingleNode("//key[.='PairRecordData']/following-sibling::data[1]")
        if ($data) { "ReadPairRecord ($masked): OK, $([Convert]::FromBase64String(($data.InnerText -replace '\s','')).Length) bytes (contents not shown)" }
        else { $num = $p.SelectSingleNode("//key[.='Number']/following-sibling::integer[1]"); "ReadPairRecord ($masked): not available (Result $($num.InnerText))" }
    }
} catch { "ListDevices: FAILED ($($_.Exception.Message))" }
