namespace XgFilter_Razor;

using BgDataTypes_Lib;

/// <summary>
/// Owns a <see cref="NamedCollection{TValue, TSelf}"/> document for the host's
/// current source: reads it at load time, applies save and delete edits, and
/// writes it back through the host's <see cref="IDocumentStorage"/>
/// adapter. The host (or the composite that mounts the pick list) drives every
/// transition through an awaited call, so the page re-renders off
/// <see cref="Document"/> / <see cref="Status"/> after each.
///
/// <para>
/// <b>A specialization owns only its identity.</b> Everything above — the
/// lifecycle, the postures below, the staleness guard, the two-name migration
/// rule — belongs here, once, for every named document a host keeps. A sealed
/// derived store contributes <see cref="FileName"/> and, when it supersedes an
/// older name, <see cref="LegacyFileName"/>; nothing else. There are
/// deliberately no domain-named forwarders on a specialization
/// (halheinrich/backgammon#190 leg (D)): a second name for
/// <see cref="Document"/> would be a second thing to keep true.
/// </para>
///
/// <para>
/// <b>Degrade, never block.</b> No member throws for storage trouble: a read
/// or parse failure records nothing and preserves the file untouched
/// (<see cref="NamedDocumentStatus.LoadFailed"/>); a write failure keeps the
/// in-memory collection but stops writing
/// (<see cref="NamedDocumentStatus.WriteFailed"/>). The host's own flow is
/// never interrupted — a source with no usable document context is fully
/// functional, minus the affordance that document powers. The one exception
/// type this contract rides on is <see cref="DocumentStorageException"/>:
/// adapters wrap their native failures in it, and anything else propagates as
/// a bug.
/// </para>
///
/// <para>
/// <b>The two-name migration rule</b> runs here whenever a specialization
/// supplies a <see cref="LegacyFileName"/>, and is a no-op when it does not
/// (a document with only ever one name never reads a second file). Read
/// <see cref="FileName"/> first and fall back to the legacy name only when the
/// canonical file is <i>absent</i> — never when it is present but unparseable,
/// which would resurrect stale data over newer-but-corrupt data. Write only
/// the canonical name — the first save after a legacy fallback is what
/// migrates the document — and never delete the legacy file.
/// </para>
///
/// <para>
/// <b>Serialization is the document's own.</b> A specialization of
/// <see cref="NamedCollection{TValue, TSelf}"/> carries a type-level
/// <c>[JsonConverter]</c>, so this store round-trips through
/// <see cref="NamedCollection{TValue, TSelf}.ToJson"/> /
/// <see cref="NamedCollection{TValue, TSelf}.TryFromJson"/> — it owns no
/// <c>JsonSerializerOptions</c>.
/// </para>
/// </summary>
/// <typeparam name="TValue">
/// The payload stored under each name; see
/// <see cref="NamedCollection{TValue, TSelf}"/>.
/// </typeparam>
/// <typeparam name="TSelf">
/// The collection specialization this store holds; see
/// <see cref="NamedCollection{TValue, TSelf}"/>.
/// </typeparam>
public abstract class NamedDocumentStore<TValue, TSelf>
    where TValue : IJsonDocument<TValue>
    where TSelf : NamedCollection<TValue, TSelf>, INamedCollectionSpecialization<TValue, TSelf>
{
    private readonly IDocumentStorage? _storage;

    private TSelf _document = NamedCollection<TValue, TSelf>.Empty;

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
    /// <see cref="NamedDocumentStatus.Disabled"/> and mutations no-op, so an
    /// adapterless host still composes with the same store rather than a
    /// special case.
    /// </summary>
    /// <param name="storage">The host's document I/O, or <c>null</c> for none.</param>
    protected NamedDocumentStore(IDocumentStorage? storage) => _storage = storage;

    /// <summary>
    /// The canonical file name this store reads and writes — the only name
    /// ever written. The whole of a specialization's required contribution.
    /// </summary>
    protected abstract string FileName { get; }

    /// <summary>
    /// An older name this document supersedes, read as a fallback when
    /// <see cref="FileName"/> is absent; never written, never deleted.
    /// <see langword="null"/> — the default — means the document has only ever
    /// had one name, and the fallback read does not happen at all.
    /// </summary>
    protected virtual string? LegacyFileName => null;

    /// <summary>
    /// The current document — <see cref="NamedCollection{TValue, TSelf}.Empty"/>
    /// until a load lands one, and again after a <see cref="Reset"/>. A fresh
    /// instance after every save/delete (the wither returns a new collection),
    /// which is what lets a pick-list panel observe the change and clear its
    /// pending confirms.
    /// </summary>
    public TSelf Document => _document;

    /// <summary>The document context's condition; see <see cref="NamedDocumentStatus"/>.</summary>
    public NamedDocumentStatus Status { get; private set; } = NamedDocumentStatus.Disabled;

    /// <summary>
    /// The file the failed load was about — non-null exactly while
    /// <see cref="Status"/> is <see cref="NamedDocumentStatus.LoadFailed"/>.
    /// <see cref="FileName"/> when the canonical file couldn't be read or
    /// parsed, <see cref="LegacyFileName"/> when the failure happened on the
    /// legacy fallback — so a degrade notice can name the actual file the user
    /// should look at, rather than guessing the canonical name at a
    /// legacy-era folder.
    /// </summary>
    public string? LoadFailedFileName { get; private set; }

    /// <summary>
    /// The identity of the most recent failed write — a distinct value for
    /// every write that fails, <see langword="null"/> until one does. It
    /// exists so that whoever reports a failed write can tell one failure
    /// from the next: <see cref="Status"/> reads
    /// <see cref="NamedDocumentStatus.WriteFailed"/> after each of them alike,
    /// so the status says <i>that</i> writing is failing and never
    /// <i>which</i> failure is being reported. Only this store knows when a
    /// write failed, so only this store names the occurrence; a consumer
    /// inferring it from renders, statuses or gestures would be a second
    /// owner.
    /// <para>
    /// Opaque on purpose: compare it (<see cref="object.Equals(object)"/>),
    /// never inspect it. It is not a count and carries no ordering. Whether a
    /// failure currently stands is <see cref="Status"/>'s to say and not this
    /// member's — it is never cleared, because the most recent failure stays
    /// the most recent failure after a reload returns the context to
    /// <see cref="NamedDocumentStatus.Ready"/>.
    /// </para>
    /// <para>
    /// The composite hands it to its write-failed notice as the notice's
    /// occurrence key, which is what makes a dismissal last exactly one
    /// failed write (the umbrella's SPEC-notices.md, a condition notice
    /// dismissible per occurrence).
    /// </para>
    /// </summary>
    public object? LastWriteFailure { get; private set; }

    /// <summary>
    /// Read the document, re-deriving the whole context — called by the host
    /// after its source is (re)established. Applies the two-name migration
    /// rule where a legacy name is declared, and swallows every storage
    /// failure into <see cref="Status"/> — it never throws, so the host's flow
    /// is never blocked by document trouble.
    /// </summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;

        if (_storage is null)
        {
            _document = NamedCollection<TValue, TSelf>.Empty;
            Status = NamedDocumentStatus.Disabled;
            LoadFailedFileName = null;
            return;
        }

        // Which file the load is currently about — what LoadFailedFileName
        // reports if this attempt degrades.
        var fileName = FileName;
        try
        {
            var json = await _storage.ReadAsync(fileName);
            if (version != _loadVersion) return;

            if (json is null && LegacyFileName is { } legacyFileName)
            {
                // Canonical file absent — the one case that falls back to the
                // legacy name, and only for a document that declares one. A
                // present-but-corrupt canonical file must NOT reach here:
                // falling back would resurrect stale legacy data over
                // newer-but-corrupt data.
                fileName = legacyFileName;
                json = await _storage.ReadAsync(fileName);
                if (version != _loadVersion) return;
            }

            if (json is null)
            {
                // No file yet — a fresh source. Ready over the empty
                // collection; the first save writes the canonical file into
                // being.
                _document = NamedCollection<TValue, TSelf>.Empty;
                Status = NamedDocumentStatus.Ready;
                LoadFailedFileName = null;
            }
            else if (NamedCollection<TValue, TSelf>.TryFromJson(json, out var loaded))
            {
                _document = loaded;
                Status = NamedDocumentStatus.Ready;
                LoadFailedFileName = null;
            }
            else
            {
                // A file exists but is corrupt / foreign / newer-schema
                // (TryFromJson false on non-null input). Preserve it untouched
                // — never write the empty fallback over the user's file — and
                // degrade to a notice naming it.
                _document = NamedCollection<TValue, TSelf>.Empty;
                Status = NamedDocumentStatus.LoadFailed;
                LoadFailedFileName = fileName;
            }
        }
        catch (DocumentStorageException)
        {
            if (version != _loadVersion) return;
            // The adapter failed the read. Degrade — no document for this
            // source, the file untouched.
            _document = NamedCollection<TValue, TSelf>.Empty;
            Status = NamedDocumentStatus.LoadFailed;
            LoadFailedFileName = fileName;
        }
    }

    /// <summary>
    /// Save <paramref name="value"/> under <paramref name="name"/> (add or
    /// replace) and persist. No-op unless the context is
    /// <see cref="NamedDocumentStatus.Ready"/> — the handler guard behind a
    /// host's <c>CanPersist</c> gate, holding even if a disabled button is
    /// bypassed.
    /// </summary>
    /// <param name="name">
    /// The entry name, pre-normalized per
    /// <see cref="NamedCollection{TValue, TSelf}.With"/>'s contract.
    /// </param>
    /// <param name="value">The value to save.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="value"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> violates the collection's name rule (blank or
    /// untrimmed) — the panel pre-normalizes, so this is a caller-contract
    /// guard, not a user-facing path.
    /// </exception>
    public Task SaveAsync(string name, TValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        if (Status != NamedDocumentStatus.Ready || _storage is null) return Task.CompletedTask;
        return PersistAsync(_document.With(name, value));
    }

    /// <summary>
    /// Remove the named entry (idempotent) and persist. No-op unless the
    /// context is <see cref="NamedDocumentStatus.Ready"/>.
    /// </summary>
    /// <param name="name">The entry name to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Task DeleteAsync(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Status != NamedDocumentStatus.Ready || _storage is null) return Task.CompletedTask;
        return PersistAsync(_document.Without(name));
    }

    /// <summary>
    /// Reset to the no-context state — called when the host's source goes
    /// away. The next <see cref="LoadAsync"/> re-derives everything; an
    /// in-flight load is expired and discards its outcome.
    /// </summary>
    public void Reset()
    {
        _loadVersion++;
        _document = NamedCollection<TValue, TSelf>.Empty;
        Status = NamedDocumentStatus.Disabled;
        LoadFailedFileName = null;
    }

    /// <summary>
    /// Adopt <paramref name="updated"/> in memory first, then write it back —
    /// to the canonical name only, never the legacy one. On a write failure
    /// the in-memory collection is kept (the pick list reflects the change)
    /// while writes stop for this context
    /// (<see cref="NamedDocumentStatus.WriteFailed"/>).
    /// </summary>
    private async Task PersistAsync(TSelf updated)
    {
        _document = updated;
        try
        {
            await _storage!.WriteAsync(FileName, updated.ToJson());
        }
        catch (DocumentStorageException)
        {
            // Both move here and only here: the condition, and the identity
            // of this particular failure (see LastWriteFailure).
            Status = NamedDocumentStatus.WriteFailed;
            LastWriteFailure = new object();
        }
    }
}
