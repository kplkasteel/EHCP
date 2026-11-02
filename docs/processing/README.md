# Run the processing flow

Prerequisites: .NET 10 SDK, Azure Functions Core Tools v4 with .NET 10 support,
Azurite and a trusted local Cosmos DB emulator. These are synthetic sample
datasets, not automated tests. Existing tests are unchanged.

Build with dotnet build Ehcp.slnx. Run Azurite separately. Configure both hosts
with the same Cosmos database and mailbox containers.

From the repository root, use PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:AZURE_FUNCTIONS_ENVIRONMENT = "Development"
$env:FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"
$env:AzureWebJobsStorage = "UseDevelopmentStorage=true"
$env:ProcessingQueueName = "ehcp-processing"
$env:Cosmos__Endpoint = "https://localhost:8081"
$env:Cosmos__Key = "<emulator key>"
$env:Cosmos__Database = "ehcp-local"
$env:Cosmos__Initialize = "true"
[Environment]::SetEnvironmentVariable("Cosmos__Mailboxes__ehcp-team-shared-1__EmailsContainer", "scenario-emails", "Process")
[Environment]::SetEnvironmentVariable("Cosmos__Mailboxes__ehcp-team-shared-1__CasesContainer", "scenario-cases", "Process")
$env:Processing__Graph__Mode = "Mock"
$env:Processing__Graph__DataPath = (Resolve-Path docs/processing/sample-graph-email.json).Path
$env:Processing__Extraction__Mode = "Mock"
$env:Processing__Extraction__DataPath = (Resolve-Path docs/processing/sample-ai-extract.json).Path
```

Every host needs these settings in its own process. Run the application:

```powershell
dotnet run --project src/Ehcp.Api
```

In a second terminal with the same settings, run the queue-triggered Function:

```powershell
Set-Location src/Ehcp.Processing
func start
```

Alternatively copy local.settings.example.json to local.settings.json inside
Ehcp.Processing and fill in the values and the sample mailbox container settings.
local.settings.json is ignored by Git. Initialization is permitted only in
Development; production containers must be provisioned separately.

## Seed, enqueue and read

sample-data.json has emails and cases arrays. Both are empty for this sample.
For other scenarios, seed the selected mailbox containers directly or use
the existing POST /api/mailboxes/{mailboxId}/emails and /cases endpoints.

From the repository root, using Azure CLI against Azurite:

```powershell
az storage queue create --name $env:ProcessingQueueName --connection-string $env:AzureWebJobsStorage
$json = Get-Content docs/processing/sample-queue.json -Raw
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
az storage message put --queue-name $env:ProcessingQueueName --connection-string $env:AzureWebJobsStorage --content $payload
Invoke-RestMethod http://localhost:5100/api/mailboxes/ehcp-team-shared-1/emails/scenario-1-single-child
```

Use a full Azurite connection string if your CLI does not expand
UseDevelopmentStorage=true. With Azure.Storage.Queues, use
QueueClientOptions.MessageEncoding=QueueMessageEncoding.Base64 and send raw
JSON. The host expects base64; do not encode twice.

Poll the application endpoint until status is Processed. The sample has a
child's name without DOB or strong identifier: the child remains PendingMatch
and no case is created. The repository's existing /api/mailboxes/... route is
the equivalent of the shorthand /mailbox/... shown in the diagram.

For manually authored spec hooks, seed Cosmos, select both fixture files,
start a dedicated Function host with that configuration, enqueue through
Azurite, wait for status, and compare the API result. Isolate database and
queue names per scenario; stop the host before deleting its resources.

## Live Graph

Set Processing__Graph__Mode=Live and provide:

| Setting | Value |
| --- | --- |
| Processing__Graph__TenantId | Entra tenant GUID |
| Processing__Graph__ClientId | Application GUID |
| Processing__Graph__ClientSecret | Secret from environment/secret store |
| Processing__Graph__Mailboxes__{mailboxId} | Mailbox primary SMTP address |

The adapter uses client credentials and the Graph .default scope. Configure
admin-consented Mail.Read application permission and appropriate mailbox
access. It requests /v1.0/users/{user}/messages/{graphMessageId} with plain-text
body content. The primary SMTP address is also used to identify outbound mail.
Graph credentials are unnecessary in Mock mode.

## Live extraction

The diagram does not identify an AI vendor or extraction deployment.
HttpEmailEntityExtractionService integrates with your configured EHCP
extraction HTTP service; it does not provision an AI model.

| Setting | Value |
| --- | --- |
| Processing__Extraction__Mode | Live |
| Processing__Extraction__Endpoint | Full HTTPS POST URL |
| Processing__Extraction__ApiKey | Optional X-Api-Key |

Request body: { "mailboxId": "...", "email": { ... } }, where email has the
same fields as the email object in sample-graph-email.json.

Response body: { "children": [ ... ], "summary": "..." }, with the same fields
as result in sample-ai-extract.json. Child fields are name, nhsNumber, caseId,
dateOfBirth, relationships and attributes. Dates are ISO 8601; confidence bands
use the existing Core string enums. Facts are extracted here; matching outcomes
and officer confirmation are determined by the application.

Non-success HTTP responses and malformed output fail the invocation and are
retried by the queue runtime. Extraction credentials are unnecessary in Mock mode.
