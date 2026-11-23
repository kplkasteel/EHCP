# Posts a PR comment with the results in artifacts/summary.json, and replies to or deletes earlier results.
# Runs only for PR builds. Uses System.AccessToken, passed in as the SYSTEM_ACCESSTOKEN environment variable.
# Each run posts a new result comment. Earlier results are kept: each one gets a reply saying a newer
# result exists, and is resolved. The comment is found by a hidden marker.
# -TidyOldThreads deletes earlier results that have no replies instead of keeping them.
param(
  [string]$SummaryFile = (Join-Path $PSScriptRoot '..\..\artifacts\summary.json'),
  [switch]$FailOnError,
  [switch]$DryRun,
  [switch]$TidyOldThreads
)
$ErrorActionPreference = 'Stop'
$marker = '<!-- ehcp-pr-validation -->'

function Format-Row($name, $r) {
  if (-not $r) { return "| $name | ⚠️ Not run | | | | |" }
  $icon = if ($r.failed -eq 0) { '✅' } else { '❌' }
  "| $name | $icon | $($r.passed) | $($r.failed) | $($r.total) | $($r.duration_s) s |"
}

$summary = Get-Content -LiteralPath $SummaryFile -Raw | ConvertFrom-Json
$passed = $summary.overall -eq 'passed'
$title = if ($passed) { '✅ PR Build and Test passed' } else { '❌ PR Build and Test failed' }
# Thread status: 2 = Resolved (shown as Resolved in the PR), 1 = Active.
$threadStatus = if ($passed) { 2 } else { 1 }

