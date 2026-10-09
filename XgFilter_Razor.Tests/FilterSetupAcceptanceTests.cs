using AngleSharp.Dom;
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
/// The acceptance cases <c>SPEC-filtering.md</c> §4 lists for the setup-state
/// owner (halheinrich/backgammon#374), each through the composite a host
/// embeds, with the host's side played as a host plays it: report the source,
/// read the gate from the owner's snapshot. Storage is planned strictly, and
/// the pending and ordering cases hold the calls they are about; every wait on
/// a held call's continuation is on the operation itself, bounded by the
/// test's normal timeout.
/// </summary>
public class FilterSetupAcceptanceTests : BunitContext
{
    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromPath(@"D:\Matches\A");
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromPath(@"D:\Matches\B");

    private static readonly FilterConfig SelectionA = new() { ErrorMin = 0.1 };
    private static readonly FilterConfig SelectionB = new() { ErrorMin = 0.2 };

    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterSetupAcceptanceTests()
    {
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    private IRenderedComponent<FilterSurface> Mount() => Render<FilterSurface>();

    // The host latching a source — on the renderer's context, as a host's own
    // handler would be, while a page is mounted; directly when none is.
    private async Task HostReportsAsync(IRenderedComponent<FilterSurface>? cut, FilterSourceToken? source)
    {
        if (cut is null)
        {
            Setup.ReportSource(source);
            return;
        }

        await cut.InvokeAsync(() => Setup.ReportSource(source));
    }

    // Navigating away: the page and everything on it goes.
    private Task NavigateAwayAsync() => DisposeComponentsAsync();

    private static IElement Apply(IRenderedComponent<FilterSurface> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Apply Filter"));

    private static IElement ErrorMin(IRenderedComponent<FilterSurface> cut) => cut.Find("#errorMin");

    private static Task ClickApplyAsync(IRenderedComponent<FilterSurface> cut) =>
        Apply(cut).ClickAsync(new MouseEventArgs());

    // ── Edit, navigate, return, undo ───────────────────────────────────────

    // The draft outlives the component (§1, "Navigating away and back"): an
    // unapplied edit is still on screen after the return, still not in
    // effect, and undoing it back to the applied values puts the applied
    // filter back in effect — the comparison against the baseline, which
    // also outlived the page.
    [Fact]
    public async Task EditNavigateReturnUndo_KeepsTheDraft_AndUndoingRestoresTheInEffectFilter()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount(times: 2);
        _storage.ExpectFilterCommit(SelectionA, BrowserStorageWriteAnswer.Succeeded);
        Setup.ReportSource(TokenA);
        var page = Mount();
        ErrorMin(page).Input("0.1");
        await ClickApplyAsync(page);
        ErrorMin(page).Input("0.2");
        Assert.False(Setup.Current.IsInEffectFor(TokenA));

        await NavigateAwayAsync();
        var back = Mount();

        Assert.Equal("0.2", ErrorMin(back).GetAttribute("value"));
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Apply(back).HasAttribute("disabled"));

        ErrorMin(back).Input("0.1");

        Assert.Equal(SelectionA, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.True(Apply(back).HasAttribute("disabled"));
        Assert.Contains("already applied", back.Find("#applyDisabledReason").TextContent);
        _storage.Verify();
    }

    // ── A suspended Apply across a source replacement ──────────────────────

    public static TheoryData<bool> WriteAnswers => new() { true, false };

    // The commit is in memory before its write begins, so it is in effect at
    // once; the host replaces the source while the write is held; when the
    // write completes — landing or refused — the new setup is exactly as the
    // replacement left it: no baseline, nothing in effect for either source.
    [Theory]
    [MemberData(nameof(WriteAnswers))]
    public async Task ASuspendedApply_AcrossASourceReplacement_ChangesNothingInTheNewSetup(bool lands)
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        var write = _storage.ExpectHeldFilterCommit(SelectionA);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");

        var apply = ClickApplyAsync(cut);
        Assert.True(write.IsReached);
        Assert.True(Setup.Current.IsInEffectFor(TokenA));

        await HostReportsAsync(cut, TokenB);
        var replaced = Setup.Current;
        Assert.False(replaced.IsInEffectFor(TokenB));

        write.Release(lands ? BrowserStorageWriteAnswer.Succeeded : BrowserStorageWriteAnswer.Refused);
        await apply.WaitAsync(DefaultWaitTimeout);

        Assert.Same(replaced, Setup.Current);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value"));
        Assert.Equal(lands ? 0 : 1, _refusals.Refusals.Count);
        _storage.Verify();
    }

