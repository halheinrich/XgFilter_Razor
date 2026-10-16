using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// <see cref="FilterSetup.InEffectChanged"/>: the host's entry for the work an in-effect
/// selection triggers, raised exactly when what is in effect changes, after the observers.
/// </summary>
public class FilterSetupInEffectChangedTests : BunitContext
{
    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromGeneration(2);
    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterSetupInEffectChangedTests()
    {
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    private async Task SettledAsync()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        await Setup.RestoreAsync();
        Setup.ReportSource(TokenA);
    }

    [Fact]
    public async Task Apply_Raises_WithTheSnapshotInEffect_AfterObserversSawIt()
    {
        await SettledAsync();
        Setup.Edit(d => d with { ErrorMinText = "0.1" });   // the empty selection goes out of effect: one raise
        var order = new List<string>();
        Setup.Attach(_ => order.Add("observer"));
        order.Clear();
        FilterSetupSnapshot? raised = null;
        Setup.InEffectChanged += snapshot => { raised = snapshot; order.Add("in-effect"); };
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);

        await Setup.ApplyAsync();

        Assert.Same(Setup.Current, raised);
        Assert.Equal(new FilterConfig { ErrorMin = 0.1 }, raised!.ConfigInEffectFor(TokenA));
        Assert.Equal(["observer", "in-effect"], order);
        _storage.Verify();
    }

    [Fact]
    public async Task Clear_Raises_WithTheEmptySelectionInEffect()
    {
        await SettledAsync();
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();
        var raised = 0;
        Setup.InEffectChanged += _ => raised++;
        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Succeeded);

        await Setup.ClearAsync();

        Assert.Equal(1, raised);
        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(TokenA));
        _storage.Verify();
    }

    [Fact]
    public async Task RestorationSettling_PutsTheReadyEmptySelectionInEffect_AndRaisesOnce()
    {
        Setup.ReportSource(TokenA);
        var raised = new List<FilterSetupSnapshot>();
        Setup.InEffectChanged += raised.Add;
        Assert.Null(Setup.Current.ConfigInEffectFor(TokenA));
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);

        await Setup.RestoreAsync();

        var snapshot = Assert.Single(raised);
        Assert.Equal(new FilterConfig(), snapshot.ConfigInEffectFor(TokenA));
        _storage.Verify();
    }

    [Fact]
    public async Task ASourceReport_ThatEndsTheSetup_Raises_AndEditsRaiseNothing()
    {
        await SettledAsync();   // the empty selection is in effect for TokenA
        var raised = 0;
        Setup.InEffectChanged += _ => raised++;

        Setup.Edit(d => d with { ErrorMinText = "0.1" });           // out of effect: 1
        Setup.Edit(d => d with { ErrorMinText = "0.2" });           // still out of effect: nothing
        Setup.Edit(d => d with { ErrorMinText = "not a number" });  // invalid: nothing
        await Setup.ApplyAsync();                                   // refused: nothing
        Setup.ReportSource(TokenB);                                 // another setup, nothing in effect: nothing
        Setup.Edit(d => d with { ErrorMinText = "" });              // the ready-empty selection, in effect for B: 2

        Assert.Equal(2, raised);
        Assert.True(Setup.Current.IsInEffectFor(TokenB));
        _storage.Verify();
    }
}
