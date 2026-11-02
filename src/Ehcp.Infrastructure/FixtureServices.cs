using System.Text.Json;
using Ehcp.Core;

namespace Ehcp.Infrastructure;

// The mailbox and message checks prevent a fixture silently serving another scenario.
public sealed class MockGraphAPIService(GraphSettings settings) : IGraphAPIService
{
    public async Task<GraphEmail> GetEmailAsync(string mailboxId, string graphMessageId, CancellationToken ct)
    {
        GraphFixture fixture = await FixtureFile.ReadAsync<GraphFixture>(settings.DataPath, ct);
        if (fixture.MailboxId != mailboxId || fixture.Email.Id != graphMessageId)
            throw new DocumentValidationException("Graph fixture does not match the requested mailbox and message.");
        return fixture.Email;
    }
}

public sealed class MockEhcpExtractionService(ExtractionSettings settings) : IEmailEntityExtractionService
{
    public async Task<EmailExtraction> ExtractAsync(string mailboxId, GraphEmail email, CancellationToken ct)
    {
        ExtractionFixture fixture = await FixtureFile.ReadAsync<ExtractionFixture>(settings.DataPath, ct);
        if (fixture.MailboxId != mailboxId || fixture.EmailId != email.Id)
            throw new DocumentValidationException("Extraction fixture does not match the requested mailbox and message.");
        return fixture.Result;
    }
}

public sealed record GraphFixture(string MailboxId, GraphEmail Email);
public sealed record ExtractionFixture(string MailboxId, string EmailId, EmailExtraction Result);

internal static class FixtureFile
{
    internal static async Task<T> ReadAsync<T>(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, CosmosDocumentStore<EmailDocument>.JsonOptions, ct)
            ?? throw new DocumentValidationException("Fixture is empty.");
    }
}
