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

    // Host-side captures: the holder the composite mediates, and the two
    // re-raised event channels (lists — per-gesture counts are part of the
    // contract).
    private readonly AppliedFilter _holder = new();
    private readonly List<FilterConfig> _committed = [];
    private readonly List<FilterConfig?> _reports = [];

    private IRenderedComponent<FilterSurface> RenderSurface(
        FilterSourceToken? source,
        IFilterDocumentStorage? storage,
        bool canPersist = true,
        string? persistDisabledReason = null)
        => Render<FilterSurface>(parameters => parameters
            .Add(p => p.AppliedFilter, _holder)
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
        cut.Find("input[type='number'][placeholder='Min']");

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

        Assert.True(_holder.IsApplied);
        Assert.Same(appliedConfig, _holder.Config);
        Assert.True(_holder.WasAppliedFor(TokenA));
        Assert.Empty(_reports);
        Assert.Empty(_committed);
    }

    // ── The first-mount reconcile (#82) ─────────────────────────────────────

    // The navigate-back case the reconcile exists for: the holder outlived the
    // previous mount still carrying the applied config and this source's
    // stamp, and the panel restores exactly that selection from storage. The
    // fresh panel has committed nothing of its own, so without the reconcile
    // Apply re-arms with nothing to do. Seeding is silent — a reconcile
    // derives from the holder, which already agrees, so there is no news; the
    // mount pin above still holds.
    [Fact]
    public void Mount_HolderAppliedForThisSource_SeedsCommitted_ApplyDisabled_RaisesNothing()
    {
        var applied = new FilterConfig { ErrorMin = 0.1 };
        _holder.Set(applied, TokenA);
        StoredConfig(applied);

        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        cut.WaitForAssertion(() => Assert.True(Apply(cut).HasAttribute("disabled")));
        Assert.Contains("already applied", cut.Find("#applyDisabledReason").TextContent);
        Assert.Empty(_reports);
        Assert.Empty(_committed);
        Assert.Same(applied, _holder.Config);
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

        Assert.False(_holder.IsApplied);
        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // Guarded on the stamp, not merely on a config being present: a holder
    // carrying a config applied against another source has said nothing about
    // this one, so the reconcile leaves Apply armed.
    [Fact]
    public void Mount_HolderStampedForAnotherSource_DoesNotSeed_ApplyStaysEnabled()
    {
        var applied = new FilterConfig { ErrorMin = 0.1 };
        _holder.Set(applied, TokenB);
        StoredConfig(applied);

        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        cut.WaitForAssertion(() => Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value")));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // ── Applied-state mediation ─────────────────────────────────────────────

    [Fact]
    public async Task Apply_StampsHolderWithSource_AndRaisesBothEvents()
    {
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        Assert.True(_holder.IsApplied);
        Assert.True(_holder.WasAppliedFor(TokenA));
        Assert.False(_holder.WasAppliedFor(TokenB));
        var committed = Assert.Single(_committed);
        Assert.Same(committed, _holder.Config);
        // The edit's null report, then the commit's clean re-affirm.
        Assert.Equal(2, _reports.Count);
        Assert.Null(_reports[0]);
        Assert.Same(committed, _reports[1]);
    }

    [Fact]
    public async Task EditAfterApply_ClearsHolderConfig_KeepsStamp_ReportsNull()
    {
        var cut = RenderSurface(TokenA, new FakeFilterDocumentStorage());
        await Apply(cut).ClickAsync(new());
        Assert.True(_holder.IsApplied);

        ErrorMin(cut).Input("0.05");

        Assert.False(_holder.IsApplied);
        Assert.True(_holder.WasAppliedFor(TokenA)); // the stamp survives the edit
        Assert.Null(_reports[^1]);
    }

    [Fact]
    public async Task Apply_WithNoSource_LeavesHolderUntouched_StillRaisesEvents()
    {
        var cut = RenderSurface(source: null, storage: null);

        await Apply(cut).ClickAsync(new());

        Assert.False(_holder.IsApplied);
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

        // The holder's config is cleared; the stamp expires by token
        // inequality, not by a reset — WasAppliedFor(TokenA) still answers
        // for the old source, and the new source has never been filtered.
        Assert.False(_holder.IsApplied);
        Assert.True(_holder.WasAppliedFor(TokenA));
        Assert.False(_holder.WasAppliedFor(TokenB));
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
        Assert.Empty(_committed);           // staged, not committed
        Assert.False(_holder.IsApplied);    // the holder only moves on commit
        Assert.Null(_reports[^1]);          // staging reported as uncommitted
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

        Assert.Contains("position pattern is invalid", cut.Find("#filterSaveError").TextContent);
        Assert.Empty(storage.Writes);

        // Any panel gesture moots the refusal — fixing the pattern is one.
        cut.Find("#positionPattern").Input(string.Empty);

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
