using Bunit;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;

namespace XgFilter_Razor.Tests;

public class FilterPanelTests : BunitContext
{
    public FilterPanelTests()
    {
        // Loose mode — OnAfterRenderAsync issues localStorage.getItem calls;
        // the mock returns default (null) for each, which is what the
        // component expects for "no persisted state."
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_DefaultParameters_ProducesFilterCardMarkup()
    {
        var cut = Render<FilterPanel>();

        Assert.Contains("Filters", cut.Markup);
        Assert.Contains("Apply Filter", cut.Markup);
        Assert.Contains("Reset", cut.Markup);
    }

    [Fact]
    public void EventCallbacks_AreAccepted()
    {
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig _) => { })
            .Add(p => p.OnFilterDirty, () => { }));

        Assert.NotNull(cut);
    }

    [Fact]
    public async Task ApplyButton_RaisesFilterConfigCallback()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        var applyButton = cut.Find("button.btn-primary");
        await applyButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.NotNull(capturedConfig);
        Assert.Equal(DecisionTypeOption.Both, capturedConfig!.DecisionType);
    }
}
