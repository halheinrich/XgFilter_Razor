using Bunit;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components.Internal;
using XgFilter_Razor.Testing;

namespace XgFilter_Razor.Tests;

// The producer's test-support seam, pinned the way a host suite uses it.
//
// This file deliberately never names a storage key. That is the point of the
// seam — and it is also what makes the pin worth having: the assertion is that
// seeding through FilterPanelTestState puts the selection where the panel
// actually reads it, so a key rename that missed the seeder fails HERE, in the
// producer's own suite, instead of silently turning every host's
// "previous visit left a selection" arrangement into a no-op that still passes.
public class FilterPanelTestStateTests : BunitContext
{
    public FilterPanelTestStateTests()
    {
        // Loose mode — the panel's OnAfterRenderAsync issues localStorage
        // getItem calls beyond the one seeded here; default (null) is the
        // "nothing stored" answer they expect.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SeedStoredSelection_IsRestoredByTheNextPanelRender()
    {
        FilterPanelTestState.SeedStoredSelection(JSInterop, new FilterConfig { ErrorMin = 0.1 });

        var cut = Render<FilterPanel>();

        cut.WaitForAssertion(() => Assert.Equal(
            "0.1",
            cut.Find("input[type='number'][placeholder='Min']").GetAttribute("value")));
    }

    // The seeded blob is the config's own serialization, so a test cannot
    // arrange a selection the product could never have written — round-tripped
    // here through a config that exercises more than one facet.
    [Fact]
    public void SeedStoredSelection_RoundTripsTheWholeSelection()
    {
        var stored = new FilterConfig { ErrorMin = 0.1, ErrorMax = 0.5, MoveNumberMin = 7 };

        FilterPanelTestState.SeedStoredSelection(JSInterop, stored);
        var cut = Render<FilterPanel>();
        cut.Find("#moreFiltersToggle").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("0.1", cut.Find("input[type='number'][placeholder='Min']").GetAttribute("value"));
            Assert.Equal("0.5", cut.Find("input[type='number'][placeholder='Max']").GetAttribute("value"));
        });
    }

    [Fact]
    public void SeedStoredSelection_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => FilterPanelTestState.SeedStoredSelection(null!, new FilterConfig()));
        Assert.Throws<ArgumentNullException>(
            () => FilterPanelTestState.SeedStoredSelection(JSInterop, null!));
    }
}
