using XgFilter_Lib.Filtering;

namespace XgFilter_Razor;

/// <summary>
/// The filter setup's state owner (<c>SPEC-filtering.md</c> §4, "The
/// setup-state owner"; halheinrich/backgammon#374): the one app-scoped holder
/// of everything the filter surface's rules govern, so that state outlives
/// the components that show it. Hosts register it with
/// <see cref="FilterSurfaceServiceCollectionExtensions.AddFilterSurface{TRefusalSink}"/>,
/// report their source to it, and read their filter gate from its snapshots.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it holds.</b> The setup's identity — the source the host reported
/// and a generation advanced whenever a setup ends; the draft, the selection
/// on screen as the user left it (<see cref="FilterDraft"/>), and whether it
/// is <i>resolved</i> — unresolved only after a failed restoration, until a
/// valid Apply or Clear makes it the user's choice; the committed
/// baseline, the last selection applied in this setup, or none; this boot's
/// restoration outcome (<see cref="FilterRestoration"/>); and the two
/// restoration notices, whose occurrence — this boot's restore — is the same
/// as the outcome's. Nothing is persisted but the committed selection, which
/// is what the restoration reads back.
/// </para>
/// <para>
/// <b>What it derives.</b> Whether a filter is in effect and which, and the
/// gates' filter terms — <see cref="FilterSetupSnapshot"/>'s reading of the
/// above, never stored beside it.
/// </para>
/// <para>
/// <b>Order.</b> Every accepted change takes effect, and is published,
/// before any storage call it makes: a commit records its baseline and
/// publishes it, then writes. A write's completion can therefore change no
/// draft, baseline or gate — it reports a refusal to the host and, landing,
/// ends the failed-restore notice, and that is all — so a write that
/// completes late, after a newer commit or after the setup ended, cannot
/// restore anything older. The restoration is the one read whose answer
/// changes state, and it changes only a draft nobody has touched since it
/// began.
/// </para>
/// <para>
/// <b>Observing.</b> <see cref="Attach"/> delivers the current snapshot at
/// once, then each real change, so a component or page mounted later learns
/// the existing state without any change being manufactured.
/// <see cref="Current"/> answers the same thing on demand.
/// </para>
/// <para>
/// <b>Lifetime.</b> One per app scope — a Blazor WebAssembly app, or a server
/// circuit. A full reload starts a fresh owner, which is a fresh setup and a
/// fresh restoration (§4's last row). Not thread-safe: every member is called
/// on the renderer's synchronization context, which every component event and
/// lifecycle method already is.
/// </para>
/// </remarks>
public sealed class FilterSetup
{
    private readonly FilterStorage _storage;
    private readonly List<Action<FilterSetupSnapshot>> _observers = [];

    private FilterSourceToken? _source;
    private int _generation;
    private FilterDraft _draft = FilterDraft.Empty;
    private FilterDraft? _baseline;

    // Whether the draft is the user's choice, or the defaults a failed
    // restoration left on screen (§4, halheinrich/backgammon#266). Current
    // state, not history: it travels with the draft through navigation and
    // source changes, a valid Apply or Clear makes it true, and only a failed
    // restoration — once per boot — makes it false. The restoration outcome
    // stays the diagnostic, and is never rewritten to make Run possible.
    private bool _resolved = true;
    private FilterRestoration _restoration = FilterRestoration.Pending;
    private bool _restoredNoticeShowing;
    private bool _failureNoticeShowing;

    // Advanced by every change to the draft. The restoration notes it when its
    // read begins and hydrates the draft only if it has not moved, so a read
    // answered after the user edited never overwrites the edit.
    private long _draftRevision;

    // The one restoration of this boot, started by the first call to
    // RestoreAsync and shared by every later one.
    private Task? _restorationRead;

    private FilterSetupSnapshot _current;

    /// <summary>The owner over the surface's storage. Constructed by the registration, never by a host.</summary>
    /// <param name="storage">The surface's storage.</param>
    internal FilterSetup(FilterStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        _storage = storage;
        _current = Snapshot();
    }

