using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.Testing;

namespace XgFilter_Razor.Tests;

/// <summary>
/// The composite's own wiring: the saved-filters mount and its mediation, the
/// setup-change rule for the saved-filters context, the boxes it and the
/// panel render, and what a host sees of the owner through it. The §4
/// acceptance cases are <see cref="FilterSetupAcceptanceTests"/>.
/// </summary>
public class FilterSurfaceTests : BunitContext
{
    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromGeneration(2);

    private readonly RecordingRefusalSink _refusals = new();

    public FilterSurfaceTests()
    {
        // Loose mode — storage is incidental to most of these: every read
        // answers "nothing stored" and every write lands, through the real
        // BrowserStorage. A test about what was restored plans storage
        // strictly instead (Booting).
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();

        // The host's source, latched before the page mounts.
        Setup.ReportSource(TokenA);
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    private IRenderedComponent<FilterSurface> RenderSurface(
        IDocumentStorage? storage,
        bool canPersist = true,
        string? persistDisabledReason = null)
        => Render<FilterSurface>(parameters => parameters
            .Add(p => p.Storage, storage)
            .Add(p => p.CanPersist, canPersist)
            .Add(p => p.PersistDisabledReason, persistDisabledReason));

    // A boot whose restoration reads `restore`, and `mounts` mounts of the
    // panel finding no display preference stored — planned strictly, for the
    // tests whose subject is what was restored.
    private BrowserStoragePlan Booting(BrowserStorageReadAnswer restore, int mounts = 1)
    {
        var plan = BrowserStoragePlan.On(JSInterop);
        plan.ExpectRead(BrowserStorageArea.Local, FilterStorage.ConfigKey, restore);
        plan.ExpectFilterPanelMount(mounts);
        return plan;
    }

    private BrowserStoragePlan BootingWith(FilterConfig stored, int mounts = 1) =>
        Booting(FilterSurfaceStorage.RestoreAnswer(stored), mounts);

    // The panel's facet rows are folded behind its More filters container
    // (halheinrich/backgammon#231), so a surface test that reaches a row opens
    // the container first — through its real toggle, and waiting on
    // aria-expanded, because the handler persists the choice before the
    // render that puts the rows in the DOM lands.
    private static void OpenMoreFilters(IRenderedComponent<FilterSurface> cut)
    {
        cut.Find("#moreFiltersToggle").Click();
        cut.WaitForAssertion(() => Assert.Equal(
            "true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
    }

    private static string CollectionJson(params (string Name, FilterConfig Config)[] entries)
    {
        var filters = NamedFilterCollection.Empty;
        foreach (var (name, config) in entries)
            filters = filters.With(name, config);
        return filters.ToJson();
    }

    private static FakeDocumentStorage StorageWith(params (string Name, FilterConfig Config)[] entries)
    {
        var storage = new FakeDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = CollectionJson(entries);
        return storage;
    }

    // ── Gesture helpers (the NamedEntriesPanelTests idioms, over the surface) ─

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

        var cut = RenderSurface(storage);

        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
        Assert.Contains(SavedFiltersDocument.FileName, storage.Reads);
    }

    [Fact]
    public void Mount_NullStorage_NoSavedSection_FilterPanelStillRenders()
    {
        var cut = RenderSurface(storage: null);

        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Contains("Apply Filter", cut.Markup);
    }

    // A mount is not a change. A remount over the same source — a
    // navigate-back — learns the setup as it stands, so what was applied is
    // still in effect, the panel shows it with Apply disabled for the reason
    // it is, and the mount publishes nothing that could move a host's gate.
    [Fact]
    public async Task Remount_OverTheSameSource_KeepsWhatIsInEffect_AndPublishesNothing()
    {
        var first = RenderSurface(new FakeDocumentStorage());
        ErrorMin(first).Input("0.1");
        await Apply(first).ClickAsync(new());
        await DisposeComponentsAsync();
        var published = 0;
        Setup.Attach(_ => published++);
        published = 0;

        var second = RenderSurface(new FakeDocumentStorage());

        Assert.Equal("0.1", ErrorMin(second).GetAttribute("value"));
        Assert.True(Apply(second).HasAttribute("disabled"));
        Assert.Contains("already applied", second.Find("#applyDisabledReason").TextContent);
        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        Assert.Equal(0, published);
    }

    // The surface binds only what is the host's to say here — the
    // saved-filters adapter and the host's capability ruling with its wording.
    // Everything about the selection is the owner's, so no parameter can hand
    // the surface a second copy of it.
    [Fact]
    public void TheSurface_BindsOnlyTheHostsSavedFiltersFacts()
    {
        var parameters = typeof(FilterSurface)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null)
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [nameof(FilterSurface.CanPersist), nameof(FilterSurface.PersistDisabledReason), nameof(FilterSurface.Storage)],
            parameters);
    }

