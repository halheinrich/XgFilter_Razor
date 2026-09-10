namespace XgFilter_Razor;

/// <summary>
/// The one exception an <see cref="IDocumentStorage"/> adapter signals
/// failure with — hosts wrap their native failures (browser-interop
/// <c>JSException</c>, <c>IOException</c>, HTTP errors) in this so
/// <see cref="NamedDocumentStore{TValue, TSelf}"/>'s typed catch can keep the
/// degrade-never-block posture producer-side without knowing any host's
/// failure types. An exception of any other type escaping an adapter is a
/// bug, not a degrade, and propagates. Like the seam it belongs to, it is
/// named for the layer rather than for any document kind
/// (halheinrich/backgammon#190 leg (D)) — one store keeps every named
/// document, so one failure type serves them all.
/// </summary>
public sealed class DocumentStorageException : Exception
{
    /// <summary>Create with a message describing the failed operation.</summary>
    /// <param name="message">What failed, in the adapter's terms.</param>
    public DocumentStorageException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Create wrapping the native failure — the usual adapter form, keeping
    /// the original exception on <see cref="Exception.InnerException"/> for
    /// diagnostics.
    /// </summary>
    /// <param name="message">What failed, in the adapter's terms.</param>
    /// <param name="innerException">The native failure being wrapped.</param>
    public DocumentStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
