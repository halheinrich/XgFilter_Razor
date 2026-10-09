using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.Components.Internal;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins the restoration marker (halheinrich/backgammon#346) — what a host's
/// browser test waits on and reads instead of the panel's storage keys — and
/// the producer's selectors for it (<see cref="FilterRestorationMarker"/>),
/// used the way a host's test uses them. The marker reports one outcome on
/// failure as on success, and settles per mount.
/// </summary>
public class FilterRestorationMarkerTests : BunitContext
{
    private static readonly FilterSourceToken Source = FilterSourceToken.FromGeneration(1);

    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterRestorationMarkerTests()
    {
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();
        _storage = BrowserStoragePlan.On(JSInterop);
        Services.GetRequiredService<FilterSetup>().ReportSource(Source);
    }

    private static FilterRestoration Marker(IRenderedComponent<FilterSurface> cut) =>
        FilterRestorationMarker.Parse(
            cut.Find(FilterRestorationMarker.Selector(FilterRestoration.Pending) + ", " + FilterRestorationMarker.SettledSelector)
               .GetAttribute(FilterRestorationMarker.AttributeName));

    public static TheoryData<FilterRestoration> Outcomes => new()
    {
        FilterRestoration.Restored,
        FilterRestoration.NothingStored,
        FilterRestoration.Refused,
        FilterRestoration.Unreadable,
    };

    // Pending while the read is held — even though nothing is wrong yet,
    // nothing is settled — and then the one outcome it settled with,
    // failures as observable as success.
    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task TheMarker_IsPendingUntilTheRestorationSettles_ThenReportsItsOutcome(FilterRestoration outcome)
    {
        var restore = _storage.ExpectHeldFilterRestore();
        _storage.ExpectFilterPanelMount();
        var cut = Render<FilterSurface>();

        Assert.Equal(FilterRestoration.Pending, Marker(cut));
        Assert.Empty(cut.FindAll(FilterRestorationMarker.SettledSelector));

        restore.Release(outcome == FilterRestoration.Restored
            ? FilterSurfaceStorage.RestoreAnswer(new FilterConfig { ErrorMin = 0.1 })
            : FilterSurfaceStorage.RestoreAnswer(outcome));
        await cut.FindComponent<FilterPanel>().Instance.MountRestored.WaitAsync(DefaultWaitTimeout);

        cut.WaitForAssertion(() => Assert.Equal(outcome, Marker(cut)));
        Assert.Single(cut.FindAll(FilterRestorationMarker.SettledSelector));
        Assert.Single(cut.FindAll(FilterRestorationMarker.Selector(outcome)));
        _storage.Verify();
    }

    // Each mount settles its own marker: a navigate-back reports Pending until
    // that mount's display preferences are restored — the restore a click on
    // the container's toggle could otherwise race — although the boot's
    // outcome was settled long before.
    [Fact]
    public async Task ARemount_ReportsPending_UntilItsOwnRestoresSettle()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        var fold = _storage.ExpectHeldRead(BrowserStorageArea.Local, FilterStorage.MoreFiltersKey);
        _storage.ExpectRead(BrowserStorageArea.Local, FilterStorage.DisclosureKey, BrowserStorageReadAnswer.Absent);
        var first = Render<FilterSurface>();
        Assert.Equal(FilterRestoration.NothingStored, Marker(first));

        await DisposeComponentsAsync();
        var second = Render<FilterSurface>();

        Assert.Equal(FilterRestoration.Pending, Marker(second));

        fold.Release(BrowserStorageReadAnswer.Absent);
        await second.FindComponent<FilterPanel>().Instance.MountRestored.WaitAsync(DefaultWaitTimeout);

        second.WaitForAssertion(() => Assert.Equal(FilterRestoration.NothingStored, Marker(second)));
        _storage.Verify();
    }

    // The marker is the outcome alone: it says nothing about the gates. A
    // restored selection is settled and not in effect; a refused read is
    // settled and leaves the defaults unresolved.
    [Fact]
    public void TheMarker_IsNotTheGate()
    {
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.1 });
        _storage.ExpectFilterPanelMount();

        var cut = Render<FilterSurface>();

        Assert.Equal(FilterRestoration.Restored, Marker(cut));
        Assert.False(Services.GetRequiredService<FilterSetup>().Current.IsInEffectFor(Source));
        _storage.Verify();
    }

    // The selectors and the reading are the producer's spelling of one
    // attribute, each member by its own name.
    [Fact]
    public void Parse_ReadsEveryMemberTheMarkerWrites_AndNothingElse()
    {
        foreach (var outcome in Enum.GetValues<FilterRestoration>())
            Assert.Equal(outcome, FilterRestorationMarker.Parse(outcome.ToString()));

        Assert.Throws<ArgumentException>(() => FilterRestorationMarker.Parse(null));
        Assert.Throws<ArgumentException>(() => FilterRestorationMarker.Parse("restored"));
        Assert.Throws<ArgumentException>(() => FilterRestorationMarker.Parse("1"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterRestorationMarker.Selector((FilterRestoration)99));
    }
}