    // ── The restored-selection notice (§4) ──────────────────────────────────
    //
    // A reload ends the setup: selections restored, applied-ness dropped,
    // Apply re-armed — correct by rule, indistinguishable from a bug unless
    // the screen says so. The owner is the app boot (one per test context,
    // like one per reload), which is what distinguishes a fresh boot from a
    // remount within a setup; these pins drive both sides of that line and
    // the notice's end at the first owning gesture.

    [Fact]
    public void FreshBoot_RestoredSelection_ShowsTheNotice_ApplyStaysArmed()
    {
        var plan = BootingWith(new FilterConfig { ErrorMin = 0.1 });

        var cut = RenderSurface(new FakeDocumentStorage());

        Assert.Contains("previous session", cut.Find("#filterRestoredNotice").TextContent);
        Assert.False(Apply(cut).HasAttribute("disabled"));
        plan.Verify();
    }

    [Fact]
    public void Mount_NothingStored_NoNotice()
    {
        var plan = Booting(BrowserStorageReadAnswer.Absent);

        var cut = RenderSurface(new FakeDocumentStorage());

        Assert.Equal(FilterRestoration.NothingStored, Setup.Current.Restoration);
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        plan.Verify();
    }

    [Fact]
    public void Notice_EndsAtTheFirstEdit()
    {
        BootingWith(new FilterConfig { ErrorMin = 0.1 });
        var cut = RenderSurface(new FakeDocumentStorage());

        ErrorMin(cut).Input("0.2");

        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
    }

    [Fact]
    public async Task Notice_EndsAtApply()
    {
        var plan = BootingWith(new FilterConfig { ErrorMin = 0.1 });
        plan.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderSurface(new FakeDocumentStorage());

        await Apply(cut).ClickAsync(new());

        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        plan.Verify();
    }

    [Fact]
    public void Notice_SurvivesAFacetRowToggle()
    {
        // Opening a filter row is navigation, not an edit — the restored
        // selection is still not the user's own, so the notice holds. The rows
        // are folded behind the More filters container
        // (halheinrich/backgammon#231) — opening it is navigation too.
        var plan = BootingWith(new FilterConfig { ErrorMin = 0.1 });
        plan.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded);
        plan.ExpectFilterRowsToggle([FilterFacet.DiceRolls], BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderSurface(new FakeDocumentStorage());

        OpenMoreFilters(cut);
        cut.Find("#facetToggle_DiceRolls").Click();

        Assert.NotNull(cut.Find("#filterRestoredNotice"));
        plan.Verify();
    }

    // The trap the owner's lifetime exists for: navigate-back with unapplied
    // edits looks exactly like a fresh boot at mount time — the same selection
    // on screen, nothing in effect — and §1 rules that navigation changes
    // nothing, including no new notice. The edit ended this boot's notice, and
    // the remount reads nothing, so nothing can bring it back.
    [Fact]
    public async Task Remount_WithinSetup_AfterAnEdit_DoesNotResurrectTheNotice()
    {
        var plan = BootingWith(new FilterConfig { ErrorMin = 0.1 }, mounts: 2);
        var first = RenderSurface(new FakeDocumentStorage());
        ErrorMin(first).Input("0.2");

        await DisposeComponentsAsync();
        var second = RenderSurface(new FakeDocumentStorage());

        Assert.Equal("0.2", ErrorMin(second).GetAttribute("value"));
        Assert.Empty(second.FindAll("#filterRestoredNotice"));
        plan.Verify();
    }

    // The other side of that line: navigation changes nothing, so a remount
    // over a still-untouched restored selection shows the same notice.
    [Fact]
    public async Task Remount_WithinSetup_Untouched_KeepsTheNotice()
    {
        var plan = BootingWith(new FilterConfig { ErrorMin = 0.1 }, mounts: 2);
        RenderSurface(new FakeDocumentStorage());

        await DisposeComponentsAsync();
        var second = RenderSurface(new FakeDocumentStorage());

        Assert.NotNull(second.Find("#filterRestoredNotice"));
        plan.Verify();
    }

    // A source change is not a user gesture: the setup ends and Apply re-arms
    // without ending the notice, whose statement — these selections came from
    // a previous session and are not in effect — is still true against the
    // new source.
    [Fact]
    public async Task Notice_SurvivesASourceChange()
    {
        BootingWith(new FilterConfig { ErrorMin = 0.1 });
        var cut = RenderSurface(new FakeDocumentStorage());

        await cut.InvokeAsync(() => Setup.ReportSource(TokenB));

        Assert.NotNull(cut.Find("#filterRestoredNotice"));
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
    }

    // ── What a host reads ───────────────────────────────────────────────────

