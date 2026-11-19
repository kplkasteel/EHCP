using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ehcp.Specs.Steps;

// Per-example diagnostics. Lines go to the Reqnroll output, which the TRX file keeps as StdOut,
// so the living documentation report shows them against the scenario that wrote them.
public sealed partial class GetEmailSteps
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private void WriteScenarioInputs()
    {
        _output.WriteLine("[SCENARIO INPUTS]");
        foreach (KeyValuePair<string, string> pair in _scenarioValues)
            _output.WriteLine($"  {pair.Key}: {pair.Value}");
        _output.WriteLine("  child identifiers:");
        foreach (KeyValuePair<string, string> pair in _childIdentifiers ?? new Dictionary<string, string>())
            _output.WriteLine($"    {pair.Key}: {pair.Value}");
    }

    private async Task WriteDatasetAsync(string heading, string dataset)
    {
        JsonNode? root = JsonNode.Parse(await RenderDatasetAsync(dataset));
        _output.WriteLine($"[{heading}] {dataset}");
        _output.WriteLine(root?.ToJsonString(IndentedJson) ?? "  (empty)");
    }

    // Rows compare by position, matching the order-sensitive comparison that decides the test.
    // Returns the report lines so the assertion message can carry the same table.
    private List<string> WriteMatchReport(JsonArray expectedChildren, JsonArray actualChildren)
    {
        var lines = new List<string>();
        for (int i = 0; i < expectedChildren.Count; i++)
        {
            if (expectedChildren[i]?["possibleCaseMatches"] is not JsonArray expected) continue;
            JsonArray actual = actualChildren[i]?["possibleCaseMatches"] as JsonArray ?? new JsonArray();

            bool matches = expected.Count == actual.Count;
            var rows = new List<string>();
            int count = Math.Max(expected.Count, actual.Count);
            for (int r = 0; r < count; r++)
            {
                JsonNode? e = r < expected.Count ? expected[r] : null;
                JsonNode? a = r < actual.Count ? actual[r] : null;
                bool same = e is not null && a is not null && JsonNode.DeepEquals(e, a);
                matches &= same;
                rows.Add($"  {r + 1,-3}{Describe(e),-48}{Describe(a),-48}{(same ? string.Empty : "<-- differs")}");
            }

            lines.Add($"possibleCaseMatches, child {i}: {(matches ? "MATCH" : "MISMATCH")}");
            lines.Add($"  {"#",-3}{"expected (entityId band score)",-48}actual (entityId band score)");
            lines.AddRange(rows);
        }

        foreach (string line in lines) _output.WriteLine(line);
        return lines;
    }

    private static string Describe(JsonNode? match) => match is null
        ? "-"
        : $"{match["entityId"]?.GetValue<string>()}  {match["confidenceBand"]?.GetValue<string>()}  {match["score"]?.ToJsonString()}";
}
