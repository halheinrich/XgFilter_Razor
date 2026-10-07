using AngleSharp.Dom;
using Bunit;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.Components.Internal;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins the panel's degradation, through <see cref="FilterSurface"/>, when
/// <c>localStorage</c> is unavailable (halheinrich/backgammon#102) — a
/// disabled-storage or hostile-privacy setting, where every call raises a
/// <c>SecurityError</c> that reaches Blazor as a <see cref="JSException"/>.
/// <para>
/// Hal's ruling (2026-10-06, halheinrich/backgammon#367): the panel must
/// still deliver an applied selection to its host when remembering that
/// selection fails, with coverage of both shapes — reads failing, and reads
/// succeeding while writes fail. Unguarded, the first refused read faulted
/// the panel's first render (a host saw no filter panel at all), and a
/// refused write stopped the commit between assigning the committed config
/// and raising the events, so the host never heard of the applied
/// selection. ExtractFromXgToCsv's <c>HomeStorageUnavailableTests</c>
/// (halheinrich/backgammon#91) is the shape: a fix-prover that fails
/// against the unguarded component.
/// </para>
/// <para>
/// The failure modelled here is per browser, not per key: every
/// <c>localStorage</c> call the panel makes is refused, under the real
/// identifiers, through the composite hosts embed. What the host is told is
/// pinned here too, by binding what a host binds.
/// </para>
/// </summary>
public class FilterSurfaceStorageUnavailableTests : BunitContext
{
    public FilterSurfaceStorageUnavailableTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);

    // Host-side captures: the holder and the notice state (both app-scoped
    // in a real host — one instance across every render here, like one
    // across every mount in an app) and the two re-raised event channels.
    private readonly AppliedFilter _holder = new();
    private readonly FilterRestoreNotice _notice = new();
    private readonly List<FilterConfig> _committed = [];
    private readonly List<FilterConfig?> _reports = [];

    // The host's binding of the storage-unavailable fact: how many times the
    // composite told it. A count, not a bool, because "once per mount" is
    // part of the contract.
    private int _storageUnavailableReports;

    /// <summary>
    /// What a browser with storage refused actually raises: the JS-side
    /// <c>SecurityError</c>, surfaced through the interop boundary.
    /// </summary>
    private static JSException StorageRefused() =>
        new("SecurityError: The operation is insecure.");

    /// <summary>Every read throws, as a dead store does — whatever the key.</summary>
    private void WithReadsRefused() =>
        JSInterop.Setup<string?>("localStorage.getItem", _ => true)
                 .SetException(StorageRefused());

    /// <summary>Every write throws — whatever the key.</summary>
    private void WithWritesRefused() =>
        JSInterop.SetupVoid("localStorage.setItem", _ => true)
                 .SetException(StorageRefused());

    // A previous visit's remembered selection, for the reads-succeed shape.
    private void StoredConfig(FilterConfig config) =>
        JSInterop.Setup<string?>("localStorage.getItem", FilterPanel.ConfigKey)
                 .SetResult(config.ToJson());

    // Binds what a host binds, the storage-unavailable report included; a
    // host that binds nothing new is the other render below.
    private IRenderedComponent<FilterSurface> RenderSurface() =>
        Render<FilterSurface>(parameters => parameters
            .Add(p => p.AppliedFilter, _holder)
            .Add(p => p.RestoreNotice, _notice)
            .Add(p => p.Source, TokenA)
            .Add(p => p.Storage, new FakeDocumentStorage())
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => _committed.Add(c))
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => _reports.Add(c))
            .Add(p => p.OnStorageUnavailable, () => _storageUnavailableReports++));

    private IRenderedComponent<FilterSurface> RenderSurfaceBindingNothingNew() =>
        Render<FilterSurface>(parameters => parameters
            .Add(p => p.AppliedFilter, _holder)
            .Add(p => p.RestoreNotice, _notice)
            .Add(p => p.Source, TokenA)
            .Add(p => p.Storage, new FakeDocumentStorage())
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => _committed.Add(c))
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => _reports.Add(c)));

    private static IElement Apply(IRenderedComponent<FilterSurface> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Apply Filter"));

    private static IElement ErrorMin(IRenderedComponent<FilterSurface> cut) =>
        cut.Find("#errorMin");

    private int Reads => JSInterop.Invocations.Count(i => i.Identifier == "localStorage.getItem");

    // ── Reads refused: the structural failure this exists for ───────────────

    [Fact]
    public void ReadsRefused_TheFirstRenderCompletes_OnTheDefaults_WithNoException()
    {
        WithReadsRefused();

        // Unguarded, the first restore faults the panel's after-render and
        // the host has no filter panel, silently.
        var cut = RenderSurface();

        cut.WaitForAssertion(() => Assert.Equal(1, Reads));
        Assert.Equal(string.Empty, ErrorMin(cut).GetAttribute("value"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        // A refused read is nothing stored, not an unreadable document: no
        // restore claim either way.
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        Assert.Empty(cut.FindAll("#filterRestoreFailedNotice"));
    }

    // The latch: one refused call tells the panel storage is dead for this
    // mount, and the two reads behind it are not attempted — a failed call
    // per key would be one identical console error each.
    [Fact]
    public void ReadsRefused_StorageIsNotRetried_WithinTheMount()
    {
        WithReadsRefused();

        var cut = RenderSurface();

        cut.WaitForAssertion(() => Assert.Equal(1, Reads));
        cut.Find("#moreFiltersToggle").Click();
        Assert.Equal(1, Reads);
    }

    [Fact]
    public async Task ReadsRefused_ApplyDeliversTheAppliedSelection_ToTheHost()
    {
        WithReadsRefused();
        var cut = RenderSurface();
        cut.WaitForAssertion(() => Assert.Equal(1, Reads));

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        var applied = Assert.Single(_committed);
        Assert.Equal(0.05, applied.ErrorMin);
        Assert.Equal(applied, _reports[^1]);
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // ── Reads succeed, writes refused ───────────────────────────────────────

    [Fact]
    public async Task WritesRefused_TheRestoredSelectionIsOnScreen_AndApplyDeliversToTheHost()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        WithWritesRefused();
        var cut = RenderSurface();
        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));

        ErrorMin(cut).Input("0.2");
        // Unguarded, the refused write throws out of the commit between
        // assigning the committed config and raising the events.
        await Apply(cut).ClickAsync(new());

        var applied = Assert.Single(_committed);
        Assert.Equal(0.2, applied.ErrorMin);
        Assert.Equal(applied, _reports[^1]);
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // A refused write keeps the in-memory state it was recording: the row the
    // user opened stays open for this mount. (Its retention promise is the
    // mount's, like the key's own: a remount restores from storage.)
    [Fact]
    public void WritesRefused_AToggledRowStaysOpen_ForTheMount()
    {
        WithWritesRefused();
        var cut = RenderSurface();

        cut.Find("#moreFiltersToggle").Click();

        cut.WaitForAssertion(() => Assert.Equal(
            "true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
        cut.Find("#facetToggle_DiceRolls").Click();
        Assert.Equal("true", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
    }

    // ── The retention promise across a same-source remount ──────────────────
    //
    // The applied holder outlives the mount and is the owner of the applied
    // selection. After an Apply whose write was refused, storage holds the
    // previous selection (writes refused) or nothing (reads refused); a
    // remount that hydrated from storage would show one selection while the
    // holder — and the host's gate — said another was applied. The resume
    // comes from the holder, buffers and committed config both, silently.

    [Fact]
    public async Task WritesRefused_SameSourceRemountAfterApply_RetainsTheAppliedSelection_NotTheStaleStoredOne()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        WithWritesRefused();
        var first = RenderSurface();
        first.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(first).GetAttribute("value")));
        ErrorMin(first).Input("0.2");
        await Apply(first).ClickAsync(new());
        var applied = Assert.Single(_committed);
        var reportsBeforeRemount = _reports.Count;

        var second = RenderSurface();

        // The stored 0.1 is what a storage-hydrated remount would show.
        second.WaitForAssertion(() => Assert.Equal("0.2", ErrorMin(second).GetAttribute("value")));
        Assert.True(Apply(second).HasAttribute("disabled"));
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
        // Silent: a resume derives from the holder, which already agrees.
        Assert.Equal(reportsBeforeRemount, _reports.Count);
        Assert.Empty(second.FindAll("#filterRestoredNotice"));
    }

    [Fact]
    public async Task ReadsRefused_SameSourceRemountAfterApply_RetainsTheAppliedSelection_NotTheDefaults()
    {
        WithReadsRefused();
        var first = RenderSurface();
        first.WaitForAssertion(() => Assert.Equal(1, Reads));
        ErrorMin(first).Input("0.05");
        await Apply(first).ClickAsync(new());
        var applied = Assert.Single(_committed);
        var reportsBeforeRemount = _reports.Count;

        var second = RenderSurface();

        second.WaitForAssertion(() => Assert.Equal("0.05", ErrorMin(second).GetAttribute("value")));
        Assert.True(Apply(second).HasAttribute("disabled"));
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
        Assert.Equal(reportsBeforeRemount, _reports.Count);
    }

    // The control for the remount pins: with storage working — the write
    // landed and the next read returns it — the same remount lands on the
    // same answer by either route, since the resume and the restore agree.
    // So the pins above are about the disagreeing case, and this one passes
    // against the unguarded component too.
    [Fact]
    public async Task StorageWorking_SameSourceRemountAfterApply_RetainsTheAppliedSelection()
    {
        var first = RenderSurface();
        ErrorMin(first).Input("0.05");
        await Apply(first).ClickAsync(new());
        var applied = Assert.Single(_committed);
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == FilterPanel.ConfigKey).Arguments[1] as string;
        Assert.NotNull(stored);
        JSInterop.Setup<string?>("localStorage.getItem", FilterPanel.ConfigKey).SetResult(stored);

        var second = RenderSurface();

        second.WaitForAssertion(() => Assert.Equal("0.05", ErrorMin(second).GetAttribute("value")));
        Assert.True(Apply(second).HasAttribute("disabled"));
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
        Assert.Equal(0, _storageUnavailableReports);
    }

    // ── What the host is told ───────────────────────────────────────────────
    //
    // The panel discovers the condition; the host owns the page's notice
    // about it (SPEC-notices.md: a condition notice, dismissible per
    // occurrence — the host's leg, halheinrich/backgammon#102). So the
    // composite re-exposes the panel's report as-is: once per mount, at the
    // first refused call, with no payload.

    [Fact]
    public void ReadsRefused_TheFactReachesTheHostsBinding_OnceForTheMount()
    {
        WithReadsRefused();

        var cut = RenderSurface();

        cut.WaitForAssertion(() => Assert.Equal(1, _storageUnavailableReports));
        // The latch holds the count: a later write is not attempted either.
        cut.Find("#moreFiltersToggle").Click();
        Assert.Equal(1, _storageUnavailableReports);
    }

    [Fact]
    public async Task WritesRefused_TheFactReachesTheHostsBinding_AtTheFirstRefusedWrite()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        WithWritesRefused();
        var cut = RenderSurface();
        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));

        // Reads succeeded, so the page booted clean and nothing was said.
        Assert.Equal(0, _storageUnavailableReports);

        ErrorMin(cut).Input("0.2");
        await Apply(cut).ClickAsync(new());

        Assert.Equal(1, _storageUnavailableReports);
    }

    [Fact]
    public async Task StorageWorking_NothingIsReported()
    {
        // The control: Loose mode answers every read with null — nothing
        // stored, which is not a failure — and swallows every write.
        var cut = RenderSurface();
        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        Assert.Equal(0, _storageUnavailableReports);
    }

    // ── The failed-restore notice outlives a refused write ──────────────────
    //
    // The notice claims the stored document could not be read, and it ends at
    // a commit only because the commit replaces that document. A commit whose
    // write the browser refused replaces nothing, so the notice must stand —
    // beside whatever the host says about storage — while the applied
    // selection is still committed and delivered (Hal's ruling, 2026-10-07).
    // The control is FilterSurfaceTests'
    // RestoreFailedNotice_SurvivesAnEdit_AndEndsAtACommit, where the write
    // lands and the notice ends.

    [Fact]
    public async Task WritesRefused_ApplyCommits_ButTheFailedRestoreNoticeStays_AcrossARemount()
    {
        JSInterop.Setup<string?>("localStorage.getItem", FilterPanel.ConfigKey)
                 .SetResult("}{ not a config");
        WithWritesRefused();
        var first = RenderSurface();
        first.WaitForAssertion(() => first.Find("#filterRestoreFailedNotice"));

        ErrorMin(first).Input("0.05");
        await Apply(first).ClickAsync(new());

        // Committed and delivered, exactly as a landed write would be.
        var applied = Assert.Single(_committed);
        Assert.Equal(applied, _reports[^1]);
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
        // But the unreadable document was never replaced, so the notice stands.
        Assert.NotNull(first.Find("#filterRestoreFailedNotice"));
        Assert.True(_notice.IsFailureVisible);

        var second = RenderSurface();

        second.WaitForAssertion(() => Assert.Equal("0.05", ErrorMin(second).GetAttribute("value")));
        Assert.True(Apply(second).HasAttribute("disabled"));
        Assert.NotNull(second.Find("#filterRestoreFailedNotice"));
    }

    // The parameter is optional by design: both hosts bind it in their own
    // legs, and until then — or in a host that never does — the panel
    // degrades exactly the same way and the host simply is not told.
    [Fact]
    public async Task HostBindingNothingNew_StillGetsTheAppliedSelection_WhenStorageIsRefused()
    {
        WithReadsRefused();
        WithWritesRefused();
        var cut = RenderSurfaceBindingNothingNew();
        cut.WaitForAssertion(() => Assert.Equal(1, Reads));

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        var applied = Assert.Single(_committed);
        Assert.Equal(applied, _reports[^1]);
        Assert.Equal(applied, _holder.ConfigFor(TokenA));
    }
}
