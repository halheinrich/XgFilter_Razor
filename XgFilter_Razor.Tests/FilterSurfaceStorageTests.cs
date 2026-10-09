using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.Testing;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins the producer's test support for hosts (<see cref="FilterSurfaceStorage"/>)
/// against a real render of the surface, used exactly as a host's suite uses
/// it — never naming a key or an interop call. Each helper is held to what the
/// surface really does: a plan built from them verifies clean against the real
/// calls, and a plan missing one fails where the surface makes it. A rename or
/// a new call that missed the helper fails here, before it reaches a host.
/// </summary>
public class FilterSurfaceStorageTests : BunitContext
{
    private static readonly FilterSourceToken Source = FilterSourceToken.FromGeneration(1);

    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterSurfaceStorageTests()
    {
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();
        _storage = BrowserStoragePlan.On(JSInterop);
        Setup.ReportSource(Source);
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    [Fact]
    public void ExpectFilterRestore_OfASelection_IsWhatTheSurfaceReads_AndPutsOnScreen()
    {
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.1 });
        _storage.ExpectFilterPanelMount();

        var cut = Render<FilterSurface>();

        Assert.Equal("0.1", cut.Find("#errorMin").GetAttribute("value"));
        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        _storage.Verify();
    }

    public static TheoryData<FilterRestoration> AnswerableOutcomes => new()
    {
        FilterRestoration.NothingStored,
        FilterRestoration.Refused,
        FilterRestoration.Unreadable,
    };

    // Each outcome a host can arrange is the outcome the surface records.
    [Theory]
    [MemberData(nameof(AnswerableOutcomes))]
    public void ExpectFilterRestore_OfAnOutcome_SettlesTheRestorationAsThatOutcome(FilterRestoration outcome)
    {
        _storage.ExpectFilterRestore(outcome);
        _storage.ExpectFilterPanelMount();

        Render<FilterSurface>();

        Assert.Equal(outcome, Setup.Current.Restoration);
        _storage.Verify();
    }

    [Theory]
    [InlineData(FilterRestoration.Restored)]
    [InlineData(FilterRestoration.Pending)]
    public void RestoreAnswer_RefusesAnOutcomeThatIsNotABrowsersAnswer(FilterRestoration outcome)
    {
        Assert.Throws<ArgumentException>(() => FilterSurfaceStorage.RestoreAnswer(outcome));
    }

    // A held restoration is pending until the host's test releases it, in the
    // helper's own terms.
    [Fact]
    public async Task ExpectHeldFilterRestore_KeepsTheRestorationPending_UntilReleased()
    {
        var restore = _storage.ExpectHeldFilterRestore();
        _storage.ExpectFilterPanelMount();
        Render<FilterSurface>();
        Assert.True(restore.IsReached);
        Assert.Equal(FilterRestoration.Pending, Setup.Current.Restoration);

        restore.Release(FilterSurfaceStorage.RestoreAnswer(new FilterConfig { ErrorMin = 0.1 }));
        await Setup.RestoreAsync().WaitAsync(DefaultWaitTimeout);

        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        _storage.Verify();
    }

    // Every mount reads the two display preferences, and the restoration is
    // read once per boot: two mounts are one restore and two mounts' reads.
    [Fact]
    public async Task ExpectFilterPanelMount_IsWhatEachMountReads()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount(times: 2);

        Render<FilterSurface>();
        await DisposeComponentsAsync();
        Render<FilterSurface>();

        _storage.Verify();
    }

    [Fact]
    public void ExpectFilterPanelMountRefused_RefusesEachRead_AndEachReachesTheSink()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMountRefused();

        Render<FilterSurface>();

        Assert.Equal(2, _refusals.Refusals.Count);
        _storage.Verify();
    }

    // A mount without its preference reads declared is a call the plan did
    // not expect: the helper is not decoration.
    [Fact]
    public void AMountWithoutExpectFilterPanelMount_FailsTheVerification()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);

        Assert.ThrowsAny<Exception>(() => Render<FilterSurface>());
        Assert.Throws<BrowserStorageAssertionException>(() => _storage.Verify());
    }

    // A commit writes the committed selection however the test spelled it:
    // list order in the test's config is not the surface's.
    [Fact]
    public async Task ExpectFilterCommit_IsWhatApplyWrites_WhateverOrderTheTestListsIn()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterCommit(
            new FilterConfig { ErrorMin = 0.1, ContactTypes = [ContactType.Race, ContactType.Contact] },
            BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterSurface>();

        await cut.InvokeAsync(() => Setup.Stage(
            new FilterConfig { ErrorMin = 0.1, ContactTypes = [ContactType.Contact, ContactType.Race] }));
        await cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Apply Filter"))
            .ClickAsync(new MouseEventArgs());

        _storage.Verify();
    }

    [Fact]
    public async Task ExpectHeldFilterCommit_HoldsApplysWrite_UntilReleased()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        var write = _storage.ExpectHeldFilterCommit(new FilterConfig());
        var cut = Render<FilterSurface>();

        var clear = cut.Find("#clearFilters").ClickAsync(new MouseEventArgs());
        Assert.True(write.IsReached);
        Assert.False(clear.IsCompleted);

        write.Release(BrowserStorageWriteAnswer.Succeeded);
        await clear.WaitAsync(DefaultWaitTimeout);

        _storage.Verify();
    }

    // The two display preferences' writes, as a host's test opens what it needs.
    [Fact]
    public void ExpectFilterFoldToggle_AndExpectFilterRowsToggle_AreWhatTheTogglesWrite()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectFilterRowsToggle([FilterFacet.DiceRolls], BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectFilterRowsToggle([FilterFacet.DiceRolls, FilterFacet.Players], BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectFilterFoldToggle(open: false, BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterSurface>();

        cut.Find("#moreFiltersToggle").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
        cut.Find("#facetToggle_DiceRolls").Click();
        cut.Find("#facetToggle_Players").Click();
        cut.Find("#moreFiltersToggle").Click();

        _storage.Verify();
    }
}