    [Fact]
    public async Task Apply_PutsTheSelectionInEffect_ForThisSourceOnly()
    {
        var cut = RenderSurface(new FakeDocumentStorage());

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        Assert.Equal(new FilterConfig { ErrorMin = 0.05 }, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.Null(Setup.Current.ConfigInEffectFor(TokenB));
    }

    // An edit takes the selection out of effect — nothing stays in force for
    // any source (spec §3: only present ownership exists).
    [Fact]
    public async Task EditAfterApply_TakesTheSelectionOutOfEffect()
    {
        var cut = RenderSurface(new FakeDocumentStorage());
        await Apply(cut).ClickAsync(new());
        Assert.True(Setup.Current.IsInEffectFor(TokenA));

        ErrorMin(cut).Input("0.05");

        Assert.False(Setup.Current.IsInEffectFor(TokenA));
    }

    // With no source there is nothing to filter (§1, "Before a dir is
    // selected"): Apply is off, and nothing is in effect.
    [Fact]
    public async Task WithNoSource_ApplyIsOff()
    {
        Setup.ReportSource(null);
        var cut = RenderSurface(storage: null);

        await Apply(cut).ClickAsync(new());

        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Null(Setup.Current.Baseline);
        // And no reason claims a filter state: with no source, "no filter is
        // set, so every decision is included" would be false.
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // Hal's ruling of 2026-10-08 on the surface before a source is reported:
    // editing is allowed, Apply is off, and Clear is off and writes nothing —
    // the strict plan has no write for it, so a dispatch that ignored the
    // disabled button would fail here — and no disabled button gives a
    // reason. A draft prepared meanwhile is kept for the source to come.
    [Fact]
    public async Task BeforeASource_EditingIsAllowed_ApplyAndClearAreOff_AndClearWritesNothing()
    {
        Setup.ReportSource(null);
        var plan = Booting(FilterSurfaceStorage.RestoreAnswer(FilterRestoration.NothingStored));
        var cut = RenderSurface(storage: null);

        ErrorMin(cut).Input("0.1");

        Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.True(cut.Find("#clearFilters").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));

        await cut.Find("#clearFilters").ClickAsync(new());
        await cut.InvokeAsync(() => Setup.ClearAsync());

        Assert.Equal("0.1", Setup.Current.Draft.ErrorMinText);
        Assert.Null(Setup.Current.Baseline);
        Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value"));
        plan.Verify();
    }

    // ── The setup-change rule for the saved-filters context ────────────────

    [Fact]
    public async Task SourceChange_EndsTheSetup_ReArmsApply_ReloadsTheContext()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(storage);
        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));
        var readsBefore = storage.Reads.Count;

        await cut.InvokeAsync(() => Setup.ReportSource(TokenB));

        // The setup ended: nothing stays in force for the old source or the new.
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        // Apply re-armed on the still-mounted panel; the draft stayed.
        cut.WaitForAssertion(() => Assert.False(Apply(cut).HasAttribute("disabled")));
        Assert.Equal("0.05", ErrorMin(cut).GetAttribute("value"));
        // The saved-filters context reloaded through the seam.
        cut.WaitForAssertion(() => Assert.True(storage.Reads.Count > readsBefore));
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
    }

    [Fact]
    public async Task SourceChangeToNone_ResetsTheContext_HidesTheSavedSection()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(storage);
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));

        await cut.InvokeAsync(() => Setup.ReportSource(null));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("li.list-group-item")));
        Assert.Empty(cut.FindAll("#saveFilterName"));
    }

    // The reload a setup's end starts is awaited by nothing, so the composite
    // observes it (halheinrich/backgammon#374, the review's third correction).
    // An adapter whose read fails after an await with anything but the
    // storage's own failure has a bug, and the fault goes to the renderer's
    // error path as the composite's: here, an enclosing ErrorBoundary. The
    // mount's own load succeeded; the failure belongs to the reload.
    [Fact]
    public async Task ASourceChangeReload_ThatFailsAfterAnAwait_ReachesTheErrorBoundary()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = Render<ErrorBoundary>(parameters => parameters
            .Add(p => p.ChildContent, builder =>
            {
                builder.OpenComponent<FilterSurface>(0);
                builder.AddComponentParameter(1, nameof(FilterSurface.Storage), storage);
                builder.CloseComponent();
            })
            .Add(p => p.ErrorContent, fault => builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "id", "surfaceFault");
                builder.AddContent(2, fault.Message);
                builder.CloseElement();
            }));
        Assert.NotNull(FindRowButton(cut.FindComponent<FilterSurface>(), "Race", "Load"));
        var held = new TaskCompletionSource();
        storage.ReadOverride = async _ =>
        {
            await held.Task;
            throw new InvalidOperationException("The adapter has a bug.");
        };

        await cut.InvokeAsync(() => Setup.ReportSource(TokenB));
        held.SetResult();

        cut.WaitForAssertion(() =>
            Assert.Equal("The adapter has a bug.", cut.Find("#surfaceFault").TextContent));
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // With no ErrorBoundary around it, the same fault reaches the renderer
    // itself — the host's unhandled-error handling — never a discarded task.
    [Fact]
    public async Task ASourceChangeReload_ThatFailsAfterAnAwait_WithNoErrorBoundary_ReachesTheRenderer()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(storage);
        var held = new TaskCompletionSource();
        storage.ReadOverride = async _ =>
        {
            await held.Task;
            throw new InvalidOperationException("The adapter has a bug.");
        };

        await cut.InvokeAsync(() => Setup.ReportSource(TokenB));
        held.SetResult();

        var fault = await Renderer.UnhandledException.WaitAsync(DefaultWaitTimeout);
        Assert.Equal("The adapter has a bug.", fault.Message);
    }

    // The storage's own failure is not a fault: the store degrades to
    // LoadFailed, the composite's existing notice names the file, and nothing
    // reaches the renderer's error path.
    [Fact]
    public async Task ASourceChangeReload_ThatTheStorageRefusesAfterAnAwait_ShowsTheLoadFailedNotice()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(storage);
        var held = new TaskCompletionSource();
        storage.ReadOverride = async _ =>
        {
            await held.Task;
            throw new DocumentStorageException("read failed");
        };

        await cut.InvokeAsync(() => Setup.ReportSource(TokenB));
        held.SetResult();

        cut.WaitForAssertion(() =>
            Assert.Contains(SavedFiltersDocument.FileName, cut.Find("#savedFiltersLoadFailed").TextContent));
        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.False(Renderer.UnhandledException.IsCompleted);
    }

    // ── Saved-filters wiring ────────────────────────────────────────────────

    [Fact]
    public async Task Load_StagesTheSavedConfig_WithoutCommitting()
    {
        var cut = RenderSurface(StorageWith(("Race", new FilterConfig { ErrorMin = 0.25 })));

        await ClickRowButtonAsync(cut, "Race", "Load");

        Assert.Equal("0.25", ErrorMin(cut).GetAttribute("value"));
        Assert.Null(Setup.Current.Baseline);          // staged, not committed
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
    }

    // A load request naming an entry the document does not hold cannot come
    // from the panel's rows — every name it raises came from this very
    // collection — so the miss is a wiring bug, and the composite says so
    // instead of no-opping under the panel's "{name} loaded." confirmation
    // (halheinrich/backgammon#173). Reached through the mounted panel's own
    // callback, the only way to pose a name the rows cannot.
    [Fact]
    public async Task LoadRequest_NamingAnAbsentEntry_Throws()
    {
        var cut = RenderSurface(StorageWith(("Race", new FilterConfig())));
        var panel =
            cut.FindComponent<NamedEntriesPanel<FilterConfig, NamedFilterCollection>>();

        // Through the renderer's dispatcher, the way a real click arrives —
        // invoking the callback off-thread trips bUnit's own guard instead.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => cut.InvokeAsync(() => panel.Instance.OnLoadRequested.InvokeAsync("Blitz")));

        Assert.Contains("Blitz", thrown.Message);
    }

    [Fact]
    public async Task RowSave_SnapshotsTheLiveDraft_WritesCanonicalThroughSeam()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(storage);

        ErrorMin(cut).Input("0.5");
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        var write = Assert.Single(storage.Writes);
        Assert.Equal(SavedFiltersDocument.FileName, write.FileName);
        Assert.True(NamedFilterCollection.TryFromJson(write.Json, out var written));
        Assert.True(written.TryGet("Race", out var saved));
        Assert.Equal(0.5, saved!.ErrorMin); // the unapplied edit rode along
    }

    [Fact]
    public async Task SaveAs_NewName_WritesThroughSeam()
    {
        var storage = new FakeDocumentStorage();
        var cut = RenderSurface(storage);

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
        var cut = RenderSurface(storage);

        // Stage an unparseable position pattern (inside its own filter row),
        // then attempt a row Save: the snapshot is refused, so the composite
        // must say why instead of no-opping silently.
        OpenMoreFilters(cut);   // the rows are behind it
        cut.Find("#facetToggle_PositionPattern").Click();
        cut.Find("#positionPattern").Input("not a bracket list");
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        Assert.Contains("can't be saved", cut.Find("#filterSaveError").TextContent);
        Assert.Empty(storage.Writes);

        // Any change to the selection moots the refusal — fixing the pattern is one.
        cut.Find("#positionPattern").Input(string.Empty);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#filterSaveError")));
    }

    // The refusal is the draft's validity verdict, not one particular rule of
    // it: an error bound the lib rules invalid refuses the snapshot exactly as
    // an unparseable pattern does, and so does a box its field cannot be —
    // each with the same field-agnostic copy, the offending value already
    // marked, with its own explanation, in the panel below.
    [Theory]
    [InlineData("5", "2")]
    [InlineData("abc", "")]
    public async Task Save_InvalidErrorBound_RefusalNotice_NoWrite(string min, string max)
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        var cut = RenderSurface(storage);

        cut.Find("#errorMin").Input(min);
        cut.Find("#errorMax").Input(max);
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");

        Assert.Contains("can't be saved", cut.Find("#filterSaveError").TextContent);
        Assert.Empty(storage.Writes);

        // Fixing the bound is a change to the selection, so it moots the refusal.
        cut.Find("#errorMin").Input("1");
        cut.Find("#errorMax").Input("9");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#filterSaveError")));
    }

    [Fact]
    public async Task Delete_PersistsTheRemoval()
    {
        var storage = StorageWith(("Race", new FilterConfig()), ("Blitz", new FilterConfig()));
        var cut = RenderSurface(storage);

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
        var storage = new FakeDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        var cut = RenderSurface(storage);

        var notice = cut.Find("#savedFiltersLoadFailed");
        Assert.Contains(SavedFiltersDocument.FileName, notice.TextContent);
        Assert.Contains("left untouched", notice.TextContent);
        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
    }

    [Fact]
    public void LoadFailed_OnTheLegacyFallback_NamesTheLegacyFile()
    {
        var storage = new FakeDocumentStorage();
        storage.Documents[SavedFiltersDocument.LegacyFileName] = "not a filters document";
        var cut = RenderSurface(storage);

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
        var cut = RenderSurface(storage);

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
        var cut = RenderSurface(new FakeDocumentStorage(), canPersist: false);

        Assert.Empty(cut.FindAll("li.list-group-item"));
        Assert.Empty(cut.FindAll("#saveFilterName"));
        Assert.Empty(cut.FindAll("#savedFiltersLoadFailed"));
    }

    [Fact]
    public void ReadOnlyNonEmpty_ShowsPanel_WithSavesDisabledForTheHostsReason()
    {
        const string reason = "Write access wasn't granted — saved filters can be loaded only.";
        var cut = RenderSurface(
            StorageWith(("Race", new FilterConfig())),
            canPersist: false, persistDisabledReason: reason);

        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
        Assert.True(FindRowButton(cut, "Race", "Save")!.HasAttribute("disabled"));
        Assert.True(FindRowButton(cut, "Race", "Delete")!.HasAttribute("disabled"));
        Assert.Equal(reason, FindRowButton(cut, "Race", "Save")!.GetAttribute("title"));
    }

    // ── The boxes render through the shared Notice (halheinrich/backgammon#248) ──
    //
    // The umbrella's SPEC-notices.md rules which of this composite's boxes
    // dismiss and what a dismissal survives; BgUiPrimitives_Razor's Notice owns
    // the markup and pins its own gestures. What is pinned HERE is this
    // member's half: that each box landed on the component with the identity
    // it had before (id, spacing class, inline style, role), that each
    // dismissible one has exactly one holder which a new occurrence resets,
    // and that the gate reason cannot be closed. Every gesture goes through the
    // real composite and a real DOM click — both of them, the close button and
    // the box, because the model rules both.

    // The two dismiss gestures, as a selector suffix under a box's id.
    public static TheoryData<string> DismissGestures => new()
    {
        " button.btn-close",    // the visible affordance: keyboard and screen reader
        string.Empty,           // the whole box: the large target
    };

    private static void AssertBox(
        IElement box, string expectedClass, string expectedRole, string? expectedStyle, bool dismissible)
    {
        Assert.Equal(expectedClass, box.GetAttribute("class"));
        Assert.Equal(expectedStyle, box.GetAttribute("style"));
        Assert.Equal(dismissible ? 1 : 0, box.QuerySelectorAll("button.btn-close").Length);

        // Who announces. The box is never the live region: a status or alert
        // region is atomic, so a role on the box would make the close
        // button's name part of what the notice says. The region is the
        // content wrapper — exactly one element under the box carries a role,
        // it carries this box's, and the close button sits outside it.
        Assert.False(box.HasAttribute("role"));
        var region = Assert.Single(box.QuerySelectorAll("[role]"));
        Assert.True(region.ClassList.Contains("bg-notice-content"));
        Assert.Equal(expectedRole, region.GetAttribute("role"));
        Assert.Empty(region.QuerySelectorAll("button.btn-close"));

        // And nothing else was splatted onto the box. An attribute on the tag
        // that Notice does not match as a parameter lands here silently — a
        // renamed parameter did exactly that once, with the build green and
        // the two assertive boxes quietly polite. So the box carries what
        // this member passes (id, class, style) and the renderer's own
        // bookkeeping (event and CSS-scope attributes), and nothing more.
        Assert.All(box.Attributes, a => Assert.True(
            a.Name is "id" or "class" or "style"
                || a.Name.StartsWith("blazor:", StringComparison.Ordinal)
                || a.Name.StartsWith("b-", StringComparison.Ordinal),
            $"Unexpected attribute on the box: {a.Name}"));
    }

    private IRenderedComponent<FilterSurface> RenderWithRestoredSelection(int mounts = 1)
    {
        BootingWith(new FilterConfig { ErrorMin = 0.1 }, mounts);
        var cut = RenderSurface(new FakeDocumentStorage());
        Assert.NotNull(cut.Find("#filterRestoredNotice"));
        return cut;
    }

    // Min above Max on the always-visible facet, then a confirmed row Save:
    // the snapshot is refused. Repeatable with no gesture in between, which is
    // what makes two consecutive refusals — identical text — reachable.
    private static async Task RefuseASaveAsync(IRenderedComponent<FilterSurface> cut)
    {
        cut.Find("#errorMin").Input("5");
        cut.Find("#errorMax").Input("2");
        await ConfirmRowSaveAsync(cut);
    }

    private static async Task ConfirmRowSaveAsync(IRenderedComponent<FilterSurface> cut)
    {
        await ClickRowButtonAsync(cut, "Race", "Save");
        await ClickRowButtonAsync(cut, "Race", "Overwrite");
    }

    // · #filterRestoredNotice — event notice; holder: the app-scoped FilterSetup

    [Fact]
    public void RestoreNotice_LandsOnTheComponent_WithItsIdentityIntact()
    {
        var cut = RenderWithRestoredSelection();

        AssertBox(cut.Find("#filterRestoredNotice"),
            "alert alert-info alert-dismissible mb-3", "status", expectedStyle: null, dismissible: true);
    }

    [Theory]
    [MemberData(nameof(DismissGestures))]
    public void RestoreNotice_Dismisses_AndTheOwnerIsWhatMoved(string gesture)
    {
        var cut = RenderWithRestoredSelection();
        var draft = Setup.Current.Draft;

        cut.Find("#filterRestoredNotice" + gesture).Click();

        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        Assert.False(Setup.Current.IsRestoredNoticeShowing);
        // Closing a notice is not an edit: the draft is as it was, and Apply
        // is still armed over the still-restored selection.
        Assert.Same(draft, Setup.Current.Draft);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The reason the dismissal is the owner's and not the panel's: the
    // occurrence — this boot's restore — outlives the mount. A navigate-back
    // remounts the panel; the closed notice must not come back.
    [Fact]
    public async Task RestoreNotice_ClosedByTheUser_StaysClosedAcrossARemount()
    {
        var first = RenderWithRestoredSelection(mounts: 2);
        first.Find("#filterRestoredNotice button.btn-close").Click();

        await DisposeComponentsAsync();
        var second = RenderSurface(new FakeDocumentStorage());

        Assert.Equal("0.1", ErrorMin(second).GetAttribute("value"));
        Assert.Empty(second.FindAll("#filterRestoredNotice"));
    }

    // · #filterSaveError — error; holder: this composite, as the refusal itself

    [Fact]
    public async Task SaveError_LandsOnTheComponent_WithItsIdentityIntact()
    {
        var cut = RenderSurface(StorageWith(("Race", new FilterConfig())));

        Assert.Empty(cut.FindAll("#filterSaveError"));
        await RefuseASaveAsync(cut);

        AssertBox(cut.Find("#filterSaveError"),
            "alert alert-danger alert-dismissible mb-4", "alert", "max-width:800px", dismissible: true);
    }

    // The text is not the occurrence: both refusals carry the same copy, and
    // the second must show although the first was closed. No change to the
    // selection sits between them, so nothing but the refusal itself can have
    // re-shown it.
    [Theory]
    [MemberData(nameof(DismissGestures))]
    public async Task SaveError_Dismisses_AndASecondIdenticalRefusalShowsFresh(string gesture)
    {
        var cut = RenderSurface(StorageWith(("Race", new FilterConfig())));
        await RefuseASaveAsync(cut);
        var firstText = cut.Find("#filterSaveError").TextContent;

        cut.Find("#filterSaveError" + gesture).Click();

        Assert.Empty(cut.FindAll("#filterSaveError"));

        await ConfirmRowSaveAsync(cut);

        Assert.Equal(firstText, cut.Find("#filterSaveError").TextContent);
    }

    // · #savedFiltersLoadFailed — gate reason; never dismissible

    [Fact]
    public void LoadFailed_LandsOnTheComponent_AndCannotBeClosed()
    {
        var storage = new FakeDocumentStorage();
        storage.Documents[SavedFiltersDocument.FileName] = "not a filters document";
        var cut = RenderSurface(storage);

        var box = cut.Find("#savedFiltersLoadFailed");
        AssertBox(box, "alert alert-warning mb-4", "status", "max-width:800px", dismissible: false);

        // No handler at all, on the box or inside it — a click is not merely
        // ignored, there is nothing listening for one.
        Assert.Throws<MissingEventHandlerException>(() => box.Click());
        Assert.Throws<MissingEventHandlerException>(
            () => cut.Find("#savedFiltersLoadFailed .bg-notice-content").Click());
        Assert.NotNull(cut.Find("#savedFiltersLoadFailed"));
    }

    // · #savedFiltersWriteFailed — condition notice; occurrence named by the store

    private async Task<IRenderedComponent<FilterSurface>> RenderWithAFailedWriteAsync()
    {
        var storage = StorageWith(("Race", new FilterConfig()));
        storage.ThrowOnWrite = true;
        var cut = RenderSurface(storage);
        await ConfirmRowSaveAsync(cut);
        return cut;
    }

    [Fact]
    public async Task WriteFailed_LandsOnTheComponent_WithItsIdentityIntact()
    {
        var cut = await RenderWithAFailedWriteAsync();

        AssertBox(cut.Find("#savedFiltersWriteFailed"),
            "alert alert-warning alert-dismissible mb-4", "alert", "max-width:800px", dismissible: true);
    }

    // Dismissible per failed write. A WriteFailed context refuses further
    // writes, so the next failure is reached the only way a user reaches it:
    // the source changes, the context reloads to Ready, and the next save
    // fails again. The store names that failure; the notice shows for it.
    [Theory]
    [MemberData(nameof(DismissGestures))]
    public async Task WriteFailed_Dismisses_AndTheNextFailedWriteShowsFresh(string gesture)
    {
        var cut = await RenderWithAFailedWriteAsync();

        cut.Find("#savedFiltersWriteFailed" + gesture).Click();

        Assert.Empty(cut.FindAll("#savedFiltersWriteFailed"));
        // Closing the notice closes nothing else: the condition stands, so
        // saving stays off and the in-memory list stays on screen.
        Assert.True(FindRowButton(cut, "Race", "Save")!.HasAttribute("disabled"));

        await cut.InvokeAsync(() => Setup.ReportSource(TokenB));
        cut.WaitForAssertion(() =>
            Assert.False(FindRowButton(cut, "Race", "Save")!.HasAttribute("disabled")));
        await ConfirmRowSaveAsync(cut);

        Assert.NotNull(cut.Find("#savedFiltersWriteFailed"));
    }

    // ── The restore's outcomes (halheinrich/backgammon#367, #346) ───────────
    //
    // The remembered selection reaches the owner in one of four states, and
    // each has its own answer. Readable: restored and staged, under the
    // restored-selection notice above. Present but unreadable: defaults, and
    // the failed-restore notice — a failed restore is never silent (Hal's
    // ruling). Absent: defaults and no word — a first visit is not a failure.
    // Refused: defaults, no notice of the panel's, the refusal at the host's
    // sink. The first pin is the producer's ruling carried through
    // (XgFilter_Lib, halheinrich/backgammon#269): a stored pattern the grammar
    // refuses is not an unreadable document. It restores whole, every other
    // value intact, the pattern shown as stored, the field marked, and Apply
    // withheld with the field's own reason — never silently repaired, never
    // dropped, never silently applied.

    private const string RestoreFailedNotice = "#filterRestoreFailedNotice";

    [Fact]
    public void Restore_RefusedPattern_RestoresWhole_MarksTheField_WithholdsApply()
    {
        var plan = BootingWith(new FilterConfig { ErrorMin = 0.1, PositionPattern = "[6,2" });
        plan.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded);
        plan.ExpectFilterRowsToggle([FilterFacet.PositionPattern], BrowserStorageWriteAnswer.Succeeded);

        var cut = RenderSurface(new FakeDocumentStorage());

        // Every other value intact, and the document counted as restored: the
        // restored-selection notice, not the failure notice.
        Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value"));
        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        Assert.NotNull(cut.Find("#filterRestoredNotice"));
        Assert.Empty(cut.FindAll(RestoreFailedNotice));

        // The text the user stored, shown back, marked, with its reason.
        OpenMoreFilters(cut);
        cut.Find("#facetToggle_PositionPattern").Click();
        var pattern = cut.Find("#positionPattern");
        Assert.Equal("[6,2", pattern.GetAttribute("value"));
        Assert.Contains("is-invalid", pattern.GetAttribute("class"));
        Assert.NotNull(cut.Find("#positionPattern ~ .invalid-feedback"));

        // And no way to make it an executable filter without fixing it.
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        plan.Verify();
    }

    [Fact]
    public void Restore_UnreadableDocument_RestoresDefaults_AndSaysSo()
    {
        var plan = Booting(FilterSurfaceStorage.RestoreAnswer(FilterRestoration.Unreadable));

        var cut = RenderSurface(new FakeDocumentStorage());

        Assert.Equal(FilterRestoration.Unreadable, Setup.Current.Restoration);
        Assert.NotNull(cut.Find(RestoreFailedNotice));
        Assert.Equal(string.Empty, ErrorMin(cut).GetAttribute("value"));
        // Nothing was restored, so the restored-selection notice has no claim
        // to make. The unreadable document is left as it is — the plan holds
        // the boot to its reads alone, no write.
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        plan.Verify();
    }

    [Fact]
    public void Restore_NothingStored_RestoresDefaults_AndSaysNothing()
    {
        var plan = Booting(BrowserStorageReadAnswer.Absent);

        var cut = RenderSurface(new FakeDocumentStorage());

        Assert.Equal(FilterRestoration.NothingStored, Setup.Current.Restoration);
        Assert.Equal(string.Empty, ErrorMin(cut).GetAttribute("value"));
        Assert.Empty(cut.FindAll(RestoreFailedNotice));
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        plan.Verify();
    }

    [Fact]
    public void Restore_Refused_RestoresDefaults_TellsTheSink_AndShowsNoNoticeOfItsOwn()
    {
        var plan = Booting(BrowserStorageReadAnswer.Refused);

        var cut = RenderSurface(new FakeDocumentStorage());

        Assert.Equal(FilterRestoration.Refused, Setup.Current.Restoration);
        Assert.Equal(string.Empty, ErrorMin(cut).GetAttribute("value"));
        Assert.Single(_refusals.Refusals);
        Assert.Empty(cut.FindAll(RestoreFailedNotice));
        Assert.Empty(cut.FindAll("#filterRestoredNotice"));
        plan.Verify();
    }

    // The saved-filters document holds the same posture one tier up
    // (XgFilter_Lib's NamedFilterCollection, halheinrich/backgammon#269): an
    // entry whose pattern the grammar refuses loads beside the others, and
    // loading it stages exactly what a refused restore stages — the text,
    // the mark, Apply withheld. The store's LoadFailed path is for a document
    // that cannot be read at all, and one such entry is not that.
    [Fact]
    public async Task SavedEntry_WithARefusedPattern_LoadsBesideTheOthers_AndStagesAsInvalid()
    {
        var storage = StorageWith(
            ("Stale", new FilterConfig { PositionPattern = "[6,2" }),
            ("Race", new FilterConfig { ErrorMin = 0.1 }));

        var cut = RenderSurface(storage);

        Assert.NotNull(FindRowButton(cut, "Stale", "Load"));
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
        Assert.Empty(cut.FindAll("#savedFiltersLoadFailed"));

        await ClickRowButtonAsync(cut, "Stale", "Load");

        OpenMoreFilters(cut);
        cut.Find("#facetToggle_PositionPattern").Click();
        var pattern = cut.Find("#positionPattern");
        Assert.Equal("[6,2", pattern.GetAttribute("value"));
        Assert.Contains("is-invalid", pattern.GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
    }

    // · #filterRestoreFailedNotice — event notice; holder: the app-scoped
    //   FilterSetup, the same owner for the same occurrence (this boot's
    //   restore) and the same reason.

    private IRenderedComponent<FilterSurface> RenderWithFailedRestore(int mounts = 1)
    {
        Booting(FilterSurfaceStorage.RestoreAnswer(FilterRestoration.Unreadable), mounts);
        var cut = RenderSurface(new FakeDocumentStorage());
        Assert.NotNull(cut.Find(RestoreFailedNotice));
        return cut;
    }

    [Fact]
    public void RestoreFailedNotice_LandsOnTheComponent_WithItsIdentityIntact()
    {
        var cut = RenderWithFailedRestore();

        AssertBox(cut.Find(RestoreFailedNotice),
            "alert alert-warning alert-dismissible mb-3", "status", expectedStyle: null, dismissible: true);
    }

    [Theory]
    [MemberData(nameof(DismissGestures))]
    public void RestoreFailedNotice_Dismisses_AndTheOwnerIsWhatMoved(string gesture)
    {
        var cut = RenderWithFailedRestore();
        var draft = Setup.Current.Draft;

        cut.Find(RestoreFailedNotice + gesture).Click();

        Assert.Empty(cut.FindAll(RestoreFailedNotice));
        Assert.False(Setup.Current.IsFailureNoticeShowing);
        // Closing a notice is not an edit: the draft is as it was.
        Assert.Same(draft, Setup.Current.Draft);
    }

    // Why the dismissal is the owner's and not the panel's: the unreadable
    // document outlives the mount. A navigate-back remounts the panel; the
    // closed notice must not come back.
    [Fact]
    public async Task RestoreFailedNotice_ClosedByTheUser_StaysClosedAcrossARemount()
    {
        var first = RenderWithFailedRestore(mounts: 2);
        first.Find(RestoreFailedNotice + " button.btn-close").Click();

        await DisposeComponentsAsync();
        var second = RenderSurface(new FakeDocumentStorage());

        Assert.Empty(second.FindAll(RestoreFailedNotice));
    }

    // The other side: untouched, a remount shows the same notice (navigation
    // changes nothing).
    [Fact]
    public async Task RestoreFailedNotice_NotDismissed_IsStillShownAfterARemount()
    {
        RenderWithFailedRestore(mounts: 2);

        await DisposeComponentsAsync();
        var second = RenderSurface(new FakeDocumentStorage());

        Assert.NotNull(second.Find(RestoreFailedNotice));
    }

    // An edit changes nothing about what is stored, so the notice stands; a
    // commit writes a fresh document over the unreadable one, so it ends.
    [Fact]
    public async Task RestoreFailedNotice_SurvivesAnEdit_AndEndsAtACommitWhoseWriteLands()
    {
        var plan = Booting(FilterSurfaceStorage.RestoreAnswer(FilterRestoration.Unreadable));
        plan.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.2 }, BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderSurface(new FakeDocumentStorage());

        ErrorMin(cut).Input("0.2");
        Assert.NotNull(cut.Find(RestoreFailedNotice));

        await Apply(cut).ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(RestoreFailedNotice)));
        Assert.False(Setup.Current.IsFailureNoticeShowing);
        plan.Verify();
    }
}
