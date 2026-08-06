namespace XgFilter_Razor;

/// <summary>
/// The host storage-adapter seam behind <see cref="SavedFiltersStore"/>:
/// per-document text I/O keyed by file name, and nothing else. Hosts supply
/// only the raw read and write of a named document wherever their storage
/// lives (a File System Access directory handle, a server endpoint, browser
/// storage); everything above that — the status taxonomy, the
/// degrade-never-block posture, preserve-file-on-corrupt, document names and
/// the legacy-name migration rule, the JSON round-trip — is producer-owned
/// and must not be re-encoded host-side.
///
/// <para>
/// <b>Failure contract.</b> Implementations wrap every native failure
/// (a <c>JSException</c> from browser interop, an <c>IOException</c>, an HTTP
/// error) in <see cref="FilterStorageException"/> — that is the one exception
/// type the store catches and degrades on. Anything else escaping an adapter
/// is treated as a bug and propagates. An absent document is not a failure:
/// <see cref="ReadAsync"/> returns <c>null</c> for it.
/// </para>
///
/// <para>
/// The seam is deliberately generalized by document name rather than bound to
/// one file, so a future sibling store (named mix saves is queued) reuses the
/// same adapter with its own document identity — zero interface change.
/// </para>
/// </summary>
public interface IFilterDocumentStorage
{
    /// <summary>
    /// Read the named document's full text.
    /// </summary>
    /// <param name="fileName">The document's file name (no path — where documents live is the adapter's business).</param>
    /// <returns>The document's text, or <c>null</c> when no such document exists.</returns>
    /// <exception cref="FilterStorageException">The read failed (as opposed to finding nothing).</exception>
    Task<string?> ReadAsync(string fileName);

    /// <summary>
    /// Write <paramref name="json"/> as the named document's full content,
    /// creating it if absent and replacing it if present.
    /// </summary>
    /// <param name="fileName">The document's file name.</param>
    /// <param name="json">The document text to write.</param>
    /// <exception cref="FilterStorageException">The write failed.</exception>
    Task WriteAsync(string fileName, string json);
}
