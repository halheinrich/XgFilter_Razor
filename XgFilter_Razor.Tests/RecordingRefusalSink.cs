using Microsoft.JSInterop;

namespace XgFilter_Razor.Tests;

/// <summary>
/// A host's storage-refusal sink, as these suites bind one: it records every
/// refusal the filter surface reports, in order, and raises
/// <see cref="Reported"/> after each — the change notification a real host's
/// holder raises so a page already on screen re-renders.
/// </summary>
internal sealed class RecordingRefusalSink : IFilterStorageRefusalSink
{
    /// <summary>Every refusal reported, in order.</summary>
    public List<JSException> Refusals { get; } = [];

    /// <summary>Raised after each refusal is recorded.</summary>
    public event Action? Reported;

    /// <inheritdoc/>
    public void ReportRefused(JSException refusal)
    {
        Refusals.Add(refusal);
        Reported?.Invoke();
    }
}
