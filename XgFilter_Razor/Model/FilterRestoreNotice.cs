namespace XgFilter_Razor;

/// <summary>
/// App-scoped state for the restored-filter notice — the legibility half of
/// the spec's setup law (§4): a reload ends the setup, so the panel restores
/// the persisted selections with nothing applied and Apply re-armed, and
/// without a line of copy that correct state is indistinguishable from a bug
/// (the user's filter on screen, Apply mysteriously armed). This type holds
/// the one fact that copy hangs on: <i>this app boot restored a previous
/// session's selection, and the user has not yet made it their own.</i>
///
/// <para>
/// <b>The lifetime is the trigger.</b> Hosts register this at app scope
/// (beside <see cref="AppliedFilter"/>) and bind it to the filter surface; a
/// full reload constructs a fresh instance, and that construction — not any
/// recorded fact — is what distinguishes a fresh boot from a remount within
/// a setup. The distinction cannot be derived at mount time: "a stored
/// selection was restored and nothing is applied" is also true of a
/// navigate-back with unapplied edits, where the panel remounts, restores,
/// and finds the holder empty — and navigation must change nothing (§1),
/// including no notice appearing that wasn't already there.
/// </para>
///
/// <para>
/// <b>Not a history fact.</b> The spec's §3 bans behaviour from depending on
/// what has ever happened; this records a <i>pending</i> state in the
/// present tense: it arms
/// when the boot's restore stages a stored selection, stays visible across
/// navigation while the restored selection is untouched (a remount re-arms
/// it — the same notice persisting, not a new one), and dies at the first
/// gesture that makes the selection the user's own — a buffer edit, a load,
/// or a commit. Once dismissed it cannot re-arm: within one app lifetime the
/// state only moves toward death, and it resets solely by the instance dying
/// with the app — the same no-persistence posture as the applied holder.
/// </para>
///
/// <para>
/// The movers are <c>internal</c>: only the producer's panel arms, reads,
/// and dismisses this — a host registers the instance and binds it, nothing
/// more, so the notice behaves identically in every host by construction.
/// </para>
/// </summary>
public sealed class FilterRestoreNotice
{
    // One-way within an app lifetime: once the notice is dismissed, later
    // restores (remounts within the same boot) must not resurrect it.
    private bool _spent;

    /// <summary>
    /// Whether the notice is showing: the boot's restore staged a stored
    /// selection and no gesture has yet superseded it.
    /// </summary>
    internal bool IsVisible { get; private set; }

    /// <summary>
    /// Record that a first-render restore staged a stored selection. Shows
    /// the notice unless a gesture already dismissed it this app lifetime —
    /// so a remount over an untouched setup re-shows the same notice
    /// (navigation changes nothing), while a remount after an edit stays
    /// quiet.
    /// </summary>
    internal void Arm()
    {
        if (!_spent)
        {
            IsVisible = true;
        }
    }

    /// <summary>
    /// The restored selection became the user's own — a buffer-affecting
    /// gesture or a commit — so the notice's statement no longer holds.
    /// Hides it and spends it for the rest of the app lifetime.
    /// </summary>
    internal void Dismiss()
    {
        IsVisible = false;
        _spent = true;
    }
}
