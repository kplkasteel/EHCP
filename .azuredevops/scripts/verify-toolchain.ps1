# Prints the version of every tool the build needs and fails if any cannot run. Safe to run locally.
$tools = @(
  @{ n = 'ruby';    a = '-v';        label = 'Ruby' },
  @{ n = 'rake';    a = '--version'; label = 'Rake' },
  @{ n = 'dotnet';  a = '--version'; label = '.NET SDK' },
  @{ n = 'node';    a = '-v';        label = 'Node.js' },
  @{ n = 'azurite'; a = '--version'; label = 'Azurite' },
  @{ n = 'func';    a = '--version'; label = 'Azure Functions Core Tools' }
)

$failed = @()
foreach ($tool in $tools) {
  $out = @(& $tool.n $tool.a 2>&1)
  $code = $LASTEXITCODE
  if ($code -ne 0) {
    $failed += $tool.label
    Write-Host ("{0,-30} FAILED (exit {1}): {2}" -f $tool.label, $code, ($out -join ' '))
  } else {
    Write-Host ("{0,-30} {1}" -f $tool.label, $out[0])
  }
}
if ($failed) { throw "Not runnable: $($failed -join ', ')" }
exit 0
