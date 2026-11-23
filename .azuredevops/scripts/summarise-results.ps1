# Reads the newest unit and Specs .trx files and writes artifacts/summary.json for the PR comment.
# Runs as an always() step, so it must not fail when a test run did not happen.
param(
  [string]$OverallStatus = 'unknown',
  # 'true' once rake go has started, and 'true' once unit tests have started (they run only after a successful build).
  [string]$RakeStarted = '',
  [string]$UnitStarted = '',
  [string]$OutFile = (Join-Path $PSScriptRoot '..\..\artifacts\summary.json')
)
$ErrorActionPreference = 'Continue'

function Get-RunSummary {
  param([string]$Folder)
  $trx = Get-ChildItem -Path $Folder -Filter '*.trx' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $trx) { return $null }

  [xml]$doc = Get-Content -LiteralPath $trx.FullName -Raw
  $counters = $doc.TestRun.ResultSummary.Counters
  $times = $doc.TestRun.Times
  $duration = ([datetime]$times.finish - [datetime]$times.start).TotalSeconds
  $failed = @($doc.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' } |
    ForEach-Object { $_.testName } | Select-Object -Unique)

  [ordered]@{
    total        = [int]$counters.total
    passed       = [int]$counters.passed
    failed       = [int]$counters.failed
    duration_s   = [math]::Round($duration, 1)
    failed_tests = @($failed | Select-Object -First 20)
  }
}

# Pipeline job status is 'Succeeded' or 'Failed'. Map it to the passed or failed the comment expects.
$overall = switch ($OverallStatus) { 'Succeeded' { 'passed' } 'passed' { 'passed' } default { 'failed' } }

# Build: passed if unit tests started (they depend on a successful build), failed if rake started but the build did not, otherwise not run.
$build = if ($UnitStarted -eq 'true') { 'passed' } elseif ($RakeStarted -eq 'true') { 'failed' } else { 'not run' }

$summary = [ordered]@{
  overall = $overall
  build   = $build
  unit    = Get-RunSummary -Folder (Join-Path $PSScriptRoot '..\..\tests\Ehcp.Tests\TestResults')
  specs   = Get-RunSummary -Folder (Join-Path $PSScriptRoot '..\..\tests\Ehcp.Specs\TestResults')
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutFile -Encoding UTF8
# Gather the artifacts into one folder, so a single upload step can publish them:
# the newest unit and Specs TRX files, plus the Specs host logs.
$artifactDir = Join-Path (Split-Path $OutFile) 'test-results'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
# Each TRX goes into a folder for its suite, under a fixed file name.
$trxFiles = @{ 'unit-tests' = '..\..\tests\Ehcp.Tests\TestResults'; 'specs' = '..\..\tests\Ehcp.Specs\TestResults' }
foreach ($suite in $trxFiles.Keys) {
  $newest = Get-ChildItem -Path (Join-Path $PSScriptRoot $trxFiles[$suite]) -Filter '*.trx' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($newest) {
    $suiteDir = Join-Path $artifactDir $suite
    New-Item -ItemType Directory -Force -Path $suiteDir | Out-Null
    Copy-Item -LiteralPath $newest.FullName -Destination (Join-Path $suiteDir $newest.Name) -Force
  }
}
$hostLogs = Join-Path $PSScriptRoot '..\..\tests\Ehcp.Specs\TestResults\local-stack'
if (Test-Path $hostLogs) {
  Copy-Item -Path $hostLogs -Destination (Join-Path $artifactDir 'host-logs') -Recurse -Force
}
# Tell the pipeline whether there is anything to upload, so the artifact step can skip when there isn't.
if (Get-ChildItem -Path $artifactDir -Recurse -File -ErrorAction SilentlyContinue) {
  Write-Host '##vso[task.setvariable variable=HasTestArtifacts]true'
}

Write-Host "Summary written to $OutFile (overall: $OverallStatus)"
exit 0
