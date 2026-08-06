namespace XgFilter_Razor;

using XgFilter_Lib.Filtering;

/// <summary>
/// Owns the <see cref="NamedFilterCollection"/> for the host's current source:
/// reads the saved-filters document at load time, applies save and delete
/// edits, and writes the document back through the host's
/// <see cref="IFilterDocumentStorage"/> adapter. Generalized from BgQuiz's
/// app-side original so both consumer apps share one encoding of the
/// saved-filters lifecycle; the host (or the FilterSurface composite) drives every
/// transition through an awaited call, so the page re-renders off
/// <see cref="Filters"/> / <see cref="Status"/> after each.
///
/// <para>
/// <b>Degrade, never block.</b> No member throws for saved-filters trouble: a
/// read or parse failure records nothing and preserves the file untouched
/// (<see cref="SavedFiltersStatus.LoadFailed"/>); a write failure keeps the
/// in-memory collection but stops writing
/// (<see cref="SavedFiltersStatus.WriteFailed"/>). The host's own flow is
/// never interrupted — a source with no usable saved-filters context is fully
/// functional, minus the saved-filters affordance. The one exception type this
/// contract rides on is <see cref="FilterStorageException"/>: adapters wrap
/// their native failures in it, and anything else propagates as a bug.
/// </para>
///
/// <para>
/// <b>Document identity and migration live in <see cref="SavedFiltersDocument"/>.</b>
/// This store reads the canonical name first, falls back to the legacy name
/// only when the canonical file is absent (never when it is corrupt — see the
/// identity type's rule), writes only the canonical name, and never deletes
/// the legacy file. Deliberately hardcoded to the saved-filters document: the
/// queued mix-saves sibling is a new store over its own identity, and any
/// store-level generalization is that arc's decision.
/// </para>
///
/// <para>
/// <b>Serialization is the document's own.</b>
/// <see cref="NamedFilterCollection"/> carries a type-level
/// <c>[JsonConverter]</c>, so this store round-trips through
/// <see cref="NamedFilterCollection.ToJson"/> /
/// <see cref="NamedFilterCollection.TryFromJson"/> — it owns no
/// <c>JsonSerializerOptions</c>.
/// </para>
/// </summary>
public sealed class SavedFiltersStore
{
    private readonly IFilterDocumentStorage? _storage;

    private NamedFilterCollection _filters = NamedFilterCollection.Empty;

    // Staleness key: bumped by every LoadAsync and Reset, so an in-flight read
    // superseded by a newer transition discards its outcome instead of
    // clobbering the newer context. The producer edition of BgQuiz's
    // PickGeneration discipline — defensive insurance on a single-threaded
    // sync context, not a live race.
    private int _loadVersion;

    /// <summary>
    /// Create a store over the host's storage adapter. A <c>null</c> adapter
    /// means the host has no readable storage at all — every
    /// <see cref="LoadAsync"/> then lands on
    /// <see cref="SavedFiltersStatus.Disabled"/> and mutations no-op, so an
    /// adapterless host still composes with the same store rather than a
    /// special case.
    /// </summary>
    /// <param name="storage">The host's document I/O, or <c>null</c> for none.</param>
    public SavedFiltersStore(IFilterDocumentStorage? storage) => _storage = storage;

    /// <summary>
    /// The current saved-filters document —
    /// <see cref="NamedFilterCollection.Empty"/> until a load lands one, and
    /// again after a <see cref="Reset"/>. A fresh instance after every
    /// save/delete (the wither returns a new collection), which is what lets
    /// <c>SavedFiltersPanel</c> observe the change and clear its pending
    /// confirms.
    /// </summary>
    public NamedFilterCollection Filters => _filters;

    /// <summary>The saved-filters context's condition; see <see cref="SavedFiltersStatus"/>.</summary>
    public SavedFiltersStatus Status { get; private set; } = SavedFiltersStatus.Disabled;

    /// <summary>
    /// The file the failed load was about — non-null exactly while
    /// <see cref="Status"/> is <see cref="SavedFiltersStatus.LoadFailed"/>.
    /// <see cref="SavedFiltersDocument.FileName"/> when the canonical file
    /// couldn't be read or parsed, <see cref="SavedFiltersDocument.LegacyFileName"/>
    /// when the failure happened on the legacy fallback — so a degrade notice
    /// can name the actual file the user should look at, rather than guessing
    /// the canonical name at a legacy-era folder.
    /// </summary>
    public string? LoadFailedFileName { get; private set; }

