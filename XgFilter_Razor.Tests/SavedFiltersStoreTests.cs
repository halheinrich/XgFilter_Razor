using XgFilter_Lib.Filtering;

namespace XgFilter_Razor.Tests;

public class SavedFiltersStoreTests
{
    private static string CollectionJson(params string[] names)
    {
        var filters = NamedFilterCollection.Empty;
        foreach (var name in names)
            filters = filters.With(name, new FilterConfig());
        return filters.ToJson();
    }

    [Fact]
    public async Task NullAdapter_Load_LandsOnDisabled()
    {
        var store = new SavedFiltersStore(storage: null);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    // A null adapter also no-ops mutations — the adapterless host composes
    // with the same store rather than a special case.
    [Fact]
    public async Task NullAdapter_Save_NoOps()
    {
        var store = new SavedFiltersStore(storage: null);
        await store.LoadAsync();

        await store.SaveAsync("Race", new FilterConfig());

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    [Fact]
    public async Task Load_NeitherFileExists_ReadyOverEmpty()
    {
        var storage = new FakeFilterDocumentStorage();
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        Assert.Equal(0, store.Filters.Count);
        // Both names were tried, canonical first.
        Assert.Equal(
            [SavedFiltersDocument.FileName, SavedFiltersDocument.LegacyFileName],
            storage.Reads);
    }

    [Fact]
    public async Task Load_CanonicalParses_Ready_AndLegacyIsNeverRead()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = CollectionJson("Race");
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        Assert.True(store.Filters.Contains("Race"));
        Assert.Equal([SavedFiltersDocument.FileName], storage.Reads);
    }

    // The ratified no-fallback-on-corrupt rule: a present-but-unparseable
    // canonical file lands on LoadFailed with NO legacy read — falling back
    // would resurrect stale legacy data while newer-but-corrupt data exists.
    [Fact]
    public async Task Load_CanonicalCorrupt_LoadFailed_WithoutLegacyFallback()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        storage.Documents[SavedFiltersDocument.LegacyFileName] = CollectionJson("Race");
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
        Assert.Equal(0, store.Filters.Count);
        Assert.Equal([SavedFiltersDocument.FileName], storage.Reads);
    }

    // ...and LoadFailed keeps saving dead, so the corrupt file can never be
    // overwritten: preserve-file-on-corrupt is enforced by the store, not by
    // the host's CanPersist courtesy.
    [Fact]
    public async Task Save_UnderLoadFailed_NoOps_AndWritesNothing()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        var store = new SavedFiltersStore(storage);
        await store.LoadAsync();

        await store.SaveAsync("Race", new FilterConfig());

        Assert.Empty(storage.Writes);
        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    // The migration rule end to end: canonical absent → the legacy document
    // is adopted; the first save then writes the canonical name only, and the
    // legacy file is neither rewritten nor deleted.
    [Fact]
    public async Task Load_LegacyFallback_AdoptsLegacy_AndFirstSaveWritesCanonicalOnly()
    {
        var storage = new FakeFilterDocumentStorage();
        var legacyJson = CollectionJson("Race");
        storage.Documents[SavedFiltersDocument.LegacyFileName] = legacyJson;
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        Assert.True(store.Filters.Contains("Race"));

        await store.SaveAsync("Blitz", new FilterConfig());

        var write = Assert.Single(storage.Writes);
        Assert.Equal(SavedFiltersDocument.FileName, write.FileName);
        // The migrated document carries the legacy content plus the new save...
        Assert.True(store.Filters.Contains("Race"));
        Assert.True(store.Filters.Contains("Blitz"));
        // ...while the legacy file survives byte-for-byte.
        Assert.Equal(legacyJson, storage.Documents[SavedFiltersDocument.LegacyFileName]);
    }

