namespace XgFilter_Razor;

/// <summary>
/// What became of this app boot's restoration of the remembered filter
/// selection (<c>SPEC-filtering.md</c> §1, Reload; halheinrich/backgammon#346).
/// It is <see cref="Pending"/> until the one read settles, and then exactly one
/// of the four outcomes for the rest of the boot.
/// </summary>
/// <remarks>
/// <para>
/// <b>A diagnostic fact, not a gate.</b> The outcome says what the read found;
/// whether Apply or Run is permitted is a different fact, which
/// <see cref="FilterSetupSnapshot"/> answers. No outcome is ever rewritten to
/// make Run possible.
/// </para>
/// <para>
/// <b>Once per boot.</b> The restoration runs at the first interactive render
/// of the filter panel and settles once, whatever the source does meanwhile. A
/// later mount of the panel reads nothing and finds the outcome already here.
/// </para>
/// </remarks>
public enum FilterRestoration
{
    /// <summary>The read has not settled: it has not started, or the browser has not answered.</summary>
    Pending,

    /// <summary>
    /// A remembered selection was read. It is on screen — unless the user had
    /// already edited the draft while the read was pending, in which case the
    /// newer draft stands and nothing was overwritten.
    /// </summary>
    Restored,

    /// <summary>Nothing was remembered: an ordinary first visit.</summary>
    NothingStored,

    /// <summary>The browser refused the read. The defaults are on screen.</summary>
    Refused,

    /// <summary>Something was remembered but is not a filter selection this version can read. The defaults are on screen.</summary>
    Unreadable,
}
