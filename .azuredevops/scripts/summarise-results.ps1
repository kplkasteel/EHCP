# Reads the newest unit and Specs .trx files and writes artifacts/summary.json for the PR comment.
# Runs as an always() step, so it must not fail when a test run did not happen.
param(
  [string]$OverallStatus = 'unknown',
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

$summary = [ordered]@{
  overall = $overall
  unit    = Get-RunSummary -Folder (Join-Path $PSScriptRoot '..\..\tests\Ehcp.Tests\TestResults')
  specs   = Get-RunSummary -Folder (Join-Path $PSScriptRoot '..\..\tests\Ehcp.Specs\TestResults')
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutFile -Encoding UTF8
Write-Host "Summary written to $OutFile (overall: $OverallStatus)"
exit 0