    [Fact]
    public async Task Load_LegacyCorrupt_LoadFailed()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.LegacyFileName] = "not a filters document";
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    [Fact]
    public async Task Load_ReadThrowsStorageException_LoadFailed()
    {
        var storage = new FakeFilterDocumentStorage { ThrowOnRead = true };
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersStatus.LoadFailed, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    // ── LoadFailedFileName: non-null exactly while LoadFailed, naming the
    // actual file the failed load was about — what the composite's degrade
    // notice renders, so a legacy-era folder is never told to look for a
    // canonical file that doesn't exist.

    [Fact]
    public async Task LoadFailedFileName_NullWhileReady_AndWhileDisabled()
    {
        var store = new SavedFiltersStore(new FakeFilterDocumentStorage());

        Assert.Null(store.LoadFailedFileName); // Disabled (initial)
        await store.LoadAsync();
        Assert.Null(store.LoadFailedFileName); // Ready
    }

    [Fact]
    public async Task LoadFailedFileName_CanonicalCorrupt_NamesCanonical()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersDocument.FileName, store.LoadFailedFileName);
    }

    [Fact]
    public async Task LoadFailedFileName_LegacyCorrupt_NamesLegacy()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.LegacyFileName] = "not a filters document";
        var store = new SavedFiltersStore(storage);

        await store.LoadAsync();

        Assert.Equal(SavedFiltersDocument.LegacyFileName, store.LoadFailedFileName);
    }

    [Fact]
    public async Task LoadFailedFileName_ReadThrows_NamesTheFileBeingRead()
    {
        // First read (canonical) throws → the canonical name is the one to report.
        var storage = new FakeFilterDocumentStorage { ThrowOnRead = true };
        var store = new SavedFiltersStore(storage);
        await store.LoadAsync();
        Assert.Equal(SavedFiltersDocument.FileName, store.LoadFailedFileName);

        // Canonical absent, legacy read throws → the legacy name.
        var storage2 = new FakeFilterDocumentStorage();
        storage2.ReadOverride = name =>
            name == SavedFiltersDocument.FileName
                ? Task.FromResult<string?>(null)
                : throw new FilterStorageException("read failed");
        var store2 = new SavedFiltersStore(storage2);
        await store2.LoadAsync();
        Assert.Equal(SavedFiltersDocument.LegacyFileName, store2.LoadFailedFileName);
    }

    [Fact]
    public async Task LoadFailedFileName_ClearedByRecoveredReload_AndByReset()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        var store = new SavedFiltersStore(storage);
        await store.LoadAsync();
        Assert.NotNull(store.LoadFailedFileName);

        storage.Documents[SavedFiltersDocument.FileName] = CollectionJson("Race");
        await store.LoadAsync();
        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
        Assert.Null(store.LoadFailedFileName);

        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        await store.LoadAsync();
        store.Reset();
        Assert.Null(store.LoadFailedFileName);
    }

    // The WriteFailed posture: the in-memory collection keeps the edit (the
    // pick list stays truthful) but no further writes are attempted — even
    // after the storage recovers, because the status gate holds until the
    // next load re-derives the context.
    [Fact]
    public async Task Save_WriteThrows_WriteFailed_KeepsEditInMemory_AndStopsFurtherWrites()
    {
        var storage = new FakeFilterDocumentStorage();
        var store = new SavedFiltersStore(storage);
        await store.LoadAsync();

        storage.ThrowOnWrite = true;
        await store.SaveAsync("Race", new FilterConfig());

        Assert.Equal(SavedFiltersStatus.WriteFailed, store.Status);
        Assert.True(store.Filters.Contains("Race"));
        Assert.Empty(storage.Writes);

        storage.ThrowOnWrite = false;
        await store.SaveAsync("Blitz", new FilterConfig());

        Assert.Empty(storage.Writes);
        Assert.False(store.Filters.Contains("Blitz"));
    }

    [Fact]
    public async Task Delete_RemovesAndPersists()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = CollectionJson("Race", "Blitz");
        var store = new SavedFiltersStore(storage);
        await store.LoadAsync();

        await store.DeleteAsync("Race");

        Assert.False(store.Filters.Contains("Race"));
        Assert.True(store.Filters.Contains("Blitz"));
        var write = Assert.Single(storage.Writes);
        Assert.Equal(SavedFiltersDocument.FileName, write.FileName);
        Assert.Equal(SavedFiltersStatus.Ready, store.Status);
    }

    [Fact]
    public async Task Reset_ReturnsToDisabledOverEmpty()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = CollectionJson("Race");
        var store = new SavedFiltersStore(storage);
        await store.LoadAsync();

        store.Reset();

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    // The staleness guard: a load whose read is still in flight when a newer
    // transition (here a Reset) supersedes it must discard its outcome rather
    // than clobber the newer context.
    [Fact]
    public async Task SupersededLoad_DiscardsItsOutcome()
    {
        var storage = new FakeFilterDocumentStorage();
        var pendingRead = new TaskCompletionSource<string?>();
        storage.ReadOverride = _ => pendingRead.Task;
        var store = new SavedFiltersStore(storage);

        var load = store.LoadAsync();
        store.Reset();
        pendingRead.SetResult(CollectionJson("Race"));
        await load;

        Assert.Equal(SavedFiltersStatus.Disabled, store.Status);
        Assert.Equal(0, store.Filters.Count);
    }

    [Fact]
    public async Task Save_NullArguments_Throw()
    {
        var store = new SavedFiltersStore(new FakeFilterDocumentStorage());
        await store.LoadAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.SaveAsync(null!, new FilterConfig()));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.SaveAsync("Race", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => store.DeleteAsync(null!));
    }
}
