# EHCP logging: local Serilog / Azure Application Insights

All code logs through `ILogger<T>`. `Ehcp.Logging` configures the providers at Function App startup.
The target framework is .NET 10. The switch is **runtime environment**, not Debug/Release build configuration.

| Runtime environment | Destination | Azure subscription |
| --- | --- | --- |
| Development / Testing | Serilog console + daily `.clef` JSON files + optional Seq | Not needed |
| Staging / Production / anything else | Application Insights via Azure Monitor OpenTelemetry exporter | Required |

The environment selection uses `AZURE_FUNCTIONS_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`,
then `DOTNET_ENVIRONMENT`, then the host environment. `AZURE_FUNCTIONS_ENVIRONMENT` has priority.
This preserves the existing `rake up` behaviour, which sets `ASPNETCORE_ENVIRONMENT=Development`.

## Local use

1. Restore/build the solution as usual.
2. Optionally run Seq (default URL `http://localhost:5341`). Seq is not required for file/console logs.
3. Run `rake go` or run the app under Visual Studio / Azure Functions Core Tools.
4. Log files are generated in each Function App's output folder:
   `src/Ehcp.Processing/bin/Debug/net10.0/logs/Ehcp.Processing-YYYYMMDD.clef` (and equivalents for API / mocks).
5. Existing Reqnroll host logs remain under `tests/Ehcp.Specs/TestResults/local-stack/<run>/`.

Local configuration (environment variables or each app's `local.settings.json` Values):

- `EHCP_SEQ_ENABLED`: `true` by default; use `false` if not using Seq.
- `EHCP_SEQ_URL`: `http://localhost:5341` by default.
- `EHCP_LOG_FILE`: optional full log file template path. Normally leave unset.

No Application Insights connection string is needed locally. Keep real email bodies, NHS numbers,
DOB, names and sensitive extraction results out of logging statements and log scopes.

## Azure deployment

`infra/main.bicep` provisions Log Analytics and workspace-based Application Insights and passes
`APPLICATIONINSIGHTS_CONNECTION_STRING` to all four deployed Function Apps.
Each app has its own `OTEL_SERVICE_NAME` to distinguish telemetry in the shared resource.
The common `host.json` `telemetryMode: OpenTelemetry` enables host-side telemetry; the worker uses
`AddOpenTelemetry().UseFunctionsWorkerDefaults().UseAzureMonitorExporter()`.

Non-development startup intentionally fails if the Application Insights connection string is missing.
Configure environment `Production` or `Staging` on the deployed host; never set it to `Development`
in production. No logging data is uploaded to Azure while running the local Serilog provider.

Example Azure query:

```kusto
AppTraces
| where TimeGenerated > ago(24h)
| where SeverityLevel >= 2
| project TimeGenerated, Message, OperationId, Properties
| order by TimeGenerated desc
```

Logging shouldn't change queue retry behaviour: `ProcessEmailFunction` logs the exception and rethrows.
After five dequeues the existing host configuration sends the message to the poison queue.

## Verification

```powershell
dotnet restore Ehcp.slnx
dotnet build Ehcp.slnx
# Local environment, with test dependencies installed (Azurite, Cosmos emulator, Functions Core Tools):
rake go
# Logging-only unit tests:
dotnet test tests/Ehcp.Tests --filter FullyQualifiedName~LoggingEnvironmentTests
```
