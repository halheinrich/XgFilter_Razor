namespace XgFilter_Razor;

/// <summary>
/// Everything <c>NamedEntriesPanel</c> renders that the generic cannot know:
/// the copy a particular document's pick list speaks in, and the element ids
/// host suites pin it by. One required parameter carries all of it, so mounting
/// the panel for a new document is one record literal rather than seven
/// parameters that can each be forgotten separately
/// (halheinrich/backgammon#190 leg (D)).
///
/// <para>
/// <b>The ids are part of the contract, not decoration.</b> A mount's ids are
/// what every host's unit and e2e suite finds the panel by, so changing one
/// breaks tests in repositories this one cannot see. Treat a preset's ids as
/// published the moment a host renders it; the copy is likewise user-facing
/// and belongs to whoever owns that document's voice.
/// </para>
///
/// <para>
/// <b>A preset belongs with the composite that mounts it</b>, as a
/// <c>static readonly</c> beside the mount rather than a constant on this
/// type: this record is the shape, and knows no document. The saved-filters
/// preset lives on <c>FilterSurface</c>; the mix-saves document the arc queues
/// brings its own.
/// </para>
/// </summary>
public sealed record NamedEntriesSurface
{
    /// <summary>The card header — e.g. <c>"Saved Filters"</c>.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The line shown in place of the list while the document holds no
    /// entries — e.g. <c>"No saved filters yet."</c>.
    /// </summary>
    public required string EmptyText { get; init; }

    /// <summary>
    /// The save-as input's placeholder — e.g. <c>"Filter name"</c>.
    /// </summary>
    public required string NamePlaceholder { get; init; }

    /// <summary>
    /// What a row's Save prompt says will replace the entry — e.g.
    /// <c>"the current filters"</c>, rendered as "Overwrite 'name' with the
    /// current filters?". Deliberately a noun phrase, so the row prompt stays
    /// distinguishable from the save-as overwrite prompt ("Overwrite 'name'?")
    /// in every mount.
    /// </summary>
    public required string OverwriteWithNoun { get; init; }

    /// <summary>The save-as name input's element id.</summary>
    public required string NameInputId { get; init; }

    /// <summary>The save-as button's element id.</summary>
    public required string SaveButtonId { get; init; }

    /// <summary>
    /// The element id of the polite live region carrying the load
    /// confirmation. The region is permanent and only its content swaps, so
    /// this id is present from the first render.
    /// </summary>
    public required string LoadedNoticeId { get; init; }
}
