param(
    [string]$Source = "",
    [int]$MaxNodes = 120,
    [int]$Batch = 4,
    [int]$WantWinners = 6,
    [switch]$DryRun
)
# Builds config.json from several public VLESS nodes that really open Notion,
# with an Xray leastPing balancer so dead nodes drop out automatically.

$ErrorActionPreference = 'Stop'
$dir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$xray = @(
    (Join-Path $dir '..\bin\xray.exe'),
    (Join-Path $dir 'xray.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
$tmp  = $env:TEMP

function Parse-Vless([string]$uri) {
    $body = $uri.Substring(8)
    $name = ''
    $h = $body.IndexOf('#')
    if ($h -ge 0) { $name = [System.Uri]::UnescapeDataString($body.Substring($h + 1)); $body = $body.Substring(0, $h) }
    $q = $null
    $qi = $body.IndexOf('?')
    if ($qi -ge 0) { $q = $body.Substring($qi + 1); $body = $body.Substring(0, $qi) }
    $at = $body.LastIndexOf('@')
    if ($at -lt 0) { return $null }
    $id = $body.Substring(0, $at)
    $hp = ($body.Substring($at + 1) -split '/')[0]
    $ci = $hp.LastIndexOf(':')
    if ($ci -lt 1) { return $null }
    $addr = $hp.Substring(0, $ci).Trim('[', ']')
    $port = 0
    if (-not [int]::TryParse($hp.Substring($ci + 1), [ref]$port)) { return $null }
    if ($port -lt 1 -or $port -gt 65535) { return $null }
    $p = @{}
    if ($q) {
        foreach ($pair in $q.Split('&')) {
            if (-not $pair) { continue }
            $kv = $pair.Split('=', 2)
            $k = [System.Uri]::UnescapeDataString($kv[0])
            $v = if ($kv.Count -gt 1) { [System.Uri]::UnescapeDataString($kv[1]) } else { '' }
            $p[$k] = $v
        }
    }
    [pscustomobject]@{
        Name = $name; Id = $id; Addr = $addr; Port = $port; P = $p
        Net  = $(if ($p['type']) { $p['type'] } else { 'tcp' })
        Sec  = $(if ($p['security']) { $p['security'] } else { 'none' })
    }
}

function New-Transport($node) {
    $stream = [ordered]@{ network = $node.Net }
    if ($node.Sec -eq 'tls') {
        $stream.security = 'tls'
        $tls = [ordered]@{ serverName = $(if ($node.P['sni']) { $node.P['sni'] } else { $node.Addr }) }
        if ($node.P['fp']) { $tls.fingerprint = $node.P['fp'] }
        $stream.tlsSettings = $tls
    }
    elseif ($node.Sec -eq 'reality') {
        $stream.security = 'reality'
        $stream.realitySettings = [ordered]@{
            serverName  = $node.P['sni']
            publicKey   = $node.P['pbk']
            shortId     = $(if ($node.P['sid']) { $node.P['sid'] } else { '' })
            fingerprint = $(if ($node.P['fp']) { $node.P['fp'] } else { 'chrome' })
        }
    }
    switch ($node.Net) {
        'ws'    { $stream.wsSettings = [ordered]@{ path = $(if ($node.P['path']) { $node.P['path'] } else { '/' }); host = $(if ($node.P['host']) { $node.P['host'] } else { $node.Addr }) } }
        'xhttp' { $stream.xhttpSettings = [ordered]@{ path = $(if ($node.P['path']) { $node.P['path'] } else { '/' }); host = $(if ($node.P['host']) { $node.P['host'] } else { $node.Addr }); mode = $(if ($node.P['mode']) { $node.P['mode'] } else { 'auto' }) } }
        'grpc'  { $stream.grpcSettings = [ordered]@{ serviceName = $(if ($node.P['serviceName']) { $node.P['serviceName'] } else { '' }) } }
    }
    return $stream
}

function New-Outbound($node, [string]$tag) {
    [ordered]@{
        tag            = $tag
        protocol       = 'vless'
        settings       = [ordered]@{ vnext = @([ordered]@{ address = $node.Addr; port = $node.Port; users = @([ordered]@{ id = $node.Id; encryption = 'none'; flow = $(if ($node.P['flow']) { $node.P['flow'] } else { '' }) }) }) }
        streamSettings = (New-Transport $node)
    }
}

function New-FullConfig($winners) {
    $obs = @()
    for ($i = 0; $i -lt $winners.Count; $i++) { $obs += New-Outbound $winners[$i] "proxy-$i" }
    [ordered]@{
        log         = [ordered]@{ loglevel = 'warning' }
        inbounds    = @(
            [ordered]@{ port = 10808; listen = '127.0.0.1'; protocol = 'socks'; settings = [ordered]@{ udp = $false; auth = 'noauth' } },
            [ordered]@{ port = 10809; listen = '127.0.0.1'; protocol = 'http'; settings = [ordered]@{ allowTransparent = $false } }
        )
        outbounds   = $obs
        observatory = [ordered]@{
            subjectSelector   = @('proxy')
            probeUrl          = 'https://www.gstatic.com/generate_204'
            probeInterval     = '45s'
            enableConcurrency = $true
        }
        routing     = [ordered]@{
            domainStrategy = 'AsIs'
            balancers      = @([ordered]@{ tag = 'bal'; selector = @('proxy'); strategy = [ordered]@{ type = 'leastPing' } })
            rules          = @([ordered]@{ type = 'field'; network = 'tcp,udp'; balancerTag = 'bal' })
        }
    }
}

$urls = @(
    'https://raw.githubusercontent.com/igareck/vpn-configs-for-russia/main/BLACK_VLESS_RUS_mobile.txt',
    'https://raw.githubusercontent.com/igareck/vpn-configs-for-russia/main/BLACK_VLESS_RUS.txt'
)

$all = @()
if ($Source) {
    $all += Get-Content $Source | Where-Object { $_ -match '^vless://' } | ForEach-Object { Parse-Vless $_ } | Where-Object { $_ }
}
else {
    foreach ($u in $urls) {
        try {
            $c = (Invoke-WebRequest -Uri $u -TimeoutSec 25 -UseBasicParsing).Content
            $all += ($c -split "`r?`n") | Where-Object { $_ -match '^vless://' } | ForEach-Object { Parse-Vless $_ } | Where-Object { $_ }
            Write-Host ("loaded {0} lines from {1}" -f $all.Count, $u)
        } catch { Write-Host ("skip {0}" -f $u) }
    }
}

$seen = @{}
$nodes = @()
foreach ($n in $all) {
    $key = "$($n.Id)|$($n.Addr)|$($n.Port)|$($n.Net)|$($n.Sec)"
    if ($seen.ContainsKey($key)) { continue }
    $seen[$key] = 1
    $nodes += $n
}
$nodes = @($nodes | Sort-Object @{ Expression = { if ($_.Sec -eq 'reality') { 0 } elseif ($_.Net -eq 'ws' -and $_.Sec -eq 'tls') { 1 } else { 2 } } })
Write-Host ("unique nodes: {0}, testing up to {1}, want {2} winners" -f $nodes.Count, $MaxNodes, $WantWinners)

$winners = @()
$idx = 0
while ($idx -lt $nodes.Count -and $idx -lt $MaxNodes -and $winners.Count -lt $WantWinners) {
    $procs = @()
    $take = [Math]::Min($Batch, [Math]::Min($nodes.Count, $MaxNodes) - $idx)
    for ($k = 0; $k -lt $take; $k++) {
        $node = $nodes[$idx + $k]
        $port = 10901 + $k
        $cfgPath = Join-Path $tmp ("notionvpn_cfg_$port.json")
        try {
            $probeCfg = [ordered]@{
                log       = [ordered]@{ loglevel = 'warning' }
                inbounds  = @([ordered]@{ port = $port; listen = '127.0.0.1'; protocol = 'socks'; settings = [ordered]@{ udp = $false; auth = 'noauth' } })
                outbounds = @(New-Outbound $node 'proxy-0')
            }
            $json = $probeCfg | ConvertTo-Json -Depth 14
            [System.IO.File]::WriteAllText($cfgPath, $json, (New-Object System.Text.UTF8Encoding($false)))
            $proc = Start-Process -FilePath $xray -ArgumentList @('run', '-c', "`"$cfgPath`"") -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $tmp "nv_o_$port.log") -RedirectStandardError (Join-Path $tmp "nv_e_$port.log")
            $procs += [pscustomobject]@{ Node = $node; Port = $port; Proc = $proc }
        } catch { }
    }
    Start-Sleep -Seconds 3
    foreach ($b in $procs) {
        $sp = "127.0.0.1:$($b.Port)"
        $alive = & curl.exe -s -o NUL -w "%{http_code}" --max-time 5 --socks5-hostname $sp 'https://www.gstatic.com/generate_204'
        $notion = ''
        if ($alive -eq '204') {
            $notion = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 12 --socks5-hostname $sp 'https://www.notion.com/'
        }
        Write-Host ("{0,-28} {1,-5} {2,-7} {3}:{4} alive={5} notion={6}" -f ($b.Node.Name -replace '[^\x20-\x7E]', ''), $b.Node.Net, $b.Node.Sec, $b.Node.Addr, $b.Node.Port, $alive, $notion)
        if ($notion -eq '200') { $winners += $b.Node }
    }
    foreach ($b in $procs) { try { Stop-Process -Id $b.Proc.Id -Force -ErrorAction SilentlyContinue } catch { } }
    $idx += $Batch
}

if ($winners.Count -eq 0) { Write-Host "no working node found - run again in a few minutes"; exit 1 }

$cfgPath = Join-Path $dir 'config.json'
if (Test-Path $cfgPath) { Copy-Item $cfgPath "$cfgPath.bak" -Force }
$finalJson = (New-FullConfig $winners) | ConvertTo-Json -Depth 14
[System.IO.File]::WriteAllText($cfgPath, $finalJson, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("config.json written with {0} balancer nodes" -f $winners.Count)

if (-not $DryRun) {
    Get-Process xray -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    Start-Process -FilePath 'wscript.exe' -ArgumentList @("`"$dir\hidden.vbs`"") -WindowStyle Hidden
    Start-Sleep -Seconds 4
    $check = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 20 --socks5-hostname 127.0.0.1:10808 'https://www.notion.com/'
    Write-Host ("self-check through tunnel: notion.com -> {0}" -f $check)
}
