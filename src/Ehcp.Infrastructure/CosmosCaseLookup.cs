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
        var query = new QueryDefinition("""
            SELECT * FROM c WHERE
              (@nhs != "" AND UPPER(TRIM(c.nhsNumber)) = @nhs)
              OR (@caseId != "" AND UPPER(TRIM(c.caseId)) = @caseId)
              OR (@name != "" AND @dob != "" AND UPPER(TRIM(c.name)) = @name AND c.dateOfBirth = @dob)
            """)
            .WithParameter("@nhs", Normalize(child.NhsNumber))
            .WithParameter("@caseId", Normalize(child.CaseId))
            .WithParameter("@name", Normalize(child.Name))
            .WithParameter("@dob", child.DateOfBirth?.ToString("yyyy-MM-dd") ?? "");
        var result = new List<CaseDocument>();
        using FeedIterator<CaseDocument> iterator = client.GetContainer(settings.Database, mailbox.CasesContainer)
            .GetItemQueryIterator<CaseDocument>(query);
        try
        {
            while (iterator.HasMoreResults)
                result.AddRange(await iterator.ReadNextAsync(ct));
        }
        catch (CosmosException)
        {
            throw new DocumentStoreException(StoreFailure.Unavailable);
        }
        return result;
    }

    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";
}
