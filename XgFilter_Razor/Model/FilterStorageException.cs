namespace XgFilter_Razor;

/// <summary>
/// The one exception an <see cref="IFilterDocumentStorage"/> adapter signals
/// failure with — hosts wrap their native failures (browser-interop
/// <c>JSException</c>, <c>IOException</c>, HTTP errors) in this so
/// <see cref="SavedFiltersStore"/>'s typed catch can keep the
/// degrade-never-block posture producer-side without knowing any host's
/// failure types. An exception of any other type escaping an adapter is a
/// bug, not a degrade, and propagates.
/// </summary>
public sealed class FilterStorageException : Exception
{
    /// <summary>Create with a message describing the failed operation.</summary>
    /// <param name="message">What failed, in the adapter's terms.</param>
    public FilterStorageException(string message)
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
    public FilterStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