    /// <summary>The setup as it stands now.</summary>
    public FilterSetupSnapshot Current => _current;

    /// <summary>
    /// Observe the setup: <paramref name="observer"/> receives
    /// <see cref="Current"/> before this returns, and then every real change,
    /// synchronously and in order, until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// Dispose the handle when the observer goes — a component in its
    /// <c>Dispose</c>. Disposing stops snapshots to that observer and does
    /// nothing else: an operation in flight is not cancelled, and a storage
    /// refusal it meets still reaches the host's sink, which does not go
    /// through any observer. An observer must not throw and must not call
    /// this owner's operations; it records what it was given and schedules
    /// its own render.
    /// </remarks>
    /// <param name="observer">Receives each snapshot.</param>
    /// <returns>The attachment; dispose it to stop observing. Disposing twice does nothing more.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
    public IDisposable Attach(Action<FilterSetupSnapshot> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        observer(_current);
        _observers.Add(observer);
        return new Attachment(this, observer);
    }

    /// <summary>
    /// Tell the owner which source the host holds now. A different source
    /// ends the setup: a new generation, the baseline dropped, the draft kept
    /// (§1, "The dir is changed"). The same source again does nothing — which
    /// is how a remount, or a return to an unchanged path, keeps its setup.
    /// </summary>
    /// <remarks>
    /// Report every source the host latches, at the moment it latches it,
    /// whether or not the filter surface is mounted: A → B → A while it is not
    /// is two endings, and A's consent never returns (§4). Report
    /// <see langword="null"/> only for "no source exists", never for "not yet
    /// known" — a placeholder later corrected to the real token reads as a
    /// real change, and ends the setup.
    /// </remarks>
    /// <param name="source">The host's current source, or <see langword="null"/> for none.</param>
    public void ReportSource(FilterSourceToken? source)
    {
        if (_source == source) return;

        _source = source;
        _generation++;
        _baseline = null;
        Publish();
    }

    // ── The surface's operations ───────────────────────────────────────────

    /// <summary>
    /// One edit gesture. The draft becomes <paramref name="edit"/>'s result,
    /// and the gesture makes the selection the user's own, so the restored
    /// notice ends.
    /// </summary>
    /// <param name="edit">The edit, applied to the current draft.</param>
    internal void Edit(Func<FilterDraft, FilterDraft> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);

