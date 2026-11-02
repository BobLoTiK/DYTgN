param([int]$Cap = 0)
# Collects VLESS configs from many fresh repos, dedupes by (id, host, port),
# keeps only reality/tls, orders reality first. Writes candidates.txt.

$ErrorActionPreference = 'Continue'
$dir = Join-Path $env:TEMP 'vpnfree'
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$repos = @(
    'MohammadBahemmat/V2ray-Collector',
    'SoliSpirit/v2ray-configs',
    'iboxz/free-v2ray-collector',
    'ProblemTheCode/SylphNet-public',
    'amirkma/proxykma',
    'amir-reza-bijandi/v2ray-configs',
    'penhandev/AutoAiVPN',
    '0xRadikal/Free-v2ray-Configs',
    'jafarm83/ConfigV2Ray',
    'ALIILAPRO/v2rayNG-Config',
    'alireza786786/Free-V2ray-Configs',
    'Epodonios/v2ray-configs'
)

$all = New-Object System.Collections.Generic.List[string]
foreach ($repo in $repos) {
    try {
        $items = Invoke-RestMethod "https://api.github.com/repos/$repo/contents" -Headers @{ 'User-Agent' = 'ps' } -TimeoutSec 20
        $files = $items | Where-Object { $_.type -eq 'file' -and $_.name -match '\.(txt)$' -and $_.size -gt 2000 } |
            Sort-Object size -Descending | Select-Object -First 3
        $got = 0
        foreach ($f in $files) {
            try {
                $c = (Invoke-WebRequest -Uri $f.download_url -TimeoutSec 45 -UseBasicParsing).Content
                if ($c -notmatch '://') { try { $c = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($c -replace '\s', ''))) } catch { } }
                foreach ($l in ($c -split "`r?`n")) {
                    if ($l -match '^vless://') { $all.Add($l.Trim()); $got++ }
                }
            }
            catch { }
        }
        Write-Host ("{0,-40} +{1} vless" -f $repo, $got)
    }
    catch { Write-Host ("{0,-40} skip" -f $repo) }
}

$good = $all | Where-Object { $_ -match 'security=(reality|tls)' } | Sort-Object -Unique
$seen = @{}
$uniq = New-Object System.Collections.Generic.List[string]
foreach ($u in $good) {
    $key = ''
    if ($u -match '^vless://([^@]+)@([^:/?#]+):(\d+)') { $key = "$($Matches[1])|$($Matches[2])|$($Matches[3])" }
    if ($key -and $seen.ContainsKey($key)) { continue }
    if ($key) { $seen[$key] = 1 }
    $uniq.Add($u)
}
$ordered = $uniq | Sort-Object @{ Expression = {
        if ($_ -match 'security=reality' -and $_ -match 'flow=xtls-rprx-vision') { 0 }
        elseif ($_ -match 'security=reality') { 1 }
        elseif ($_ -match 'type=ws') { 2 }
        else { 3 }
    } }
if ($Cap -gt 0) { $ordered = $ordered | Select-Object -First $Cap }

$out = Join-Path $dir 'candidates.txt'
[System.IO.File]::WriteAllLines($out, [string[]]$ordered, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("total vless: {0}, reality/tls: {1}, unique ordered: {2}" -f $all.Count, $good.Count, $ordered.Count)
Write-Host ("written: {0}" -f $out)
