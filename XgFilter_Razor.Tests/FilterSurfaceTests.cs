using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.Components.Internal;

namespace XgFilter_Razor.Tests;

public class FilterSurfaceTests : BunitContext
{
    public FilterSurfaceTests()
    {
        // Loose mode — the inner FilterPanel's OnAfterRenderAsync issues
        // localStorage.getItem calls; default (null) means "no persisted state".
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromGeneration(2);

    // Host-side captures: the holder and restore notice the composite
    // mediates (both app-scoped in a real host — one instance across every
    // render in a test, like one instance across every mount in an app), and
    // the two re-raised event channels (lists — per-gesture counts are part
    // of the contract).
    private readonly AppliedFilter _holder = new();
    private readonly FilterRestoreNotice _notice = new();
    private readonly List<FilterConfig> _committed = [];
    private readonly List<FilterConfig?> _reports = [];

    private IRenderedComponent<FilterSurface> RenderSurface(
        FilterSourceToken? source,
        IFilterDocumentStorage? storage,
        bool canPersist = true,
        string? persistDisabledReason = null)
        => Render<FilterSurface>(parameters => parameters
            .Add(p => p.AppliedFilter, _holder)
            .Add(p => p.RestoreNotice, _notice)
            .Add(p => p.Source, source)
            .Add(p => p.Storage, storage)
            .Add(p => p.CanPersist, canPersist)
            .Add(p => p.PersistDisabledReason, persistDisabledReason)
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => _committed.Add(c))
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => _reports.Add(c)));

    private static string CollectionJson(params (string Name, FilterConfig Config)[] entries)
    {
        var filters = NamedFilterCollection.Empty;
        foreach (var (name, config) in entries)
            filters = filters.With(name, config);
        return filters.ToJson();
    }

    // Stand the inner panel's persisted selection up, so a mount restores it —
    // the navigate-back starting state, where localStorage still holds what
    // the previous mount applied. Through FilterPanel.ConfigKey rather than a
    // repeated literal: the key is incidental plumbing here, not the subject.
    private void StoredConfig(FilterConfig config) =>
        JSInterop.Setup<string?>("localStorage.getItem", FilterPanel.ConfigKey)
                 .SetResult(config.ToJson());

    private static FakeFilterDocumentStorage StorageWith(params (string Name, FilterConfig Config)[] entries)
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = CollectionJson(entries);
        return storage;
    }

    // ── Gesture helpers (the SavedFiltersPanelTests idioms, over the surface) ─

    private static IElement Apply(IRenderedComponent<FilterSurface> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Apply Filter"));

    private static IElement ErrorMin(IRenderedComponent<FilterSurface> cut) =>
        cut.Find("#errorMin");

    private static IElement? FindRowButton(
        IRenderedComponent<FilterSurface> cut, string name, string buttonText)
    {
        var row = cut.FindAll("li.list-group-item")
            .Single(li => li.QuerySelector("span")?.TextContent == name);
        return row.QuerySelectorAll("button")
            .SingleOrDefault(b => b.TextContent.Trim() == buttonText);
    }

    private static async Task ClickRowButtonAsync(
        IRenderedComponent<FilterSurface> cut, string name, string buttonText)
    {
        var button = FindRowButton(cut, name, buttonText);
        Assert.NotNull(button);
        await button.ClickAsync(new());
    }

    // ── Mount ───────────────────────────────────────────────────────────────

    [Fact]
    public void Mount_WithStorage_LoadsContext_AndRendersSavedPanel()
    {
        var storage = StorageWith(("Race", new FilterConfig()));

        var cut = RenderSurface(TokenA, storage);

        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
        Assert.Contains(SavedFiltersDocument.FileName, storage.Reads);
    }

    [Fact]
    public void Mount_NullStorage_NoSavedSection_FilterPanelStillRenders()
    {
        var cut = RenderSurface(TokenA, storage: null);

        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Contains("Apply Filter", cut.Markup);
    }

    // The first-mount pin: mounting over an unchanged source initializes the
    // comparison token and loads the context — nothing else. A holder already
    // applied (navigate-back: the holder outlived the previous mount) stays
    // applied, and mount alone re-raises no applied-state report that could
    // move a host gate.
    [Fact]
    public void Mount_OverSameSource_LeavesAppliedHolderUntouched_RaisesNothing()
    {
        var appliedConfig = new FilterConfig { ErrorMin = 0.1 };
        _holder.Set(appliedConfig, TokenA);

        RenderSurface(TokenA, StorageWith(("Race", new FilterConfig())));

        Assert.Same(appliedConfig, _holder.ConfigFor(TokenA));
        Assert.Empty(_reports);
        Assert.Empty(_committed);
    }

    // ── The first-mount reconcile (#82) ─────────────────────────────────────

    // The navigate-back case the reconcile exists for: the holder outlived the
    // previous mount still carrying the config applied for this source, and
    // the panel restores exactly that selection from storage. The
    // fresh panel has committed nothing of its own, so without the reconcile
    // Apply re-arms with nothing to do. Seeding is silent — a reconcile
    // derives from the holder, which already agrees, so there is no news; the
    // mount pin above still holds.
    [Fact]
    public void Mount_HolderAppliedForThisSource_SeedsCommitted_ApplyDisabled_RaisesNothing()
    {
        // Navigate-back after Apply: the holder still carries the applied
        // config, and the Apply that set it also spent the restore notice —
        // an applied holder and a pending notice cannot coexist in a real
        // boot, so the simulated history says so.
        var applied = new FilterConfig { ErrorMin = 0.1 };
        _holder.Set(applied, TokenA);
        _notice.Dismiss();
        StoredConfig(applied);

        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        cut.WaitForAssertion(() => Assert.True(Apply(cut).HasAttribute("disabled")));
        Assert.Contains("already applied", cut.Find("#applyDisabledReason").TextContent);
        Assert.Empty(_reports);
        Assert.Empty(_committed);
        Assert.Same(applied, _holder.ConfigFor(TokenA));
    }

    // The fresh-mount posture the reconcile must not eat, and the test that
    // fails if anyone ever seeds from localStorage instead of the holder: a
    // full browser reload keeps the stored selection but resets the holder, so
    // there IS something to do — applying re-opens the host's gate. Disabling
    // Apply here would be a lock-out.
    [Fact]
    public void Mount_EmptyHolder_WithRestorableStorage_LeavesApplyEnabled()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });

        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        Assert.Null(_holder.ConfigFor(TokenA));
        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The keyed lookup answers source-relatively: a holder carrying a config
    // applied against another source has said nothing about this one, so the
    // reconcile leaves Apply armed.
    [Fact]
    public void Mount_HolderKeyedToAnotherSource_DoesNotSeed_ApplyStaysEnabled()
    {
        var applied = new FilterConfig { ErrorMin = 0.1 };
        _holder.Set(applied, TokenB);
        StoredConfig(applied);

        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // ── The restored-selection notice (§4) ──────────────────────────────────
    //
    // A reload ends the setup: selections restored, applied-ness dropped,
    // Apply re-armed — correct by rule, indistinguishable from a bug unless
    // the screen says so. The notice's app-scoped state (one _notice per
    // test class, like one per app boot) is what distinguishes a fresh boot
    // from a remount within a setup; these pins drive both sides of that
    // line and the notice's death at the first owning gesture.

    [Fact]
    public void FreshBoot_RestoredSelection_ShowsTheNotice_ApplyStaysArmed()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });

        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        cut.WaitForAssertion(() => Assert.Contains(
            "previous session", cut.Find("#filterRestoredNotice").TextContent));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Mount_NothingStored_NoNotice()
    {
        // Nothing in storage (the loose JS default): nothing was restored,
        // so the notice must not claim otherwise over a defaults screen.
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#filterRestoredNotice")));
    }

    [Fact]
    public void Notice_DiesAtTheFirstEdit()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        cut.WaitForAssertion(() => cut.Find("#filterRestoredNotice"));

        ErrorMin(cut).Input("0.2");

        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
    }

    [Fact]
    public async Task Notice_DiesAtApply()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        cut.WaitForAssertion(() => cut.Find("#filterRestoredNotice"));

        await Apply(cut).ClickAsync(new());

        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
    }

    [Fact]
    public void Notice_SurvivesTheDisclosureToggle()
    {
        // Toggling the disclosure is navigation, not an edit — the restored
        // selection is still not the user's own, so the notice holds.
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        cut.WaitForAssertion(() => cut.Find("#filterRestoredNotice"));

        cut.Find("#moreFiltersToggle").Click();

        Assert.NotNull(cut.Find("#filterRestoredNotice"));
    }

    // The trap the app-scoped state exists for: navigate-back with unapplied
    // edits looks exactly like a fresh boot at mount time — same restore,
    // same empty holder — and §1 rules that navigation changes nothing,
    // including no new notice. The edit spent this boot's notice, so the
    // remount's restore must not resurrect it.
    [Fact]
    public void Remount_WithinSetup_AfterAnEdit_DoesNotResurrectTheNotice()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        var first = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        first.WaitForAssertion(() => first.Find("#filterRestoredNotice"));
        ErrorMin(first).Input("0.2");

        var second = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        second.WaitForAssertion(() =>
            Assert.Equal("0.1", ErrorMin(second).GetAttribute("value")));
        Assert.Empty(second.FindAll("#filterRestoredNotice"));
    }

    // The other side of that line: navigation changes nothing, so a remount
    // over a still-untouched restored selection re-shows the same notice.
    [Fact]
    public void Remount_WithinSetup_Untouched_KeepsTheNotice()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        var first = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        first.WaitForAssertion(() => first.Find("#filterRestoredNotice"));

        var second = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        second.WaitForAssertion(() => second.Find("#filterRestoredNotice"));
    }

    // A source change is choreography, not a user gesture: ForgetCommitted
    // re-arms Apply without dismissing the notice, whose statement — these
    // selections came from a previous session and are not in effect — is
    // still true against the new source. (A gated host's source change
    // crosses an unmount and runs no panel code at all; the in-place rule
    // must not treat the notice differently.)
    [Fact]
    public void Notice_SurvivesAnInPlaceSourceChange()
    {
        StoredConfig(new FilterConfig { ErrorMin = 0.1 });
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        cut.WaitForAssertion(() => cut.Find("#filterRestoredNotice"));

        cut.Render(parameters => parameters.Add(p => p.Source, TokenB));

        Assert.NotNull(cut.Find("#filterRestoredNotice"));
        Assert.Null(_reports[^1]);
    }

    // ── Applied-state mediation ─────────────────────────────────────────────

    [Fact]
    public async Task Apply_KeysHolderToSource_AndRaisesBothEvents()
    {
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        var committed = Assert.Single(_committed);
        Assert.Same(committed, _holder.ConfigFor(TokenA));
        Assert.Null(_holder.ConfigFor(TokenB));
        // The edit's null report, then the commit's clean re-affirm.
        Assert.Equal(2, _reports.Count);
        Assert.Null(_reports[0]);
        Assert.Same(committed, _reports[1]);
    }

    // An edit drops the applied state entirely — nothing survives it for any
    // source (halheinrich/backgammon#92, spec §3: only present ownership
    // exists; no behaviour may answer from filter history).
    [Fact]
    public async Task EditAfterApply_DropsTheAppliedState_ReportsNull()
    {
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        await Apply(cut).ClickAsync(new());
        Assert.NotNull(_holder.ConfigFor(TokenA));

        ErrorMin(cut).Input("0.05");

        Assert.Null(_holder.ConfigFor(TokenA));
        Assert.Null(_reports[^1]);
    }

    [Fact]
    public async Task Apply_WithNoSource_LeavesHolderUntouched_StillRaisesEvents()
    {
        var cut = RenderSurface(source: null, storage: null);

        await Apply(cut).ClickAsync(new());

        Assert.Null(_holder.ConfigFor(TokenA));
        Assert.Single(_committed);
        Assert.Single(_reports);
    }

    // ── The source-change rule ──────────────────────────────────────────────

    [Fact]
    public async Task SourceChange_EndsSetup_ReArmsApply_ReloadsContext()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(TokenA, storage);
        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));
        var readsBefore = storage.Reads.Count;

        cut.Render(parameters => parameters.Add(p => p.Source, TokenB));

        // The setup ended: the applied state dropped entirely — nothing stays
        // in force for the old source or the new one.
        Assert.Null(_holder.ConfigFor(TokenA));
        Assert.Null(_holder.ConfigFor(TokenB));
        // Apply re-armed on the still-mounted panel, and the host was told
        // through the normal path.
        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Null(_reports[^1]);
        // The saved-filters context reloaded through the seam.
        Assert.True(storage.Reads.Count > readsBefore);
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
    }

    [Fact]
    public void SourceChangeToNull_ResetsContext_HidesSavedSection()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(TokenA, storage);
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));

        cut.Render(parameters => parameters.Add(p => p.Source, (FilterSourceToken?)null));

        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
    }

    // ── Saved-filters wiring ────────────────────────────────────────────────

    [Fact]
    public async Task Load_StagesTheSavedConfig_WithoutCommitting()
    {
        var cut = RenderSurface(
            TokenA, StorageWith(("Race", new FilterConfig { ErrorMin = 0.25 })));

        await ClickRowButtonAsync(cut, "Race", "Load");

        Assert.Equal("0.25", ErrorMin(cut).GetAttribute("value"));
        Assert.Empty(_committed);                 // staged, not committed
        Assert.Null(_holder.ConfigFor(TokenA));   // the holder only moves on commit
        Assert.Null(_reports[^1]);                // staging reported as uncommitted
    }

    [Fact]
    public async Task RowSave_SnapshotsLiveBuffers_WritesCanonicalThroughSeam()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(TokenA, storage);

        ErrorMin(cut).Input("0.5");
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        var write = Assert.Single(storage.Writes);
        Assert.Equal(SavedFiltersDocument.FileName, write.FileName);
        Assert.True(NamedFilterCollection.TryFromJson(write.Json, out var written));
        Assert.True(written.TryGetConfig("Race", out var saved));
        Assert.Equal(0.5, saved!.ErrorMin); // the unapplied edit rode along
    }

    [Fact]
    public async Task SaveAs_NewName_WritesThroughSeam()
    {
        var storage = new FakeFilterDocumentStorage();
        var cut = RenderSurface(TokenA, storage);

        cut.Find("#saveFilterName").Input("Blitz");
        await cut.Find("#saveFilterButton").ClickAsync(new());

        var write = Assert.Single(storage.Writes);
        Assert.Equal(SavedFiltersDocument.FileName, write.FileName);
        Assert.True(NamedFilterCollection.TryFromJson(write.Json, out var written));
        Assert.True(written.Contains("Blitz"));
    }

    [Fact]
    public async Task Save_UnparseablePattern_RefusalNotice_NoWrite_ClearedByNextGesture()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(TokenA, storage);

        // Stage an unparseable position pattern (behind the disclosure), then
        // attempt a row Save: TryGetEditedConfig refuses, so the composite
        // must say why instead of no-opping silently.
        cut.Find("#moreFiltersToggle").Click();
        cut.Find("#positionPattern").Input("not a bracket list");
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        Assert.Contains("can't be saved", cut.Find("#filterSaveError").TextContent);
        Assert.Empty(storage.Writes);

        // Any panel gesture moots the refusal — fixing the pattern is one.
        cut.Find("#positionPattern").Input(string.Empty);

        Assert.Empty(cut.FindAll("#filterSaveError"));
    }

    // The refusal is the panel's validity gate, not one particular rule of it:
    // an error bound the lib rules invalid refuses the snapshot exactly as an
    // unparseable pattern does, and the composite says so with the same
    // field-agnostic copy — the offending value is already marked, with its own
    // explanation, in the panel below.
    [Fact]
    public async Task Save_InvalidErrorBound_RefusalNotice_NoWrite()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(TokenA, storage);

        // Min above Max — always-visible facet, so no disclosure gesture needed.
        cut.Find("#errorMin").Input("5");
        cut.Find("#errorMax").Input("2");
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        Assert.Contains("can't be saved", cut.Find("#filterSaveError").TextContent);
        Assert.Empty(storage.Writes);

        // Fixing the bound is a panel gesture, so it moots the refusal.
        cut.Find("#errorMax").Input("9");

        Assert.Empty(cut.FindAll("#filterSaveError"));
    }

    [Fact]
    public async Task Delete_PersistsTheRemoval()
    {
        var storage = StorageWith(("Race", new FilterConfig()), ("Blitz", new FilterConfig()));
        var cut = RenderSurface(TokenA, storage);

        await ClickRowButtonAsync(cut, "Race", "Delete");
        await ClickRowButtonAsync(cut, "Race", "Confirm delete");

        var write = Assert.Single(storage.Writes);
        Assert.True(NamedFilterCollection.TryFromJson(write.Json, out var written));
        Assert.False(written.Contains("Race"));
        Assert.True(written.Contains("Blitz"));
    }

    // ── Degrade notices (composite-owned copy) ──────────────────────────────

    [Fact]
    public void LoadFailed_NoticeReplacesPanel_NamingTheActualFile()
    {
        // Canonical corrupt → the notice names the canonical file.
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        var cut = RenderSurface(TokenA, storage);

        var notice = cut.Find("#savedFiltersLoadFailed");
        Assert.Contains(SavedFiltersDocument.FileName, notice.TextContent);
        Assert.Contains("left untouched", notice.TextContent);
        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
    }

    [Fact]
    public void LoadFailed_OnTheLegacyFallback_NamesTheLegacyFile()
    {
        var storage = new FakeFilterDocumentStorage();
        storage.Documents[SavedFiltersDocument.LegacyFileName] = "not a filters document";
        var cut = RenderSurface(TokenA, storage);

        Assert.Contains(
            SavedFiltersDocument.LegacyFileName,
            cut.Find("#savedFiltersLoadFailed").TextContent);
    }

    // The WriteFailed copy promises page-lifetime retention only: this
    // component (and its store) lives and dies with the page, so "kept for
    // this session" would over-promise — the pinned phrase says what actually
    // holds.
    [Fact]
    public async Task WriteFailed_NoticeBesideThePanel_WithPageLifetimeCopy()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        storage.ThrowOnWrite = true;
        var cut = RenderSurface(TokenA, storage);

        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        var notice = cut.Find("#savedFiltersWriteFailed");
        Assert.Contains(SavedFiltersDocument.FileName, notice.TextContent);
        Assert.Contains("when you leave this page or reload", notice.TextContent);
        // The panel stays: the in-memory list is still truthful.
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
    }

    // ── Persist gating ──────────────────────────────────────────────────────

    [Fact]
    public void ReadOnlyAndEmpty_HidesThePanel()
    {
        // No document at all → Ready over Empty; with the host's CanPersist
        // false there is nothing to load and nothing to save — clutter rule.
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage(), canPersist: false);

        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Empty(cut.FindAll("#savedFiltersLoadFailed"));
    }

    [Fact]
    public void ReadOnlyNonEmpty_ShowsPanel_WithSavesDisabledForTheHostsReason()
    {
        const string reason = "Write access wasn't granted — saved filters can be loaded only.";
        var cut = RenderSurface(
            TokenA, StorageWith(("Race", new FilterConfig())),
            canPersist: false, persistDisabledReason: reason);

        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
        Assert.True(FindRowButton(cut, "Race", "Save")!.HasAttribute("disabled"));
        Assert.True(FindRowButton(cut, "Race", "Delete")!.HasAttribute("disabled"));
        Assert.Equal(reason, FindRowButton(cut, "Race", "Save")!.GetAttribute("title"));
    }

    // ── Required-parameter pins ─────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(FilterSurface.AppliedFilter))]
    [InlineData(nameof(FilterSurface.OnFilterConfigChanged))]
    [InlineData(nameof(FilterSurface.OnAppliedStateChanged))]
    public void LoadBearingParameters_AreEditorRequired(string parameterName)
    {
        var property = typeof(FilterSurface).GetProperty(parameterName);

        Assert.NotNull(property);
        Assert.NotNull(property.GetCustomAttribute<EditorRequiredAttribute>());
    }
}
