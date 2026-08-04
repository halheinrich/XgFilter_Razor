using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using BgDataTypes_Lib;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;

namespace XgFilter_Razor.Tests;

public class FilterPanelTests : BunitContext
{
    public FilterPanelTests()
    {
        // Loose mode — OnAfterRenderAsync issues localStorage.getItem calls;
        // the mock returns default (null) for each, which is what the
        // component expects for "no persisted state."
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // The two localStorage keys the panel persists under: the whole
    // FilterConfig as one serialized blob, and the disclosure's
    // expand/collapse choice — user preference, deliberately outside the
    // config blob.
    private const string ConfigKey = "xg_filter_config";
    private const string DisclosureKey = "xg_moreFiltersExpanded";

    // The disclosure hides every section except Error range at rest, so tests
    // that touch a hidden control expand first — through the real toggle
    // button, so every such test also exercises the disclosure's actual
    // wiring rather than reaching around it.
    private static void ExpandMoreFilters(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("#moreFiltersToggle").Click();

    // Render-and-expand, for the many tests whose subject controls live
    // behind the disclosure.
    private IRenderedComponent<FilterPanel> RenderExpanded(
        Action<ComponentParameterCollectionBuilder<FilterPanel>>? parameters = null)
    {
        var cut = parameters is null ? Render<FilterPanel>() : Render<FilterPanel>(parameters);
        ExpandMoreFilters(cut);
        return cut;
    }

    // Render with the applied-state channel captured into `reports`. A list,
    // not a single field: OnAppliedStateChanged is per-gesture by contract, so
    // how many times it fired is as much a part of the assertion as what it
    // carried.
    private IRenderedComponent<FilterPanel> RenderReporting(List<FilterConfig?> reports) =>
        Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

    private IRenderedComponent<FilterPanel> RenderExpandedReporting(List<FilterConfig?> reports)
    {
        var cut = RenderReporting(reports);
        ExpandMoreFilters(cut);
        return cut;
    }

    // The two controls these tests drive to make the panel dirty and clean
    // again — the always-visible Error-range Min box and the Apply button.
    private static IElement ErrorMin(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("input[type='number'][placeholder='Min']");

    private static IElement Apply(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("button.btn-primary");

    [Fact]
    public void Render_DefaultParameters_ProducesFilterCardMarkup()
    {
        var cut = Render<FilterPanel>();

        Assert.Contains("Filters", cut.Markup);
        Assert.Contains("Apply Filter", cut.Markup);
        Assert.Contains("Clear filters", cut.Markup);
    }

    [Fact]
    public void EventCallbacks_AreAccepted()
    {
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig _) => { })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? _) => { }));

