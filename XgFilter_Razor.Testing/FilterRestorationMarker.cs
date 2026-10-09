namespace XgFilter_Razor.Testing;

using XgFilter_Razor.Components.Internal;

/// <summary>
/// The filter panel's restoration marker, for a host's browser tests: CSS
/// selectors and a reading of the attribute the panel's root carries, so a
/// test can wait for this boot's restoration and read its outcome without
/// naming the panel's storage keys or the attribute itself
/// (halheinrich/backgammon#346).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it reports.</b> <see cref="FilterRestoration.Pending"/> until the
/// mounted panel has applied everything it restores — its two display
/// preferences, and the boot's restoration of the selection — and then the
/// selection's outcome, failures included (<see cref="FilterRestoration.Refused"/>,
/// <see cref="FilterRestoration.Unreadable"/>). It is a different fact from
/// whether Apply or Run is permitted. Each mount settles its own marker, so a
/// navigate-back reports Pending until that mount's preferences are restored.
/// </para>
/// <para>
/// <b>The use it exists for.</b> Wait for <see cref="SettledSelector"/> before
/// acting on the panel. Before it, the panel's own restore of the More filters
/// container may still be in flight, so a click on the container's toggle
/// can race it; after it, what the panel shows is what it restored.
/// </para>
/// <para>
/// Plain strings, so a browser-driving test (Playwright or another) can use
/// them in its own locators. Host code reads the outcome from
/// <see cref="FilterSetupSnapshot.Restoration"/>, never from the DOM.
/// </para>
/// </remarks>
public static class FilterRestorationMarker
{
    /// <summary>The name of the attribute the panel's root carries, for reading its value.</summary>
    public static string AttributeName => FilterPanel.RestorationAttribute;

    /// <summary>
    /// A CSS selector matching the panel once its restoration has settled,
    /// whatever the outcome — the thing to wait for before acting on it.
    /// </summary>
    public static string SettledSelector =>
        $"[{FilterPanel.RestorationAttribute}]:not([{FilterPanel.RestorationAttribute}=\"{FilterRestoration.Pending}\"])";

    /// <summary>A CSS selector matching the panel while it reports <paramref name="outcome"/>.</summary>
    /// <param name="outcome">The outcome, or <see cref="FilterRestoration.Pending"/>.</param>
    /// <returns>The selector.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="outcome"/> is not a declared member.</exception>
    public static string Selector(FilterRestoration outcome)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not a restoration outcome.");
        }

        return $"[{FilterPanel.RestorationAttribute}=\"{outcome}\"]";
    }

    /// <summary>
    /// What the marker's value says — the value of <see cref="AttributeName"/>
    /// as a test read it off the panel's root.
    /// </summary>
    /// <param name="value">The attribute's value.</param>
    /// <returns>The outcome it reports.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is not a value the panel writes — a missing
    /// attribute, or a marker this version does not know.
    /// </exception>
    public static FilterRestoration Parse(string? value) =>
        Enum.TryParse<FilterRestoration>(value, ignoreCase: false, out var outcome)
        && Enum.IsDefined(outcome)
        && !int.TryParse(value, out _)
            ? outcome
            : throw new ArgumentException($"'{value}' is not a restoration marker the filter panel writes.", nameof(value));
}
