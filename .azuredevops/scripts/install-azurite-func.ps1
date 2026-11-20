# Installs Azurite and Azure Functions Core Tools through npm, pinned to a minor version (for example 3.37 or 4.16).
# Skips the install when both already run at the pinned minor. Reports how long the install took and how much it wrote.
# Intended for CI agents: it runs `npm install -g`, which changes the machine it runs on.
param(
  [Parameter(Mandatory)][string]$AzuriteVersion,
  [Parameter(Mandatory)][string]$FuncVersion
)
$ErrorActionPreference = 'Continue'

# Runs a script block until its native command exits 0, waiting between attempts.
function Retry {
  param(
    [scriptblock]$Action,
    [string]$Name,
    [int]$Attempts = 3,
    [int]$DelaySeconds = 15
  )
  for ($i = 1; $i -le $Attempts; $i++) {
    & $Action
    if ($LASTEXITCODE -eq 0) { return }
    Write-Host "$Name failed (attempt $i of $Attempts)."
    if ($i -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
  }
  throw "$Name failed after $Attempts attempts."
}

Write-Host 'Installing Azurite and Azure Functions Core Tools. Expect 1 to 5 minutes: download speed varies between runs.'
Write-Host 'Install time has been optimised (npm prefix and cache moved to the D: drive), but it still depends on factors outside of our control and cannot be guaranteed.'
Write-Host 'Another option could be to have the installs already local to this repo & reference them directly, avoiding the need for npm install on each run but that adds additional maintenance overhead.'

$want = @{ azurite = $AzuriteVersion; func = $FuncVersion }

function HasPinned {
  param([string]$Tool)
  if (-not (Get-Command $Tool -ErrorAction SilentlyContinue)) { return $false }
  $found = (& $Tool --version | Select-Object -Last 1).ToString().Trim()
  return $found -like "$($want[$Tool]).*"
}

function DirMb {
  param([string]$Path)
  if (-not $Path -or -not (Test-Path $Path)) { return 0 }
  return (Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1MB
}

$prefix = (npm config get prefix).Trim()
$env:PATH = "$prefix;$env:PATH"

if ((HasPinned 'azurite') -and (HasPinned 'func')) {
  Write-Host "Azurite $($want.azurite).x and func $($want.func).x already installed; skipping npm install."
} else {
  $cacheBefore = DirMb $env:NPM_CONFIG_CACHE
  $prefixBefore = DirMb $prefix
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  Retry { npm install -g "azurite@$($want.azurite)" "azure-functions-core-tools@$($want.func)" } 'npm install'
  $sw.Stop()
  $cacheMb = (DirMb $env:NPM_CONFIG_CACHE) - $cacheBefore
  $prefixMb = (DirMb $prefix) - $prefixBefore
  $secs = [math]::Max($sw.Elapsed.TotalSeconds, 1)
  Write-Host ("npm install finished in {0:N0}s: {1:N0} MB added to the npm download cache, {2:N0} MB installed (about {3:N1} MB/s written, approximate)." -f $secs, $cacheMb, $prefixMb, (($cacheMb + $prefixMb) / $secs))
}

Write-Host "##vso[task.prependpath]$prefix"
$deadline = (Get-Date).AddMinutes(2)
foreach ($tool in 'azurite', 'func') {
  while (-not (HasPinned $tool)) {
    if ((Get-Date) -gt $deadline) { throw "$tool $($want[$tool]).x not runnable after install." }
    Start-Sleep -Seconds 5
  }
}
exit 0