    /// <summary>
    /// Read the saved-filters document, re-deriving the whole context — called
    /// by the host after its source is (re)established. Applies the two-name
    /// migration rule (<see cref="SavedFiltersDocument"/>) and swallows every
    /// storage failure into <see cref="Status"/> — it never throws, so the
    /// host's flow is never blocked by saved-filters trouble.
    /// </summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;

        if (_storage is null)
        {
            _filters = NamedFilterCollection.Empty;
            Status = SavedFiltersStatus.Disabled;
            LoadFailedFileName = null;
            return;
        }

        // Which file the load is currently about — what LoadFailedFileName
        // reports if this attempt degrades.
        var fileName = SavedFiltersDocument.FileName;
        try
        {
            var json = await _storage.ReadAsync(fileName);
            if (version != _loadVersion) return;

            if (json is null)
            {
                // Canonical file absent — the one case that falls back to the
                // legacy name. A present-but-corrupt canonical file must NOT
                // reach here: falling back would resurrect stale legacy data
                // over newer-but-corrupt data (see SavedFiltersDocument).
                fileName = SavedFiltersDocument.LegacyFileName;
                json = await _storage.ReadAsync(fileName);
                if (version != _loadVersion) return;
            }

            if (json is null)
            {
                // Neither file yet — a fresh source. Ready over the empty
                // collection; the first save writes the canonical file into
                // being.
                _filters = NamedFilterCollection.Empty;
                Status = SavedFiltersStatus.Ready;
                LoadFailedFileName = null;
            }
            else if (NamedFilterCollection.TryFromJson(json, out var loaded))
            {
                _filters = loaded;
                Status = SavedFiltersStatus.Ready;
                LoadFailedFileName = null;
            }
            else
            {
                // A file exists but is corrupt / foreign / newer-schema
                // (TryFromJson false on non-null input). Preserve it untouched
                // — never write the empty fallback over the user's file — and
                // degrade to a notice naming it.
                _filters = NamedFilterCollection.Empty;
                Status = SavedFiltersStatus.LoadFailed;
                LoadFailedFileName = fileName;
            }
        }
        catch (FilterStorageException)
        {
            if (version != _loadVersion) return;
            // The adapter failed the read. Degrade — no saved filters for this
            // source, the file untouched.
            _filters = NamedFilterCollection.Empty;
            Status = SavedFiltersStatus.LoadFailed;
            LoadFailedFileName = fileName;
        }
    }

    /// <summary>
    /// Save <paramref name="config"/> under <paramref name="name"/> (add or
    /// replace) and persist. No-op unless the context is
    /// <see cref="SavedFiltersStatus.Ready"/> — the handler guard behind a
    /// host's <c>CanPersist</c> gate, holding even if a disabled button is
    /// bypassed.
    /// </summary>
    /// <param name="name">The saved-filter name, pre-normalized per <see cref="NamedFilterCollection.With"/>'s contract.</param>
    /// <param name="config">The config to save.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="config"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> violates the collection's name rule (blank or
    /// untrimmed) — the panel pre-normalizes, so this is a caller-contract
    /// guard, not a user-facing path.
    /// </exception>
    public Task SaveAsync(string name, FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(config);
        if (Status != SavedFiltersStatus.Ready || _storage is null) return Task.CompletedTask;
        return PersistAsync(_filters.With(name, config));
    }

    /// <summary>
    /// Remove the named filter (idempotent) and persist. No-op unless the
    /// context is <see cref="SavedFiltersStatus.Ready"/>.
    /// </summary>
    /// <param name="name">The saved-filter name to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Task DeleteAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Status != SavedFiltersStatus.Ready || _storage is null) return Task.CompletedTask;
        return PersistAsync(_filters.Without(name));
    }

    /// <summary>
    /// Reset to the no-context state — called when the host's source goes
    /// away. The next <see cref="LoadAsync"/> re-derives everything; an
    /// in-flight load is expired and discards its outcome.
    /// </summary>
    public void Reset()
    {
        _loadVersion++;
        _filters = NamedFilterCollection.Empty;
        Status = SavedFiltersStatus.Disabled;
        LoadFailedFileName = null;
    }

    /// <summary>
    /// Adopt <paramref name="updated"/> in memory first, then write it back —
    /// to the canonical name only, never the legacy one. On a write failure
    /// the in-memory collection is kept (the pick list reflects the change)
    /// while writes stop for this context
    /// (<see cref="SavedFiltersStatus.WriteFailed"/>).
    /// </summary>
    private async Task PersistAsync(NamedFilterCollection updated)
    {
        _filters = updated;
        try
        {
            await _storage!.WriteAsync(SavedFiltersDocument.FileName, updated.ToJson());
        }
        catch (FilterStorageException)
        {
            Status = SavedFiltersStatus.WriteFailed;
        }
    }
}
