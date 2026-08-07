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
/// <b>Gate semantics — applied, not merely present.</b> <see cref="IsApplied"/>
/// means the user took the deliberate Apply action, not just that some config
/// exists. While the panel reports uncommitted edits (its applied-state report
/// carrying <c>null</c>) this holder must be <see cref="Clear"/>ed, so a
/// half-edited, un-applied selection gates the downstream action — and when an
/// edit is undone the panel reports the committed config again, which
/// re-<see cref="Set"/>s it. The panel's first-render restore is silent (it
/// raises neither callback), so it never spuriously marks applied or clears an
/// existing applied state.
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
/// while <see cref="IsApplied"/> is false — the host's gate closed with the
/// one control that could open it greyed out.
/// </para>
///
/// <para>
/// <b>Two facts, two lifetimes.</b> Beside the config itself this holder
/// records <i>which source</i> the last Apply was made for, stamped with the
/// host-minted <see cref="FilterSourceToken"/>. The config is edit-coupled —
/// any control change <see cref="Clear"/>s it — while the source stamp is
/// <i>not</i>: it answers "has this source ever been filtered?", which a
/// half-typed edit does not un-answer. The two are independent by design, and
/// a host gates on each separately (BgQuiz: Start on the config, Apply Mix on
/// the stamp via <see cref="WasAppliedFor"/>).
/// </para>
///
/// <para>
/// In-memory only: nothing here is persisted, so the applied state lasts
/// exactly as long as the instance the host registered — a full browser reload
/// reboots a WASM host and resets it, by design.
/// </para>
/// </summary>
public sealed class AppliedFilter
{
    /// <summary>
    /// The applied filter config; <c>null</c> until the user applies one, and
    /// again after an edit clears it via <see cref="Clear"/>.
    /// </summary>
    public FilterConfig? Config { get; private set; }

    /// <summary>
    /// The source stamp of the last <see cref="Set"/>; <c>null</c> until the
    /// first Apply of this instance's lifetime. Deliberately <i>not</i> cleared
    /// by <see cref="Clear"/> — see <see cref="WasAppliedFor"/>.
    /// </summary>
    private FilterSourceToken? _appliedSource;

    /// <summary>True once the user has deliberately applied a filter config.</summary>
    public bool IsApplied => Config is not null;

    /// <summary>
    /// Record <paramref name="config"/> as the deliberately-applied filter set,
    /// stamped with the source it was applied against.
    /// </summary>
    /// <param name="config">The config the user committed.</param>
    /// <param name="source">
    /// The host's token for the source the Apply was made against — what
    /// <see cref="WasAppliedFor"/> later compares. Passed in rather than read
    /// from some sibling holder: this stays a value holder with no dependency
    /// on host state, and the host — which owns its sequencing story — states
    /// the relationship at the call site.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    public void Set(FilterConfig config, FilterSourceToken source)
    {
        ArgumentNullException.ThrowIfNull(config);
        Config = config;
        _appliedSource = source;
    }

    /// <summary>
    /// Whether a filter has been applied for the source identified by
    /// <paramref name="source"/> — i.e. "this source has been filtered at
    /// least once", compared against the host's <i>live</i> token so a source
    /// change expires the answer by construction (a generation-counter host
    /// bumps on every pick and clear; a path host mints a different token).
    ///
    /// <para>
    /// <b>Survives <see cref="Clear"/> on purpose.</b> A filter edit
    /// un-applies the <i>config</i> (the start gate re-closes), but the user
    /// has still filtered this source, so gestures gated on "ever filtered"
    /// (BgQuiz's Apply Mix) must not re-gate — a settled ruling: a dirty
    /// filter draft does not revoke the mix gate.
    /// </para>
    /// </summary>
    /// <param name="source">The host's current source token.</param>
    /// <returns>Whether the last Apply was stamped with an equal token.</returns>
    public bool WasAppliedFor(FilterSourceToken source) => _appliedSource == source;

    /// <summary>
    /// Drop the applied config — called while the panel reports uncommitted
    /// edits, so a half-edited selection re-gates the downstream action until
    /// the user applies again. The source stamp is deliberately left standing
    /// (see <see cref="WasAppliedFor"/>).
    /// </summary>
    public void Clear() => Config = null;
}