# One row for the whole install range: from the first to the last step of install-tools.yml, read from this build's timeline.
# The step names come from install-tools.yml, so steps added to that file are included automatically.
function Get-ToolInstallRow {
  if ($DryRun) { return }
  # Each displayName in the template becomes a pattern. ${{ ... }} parts (such as versions) match any text.
  $patterns = @(Select-String -Path (Join-Path $PSScriptRoot '..\templates\install-tools.yml') -Pattern '^\s*displayName:\s*(.+?)\s*$' | ForEach-Object {
    $parts = [regex]::Split($_.Matches[0].Groups[1].Value, '\$\{\{.*?\}\}')
    '^' + (($parts | ForEach-Object { [regex]::Escape($_) }) -join '.*') + '$'
  })
  try {
    $uri = "$($env:SYSTEM_COLLECTIONURI.TrimEnd('/'))/$([uri]::EscapeDataString($env:SYSTEM_TEAMPROJECT))/_apis/build/builds/$($env:BUILD_BUILDID)/timeline?api-version=7.1"
    $tasks = @((Invoke-RestMethod -Uri $uri -Headers @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN" }).records | Where-Object { $_.type -eq 'Task' })
  } catch {
    Write-Warning "Could not read tool install times: $($_.Exception.Message)"
    return
  }
  $steps = @($tasks | Where-Object {
    $name = $_.name
    $_.result -ne 'skipped' -and $_.startTime -and $_.finishTime -and @($patterns | Where-Object { $name -match $_ }).Count -gt 0
  })
  if ($steps.Count -eq 0) { return '| Tool install | ⚠️ Not run | | | | |' }
  $start = [datetime](($steps | ForEach-Object { [datetime]$_.startTime } | Measure-Object -Minimum).Minimum)
  $end = [datetime](($steps | ForEach-Object { [datetime]$_.finishTime } | Measure-Object -Maximum).Maximum)
  $icon = if (@($steps | Where-Object { $_.result -eq 'failed' }).Count -gt 0) { '❌' } else { '✅' }
  "| Tool install | $icon | | | | $([math]::Round(($end - $start).TotalSeconds, 1)) s |"
}

$buildTime = if ($summary.build_duration_s) { "$($summary.build_duration_s) s" } else { '' }
$buildRow = switch ($summary.build) {
  'passed' { "| Build | ✅ Passed | | | | $buildTime |" }
  'failed' { "| Build | ❌ Failed | | | | $buildTime |" }
  default  { '| Build | ⚠️ Not run | | | | |' }
}
$toolRows = @(Get-ToolInstallRow)

$lines = @(
  $marker,
  "**$title**",
  '',
  '| Suite | Result | Passed | Failed | Total | Duration |',
  '|---|---|---|---|---|---|'
) + $toolRows + @(
  $buildRow,
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
$prUrl = "$($env:SYSTEM_COLLECTIONURI)$($env:SYSTEM_TEAMPROJECT)/_git/$($env:BUILD_REPOSITORY_NAME)/pullrequest/$($env:SYSTEM_PULLREQUEST_PULLREQUESTID)"
$runUrl = "$($env:SYSTEM_COLLECTIONURI)$($env:SYSTEM_TEAMPROJECT)/_build/results?buildId=$($env:BUILD_BUILDID)"
$testsUrl = "$runUrl&view=ms.vss-test-web.build-test-results-tab"
$artifactsUrl = "$runUrl&view=artifacts&type=publishedArtifacts"
$links = @("[View the run]($runUrl)")
# Test results exist only if a suite ran, so link to them only then.
if ($summary.unit -or $summary.specs) { $links += "[View tests]($testsUrl)" }
# The artifacts tab holds the TRX files and host logs. The link is shown only if the Specs run produced results.
if ($summary.specs) { $links += "[View artifacts]($artifactsUrl)" }
$lines += $links -join ' | '
Write-Host "Pull request: $prUrl"
$lines += ''
$lines += if ($passed) {
  '_This comment is resolved because the pipeline tests passed. A newer pipeline run will mark it superseded._'
} else {
  '_A newer pipeline run will mark this comment superseded and resolve it._'
}
$body = $lines -join "`n"

if ($DryRun) { Write-Output $body; exit 0 }

$org = $env:SYSTEM_COLLECTIONURI.TrimEnd('/')
$project = [uri]::EscapeDataString($env:SYSTEM_TEAMPROJECT)
$base = "$org/$project/_apis/git/repositories/$env:BUILD_REPOSITORY_ID/pullRequests/$env:SYSTEM_PULLREQUEST_PULLREQUESTID"
$headers = @{ Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN"; 'Content-Type' = 'application/json' }

try {
  $threads = (Invoke-RestMethod -Uri "$base/threads?api-version=7.1" -Headers $headers).value
  # Earlier results. By default each one is replied to and resolved, so its history stays on the PR.
  # With -TidyOldThreads, earlier results that have no replies are deleted instead. Threads with replies are
  # always kept, so a person's discussion isn't lost.
  $previous = @($threads | Where-Object { $_.comments.content -like "*$marker*" })
  $kept = 0; $skipped = 0; $removed = 0
  foreach ($thread in $previous) {
    $hasReplies = @($thread.comments | Where-Object { $_.content -notlike "*$marker*" -and $_.isDeleted -ne $true }).Count -gt 0
    if ($TidyOldThreads -and -not $hasReplies) {
      foreach ($c in ($thread.comments | Where-Object { $_.content -like "*$marker*" })) {
        $null = Invoke-RestMethod -Method Delete -Uri "$base/threads/$($thread.id)/comments/$($c.id)?api-version=7.1" -Headers $headers
      }
      $removed++
    } else {
      # Reply and resolve once. A thread that already has the superseded reply is left alone.
      # Checked by the reply text, not the thread status: a passing run's comment is already resolved,
      # but still needs the reply.
      $alreadySuperseded = @($thread.comments | Where-Object { $_.content -like '*superseded this result*' }).Count -gt 0
      if ($alreadySuperseded) {
        $skipped++
      } else {
        $reply = @{ content = '⚠️ **A newer pipeline run has superseded this result.** See the latest comment.'; parentCommentId = 0; commentType = 1 }
        $null = Invoke-RestMethod -Method Post -Uri "$base/threads/$($thread.id)/comments?api-version=7.1" -Headers $headers -Body ($reply | ConvertTo-Json)
        $null = Invoke-RestMethod -Method Patch -Uri "$base/threads/$($thread.id)?api-version=7.1" -Headers $headers -Body (@{ status = 2 } | ConvertTo-Json)
        $kept++
      }
    }
  }
  Write-Host "Earlier threads: $kept replied to and resolved, $skipped already superseded, $removed deleted."

  $new = @{ comments = @(@{ parentCommentId = 0; content = $body; commentType = 1 }); status = $threadStatus }
  $null = Invoke-RestMethod -Method Post -Uri "$base/threads?api-version=7.1" -Headers $headers -Body ($new | ConvertTo-Json -Depth 5)
  Write-Host 'Posted PR comment.'
} catch {
  if ($FailOnError) { throw }
  Write-Warning "Could not post the PR comment: $($_.Exception.Message)"
}
exit 0
