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
/// <b>Every member is validated, never coerced</b> — the same posture, and the
/// same wording, as the name rule in
/// <see cref="BgDataTypes_Lib.NamedCollection{TValue, TSelf}"/>. Null, blank
/// and untrimmed are each rejected at <see langword="init"/>, so no instance
/// of this type can exist that renders <c>id=""</c> or an empty card header.
/// That failure mode is precisely the silent one: a blank id renders valid
/// markup, and every host pin that looks for the real id simply goes dark —
/// no exception, no failing build here, just a suite in another repository
/// finding nothing. <c>required</c> stops a preset being half-built; this
/// stops it being built wrong. Both are compile- or construction-time, because
/// neither is discoverable later.
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
    private readonly string _title = null!;
    private readonly string _emptyText = null!;
    private readonly string _namePlaceholder = null!;
    private readonly string _overwriteWithNoun = null!;
    private readonly string _nameInputId = null!;
    private readonly string _saveButtonId = null!;
    private readonly string _loadedNoticeId = null!;

    /// <summary>The card header — e.g. <c>"Saved Filters"</c>.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string Title
    {
        get => _title;
        init => _title = Validated(value, nameof(Title));
    }

    /// <summary>
    /// The line shown in place of the list while the document holds no
    /// entries — e.g. <c>"No saved filters yet."</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string EmptyText
    {
        get => _emptyText;
        init => _emptyText = Validated(value, nameof(EmptyText));
    }

    /// <summary>
    /// The save-as input's placeholder — e.g. <c>"Filter name"</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string NamePlaceholder
    {
        get => _namePlaceholder;
        init => _namePlaceholder = Validated(value, nameof(NamePlaceholder));
    }

    /// <summary>
    /// What a row's Save prompt says will replace the entry — e.g.
    /// <c>"the current filters"</c>, rendered as "Overwrite 'name' with the
    /// current filters?". Deliberately a noun phrase, so the row prompt stays
    /// distinguishable from the save-as overwrite prompt ("Overwrite 'name'?")
    /// in every mount.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string OverwriteWithNoun
    {
        get => _overwriteWithNoun;
        init => _overwriteWithNoun = Validated(value, nameof(OverwriteWithNoun));
    }

    /// <summary>The save-as name input's element id.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string NameInputId
    {
        get => _nameInputId;
        init => _nameInputId = Validated(value, nameof(NameInputId));
    }

    /// <summary>The save-as button's element id.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string SaveButtonId
    {
        get => _saveButtonId;
        init => _saveButtonId = Validated(value, nameof(SaveButtonId));
    }

    /// <summary>
    /// The element id of the polite live region carrying the load
    /// confirmation. The region is permanent and only its content swaps, so
    /// this id is present from the first render.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    /// <exception cref="ArgumentException">
    /// The value is blank, or carries leading or trailing whitespace.
    /// </exception>
    public required string LoadedNoticeId
    {
        get => _loadedNoticeId;
        init => _loadedNoticeId = Validated(value, nameof(LoadedNoticeId));
    }

    // One rule for all seven, so no member can be validated more leniently
    // than its neighbours by an edit that touches only its own accessor. A
    // `with` expression re-runs it for whatever it changes: the record's copy
    // constructor copies the backing fields, then the init accessors run for
    // the members the expression names, so there is no unvalidated path in.
    private static string Validated(string? value, string member)
    {
        ArgumentNullException.ThrowIfNull(value, member);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{member} must be non-blank.", member);
        if (value != value.Trim())
            throw new ArgumentException(
                $"{member} must not carry leading or trailing whitespace (got '{value}').",
                member);
        return value;
    }
}
