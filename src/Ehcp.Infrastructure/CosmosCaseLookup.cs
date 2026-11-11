using System.Globalization;
using Ehcp.Core;
using Microsoft.Azure.Cosmos;

namespace Ehcp.Infrastructure;

public sealed class CosmosCaseLookup(CosmosClient client, CosmosSettings settings) : ICaseLookup
{
    public async Task<IReadOnlyList<CaseDocument>> FindCandidatesAsync(
        string mailboxId, ExtractedChild child, CancellationToken ct)
    {
        if (!settings.Mailboxes.TryGetValue(mailboxId, out MailboxSettings? mailbox))
            throw new DocumentStoreException(StoreFailure.NotFound);
        StrongIdentifiers identifiers = StrongIdentifiers.From(child);
        QueryDefinition query = new QueryDefinition("""
                                                    SELECT * FROM c WHERE
                                                      (@nhs != "" AND UPPER(TRIM(c.nhsNumber)) = @nhs)
                                                      OR ARRAY_CONTAINS(@caseIds, UPPER(TRIM(c.caseId)))
                                                      OR EXISTS (
                                                        SELECT VALUE a FROM a IN c.attributes WHERE
                                                          (UPPER(TRIM(a.type)) = "SYNERGYID" AND ARRAY_CONTAINS(@caseIds, UPPER(TRIM(a["value"]))))
                                                          OR (UPPER(TRIM(a.type)) = "DFEID" AND ARRAY_CONTAINS(@dfeIds, UPPER(TRIM(a["value"]))))
                                                          OR (UPPER(TRIM(a.type)) = "MOSAICID" AND ARRAY_CONTAINS(@mosaicIds, UPPER(TRIM(a["value"]))))
                                                      )
                                                      OR (@name != "" AND @dob != "" AND UPPER(TRIM(c.name)) = @name AND c.dateOfBirth = @dob)
                                                    """)
            .WithParameter("@nhs", Normalize(child.NhsNumber))
            .WithParameter("@caseIds", identifiers.CaseIds)
            .WithParameter("@dfeIds", identifiers.DfeIds)
            .WithParameter("@mosaicIds", identifiers.MosaicIds)
            .WithParameter("@name", Normalize(child.Name))
            .WithParameter("@dob", child.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
        var result = new List<CaseDocument>();
        using FeedIterator<CaseDocument> iterator = client.GetContainer(settings.Database, mailbox.CasesContainer)
            .GetItemQueryIterator<CaseDocument>(query);
        try
        {
            while (iterator.HasMoreResults)
                result.AddRange(await iterator.ReadNextAsync(ct));
        }
        catch (CosmosException error)
        {
            throw new DocumentStoreException(StoreFailure.Unavailable, error.Message, error);
        }

        return result;
    }

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";
}