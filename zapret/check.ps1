# Non-admin health check for zapret: is winws alive and do the DPI-blocked sites open?
# Used by netsuite.ps1 and runnable by hand.
# Does NOT touch anything: only probes.

param([string[]]$Targets = @('https://www.youtube.com/', 'https://discord.com/'))

function Test-Http([string]$url, [int]$timeoutSec = 8) {
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time $timeoutSec $url 2>$null
    $time = 0.0
    [double]::TryParse((& curl.exe -s -o NUL -w "%{time_total}" --max-time $timeoutSec $url 2>$null), [ref]$time) | Out-Null
    return [pscustomobject]@{
        Url    = $url
        Code   = $code
        Time   = $time
        Ok     = ($code -match '^(200|204)$')
    }
}

$winws = (Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
$internet = (Test-Http 'https://www.google.com/generate_204' 6).Ok
$results = foreach ($t in $Targets) { Test-Http $t }
$blocked = @($results | Where-Object { -not $_.Ok })

$state = 'unknown'
if (-not $winws) { $state = 'off' }
elseif (-not $internet) { $state = 'no-internet' }
elseif ($blocked.Count -eq 0) { $state = 'ok' }
else { $state = 'dead' }

[pscustomobject]@{
    State     = $state
    Winws     = $winws
    Internet  = $internet
    Targets   = (@($results | ForEach-Object { "{0}={1}" -f ($_.Url -replace 'https://',''), $_.Code }) -join ' ')
    Blocked   = (@($blocked | ForEach-Object { $_.Url }) -join ' ')
}
