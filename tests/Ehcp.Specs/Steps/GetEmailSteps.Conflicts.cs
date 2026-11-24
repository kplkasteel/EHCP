using System.Text.Json;
using Reqnroll;
using Xunit;

namespace Ehcp.Specs.Steps;

// Checks the conflicts recorded on a seeded case after the email is processed.
// Expected value: "Band: matchedOn" for one conflict raised by this email, or blank for no conflicts.
//   e.g. "High: NHS number matches; Synergy ID does not match"
// flaggedAt is set by the product at processing time, so the step checks it is present, not its value.
public sealed partial class GetEmailSteps
{
    [Then("the case {string} should have conflict {string}")]
    public async Task ThenTheCaseShouldHaveConflict(string caseId, string expectedConflict)
    {
        string emailId = RequiredScenarioValue("emailId");
        using HttpResponseMessage response = await _http.GetAsync(
            $"/api/mailboxes/{Uri.EscapeDataString(_mailboxId)}/cases/{Uri.EscapeDataString(ResolveCaseId(caseId))}");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement conflicts = document.RootElement.GetProperty("conflicts");
        _output.WriteLine($"[CASE CONFLICTS] {caseId}: {conflicts.GetRawText()}");

        if (string.IsNullOrWhiteSpace(expectedConflict))
        {
            Assert.True(conflicts.GetArrayLength() == 0,
                $"Expected no conflicts on '{caseId}', found {conflicts.GetRawText()}.");
            return;
        }

        string[] parts = expectedConflict.Split(": ", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            throw new InvalidOperationException(
                $"Invalid expected conflict '{expectedConflict}'. Use 'Band: matchedOn', or leave it blank for none.");

        Assert.True(conflicts.GetArrayLength() == 1,
            $"Expected one conflict on '{caseId}', found {conflicts.GetRawText()}.");
        JsonElement conflict = conflicts[0];
        Assert.Equal(emailId, conflict.GetProperty("emailId").GetString());
        Assert.Equal(parts[0], conflict.GetProperty("confidenceBand").GetString());
        Assert.Equal(parts[1], conflict.TryGetProperty("matchedOn", out JsonElement matchedOn)
            && matchedOn.ValueKind == JsonValueKind.String ? matchedOn.GetString() : null);
        Assert.True(conflict.TryGetProperty("flaggedAt", out JsonElement flaggedAt)
            && flaggedAt.ValueKind == JsonValueKind.String && flaggedAt.GetString()!.Length > 0,
            $"Expected flaggedAt on the conflict for '{caseId}'.");
    }
}
