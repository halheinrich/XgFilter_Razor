using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// <see cref="FilterSetup.Applied"/>: the host's entry for the work a committed selection
/// triggers, raised by Apply, by Clear and by a restoration that settles the ready empty
/// selection into effect, after the observers; never by an edit or a source report.
/// </summary>
public class FilterSetupAppliedTests : BunitContext
{
    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromGeneration(2);

    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterSetupAppliedTests()
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
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        var order = new List<string>();
        Setup.Attach(_ => order.Add("observer"));
        order.Clear();
        FilterSetupSnapshot? raised = null;
        Setup.Applied += snapshot => { raised = snapshot; order.Add("applied"); };
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);

        await Setup.ApplyAsync();

        Assert.Same(Setup.Current, raised);
        Assert.Equal(new FilterConfig { ErrorMin = 0.1 }, raised!.ConfigInEffectFor(TokenA));
        Assert.Equal(["observer", "applied"], order);
        _storage.Verify();
    }

    [Fact]
    public async Task Clear_Raises_EvenWhereAnEditHadAlreadyPutTheEmptySelectionInEffect()
    {
        await SettledAsync();
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();
        var raised = 0;
        Setup.Applied += _ => raised++;

        // The edit empties the draft: the empty selection is in effect, and nothing is committed.
        Setup.Edit(d => d with { ErrorMinText = "" });
        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(TokenA));
        Assert.Equal(0, raised);

        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Succeeded);
        await Setup.ClearAsync();
        Assert.Equal(1, raised);
        _storage.Verify();
    }

    [Fact]
    public async Task RestorationSettling_WithTheReadyEmptySelection_RaisesOnce()
    {
        Setup.ReportSource(TokenA);
        var raised = new List<FilterSetupSnapshot>();
        Setup.Applied += raised.Add;
        Assert.Null(Setup.Current.ConfigInEffectFor(TokenA));
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);

        await Setup.RestoreAsync();

        var snapshot = Assert.Single(raised);
        Assert.Equal(new FilterConfig(), snapshot.ConfigInEffectFor(TokenA));
        _storage.Verify();
    }

    [Fact]
    public async Task RestorationSettling_WithARestoredSelection_RaisesNothing_UntilItIsApplied()
    {
        Setup.ReportSource(TokenA);
        var raised = 0;
        Setup.Applied += _ => raised++;
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.1 });

        await Setup.RestoreAsync();
        Assert.Equal(0, raised);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));   // restored to the draft; Apply is the consent

        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();
        Assert.Equal(1, raised);
        _storage.Verify();
    }

    [Fact]
    public async Task Edits_ARefusedApply_AndASourceReport_RaiseNothing()
    {
        await SettledAsync();   // the empty selection is in effect for TokenA
        var raised = 0;
        Setup.Applied += _ => raised++;

        Setup.Edit(d => d with { ErrorMinText = "0.1" });           // out of effect
        Setup.Edit(d => d with { ErrorMinText = "0.2" });
        Setup.Edit(d => d with { ErrorMinText = "not a number" });  // invalid
        await Setup.ApplyAsync();                                   // refused by the gate
        Setup.ReportSource(TokenB);                                 // another setup
        Setup.Edit(d => d with { ErrorMinText = "" });              // the ready-empty selection, in effect for B by the edit

        Assert.Equal(0, raised);
        Assert.True(Setup.Current.IsInEffectFor(TokenB));
        _storage.Verify();
    }
}
