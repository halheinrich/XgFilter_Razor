namespace XgFilter_Razor;

/// <summary>
/// The host storage-adapter seam behind
/// <see cref="NamedDocumentStore{TValue, TSelf}"/>: per-document text I/O
/// keyed by file name, and nothing else. Hosts supply only the raw read and
/// write of a named document wherever their storage lives (a File System
/// Access directory handle, a server endpoint, browser storage); everything
/// above that — the status taxonomy, the degrade-never-block posture,
/// preserve-file-on-corrupt, document names and the legacy-name migration
/// rule, the JSON round-trip — is producer-owned and must not be re-encoded
/// host-side.
///
/// <para>
/// <b>The seam knows no document kind</b>, which is why neither "filter" nor
/// "named" appears in its name (halheinrich/backgammon#190 leg (D)): it moves
/// text for a file name, and every question of <i>which</i> document that is
/// belongs to the store above it. An adapter is named for where it reads and
/// writes, never for what it carries.
/// </para>
///
/// <para>
/// <b>Failure contract.</b> Implementations wrap every native failure
/// (a <c>JSException</c> from browser interop, an <c>IOException</c>, an HTTP
/// error) in <see cref="DocumentStorageException"/> — that is the one exception
/// type the store catches and degrades on. Anything else escaping an adapter
/// is treated as a bug and propagates. An absent document is not a failure:
/// <see cref="ReadAsync"/> returns <c>null</c> for it.
/// </para>
///
/// <para>
/// The seam is deliberately generalized by document name rather than bound to
/// one file, so a sibling store reuses the same adapter with its own document
/// identity — zero interface change. That generalization has since been taken
/// up: <see cref="NamedDocumentStore{TValue, TSelf}"/> keeps <i>any</i>
/// <see cref="BgDataTypes_Lib.NamedCollection{TValue, TSelf}"/> document over
/// this seam, and a specialization such as <see cref="SavedFiltersStore"/>
/// contributes only its file names. The queued mix-saves document is one more
/// specialization, and still no interface change.
/// </para>
/// </summary>
public interface IDocumentStorage
{
    /// <summary>
    /// Read the named document's full text.
    /// </summary>
    /// <param name="fileName">The document's file name (no path — where documents live is the adapter's business).</param>
    /// <returns>The document's text, or <c>null</c> when no such document exists.</returns>
    /// <exception cref="DocumentStorageException">The read failed (as opposed to finding nothing).</exception>
    Task<string?> ReadAsync(string fileName);

    /// <summary>
    /// Write <paramref name="json"/> as the named document's full content,
    /// creating it if absent and replacing it if present.
    /// </summary>
    /// <param name="fileName">The document's file name.</param>
    /// <param name="json">The document text to write.</param>
    /// <exception cref="DocumentStorageException">The write failed.</exception>
    Task WriteAsync(string fileName, string json);
}
