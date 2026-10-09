using System.Diagnostics.CodeAnalysis;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace XgFilter_Razor;

/// <summary>
/// One moment of the filter setup, as <see cref="FilterSetup"/> publishes it:
/// immutable, and complete for that moment. A host reads its filter gate here
/// — whether a filter is in effect for its source, and which — and what
/// became of this boot's restoration (<c>SPEC-filtering.md</c> §4, "What
/// consumers see").
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived, not stored.</b> What is in effect and the gates' filter terms
/// are this snapshot's own reading of the state it was made from, worked out
/// once when it was made. Nothing derived is kept anywhere as a second copy
/// that could be changed, and nothing a consumer does to a snapshot — or to a
/// config it got from one — reaches the owner: the snapshot holds immutable
/// state, and every config it hands out is new.
/// </para>
/// <para>
/// <b>Asked source-relatively.</b> The in-effect filter is answered only for a
/// source the caller names, and only when it is the source this snapshot's
/// setup belongs to. A host that forgot to report a changed source therefore
/// reads "nothing in effect" — its gate dark — rather than a filter applied to
/// a different corpus (<c>SPEC-filtering.md</c> §3: the need is ownership, not
/// history).
/// </para>
/// </remarks>
public sealed class FilterSetupSnapshot
{
    // The draft whose config is in effect, or null when none is. The draft
    // rather than its config, so every read hands out a new config.
    private readonly FilterDraft? _inEffect;

    internal FilterSetupSnapshot(
        FilterSourceToken? source,
        int generation,
        FilterDraft draft,
        bool resolved,
        FilterDraft? baseline,
        FilterRestoration restoration,
        bool restoredNoticeShowing,
        bool failureNoticeShowing)
    {
        Source = source;
        Generation = generation;
        Draft = draft;
        IsResolved = resolved;
        Baseline = baseline;
        Restoration = restoration;
        IsRestoredNoticeShowing = restoredNoticeShowing;
        IsFailureNoticeShowing = failureNoticeShowing;

        InvalidFields = draft.InvalidFields;
        ActiveFacets = draft.ActiveFacets;

        // The applied baseline is in effect while the draft equals it — as a
        // config, so an edit undone back to the applied values is in effect
        // again (§1, "Editing back to exactly the applied values"). Only a
        // valid draft can equal it: a box the config cannot represent parses
        // as no criterion and would otherwise match a baseline without one.
        IsAlreadyApplied =
            source is not null
            && baseline is not null
            && InvalidFields.Count == 0
            && draft.ToConfig().Equals(baseline.ToConfig());

        // The empty selection is ready without Apply (§1,
        // halheinrich/backgammon#266): resolved, valid and restricting
        // nothing, once restoration has settled, it runs everything — what is
        // on screen is what would run, so there is no consent to give. A
        // pending or failed restoration is not evidence that the user chose
        // no filter, and an invalid draft is not the empty selection.
        IsReadyEmpty =
            source is not null
            && restoration != FilterRestoration.Pending
            && resolved
            && draft.RestrictsNothing;

        _inEffect = IsAlreadyApplied || IsReadyEmpty ? draft : null;
    }

    /// <summary>
    /// The source the host last reported (<see cref="FilterSetup.ReportSource"/>),
    /// or <see langword="null"/> while it has reported none.
    /// </summary>
    public FilterSourceToken? Source { get; }

    /// <summary>
    /// The setup's generation: advanced by the owner every time a setup ends,
    /// and never otherwise. Two snapshots with the same generation belong to
    /// one setup, so a consumer can tell "the setup ended" from "something in
    /// it changed" without comparing sources itself.
    /// </summary>
    public int Generation { get; }

    /// <summary>What became of this boot's restoration of the remembered selection.</summary>
    public FilterRestoration Restoration { get; }

    /// <summary>
    /// Whether a filter is in effect for <paramref name="source"/> — the
    /// filter half of a host's Run gate (<c>SPEC-filtering.md</c> §2). True
    /// when <paramref name="source"/> is this setup's source and the selection
    /// on screen is either the one the user applied for it, or the ready empty
    /// selection, which needs no Apply. The producer answers readiness: a host
    /// adds no empty-filter exception of its own.
    /// </summary>
    /// <param name="source">The host's current source.</param>
    /// <returns>Whether a filter is in effect for it.</returns>
    public bool IsInEffectFor(FilterSourceToken source) => Source == source && _inEffect is not null;

