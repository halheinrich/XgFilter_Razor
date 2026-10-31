using BgUiPrimitives_Razor;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace XgFilter_Razor;

/// <summary>
/// The filter surface's route into the app's one
/// <see cref="BrowserStorageCondition"/>: the <see cref="IFilterStorageRefusalSink"/>
/// that <see cref="FilterSurfaceServiceCollectionExtensions.AddFilterSurface(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
/// registers, for a host whose storage notice reads the shared condition.
/// </summary>
/// <remarks>
/// <para>
/// <b>An adapter in front of the condition, not a second holder.</b> The
/// condition is one fact however it arose, so the surface's refusals reach the
/// same occurrence the host's other stores report to. It lives here, and not
/// beside the condition, because this library depends on BgUiPrimitives_Razor
/// and not the reverse: only here are both types visible. It forwards every
/// refusal the surface reports, as <see cref="IFilterStorageRefusalSink"/>
/// asks; deduplicating is the condition's.
/// </para>
/// <para>
/// <b>It logs, because nothing else does.</b> The surface logs nothing and the
/// condition holds no logger, so the surface's line is written here, with the
/// browser's exception attached: the panel's filters work for this visit but
/// may not be remembered.
/// </para>
/// <para>
/// <b>The occurrence is recorded first, and a failing logger cannot stop it.</b>
/// The sink contract lets an exception propagate out of the surface's
/// operation. The condition is told before the log call, and a failure of
/// that call is contained, so a throwing logging provider can keep neither
/// the occurrence nor the surface's operation from completing.
/// </para>
/// <para>
/// <b>App-scoped, like the condition it feeds</b>, so a refusal that arrives
/// after the page that started the call has gone still lands. Called on the
/// renderer's synchronization context.
/// </para>
/// </remarks>
/// <param name="condition">The app's storage condition.</param>
/// <param name="logger">The adapter's own logger.</param>
public sealed class BrowserStorageConditionRefusalSink(
    BrowserStorageCondition condition, ILogger<BrowserStorageConditionRefusalSink> logger)
    : IFilterStorageRefusalSink
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="refusal"/> is <see langword="null"/>.</exception>
    public void ReportRefused(JSException refusal)
    {
        condition.ReportRefused(refusal);

        try
        {
            logger.LogWarning(refusal,
                "The browser refused a storage call the filter panel made; its filters work for this visit "
                + "but may not be remembered.");
        }
#pragma warning disable CA1031 // Contained by contract: a failing logging provider must not undo a recorded refusal.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
