using AngleSharp.Dom;
using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins the surface's storage policy (halheinrich/backgammon#374, over
/// BgUiPrimitives_Razor's <c>BrowserStorage</c>) where the browser refuses
/// storage — a disabled-storage or hostile-privacy setting, or a full quota:
/// every requested call is made, every refusal reaches the host's sink, a
/// refused commit write never undoes the choice, and rejecting a stale
/// completion never discards its refusal.
/// <para>
/// The refusals are planned per call, through the real accessor, in the
/// accessor's vocabulary; a host's side is played by
/// <see cref="RefusalNoticeHost"/> over the recording sink, which is what a
/// host binds.
/// </para>
/// </summary>
public class FilterSurfaceStorageUnavailableTests : BunitContext
{
    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromGeneration(2);

    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterSurfaceStorageUnavailableTests()
    {
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();
        _storage = BrowserStoragePlan.On(JSInterop);
        Setup.ReportSource(TokenA);
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    // The surface's Apply button and Error-range Min box, in whatever renders
    // the surface — the surface itself or a host page around it.
    private static IElement ApplyIn(IRenderedComponent<IComponent> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Apply Filter"));

    private static IElement ErrorMinIn(IRenderedComponent<IComponent> cut) => cut.Find("#errorMin");

    // ── Every call made, every refusal told ────────────────────────────────

    // A browser that refuses every read: the restoration and both display
    // preferences are refused, each refusal reaches the sink, and the first
    // render completes on the defaults with no notice of the panel's own.
    [Fact]
    public void ReadsRefused_EveryRefusalReachesTheSink_AndThePanelStartsOnItsDefaults()
    {
        _storage.ExpectFilterRestore(FilterRestoration.Refused);
        _storage.ExpectFilterPanelMountRefused();

        var cut = Render<FilterSurface>();

        Assert.Equal(3, _refusals.Refusals.Count);
        Assert.Equal(FilterRestoration.Refused, Setup.Current.Restoration);
        Assert.Equal(string.Empty, ErrorMinIn(cut).GetAttribute("value"));
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        Assert.Empty(cut.FindAll("#filterRestoreFailedNotice"));
        _storage.Verify();
    }

    // No latch: after the reads were refused, the next call is still made —
    // a toggle's write, then a commit's — and each refusal is told again.
    // A refusal is never followed by a skipped call.
    [Fact]
    public async Task AfterARefusal_TheNextCallIsStillMade_AndItsRefusalToldToo()
    {
        _storage.ExpectFilterRestore(FilterRestoration.Refused);
        _storage.ExpectFilterPanelMountRefused();
        _storage.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Refused);
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.05 }, BrowserStorageWriteAnswer.Refused);
        var cut = Render<FilterSurface>();

        cut.Find("#moreFiltersToggle").Click();
        ErrorMinIn(cut).Input("0.05");
        await ApplyIn(cut).ClickAsync(new MouseEventArgs());

