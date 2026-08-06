namespace XgFilter_Razor;

/// <summary>
/// The condition of a <see cref="SavedFiltersStore"/>'s saved-filters context,
/// driving the saved-filters panel and its notices. Re-derived from scratch on
/// every <see cref="SavedFiltersStore.LoadAsync"/>; reset by
/// <see cref="SavedFiltersStore.Reset"/>. One posture throughout: degrade,
/// never block — no saved-filters trouble ever interrupts the host's own flow.
/// </summary>
public enum SavedFiltersStatus
{
    /// <summary>
    /// No saved-filters context: nothing loaded yet, the store was
    /// <see cref="SavedFiltersStore.Reset"/>, or the host supplied no storage
    /// adapter (a source with no readable storage). No panel renders.
    /// </summary>
    Disabled,

    /// <summary>
    /// The saved-filters document loaded (or was seeded empty because no
    /// document exists yet). Loads work; saves work too when the host's
    /// capability allows them (the host's <c>CanPersist</c> ruling — read-only
    /// contexts stay <see cref="Ready"/> with saving gated host-side).
    /// </summary>
    Ready,

    /// <summary>
    /// The saved-filters document couldn't be read (a storage failure) or
    /// couldn't be parsed (corrupt, foreign, or a newer schema). Terminal for
    /// this context: the file is <b>never</b> written, so the user's data is
    /// preserved untouched, and the panel degrades to a notice.
    /// </summary>
    LoadFailed,

    /// <summary>
    /// A persist failed. The in-memory collection keeps the edit (the pick
    /// list stays truthful) but no further writes are attempted for this
    /// context.
    /// </summary>
    WriteFailed,
}
