# Waits until the Cosmos DB emulator answers an authenticated account read over trusted TLS.
# Mirrors the Specs hook (tests/Ehcp.Specs/Hooks/LocalTestStack.cs), which does the same with
# CosmosClient.ReadAccountAsync and a 5 second request timeout. Here curl.exe sends the same signed GET
# to https://localhost:8081/. The emulator certificate is taken from the Windows store and trusted first.
# Needs an elevated session to import into LocalMachine\Root, as CI agents are.
param(
  [long]$LaunchEpoch = 0,
  [int]$TimeoutMinutes = 10
)
$ErrorActionPreference = 'Continue'

# True when something accepts TCP connections on the local port.
function PortOpen {
  param([int]$Port)
  $c = New-Object System.Net.Sockets.TcpClient
  try { $c.Connect('127.0.0.1', $Port); return $true } catch { return $false } finally { $c.Close() }
}

if (-not $LaunchEpoch) { $LaunchEpoch = [long]$env:COSMOS_LAUNCH_EPOCH }
$begin = Get-Date
$deadline = $begin.AddMinutes($TimeoutMinutes)

# Microsoft's published, well-known Cosmos DB emulator key, not a secret. It is the same value as
# EMULATOR_KEY in the Rakefile and the default in CosmosSettings, and works only against the local emulator.
$emulatorKey = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='
$script:lastCode = ''

function EmulatorCert {
  foreach ($store in 'Cert:\LocalMachine\My', 'Cert:\CurrentUser\My') {
    $c = Get-ChildItem $store -ErrorAction SilentlyContinue |
      Where-Object { $_.FriendlyName -like '*DocumentDbEmulator*' -or $_.Subject -eq 'CN=localhost' } |
      Select-Object -First 1
    if ($c) { return $c }
  }
  return $null
}

function CosmosReady {
  $date = [DateTime]::UtcNow.ToString('r', [System.Globalization.CultureInfo]::InvariantCulture).ToLowerInvariant()
  $hmac = New-Object System.Security.Cryptography.HMACSHA256 -ArgumentList (, [Convert]::FromBase64String($emulatorKey))
  $sig = [Convert]::ToBase64String($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes("get`n`n`n$date`n`n")))
  $token = [Uri]::EscapeDataString("type=master&ver=1.0&sig=$sig")
  $script:lastCode = (& curl.exe -sS -o NUL -w '%{http_code}' --max-time 5 -H "authorization: $token" -H "x-ms-date: $date" -H 'x-ms-version: 2018-12-31' https://localhost:8081/ 2>&1) -join ' '
  return ($script:lastCode -eq '200')
}

$trusted = $false
$lastLog = Get-Date
while ($true) {
  if (-not $trusted) {
    $cert = EmulatorCert
    if ($cert) {
      $cer = Join-Path $env:TEMP 'cosmos-emulator.cer'
      Export-Certificate -Cert $cert -FilePath $cer -Type CERT | Out-Null
      Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
      $trusted = $true
      Write-Host ("Trusted emulator certificate '{0}' after {1:N0}s." -f $cert.Subject, ((Get-Date) - $begin).TotalSeconds)
    }
  }
  if (CosmosReady) { break }
  if (((Get-Date) - $lastLog).TotalSeconds -ge 30) {
    $procs = (Get-Process | Where-Object { $_.Name -like '*Cosmos*' } | ForEach-Object { $_.Name }) -join ', '
    Write-Host ("Waiting {0:N0}s: processes=[{1}], port 8081 open={2}, certificate trusted={3}, account read result={4}" -f ((Get-Date) - $begin).TotalSeconds, $procs, (PortOpen 8081), $trusted, $script:lastCode)
    $lastLog = Get-Date
  }
  if ((Get-Date) -gt $deadline) {
    Write-Host 'Certificates in LocalMachine\My:'
    Get-ChildItem Cert:\LocalMachine\My | ForEach-Object { Write-Host ("  {0} | {1}" -f $_.Subject, $_.FriendlyName) }
    throw "Cosmos DB emulator account read did not succeed within $TimeoutMinutes minutes."
  }
  Start-Sleep -Seconds 3
}

if ($LaunchEpoch) {
  $seconds = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - $LaunchEpoch
  Write-Host "Cosmos DB emulator ready (authenticated account read over trusted TLS) $seconds s after launch."
} else {
  Write-Host 'Cosmos DB emulator ready (authenticated account read over trusted TLS).'
}
exit 0
