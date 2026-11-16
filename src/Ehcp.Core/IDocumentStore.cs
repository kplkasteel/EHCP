namespace Ehcp.Core;

public sealed record StoredDocument<T>(T Document, string ETag);
public interface IDocumentStore<T> where T : class
{
    Task<StoredDocument<T>> ReadAsync(string mailboxId, string id, CancellationToken cancellationToken);
    Task<StoredDocument<T>> CreateAsync(string mailboxId, T document, CancellationToken cancellationToken);
    Task<StoredDocument<T>> ReplaceAsync(string mailboxId, string id, T document, string etag, CancellationToken cancellationToken);
    Task DeleteAsync(string mailboxId, string id, string etag, CancellationToken cancellationToken);
}
public enum StoreFailure { NotFound, Conflict, PreconditionFailed, Unavailable }
public sealed class DocumentStoreException(
    StoreFailure failure,
    string? message = null,
    Exception? innerException = null)
    : Exception(message ?? "Document storage operation failed.", innerException)
{
    public StoreFailure Failure { get; } = failure;
}
