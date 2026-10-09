using Microsoft.JSInterop;

namespace XgFilter_Razor;

/// <summary>
/// The host's owner of "the browser refused storage" — the occurrence a host's
/// storage notice is about — as the filter surface reports to it. A host
/// implements this on the holder its notice reads (or on a small adapter in
/// front of it), registers that holder at app scope, and names it to
/// <see cref="FilterSurfaceServiceCollectionExtensions.AddFilterSurface{TRefusalSink}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal, as it happens.</b> The filter surface calls
/// <see cref="ReportRefused"/> once for each browser-storage call it makes that
/// the browser refuses — a read or a write, of the remembered selection or of
/// the panel's display preferences — and keeps calling storage afterwards: there
/// is no latch, and a refusal is never followed by a skipped call
/// (halheinrich/backgammon#374). Deduplicating is the sink's business: the
/// condition is one fact to the user however many calls were refused, and only
/// the host knows whether its own keys share it.
/// </para>
/// <para>
/// <b>Why a registered sink, and not an event on a component.</b> A refusal can
/// arrive after the page that started the operation has gone: a commit's write
/// that completes after the user navigated away, or after the source changed.
/// The surface's state owner (<see cref="FilterSetup"/>) and this sink share
/// the app's scope, so such a refusal still reaches the host's occurrence
/// owner, and a page that mounts later shows it without another failure. No
/// subscription and no disposal stands between the two, so none can lose one.
/// </para>
/// <para>
/// <b>Called on the renderer's synchronization context</b>, after the refused
/// call returns and before anything that follows it in the surface. The
/// surface has already degraded by then — a refused read is "nothing stored",
/// a refused write leaves the in-memory choice standing — and asks nothing of
/// the sink. A page that shows the condition re-renders off the sink's own
/// change notification: the surface cannot re-render a page it does not own.
/// A sink must not throw; an exception from it propagates out of the
/// surface's operation, the same as any other bug.
/// </para>
/// </remarks>
public interface IFilterStorageRefusalSink
{
    /// <summary>Report one refused browser-storage call.</summary>
    /// <param name="refusal">
    /// The browser's refusal exactly as it was raised, for the host's log —
    /// a <c>SecurityError</c> where storage is blocked, a
    /// <c>QuotaExceededError</c> where it is full.
    /// </param>
    void ReportRefused(JSException refusal);
}
