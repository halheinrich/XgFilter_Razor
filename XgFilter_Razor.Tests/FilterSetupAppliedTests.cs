using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// <see cref="FilterSetup.Applied"/>: the host's explicit entry for the work an applied
/// selection triggers, raised by Apply and Clear alone, with the resulting snapshot.
/// </summary>
public class FilterSetupAppliedTests : BunitContext
{
    private static readonly FilterSourceToken Token = FilterSourceToken.FromGeneration(1);
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
        Setup.ReportSource(Token);
    }

    [Fact]
    public async Task Apply_RaisesApplied_WithTheSnapshotInEffect_AfterObserversSawIt()
    {
        await SettledAsync();
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        var order = new List<string>();
        Setup.Attach(_ => order.Add("observer"));
        order.Clear();
        FilterSetupSnapshot? applied = null;
        Setup.Applied += snapshot => { applied = snapshot; order.Add("applied"); };
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);

        await Setup.ApplyAsync();

        Assert.Same(Setup.Current, applied);
        Assert.True(applied!.IsInEffectFor(Token));
        Assert.Equal(new FilterConfig { ErrorMin = 0.1 }, applied.ConfigInEffectFor(Token));
        Assert.Equal(["observer", "applied"], order);
        _storage.Verify();
    }

    [Fact]
    public async Task Clear_RaisesApplied_WithTheEmptySelectionInEffect()
    {
        await SettledAsync();
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();
        var raised = 0;
        Setup.Applied += _ => raised++;
        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Succeeded);

        await Setup.ClearAsync();

        Assert.Equal(1, raised);
        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(Token));
        _storage.Verify();
    }

    [Fact]
    public async Task EditsReportsAndRestoration_RaiseNothing_AndARefusedApplyRaisesNothing()
    {
        var raised = 0;
        Setup.Applied += _ => raised++;
        await SettledAsync();                                   // restoration and a source report
        Setup.Edit(d => d with { ErrorMinText = "0.1" });       // an edit
        Setup.Edit(d => d with { ErrorMinText = "not a number" });
        await Setup.ApplyAsync();                               // refused: an invalid field
        Setup.ReportSource(FilterSourceToken.FromGeneration(2));
        Assert.Equal(0, raised);
        _storage.Verify();
    }
}