        SetDraft(edit(_draft));
        _restoredNoticeShowing = false;
        Publish();
    }

    /// <summary>
    /// Stage <paramref name="config"/> — a saved filter loaded — as the draft:
    /// an edit like any other, so it commits nothing. The config is copied.
    /// </summary>
    /// <param name="config">The configuration to show.</param>
    internal void Stage(FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var staged = FilterDraft.From(config);
        Edit(_ => staged);
    }

    /// <summary>
    /// Apply: when the gate allows it, the draft becomes this setup's baseline
    /// and is published, then the selection is written for the next visit. A
    /// refused write never undoes the commit.
    /// </summary>
    /// <returns>A task that completes when the write has been answered.</returns>
    internal Task ApplyAsync()
    {
        if (!_current.CanApply) return Task.CompletedTask;

        _baseline = _draft;
        _resolved = true;
        _restoredNoticeShowing = false;
        Publish();
        return RememberAsync(_draft);
    }

    /// <summary>
    /// Clear filters: once restoration has settled, the draft becomes the
    /// empty selection and so does the baseline, and that is published, then
    /// written. A refused write never undoes it.
    /// </summary>
    /// <returns>A task that completes when the write has been answered.</returns>
    internal Task ClearAsync()
    {
        if (!_current.CanClear) return Task.CompletedTask;

        SetDraft(FilterDraft.Empty);
        _baseline = FilterDraft.Empty;
        _resolved = true;
        _restoredNoticeShowing = false;
        Publish();
        return RememberAsync(FilterDraft.Empty);
    }

    /// <summary>
    /// The one restoration of this boot: the first call reads the remembered
    /// selection, and every call returns that same read's task. The panel
    /// calls it at its first interactive render, where storage can be reached.
    /// </summary>
    /// <returns>The restoration, which completes when it has settled.</returns>
    internal Task RestoreAsync() => _restorationRead ??= RestoreCoreAsync();

    /// <summary>The user closed the restored-selection notice.</summary>
    internal void DismissRestoredNotice()
    {
        _restoredNoticeShowing = false;
        Publish();
    }

    /// <summary>The user closed the failed-restore notice.</summary>
    internal void DismissFailureNotice()
    {
        _failureNoticeShowing = false;
        Publish();
    }

    // ── Inside ─────────────────────────────────────────────────────────────

    private async Task RestoreCoreAsync()
    {
        var revisionAtStart = _draftRevision;
        var read = await _storage.ReadAsync(FilterStorage.ConfigKey);

        // The three readings are told apart here and nowhere else. A refused
        // read and a missing item are different facts (refused / nothing
        // stored), though both leave the defaults; TryFromJson cannot draw
        // that line, since it answers false for a missing item too, which is
        // why absence is asked first. A document that reads restores whole, a
        // field a rule outlaws included (#269); one that does not read is
        // unreadable, and is left in storage as it is.
        FilterConfig? restored = null;
        if (read.IsRefused)
        {
            _restoration = FilterRestoration.Refused;
            _resolved = false;
        }
        else if (read.IsAbsent)
        {
            _restoration = FilterRestoration.NothingStored;
        }
        else if (FilterConfig.TryFromJson(read.Value, out var config))
        {
            _restoration = FilterRestoration.Restored;
            restored = config;
        }
        else
        {
            _restoration = FilterRestoration.Unreadable;
            _resolved = false;
        }

        // The outcome is recorded whatever happened meanwhile; the draft is
        // hydrated only if nobody has edited it since the read began, so a
        // late answer never overwrites a newer draft. The source may have
        // changed in between: the draft is kept across a setup's end, so that
        // changes nothing here. A restored empty selection raises no notice:
        // it is ready as it stands, and nothing differs from a first visit
        // (§1, Reload).
        if (restored is not null && _draftRevision == revisionAtStart)
        {
            SetDraft(FilterDraft.From(restored));
            _restoredNoticeShowing = !_draft.RestrictsNothing;
        }

        _failureNoticeShowing = _restoration == FilterRestoration.Unreadable;
        Publish();
    }

    // Write the committed selection for the next visit. The commit was
    // published before this began, so the answer changes no draft, baseline or
    // gate: a refusal has already gone to the host (FilterStorage), and a
    // write that lands has replaced any unreadable document, which ends the
    // failed-restore notice whichever commit's write it was.
    private async Task RememberAsync(FilterDraft committed)
    {
        var written = await _storage.WriteAsync(FilterStorage.ConfigKey, committed.ToConfig().ToJson());

        if (written.Succeeded && _failureNoticeShowing)
        {
            _failureNoticeShowing = false;
            Publish();
        }
    }

    private void SetDraft(FilterDraft draft)
    {
        if (draft.Equals(_draft)) return;

        _draft = draft;
        _draftRevision++;
    }

    private FilterSetupSnapshot Snapshot() => new(
        _source, _generation, _draft, _resolved, _baseline, _restoration, _restoredNoticeShowing, _failureNoticeShowing);

    // Publish the state as it now stands — only when it differs from the last
    // published snapshot, so each observer sees each real change and nothing
    // manufactured. Observers are called over a copy, so one that detaches
    // while being told does not disturb the others.
    private void Publish()
    {
        var next = Snapshot();
        if (next.HoldsTheSameStateAs(_current)) return;

        _current = next;
        foreach (var observer in _observers.ToArray())
        {
            observer(next);
        }
    }

    private sealed class Attachment(FilterSetup owner, Action<FilterSetupSnapshot> observer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            owner._observers.Remove(observer);
        }
    }
}
