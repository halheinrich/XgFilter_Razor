namespace XgFilter_Razor;

using XgFilter_Lib.Filtering;

/// <summary>
/// The saved-filters document's store: a
/// <see cref="NamedDocumentStore{TValue, TSelf}"/> over
/// <see cref="NamedFilterCollection"/>, contributing nothing but the two
/// names in <see cref="SavedFiltersDocument"/>
/// (halheinrich/backgammon#190 leg (D)).
///
/// <para>
/// Everything this store does is documented on the base and belongs to every
/// named document a host keeps: the lifecycle
/// (<see cref="NamedDocumentStore{TValue, TSelf}.LoadAsync"/> /
/// <see cref="NamedDocumentStore{TValue, TSelf}.SaveAsync"/> /
/// <see cref="NamedDocumentStore{TValue, TSelf}.DeleteAsync"/> /
/// <see cref="NamedDocumentStore{TValue, TSelf}.Reset"/>), degrade-never-block,
/// preserve-on-corrupt, write-failure-keeps-memory, the load-version staleness
/// guard, and the two-name migration rule. The sibling document the mix-saves
/// arc queues is another eleven-line specialization beside this one, not a
/// second copy of any of that.
/// </para>
/// </summary>
public sealed class SavedFiltersStore : NamedDocumentStore<FilterConfig, NamedFilterCollection>
{
    /// <summary>
    /// Create a store over the host's storage adapter; see
    /// <see cref="NamedDocumentStore{TValue, TSelf}"/> for what a
    /// <c>null</c> adapter means.
    /// </summary>
    /// <param name="storage">The host's document I/O, or <c>null</c> for none.</param>
    public SavedFiltersStore(IFilterDocumentStorage? storage) : base(storage) { }

    /// <inheritdoc/>
    protected override string FileName => SavedFiltersDocument.FileName;

    /// <inheritdoc/>
    protected override string? LegacyFileName => SavedFiltersDocument.LegacyFileName;
}
