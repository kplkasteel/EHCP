# Posts or updates one PR comment with the results in artifacts/summary.json.
# Runs only for PR builds. Uses System.AccessToken, passed in as the SYSTEM_ACCESSTOKEN environment variable.
# The comment is found by a hidden marker, so each run updates it instead of adding a new one.
param(
  [string]$SummaryFile = (Join-Path $PSScriptRoot '..\..\artifacts\summary.json'),
  [switch]$FailOnError,
  [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$marker = '<!-- ehcp-pr-validation -->'

function Format-Row($name, $r) {
  if (-not $r) { return "| $name | not run | | | |" }
  "| $name | $($r.passed) | $($r.failed) | $($r.total) | $($r.duration_s) s |"
}

$summary = Get-Content -LiteralPath $SummaryFile -Raw | ConvertFrom-Json
$passed = $summary.overall -eq 'passed'
$title = if ($passed) { 'PR Build and Test passed' } else { 'PR Build and Test failed' }
# Thread status: 2 = Resolved (shown as Resolved in the PR), 1 = Active.
$threadStatus = if ($passed) { 2 } else { 1 }

$lines = @(
  $marker,
  "**$title**",
  '',
  '| Suite | Passed | Failed | Total | Duration |',
  '|---|---|---|---|---|',
  (Format-Row 'Unit tests' $summary.unit),
  (Format-Row 'Specs' $summary.specs)
)
$failing = @($summary.unit.failed_tests) + @($summary.specs.failed_tests) | Where-Object { $_ }
if ($failing) {
  $lines += ''
  $lines += '**Failing tests**'
  $lines += $failing | Select-Object -First 20 | ForEach-Object { "- $_" }
}
$lines += ''
$lines += "[View the run]($env:SYSTEM_COLLECTIONURI$env:SYSTEM_TEAMPROJECT/_build/results?buildId=$env:BUILD_BUILDID)"
$lines += ''
$lines += if ($passed) {
  '_This comment is marked as resolved because the pipeline tests passed._'
} else {
  '_This comment stays active until the pipeline tests pass._'
}
$body = $lines -join "`n"

if ($DryRun) { Write-Output $body; exit 0 }

$org = $env:SYSTEM_COLLECTIONURI.TrimEnd('/')
$project = [uri]::EscapeDataString($env:SYSTEM_TEAMPROJECT)
$base = "$org/$project/_apis/git/repositories/$env:BUILD_REPOSITORY_ID/pullRequests/$env:SYSTEM_PULLREQUEST_PULLREQUESTID"
$headers = @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN"; 'Content-Type' = 'application/json' }

try {
  $threads = (Invoke-RestMethod -Uri "$base/threads?api-version=7.1" -Headers $headers).value
  $existing = $threads | Where-Object { $_.comments.content -like "*$marker*" } | Select-Object -First 1

  if ($existing) {
    $commentId = ($existing.comments | Where-Object { $_.content -like "*$marker*" } | Select-Object -First 1).id
    $null = Invoke-RestMethod -Method Patch -Uri "$base/threads/$($existing.id)/comments/$commentId`?api-version=7.1" `
      -Headers $headers -Body (@{ content = $body } | ConvertTo-Json)
    $null = Invoke-RestMethod -Method Patch -Uri "$base/threads/$($existing.id)?api-version=7.1" `
      -Headers $headers -Body (@{ status = $threadStatus } | ConvertTo-Json)
    Write-Host "Updated PR comment (thread $($existing.id))."
  } else {
    $new = @{ comments = @(@{ parentCommentId = 0; content = $body; commentType = 1 }); status = $threadStatus }
    $null = Invoke-RestMethod -Method Post -Uri "$base/threads?api-version=7.1" -Headers $headers -Body ($new | ConvertTo-Json -Depth 5)
    Write-Host 'Posted PR comment.'
  }
} catch {
  if ($FailOnError) { throw }
  Write-Warning "Could not post the PR comment: $($_.Exception.Message)"
}
exit 0
