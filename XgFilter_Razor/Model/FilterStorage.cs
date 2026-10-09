using BgUiPrimitives_Razor;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace XgFilter_Razor;

/// <summary>
/// The filter surface's browser storage: what it keeps, where, and the one
/// place its refusals are reported. Every storage call the surface makes —
/// the state owner's read and writes of the remembered selection, the panel's
/// reads and writes of its display preferences — goes through here, over
/// <see cref="BrowserStorage"/>'s local area (halheinrich/backgammon#374).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every call is made, and every refusal is told.</b> There is no latch:
/// a refused call disables nothing, and the next call is simply made. Each
/// refusal goes to the host's <see cref="IFilterStorageRefusalSink"/> before
/// the result comes back, so no caller can forget to report one and no
/// caller reports one twice. The result still carries the refusal, for the
/// caller to degrade on — a refused read reads as nothing stored, a refused
/// write leaves the in-memory choice standing.
/// </para>
/// <para>
/// <b>Access and keys only.</b> Parsing, defaults and judging a stored value
/// stay with the reader: the owner reads the selection through the lib's
/// document trio, the panel its preferences through its own rules. A failure
/// that is not the browser's refusal is not caught here or below, so it
/// propagates — <see cref="BrowserStorage"/>'s boundary, unchanged.
/// </para>
/// <para>
/// Internal and registered by
/// <see cref="FilterSurfaceServiceCollectionExtensions.AddFilterSurface{TRefusalSink}"/>,
/// scoped beside the owner: its lifetime is the app's, so a refusal arriving
/// after the component that started the call has gone still has somewhere
/// to go.
/// </para>
/// </remarks>
internal sealed class FilterStorage
{
    /// <summary>
    /// The remembered selection: the last committed <see cref="FilterConfig"/>
    /// as one blob, in the lib's own JSON (<see cref="FilterConfig.ToJson"/> /
    /// <see cref="FilterConfig.TryFromJson"/>) — this component never touches a
    /// serializer for it. Written by every commit (Apply, Clear filters) and
    /// read once per app boot, by the restoration.
    /// </summary>
    /// <remarks>
    /// <c>internal</c>, not private, so <see cref="Components.FilterHelp"/> can
    /// render the name in its "what the panel remembers" copy from this one
    /// constant rather than repeating it as a prose literal, and so the
    /// producer's test support can arrange it for a host's test. Deliberately
    /// not <c>public</c>: a consumer must never see — let alone depend on —
    /// the surface's storage keys, which is why the copy naming them is
    /// producer-owned and lives here.
    /// </remarks>
    internal const string ConfigKey = "xg_filter_config";

    /// <summary>
    /// The set of expanded facet rows, under its own key (camelCase after the
    /// <c>xg_</c> prefix, per the sibling convention: <c>xg_quizMix</c>,
    /// <c>xg_xgpPattern</c>). User preference, not filter state — it never
    /// rides in the <see cref="ConfigKey"/> blob, and staging a saved filter
    /// or Clear filters never changes it. The value is a JSON array of
    /// <see cref="FilterFacet"/> member names, written in row order; restore is
    /// all-or-nothing, so anything unreadable — not an array, a name no row
    /// answers to, a numeric token — restores every row collapsed rather than
    /// salvaging part of it. A display preference that resets once is not
    /// data, which is also why the key this replaced is neither read nor
    /// migrated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c> for the same reason as <see cref="ConfigKey"/> — see its remarks.
    /// </para>
    /// <para>
    /// This is the one value the panel does serialize itself, and the exception
    /// proves the rule: the <see cref="ConfigKey"/> blob's shape is the lib's, so
    /// nothing here touches a serializer for it, while the set of open rows is
    /// the panel's own view-state and belongs to no other party.
    /// </para>
    /// </remarks>
    internal const string DisclosureKey = "xg_expandedFilters";

    /// <summary>
    /// Whether the container over the rows is open — the one disclosure over
    /// all eight rows (halheinrich/backgammon#231). Its own key, beside
    /// <see cref="DisclosureKey"/> rather than inside it: that key's value is a
    /// list of <see cref="FilterFacet"/> member names and this container is not
    /// a facet, so a sentinel name in it would be the very corruption its
    /// all-or-nothing restore exists to refuse. User preference like the rows':
    /// never part of the <see cref="ConfigKey"/> blob, and staging a saved
    /// filter or Clear filters never changes it.
    /// <para>
    /// The value is the literal <c>true</c> or <c>false</c> and nothing else —
    /// one bit needs no serializer, which is why <see cref="DisclosureKey"/>
    /// remains the only value the panel serializes itself. Restore is
    /// <see cref="bool.TryParse(string?, out bool)"/>: anything unreadable
    /// leaves the container folded, the same posture a fresh visit gets.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <c>internal</c> for the same reason as <see cref="ConfigKey"/> — see its
    /// remarks.
    /// </remarks>
    internal const string MoreFiltersKey = "xg_moreFiltersOpen";

    private readonly BrowserStorage _browser;
    private readonly IFilterStorageRefusalSink _refusals;

    /// <summary>The surface's storage, over the host's accessor and its refusal sink.</summary>
    /// <param name="browser">The browser-storage accessor.</param>
    /// <param name="refusals">The host's owner of the refusal occurrence.</param>
    internal FilterStorage(BrowserStorage browser, IFilterStorageRefusalSink refusals)
    {
        ArgumentNullException.ThrowIfNull(browser);
        ArgumentNullException.ThrowIfNull(refusals);

        _browser = browser;
        _refusals = refusals;
    }

    /// <summary>
    /// Read <paramref name="key"/>, telling the host of a refusal before
    /// returning it.
    /// </summary>
    /// <param name="key">One of this type's keys.</param>
    /// <returns>The accessor's result: stored, absent or refused.</returns>
    internal async Task<BrowserStorageReadResult> ReadAsync(string key)
    {
        var read = await _browser.ReadAsync(BrowserStorageArea.Local, key);

        if (read.IsRefused)
        {
            _refusals.ReportRefused(read.Refusal);
        }

        return read;
    }

    /// <summary>
    /// Write <paramref name="value"/> under <paramref name="key"/>, telling
    /// the host of a refusal before returning it.
    /// </summary>
    /// <param name="key">One of this type's keys.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The accessor's result: succeeded or refused.</returns>
    internal async Task<BrowserStorageWriteResult> WriteAsync(string key, string value)
    {
        var written = await _browser.WriteAsync(BrowserStorageArea.Local, key, value);

        if (written.IsRefused)
        {
            _refusals.ReportRefused(written.Refusal);
        }

        return written;
    }
}
