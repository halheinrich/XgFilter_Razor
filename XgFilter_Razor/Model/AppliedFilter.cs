namespace XgFilter_Razor;

using XgFilter_Lib.Filtering;

/// <summary>
/// Holder for the filter config the user has <i>deliberately applied</i> — the
/// filter half of a host's start-the-run gate. Generalized from BgQuiz's
/// app-side original so both consumer apps share one encoding of the applied
/// lifecycle; the host registers it at whatever lifetime its gate must survive
/// (BgQuiz: Scoped, one per loaded app, so the gate survives in-app navigation
/// while the <c>FilterPanel</c>'s own committed state dies with each mount).
///
/// <para>
/// <b>One fact: the applied config, keyed to its source.</b> This holder
/// records a single nullable pair — the config the user committed and the
/// host-minted <see cref="FilterSourceToken"/> it was committed against — and
/// answers only the source-relative question <i>"what is applied for this
/// source?"</i> via <see cref="ConfigFor"/>. There is deliberately no bare
/// "what is applied?" accessor: a config applied against some other source is
/// not applied at all as far as any consumer is concerned, and a surface that
/// can only be asked source-relatively makes reading it absolutely — the old
/// conflation — unrepresentable. Nothing here answers "has this source ever
/// been filtered": no behaviour may depend on history, only on present
/// ownership (the spec's §3 ruling).
/// </para>
///
/// <para>
/// <b>Gate semantics — applied, not merely present.</b> A non-null
/// <see cref="ConfigFor"/> means the user took the deliberate Apply action
/// against that source, not just that some config exists. While the panel
/// reports uncommitted edits (its applied-state report carrying <c>null</c>)
/// this holder must be <see cref="Clear"/>ed, so a half-edited, un-applied
/// selection gates the downstream action — and when an edit is undone the
/// panel reports the committed config again, which re-<see cref="Set"/>s it.
/// The panel's first-render restore is silent (it raises neither callback), so
/// it never spuriously marks applied or clears an existing applied state.
/// </para>
///
/// <para>
/// <b>The reconcile reads this holder, and only this holder.</b> Because the
/// panel's committed state dies each mount while this survives, the composite
/// seeds the remounted panel's committed config from here — so Apply is not
/// re-armed over a selection this holder still says is applied. That seed must
/// never come from the panel's persisted <c>localStorage</c> selection: that
/// blob outlives a browser reload and this holder deliberately does not (see
/// below), so a storage-seeded reconcile would disable Apply after a reload
/// while nothing is applied — the host's gate closed with the one control that
/// could open it greyed out.
/// </para>
///
/// <para>
/// In-memory only: nothing here is persisted, so the applied state lasts
/// exactly as long as the instance the host registered — a full browser reload
/// reboots a WASM host and resets it, by design (the spec's §4: consent dies
/// with the setup; choices are persisted elsewhere).
/// </para>
/// </summary>
public sealed class AppliedFilter
{
    // The applied state as one pair with one lifetime: a config and the source
    // it belongs to are set together, dropped together, and can never exist
    // without each other.
    private (FilterConfig Config, FilterSourceToken Source)? _applied;

    /// <summary>
    /// The config applied for the source identified by <paramref name="source"/>,
    /// or <see langword="null"/> when nothing is applied for it — because
    /// nothing is applied at all, or because what is applied belongs to a
    /// different source. Compared against the host's <i>live</i> token, so a
    /// source change expires the answer by construction (a generation-counter
    /// host bumps on every pick and clear; a path host mints a different
    /// token).
    /// </summary>
    /// <param name="source">The host's current source token.</param>
    /// <returns>
    /// The applied config when the last <see cref="Set"/> was keyed to an
    /// equal token and no <see cref="Clear"/> has intervened; otherwise
    /// <see langword="null"/>.
    /// </returns>
    public FilterConfig? ConfigFor(FilterSourceToken source) =>
        _applied is { } applied && applied.Source == source ? applied.Config : null;

    /// <summary>
    /// Record <paramref name="config"/> as the deliberately-applied filter set,
    /// keyed to the source it was applied against.
    /// </summary>
    /// <param name="config">The config the user committed.</param>
    /// <param name="source">
    /// The host's token for the source the Apply was made against — the key
    /// <see cref="ConfigFor"/> later compares. Passed in rather than read
    /// from some sibling holder: this stays a value holder with no dependency
    /// on host state, and the host — which owns its sequencing story — states
    /// the relationship at the call site.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    public void Set(FilterConfig config, FilterSourceToken source)
    {
        ArgumentNullException.ThrowIfNull(config);
        _applied = (config, source);
    }

    /// <summary>
    /// Drop the applied state entirely — called while the panel reports
    /// uncommitted edits, and by the source-change rule when a setup ends, so
    /// nothing stays in force until the user applies again. No residue
    /// survives: there is no separate fact beside the pair to leave standing.
    /// </summary>
    public void Clear() => _applied = null;
}