    // A → B → A is two endings (§4): A's consent does not come back with A.
    [Fact]
    public async Task ASuspendedApply_AcrossAThenBThenA_DoesNotBringAsConsentBack()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        var write = _storage.ExpectHeldFilterCommit(SelectionA);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        var apply = ClickApplyAsync(cut);
        var generation = Setup.Current.Generation;

        await HostReportsAsync(cut, TokenB);
        await HostReportsAsync(cut, TokenA);
        write.Release(BrowserStorageWriteAnswer.Succeeded);
        await apply.WaitAsync(DefaultWaitTimeout);

        Assert.Equal(generation + 2, Setup.Current.Generation);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        _storage.Verify();
    }

    // The same with the surface gone meanwhile — a host that unmounts the
    // filter while no source is held reports each source when it latches it,
    // and nothing that completes off screen brings A's consent back.
    [Fact]
    public async Task ASuspendedApply_AcrossAThenBThenA_WhileTheSurfaceIsNotMounted()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount(times: 2);
        var write = _storage.ExpectHeldFilterCommit(SelectionA);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        var apply = ClickApplyAsync(cut);
        var generation = Setup.Current.Generation;

        await NavigateAwayAsync();
        await HostReportsAsync(null, TokenB);
        await HostReportsAsync(null, TokenA);
        write.Release(BrowserStorageWriteAnswer.Succeeded);
        await apply.WaitAsync(DefaultWaitTimeout);
        var back = Mount();

        Assert.Equal(generation + 2, Setup.Current.Generation);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Apply(back).HasAttribute("disabled"));
        Assert.Equal("0.1", ErrorMin(back).GetAttribute("value"));
        _storage.Verify();
    }

    // ── An older Apply completing after a newer edit or Apply ──────────────

    // Two commits in one setup, their writes completing newest first: the
    // newer commit stays the baseline, and the older one's late completion
    // restores nothing.
    [Fact]
    public async Task AnOlderApply_CompletingAfterANewerApply_DoesNotRestoreIt()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        var older = _storage.ExpectHeldFilterCommit(SelectionA);
        var newer = _storage.ExpectHeldFilterCommit(SelectionB);
        Setup.ReportSource(TokenA);
        var cut = Mount();

        ErrorMin(cut).Input("0.1");
        var applyA = ClickApplyAsync(cut);
        ErrorMin(cut).Input("0.2");
        var applyB = ClickApplyAsync(cut);
        Assert.True(older.IsReached);
        Assert.True(newer.IsReached);

        newer.Release(BrowserStorageWriteAnswer.Succeeded);
        await applyB.WaitAsync(DefaultWaitTimeout);
        older.Release(BrowserStorageWriteAnswer.Succeeded);
        await applyA.WaitAsync(DefaultWaitTimeout);

        Assert.Equal(SelectionB, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Equal("0.2", ErrorMin(cut).GetAttribute("value"));
        _storage.Verify();
    }

    // An edit after a commit whose write is still held: the late completion
    // puts nothing back — the edited draft stands, not in effect.
    [Fact]
    public async Task AnOlderApply_CompletingAfterANewerEdit_DoesNotRestoreIt()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        var write = _storage.ExpectHeldFilterCommit(SelectionA);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        var apply = ClickApplyAsync(cut);

        ErrorMin(cut).Input("0.2");
        write.Release(BrowserStorageWriteAnswer.Succeeded);
        await apply.WaitAsync(DefaultWaitTimeout);

        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.Equal("0.2", ErrorMin(cut).GetAttribute("value"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        _storage.Verify();
    }

    // ── A restore pending across a source change ───────────────────────────

    // The read is held while the host changes the source and the user edits.
    // Released, the restoration settles — it is not stranded in "pending" —
    // records what it read, and leaves the newer draft standing; and since it
    // did not put the stored selection on screen, it claims nothing about one.
    [Fact]
    public async Task ARestorePendingAcrossASourceChange_Settles_WithoutOverwritingANewerDraft()
    {
        var restore = _storage.ExpectHeldFilterRestore();
        _storage.ExpectFilterPanelMount();
        Setup.ReportSource(TokenA);
        var cut = Mount();
        Assert.True(restore.IsReached);
        Assert.Equal(FilterRestoration.Pending, Setup.Current.Restoration);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.True(cut.Find("#clearFilters").HasAttribute("disabled"));

        await HostReportsAsync(cut, TokenB);
        ErrorMin(cut).Input("0.3");
        restore.Release(FilterSurfaceStorage.RestoreAnswer(SelectionA));
        await Setup.RestoreAsync().WaitAsync(DefaultWaitTimeout);

        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        Assert.Equal("0.3", ErrorMin(cut).GetAttribute("value"));
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        _storage.Verify();
    }

    // The same with no edit meanwhile: the source change kept the draft, so
    // the restored selection lands on it — a choice, not a consent — with the
    // notice saying where it came from.
    [Fact]
    public async Task ARestorePendingAcrossASourceChange_WithNoEditMeanwhile_PutsTheSelectionOnScreen()
    {
        var restore = _storage.ExpectHeldFilterRestore();
        _storage.ExpectFilterPanelMount();
        Setup.ReportSource(TokenA);
        var cut = Mount();

        await HostReportsAsync(cut, TokenB);
        restore.Release(FilterSurfaceStorage.RestoreAnswer(SelectionA));
        await Setup.RestoreAsync().WaitAsync(DefaultWaitTimeout);

        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));
        Assert.NotNull(cut.Find("#filterRestoredNotice"));
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        _storage.Verify();
    }

    // ── Readiness (§1, halheinrich/backgammon#266) ────────────────────────

    // A first visit has chosen nothing: the empty selection is ready without
    // Apply, so the host's gate is open with no gesture and Apply has
    // nothing to do. The host adds no empty-filter exception of its own; it
    // reads the owner.
    [Fact]
    public void AFirstVisit_TheEmptySelectionIsReady_WithoutApply()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        Setup.ReportSource(TokenA);

        var cut = Mount();

        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Contains("no filter is set", cut.Find("#applyDisabledReason").TextContent);
        _storage.Verify();
    }

    // A restored valid empty selection is ready once restoration settles, and
    // raises no "restored, not in effect until Apply" notice: nothing differs
    // from a first visit (§1, Reload).
    [Fact]
    public void ARestoredEmptySelection_IsReady_AndRaisesNoRestoredNotice()
    {
        _storage.ExpectFilterRestore(new FilterConfig());
        _storage.ExpectFilterPanelMount();
        Setup.ReportSource(TokenA);

        var cut = Mount();

        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        _storage.Verify();
    }

    // A property of the selection, not of a gesture: editing an applied
    // filter back to nothing is the empty selection, ready as it stands.
    [Fact]
    public async Task EditingAnAppliedFilterBackToEmpty_IsReady()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterCommit(SelectionA, BrowserStorageWriteAnswer.Succeeded);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        await ClickApplyAsync(cut);

        ErrorMin(cut).Input(string.Empty);

        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        _storage.Verify();
    }

    // A restoration still pending is not evidence that the user chose no
    // filter: Run stays off until it settles, so an unrestricted run is never
    // briefly enabled over a stored filter that has not loaded yet.
    [Fact]
    public async Task APendingRestoration_IsNotReady_UntilItSettles()
    {
        var restore = _storage.ExpectHeldFilterRestore();
        _storage.ExpectFilterPanelMount();
        Setup.ReportSource(TokenA);
        Mount();

        Assert.False(Setup.Current.IsInEffectFor(TokenA));

        restore.Release(FilterSurfaceStorage.RestoreAnswer(FilterRestoration.NothingStored));
        await Setup.RestoreAsync().WaitAsync(DefaultWaitTimeout);

        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        _storage.Verify();
    }

    // ── Recovering from a failed restore ───────────────────────────────────

    public static TheoryData<FilterRestoration, bool> FailedRestoresAndEmptyChoices => new()
    {
        { FilterRestoration.Refused, true },
        { FilterRestoration.Refused, false },
        { FilterRestoration.Unreadable, true },
        { FilterRestoration.Unreadable, false },
    };

    // A failed restore shows the defaults, and the defaults are unresolved:
    // not ready, with Apply on for them. Clear — or a valid Apply of the empty
    // defaults — resolves the choice in memory even though remembering it is
    // refused, and the retained empty draft then stays resolved and ready
    // across a navigation and a source change. The outcome stays what it was:
    // it is the diagnostic, never rewritten to make Run possible.
    [Theory]
    [MemberData(nameof(FailedRestoresAndEmptyChoices))]
    public async Task AFailedRestore_ThenAnEmptyChoice_StaysReady_ThroughARefusedWrite_NavigationAndASourceChange(
        FilterRestoration failure, bool byClear)
    {
        _storage.ExpectFilterRestore(failure);
        _storage.ExpectFilterPanelMount(times: 2);
        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Refused);
        Setup.ReportSource(TokenA);
        var cut = Mount();

        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Apply(cut).HasAttribute("disabled"));

        if (byClear)
            await cut.Find("#clearFilters").ClickAsync(new MouseEventArgs());
        else
            await ClickApplyAsync(cut);

        Assert.True(Setup.Current.IsInEffectFor(TokenA));

        await NavigateAwayAsync();
        var back = Mount();
        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        Assert.True(Apply(back).HasAttribute("disabled"));

        await HostReportsAsync(back, TokenB);

        Assert.True(Setup.Current.IsInEffectFor(TokenB));
        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(TokenB));
        Assert.Equal(failure, Setup.Current.Restoration);
        _storage.Verify();
    }

    public static TheoryData<FilterRestoration> FailedRestores => new()
    {
        FilterRestoration.Refused,
        FilterRestoration.Unreadable,
    };

    // The same with a non-empty choice: resolution and the draft survive the
    // refused write and the navigation, but a non-empty choice is consent,
    // and the source change drops it — Apply is required again for the new
    // source. Resolution is still there to find: edited back to empty, the
    // draft is ready at once.
    [Theory]
    [MemberData(nameof(FailedRestores))]
    public async Task AFailedRestore_ThenANonEmptyApply_SurvivesNavigation_ButTheSourceChangeRequiresApplyAgain(
        FilterRestoration failure)
    {
        _storage.ExpectFilterRestore(failure);
        _storage.ExpectFilterPanelMount(times: 2);
        _storage.ExpectFilterCommit(SelectionA, BrowserStorageWriteAnswer.Refused);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        await ClickApplyAsync(cut);
        Assert.Equal(SelectionA, Setup.Current.ConfigInEffectFor(TokenA));

        await NavigateAwayAsync();
        var back = Mount();
        Assert.Equal("0.1", ErrorMin(back).GetAttribute("value"));
        Assert.Equal(SelectionA, Setup.Current.ConfigInEffectFor(TokenA));

        await HostReportsAsync(back, TokenB);

        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        Assert.False(Apply(back).HasAttribute("disabled"));
        Assert.Equal("0.1", ErrorMin(back).GetAttribute("value"));

        ErrorMin(back).Input(string.Empty);

        Assert.True(Setup.Current.IsInEffectFor(TokenB));
        _storage.Verify();
    }

    // Clear commits the empty selection as this setup's baseline (§4's
    // transitions table), so a non-empty applied filter does not survive it:
    // editing back to it after a Clear is a selection nobody applied since,
    // and Apply is required.
    [Fact]
    public async Task ApplyA_ThenClear_ThenEditBackToA_ApplyIsRequired()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterCommit(SelectionA, BrowserStorageWriteAnswer.Succeeded);
        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Succeeded);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        await ClickApplyAsync(cut);

        await cut.Find("#clearFilters").ClickAsync(new MouseEventArgs());
        ErrorMin(cut).Input("0.1");

        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        _storage.Verify();
    }

    // ── Mutation through a retained config reference ───────────────────────

    // Nothing a consumer holds reaches the owner: not the config it read as
    // in effect, not the config it handed in to be staged, not a snapshot it
    // kept.
    [Fact]
    public async Task MutatingARetainedConfig_ChangesNothingInTheOwner()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount();
        _storage.ExpectFilterCommit(SelectionA, BrowserStorageWriteAnswer.Succeeded);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        ErrorMin(cut).Input("0.1");
        await ClickApplyAsync(cut);
        var kept = Setup.Current;

        var read = Setup.Current.ConfigInEffectFor(TokenA)!;
        read.ErrorMin = 0.9;
        read.Players.Add("Intruder");

        Assert.Equal(SelectionA, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value"));

        var staged = new FilterConfig { ErrorMin = 0.4 };
        await cut.InvokeAsync(() => Setup.Stage(staged));
        staged.ErrorMin = 0.9;
        staged.Players.Add("Intruder");

        Assert.Equal("0.4", Setup.Current.Draft.ErrorMinText);
        Assert.Equal(string.Empty, Setup.Current.Draft.PlayersText);
        Assert.Equal("0.1", kept.Draft.ErrorMinText);
        Assert.True(kept.IsInEffectFor(TokenA));
        _storage.Verify();
    }

    // ── An invalid draft through edit, unmount, remount and correction ────

    public static TheoryData<FilterFacet, string, string, string> InvalidInputs => new()
    {
        // A numeric box its config field cannot represent: a move number is whole.
        { FilterFacet.MoveNumberRange, "#moveNumberMin", "1.5", "2" },
        // Pattern text the grammar refuses.
        { FilterFacet.PositionPattern, "#positionPattern", "[6,2", "[6,2,]" },
    };

    // The retained draft is the editor's state, not its config: the invalid
    // input is back on screen after the remount, still marked, still not a
    // selection anyone can apply or run — and correcting it makes it one.
    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task AnInvalidDraft_SurvivesUnmountAndRemount_AndCorrectingItMakesItValid(
        FilterFacet facet, string box, string invalid, string corrected)
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount(times: 2);
        _storage.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded, times: 2);
        _storage.ExpectFilterRowsToggle([facet], BrowserStorageWriteAnswer.Succeeded, times: 2);
        Setup.ReportSource(TokenA);
        var cut = Mount();
        OpenRow(cut, facet);
        cut.Find(box).Input(invalid);
        cut.Find(box).Blur();
        Assert.True(Apply(cut).HasAttribute("disabled"));

        await NavigateAwayAsync();
        var back = Mount();
        OpenRow(back, facet);

        Assert.Equal(invalid, back.Find(box).GetAttribute("value"));
        Assert.Contains("is-invalid", back.Find(box).GetAttribute("class"));
        Assert.True(Apply(back).HasAttribute("disabled"));
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.TryGetSavable(out _));

        back.Find(box).Input(corrected);
        back.Find(box).Blur();

        Assert.DoesNotContain("is-invalid", back.Find(box).GetAttribute("class"));
        Assert.False(Apply(back).HasAttribute("disabled"));
        _storage.Verify();
    }

    // ── Unfinished numeric input (halheinrich/backgammon#379) ─────────────

    public static TheoryData<FilterFacet?, string, string, string> UnfinishedBounds => new()
    {
        { null, "#errorMin", "1e-", "1e-3" },
        { null, "#errorMax", "-", "0,5" },
        { FilterFacet.MoveNumberRange, "#moveNumberMin", "+", "3,0" },
        { FilterFacet.MoveNumberRange, "#moveNumberMax", ".", "12" },
    };

    // A bound on its way to being a number is the user's unfinished text, not
    // a blank box: it closes the host's gate the moment it is typed, where a
    // blank bound would leave the empty selection in effect; it is back on
    // screen as typed after a remount, marked and unsavable; and correcting
    // it — with either decimal mark — makes it a selection, its text still
    // the user's. The error range is always on screen; a move number's row is
    // opened first.
    [Theory]
    [MemberData(nameof(UnfinishedBounds))]
    public async Task AnUnfinishedBound_IsNotBlank_AndSurvivesUnmountRemountAndCorrection(
        FilterFacet? row, string box, string unfinished, string corrected)
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        _storage.ExpectFilterPanelMount(times: 2);
        if (row is { } rowFacet)
        {
            _storage.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded, times: 2);
            _storage.ExpectFilterRowsToggle([rowFacet], BrowserStorageWriteAnswer.Succeeded, times: 2);
        }
        Setup.ReportSource(TokenA);
        var cut = Mount();
        if (row is { } first) OpenRow(cut, first);
        Assert.True(Setup.Current.IsInEffectFor(TokenA));

        cut.Find(box).Input(unfinished);

        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.True(Apply(cut).HasAttribute("disabled"));

        await NavigateAwayAsync();
        var back = Mount();
        if (row is { } again) OpenRow(back, again);

        Assert.Equal(unfinished, back.Find(box).GetAttribute("value"));
        Assert.Contains("is-invalid", back.Find(box).GetAttribute("class"));
        Assert.True(Apply(back).HasAttribute("disabled"));
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.TryGetSavable(out _));

        back.Find(box).Input(corrected);

        Assert.Equal(corrected, back.Find(box).GetAttribute("value"));
        Assert.DoesNotContain("is-invalid", back.Find(box).GetAttribute("class"));
        Assert.False(Apply(back).HasAttribute("disabled"));
        Assert.True(Setup.Current.TryGetSavable(out _));
        _storage.Verify();
    }

    // Open the container and one row through their real toggles, waiting on
    // each to land.
    private static void OpenRow(IRenderedComponent<FilterSurface> cut, FilterFacet facet)
    {
        cut.Find("#moreFiltersToggle").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
        cut.Find($"#facetToggle_{facet}").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find($"#facetToggle_{facet}").GetAttribute("aria-expanded")));
    }
}
