using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using BgDataTypes_Lib;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components.Internal;

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

    // The three localStorage keys the panel persists under: the whole
    // FilterConfig as one serialized blob, the set of expanded facet rows, and
    // whether the container over those rows is open — the last two user
    // preference, deliberately outside the config blob.
    private const string ConfigKey = "xg_filter_config";
    private const string DisclosureKey = "xg_expandedFilters";
    private const string MoreFiltersKey = "xg_moreFiltersOpen";

    // The two words the container's toggle wears, and which fold each belongs
    // to — the user's ruling (halheinrich/backgammon#239). Independent
    // literals rather than a read of the panel's own constants, RowFacets'
    // posture for RowFacets' reason: what the control is CALLED is the
    // ruling, so respelling it has to be ruled on here rather than followed
    // silently — a pin that rendered the constant would agree with whatever
    // word the panel chose next. The survey at the foot of this file is what
    // keeps these the only other copies in the tree.
    private const string FoldedLabel = "More filters";
    private const string ExpandedLabel = "Fewer filters";

    // Every facet the panel gives a row, in render order. An independent
    // literal rather than a projection of anything the panel exposes: the
    // membership and the order are the ruling (halheinrich/backgammon#193), so
    // a facet gaining or losing a row must be ruled on here rather than
    // followed silently — the same posture RuledLevelVocabulary keeps one tier
    // down.
    private static readonly FilterFacet[] RowFacets =
    [
        FilterFacet.Players,
        FilterFacet.DecisionType,
        FilterFacet.MatchScores,
        FilterFacet.MoveNumberRange,
        FilterFacet.ContactTypes,
        FilterFacet.AnalysisDepth,
        FilterFacet.DiceRolls,
        FilterFacet.PositionPattern,
    ];

    // The rows are folded behind the More filters container
    // (halheinrich/backgammon#231) and the container is folded at rest, so a
    // test that reaches any row — its toggle, its badge or its controls —
    // opens the container first. Through the container's real toggle, like the
    // rows' own, so every such test exercises the disclosure's actual wiring
    // rather than reaching around it. Idempotent by the aria-expanded read,
    // because the button is a toggle: a second call on an open container would
    // fold it again, and a stored preference can mount it open.
    // Waiting on aria-expanded rather than returning straight from the click is
    // what makes this an open rather than a dispatch: the handler persists the
    // choice through interop before the render that puts the rows in the DOM
    // lands, so a Find for a row on the next line can outrun it. The read is
    // also the assertion the hosts' own row helpers make for the same reason.
    private static void OpenMoreFilters(IRenderedComponent<FilterPanel> cut)
    {
        if (cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded") == "true") return;

        cut.Find("#moreFiltersToggle").Click();
        cut.WaitForAssertion(() => Assert.Equal(
            "true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
    }

    // Every row is collapsed at rest, so a test that touches a facet's
    // controls opens that facet's row first — through the row's real toggle
    // button, so every such test also exercises the disclosure's actual wiring
    // rather than reaching around it. Each test names exactly the rows its
    // subject lives in: opening all eight everywhere would hide a control that
    // had quietly moved to another row. The container above them is opened
    // here too: a row's toggle is not in the DOM until it is.
    private static void ExpandFacets(
        IRenderedComponent<FilterPanel> cut, params FilterFacet[] facets)
    {
        OpenMoreFilters(cut);

        foreach (var facet in facets)
            cut.Find($"#facetToggle_{facet}").Click();
    }

    // Render-and-open, for the many tests whose subject controls live inside a
    // row.
    private IRenderedComponent<FilterPanel> RenderExpanded(params FilterFacet[] facets)
    {
        var cut = Render<FilterPanel>();
        ExpandFacets(cut, facets);
        return cut;
    }

    private IRenderedComponent<FilterPanel> RenderExpanded(
        Action<ComponentParameterCollectionBuilder<FilterPanel>> parameters,
        params FilterFacet[] facets)
    {
        var cut = Render<FilterPanel>(parameters);
        ExpandFacets(cut, facets);
        return cut;
    }

    // Render with the applied-state channel captured into `reports`. A list,
    // not a single field: OnAppliedStateChanged is per-gesture by contract, so
    // how many times it fired is as much a part of the assertion as what it
    // carried.
    private IRenderedComponent<FilterPanel> RenderReporting(List<FilterConfig?> reports) =>
        Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnAppliedStateChanged, (FilterConfig? c) => { reports.Add(c); }));

    private IRenderedComponent<FilterPanel> RenderExpandedReporting(
        List<FilterConfig?> reports, params FilterFacet[] facets)
    {
        var cut = RenderReporting(reports);
        ExpandFacets(cut, facets);
        return cut;
    }

    // The two controls these tests drive to make the panel dirty and clean
    // again — the always-visible Error-range Min box and the Apply button.
    // Keyed by id, like every other handle here: the panel has two range
    // facets and they carry the same Min/Max placeholders, so a placeholder
    // selector would resolve by document order and silently follow whichever
    // section renders first.
    private static IElement ErrorMin(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("#errorMin");

    private static IElement ErrorMax(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("#errorMax");

    // The other range facet's pair, behind the disclosure.
    private static IElement MoveNumberMin(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("#moveNumberMin");

    private static IElement MoveNumberMax(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("#moveNumberMax");

    private static IElement Apply(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("button.btn-primary");

    // The match-score box, behind the disclosure. Selected by the stable head
    // of its placeholder rather than the whole string, so the example tokens it
    // advertises stay free to change — they did, when the money token split by
    // the Jacoby rule (halheinrich/backgammon#121) — without re-keying every
    // test that types here.
    private static IElement MatchScores(IRenderedComponent<FilterPanel> cut) =>
        cut.Find("input[placeholder^='e.g. 4a5a']");

    // The example tokens the match-score placeholder advertises, one entry per
    // example — the unit both the "every example parses" pin and the
    // "the alias is offered" pin reason about.
    private static string[] PlaceholderExamples(IRenderedComponent<FilterPanel> cut) =>
        MatchScores(cut).GetAttribute("placeholder")!
            .Replace("e.g. ", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // The match-score field's rendered verdicts, one entry per voice, with
    // incidental markup whitespace collapsed. A list, not a string: the panel
    // words a malformed token and a retired one differently, and how many
    // voices spoke is as much a part of the assertion as what they said.
    private static string[] MatchScoreVerdicts(IRenderedComponent<FilterPanel> cut) =>
        cut.FindAll("#matchScoreFeedback span")
           .Select(e => Regex.Replace(e.TextContent.Trim(), @"\s+", " "))
           .ToArray();

    // The words of a rendered line that the grammar calls retired vocabulary —
    // the oracle for "this copy offers no retired spelling". A literal absence
    // check cannot serve here: the retired token's spelling is a prefix of both
    // live ones, so `DoesNotContain("money")` would fail on the very tokens the
    // copy is supposed to advertise. Asking MatchScoreToken.GetFault word by
    // word puts the question to the same grammar the panel renders from, and
    // needs no literal at all.
    private static string[] RetiredWordsIn(string text) =>
        Regex.Split(text, "[^A-Za-z0-9]+")
             .Where(w => w.Length > 0
                      && MatchScoreToken.GetFault(w) == MatchScoreTokenFault.Retired)
             .ToArray();

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

    // ForgetCommitted is the programmatic stand-in for the remount that makes
    // "a new folder re-enables Apply" fall out for free: a host (via the
    // composite) that keeps the panel mounted across a source change drops the
    // committed config instead. Apply re-arms, the event re-reports through
    // the normal path — necessarily null, nothing is committed any more — and
    // the buffers stay exactly as the user left them.
    [Fact]
    public async Task ForgetCommitted_ReArmsApply_ReportsNull_LeavesBuffersUntouched()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Equal(2, reports.Count); // the edit's null, the commit's config

        await cut.InvokeAsync(() => cut.Instance.ForgetCommitted());

        // One more report, through the normal applied-state path.
        Assert.Equal(3, reports.Count);
        Assert.Null(reports[^1]);
        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
        // The buffers are untouched — the user's selection stays staged.
        Assert.Equal("0.05", ErrorMin(cut).GetAttribute("value"));
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
        var cut = RenderExpandedReporting(reports, FilterFacet.PositionPattern);

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
    // CanApply as well as rendering `disabled`, matching NamedEntriesPanel's
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
        var cut = RenderExpanded(FilterFacet.DecisionType);
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
        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.MatchScores, FilterFacet.MoveNumberRange, FilterFacet.ContactTypes, FilterFacet.AnalysisDepth, FilterFacet.DiceRolls, FilterFacet.PositionPattern);

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
        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal, Magriel");
        cut.Find("#errorMin").Input("0.05");
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
        var restored = RenderExpanded(FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

        Assert.Equal("Hal, Magriel", restored.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.Equal("0.05", restored.Find("#errorMin").GetAttribute("value"));
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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.ContactTypes);

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
    // disclosure button — the depth twin of ExpandFacets, exercising the
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
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

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
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

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
    // expanded (the facet rows' idiom, one tier down).
    [Fact]
    public void LevelGroup_DefaultCollapsed_ExpandsThroughHonestDisclosure()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);
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

    // The ruled level vocabulary in its ruled order — member token paired
    // with the [Description] label the user actually reads — written out as
    // an independent literal. This is deliberately a second copy of
    // BgDataTypes_Lib's AnalysisLevel declaration rather than a projection of
    // it: an expectation built from Enum.GetValues is a tautology that moves
    // with the producer and can never fail, which is precisely how the
    // interleaved reordering and the new Ply3Red member reached this panel's
    // user-visible surface unpinned (halheinrich/backgammon#159). The order is
    // XG's own analysis-level menu — the ply family and the XG Roller family
    // interleave, they are not two separate blocks — with Unknown at the head,
    // outside the rigor scale rather than least-rigorous. A producer-side
    // rename, reorder, relabel, insertion or removal breaks this literal, and
    // that is the point: each of those changes what the user sees, so each
    // must be ruled on here rather than followed silently.
    private static readonly (string Member, string Label)[] RuledLevelVocabulary =
    [
        ("Unknown",          "Unknown"),
        ("Ply1",             "1-ply"),
        ("Ply2",             "2-ply"),
        ("Ply3Red",          "3-ply Red"),
        ("Ply3",             "3-ply"),
        ("XgRoller",         "XG Roller"),
        ("Ply4",             "4-ply"),
        ("XgRollerPlus",     "XG Roller+"),
        ("Ply5",             "5-ply"),
        ("Ply6",             "6-ply"),
        ("Ply7",             "7-ply"),
        ("XgRollerPlusPlus", "XG Roller++"),
    ];

    // Every mode's expanded level group renders exactly that vocabulary, in
    // exactly that order, labelled exactly that way. Checked per mode group,
    // not once for a representative mode: each mode owns its own list, so a
    // group that fell out of step with its siblings would be invisible to a
    // single-mode check. Sequence equality also pins the membership — an extra
    // or missing checkbox fails here too.
    //
    // Unknown is pinned present, selectable and first. That is current truth
    // and it reads as deliberate: the panel's loop is unfiltered, and "level
    // not recorded" is a real thing to filter for — a book hit whose levels
    // the book database could not supply carries BookRollout + Unknown as the
    // producer's graceful-degradation stamp, which
    // LevelCheckbox_FlowsIntoItsOwnModesListOnly exercises as a deliberate
    // opt-in. It is not the AnalysisMode treatment, where Unknown gets no
    // toggle at all: no clause can name an unknown mode, but a clause can
    // name an unknown level.
    [Fact]
    public void LevelGroup_RendersTheRuledVocabularyInTheRuledOrder()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        foreach (var mode in SelectableModes)
        {
            CheckModeAndExpandLevels(cut, mode);

            Assert.Equal(
                RuledLevelVocabulary.Select(v => v.Member),
                cut.FindAll($"input[id^='lv_{mode}_']")
                   .Select(el => el.Id![$"lv_{mode}_".Length..]));

            foreach (var (member, label) in RuledLevelVocabulary)
                Assert.Equal(label,
                    cut.Find($"label[for='lv_{mode}_{member}']").TextContent.Trim());
        }
    }

    // While collapsed, the group's badge says what the closed state hides:
    // "any" with no level checked (an unconstrained mode, styled neutral), "N
    // selected" otherwise (styled primary, the row-badge idiom). While
    // expanded nothing is hidden, so no badge renders — the same ruling the
    // facet rows keep one tier up.
    [Fact]
    public void LevelBadge_ReportsAnyOrCount_OnlyWhileCollapsed()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);
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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.AnalysisDepth);

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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.AnalysisDepth);

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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.AnalysisDepth);

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
    // opening the facet's own row and expanding a level group are both
    // navigation, not edits.
    [Fact]
    public void AnalysisDepthControls_ReportAppliedState_DisclosuresDoNot()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        ExpandFacets(cut, FilterFacet.AnalysisDepth);
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

    // The level-group disclosure is deliberately unpersisted — unlike the two
    // disclosures above it, each with its own localStorage key, toggling a
    // level group writes nothing: the collapsed badge already carries
    // everything the closed state hides, so there is no choice worth
    // remembering. The only permitted writes in this scenario are those two
    // keys, from the clicks RenderExpanded needs to reach the row.
    [Fact]
    public void LevelGroupToggle_WritesNoLocalStorage()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);
        cut.Find("#md_Rollout").Change(true);

        cut.Find("#lvlToggle_Rollout").Click();
        cut.Find("#lvlToggle_Rollout").Click();

        Assert.DoesNotContain(JSInterop.Invocations, i =>
            i.Identifier == "localStorage.setItem"
            && (string?)i.Arguments[0] != DisclosureKey
            && (string?)i.Arguments[0] != MoreFiltersKey);
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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.AnalysisDepth);

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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.AnalysisDepth);

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
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

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
        var restored = RenderExpanded(FilterFacet.AnalysisDepth);

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

    // Ply3Red through the same persistence path, both directions, against a
    // literal wire token. It is the newest member of the level vocabulary
    // (halheinrich/backgammon#159) and the one a serializer or producer change
    // is likeliest to drop or fold away, so pin it by name: check it, Apply,
    // and assert the blob literally carries the string "Ply3Red" — the member
    // name FilterConfig's canonical options write, re-typed here on purpose
    // rather than read back off the enum — then remount from that blob and assert
    // the checkbox returns checked under its own "3-ply Red" label.
    //
    // The closing assertion is the one that matters most: Ply3 must come back
    // unchecked. XG's "3-ply Red" was a label variant of Ply3 before the
    // interleave gave it its own identity, and a regression that folded the
    // two back together would round-trip perfectly while quietly widening the
    // user's filter.
    [Fact]
    public async Task AnalysisDepth_Ply3Red_RoundTripsUnderItsOwnWireToken()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.Evaluation);
        cut.Find("#lv_Evaluation_Ply3Red").Change(true);
        await Apply(cut).ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);
        Assert.Contains("\"Ply3Red\"", stored!);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded(FilterFacet.AnalysisDepth);

        restored.Find("#lvlToggle_Evaluation").Click();
        Assert.True(restored.Find("#lv_Evaluation_Ply3Red").HasAttribute("checked"));
        Assert.Equal("3-ply Red",
            restored.Find("label[for='lv_Evaluation_Ply3Red']").TextContent.Trim());
        Assert.DoesNotContain("checked", restored.Find("#lv_Evaluation_Ply3").OuterHtml);
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

        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
    }

    // The consumer half of halheinrich/backgammon#164, wire-tested here rather
    // than assumed from the producer's own suite: a stored blob whose level is
    // a numeric ordinal must not restore a filter. XgFilter_Lib tightened
    // FilterConfig's canonical options to reject ordinals, so TryFromJson
    // answers false and the panel keeps its defaults — the same
    // reset-on-unreadable path the retired-field blobs above take.
    //
    // Why this matters at the panel and not only in the lib: 5 is XgRoller's
    // number today and was Ply4's before Ply3Red was inserted into the ladder.
    // Honouring the ordinal would silently restore a level the user never
    // chose, which reads as a working filter rather than as corruption.
    [Fact]
    public void ConfigWithOrdinalLevel_IsRejected_RestoresToInactive()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\",\"IncludeEvaluations\":true," +
                "\"EvaluationLevels\":[5]}");

        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        // Byte-identical to ConfigWithNamedLevel_Restores below except for the
        // one token, so this pair discriminates: that blob renders the toggle,
        // this one must not. IncludeEvaluations is set precisely so the
        // assertion would FAIL if the ordinal were honoured.
        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
    }

    // The same blob with the level spelled as its member name DOES restore, so
    // the rejection above is about token kind and not a broken read path.
    [Fact]
    public void ConfigWithNamedLevel_Restores()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\",\"IncludeEvaluations\":true," +
                "\"EvaluationLevels\":[\"XgRoller\"]}");

        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        cut.Find("#lvlToggle_Evaluation").Click();
        Assert.True(cut.Find("#lv_Evaluation_XgRoller").HasAttribute("checked"));
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

        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
    }

    // Canonical-order render pin for the dice facet: every roll must surface as
    // a #dr_<token> checkbox, in the lib's ascending canonical order — no
    // UI-side roll list or sort rule. The expectation is an independent literal
    // of the 21 tokens, not a projection of DiceRoll.All: comparing the
    // rendered ids back to the very list the panel iterates is a tautology that
    // moves with the lib and can never fail on drift (re-keyed out of that
    // vacuous-green class alongside the level pin, halheinrich/backgammon#159).
    // The ids are DiceRoll's own two-digit tokens, read as text, so a panel-side
    // token rule could not satisfy this either, and the literal's length pins
    // the cardinality without a separate count.
    [Fact]
    public void DiceSection_RendersAll21RollsInCanonicalOrder()
    {
        var cut = RenderExpanded(FilterFacet.DiceRolls);

        Assert.Equal(
            new[]
            {
                "11",
                "21", "22",
                "31", "32", "33",
                "41", "42", "43", "44",
                "51", "52", "53", "54", "55",
                "61", "62", "63", "64", "65", "66",
            },
            cut.FindAll("input[id^='dr_']").Select(el => el.Id!["dr_".Length..]));
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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.DiceRolls);

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
        var cut = RenderExpanded(FilterFacet.DiceRolls);

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_66").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded(FilterFacet.DiceRolls);

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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.DiceRolls);

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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.PositionPattern);

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
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.PositionPattern);

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
        var cut = RenderExpanded(FilterFacet.PositionPattern);

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
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2,] [5,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = RenderExpanded(FilterFacet.PositionPattern);

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
        var cut = RenderExpanded(FilterFacet.PositionPattern);

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

        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

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
        var cut = RenderExpanded(FilterFacet.MatchScores);

        // Anchor to the match-score section's own hint, not just page markup,
        // so an unrelated mention of the convention elsewhere can't satisfy this.
        var section = MatchScores(cut).ParentElement!;
        var hint = section.QuerySelector(".form-text")!;

        Assert.Contains("on-roll-anchored", hint.TextContent);
        // Both orientations are named — the whole point is that they differ.
        Assert.Contains("4a5a", hint.TextContent);
        Assert.Contains("5a4a", hint.TextContent);
    }

    // Cross-lib invariant (XgFilter_Lib is a dependency): every example token the
    // placeholder advertises must survive FilterConfig.Build() — the same score
    // parsing the panel's Apply path runs, reached through the lib's intent
    // surface rather than its internal filter types. Build() fails loud on any
    // token the parser rejects, so this pins "the UI never advertises an example
    // the lib rejects" as a standing invariant rather than a one-time fix — a
    // future placeholder edit that introduces an example the grammar refuses
    // trips here.
    [Fact]
    public void MatchScorePlaceholder_ExampleTokensAllParse()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        var examples = PlaceholderExamples(cut);

        Assert.NotEmpty(examples);
        // Build() is the lib's Apply-time validation path.
        var cfg = new FilterConfig { MatchScores = [.. examples] };
        Assert.Null(Record.Exception(() => cfg.Build()));
    }

    // The grammar spells double match point two ways (halheinrich/backgammon#259),
    // and every place the field states its vocabulary offers both: the
    // placeholder as an example of its own, the hint as a spelling of 1a1a, and
    // the malformed verdict beside the money tokens. Each is asked for the
    // grammar's constant, never a literal — the pin follows the spelling
    // wherever MatchScoreToken takes it.
    [Fact]
    public void MatchScoreFieldCopy_OffersTheDoubleMatchPointAlias_FromTheGrammarsConstant()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        var hint = MatchScores(cut).ParentElement!.QuerySelector(".form-text")!.TextContent;
        MatchScores(cut).Input("not-a-score");
        var verdict = Assert.Single(MatchScoreVerdicts(cut));

        Assert.Contains(MatchScoreToken.DoubleMatchPoint, PlaceholderExamples(cut));
        Assert.Contains(MatchScoreToken.DoubleMatchPoint, hint);
        Assert.Contains(MatchScoreToken.DoubleMatchPoint, verdict);
    }

    // The case the user reported: typing the alias the hint explained drew
    // "Not a valid score". It now leaves the field clean and Apply offered.
    [Fact]
    public void DoubleMatchPointAlias_LeavesTheFieldClean_AndApplyOffered()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(MatchScoreToken.DoubleMatchPoint);

        Assert.DoesNotContain("is-invalid", MatchScores(cut).GetAttribute("class"));
        Assert.Empty(MatchScoreVerdicts(cut));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // ── Match-score token validity ─────────────────────────────────────────
    //
    // The grammar itself lives in XgFilter_Lib (MatchScoreToken) and is asked
    // twice over: FilterConfig.GetInvalidFields() names the list when any entry
    // faults, and MatchScoreToken.GetFault says of a single token which kind of
    // fault it is. These pins are the panel's half — that it asks, that it
    // marks this field and no other, that Apply and save both refuse while a
    // token is faulted, and that the two fault kinds get two voices.
    //
    // Posture, stated because this leg turns on it: this suite pins structure
    // and wiring, never copy — the same ruling FilterHelpTests records. Where a
    // token spelling must enter an assertion it is referenced from
    // MatchScoreToken's constants, never re-typed: two literals would agree
    // today and drift silently the day a token is respelled, which is the exact
    // drift the exported constants exist to prevent. The independent-literal
    // oracle for "the user can read X" lives in the e2e suite.

    // Both fault kinds land in the same panel state, and it is the
    // position-pattern field's: the field reds itself, a verdict appears under
    // it, and Apply closes — with no #applyDisabledReason line, because an
    // invalid value explains itself where it was typed and the panel never
    // states a reason twice.
    [Theory]
    [InlineData("not-a-score")]
    [InlineData(MatchScoreToken.RetiredMoney)]
    public void FaultedMatchScoreToken_MarksTheField_AndGatesApply(string token)
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(token);

        Assert.Contains("is-invalid", MatchScores(cut).GetAttribute("class"));
        Assert.Single(MatchScoreVerdicts(cut));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // The clean case, including both live money tokens: listing them together
    // is what the retired one used to mean, and the panel must let it through
    // rather than treating "two money entries" as a contradiction.
    [Fact]
    public void ValidMatchScoreTokens_LeaveTheFieldClean_AndApplyOffered()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(
            $"4a5a, 1a2aC, {MatchScoreToken.MoneyWithJacoby}, {MatchScoreToken.MoneyWithoutJacoby}");

        Assert.DoesNotContain("is-invalid", MatchScores(cut).GetAttribute("class"));
        Assert.Empty(MatchScoreVerdicts(cut));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The malformed voice states the vocabulary, and the spellings it states
    // are the grammar's own — not this test's, and not the panel's either.
    // The second half is the absent half of the pair: a malformed token is
    // answered by retyping it, so this line must never hand the user a
    // spelling the grammar has retired.
    [Fact]
    public void MalformedMatchScoreToken_VerdictNamesTheMoneyTokens_AndNoRetiredSpelling()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input("not-a-score");

        var verdict = Assert.Single(MatchScoreVerdicts(cut));
        Assert.Contains(MatchScoreToken.MoneyWithJacoby, verdict);
        Assert.Contains(MatchScoreToken.MoneyWithoutJacoby, verdict);
        Assert.Empty(RetiredWordsIn(verdict));
    }

    // The retired voice is the one that may — must — speak the retired
    // spelling: it is naming what the user typed. Both halves pinned, and both
    // from the lib: the retired token it names is MatchScoreToken's, and the
    // replacements it offers are the lib's own statement of what to offer,
    // RetiredMoneyReplacements, rather than a pair this panel chose.
    [Fact]
    public void RetiredMatchScoreToken_VerdictNamesTheTokenAndItsReplacements()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(MatchScoreToken.RetiredMoney);

        var verdict = Assert.Single(MatchScoreVerdicts(cut));
        Assert.Equal([MatchScoreToken.RetiredMoney], RetiredWordsIn(verdict));
        foreach (var replacement in MatchScoreToken.RetiredMoneyReplacements)
            Assert.Contains(replacement, verdict);
    }

    // Two voices, not one line with a swapped noun — the remedies genuinely
    // differ (retype it versus use these instead), so a buffer holding both
    // kinds gets both, in the order the panel renders them.
    [Fact]
    public void MatchScoreVerdicts_AreOneVoicePerFaultKind()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input("not-a-score");
        var malformed = Assert.Single(MatchScoreVerdicts(cut));

        MatchScores(cut).Input(MatchScoreToken.RetiredMoney);
        var retired = Assert.Single(MatchScoreVerdicts(cut));

        Assert.NotEqual(malformed, retired);

        MatchScores(cut).Input($"not-a-score, {MatchScoreToken.RetiredMoney}");

        Assert.Equal([malformed, retired], MatchScoreVerdicts(cut));
    }

    // One kind of mistake is explained once, however many entries made it —
    // the verdict is per fault kind, not per offending token.
    [Fact]
    public void ManyFaultedTokensOfOneKind_SpeakWithOneVoice()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input("not-a-score, 0a5a, 3a5aC");

        Assert.Single(MatchScoreVerdicts(cut));
    }

    // Recovery: the mark, the verdict, and the gate are all derived, never
    // latched, so correcting the token restores the panel with no other
    // gesture — the error-bound recovery pin, over the field next door.
    [Fact]
    public void FixingFaultedMatchScoreToken_ClearsTheVerdict_AndReEnablesApply()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(MatchScoreToken.RetiredMoney);
        Assert.True(Apply(cut).HasAttribute("disabled"));

        MatchScores(cut).Input(MatchScoreToken.MoneyWithoutJacoby);

        Assert.DoesNotContain("is-invalid", MatchScores(cut).GetAttribute("class"));
        Assert.Empty(MatchScoreVerdicts(cut));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // Attribution across facets, both directions. The invalid-field set spans
    // three facets — the score-token list and both range facets — so each
    // feedback line must key on its own fields: a faulted token must not draw
    // an explanation of either range, and a bad bound must not draw one about
    // score tokens. This is what fails if any line is ever pointed at the
    // wrong field, or at "the lib named something".
    [Fact]
    public void FaultedMatchScoreToken_DrawsNoRangeFacetVerdict()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores, FilterFacet.MoveNumberRange);

        MatchScores(cut).Input(MatchScoreToken.RetiredMoney);

        Assert.Single(MatchScoreVerdicts(cut));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#moveNumberFeedback"));
        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
    }

    [Fact]
    public void InvalidErrorBound_DrawsNoMatchScoreVerdict()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        ErrorMin(cut).Input("-1");

        Assert.NotNull(cut.Find("#errorRangeFeedback"));
        Assert.Empty(MatchScoreVerdicts(cut));
        Assert.DoesNotContain("is-invalid", MatchScores(cut).GetAttribute("class"));
    }

    // The field's advertised copy — the placeholder's examples and the hint
    // line beneath it — offers nothing the grammar has retired. The absent
    // half of the placeholder pair (its present half is
    // MatchScorePlaceholder_ExampleTokensAllParse, which the retired token
    // fails through Build()), extended to the hint, which advertised the bare
    // money token too until halheinrich/backgammon#121 split it.
    [Fact]
    public void MatchScoreFieldCopy_OffersNoRetiredSpelling()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        var placeholder = MatchScores(cut).GetAttribute("placeholder")!;
        var hint = MatchScores(cut).ParentElement!.QuerySelector(".form-text")!.TextContent;

        Assert.Empty(RetiredWordsIn(placeholder));
        Assert.Empty(RetiredWordsIn(hint));
        // The present half for the hint: it names both live tokens, from the
        // grammar's constants rather than a literal of its own.
        Assert.Contains(MatchScoreToken.MoneyWithJacoby, hint);
        Assert.Contains(MatchScoreToken.MoneyWithoutJacoby, hint);
    }

    // The lib's documented posture, pinned at the panel for the case
    // halheinrich/backgammon#121 actually creates: a filter saved before the
    // money token split still loads, still shows the token it holds, marks the
    // field, and is refused a commit — never silently rewritten to one of the
    // replacements (which would change the user's filter behind their back),
    // never silently dropped.
    [Fact]
    public void StoredConfigWithRetiredMoneyToken_LoadsAndShowsInvalid_WithApplyGated()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult(new FilterConfig { MatchScores = [MatchScoreToken.RetiredMoney] }.ToJson());

        var cut = Render<FilterPanel>();
        ExpandFacets(cut, FilterFacet.MatchScores);

        Assert.Equal(MatchScoreToken.RetiredMoney, MatchScores(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", MatchScores(cut).GetAttribute("class"));
        Assert.Single(MatchScoreVerdicts(cut));
        Assert.True(Apply(cut).HasAttribute("disabled"));
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
        ExpandFacets(cut, FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

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
        ExpandFacets(cut, FilterFacet.Players);
        cut.WaitForAssertion(() => Assert.Equal(
            "Hal",
            cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value")));
    }

    // Save-as must capture the live buffers, not the last-applied config —
    // the whole point is saving while dirty, before (or instead of) Apply.
    [Fact]
    public void TryGetEditedConfig_UnappliedEdits_ReturnsLiveBuffers()
    {
        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.ContactTypes);

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
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2");

        Assert.False(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Null(cfg);
    }

    // Re-keyed by halheinrich/backgammon#121, and the re-keying is the point.
    // Match-score text used to ride raw through both paths, validated only
    // downstream in FilterConfig.Build(); the grammar has since joined the
    // lib's field table, so GetInvalidFields names the list and the one
    // IsCommittable member both gates read tightened in the same edit. Save
    // still mirrors Apply exactly — which is why this pin moved rather than
    // being deleted: a saved document minted from a faulted token would be a
    // permanent trap, since loading it reproduces the state with Apply shut.
    [Theory]
    [InlineData("not-a-score")]
    [InlineData(MatchScoreToken.RetiredMoney)]
    public void TryGetEditedConfig_FaultedMatchScoreToken_ReturnsFalseNull(string token)
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(token);

        Assert.False(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Null(cfg);
    }

    // ── Error-bound validity ───────────────────────────────────────────────
    //
    // The rule itself lives in XgFilter_Lib (bounds non-negative, min ≤ max,
    // NaN rejected) and is asked through FilterConfig.GetInvalidFields(); these
    // pins are about the panel's half — that it asks, that it marks the field
    // the lib names and no other, and that Apply and save both refuse while any
    // field is named. They deliberately assert no message text: the wording is
    // the panel's to change, the rule is not.

    // The always-visible facet's first gate: a negative lower bound is one the
    // lib names, so the Min box reds and Apply closes — the is-invalid +
    // invalid-feedback + disabled-Apply idiom the position-pattern field
    // established, now over a field whose rule the panel does not own.
    [Fact]
    public void NegativeErrorMin_MarksMinField_AndGatesApply()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("-1");

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
    }

    // Attribution is per field, and the panel must not blur it: a negative Max
    // beside a perfectly good Min reds only the Max. This is what proves the
    // styling keys on the lib's per-FilterField verdict rather than reding the
    // whole facet the moment anything is wrong in it.
    [Fact]
    public void NegativeErrorMax_MarksOnlyTheMaxField()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("1");
        ErrorMax(cut).Input("-2");

        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // The other half of the attribution rule: min > max is a fault of the pair
    // with neither bound wrong on its own, so both fields carry the mark and
    // the user picks which end to move.
    [Fact]
    public void MisorderedErrorBounds_MarkBothFields()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("5");
        ErrorMax(cut).Input("2");

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // double.TryParse accepts the literal "NaN", so a text-entry panel really
    // can hand the lib one — and the lib rejects it. Pinned because the
    // feedback line's "a number, zero or greater" is worded to stay true for
    // exactly this input.
    [Fact]
    public void NaNErrorBound_MarksField_AndGatesApply()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("NaN");

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
    }

    // Gate composition, first direction: the two validity rules are
    // independent, so a committable position pattern does not rescue a bad
    // bound. The pattern field stays unmarked — one wrong value marks one
    // field — and no disabled-reason line appears, since an invalid value
    // explains itself where it sits.
    [Fact]
    public void InvalidErrorBound_DisablesApply_EvenWithValidPattern()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2,]");
        Assert.False(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("-1");

        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // Gate composition, other direction: good bounds do not rescue an
    // unparseable pattern, and the error-range feedback stays absent — the
    // panel never volunteers an explanation for a rule that is not broken.
    [Fact]
    public void InvalidPositionPattern_DisablesApply_EvenWithValidErrorBounds()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        ErrorMin(cut).Input("1");
        ErrorMax(cut).Input("2");
        Assert.False(Apply(cut).HasAttribute("disabled"));

        cut.Find("#positionPattern").Input("[6,2");

        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
    }

    // Recovery: the mark and the gate are both derived, never latched, so
    // correcting the value restores the panel without any other gesture.
    [Fact]
    public void FixingInvalidErrorBound_ClearsMark_AndReEnablesApply()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("-1");
        Assert.True(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("1");

        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The lib's documented posture, pinned end to end at the panel: a stored
    // selection whose bound a rule outlaws still loads, still shows the values
    // it holds, marks the offending one, and is refused a commit. Never
    // silently repaired (which would change the user's filter behind their
    // back) and never silently dropped.
    [Fact]
    public void StoredConfigWithInvalidBound_LoadsAndShowsInvalid_WithApplyGated()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult(new FilterConfig { ErrorMin = -1, ErrorMax = 2 }.ToJson());

        var cut = Render<FilterPanel>();

        Assert.Equal("-1", ErrorMin(cut).GetAttribute("value"));
        Assert.Equal("2", ErrorMax(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // The save gate is Apply's validity gate, whole: an invalid bound refuses
    // the snapshot exactly as an unparseable pattern does, so a saved document
    // can never be minted from a selection Apply would itself have refused.
    [Fact]
    public void TryGetEditedConfig_InvalidErrorBound_ReturnsFalseNull()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("5");
        ErrorMax(cut).Input("2");

        Assert.False(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Null(cfg);
    }

    // ── Move-number-bound validity ─────────────────────────────────────────
    //
    // The error-range block above, over the panel's other range facet — the
    // same three properties (the panel asks, it marks the field the lib names
    // and no other, and Apply and save both refuse while any field is named)
    // pinned separately rather than parameterized over the two facets. The
    // shape is shared, the rules are not: the lib floors an error magnitude at
    // zero and a move ordinal at one (halheinrich/backgammon#119), so a shared
    // theory would carry per-facet data for every value anyway and would hide
    // which floor each case is really exercising. Message text is deliberately
    // unasserted here as it is above — the wording is the panel's to change,
    // the rule is not.

    // The floor, from both sides of it: zero is the value a 1-based ordinal
    // most invites (nothing on screen says moves start at one until this line
    // appears), and a negative is the value a stored blob can carry in. Either
    // reds the Min box, draws the facet's own feedback line, and closes Apply.
    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void MoveNumberMinBelowOne_MarksMinField_AndGatesApply(string bound)
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input(bound);

        Assert.Contains("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.NotNull(cut.Find("#moveNumberFeedback"));
    }

    // Attribution is per field here too: a Max below the floor beside a good
    // Min reds only the Max. What proves the styling keys on the lib's
    // per-FilterField verdict rather than reding the whole facet the moment
    // anything in it is wrong.
    [Fact]
    public void MoveNumberMaxBelowOne_MarksOnlyTheMaxField()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("1");
        MoveNumberMax(cut).Input("0");

        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // The other half of the attribution rule: min > max is a fault of the pair
    // with neither bound wrong on its own, so both carry the mark and the user
    // picks which end to move.
    [Fact]
    public void MisorderedMoveNumberBounds_MarkBothFields()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("5");
        MoveNumberMax(cut).Input("2");

        Assert.Contains("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Bounds the lib accepts leave the facet unmarked and silent — the panel
    // never volunteers an explanation for a rule that is not broken. The
    // present half of the absence assertions above, and the reason a bound of
    // one is typed explicitly: it sits on the floor, where an off-by-one in
    // the rule would show.
    [Fact]
    public void ValidMoveNumberBounds_LeaveTheFacetUnmarkedAndSilent()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("1");
        MoveNumberMax(cut).Input("30");

        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#moveNumberFeedback"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // Attribution across the two range facets, both directions. They share a
    // shape and a pair of placeholders, so each line must key on its own two
    // fields: a bad move number must not draw an explanation of the error
    // bounds, and a bad error bound must not draw one about move numbers. This
    // is what fails if either line is ever pointed at "the lib named
    // something".
    [Fact]
    public void InvalidMoveNumberBound_DrawsNoOtherFacetVerdict()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores, FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("0");

        Assert.NotNull(cut.Find("#moveNumberFeedback"));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.Empty(MatchScoreVerdicts(cut));
        Assert.DoesNotContain("is-invalid", MatchScores(cut).GetAttribute("class"));
    }

    [Fact]
    public void InvalidErrorBound_DrawsNoMoveNumberVerdict()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        ErrorMin(cut).Input("-1");

        Assert.NotNull(cut.Find("#errorRangeFeedback"));
        Assert.Empty(cut.FindAll("#moveNumberFeedback"));
        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
    }

    // Recovery: the mark and the gate are derived, never latched, so
    // correcting the value restores the panel without any other gesture.
    [Fact]
    public void FixingInvalidMoveNumberBound_ClearsMark_AndReEnablesApply()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("0");
        Assert.True(Apply(cut).HasAttribute("disabled"));

        MoveNumberMin(cut).Input("1");

        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#moveNumberFeedback"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The save gate is Apply's validity gate, whole: an out-of-range move
    // bound refuses the snapshot exactly as an invalid error bound does, so a
    // saved document can never be minted from a selection Apply would itself
    // have refused.
    [Fact]
    public void TryGetEditedConfig_InvalidMoveNumberBound_ReturnsFalseNull()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("5");
        MoveNumberMax(cut).Input("2");

        Assert.False(cut.Instance.TryGetEditedConfig(out var cfg));
        Assert.Null(cfg);
    }

    // The lib's documented posture, pinned end to end for this facet: a stored
    // selection whose bound a rule outlaws still loads, still shows the values
    // it holds, marks the offending one only, and is refused a commit. The
    // case that really exists — the floor arrived in
    // halheinrich/backgammon#119, after selections carrying a zero could
    // already have been saved. Never silently repaired (which would change the
    // user's filter behind their back) and never silently dropped.
    [Fact]
    public void StoredConfigWithInvalidMoveNumberBound_LoadsAndShowsInvalid_WithApplyGated()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult(new FilterConfig { MoveNumberMin = 0, MoveNumberMax = 30 }.ToJson());

        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        Assert.Equal("0", MoveNumberMin(cut).GetAttribute("value"));
        Assert.Equal("30", MoveNumberMax(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#moveNumberFeedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Gate composition across the two range facets: independent rules, so good
    // bounds in one never rescue bad bounds in the other, and both facets
    // speak at once when both are wrong. The disabled-reason line stays absent
    // throughout — an invalid value explains itself where it sits.
    [Fact]
    public void InvalidBoundsInBothRangeFacets_MarkBothAndSpeakTwice()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        ErrorMin(cut).Input("-1");
        MoveNumberMax(cut).Input("0");

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
        Assert.NotNull(cut.Find("#moveNumberFeedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // ── More filters container ─────────────────────────────────────────────

    // Fold the container again, OpenMoreFilters' twin and waited on for the
    // same reason.
    private static void FoldMoreFilters(IRenderedComponent<FilterPanel> cut)
    {
        if (cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded") == "false") return;

        cut.Find("#moreFiltersToggle").Click();
        cut.WaitForAssertion(() => Assert.Equal(
            "false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
    }

    // The last value written under a given key.
    private string? LastWrite(string key) =>
        JSInterop.Invocations["localStorage.setItem"]
                 .Last(i => (string?)i.Arguments[0] == key).Arguments[1] as string;

    // The at-rest panel is what the container exists to make it
    // (halheinrich/backgammon#231): the error range, the container, and the
    // two buttons — and not one facet row, because the container's children
    // are absent from the DOM while it is folded rather than styled away. The
    // region it controls is rendered either way, so the aria-controls it
    // carries folded is never dangling.
    [Fact]
    public void MoreFilters_DefaultFolded_LeavesTheErrorRangeAndTheButtons()
    {
        var cut = Render<FilterPanel>();

        var toggle = cut.Find("#moreFiltersToggle");
        Assert.Equal("BUTTON", toggle.TagName);
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("moreFilters", toggle.GetAttribute("aria-controls"));
        Assert.NotNull(cut.Find("#moreFilters"));

        Assert.NotNull(cut.Find("#errorMin"));
        Assert.NotNull(cut.Find("button.btn-primary"));
        Assert.NotNull(cut.Find("#clearFilters"));

        Assert.Empty(cut.FindAll("button[id^='facetToggle_']"));
        Assert.Empty(cut.FindAll("#moreFilters *"));
    }

    // Its name is the ruled label for the fold it is currently in, and
    // nothing else — the +/− glyph is decorative and says so, the rows' idiom
    // one tier up. The label the panel shows is also the name a screen reader
    // computes, because it is the same text: nothing here sets an aria-label
    // that could say one thing while the button says another.
    //
    // Both states are read off ONE panel across real toggles rather than off
    // two fresh mounts: what the ruling asks for is that the words move when
    // the fold does, and a pin that mounted each state separately would pass a
    // panel whose label never moved at all. The return trip is pinned too — a
    // label that flipped once and stuck would be the same defect one click
    // later.
    [Fact]
    public void MoreFiltersToggle_IsNamedByTheRuledLabelForItsState()
    {
        var cut = Render<FilterPanel>();

        var folded = cut.Find("#moreFiltersToggle");
        Assert.NotNull(folded.QuerySelector("[aria-hidden='true']"));
        Assert.Null(folded.GetAttribute("aria-label"));
        Assert.Equal(FoldedLabel, HeaderName(folded));

        OpenMoreFilters(cut);

        var opened = cut.Find("#moreFiltersToggle");
        Assert.NotNull(opened.QuerySelector("[aria-hidden='true']"));
        Assert.Null(opened.GetAttribute("aria-label"));
        Assert.Equal(ExpandedLabel, HeaderName(opened));

        FoldMoreFilters(cut);

        Assert.Equal(FoldedLabel, HeaderName(cut.Find("#moreFiltersToggle")));
    }

    // The hierarchy this container was rejected for not drawing
    // (halheinrich/backgammon#239): shipped flat, its toggle sat at the rows'
    // own left edge, size and weight, so the control that folds the panel read
    // as the first of nine identical links. Two marks fix it, and this pins
    // both as a DIFFERENCE from a row rather than as a class list in
    // isolation — what the ruling asks for is that the two be told apart on
    // sight, so a row's own toggle is the other half of every assertion.
    [Fact]
    public void MoreFiltersToggle_ReadsAsTheRowsParent_NotAsANinthRow()
    {
        // The first row is opened too, so the body whose indentation the
        // region's borrows is on screen to be compared against.
        var cut = RenderExpanded(RowFacets[0]);

        var toggle = cut.Find("#moreFiltersToggle");
        var row = cut.Find($"#facetToggle_{RowFacets[0]}");

        // Header weight, the card header's <strong> in utility form.
        Assert.Contains("fw-bold", toggle.ClassList);
        Assert.DoesNotContain("fw-bold", row.ClassList);

        // And not the rows' scale: btn-sm is what marks the repeated tier, and
        // the control over that tier is not of it.
        Assert.DoesNotContain("btn-sm", toggle.ClassList);
        Assert.Contains("btn-sm", row.ClassList);

        // The rows indent under it as a group — the region's mark, and the
        // same step a row's own body already takes under its header, so the
        // panel says "inside" one way at both tiers.
        Assert.Contains("ms-3", cut.Find("#moreFilters").ClassList);
        Assert.Contains("ms-3", cut.Find($"#facet_{RowFacets[0]}").FirstElementChild!.ClassList);

        // None of it costs the disclosure its honesty: still a real button
        // carrying its own state, never a heading element — what outline this
        // panel sits in is the host's to know.
        Assert.Equal("BUTTON", toggle.TagName);
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("moreFilters", toggle.GetAttribute("aria-controls"));
    }

    // The gap the ruling asks for sits between the toggle and the first row,
    // and it is the region's: nothing stands between the two but that margin,
    // and it is there only while the region has rows in it — folded, the
    // control keeps its tight line to the buttons below.
    [Fact]
    public void TheGap_SitsAfterTheToggle_AndOnlyWhileTheRowsShow()
    {
        var cut = Render<FilterPanel>();

        var folded = cut.Find("#moreFilters");
        Assert.Contains("ms-3", folded.ClassList);
        Assert.DoesNotContain("mt-3", folded.ClassList);

        OpenMoreFilters(cut);

        var opened = cut.Find("#moreFilters");
        Assert.Contains("mt-3", opened.ClassList);
        Assert.Equal("moreFiltersToggle", opened.PreviousElementSibling!.Id);
    }

    // The rows themselves pack tightly: the wrapper that used to draw a blank
    // line between every pair of them carries no margin at all now, in either
    // state of the row. Pinned over all eight, because the wrapper is one
    // RenderFragment and a margin creeping back would creep back everywhere.
    [Fact]
    public void RowWrapper_DrawsNoSeparatingMargin()
    {
        var cut = RenderExpanded(RowFacets[0]);

        foreach (var facet in RowFacets)
            Assert.Empty(cut.Find($"#facetToggle_{facet}").ParentElement!.ClassList);
    }

    // What still needs space beneath it is an expanded BODY, so the next row's
    // header does not land against the controls above it — so the step rides
    // on the row's region and only while that region has children. The
    // collapsed half is the other side of the same ruling: a margin on the
    // empty region would put the blank line straight back between the headers.
    [Fact]
    public void ExpandedRowBody_KeepsSpaceBeneathIt_ACollapsedRowNone()
    {
        var cut = RenderExpanded(RowFacets[0]);

        Assert.Contains("mb-3", cut.Find($"#facet_{RowFacets[0]}").ClassList);

        foreach (var collapsed in RowFacets.Skip(1))
            Assert.Empty(cut.Find($"#facet_{collapsed}").ClassList);
    }

    // Opening it reveals the rows themselves, ids and order untouched, and
    // every one of them inside the region the toggle names: what the container
    // changes is where the rows are reached from, never what they are.
    [Fact]
    public void MoreFilters_Opened_RevealsTheRowsUnchanged_InsideItsRegion()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        Assert.Equal(
            RowFacets.Select(f => f.ToString()),
            cut.Find("#moreFilters")
               .QuerySelectorAll("button[id^='facetToggle_']")
               .Select(el => el.Id!["facetToggle_".Length..]));
    }

    // The container answers to none of the rows' id prefixes. Those prefixes
    // are surveyed as "the rows" — here, and in both hosts — so a container
    // that answered to one would report itself as a ninth row.
    [Fact]
    public void MoreFiltersIds_DoNotAnswerToTheRowPrefixes()
    {
        var cut = Render<FilterPanel>();

        Assert.NotNull(cut.Find("#moreFiltersToggle"));
        Assert.Empty(cut.FindAll(
            "[id^='facetToggle_'], [id^='facet_'], [id^='facetBadge_'], [id^='facetHint_']"));
    }

    // The badge counts the rows whose facet is set — the lib's ruling
    // (GetActiveFacets on the live buffers) that the rows' own badges read,
    // never a second count of this panel's own — and it speaks only while the
    // container is folded: open, each row's own badge says it in more detail.
    [Fact]
    public void MoreFiltersBadge_CountsTheSetRows_OnlyWhileFolded()
    {
        var cut = RenderExpanded(FilterFacet.ContactTypes, FilterFacet.DiceRolls);

        cut.Find("#ct_Race").Change(true);
        cut.Find("#dr_31").Change(true);
        Assert.Empty(cut.FindAll("#moreFiltersBadge"));

        FoldMoreFilters(cut);

        Assert.Equal("2 set", cut.Find("#moreFiltersBadge").TextContent.Trim());
    }

    // Nothing set, nothing counted.
    [Fact]
    public void MoreFiltersBadge_AbsentWhenNoRowIsSet()
    {
        var cut = Render<FilterPanel>();

        Assert.Empty(cut.FindAll("#moreFiltersBadge"));
    }

    // The error range is not behind this fold, so it is never in the count:
    // an active error range leaves the badge away, which is the same division
    // that keeps ErrorRange out of the rows' vocabulary.
    [Fact]
    public void MoreFiltersBadge_NeverCountsTheErrorRange()
    {
        var cut = Render<FilterPanel>();

        cut.Find("#errorMin").Input("0.05");

        Assert.Empty(cut.FindAll("#moreFiltersBadge"));
    }

    // PRESENCE follows the lib's activation predicate, not the buffer behind
    // the control: pattern text that does not parse builds as "no pattern", so
    // the row is not set and the count does not move for it. The rows' own
    // badge pin, one tier up.
    [Fact]
    public void MoreFiltersBadge_FollowsTheLibsActivationPredicate_NotTheBuffer()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern, FilterFacet.ContactTypes);

        cut.Find("#positionPattern").Input("[6,2");   // unparseable — facet off
        cut.Find("#ct_Race").Change(true);            // genuinely set
        FoldMoreFilters(cut);

        Assert.Equal("1 set", cut.Find("#moreFiltersBadge").TextContent.Trim());
    }

    // A host-staged selection lands in collapsed rows behind a folded
    // container; the badge reports it at rest, without anything opening.
    [Fact]
    public async Task LoadConfig_StagedFacets_CountOnTheFoldedContainer()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(new FilterConfig
        {
            ContactTypes = [ContactType.Race],
            DiceRolls = [new DiceRoll(3, 1)],
        }));

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Equal("2 set", cut.Find("#moreFiltersBadge").TextContent.Trim());
    }

    // Folding or unfolding is navigation, not an edit — no applied-state
    // report, in either direction, exactly as for a row.
    [Fact]
    public void MoreFiltersToggle_DoesNotReportAppliedState()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        OpenMoreFilters(cut);
        FoldMoreFilters(cut);

        Assert.Empty(reports);
    }

    // Each click persists the one bit immediately under the container's own
    // key — never the rows' key, never the config blob: the two preferences
    // are separate because the rows' key speaks a vocabulary of FilterFacet
    // names and this container is not a facet.
    [Fact]
    public void MoreFiltersToggle_PersistsUnderItsOwnKeyAlone()
    {
        var cut = Render<FilterPanel>();

        OpenMoreFilters(cut);
        Assert.Equal("true", LastWrite(MoreFiltersKey));

        FoldMoreFilters(cut);
        Assert.Equal("false", LastWrite(MoreFiltersKey));

        Assert.DoesNotContain(JSInterop.Invocations["localStorage.setItem"],
            i => (string?)i.Arguments[0] == ConfigKey
              || (string?)i.Arguments[0] == DisclosureKey);
    }

    // The remembered state restores across sessions: stored open mounts open,
    // with the rows in the DOM and no click needed.
    [Fact]
    public void StoredOpenContainer_MountsOpen()
    {
        JSInterop.Setup<string?>("localStorage.getItem", MoreFiltersKey).SetResult("true");

        var cut = Render<FilterPanel>();

        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.NotEmpty(cut.FindAll("button[id^='facetToggle_']"));
    }

    // And the value survives a round trip byte for byte: restored, then
    // written again by a pair of gestures that leave it as they found it, it
    // is the same literal that went in. The literal is written out by hand
    // here, so a change of mechanism that also changed the wire fails here
    // rather than silently refolding every user's panel.
    [Fact]
    public void StoredContainerState_RoundTripsByteIdentical()
    {
        JSInterop.Setup<string?>("localStorage.getItem", MoreFiltersKey).SetResult("true");

        var cut = Render<FilterPanel>();
        FoldMoreFilters(cut);   // close…
        OpenMoreFilters(cut);   // …and reopen

        Assert.Equal("true", LastWrite(MoreFiltersKey));
    }

    // Restore is tolerant and one-way: the panel writes only these two
    // literals, so anything else — a stored "false", a blank, a word that is
    // not one, the rows' own JSON arriving under the wrong key — leaves the
    // container folded, which is also what a fresh visit gets.
    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("[\"Players\"]")]
    public void StoredContainerStateThatIsNotTrue_MountsFolded(string stored)
    {
        JSInterop.Setup<string?>("localStorage.getItem", MoreFiltersKey).SetResult(stored);

        var cut = Render<FilterPanel>();

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("button[id^='facetToggle_']"));
    }

    // The rows' pending-restore pin, one tier up: a click landing while the
    // getItem interop is in flight is a fresh user choice, and the late
    // restore must yield to it rather than clobber it.
    [Fact]
    public void MoreFiltersToggle_DuringPendingStoredRestore_UserChoiceWins()
    {
        var pendingGet = JSInterop.Setup<string?>("localStorage.getItem", MoreFiltersKey);

        var cut = Render<FilterPanel>();

        OpenMoreFilters(cut);

        pendingGet.SetResult("false");

        cut.WaitForAssertion(() => Assert.Equal(
            "true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded")));
    }

    // Neither gesture that moves filter values moves the container: staging a
    // saved filter is the host's, clearing is the user's, and which
    // disclosures are open is neither's — the rows' own rule, one tier up.
    [Fact]
    public async Task LoadConfigAndClearFilters_LeaveTheContainerWhereItWas()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(
            new FilterConfig { ContactTypes = [ContactType.Race] }));
        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));

        await cut.Find("#clearFilters").ClickAsync(new());
        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
    }

    // ── Facet rows ─────────────────────────────────────────────────────────

    // The rows' own at-rest state, with the container over them opened to see
    // it: eight collapsed rows and not one facet control, because a collapsed
    // row's children are absent from the DOM rather than styled away. The
    // error range and the buttons are outside the container and visible
    // either way. Presence only here; the ruled order is pinned next, the
    // badges below, and the container's own resting state with the container
    // pins.
    [Fact]
    public void Rows_DefaultCollapsed_ShowNoFacetControls()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
        {
            var toggle = cut.Find($"#facetToggle_{facet}");
            Assert.Equal("BUTTON", toggle.TagName);
            Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
            Assert.Equal($"facet_{facet}", toggle.GetAttribute("aria-controls"));
            Assert.NotNull(cut.Find($"#facet_{facet}"));
        }

        Assert.NotNull(cut.Find("#errorMin"));
        Assert.NotNull(cut.Find("button.btn-primary"));
        Assert.NotNull(cut.Find("#clearFilters"));

        Assert.Empty(cut.FindAll("input[placeholder='e.g. Hal, Magriel']"));
        Assert.Empty(cut.FindAll("input[id^='dt_']"));
        Assert.Empty(cut.FindAll("input[placeholder^='e.g. 4a5a']"));
        Assert.Empty(cut.FindAll("input[id^='ct_']"));
        Assert.Empty(cut.FindAll("input[id^='md_']"));
        Assert.Empty(cut.FindAll("input[id^='dr_']"));
        Assert.Empty(cut.FindAll("#positionPattern"));
        // The other range facet is hidden like the rest — the present half of
        // this pair is the #errorMin assertion above, and the two are only
        // distinguishable because each box carries its own id.
        Assert.Empty(cut.FindAll("#moveNumberMin"));
        Assert.Empty(cut.FindAll("#moveNumberMax"));
    }

    // The ruled membership and order, against this suite's independent
    // literal. Not a tautology: the panel writes its eight rows out one by one
    // (each body is its own markup) and keeps a separate list for the stored
    // vocabulary, so nothing in the product derives the rendered order from
    // anything this compares it to. A row added, dropped or resequenced is a
    // change to what the user sees, so it must be ruled on here rather than
    // followed silently.
    [Fact]
    public void Rows_RenderTheRuledFacetsInTheRuledOrder()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        Assert.Equal(
            RowFacets.Select(f => f.ToString()),
            cut.FindAll("button[id^='facetToggle_']")
               .Select(el => el.Id!["facetToggle_".Length..]));
    }

    // Each row's name is the lib's, never a literal here: the button's text is
    // the facet's [Description] via ToLabel(). The +/− glyph is decorative and
    // says so — aria-expanded already carries which way the row sits — so it
    // is excluded from the comparison the way a screen reader excludes it.
    [Fact]
    public void RowToggle_IsNamedByTheLibsFacetLabel()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
        {
            var toggle = cut.Find($"#facetToggle_{facet}");
            var glyph = toggle.QuerySelector("[aria-hidden='true']");
            Assert.NotNull(glyph);
            Assert.Equal(facet.ToLabel(), HeaderName(toggle));
        }
    }

    // A row header's name as an assistive technology would compute it: the
    // element's text with its aria-hidden decoration left out. bUnit has no
    // accessible-name implementation, so the exclusion is done here — which is
    // the whole reason the glyph carries aria-hidden in the first place.
    private static string HeaderName(IElement header) =>
        header.TextContent
              .Replace(header.QuerySelector("[aria-hidden='true']")?.TextContent ?? string.Empty,
                       string.Empty)
              .Trim();

    // Every text and number box inside a row takes its name by reference from
    // the row's own header, so the name a user hears is the facet's label —
    // the lib's, via ToLabel() — and not the hint sitting above the box. The
    // hint is its description instead. The position-pattern field was the
    // first to do this and halheinrich/backgammon#196 made it the rule for
    // all of them, so they are pinned together: a box that grew here without
    // the pair of references would have to be added to this list to escape it.
    //
    // Both halves are resolved through the DOM the way a screen reader would
    // resolve them (follow the id, read the target), never by asserting the
    // attribute string against a literal: a typo'd id would satisfy a string
    // comparison while naming nothing at all. The described-by half is pinned
    // as identity, not as words — the target must BE the row's hint element —
    // because the wording is the panel's to change and this suite pins
    // structure, never copy.
    [Theory]
    [InlineData(FilterFacet.Players, "input[placeholder='e.g. Hal, Magriel']")]
    [InlineData(FilterFacet.MatchScores, "input[placeholder^='e.g. 4a5a']")]
    [InlineData(FilterFacet.MoveNumberRange, "#moveNumberMin")]
    [InlineData(FilterFacet.MoveNumberRange, "#moveNumberMax")]
    [InlineData(FilterFacet.PositionPattern, "#positionPattern")]
    public void RowInput_IsNamedByItsRowHeader_AndDescribedByItsHint(
        FilterFacet facet, string inputSelector)
    {
        var cut = RenderExpanded(facet);

        var input = cut.Find(inputSelector);

        var namedBy = cut.Find($"#{input.GetAttribute("aria-labelledby")}");
        Assert.Equal($"facetToggle_{facet}", namedBy.Id);
        Assert.Equal(facet.ToLabel(), HeaderName(namedBy));

        var describedBy = cut.Find($"#{input.GetAttribute("aria-describedby")}");
        // Identity by id rather than by instance: bUnit hands back a fresh
        // wrapper per Find, so two lookups of one node are never the same
        // object. The claim still holds — the element the row's hint selector
        // reaches is the one aria-describedby names.
        var hint = cut.Find($"#facet_{facet} small.text-muted");
        Assert.Equal(hint.Id, describedBy.Id);
        Assert.NotEmpty(describedBy.TextContent.Trim());

        // The lie the references replace: no label in one of these rows may
        // claim to name a box, because the only label near one carries the
        // hint. (Rows chosen by ticking options are not on this list, and
        // their per-option labels are exactly right.)
        Assert.Empty(cut.Find($"#facet_{facet}").QuerySelectorAll("label[for]"));
    }

    // The error range sits outside the rows, so it names its two boxes off its
    // own heading — and off the heading WORDS, not the label element around
    // them: a reference to the label would pull the hint into the name and
    // make "(inclusive; blank = no bound)" part of what the box is called. The
    // words themselves are the lib's, like every other facet heading on the
    // panel.
    [Theory]
    [InlineData("#errorMin")]
    [InlineData("#errorMax")]
    public void ErrorRangeInput_IsNamedByItsOwnHeading_AndDescribedByItsHint(
        string inputSelector)
    {
        var cut = Render<FilterPanel>();

        var input = cut.Find(inputSelector);

        var namedBy = cut.Find($"#{input.GetAttribute("aria-labelledby")}");
        Assert.Equal(FilterFacet.ErrorRange.ToLabel(), namedBy.TextContent.Trim());

        var describedBy = cut.Find($"#{input.GetAttribute("aria-describedby")}");
        Assert.NotEmpty(describedBy.TextContent.Trim());

        // The two are separate elements, and the name does not swallow the
        // description — the whole reason the id sits on the heading span
        // rather than on the label around both.
        Assert.NotEqual(namedBy.Id, describedBy.Id);
        Assert.DoesNotContain(
            describedBy.TextContent.Trim(), namedBy.TextContent.Trim(), StringComparison.Ordinal);
    }

    // Nothing the user can type into is left unnamed: every text and number
    // box on the panel, with every row open, carries both references. The
    // sweep is what makes the two pins above a rule rather than a list — a box
    // added tomorrow with no name fails here without anyone remembering to
    // extend a theory.
    [Fact]
    public void EveryTextAndNumberInput_CarriesBothReferences()
    {
        var cut = RenderExpanded(RowFacets);

        var boxes = cut.FindAll("input[type='number'], input:not([type])").ToArray();

        Assert.Equal(7, boxes.Length);
        Assert.All(boxes, box =>
        {
            Assert.NotNull(cut.Find($"#{box.GetAttribute("aria-labelledby")}"));
            Assert.NotNull(cut.Find($"#{box.GetAttribute("aria-describedby")}"));
        });
    }

    // Every id an aria attribute points at must actually exist: a dangling
    // reference costs the control its name or its description silently, which
    // is precisely the failure the name-by-reference idiom risks and the one
    // no rendering check would show. Swept over the whole panel with every row
    // open and every depth mode checked, so the level groups' aria-controls
    // are in scope too.
    [Fact]
    public void EveryAriaReference_ResolvesToAnElementInTheDom()
    {
        var cut = RenderExpanded(RowFacets);
        foreach (var mode in SelectableModes)
            cut.Find($"#md_{mode}").Change(true);

        var references =
            from attribute in new[] { "aria-controls", "aria-labelledby", "aria-describedby" }
            from element in cut.FindAll($"[{attribute}]")
            from id in element.GetAttribute(attribute)!.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            select (element.Id, attribute, id);

        var swept = 0;
        foreach (var (from, attribute, id) in references)
        {
            Assert.True(cut.FindAll($"#{id}").Count == 1,
                $"{from}'s {attribute} names '{id}', which resolves to no single element.");
            swept++;
        }

        // The sweep found something to sweep: eight row toggles plus the
        // pattern field's two references, at a minimum.
        Assert.True(swept >= RowFacets.Length + 2, $"only {swept} references swept");
    }

    // A row round-trips, and only that row: expanding one leaves its
    // neighbours exactly as they were. This is the whole point of the ruling —
    // eight independent rows, not one disclosure wearing eight headings.
    [Fact]
    public void Row_ExpandsAndCollapses_WithoutMovingItsNeighbours()
    {
        var cut = Render<FilterPanel>();

        ExpandFacets(cut, FilterFacet.DiceRolls);
        Assert.Equal("true", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
        Assert.NotEmpty(cut.FindAll("input[id^='dr_']"));
        Assert.Empty(cut.FindAll("#positionPattern"));
        Assert.Equal("false", cut.Find("#facetToggle_PositionPattern").GetAttribute("aria-expanded"));

        ExpandFacets(cut, FilterFacet.DiceRolls);   // a second click collapses
        Assert.Equal("false", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("input[id^='dr_']"));
    }

    // Opening or closing a row is navigation, not an edit: OnAppliedStateChanged
    // must not fire, in either direction.
    [Fact]
    public void RowToggle_DoesNotReportAppliedState()
    {
        var reports = new List<FilterConfig?>();
        var cut = RenderReporting(reports);

        ExpandFacets(cut, FilterFacet.ContactTypes);
        ExpandFacets(cut, FilterFacet.ContactTypes);

        Assert.Empty(reports);
    }

    // Each click persists the whole open set immediately under the rows' own
    // key — a JSON array of facet member names, written in row order however
    // the user got there — and never writes the config blob's key: which rows
    // are open is user preference, not filter state. The out-of-order clicks
    // are the point of the ordering rule: one arrangement of open rows has one
    // spelling on disk.
    [Fact]
    public void RowToggles_PersistTheOpenSetUnderTheirOwnKey()
    {
        var cut = Render<FilterPanel>();

        ExpandFacets(cut, FilterFacet.DiceRolls);
        Assert.Equal("[\"DiceRolls\"]", LastDisclosureWrite());

        ExpandFacets(cut, FilterFacet.Players);
        Assert.Equal("[\"Players\",\"DiceRolls\"]", LastDisclosureWrite());

        ExpandFacets(cut, FilterFacet.DiceRolls);   // close it again
        Assert.Equal("[\"Players\"]", LastDisclosureWrite());

        Assert.DoesNotContain(JSInterop.Invocations["localStorage.setItem"],
            i => (string?)i.Arguments[0] == ConfigKey);
    }

    // The stored value survives a full round trip byte for byte: read back
    // through restore, then written again by a gesture that leaves the set as
    // it found it (close a row, reopen it), it comes out as exactly the
    // literal that went in. Both directions cross the source-generated
    // metadata the row set was moved onto so a trimmed host can publish it
    // (halheinrich/backgammon#193), and the literal is written out by hand
    // rather than produced by any serializer, so a change of mechanism that
    // also changed the wire — spacing, escaping, a different shape — fails
    // here instead of silently resetting every user's open rows.
    [Fact]
    public void StoredOpenSet_RoundTripsByteIdentical()
    {
        const string stored = "[\"Players\",\"MoveNumberRange\",\"DiceRolls\"]";
        JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey).SetResult(stored);

        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);
        Assert.Equal("true", cut.Find("#facetToggle_MoveNumberRange").GetAttribute("aria-expanded"));

        ExpandFacets(cut, FilterFacet.MoveNumberRange);   // close…
        ExpandFacets(cut, FilterFacet.MoveNumberRange);   // …and reopen

        Assert.Equal(stored, LastDisclosureWrite());
    }

    // The last value written under the rows' key.
    private string? LastDisclosureWrite() =>
        JSInterop.Invocations["localStorage.setItem"]
                 .Last(i => (string?)i.Arguments[0] == DisclosureKey).Arguments[1] as string;

    // The remembered set restores across sessions: the named rows mount open,
    // no click needed, and the rows it does not name stay shut. Opening the
    // container to look is not what opened them — it carries its own key and
    // this scenario seeds only the rows' — which is the independence the two
    // keys buy.
    [Fact]
    public void StoredOpenSet_MountsExactlyThoseRowsExpanded()
    {
        JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey)
            .SetResult("[\"Players\",\"PositionPattern\"]");

        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        Assert.Equal("true", cut.Find("#facetToggle_Players").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("#positionPattern"));
        Assert.Equal("false", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("input[id^='dr_']"));
    }

    // Restore is all-or-nothing, and these are the ways a stored value can
    // fail: not JSON at all, not an array, a name no row answers to, a
    // FilterFacet member the panel gives no row (ErrorRange is always visible;
    // PositionTypes is UI-shelved), a numeric token that a careless
    // Enum.TryParse would have honoured as an ordinal, and a good name beside
    // a bad one — the case that decides all-or-nothing against salvaging the
    // rest. Every one restores every row collapsed: the panel only ever writes
    // row names, so anything else is corruption, and half-honouring it would
    // open a set the user never chose.
    [Theory]
    [InlineData("}{ not valid json")]
    [InlineData("\"Players\"")]
    [InlineData("true")]
    [InlineData("[\"NotAFacet\"]")]
    [InlineData("[\"ErrorRange\"]")]
    [InlineData("[\"PositionTypes\"]")]
    [InlineData("[\"players\"]")]
    [InlineData("[0]")]
    [InlineData("[\"0\"]")]
    [InlineData("[null]")]
    [InlineData("[\"Players\",\"NotAFacet\"]")]
    public void StoredOpenSetThatIsUnreadable_MountsEveryRowCollapsed(string stored)
    {
        JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey).SetResult(stored);

        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
            Assert.Equal("false", cut.Find($"#facetToggle_{facet}").GetAttribute("aria-expanded"));
    }

    // The rows' twin of LoadConfig_DuringPendingStoredRestore: a click landing
    // while the getItem interop is in flight is a fresh user choice the late
    // restore must not clobber. Open a row while a stored set naming a
    // different one is pending; the released restore must yield whole — the
    // user's row stays open and the stored one stays shut.
    [Fact]
    public void RowToggle_DuringPendingStoredRestore_UserChoiceWins()
    {
        var pendingGet = JSInterop.Setup<string?>("localStorage.getItem", DisclosureKey);

        var cut = Render<FilterPanel>();

        ExpandFacets(cut, FilterFacet.DiceRolls);

        pendingGet.SetResult("[\"Players\"]");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
            Assert.Equal("false", cut.Find("#facetToggle_Players").GetAttribute("aria-expanded"));
        });
    }

    // The facets whose section carries a bracketed hint — every row but
    // Decision type, whose three options say everything a hint would.
    private static readonly FilterFacet[] HintBearingFacets =
        [.. RowFacets.Where(f => f != FilterFacet.DecisionType)];

    // The hint belongs to the expanded body and nowhere else: a collapsed row
    // carries none of it, not even in its header, and an expanded one carries
    // it inside the row's own region. Pinned structurally rather than by the
    // words themselves — the wording is the panel's to change, where the hint
    // may appear is not — which is this suite's standing posture.
    [Fact]
    public void RowHint_RendersOnlyInsideTheExpandedBody()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
        {
            var row = cut.Find($"#facetToggle_{facet}").ParentElement!;
            Assert.Empty(row.QuerySelectorAll("small.text-muted"));
        }

        foreach (var facet in HintBearingFacets)
        {
            ExpandFacets(cut, facet);

            var row = cut.Find($"#facetToggle_{facet}").ParentElement!;
            var hints = row.QuerySelectorAll("small.text-muted");
            Assert.NotEmpty(hints);
            // …and every one of them sits inside the region, never in the header.
            var region = cut.Find($"#facet_{facet}");
            Assert.All(hints, h => Assert.True(region.Contains(h)));

            ExpandFacets(cut, facet);   // close it again for the next facet
        }
    }

    // ── Row badges ─────────────────────────────────────────────────────────

    // Nothing active, nothing badged. The container is opened first, so the
    // rows are in the DOM to go unbadged — folded, the claim would hold for
    // the wrong reason.
    [Fact]
    public void Badges_AbsentOnDefaults()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        Assert.Empty(cut.FindAll("span[id^='facetBadge_']"));
    }

    // Error range has no row, so nothing it does can badge one. The facet the
    // old single disclosure had to special-case is now simply not in the
    // vocabulary.
    [Fact]
    public void Badges_ErrorRangeBadgesNothing()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        cut.Find("#errorMin").Input("0.05");

        Assert.Empty(cut.FindAll("span[id^='facetBadge_']"));
    }

    // Open a row, make its facet active, close it again — the state in which a
    // badge is supposed to speak.
    private IRenderedComponent<FilterPanel> RenderWithActiveCollapsedFacet(
        FilterFacet facet, Action<IRenderedComponent<FilterPanel>> activate)
    {
        var cut = RenderExpanded(facet);
        activate(cut);
        ExpandFacets(cut, facet);
        return cut;
    }

    // The words, per facet: "set" for the facets chosen by typing into them,
    // where a count of boxes filled would tell the user nothing, and "N
    // selected" for the facets chosen by ticking options. Decision type reads
    // one by construction — a radio group has exactly one choice, and the facet
    // is active only while that choice is not the inactive default.
    [Fact]
    public void Badge_WordsSayHowMuchTheClosedRowIsHiding()
    {
        (FilterFacet Facet, Action<IRenderedComponent<FilterPanel>> Activate, string Expected)[] cases =
        [
            (FilterFacet.Players,
             c => c.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal, Magriel"), "set"),
            (FilterFacet.DecisionType,
             c => c.Find("#dt_CheckerPlaysOnly").Change(true), "1 selected"),
            (FilterFacet.MatchScores,
             c => MatchScores(c).Input("4a5a, 1a1a"), "set"),
            (FilterFacet.MoveNumberRange,
             c => MoveNumberMin(c).Input("7"), "set"),
            (FilterFacet.ContactTypes,
             c => { c.Find("#ct_Race").Change(true); c.Find("#ct_Contact").Change(true); },
             "2 selected"),
            (FilterFacet.AnalysisDepth,
             c => { c.Find("#md_Rollout").Change(true); c.Find("#md_Evaluation").Change(true); },
             "2 selected"),
            (FilterFacet.DiceRolls,
             c => { c.Find("#dr_31").Change(true); c.Find("#dr_66").Change(true); },
             "2 selected"),
            (FilterFacet.PositionPattern,
             c => c.Find("#positionPattern").Input("[6,2,]"), "set"),
        ];

        // Every row is covered: a facet that grew a row without a badge rule
        // would fail here rather than badge whatever the switch fell through to.
        Assert.Equal(RowFacets, cases.Select(x => x.Facet));

        foreach (var (facet, activate, expected) in cases)
        {
            var cut = RenderWithActiveCollapsedFacet(facet, activate);

            Assert.Equal(expected, cut.Find($"#facetBadge_{facet}").TextContent.Trim());
        }
    }

    // A badge is a closed row's business: expanded, the controls themselves say
    // everything it could, so it would be noise — the level groups' ruling one
    // tier down, and the same reason.
    [Fact]
    public void Badge_RendersOnlyWhileTheRowIsCollapsed()
    {
        var cut = RenderExpanded(FilterFacet.ContactTypes);

        cut.Find("#ct_Race").Change(true);
        Assert.Empty(cut.FindAll("#facetBadge_ContactTypes"));

        ExpandFacets(cut, FilterFacet.ContactTypes);   // collapse
        Assert.NotNull(cut.Find("#facetBadge_ContactTypes"));
    }

    // A badge lights on the value being staged, not on Apply: it reads the live
    // edit buffers through the same build path Apply commits through, which is
    // what makes it honest mid-edit. Only this row badges — the badge is per
    // facet, never a panel-wide signal wearing eight ids.
    [Fact]
    public void Badge_LightsOnStagedValues_BeforeAnyApply()
    {
        var cut = RenderExpanded(FilterFacet.DiceRolls);

        cut.Find("#dr_31").Change(true);
        ExpandFacets(cut, FilterFacet.DiceRolls);   // collapse

        var badge = Assert.Single(cut.FindAll("span[id^='facetBadge_']"));
        Assert.Equal("facetBadge_DiceRolls", badge.Id);
    }

    // The pin the badge's SSOT rests on: PRESENCE is the lib's ruling
    // (GetActiveFacets on the live buffers), never a re-reading of the buffer
    // behind the controls. These are the three states where the two answers
    // genuinely differ, so a badge that consulted its buffer directly would
    // light in each of them and fail here:
    //
    //   · position-pattern text that does not parse builds as "no pattern",
    //     so the facet is off however much text is in the box;
    //   · a depth level list whose mode toggle is off is inert by the lib's
    //     guarantee — checked levels, facet still off;
    //   · a player list of nothing but separators splits to no tokens.
    //
    // Each is a real state a user reaches by typing, not a contrivance.
    [Fact]
    public void Badge_PresenceFollowsTheLibsActivationPredicate_NotTheBuffer()
    {
        var unparseable = RenderWithActiveCollapsedFacet(
            FilterFacet.PositionPattern, c => c.Find("#positionPattern").Input("[6,2"));
        Assert.Empty(unparseable.FindAll("#facetBadge_PositionPattern"));

        var inertLevels = RenderWithActiveCollapsedFacet(FilterFacet.AnalysisDepth, c =>
        {
            CheckModeAndExpandLevels(c, AnalysisMode.Rollout);
            c.Find("#lv_Rollout_Ply4").Change(true);
            c.Find("#md_Rollout").Change(false);   // levels kept, facet off
        });
        Assert.Empty(inertLevels.FindAll("#facetBadge_AnalysisDepth"));

        var separatorsOnly = RenderWithActiveCollapsedFacet(
            FilterFacet.Players,
            c => c.Find("input[placeholder='e.g. Hal, Magriel']").Input(" , , "));
        Assert.Empty(separatorsOnly.FindAll("#facetBadge_Players"));
    }

    // A loaded saved filter can stage values into collapsed rows. Their badges
    // must report it at rest — and staging must not open anything: opening a
    // row is the user's gesture, never LoadConfig's.
    [Fact]
    public async Task LoadConfig_StagedFacets_LightBadges_WithoutOpeningRows()
    {
        var cut = Render<FilterPanel>();

        var loaded = new FilterConfig
        {
            ContactTypes = [ContactType.Race],
            DiceRolls = [new DiceRoll(3, 1)],
        };
        await cut.InvokeAsync(() => cut.Instance.LoadConfig(loaded));
        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
            Assert.Equal("false", cut.Find($"#facetToggle_{facet}").GetAttribute("aria-expanded"));

        Assert.Equal("1 selected", cut.Find("#facetBadge_ContactTypes").TextContent.Trim());
        Assert.Equal("1 selected", cut.Find("#facetBadge_DiceRolls").TextContent.Trim());
        Assert.Equal(2, cut.FindAll("span[id^='facetBadge_']").Count);
    }

    // A restored session with active facets badges them at rest — the
    // first-render restore hydrates the buffers the badges read.
    [Fact]
    public void StoredConfigWithActiveFacets_LightsBadgesAtRest()
    {
        var stored = new FilterConfig { ContactTypes = [ContactType.Race] };
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored.ToJson());

        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        var badge = Assert.Single(cut.FindAll("span[id^='facetBadge_']"));
        Assert.Equal("facetBadge_ContactTypes", badge.Id);
    }

    // Clear filters empties every buffer, so every badge goes out with them.
    [Fact]
    public async Task ClearFilters_ExtinguishesEveryBadge()
    {
        var cut = RenderExpanded(FilterFacet.ContactTypes);

        cut.Find("#ct_Race").Change(true);
        ExpandFacets(cut, FilterFacet.ContactTypes);   // collapse — the badge lights
        Assert.NotNull(cut.Find("#facetBadge_ContactTypes"));

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.Empty(cut.FindAll("span[id^='facetBadge_']"));
    }

    // Which rows are open is orthogonal to what Apply commits: the same
    // selection applies to the same config whether it was typed into an open
    // row or staged into a closed one, and opening a row after the fact does
    // not disturb the commit.
    [Fact]
    public async Task Apply_IsUnaffectedByWhichRowsAreOpen()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.ContactTypes);

        cut.Find("#ct_Race").Change(true);
        ExpandFacets(cut, FilterFacet.ContactTypes);   // collapse before applying
        await Apply(cut).ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Equal([ContactType.Race], capturedConfig!.ContactTypes);
        Assert.True(Apply(cut).HasAttribute("disabled"));

        // Opening the row afterwards is navigation: nothing to re-apply.
        ExpandFacets(cut, FilterFacet.ContactTypes);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.True(cut.Find("#ct_Race").HasAttribute("checked"));
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

    // The ruled placement (halheinrich/backgammon#230): the panel's two
    // gestures share one row, Apply first and Clear immediately to its right.
    // Pinned once, as the row's button order — the ruling is about which
    // buttons sit on the commit row and in what order, so this reads exactly
    // that off the DOM rather than asserting markup offsets or a chain of
    // sibling hops. The row is reached through Apply, the control whose class
    // identifies it, and Clear is found on it by id: the pin fails if Clear
    // leaves the row, if a third control joins it, or if the two swap.
    [Fact]
    public void ApplyRow_CarriesApplyThenClear()
    {
        var cut = Render<FilterPanel>();

        var applyRow = cut.Find("button.btn-primary").ParentElement!;

        Assert.Equal(
            ["Apply Filter", "Clear filters"],
            applyRow.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));
        Assert.Equal("clearFilters", applyRow.QuerySelectorAll("button")[1].Id);
    }

    // Clearing raises the empty config, judged by the lib's own predicates —
    // GetActiveFacets() empty — never by re-inspecting config fields here.
    [Fact]
    public async Task ClearFilters_RaisesEmptyConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = RenderExpanded(
            parameters => parameters
                .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }),
            FilterFacet.Players, FilterFacet.ContactTypes);

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal");
        cut.Find("#ct_Race").Change(true);
        cut.Find("#errorMin").Input("0.05");

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Empty(capturedConfig!.GetActiveFacets());
    }

    // Clearing touches filter values only: every row stays exactly where the
    // user put it — an open row stays open…
    [Fact]
    public async Task ClearFilters_LeavesOpenRowsOpen()
    {
        var cut = RenderExpanded(FilterFacet.ContactTypes);

        cut.Find("#ct_Race").Change(true);
        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.Equal("true", cut.Find("#facetToggle_ContactTypes").GetAttribute("aria-expanded"));
        Assert.False(cut.Find("#ct_Race").HasAttribute("checked"));
    }

    // …and a closed one stays closed, even when the cleared values lived
    // inside it (staged via LoadConfig, so no row was ever opened). Its badge
    // goes out, which is the row moving no further than the filter did.
    [Fact]
    public async Task ClearFilters_LeavesClosedRowsClosed()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => cut.Instance.LoadConfig(
            new FilterConfig { ContactTypes = [ContactType.Race] }));

        await cut.Find("#clearFilters").ClickAsync(new());
        OpenMoreFilters(cut);

        Assert.Equal("false", cut.Find("#facetToggle_ContactTypes").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("span[id^='facetBadge_']"));
    }

    // The gesture's whole persisted side-effect surface is one write: the
    // empty config blob under ConfigKey. No open-rows write — and host
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

    // ── The ruled labels have one owner ────────────────────────────────────

    // Each ruled label is spelled once in the product tree — at its definition
    // on the panel — and in no other file but this suite's own ruling above.
    // That is precisely the claim the shipped panel broke: the words were
    // producer copy with five owners (the markup, and four sentences of the
    // help), so the flip could not be made anywhere without being made in five
    // places, and the help's four would have gone on describing a control that
    // no longer answered to them.
    //
    // Over SOURCE rather than over a rendered tree, because the failure this
    // catches is a second DEFINITION and a render cannot see one — a help
    // block that retyped the words would render identically today and drift
    // tomorrow. It reads the QUOTED literal, so the container's prose name in
    // comments and doc text is out of scope by construction: that name is the
    // codebase's vocabulary (moreFiltersToggle, MoreFiltersKey), not the
    // user's copy, and it does not flip.
    [Theory]
    [InlineData(FoldedLabel)]
    [InlineData(ExpandedLabel)]
    public void EachRuledLabel_IsSpelledOnceInTheProductTree(string label)
    {
        var literal = $"\"{label}\"";

        var spellings = SourceFiles()
            .Select(f => (f.Relative, Count: Occurrences(File.ReadAllText(f.Full), literal)))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.Relative, x => x.Count, StringComparer.Ordinal);

        Assert.Equal(
            new[]
            {
                "XgFilter_Razor.Tests/FilterPanelTests.cs",
                "XgFilter_Razor/Components/FilterPanel.razor",
            },
            spellings.Keys.Order(StringComparer.Ordinal));

        Assert.Equal(1, spellings["XgFilter_Razor/Components/FilterPanel.razor"]);
        Assert.Equal(1, spellings["XgFilter_Razor.Tests/FilterPanelTests.cs"]);
    }

    /// <summary>
    /// Every hand-written C# and Razor file in the repository, as (absolute,
    /// repo-relative) pairs. Rooted at this file's own compile-time location
    /// rather than at the runner's working directory — the idiom the host
    /// suites use to read source a build never copies to output. Generated
    /// output is excluded by directory: <c>obj</c> holds a <c>.g.cs</c> for
    /// every <c>.razor</c> here, and counting those would double every literal
    /// the survey exists to count once.
    /// </summary>
    private static IEnumerable<(string Full, string Relative)> SourceFiles(
        [CallerFilePath] string thisFile = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));

        // A root resolved to the wrong place would survey nothing, and the set
        // comparison would then fail with a message about missing files rather
        // than about a missing tree. Say the real cause here instead.
        Assert.True(
            Directory.Exists(Path.Combine(root, "XgFilter_Razor", "Components")),
            $"repository root resolved to '{root}', which is not this repo");

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal)
                     || f.EndsWith(".razor", StringComparison.Ordinal))
            .Select(f => (Full: f, Relative: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(f => !f.Relative.Split('/').Any(s => s is "bin" or "obj" or ".git"))
            .OrderBy(f => f.Relative, StringComparer.Ordinal);
    }

    /// <summary>
    /// How many times <paramref name="needle"/> occurs in
    /// <paramref name="text"/>, counting non-overlapping matches ordinally. A
    /// count, not a containment check: the defect the survey above exists to
    /// catch is a SECOND spelling inside a file that legitimately holds one.
    /// </summary>
    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
