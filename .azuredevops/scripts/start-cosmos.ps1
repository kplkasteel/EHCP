# Installs the Cosmos DB emulator if the image lacks it, then launches it in the background.
# It needs 1 to 2 minutes to start cold, so it goes first and wait-for-cosmos.ps1 blocks later.
# Prints the pipeline variable COSMOS_LAUNCH_EPOCH, which wait-for-cosmos.ps1 uses to report startup time.
$ErrorActionPreference = 'Continue'

$exe = Join-Path $env:ProgramFiles 'Azure Cosmos DB Emulator\CosmosDB.Emulator.exe'
if (Test-Path $exe) { Write-Host "Cosmos DB emulator already present at $exe" }
for ($i = 1; $i -le 3 -and -not (Test-Path $exe); $i++) {
  choco install azure-cosmosdb-emulator -y --no-progress
  $deadline = (Get-Date).AddMinutes(3)
  while (-not (Test-Path $exe) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 5 }
  if (-not (Test-Path $exe) -and $i -lt 3) { Write-Host "Cosmos emulator install attempt $i failed."; Start-Sleep -Seconds 15 }
}
if (-not (Test-Path $exe)) { throw "Cosmos DB emulator not found at $exe after 3 attempts." }

$epoch = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
Write-Host "##vso[task.setvariable variable=COSMOS_LAUNCH_EPOCH]$epoch"
Start-Process -FilePath $exe -ArgumentList '/NoUI'
Write-Host "Cosmos DB emulator launched in the background at $exe"
exit 0