        Assert.Equal(5, _refusals.Refusals.Count);
        _storage.Verify();
    }

    // A storage call that succeeds after one was refused is just a success:
    // nothing about the refusal is remembered below the host's sink.
    [Fact]
    public async Task ARefusalThenASuccess_TheSuccessStands()
    {
        _storage.ExpectFilterRestore(FilterRestoration.Refused);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.05 }, BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterSurface>();

        ErrorMinIn(cut).Input("0.05");
        await ApplyIn(cut).ClickAsync(new MouseEventArgs());

        Assert.Single(_refusals.Refusals);
        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        _storage.Verify();
    }

    // ── A refused commit write never undoes the choice ─────────────────────

    // The commit is in memory before its write: refused, it is still in
    // effect, still on screen, and still both after a navigate-back — which
    // reads nothing of the selection, since the owner holds it.
    [Fact]
    public async Task ARefusedCommitWrite_LeavesTheSelectionInEffect_AcrossANavigateBack()
    {
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.1 });
        _storage.ExpectFilterPanelMount(times: 2);
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.2 }, BrowserStorageWriteAnswer.Refused);
        var first = Render<FilterSurface>();
        ErrorMinIn(first).Input("0.2");

        await ApplyIn(first).ClickAsync(new MouseEventArgs());

        Assert.Equal(new FilterConfig { ErrorMin = 0.2 }, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.True(ApplyIn(first).HasAttribute("disabled"));

        await DisposeComponentsAsync();
        var second = Render<FilterSurface>();

        Assert.Equal("0.2", ErrorMinIn(second).GetAttribute("value"));
        Assert.True(ApplyIn(second).HasAttribute("disabled"));
        Assert.Equal(new FilterConfig { ErrorMin = 0.2 }, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.Single(_refusals.Refusals);
        _storage.Verify();
    }

    // The failed-restore notice claims the stored document could not be read,
    // and it ends at a commit only because the commit replaces that document.
    // A commit whose write the browser refused replaces nothing, so the
    // notice stands — beside whatever the host says about storage — while the
    // selection is still committed (Hal's ruling, 2026-10-07). The control is
    // FilterSurfaceTests' RestoreFailedNotice_SurvivesAnEdit_AndEndsAtACommitWhoseWriteLands.
    [Fact]
    public async Task ARefusedCommitWrite_LeavesTheFailedRestoreNotice_AcrossARemount()
    {
        _storage.ExpectFilterRestore(FilterRestoration.Unreadable);
        _storage.ExpectFilterPanelMount(times: 2);
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.05 }, BrowserStorageWriteAnswer.Refused);
        var first = Render<FilterSurface>();
        Assert.NotNull(first.Find("#filterRestoreFailedNotice"));

        ErrorMinIn(first).Input("0.05");
        await ApplyIn(first).ClickAsync(new MouseEventArgs());

        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        Assert.NotNull(first.Find("#filterRestoreFailedNotice"));

        await DisposeComponentsAsync();
        var second = Render<FilterSurface>();

        Assert.True(ApplyIn(second).HasAttribute("disabled"));
        Assert.NotNull(second.Find("#filterRestoreFailedNotice"));
        _storage.Verify();
    }

    // ── What the host is told, and when ────────────────────────────────────

    // A refusal arriving after the first render still reaches what the host
    // renders: the sink is the host's holder, and its own notification
    // re-renders the page already on screen.
    [Fact]
    public async Task ARefusalAfterTheFirstRender_ReachesTheHostsPage()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.05 }, BrowserStorageWriteAnswer.Refused);
        var page = Render<RefusalNoticeHost>();
        Assert.Empty(page.FindAll("#hostStorageNotice"));

        ErrorMinIn(page).Input("0.05");
        await ApplyIn(page).ClickAsync(new MouseEventArgs());

        page.WaitForAssertion(() => Assert.Equal("1 refused", page.Find("#hostStorageNotice").TextContent));
        _storage.Verify();
    }

    // Rejecting stale state never discards a refusal. A commit's write is
    // held while the user navigates away and the host changes the source;
    // released as refused, it changes nothing in the new setup — and its
    // refusal still reaches the host's sink, so returning to the page shows
    // it with no further storage failure. The sink is reached because it is
    // the owner's, registered in the same app scope: no page's attachment or
    // disposal stands between a storage call and it.
    [Fact]
    public async Task ARefusalCompletingAfterUnmountAndASourceChange_StillReachesTheHost_AndChangesNothing()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount(times: 2);
        var write = _storage.ExpectHeldFilterCommit(new FilterConfig { ErrorMin = 0.05 });
        var page = Render<RefusalNoticeHost>();
        ErrorMinIn(page).Input("0.05");
        var apply = ApplyIn(page).ClickAsync(new MouseEventArgs());
        Assert.True(write.IsReached);

        await DisposeComponentsAsync();
        Setup.ReportSource(TokenB);
        var replaced = Setup.Current;

        write.Release(BrowserStorageWriteAnswer.Refused);
        await apply.WaitAsync(DefaultWaitTimeout);

        Assert.Same(replaced, Setup.Current);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        Assert.Single(_refusals.Refusals);

        var back = Render<RefusalNoticeHost>();

        Assert.Equal("1 refused", back.Find("#hostStorageNotice").TextContent);
        Assert.Equal("0.05", ErrorMinIn(back).GetAttribute("value"));
        Assert.Single(_refusals.Refusals);
        _storage.Verify();
    }
}
