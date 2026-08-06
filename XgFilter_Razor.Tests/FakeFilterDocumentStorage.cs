namespace XgFilter_Razor.Tests;

/// <summary>
/// Recording fake over the storage seam, shared by the store's unit tests and
/// the composite's wire tests: an in-memory document dictionary, a log of
/// every read and write, and switchable failure modes that throw the seam's
/// contractual <see cref="FilterStorageException"/>.
/// </summary>
internal sealed class FakeFilterDocumentStorage : IFilterDocumentStorage
{
    public Dictionary<string, string> Documents { get; } = new(StringComparer.Ordinal);
    public List<string> Reads { get; } = [];
    public List<(string FileName, string Json)> Writes { get; } = [];
    public bool ThrowOnRead { get; set; }
    public bool ThrowOnWrite { get; set; }

    // When set, reads resolve through this instead of Documents — the hook
    // the superseded-load test uses to hold a read in flight.
    public Func<string, Task<string?>>? ReadOverride { get; set; }

    public Task<string?> ReadAsync(string fileName)
    {
        Reads.Add(fileName);
        if (ThrowOnRead) throw new FilterStorageException("read failed");
        if (ReadOverride is not null) return ReadOverride(fileName);
        return Task.FromResult(
            Documents.TryGetValue(fileName, out var json) ? json : null);
    }

    public Task WriteAsync(string fileName, string json)
    {
        if (ThrowOnWrite) throw new FilterStorageException("write failed");
        Writes.Add((fileName, json));
        Documents[fileName] = json;
        return Task.CompletedTask;
    }
}