    /// <summary>
    /// The filter in effect for <paramref name="source"/>, or
    /// <see langword="null"/> when none is (see <see cref="IsInEffectFor"/>).
    /// A new config on every call: change it freely, it reaches nothing.
    /// </summary>
    /// <param name="source">The host's current source.</param>
    /// <returns>The in-effect filter, or <see langword="null"/>.</returns>
    public FilterConfig? ConfigInEffectFor(FilterSourceToken source) =>
        IsInEffectFor(source) ? _inEffect!.ToConfig() : null;

    /// <summary>A description for diagnostics — logs and test failure messages. Never parse it.</summary>
    /// <returns>The description.</returns>
    public override string ToString() =>
        $"FilterSetupSnapshot {{ Source = {Source?.ToString() ?? "none"}, Generation = {Generation}, "
        + $"Restoration = {Restoration}, InEffect = {_inEffect is not null} }}";

    // ── The producer's reading, for the surface's own components ──────────

    /// <summary>The selection as the user is editing it.</summary>
    internal FilterDraft Draft { get; }

    /// <summary>Whether the draft is the user's choice rather than the defaults a failed restoration left.</summary>
    internal bool IsResolved { get; }

    /// <summary>Whether the draft is the ready empty selection (§1), in effect without Apply.</summary>
    internal bool IsReadyEmpty { get; }

    /// <summary>Whether what is in effect restricts nothing — Apply's "no filter is set".</summary>
    internal bool IsTheEmptySelectionInEffect => _inEffect is not null && Draft.RestrictsNothing;

    /// <summary>The committed baseline for this setup — the last applied selection — or none.</summary>
    internal FilterDraft? Baseline { get; }

    /// <summary>Whether the restored-selection notice is showing.</summary>
    internal bool IsRestoredNoticeShowing { get; }

    /// <summary>Whether the failed-restore notice is showing.</summary>
    internal bool IsFailureNoticeShowing { get; }

    /// <summary>The fields whose value is wrong (<see cref="FilterDraft.InvalidFields"/>).</summary>
    internal IReadOnlySet<FilterField> InvalidFields { get; }

    /// <summary>The facets holding a criterion (<see cref="FilterDraft.ActiveFacets"/>).</summary>
    internal IReadOnlySet<FilterFacet> ActiveFacets { get; }

    /// <summary>Whether the draft is the baseline applied for this setup — Apply's "nothing changed".</summary>
    internal bool IsAlreadyApplied { get; }

    /// <summary>Whether the one restoration read has settled.</summary>
    internal bool IsRestorationSettled => Restoration != FilterRestoration.Pending;

    /// <summary>
    /// Apply's gate, <c>SPEC-filtering.md</c> §2's definition: a source is
    /// established, restoration has settled, the draft is valid, and it is
    /// not already in effect.
    /// </summary>
    internal bool CanApply =>
        Source is not null && IsRestorationSettled && InvalidFields.Count == 0 && _inEffect is null;

    /// <summary>Clear filters' gate: restoration has settled (§4's transitions table).</summary>
    internal bool CanClear => IsRestorationSettled;

    /// <summary>
    /// The draft's config when it may be saved — exactly when it could be
    /// applied apart from the setup's own terms, so no saved document is
    /// minted from a selection Apply would refuse for its values.
    /// </summary>
    /// <param name="config">A new config when this returns <see langword="true"/>.</param>
    /// <returns>Whether the draft is valid.</returns>
    internal bool TryGetSavable([NotNullWhen(true)] out FilterConfig? config)
    {
        config = InvalidFields.Count == 0 ? Draft.ToConfig() : null;
        return config is not null;
    }

    /// <summary>
    /// Whether <paramref name="other"/> holds the same state — the owner
    /// publishes a snapshot only when this is false, so an observer sees each
    /// real change and nothing else.
    /// </summary>
    /// <param name="other">The snapshot to compare with.</param>
    /// <returns>Whether the two describe the same moment.</returns>
    internal bool HoldsTheSameStateAs(FilterSetupSnapshot other) =>
        Source == other.Source
        && Generation == other.Generation
        && Draft.Equals(other.Draft)
        && IsResolved == other.IsResolved
        && Equals(Baseline, other.Baseline)
        && Restoration == other.Restoration
        && IsRestoredNoticeShowing == other.IsRestoredNoticeShowing
        && IsFailureNoticeShowing == other.IsFailureNoticeShowing;
}
