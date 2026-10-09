using BgUiPrimitives_Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace XgFilter_Razor;

/// <summary>
/// Registers what <see cref="Components.FilterSurface"/> needs with a host's
/// services: the setup-state owner (<see cref="FilterSetup"/>), the
/// surface's storage over <see cref="BrowserStorage"/>, and the host's
/// refusal sink as the place storage refusals go.
/// </summary>
public static class FilterSurfaceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the filter surface's services, scoped, with
    /// <typeparamref name="TRefusalSink"/> as the host's owner of the
    /// storage-refusal occurrence. Registering more than once registers once:
    /// the first call's sink stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The host registers its sink itself</b>, at app scope (Scoped in a
    /// WebAssembly host, where a scope is the app), because the sink is the
    /// host's holder and its lifetime is the host's decision. A missing
    /// registration fails when <see cref="FilterSetup"/> is first created —
    /// the first page that injects it, or the first
    /// <see cref="Components.FilterSurface"/> to render.
    /// </para>
    /// <para>
    /// <b>Scoped, decided here.</b> <see cref="FilterSetup"/> must live as long
    /// as the app — that is what lets it outlive every page — and no longer
    /// than the JS runtime its storage calls, which a server scopes per
    /// circuit. Both are the app scope. It calls
    /// <see cref="BrowserStorageServiceCollectionExtensions.AddBrowserStorage"/>
    /// for the same reason, so a host cannot register the owner without its
    /// storage. A host that prerenders on a server registers there too.
    /// </para>
    /// </remarks>
    /// <typeparam name="TRefusalSink">
    /// The host's registered holder of the storage-refusal occurrence.
    /// </typeparam>
    /// <param name="services">The host's service collection.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddFilterSurface<TRefusalSink>(this IServiceCollection services)
        where TRefusalSink : class, IFilterStorageRefusalSink
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBrowserStorage();
        services.TryAddScoped(static provider => new FilterStorage(
            provider.GetRequiredService<BrowserStorage>(),
            provider.GetRequiredService<TRefusalSink>()));
        services.TryAddScoped(static provider => new FilterSetup(provider.GetRequiredService<FilterStorage>()));
        return services;
    }
}
