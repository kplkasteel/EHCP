# Pipeline scripts

PowerShell scripts used by `pr-validation.yml` and `templates/install-tools.yml`. All run on PowerShell 7 (`pwsh`).

| Script | Runs | Purpose |
|---|---|---|
| `start-cosmos.ps1` | Pipeline, first step | Installs the Cosmos DB emulator if it's missing, then starts it in the background. |
| `install-azurite-func.ps1` | Pipeline, tool install | Installs Azurite and Azure Functions Core Tools at pinned versions. Skips the install if both already run. |
| `wait-for-cosmos.ps1` | Pipeline, before tests | Waits until the emulator answers an authenticated account read over trusted TLS. |
| `verify-toolchain.ps1` | Pipeline, before tests | Prints the version of each tool, including the Cosmos emulator and SDK. Fails if any is missing. |
| `summarise-results.ps1` | Pipeline, after tests (always) | Reads the unit and Specs `.trx` files and writes `artifacts/summary.json`. |
| `post-pr-comment.ps1` | Pipeline, PR builds only (always) | Posts or updates one comment on the PR, from `summary.json`. A failed post is a warning. |

## Running locally

Run from the repository root:

```powershell
pwsh .azuredevops/scripts/verify-toolchain.ps1
pwsh .azuredevops/scripts/summarise-results.ps1 -OverallStatus passed
pwsh .azuredevops/scripts/post-pr-comment.ps1 -DryRun
```

`post-pr-comment.ps1 -DryRun` prints the comment without posting. It needs `SYSTEM_COLLECTIONURI`, `SYSTEM_TEAMPROJECT` and `BUILD_BUILDID` set. The other scripts need the Cosmos emulator or a PR context, so run them only in the pipeline.

## Notes

- `summarise-results.ps1` maps the pipeline's `Succeeded` or `Failed` status to `passed` or `failed`.
- `post-pr-comment.ps1` finds its comment by the hidden marker `<!-- ehcp-pr-validation -->`. Don't remove that marker from the comment text.
- Thread status: a passing run sets the comment to Resolved. A failing run leaves it Active.
