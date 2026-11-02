$ErrorActionPreference = 'Continue'
# Serves proxy.pac on http://127.0.0.1:10810/proxy.pac using a raw TcpListener
# (no admin, no http.sys). Only Notion domains go through the local SOCKS tunnel,
# everything else stays DIRECT.
#
# NOTE: dnsDomainIs() is NOT usable here - Yandex/Chromium fails to evaluate a PAC
# that calls it and silently falls back to DIRECT for every request. Plain
# host.indexOf() works.

$port = 10810
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$logPath = Join-Path $dir 'pac.log'

$pac = @'
function FindProxyForURL(url, host) {
  if (host.indexOf("notion") >= 0 || host.indexOf("splunkcloud") >= 0) {
    return "SOCKS5 127.0.0.1:10808";
  }
  return "DIRECT";
}
'@

$listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
try { $listener.Start() } catch { exit 1 }
$body = [System.Text.Encoding]::ASCII.GetBytes($pac)
$header = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: application/x-ns-proxy-autoconfig`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")

while ($true) {
    $client = $listener.AcceptTcpClient()
    try {
        $stream = $client.GetStream()
        $stream.ReadTimeout = 3000
        $buf = New-Object byte[] 4096
        $read = 0
        try { $read = $stream.Read($buf, 0, $buf.Length) } catch { }
        $req = [System.Text.Encoding]::ASCII.GetString($buf, 0, $read)
        $first = ($req -split "`r?`n")[0]
        Add-Content -Path $logPath -Value ("{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $first) -Encoding UTF8
        $stream.Write($header, 0, $header.Length)
        $stream.Write($body, 0, $body.Length)
        $stream.Flush()
    }
    catch { }
    finally { $client.Close() }
}