        Assert.NotNull(cut);
    }

    [Fact]
    public async Task ApplyButton_RaisesFilterConfigCallback()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        var applyButton = cut.Find("button.btn-primary");
        await applyButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.NotNull(capturedConfig);
        Assert.Equal(DecisionTypeOption.Both, capturedConfig!.DecisionType);
    }

    // ── Apply gate & applied-state reporting ─────────────────────────────────
    // The panel owns the cleanliness truth because it is the only party holding
    // both the live edit buffers and the config it last committed. The tests
    // below pin that truth's two surfaces — the Apply button's disabled state
    // and the OnAppliedStateChanged payload — which are computed once in the
    // component and must therefore never disagree.

    // Nothing has been committed on a fresh mount, so Apply is offered from the
    // start and the panel volunteers no disabled-reason.
    [Fact]
    public void FreshMount_LeavesApplyEnabled_WithNoDisabledReason()
    {
        var cut = Render<FilterPanel>();

        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // Applying commits: the button disables itself, the event reports the very
    // config just committed (the same instance the config callback carried, not
    // a fresh equal one), and the panel says why the button is dead.
    [Fact]
    public async Task Apply_DisablesItself_AndReportsTheCommittedConfig()
    {
        FilterConfig? committed = null;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { committed = c; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        // The edit reported dirty, then the commit reported clean.
        Assert.Equal(2, reports.Count);
        Assert.Null(reports[0]);
        Assert.NotNull(committed);
        Assert.Same(committed, reports[1]);

        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Contains("already applied", cut.Find("#applyDisabledReason").TextContent);
    }

    // Any edit moves the buffers off the committed config: Apply re-opens, the
    // event reports null, and the disabled-reason disappears rather than going
    // stale.
    [Fact]
    public async Task EditAfterApply_ReEnablesApply_AndReportsNull()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("0.05");

        Assert.Null(reports[^1]);
        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // The wedge this design exists to kill. Deriving cleanliness from value
    // equality — rather than latching a one-way dirty flag — means an edit
    // undone back to the committed values counts as clean again, so the panel
    // re-reports the committed config and closes the gate. Under a dirty flag
    // the panel would stay "dirty" with Apply's own equality check disabling
    // the only control that could clear it: a consumer gating on the flag would
    // be stranded with no recovery gesture.
    [Fact]
    public async Task EditUndoneBackToCommittedValues_GoesCleanAgain()
    {
        FilterConfig? committed = null;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { committed = c; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        ErrorMin(cut).Input("0.1");
        Assert.Null(reports[^1]);
        Assert.False(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("0.05");
        Assert.Same(committed, reports[^1]);
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Clear filters is a commit like Apply — it persists and raises the defaults
    // config — so it moves the committed config too: the event reports the
    // defaults, Apply disables, and the next edit re-opens it.
    [Fact]
    public async Task ClearFilters_CommitsTheDefaults_AndDisablesApply()
    {
        FilterConfig? committed = null;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { committed = c; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        ErrorMin(cut).Input("0.05");
        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.NotNull(committed);
        Assert.Null(committed!.ErrorMin);
        Assert.Same(committed, reports[^1]);
        Assert.True(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("0.05");
        Assert.Null(reports[^1]);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // LoadConfig stages, never commits — so staging anything other than the
    // committed config leaves the buffers matching nothing: null reported,
    // Apply re-opened.
    [Fact]
    public async Task LoadConfig_DifferingFromCommitted_ReportsNull_AndEnablesApply()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(new FilterConfig { Players = ["Magriel"] }));

        Assert.Null(reports[^1]);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // ...and staging exactly what was committed is a genuinely clean state, so
    // it is reported as one. The staged instance is a different object built
    // independently, which is the point: the comparison is FilterConfig's value
    // equality, not reference identity.
    [Fact]
    public async Task LoadConfig_OfExactlyTheCommittedConfig_ReportsClean()
    {
        FilterConfig? committed = null;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { committed = c; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        ErrorMin(cut).Input("0.9");
        Assert.Null(reports[^1]);

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(new FilterConfig { ErrorMin = 0.05 }));

        Assert.Same(committed, reports[^1]);
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Validity and cleanliness compose: both must hold for Apply to be offered.
    // Here the selection is genuinely dirty — the event says so — yet the
    // unparseable pattern text keeps Apply disabled. The panel volunteers no
    // disabled-reason line for this case: the pattern field's own
    // invalid-feedback already explains it, and repeating it here would be a
    // second encoding of the same rule.
    [Fact]
    public async Task InvalidPositionPattern_DisablesApply_EvenWhileDirty()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderExpandedReporting(reports);

        cut.Find("#positionPattern").Input("[6,2,]");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        cut.Find("#positionPattern").Input("[6,2");

        Assert.Null(reports[^1]);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // A fresh mount is silent. The first-render localStorage restore *stages* a
    // stored selection — it does not commit one — so neither event fires and
    // Apply starts enabled even though every control is populated. That is the
    // pre-existing restore contract, and the committed-config state must not
    // disturb it.
    [Fact]
    public void FreshMount_RestoringStoredConfig_RaisesNothing_AndLeavesApplyEnabled()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult(new FilterConfig { ErrorMin = 0.05 }.ToJson());

        FilterConfig? committed = null;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { committed = c; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        Assert.Equal("0.05", ErrorMin(cut).GetAttribute("value"));
        Assert.Null(committed);
        Assert.Empty(reports);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The per-gesture rationale, as a test. This panel has committed nothing,
    // so the first edit is not a *transition* from any state it knows about —
    // and yet it must report null, because the consumer on the other side may
    // have survived a remount still holding a config from the previous mount
    // and gating on it. A transition-only event would be silent here, which is
    // precisely the state where the consumer is most wrong. Don't "optimize"
    // this into firing only on change.
    [Fact]
    public void FreshMount_ThenFirstEdit_ReportsNull()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult(new FilterConfig { ErrorMin = 0.05 }.ToJson());

        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);
        Assert.Empty(reports);

        ErrorMin(cut).Input("0.09");

        Assert.Equal([null], reports);
    }

    // A second Apply on an unchanged selection commits nothing — no repeat
    // OnFilterConfigChanged, no second config write. ApplyAsync guards on
    // CanApply as well as rendering `disabled`, matching SavedFiltersPanel's
    // handler-side gates, so the contract survives an event dispatch that
    // ignores the disabled attribute.
    [Fact]
    public async Task ApplyTwiceWithoutEditing_CommitsOnlyOnce()
    {
        var commits = 0;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig _) => { commits++; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        await Apply(cut).ClickAsync(new());

        Assert.Equal(1, commits);
        Assert.Equal(2, reports.Count);
        Assert.Single(JSInterop.Invocations["localStorage.setItem"],
            i => (string?)i.Arguments[0] == ConfigKey);
    }

    // The stale-binding half of the silent-splat discipline. Razor emits an
    // unrecognized component attribute without complaint at build time, so a
    // consumer still carrying `OnFilterDirty="…"` after this panel replaced it
    // compiles green. It must not then run green: this panel deliberately
    // declares no CaptureUnmatchedValues catch-all, so the renderer rejects the
    // unmatched attribute outright. Built here through RenderTreeBuilder because
    // that is precisely what a stale Razor binding compiles down to — a named
    // AddAttribute the component has no property for. Adding a catch-all to the
    // panel would silently turn this exception back into a dead handler.
    [Fact]
    public void StaleParameterBinding_ThrowsAtRender()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Render(builder =>
        {
            builder.OpenComponent<FilterPanel>(0);
            builder.AddAttribute(1, nameof(FilterPanel.OnFilterConfigChanged),
                EventCallback.Factory.Create<FilterConfig>(this, _ => { }));
            builder.AddAttribute(2, nameof(FilterPanel.OnAppliedStateChanged),
                EventCallback.Factory.Create<FilterConfig?>(this, _ => { }));
            builder.AddAttribute(3, "OnFilterDirty", EventCallback.Empty);
            builder.CloseComponent();
        }));

        Assert.Contains("OnFilterDirty", ex.Message);
    }

    // The silent-splat discipline: a consumer that drops this binding must fail
    // at build time (RZ2012), not silently lose its gate at runtime. Both
    // in-tree consumers genuinely require it, so the attribute is part of the
    // contract, not decoration.
    [Fact]
    public void OnAppliedStateChanged_IsEditorRequired()
    {
        var property = typeof(FilterPanel).GetProperty(nameof(FilterPanel.OnAppliedStateChanged));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
        Assert.NotNull(property.GetCustomAttribute<EditorRequiredAttribute>());
    }

    // Pins the rendered control labels to the lib's [Description] text from
    // EnumLabel.ToLabel<TEnum>(). Each enum below is one whose human label
    // differs from its bare identifier — those are exactly the cases where
    // a regression to "@pt" / a local switch with stale strings would
    // previously have gone unnoticed.
    [Theory]
    [InlineData(typeof(DecisionTypeOption), "Both checker and cube")]
    [InlineData(typeof(DecisionTypeOption), "Checker plays only")]
    [InlineData(typeof(DecisionTypeOption), "Cube decisions only")]
    public void Render_LabelsUseLibDescriptions(Type enumType, string expectedLabel)
    {
        _ = enumType;  // present so failures cite the enum that caused them
        var cut = RenderExpanded();
        Assert.Contains(expectedLabel, cut.Markup);
    }

    // Position type and Play type are shelved for later reintroduction in a
    // modified form: the XgFilter_Lib machinery (FilterConfig.PositionTypes /
    // PlayTypes, the filters, the enums) stays intact, but the UI groups are
    // hidden. Assert both control groups are absent — with the disclosure
    // expanded, so absence means "not in the panel," not "behind the
    // disclosure" — so an accidental re-add, or a future deliberate
    // reintroduction, trips this test rather than shipping silently.
    [Fact]
    public void ShelvedGroups_PositionTypeAndPlayType_AreAbsentFromPanel()
    {
        var cut = RenderExpanded();

        Assert.DoesNotContain("Position type", cut.Markup);
        Assert.DoesNotContain("Play type", cut.Markup);
        Assert.Empty(cut.FindAll("input[id^='pt_']"));
        Assert.Empty(cut.FindAll("input[id^='plt_']"));
    }

    // Round-trips through the single-key persistence path: set a spread of
    // controls, Apply (which writes one xg_filter_config blob via
    // FilterConfig.ToJson), then re-mount with the captured blob fed back through
    // the getItem mock and assert the restored controls reflect what was applied.
    [Fact]
    public async Task PersistedConfig_RoundTripsAcrossRemount()
    {
        var cut = RenderExpanded();

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal, Magriel");
        cut.Find("input[type='number'][placeholder='Min']").Input("0.05");
        cut.Find("#dt_CheckerPlaysOnly").Change(true);
        cut.Find("#ct_Race").Change(true);

        await cut.Find("button.btn-primary").ClickAsync(new());

        // Pull the exact JSON the panel persisted — one blob under one key.
        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        // Feed it back through the getItem mock and mount a fresh panel.
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded();

        Assert.Equal("Hal, Magriel", restored.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.Equal("0.05", restored.Find("input[type='number'][placeholder='Min']").GetAttribute("value"));
        Assert.True(restored.Find("#dt_CheckerPlaysOnly").HasAttribute("checked"));
        Assert.True(restored.Find("#ct_Race").HasAttribute("checked"));
    }

    // Silent-splat guard for the Contact-type section: an unbound Razor checkbox
    // attribute compiles fine but never mutates state, so check a box, Apply, and
    // assert the emitted config actually carries the selection. Pins that the new
    // #ct_* checkboxes bind to FilterConfig.ContactTypes.
    [Fact]
    public async Task ContactTypeCheckbox_FlowsIntoEmittedConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#ct_Contact").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Contains(ContactType.Contact, capturedConfig!.ContactTypes);
    }

    // The three selectable AnalysisModes, in a fixed helper so every depth test
    // names the same set. Unknown is deliberately absent — no clause can name
    // it, so the panel offers no toggle for it.
    private static readonly AnalysisMode[] SelectableModes =
        [AnalysisMode.Evaluation, AnalysisMode.Rollout, AnalysisMode.BookRollout];

    // Check a mode's toggle and expand its level group through the group's real
    // disclosure button — the depth twin of ExpandMoreFilters, exercising the
    // actual wiring rather than reaching around it.
    private static void CheckModeAndExpandLevels(
        IRenderedComponent<FilterPanel> cut, AnalysisMode mode)
    {
        cut.Find($"#md_{mode}").Change(true);
        cut.Find($"#lvlToggle_{mode}").Click();
    }

    // The depth facet's mode axis: one toggle per selectable AnalysisMode, each
    // a checkbox carrying the enum's lib-owned [Description] label via
    // EnumLabel.ToLabel — anchored to the label's `for` target so a hardcoded
    // panel string can't satisfy it. Unknown gets no toggle: legacy/unstamped
    // rows are never selectable, only admitted by leaving the facet off.
    [Fact]
    public void AnalysisDepthSection_RendersOneTogglePerSelectableMode()
    {
        var cut = RenderExpanded();

        foreach (var mode in SelectableModes)
        {
            Assert.Equal("checkbox", cut.Find($"#md_{mode}").GetAttribute("type"));
            Assert.Equal(mode.ToLabel(),
                cut.Find($"label[for='md_{mode}']").TextContent.Trim());
        }

        Assert.Empty(cut.FindAll("#md_Unknown"));
        Assert.Equal(SelectableModes.Length, cut.FindAll("input[id^='md_']").Count);
    }

    // Each mode toggle discloses its own level group and only its own:
    // unchecked, the group is absent from the DOM (not styled away); checked,
    // the group's disclosure button renders; unchecking hides it again.
    [Fact]
    public void ModeToggle_ShowsAndHidesItsOwnLevelGroup()
    {
        var cut = RenderExpanded();

        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));

        cut.Find("#md_Rollout").Change(true);
        Assert.NotNull(cut.Find("#lvlToggle_Rollout"));
        Assert.Empty(cut.FindAll("#lvlToggle_Evaluation"));
        Assert.Empty(cut.FindAll("#lvlToggle_BookRollout"));

        cut.Find("#md_Rollout").Change(false);
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
    }

    // A checked mode's level group starts collapsed behind an honest disclosure
    // — a real button carrying aria-expanded / aria-controls over the
    // always-rendered region, whose checkboxes are absent from the DOM until
    // expanded (the #moreFilters idiom, one tier down).
    [Fact]
    public void LevelGroup_DefaultCollapsed_ExpandsThroughHonestDisclosure()
    {
        var cut = RenderExpanded();
        cut.Find("#md_Evaluation").Change(true);

        var toggle = cut.Find("#lvlToggle_Evaluation");
        Assert.Equal("BUTTON", toggle.TagName);
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("lvl_Evaluation", toggle.GetAttribute("aria-controls"));
        Assert.NotNull(cut.Find("#lvl_Evaluation"));
        Assert.Empty(cut.FindAll("input[id^='lv_Evaluation_']"));

        toggle.Click();
        Assert.Equal("true", cut.Find("#lvlToggle_Evaluation").GetAttribute("aria-expanded"));
        Assert.NotEmpty(cut.FindAll("input[id^='lv_Evaluation_']"));
    }

    // Exhaustive render check for one expanded level group: every AnalysisLevel
    // member surfaces as a checkbox with its lib-owned [Description] label, in
    // Enum.GetValues declaration order (the lib's ascending-rigor order — no
    // UI-side sort rule). Iterating Enum.GetValues covers a new upstream member
    // automatically, and pins Unknown as a first-class, selectable level.
    [Fact]
    public void LevelGroup_RendersEveryLevelInDeclarationOrderWithLibLabels()
    {
        var cut = RenderExpanded();
        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);

        var renderedOrder = cut.FindAll("input[id^='lv_Rollout_']")
            .Select(el => Enum.Parse<AnalysisLevel>(el.Id!["lv_Rollout_".Length..]))
            .ToArray();
        Assert.Equal(Enum.GetValues<AnalysisLevel>(), renderedOrder);

        foreach (var level in Enum.GetValues<AnalysisLevel>())
            Assert.Equal(level.ToLabel(),
                cut.Find($"label[for='lv_Rollout_{level}']").TextContent.Trim());
    }

    // While collapsed, the group's badge is its hidden-active signal: "any"
    // with no level checked (an unconstrained mode, styled neutral), "N
    // selected" otherwise (styled primary, the hidden-active idiom). While
    // expanded nothing is hidden, so no badge renders — same ruling as the
    // panel-level signal.
    [Fact]
    public void LevelBadge_ReportsAnyOrCount_OnlyWhileCollapsed()
    {
        var cut = RenderExpanded();
        cut.Find("#md_Evaluation").Change(true);

        Assert.Equal("any", cut.Find("#lvlBadge_Evaluation").TextContent.Trim());

        cut.Find("#lvlToggle_Evaluation").Click();
        Assert.Empty(cut.FindAll("#lvlBadge_Evaluation"));

        cut.Find("#lv_Evaluation_Ply4").Change(true);
        cut.Find("#lv_Evaluation_XgRollerPlusPlus").Change(true);
        cut.Find("#lvlToggle_Evaluation").Click();
        Assert.Equal("2 selected", cut.Find("#lvlBadge_Evaluation").TextContent.Trim());
    }

    // The lib's canonical example (the beta report's selection, inexpressible
    // under the old shared level set): Rollouts on with no levels + Evaluations
    // at XG Roller++. The panel must emit raw intent verbatim across all six
    // members — two toggles on, one level list carrying Roller++, one empty
    // (= any level), the third pair untouched. The clause-union derivation is
    // FilterConfig.Build()'s job; nothing about clauses is asserted here.
    [Fact]
    public async Task AnalysisDepth_CanonicalSelection_EmitsAllSixFieldsRaw()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#md_Rollout").Change(true);
        CheckModeAndExpandLevels(cut, AnalysisMode.Evaluation);
        cut.Find("#lv_Evaluation_XgRollerPlusPlus").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.True(capturedConfig!.IncludeEvaluations);
        Assert.Equal(new[] { AnalysisLevel.XgRollerPlusPlus }, capturedConfig.EvaluationLevels);
        Assert.True(capturedConfig.IncludeRollouts);
        Assert.Empty(capturedConfig.RolloutLevels);
        Assert.False(capturedConfig.IncludeBookRollouts);
        Assert.Empty(capturedConfig.BookRolloutLevels);
    }

    // Silent-splat guard for the level axis, sharpened to the per-mode
    // contract: levels checked under Book rollouts — including Unknown, the
    // deliberate opt-in for unenriched book hits — land in BookRolloutLevels
    // and only there, never in a sibling mode's list.
    [Fact]
    public async Task LevelCheckbox_FlowsIntoItsOwnModesListOnly()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        CheckModeAndExpandLevels(cut, AnalysisMode.BookRollout);
        cut.Find("#lv_BookRollout_Unknown").Change(true);
        cut.Find("#lv_BookRollout_Ply3").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Contains(AnalysisLevel.Unknown, capturedConfig!.BookRolloutLevels);
        Assert.Contains(AnalysisLevel.Ply3, capturedConfig.BookRolloutLevels);
        Assert.Equal(2, capturedConfig.BookRolloutLevels.Count);
        Assert.Empty(capturedConfig.EvaluationLevels);
        Assert.Empty(capturedConfig.RolloutLevels);
    }

    // The deliberate keep-on-untoggle behavior: unchecking a mode hides its
    // group but keeps the checked levels — in the buffer, so re-toggling
    // restores the user's selection, and in the emitted config, where the lib
    // guarantees a level list whose toggle is off is inert (no activation, no
    // constraint). An exploratory untoggle costs nothing.
    [Fact]
    public async Task LevelSelections_SurviveModeUntoggle()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);
        cut.Find("#lv_Rollout_Ply4").Change(true);

        cut.Find("#md_Rollout").Change(false);
        Assert.Empty(cut.FindAll("input[id^='lv_Rollout_']"));

        await cut.Find("button.btn-primary").ClickAsync(new());
        Assert.NotNull(capturedConfig);
        Assert.False(capturedConfig!.IncludeRollouts);
        Assert.Equal(new[] { AnalysisLevel.Ply4 }, capturedConfig.RolloutLevels);

        cut.Find("#md_Rollout").Change(true);
        Assert.True(cut.Find("#lv_Rollout_Ply4").HasAttribute("checked"));
    }

    // Every depth edit control must report applied state so the parent can
    // disable Run until Apply — and neither disclosure tier may: expanding the
    // panel's #moreFilters and expanding a level group are both navigation,
    // not edits.
    [Fact]
    public void AnalysisDepthControls_ReportAppliedState_DisclosuresDoNot()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        ExpandMoreFilters(cut);
        Assert.Empty(reports);

        cut.Find("#md_Rollout").Change(true);
        Assert.Single(reports);

        cut.Find("#lvlToggle_Rollout").Click();
        Assert.Single(reports);

        cut.Find("#lv_Rollout_Ply4").Change(true);
        Assert.Equal(2, reports.Count);

        cut.Find("#md_BookRollout").Change(true);
        Assert.Equal(3, reports.Count);
    }

    // The level-group disclosure is deliberately unpersisted — unlike the
    // panel-level disclosure with its own localStorage key, toggling a level
    // group writes nothing: the collapsed badge already carries everything the
    // closed state hides, so there is no choice worth remembering. The only
    // permitted write in this scenario is the panel disclosure's own key from
    // the RenderExpanded click.
    [Fact]
    public void LevelGroupToggle_WritesNoLocalStorage()
    {
        var cut = RenderExpanded();
        cut.Find("#md_Rollout").Change(true);

        cut.Find("#lvlToggle_Rollout").Click();
        cut.Find("#lvlToggle_Rollout").Click();

        Assert.DoesNotContain(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem" && (string?)i.Arguments[0] != DisclosureKey);
    }

    // Deselecting everything back to nothing must emit the inactive state —
    // all three toggles off with empty level lists — "facet off," not "reject
    // everything." The Build()-skip on that combination is upstream's job; the
    // panel's contract is only that it round-trips the emptied intent
    // faithfully.
    [Fact]
    public async Task AnalysisDepth_DeselectedToEmpty_EmitsInactiveState()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);
        cut.Find("#lv_Rollout_Ply3").Change(true);
        cut.Find("#lv_Rollout_Ply3").Change(false);
        cut.Find("#md_Rollout").Change(false);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.False(capturedConfig!.IncludeEvaluations);
        Assert.False(capturedConfig.IncludeRollouts);
        Assert.False(capturedConfig.IncludeBookRollouts);
        Assert.Empty(capturedConfig.EvaluationLevels);
        Assert.Empty(capturedConfig.RolloutLevels);
        Assert.Empty(capturedConfig.BookRolloutLevels);
    }

    // Clear filters must reset all six depth fields — every toggle off (which
    // also removes the level groups from the DOM) and every level list empty,
    // including levels kept inert by an earlier untoggle: Clear is the
    // full-clear gesture, so nothing survives it.
    [Fact]
    public async Task ClearFilters_ResetsAllSixDepthFields()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);
        cut.Find("#lv_Rollout_Ply4").Change(true);
        cut.Find("#md_Rollout").Change(false);   // Ply4 now kept inert
        cut.Find("#md_Evaluation").Change(true);
        cut.Find("#md_BookRollout").Change(true);

        await cut.Find("#clearFilters").ClickAsync(new());

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));

        Assert.NotNull(capturedConfig);
        Assert.False(capturedConfig!.IncludeEvaluations);
        Assert.False(capturedConfig.IncludeRollouts);
        Assert.False(capturedConfig.IncludeBookRollouts);
        Assert.Empty(capturedConfig.EvaluationLevels);
        Assert.Empty(capturedConfig.RolloutLevels);
        Assert.Empty(capturedConfig.BookRolloutLevels);
    }

    // Round-trips the depth facet through the single-key persistence path:
    // select across two mode pairs (levels under Book rollouts, Rollouts bare),
    // Apply (writes the FilterConfig blob — level lists as member-name strings,
    // toggles as booleans), then re-mount with the captured blob and assert
    // exactly that selection restores. The restored group mounts collapsed —
    // the disclosure is session state, never persisted — with its badge
    // honestly reporting the restored count before any expansion.
    [Fact]
    public async Task AnalysisDepth_RoundTripsAcrossRemount()
    {
        var cut = RenderExpanded();

        CheckModeAndExpandLevels(cut, AnalysisMode.BookRollout);
        cut.Find("#lv_BookRollout_Ply3").Change(true);
        cut.Find("#lv_BookRollout_Ply7").Change(true);
        cut.Find("#md_Rollout").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded();

        Assert.True(restored.Find("#md_BookRollout").HasAttribute("checked"));
        Assert.True(restored.Find("#md_Rollout").HasAttribute("checked"));
        Assert.False(restored.Find("#md_Evaluation").HasAttribute("checked"));

        Assert.Equal("false", restored.Find("#lvlToggle_BookRollout").GetAttribute("aria-expanded"));
        Assert.Equal("2 selected", restored.Find("#lvlBadge_BookRollout").TextContent.Trim());
        Assert.Equal("any", restored.Find("#lvlBadge_Rollout").TextContent.Trim());

        restored.Find("#lvlToggle_BookRollout").Click();
        Assert.True(restored.Find("#lv_BookRollout_Ply3").HasAttribute("checked"));
        Assert.True(restored.Find("#lv_BookRollout_Ply7").HasAttribute("checked"));
        Assert.DoesNotContain("checked", restored.Find("#lv_BookRollout_XgRoller").OuterHtml);
    }

    // Persistence back-compat: a blob saved before the depth pairs existed
    // carries none of the three toggles or level lists. TryFromJson must
    // restore the facet inactive — no toggle checked, no level group rendered —
    // which falls out of System.Text.Json leaving the initialized defaults for
    // the absent members. Verified here rather than assumed.
    [Fact]
    public void LegacyConfigWithoutDepthFields_RestoresToInactive()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\"}");

        var cut = RenderExpanded();

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
    }

    // Migration guard: blobs saved under the two retired depth shapes — the
    // flat AnalysisDepthClasses axis and the shared AnalysisLevels list —
    // carry field names no current member answers to. System.Text.Json ignores
    // them as unknown properties, so the facet restores inactive rather than
    // throwing — the accepted reset-on-read path for old saved configs.
    [Fact]
    public void ConfigWithRetiredDepthFields_IsIgnored_RestoresToInactive()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\"," +
                "\"AnalysisDepthClasses\":[\"Ply3\",\"RolloutPly7\"]," +
                "\"AnalysisLevels\":[\"Ply3\",\"XgRollerPlus\"]}");

        var cut = RenderExpanded();

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
    }

    // Canonical-order render pin for the dice facet: every roll must surface as a
    // #dr_<token> checkbox, and the checkboxes must appear in DiceRoll.All order
    // (the lib's ascending canonical order) — no UI-side roll list or sort rule.
    // Reads the rendered #dr_* inputs in DOM order, parses each id back to a
    // DiceRoll, and compares the sequence to DiceRoll.All (which is the 21 rolls).
    [Fact]
    public void DiceSection_RendersAll21RollsInCanonicalOrder()
    {
        var cut = RenderExpanded();

        var renderedOrder = cut.FindAll("input[id^='dr_']")
            .Select(el => DiceRoll.Parse(el.Id!["dr_".Length..]))
            .ToArray();

        Assert.Equal(DiceRoll.All, renderedOrder);
        Assert.Equal(21, renderedOrder.Length);
    }

    // Silent-splat guard for the dice facet (cf. the Contact-type guard): an
    // unbound Razor checkbox compiles but never mutates state, so check a spread
    // of rolls — a double and a non-double — Apply, and assert the emitted
    // DiceRolls list carries exactly them. The list is raw intent; whether it
    // becomes an active DiceRollFilter is FilterConfig.Build()'s call.
    [Fact]
    public async Task DiceRollCheckbox_FlowsIntoEmittedConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_55").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Contains(new DiceRoll(3, 1), capturedConfig!.DiceRolls);
        Assert.Contains(new DiceRoll(5, 5), capturedConfig.DiceRolls);
        Assert.Equal(2, capturedConfig.DiceRolls.Count);
    }

    // Round-trips the dice facet through the single-key persistence path: check a
    // couple of rolls, Apply (writes the FilterConfig blob — DiceRolls as
    // two-digit token strings via DiceRoll's own converter), then re-mount with
    // the captured blob and assert exactly those checkboxes restore checked. Also
    // the "pre-populated config renders checked" coverage.
    [Fact]
    public async Task DiceRolls_RoundTripsAcrossRemount()
    {
        var cut = RenderExpanded();

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_66").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded();

        Assert.True(restored.Find("#dr_31").HasAttribute("checked"));
        Assert.True(restored.Find("#dr_66").HasAttribute("checked"));
        Assert.DoesNotContain("checked", restored.Find("#dr_21").OuterHtml);
    }

    // Deselecting every checked roll back to none must emit the inactive state —
    // an empty DiceRolls list, "facet off," not "reject everything." The
    // Build()-skip on the empty list is upstream's job; the panel's contract is
    // only that it round-trips the emptied intent faithfully.
    [Fact]
    public async Task DiceRolls_DeselectedToEmpty_EmitsInactiveState()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_31").Change(false);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Empty(capturedConfig!.DiceRolls);
    }

    // Silent-splat guard for the Position-pattern field: an unbound text input
    // would compile but never feed BuildConfig, so type a valid bracket list,
    // Apply, and assert the emitted config carries the parsed BoardPattern.
    // BoardPattern has no value-equality, so compare via its round-tripping
    // ToBracketList rendering.
    [Fact]
    public async Task PositionPattern_FlowsIntoEmittedConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#positionPattern").Input("[6,2,] [5,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.NotNull(capturedConfig!.PositionPattern);
        Assert.Equal("[6,2,] [5,,-2]", capturedConfig.PositionPattern!.ToBracketList());
    }

    // The panel is where users type the grammar by hand, so pin the borne-off
    // vocabulary at the wire: an off/opp-off pattern must reach the emitted
    // config, and mixed-case names must come back canonicalized. BoardPattern
    // parses the names case-insensitively and renders them lower-case; typing
    // "OFF"/"Opp-Off" here proves the panel hands the text to TryParse verbatim
    // rather than pre-chewing (or pre-rejecting) it.
    [Fact]
    public async Task PositionPatternWithOffTokens_FlowsIntoEmittedConfigCanonicalized()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#positionPattern").Input("[OFF,10,] [Opp-Off,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.NotNull(capturedConfig!.PositionPattern);
        Assert.Equal("[off,10,] [opp-off,,-2]", capturedConfig.PositionPattern!.ToBracketList());
    }

    // A wrong-signed borne-off bound is a grammar error, not a typo the panel
    // should quietly tolerate: [off,,-2] asks for a negative count of the on-roll
    // player's borne-off checkers, which CheckerRange rejects. The lib surfaces
    // that through TryParse like any malformed token, so the panel must land it
    // in the same invalid-field state — proving the gate keys on "does it parse,"
    // not on a local shape check that only catches unbalanced brackets.
    [Fact]
    public void WrongSignedOffBound_MarksFieldAndGatesApply()
    {
        var cut = RenderExpanded();

        cut.Find("#positionPattern").Input("[off,,-2]");

        Assert.Contains("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    // Round-trips the Position-pattern field through the single-key persistence
    // path: set a pattern, Apply (writes the FilterConfig blob, PositionPattern
    // serialized as its bracket list by BoardPatternJsonConverter), then re-mount
    // with the captured blob and assert the field shows the restored bracket list.
    [Fact]
    public async Task PositionPattern_RoundTripsAcrossRemount()
    {
        var cut = RenderExpanded();

        cut.Find("#positionPattern").Input("[6,2,] [5,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded();

        Assert.Equal("[6,2,] [5,,-2]", restored.Find("#positionPattern").GetAttribute("value"));
    }

    // Blank Position-pattern field means "no pattern filter," which must surface
    // as a null PositionPattern (not an empty pattern), per FilterConfig's
    // null-or-empty contract.
    [Fact]
    public async Task EmptyPositionPattern_EmitsNullPattern()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Null(capturedConfig!.PositionPattern);
    }

    // Invalid bracket-list text must not silently drop the filter: the chosen
    // UX marks the field invalid and gates Apply (disabled) until it parses or
    // is cleared. Clearing the bad text re-enables Apply.
    [Fact]
    public void InvalidPositionPattern_MarksFieldAndGatesApply()
    {
        var cut = RenderExpanded();

        cut.Find("#positionPattern").Input("[6,2");

        Assert.Contains("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));

        cut.Find("#positionPattern").Input(string.Empty);

        Assert.DoesNotContain("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.False(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    // Proves the FilterConfig.TryFromJson tolerant path is wired: a corrupt blob
    // in storage must restore to defaults rather than throw.
    [Fact]
    public void CorruptStoredConfig_MountsWithDefaults()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult("}{ not valid json");

        var cut = RenderExpanded();

        Assert.Equal(string.Empty, cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.True(cut.Find("#dt_Both").HasAttribute("checked"));
        Assert.DoesNotContain("checked", cut.Find("#ct_Race").OuterHtml);
    }

    // The match-score field must state the MaNa convention in a sibling hint line
    // (same form-text idiom as the position-pattern section), so a user reading
    // the panel knows scores are on-roll-anchored — 4a5a and 5a4a are distinct —
    // rather than assuming the old unordered semantics and re-filing the bug the
    // lib now enforces against.
    [Fact]
    public void MatchScoreSection_RendersOnRollAnchoredHint()
    {
        var cut = RenderExpanded();

        // Anchor to the match-score section's own hint, not just page markup,
        // so an unrelated mention of the convention elsewhere can't satisfy this.
        var section = cut.Find("input[placeholder^='e.g. 4a5a']").ParentElement!;
        var hint = section.QuerySelector(".form-text")!;

        Assert.Contains("on-roll-anchored", hint.TextContent);
        // Both orientations are named — the whole point is that they differ.
        Assert.Contains("4a5a", hint.TextContent);
        Assert.Contains("5a4a", hint.TextContent);
    }

    // The old placeholder taught "DMP", which neither the old nor the new
    // tokenizer accepts — typing it throws on Apply. Pin that the placeholder
    // advertises only the natural vocabulary (DMP's equivalent, 1a1a, belongs in
    // the hint line, not as an un-parseable example).
    [Fact]
    public void MatchScorePlaceholder_DoesNotAdvertiseInvalidDmpToken()
    {
        var cut = RenderExpanded();

        var placeholder = cut.Find("input[placeholder^='e.g. 4a5a']").GetAttribute("placeholder")!;

        Assert.DoesNotContain("DMP", placeholder);
    }

    // Cross-lib invariant (XgFilter_Lib is a dependency): every example token the
    // placeholder advertises must survive FilterConfig.Build() — the same score
    // parsing the panel's Apply path runs, reached through the lib's intent
    // surface rather than its internal filter types. Build() fails loud on any
    // token the parser rejects, so this pins "the UI never advertises an example
    // the lib rejects" as a standing invariant rather than a one-time fix — a
    // future placeholder edit that reintroduces a DMP-style un-parseable example
    // trips here.
    [Fact]
    public void MatchScorePlaceholder_ExampleTokensAllParse()
    {
        var cut = RenderExpanded();

        var placeholder = cut.Find("input[placeholder^='e.g. 4a5a']").GetAttribute("placeholder")!;
        var examples = placeholder
            .Replace("e.g. ", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.NotEmpty(examples);
        // Build() is the lib's Apply-time validation path.
        var cfg = new FilterConfig { MatchScores = [.. examples] };
        Assert.Null(Record.Exception(() => cfg.Build()));
    }

    // LoadConfig is staging-only: it projects the config into the edit buffers
    // like a bulk edit gesture. Edit-side signaling fires (OnAppliedStateChanged,
    // once — the staged state equals no committed config), but no Apply-side
    // effect may occur: no OnFilterConfigChanged, no config write. The expansion after
    // the load writes the disclosure's own key, which is exactly why the
    // no-write assertion is keyed to ConfigKey — the config blob is what
    // staging must never touch.
    [Fact]
    public async Task LoadConfig_HydratesBuffers_WithoutApplySideEffects()
    {
        FilterConfig? capturedConfig = null;
        var reports = new List<FilterConfig?>();
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; })
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

        var loaded = new FilterConfig
        {
            Players = ["Magriel"],
            DecisionType = DecisionTypeOption.CubeOnly,
            ContactTypes = [ContactType.Race],
        };
        await cut.InvokeAsync(() => cut.Instance.LoadConfig(loaded));
        ExpandMoreFilters(cut);

        Assert.Equal("Magriel", cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.True(cut.Find("#dt_CubeOnly").HasAttribute("checked"));
        Assert.True(cut.Find("#ct_Race").HasAttribute("checked"));

        Assert.Null(capturedConfig);
        Assert.Equal([null], reports);
        Assert.DoesNotContain(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem" && (string?)i.Arguments[0] == ConfigKey);
    }

    // Reproduces, deterministically, the interleaving the post-await guard in
    // the config restore exists for: the first-render restore is suspended at
    // its getItem await when the host's LoadConfig runs. A Setup with no
    // SetResult holds the interop task open — the restore parks on it — then
    // LoadConfig stages Y, then SetResult releases the restore with X. The
    // resumed continuation must yield, not clobber: Y's values survive.
    [Fact]
    public async Task LoadConfig_DuringPendingStoredRestore_TakesPrecedence()
    {
        var pendingGet = JSInterop.Setup<string?>("localStorage.getItem", ConfigKey);

        var cut = Render<FilterPanel>();

        var loaded = new FilterConfig { Players = ["Hal"] };
        await cut.InvokeAsync(() => cut.Instance.LoadConfig(loaded));

        var storedConfig = new FilterConfig { Players = ["Magriel"] };
        pendingGet.SetResult(storedConfig.ToJson());

        // WaitForAssertion: the released continuation resumes asynchronously
        // relative to SetResult; only after it has run is "didn't clobber"
        // actually proven.
        ExpandMoreFilters(cut);
        cut.WaitForAssertion(() => Assert.Equal(
            "Hal",
            cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value")));
    }

    // Save-as must capture the live buffers, not the last-applied config —
    // the whole point is saving while dirty, before (or instead of) Apply.
    [Fact]
    public void TryGetEditedConfig_UnappliedEdits_ReturnsLiveBuffers()
    {
        var cut = RenderExpanded();

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal");
        cut.Find("#ct_Race").Change(true);

        Assert.True(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Equal(["Hal"], cfg!.Players);
        Assert.Contains(ContactType.Race, cfg.ContactTypes);
    }

    // The one state Apply refuses — non-blank, unparseable position-pattern
    // text — is exactly the state TryGetEditedConfig refuses. Same gate,
    // same build path.
    [Fact]
    public void TryGetEditedConfig_InvalidPositionPattern_ReturnsFalseNull()
    {
        var cut = RenderExpanded();

        cut.Find("#positionPattern").Input("[6,2");

        Assert.False(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Null(cfg);
    }

    // Pins the deliberate Apply-parity contract: TryGetEditedConfig is no
    // stricter than Apply. Match-score text rides raw through both paths and
    // is validated only downstream in FilterConfig.Build() — a config you
    // could Apply is always a config you can save.
    [Fact]
    public void TryGetEditedConfig_MirrorsApplyGate_RawMatchScoreTextPasses()
    {
        var cut = RenderExpanded();

        cut.Find("input[placeholder^='e.g. 4a5a']").Input("not-a-score");

        Assert.True(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Contains("not-a-score", cfg!.MatchScores);
    }

    // ── Disclosure ─────────────────────────────────────────────────────────

    // The default-hidden information hierarchy: at rest the panel shows the
    // error-range section, the disclosure toggle, and the two buttons —
    // every other section's controls are absent from the DOM, not styled
    // away. The toggle is an honest disclosure control: a real button
    // carrying aria-expanded and aria-controls.
    [Fact]
    public void Disclosure_DefaultHidden_OnlyErrorRangeToggleAndButtonsAtRest()
    {
        var cut = Render<FilterPanel>();

        var toggle = cut.Find("#moreFiltersToggle");
        Assert.Equal("BUTTON", toggle.TagName);
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("moreFilters", toggle.GetAttribute("aria-controls"));
        Assert.NotNull(cut.Find("#moreFilters"));

        Assert.NotNull(cut.Find("input[type='number'][placeholder='Min']"));
        Assert.NotNull(cut.Find("button.btn-primary"));
        Assert.NotNull(cut.Find("#clearFilters"));

        Assert.Empty(cut.FindAll("input[placeholder='e.g. Hal, Magriel']"));
        Assert.Empty(cut.FindAll("input[id^='dt_']"));
        Assert.Empty(cut.FindAll("input[placeholder^='e.g. 4a5a']"));
        Assert.Empty(cut.FindAll("input[id^='ct_']"));
        Assert.Empty(cut.FindAll("input[id^='md_']"));
        Assert.Empty(cut.FindAll("input[id^='dr_']"));
        Assert.Empty(cut.FindAll("#positionPattern"));
    }

    // The toggle round-trips: expand shows the hidden sections and flips
    // aria-expanded; a second click collapses back to the at-rest state.
    [Fact]
    public void DisclosureToggle_ExpandsAndCollapses()
    {
        var cut = Render<FilterPanel>();

        ExpandMoreFilters(cut);
        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("#positionPattern"));
        Assert.NotNull(cut.Find("input[id^='dr_']"));

        cut.Find("#moreFiltersToggle").Click();
        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("#positionPattern"));
        Assert.Empty(cut.FindAll("input[id^='dr_']"));
    }

    // Toggling the disclosure is navigation, not an edit: OnAppliedStateChanged
    // must not fire, in either direction.
    [Fact]
    public void DisclosureToggle_DoesNotReportAppliedState()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        cut.Find("#moreFiltersToggle").Click();
        cut.Find("#moreFiltersToggle").Click();

        Assert.Empty(reports);
    }

    // Each toggle click persists the choice immediately under the disclosure's
    // own key — "true"/"false" literals — and never writes the config blob's
    // key: visibility is user preference, not filter state.
    [Fact]
    public void DisclosureToggle_PersistsChoiceUnderOwnKey()
    {
        var cut = Render<FilterPanel>();

        cut.Find("#moreFiltersToggle").Click();
        Assert.Equal("true", JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == DisclosureKey).Arguments[1] as string);

        cut.Find("#moreFiltersToggle").Click();
        Assert.Equal("false", JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == DisclosureKey).Arguments[1] as string);

        Assert.DoesNotContain(JSInterop.Invocations["localStorage.setItem"],
            i => (string?)i.Arguments[0] == ConfigKey);
    }

    // The remembered choice restores across sessions: a stored "true" mounts
    // the panel expanded, no click needed.
    [Fact]
    public void StoredDisclosureTrue_MountsExpanded()
    {
        JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey).SetResult("true");

        var cut = Render<FilterPanel>();

        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("#positionPattern"));
    }

    // Anything but the literal "true" — a corrupt value included — keeps the
    // default-hidden posture; the tolerant-restore twin of
    // CorruptStoredConfig_MountsWithDefaults.
    [Fact]
    public void StoredDisclosureCorrupt_MountsCollapsed()
    {
        JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey).SetResult("expanded!!");

        var cut = Render<FilterPanel>();

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("#positionPattern"));
    }

    // The disclosure twin of LoadConfig_DuringPendingStoredRestore: a toggle
    // click landing while the getItem interop is in flight is a fresh user
    // choice the late restore must not clobber. Expand-then-collapse while a
    // stored "true" is pending; the released restore must yield — the panel
    // stays collapsed.
    [Fact]
    public void Toggle_DuringPendingStoredRestore_UserChoiceWins()
    {
        var pendingGet = JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey);

        var cut = Render<FilterPanel>();

        cut.Find("#moreFiltersToggle").Click();   // expand…
        cut.Find("#moreFiltersToggle").Click();   // …and collapse: a settled choice

        pendingGet.SetResult("true");

        cut.WaitForAssertion(() => Assert.Equal(
            "false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
    }

    // ── Hidden-active signal ───────────────────────────────────────────────

    // Nothing active, nothing signalled.
    [Fact]
    public void HiddenActiveSignal_AbsentOnDefaults()
    {
        var cut = Render<FilterPanel>();

        Assert.Empty(cut.FindAll("#hiddenActiveCount"));
        Assert.Empty(cut.FindAll("#hiddenActiveNames"));
    }

    // ErrorRange is the one always-visible facet — active error bounds must
    // never light the hidden-active signal.
    [Fact]
    public void HiddenActiveSignal_ErrorRangeExcluded()
    {
        var cut = Render<FilterPanel>();

        cut.Find("input[type='number'][placeholder='Min']").Input("0.05");

        Assert.Empty(cut.FindAll("#hiddenActiveCount"));
        Assert.Empty(cut.FindAll("#hiddenActiveNames"));
    }

    // Staged values in hidden sections light the signal the moment the panel
    // collapses — before any Apply, because the signal reads the live edit
    // buffers through the same build path Apply uses (the deliberate mid-edit
    // choice). The names are the lib's FilterFacet [Description] labels, in
    // declaration order — exactly the section headings the user will find on
    // expanding.
    [Fact]
    public void HiddenActiveSignal_CountsAndNamesHiddenFacets()
    {
        var cut = RenderExpanded();

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal");
        cut.Find("#ct_Race").Change(true);
        cut.Find("#dr_31").Change(true);
        cut.Find("#moreFiltersToggle").Click();   // collapse

        Assert.Equal("3", cut.Find("#hiddenActiveCount").TextContent.Trim());
        Assert.Contains(
            string.Join(", ",
                FilterFacet.Players.ToLabel(),
                FilterFacet.ContactTypes.ToLabel(),
                FilterFacet.DiceRolls.ToLabel()),
            cut.Find("#hiddenActiveNames").TextContent);
    }

    // While expanded nothing is hidden, so the signal would be noise — it
    // renders only while collapsed.
    [Fact]
    public void HiddenActiveSignal_NotRenderedWhileExpanded()
    {
        var cut = RenderExpanded();

        cut.Find("#ct_Race").Change(true);

        Assert.Empty(cut.FindAll("#hiddenActiveCount"));
        Assert.Empty(cut.FindAll("#hiddenActiveNames"));
    }

    // A loaded saved filter can stage values into hidden sections. The signal
    // must report them at rest — and staging must not move the disclosure:
    // expanding is the user's gesture, never LoadConfig's.
    [Fact]
    public async Task LoadConfig_StagedHiddenFacets_LightSignal_WithoutExpanding()
    {
        var cut = Render<FilterPanel>();

        var loaded = new FilterConfig
        {
            ContactTypes = [ContactType.Race],
            DiceRolls = [new DiceRoll(3, 1)],
        };
        await cut.InvokeAsync(() => cut.Instance.LoadConfig(loaded));

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Equal("2", cut.Find("#hiddenActiveCount").TextContent.Trim());
        var names = cut.Find("#hiddenActiveNames").TextContent;
        Assert.Contains(FilterFacet.ContactTypes.ToLabel(), names);
        Assert.Contains(FilterFacet.DiceRolls.ToLabel(), names);
    }

    // A restored session with hidden-section facets active shows the signal at
    // rest — the first-render restore hydrates the buffers the signal reads.
    [Fact]
    public void StoredConfigWithHiddenFacets_LightsSignalAtRest()
    {
        var stored = new FilterConfig { ContactTypes = [ContactType.Race] };
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored.ToJson());

        var cut = Render<FilterPanel>();

        Assert.Equal("1", cut.Find("#hiddenActiveCount").TextContent.Trim());
        Assert.Contains(FilterFacet.ContactTypes.ToLabel(),
            cut.Find("#hiddenActiveNames").TextContent);
    }

    // Clear filters empties every buffer, so the signal goes out with them.
    [Fact]
    public async Task ClearFilters_ExtinguishesSignal()
    {
        var cut = RenderExpanded();

        cut.Find("#ct_Race").Change(true);
        cut.Find("#moreFiltersToggle").Click();   // collapse
        Assert.NotNull(cut.Find("#hiddenActiveCount"));

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.Empty(cut.FindAll("#hiddenActiveCount"));
        Assert.Empty(cut.FindAll("#hiddenActiveNames"));
    }

    // ── Clear filters contract ─────────────────────────────────────────────

    // The control says what the gesture does; the old Reset label is gone.
    [Fact]
    public void ClearButton_IsLabeledClearFilters()
    {
        var cut = Render<FilterPanel>();

        Assert.Equal("Clear filters", cut.Find("#clearFilters").TextContent.Trim());
        Assert.DoesNotContain("Reset", cut.Markup);
    }

    // Clearing raises the empty config, judged by the lib's own predicates —
    // GetActiveFacets() empty — never by re-inspecting config fields here.
    [Fact]
    public async Task ClearFilters_RaisesEmptyConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal");
        cut.Find("#ct_Race").Change(true);
        cut.Find("input[type='number'][placeholder='Min']").Input("0.05");

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Empty(capturedConfig!.GetActiveFacets());
    }

    // Clearing touches filter values only: the disclosure stays exactly where
    // the user put it — expanded stays expanded…
    [Fact]
    public async Task ClearFilters_LeavesExpandedDisclosureExpanded()
    {
        var cut = RenderExpanded();

        cut.Find("#ct_Race").Change(true);
        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.False(cut.Find("#ct_Race").HasAttribute("checked"));
    }

    // …and collapsed stays collapsed, even when the cleared values lived in
    // hidden sections (staged via LoadConfig, so the panel was never expanded).
    [Fact]
    public async Task ClearFilters_LeavesCollapsedDisclosureCollapsed()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(
            new FilterConfig { ContactTypes = [ContactType.Race] }));

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("#hiddenActiveCount"));
    }

    // The gesture's whole persisted side-effect surface is one write: the
    // empty config blob under ConfigKey. No disclosure-key write — and host
    // state (e.g. BgQuiz's picked folder) is structurally out of reach: the
    // panel has no parameter or interop path to any; the raised config is its
    // only channel to the host.
    [Fact]
    public async Task ClearFilters_WritesOnlyTheConfigKey()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(
            new FilterConfig { ContactTypes = [ContactType.Race] }));

        await cut.Find("#clearFilters").ClickAsync(new());

        var setKey = Assert.Single(JSInterop.Invocations["localStorage.setItem"]
            .Select(i => (string?)i.Arguments[0]).Distinct());
        Assert.Equal(ConfigKey, setKey);
    }
}
