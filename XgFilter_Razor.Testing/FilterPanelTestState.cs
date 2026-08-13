namespace XgFilter_Razor.Testing;

using Bunit;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components.Internal;

/// <summary>
/// Producer-owned seeding of the browser state the filter panel restores from,
/// for host test suites: the arrangement half of "a previous visit left a
/// stored selection behind".
/// </summary>
/// <remarks>
/// <para>
/// The panel's storage keys are deliberately not consumer surface — a host must
/// never build behaviour on them, which is why the constants are
/// <see langword="internal"/> and the copy naming them is producer-owned. But a
/// host test arranging a navigate-back or reload scenario genuinely does need
/// the state to exist, and before this seam existed the only way to get it was
/// to repeat the key as a literal in the host's own suite. That dependency was
/// real either way; keeping the constant internal only made it untyped, so a
/// producer-side rename would leave the literal behind and the host's test
/// would go on passing for the wrong reason — arranging nothing and asserting
/// the "no stored selection" path.
/// </para>
/// <para>
/// So the fact moves to where it can be kept true: hosts state the intent and
/// the producer supplies the mechanism, and the key never leaves this repo.
/// The assembly is test support, never product (<c>IsPackable=false</c>) — no
/// app-graph project may reference it.
/// </para>
/// </remarks>
public static class FilterPanelTestState
{
    /// <summary>
    /// Arrange a stored filter selection, as a previous visit would have left
    /// it: the next panel render restores <paramref name="config"/> into its
    /// edit buffers on first render.
    /// </summary>
    /// <remarks>
    /// Serialization is the config's own (<c>FilterConfig.ToJson</c>), so what
    /// is seeded is exactly what the panel writes — a test cannot arrange a
    /// blob the product could never have produced.
    /// </remarks>
    /// <param name="jsInterop">
    /// The bUnit JSInterop of the context about to render the panel (directly
    /// or through <c>FilterSurface</c>).
    /// </param>
    /// <param name="config">The selection the previous visit applied.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="jsInterop"/> or <paramref name="config"/> is null.
    /// </exception>
    public static void SeedStoredSelection(BunitJSInterop jsInterop, FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(jsInterop);
        ArgumentNullException.ThrowIfNull(config);

        jsInterop.Setup<string?>("localStorage.getItem", FilterPanel.ConfigKey)
                 .SetResult(config.ToJson());
    }
}
