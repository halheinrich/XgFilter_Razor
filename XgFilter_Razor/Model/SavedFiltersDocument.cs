namespace XgFilter_Razor;

/// <summary>
/// Identity of the saved-filters document — the producer-owned canonical file
/// name every host shares, plus the legacy name it supersedes.
/// <see cref="SavedFiltersStore"/> implements the migration rule these names
/// imply; this type is where the rule is stated:
///
/// <para>
/// <b>The two-name migration rule.</b> Read <see cref="FileName"/> first and
/// fall back to <see cref="LegacyFileName"/> only when the canonical file is
/// <i>absent</i> — never when it is present but unparseable (falling back on
/// corrupt would resurrect stale data while newer-but-corrupt data exists;
/// the store's <see cref="SavedFiltersStatus.LoadFailed"/> both reports the
/// corruption and keeps saving disabled so the file is preserved untouched).
/// Write only the canonical name — the first save after a legacy fallback is
/// what migrates the document — and never delete the legacy file: it stays as
/// the user's own backup, at the cost of going stale from that moment.
/// </para>
///
/// <para>
/// <b>Why the constants are <c>public</c> — deliberately unlike the panel's
/// storage keys.</b> <c>FilterPanel.ConfigKey</c> / <c>DisclosureKey</c> are
/// <c>internal</c> because no consumer may know or depend on them. The shared
/// file name is the opposite kind of fact: it is user-facing copy — every
/// host's help and the composite's degrade notices must name the file the
/// user can find in their folder — so one public source is the SSOT move,
/// and each host renders these constants rather than spelling its own copy
/// of the name.
/// </para>
///
/// <para>
/// A future sibling document (named mix saves is queued) gets its own
/// identity type beside this one, over the same
/// <see cref="IFilterDocumentStorage"/> seam.
/// </para>
/// </summary>
public static class SavedFiltersDocument
{
    /// <summary>
    /// The canonical saved-filters file name, shared by every host so the
    /// same folder serves them all. The only name ever written.
    /// </summary>
    public const string FileName = "xg-filters.json";

    /// <summary>
    /// The legacy name from BgQuiz's app-side era, read as a fallback when
    /// <see cref="FileName"/> is absent. Never written, never deleted.
    /// </summary>
    public const string LegacyFileName = "bgquiz-filters.json";
}
