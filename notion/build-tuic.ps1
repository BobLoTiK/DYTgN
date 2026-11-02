param([int]$MaxNodes = 60, [switch]$DryRun)
# Collects Hysteria2 / TUIC nodes, probes them with sing-box, keeps the first one
# that really opens Notion, writes sb-config.json, restarts the local tunnel.
# Local only: SOCKS 127.0.0.1:10808 + HTTP 127.0.0.1:10809. No system changes.

$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sb = @(
    (Join-Path $dir '..\bin\sing-box.exe'),
    (Join-Path $dir 'sing-box.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $sb) { Write-Host 'sing-box.exe not found (expected ..\bin\sing-box.exe)'; exit 1 }
$tmp = $env:TEMP

function Parse-Uri([string]$u) {
    $u = $u -replace '&amp;', '&'
    $scheme = ($u -split '://')[0]
    $body = $u.Substring($scheme.Length + 3)
    $name = ''
    $h = $body.IndexOf('#')
    if ($h -ge 0) { $name = [System.Uri]::UnescapeDataString($body.Substring($h + 1)); $body = $body.Substring(0, $h) }
    $q = $null
    $qi = $body.IndexOf('?')
    if ($qi -ge 0) { $q = $body.Substring($qi + 1); $body = $body.Substring(0, $qi) }
    $body = ($body -split '/')[0]
    $at = $body.LastIndexOf('@')
    if ($at -lt 0) { return $null }
    $auth = $body.Substring(0, $at)
    $hp = $body.Substring($at + 1)
    $ci = $hp.LastIndexOf(':')
    if ($ci -lt 1) { return $null }
    $server = $hp.Substring(0, $ci).Trim('[', ']')
    $port = 0
    if (-not [int]::TryParse($hp.Substring($ci + 1), [ref]$port)) { return $null }
    $p = @{}
    if ($q) {
        foreach ($pair in $q.Split('&')) {
            if (-not $pair) { continue }
            $kv = $pair.Split('=', 2)
            $p[[System.Uri]::UnescapeDataString($kv[0])] = if ($kv.Count -gt 1) { [System.Uri]::UnescapeDataString($kv[1]) } else { '' }
        }
    }
    [pscustomobject]@{ Scheme = $scheme; Name = $name; Auth = $auth; Server = $server; Port = $port; P = $p }
}

function New-Outbound($n) {
    $tls = [ordered]@{ enabled = $true; server_name = $(if ($n.P['sni']) { $n.P['sni'] } else { $n.Server }) }
    if ($n.P['insecure'] -eq '1' -or $n.P['allowInsecure'] -eq '1') { $tls.insecure = $true }
    if ($n.P['alpn']) { $tls.alpn = @($n.P['alpn'].Split(',')) }
    if ($n.Scheme -in @('hysteria2', 'hy2')) {
        $o = [ordered]@{ type = 'hysteria2'; tag = 'proxy'; server = $n.Server; server_port = $n.Port; password = $n.Auth; tls = $tls }
        if ($n.P['obfs'] -eq 'salamander') { $o.obfs = [ordered]@{ type = 'salamander'; password = $n.P['obfs-password'] } }
        return $o
    }
    $parts = $n.Auth.Split(':', 2)
    $o = [ordered]@{ type = 'tuic'; tag = 'proxy'; server = $n.Server; server_port = $n.Port; uuid = $parts[0]; password = $(if ($parts.Count -gt 1) { $parts[1] } else { '' }); tls = $tls }
    if ($n.P['congestion_control']) { $o.congestion_control = $n.P['congestion_control'] }
    if ($n.P['udp_relay_mode']) { $o.udp_relay_mode = $n.P['udp_relay_mode'] }
    return $o
}

function New-FullConfig($node) {
    [ordered]@{
        log       = [ordered]@{ level = 'warn' }
        inbounds  = @(
            [ordered]@{ type = 'socks'; tag = 'in-socks'; listen = '127.0.0.1'; listen_port = 10808 },
            [ordered]@{ type = 'http'; tag = 'in-http'; listen = '127.0.0.1'; listen_port = 10809 }
        )
        outbounds = @(New-Outbound $node)
    }
}

# ---- collect nodes ----
$repos = @(
    'MohammadBahemmat/V2ray-Collector',
    'penhandev/AutoAiVPN',
    'ALIILAPRO/v2rayNG-Config',
    'SoliSpirit/v2ray-configs',
    'iboxz/free-v2ray-collector',
    'ProblemTheCode/SylphNet-public',
    'amirkma/proxykma',
    '0xRadikal/Free-v2ray-Configs',
    'alireza786786/Free-V2ray-Configs'
)
$uris = New-Object System.Collections.Generic.List[string]
foreach ($repo in $repos) {
    try {
        $items = Invoke-RestMethod "https://api.github.com/repos/$repo/contents" -Headers @{ 'User-Agent' = 'ps' } -TimeoutSec 20
        $files = $items | Where-Object { $_.type -eq 'file' -and $_.name -match '\.txt$' -and $_.size -gt 2000 } | Sort-Object size -Descending | Select-Object -First 3
        foreach ($f in $files) {
            try {
                $c = (Invoke-WebRequest -Uri $f.download_url -TimeoutSec 45 -UseBasicParsing).Content
                if ($c -notmatch '://') { try { $c = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($c -replace '\s', ''))) } catch { } }
                foreach ($l in ($c -split "`r?`n")) {
                    if ($l -match '^(hysteria2|hy2|tuic)://') { $uris.Add($l.Trim()) }
                }
            }
            catch { }
        }
    }
    catch { }
}
$nodes = @($uris | Sort-Object -Unique | ForEach-Object { Parse-Uri $_ } | Where-Object { $_ } | Select-Object -First $MaxNodes)
Write-Host ("hy2/tuic candidates: {0}" -f $nodes.Count)

