namespace XgFilter_Razor;

/// <summary>
/// The condition of a <see cref="NamedDocumentStore{TValue, TSelf}"/>'s
/// document context, driving the pick-list panel and its notices. Re-derived
/// from scratch on every
/// <see cref="NamedDocumentStore{TValue, TSelf}.LoadAsync"/>; reset by
/// <see cref="NamedDocumentStore{TValue, TSelf}.Reset"/>. One posture
/// throughout: degrade, never block — no document trouble ever interrupts the
/// host's own flow.
/// </summary>
public enum NamedDocumentStatus
{
    /// <summary>
    /// No document context: nothing loaded yet, the store was
    /// <see cref="NamedDocumentStore{TValue, TSelf}.Reset"/>, or the host
    /// supplied no storage adapter (a source with no readable storage). No
    /// panel renders.
    /// </summary>
    Disabled,

    /// <summary>
    /// The document loaded (or was seeded empty because none exists yet).
    /// Loads work; saves work too when the host's capability allows them (the
    /// host's <c>CanPersist</c> ruling — read-only contexts stay
    /// <see cref="Ready"/> with saving gated host-side).
    /// </summary>
    Ready,

    /// <summary>
    /// The document couldn't be read (a storage failure) or couldn't be parsed
    /// (corrupt, foreign, or a newer schema). Terminal for this context: the
    /// file is <b>never</b> written, so the user's data is preserved untouched,
    /// and the panel degrades to a notice.
    /// </summary>
    LoadFailed,

    /// <summary>
    /// A persist failed. The in-memory collection keeps the edit (the pick
    /// list stays truthful) but no further writes are attempted for this
    /// context.
    /// </summary>
    WriteFailed,
}
