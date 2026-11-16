using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Reqnroll;

namespace Ehcp.Specs.Steps;

// Fixtures named case-creation-* are templates, filled from the Scenario Outline's tables for this scenario.
// Files without tokens are returned unchanged, so other scenarios are unaffected.
//   {{name}}               string, JSON-escaped; sits inside quotes already in the file
//   {{name:json}}          JSON literal: quoted string, or null when the identifier is blank
//   {{attributes}}         JSON array of non-typed identifiers with confidenceBand (ai-extract input)
//   {{expectedAttributes}} JSON array of non-typed identifiers with type and value only (expected output)
public sealed partial class GetEmailSteps
{
    private const string GraphMessageIdPrefix = "AAMkAGE1-spec-";
    private static readonly Regex TokenPattern = new(@"\{\{(\w+)(:json)?\}\}", RegexOptions.Compiled);
    private static readonly HashSet<string> TypedIdentifiers = new(StringComparer.Ordinal)
    {
        "childName", "nhsNumber", "dateOfBirth"
    };

    private Dictionary<string, string> _scenarioValues = new(StringComparer.Ordinal);
    private Dictionary<string, string>? _childIdentifiers;

    [Given("the scenario values are")]
    public void GivenTheScenarioValuesAre(DataTable table) =>
        _scenarioValues = ReadPairs(table, "key", "value");

    [Given("the child identifiers are")]
    public void GivenTheChildIdentifiersAre(DataTable table) =>
        _childIdentifiers = ReadPairs(table, "identifier", "value")
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private async Task<string> RenderDatasetAsync(string dataset)
    {
        string text = await File.ReadAllTextAsync(DatasetPath(dataset));
        if (!text.Contains("{{", StringComparison.Ordinal)) return text;

        Dictionary<string, string> identifiers = _childIdentifiers
            ?? throw new InvalidOperationException("Set 'the child identifiers are' before using dataset templates.");
        Dictionary<string, string?> tokens = BuildTokens(identifiers);
        Dictionary<string, string> fragments = BuildFragments(identifiers);
        return TokenPattern.Replace(text, match => ResolveToken(match, tokens, fragments));
    }

    private Dictionary<string, string?> BuildTokens(Dictionary<string, string> identifiers)
    {
        string emailId = RequiredScenarioValue("emailId");
        string? childName = identifiers.GetValueOrDefault("childName");
        string childLabel = childName ?? "my child";

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["emailId"] = emailId,
            ["conversationId"] = RequiredScenarioValue("conversationId"),
            ["parentName"] = RequiredScenarioValue("parentName"),
            ["parentEmail"] = RequiredScenarioValue("parentEmail"),
            ["graphMessageId"] = GraphMessageIdPrefix + emailId,
            ["caseAlias"] = emailId + "-caseId",
            ["childName"] = childName,
            ["childLabel"] = childLabel,
            ["nhsNumber"] = identifiers.GetValueOrDefault("nhsNumber"),
            ["dateOfBirth"] = identifiers.GetValueOrDefault("dateOfBirth"),
            ["subject"] = $"Request for support for {childLabel}",
            ["identifierLines"] = string.Join("\n", identifiers
                .Where(pair => pair.Key != "childName")
                .Select(pair => $"{IdentifierLabel(pair.Key)}: {pair.Value}")),
        };
    }

    private static Dictionary<string, string> BuildFragments(Dictionary<string, string> identifiers)
    {
        IEnumerable<KeyValuePair<string, string>> others = identifiers
            .Where(pair => !TypedIdentifiers.Contains(pair.Key));
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attributes"] = JsonSerializer.Serialize(others
                .Select(pair => new { type = pair.Key, value = pair.Value, confidenceBand = "High" })),
            ["expectedAttributes"] = JsonSerializer.Serialize(others
                .Select(pair => new { type = pair.Key, value = pair.Value })),
        };
    }

    private static string ResolveToken(
        Match match, IReadOnlyDictionary<string, string?> tokens, IReadOnlyDictionary<string, string> fragments)
    {
        string name = match.Groups[1].Value;
        if (fragments.TryGetValue(name, out string? fragment)) return fragment;
        if (!tokens.TryGetValue(name, out string? value))
            throw new InvalidOperationException("Unknown dataset token '" + name + "'.");
        if (match.Groups[2].Success)
            return value is null ? "null" : JsonSerializer.Serialize(value);
        return value is null ? string.Empty : JsonSerializer.Serialize(value)[1..^1];
    }

    private string RequiredScenarioValue(string key) =>
        _scenarioValues.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : throw new InvalidOperationException($"Set '{key}' in 'the scenario values are'.");

    private static Dictionary<string, string> ReadPairs(DataTable table, string keyColumn, string valueColumn)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DataTableRow row in table.Rows)
            pairs.Add(row[keyColumn].Trim(), row[valueColumn].Trim());
        return pairs;
    }

    private static string IdentifierLabel(string identifier) => identifier switch
    {
        "nhsNumber" => "NHS number",
        "dateOfBirth" => "Date of birth",
        _ => identifier
    };

    // Attribute identity is type + value. App-assigned fields (confidenceBand, sourceEmailId) are not known
    // to the test, so each expected attribute keeps only the keys it declares before comparison.
    private static void ProjectAttributes(JsonNode? expected, JsonNode? actual)
    {
        switch (expected, actual)
        {
            case (JsonObject expectedObject, JsonObject actualObject):
                foreach (KeyValuePair<string, JsonNode?> pair in expectedObject)
                {
                    if (!actualObject.ContainsKey(pair.Key)) continue;
                    if (pair.Key == "attributes"
                        && pair.Value is JsonArray expectedAttributes
                        && actualObject[pair.Key] is JsonArray actualAttributes)
                        ProjectAttributeArray(expectedAttributes, actualAttributes);
                    else
                        ProjectAttributes(pair.Value, actualObject[pair.Key]);
                }
                break;
            case (JsonArray expectedArray, JsonArray actualArray):
                for (int i = 0; i < Math.Min(expectedArray.Count, actualArray.Count); i++)
                    ProjectAttributes(expectedArray[i], actualArray[i]);
                break;
        }
    }

    private static void ProjectAttributeArray(JsonArray expected, JsonArray actual)
    {
        var remaining = actual.Select(item => item?.DeepClone()).ToList();
        var projected = new List<JsonNode?>();
        foreach (JsonNode? expectedItem in expected)
        {
            string? type = expectedItem?["type"]?.GetValue<string>();
            JsonNode? match = remaining.FirstOrDefault(item => item?["type"]?.GetValue<string>() == type);
            if (match is null) continue;

            remaining.Remove(match);
            var projectedItem = new JsonObject();
            foreach (string key in expectedItem!.AsObject().Select(pair => pair.Key))
                projectedItem[key] = match[key]?.DeepClone();
            projected.Add(projectedItem);
        }
        projected.AddRange(remaining);

        actual.Clear();
        foreach (JsonNode? item in projected) actual.Add(item);
    }
}