$winner = $null
$port = 10950
foreach ($n in $nodes) {
    $probeCfg = Join-Path $tmp 'sb_probe.json'
    $json = [ordered]@{
        log       = [ordered]@{ level = 'error' }
        inbounds  = @([ordered]@{ type = 'socks'; tag = 'in'; listen = '127.0.0.1'; listen_port = $port })
        outbounds = @(New-Outbound $n)
    } | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($probeCfg, $json, (New-Object System.Text.UTF8Encoding($false)))
    $p = Start-Process -FilePath $sb -ArgumentList @('run', '-c', "`"$probeCfg`"") -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $tmp 'sb_o.log') -RedirectStandardError (Join-Path $tmp 'sb_e.log')
    Start-Sleep -Seconds 3
    $alive = & curl.exe -s -o NUL -w "%{http_code}" --max-time 7 --socks5-hostname "127.0.0.1:$port" 'https://www.gstatic.com/generate_204' 2>$null
    $notion = ''
    if ($alive -eq '204') { $notion = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 12 --socks5-hostname "127.0.0.1:$port" 'https://www.notion.com/' 2>$null }
    Write-Host ("{0,-20} {1}:{2} alive={3} notion={4}" -f $n.Scheme, $n.Server, $n.Port, $alive, $(if ($notion) { $notion } else { '-' }))
    try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
    if ($notion -eq '200') { $winner = $n; break }
    Start-Sleep -Milliseconds 300
}

if (-not $winner) { Write-Host 'no working hy2/tuic node right now'; exit 1 }
Write-Host ("WINNER: {0} {1}:{2}" -f $winner.Scheme, $winner.Server, $winner.Port)

$cfgPath = Join-Path $dir 'sb-config.json'
if (Test-Path $cfgPath) { Copy-Item $cfgPath "$cfgPath.bak" -Force }
$final = (New-FullConfig $winner) | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($cfgPath, $final, (New-Object System.Text.UTF8Encoding($false)))
Write-Host 'sb-config.json written'

if (-not $DryRun) {
    Get-Process sing-box,xray -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    Start-Process -FilePath 'wscript.exe' -ArgumentList @("`"$dir\hidden.vbs`"") -WindowStyle Hidden
    Start-Sleep -Seconds 5
    $check = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 20 --socks5-hostname 127.0.0.1:10808 'https://www.notion.com/' 2>$null
    Write-Host ("self-check: notion.com -> {0}" -f $check)
}
