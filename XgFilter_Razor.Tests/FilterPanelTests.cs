using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using BgDataTypes_Lib;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Lib.Patterns;
using XgFilter_Razor.Components.Internal;
using XgFilter_Razor.Testing;

namespace XgFilter_Razor.Tests;

public class FilterPanelTests : BunitContext
{
    // The source this suite's host holds. The panel renders the app-scoped
    // owner's state (FilterSetup), and a commit needs a source, so the fixture
    // reports one the way a host does before the panel mounts.
    private static readonly FilterSourceToken Source = FilterSourceToken.FromGeneration(1);

    private readonly RecordingRefusalSink _refusals = new();
    private readonly List<FilterConfig> _commits = [];
    private int _published;

    public FilterPanelTests()
    {
        // Loose mode — storage is incidental to most of this suite: every read
        // answers "nothing stored" and every write lands, through the real
        // BrowserStorage. A test whose subject IS storage puts a
        // BrowserStoragePlan on instead (Planned), and from then on every
        // storage call is the plan's, strictly.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();

        Setup.ReportSource(Source);

        // What the suite reads back: every snapshot the owner publishes after
        // this one (_published), and each commit — a new baseline — in order.
        FilterDraft? baseline = null;
        Setup.Attach(snapshot =>
        {
            _published++;
            if (!Equals(snapshot.Baseline, baseline))
            {
                baseline = snapshot.Baseline;
                if (baseline is not null) _commits.Add(baseline.ToConfig());
            }
        });
        _published = 0;
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    // The filter in effect for the suite's source, as a host reads its gate.
    private FilterConfig? InEffect => Setup.Current.ConfigInEffectFor(Source);

    // The selection the last commit (Apply or Clear filters) made the
    // baseline, or null when nothing has been committed.
    private FilterConfig? LastCommit => _commits.LastOrDefault();

    // Put a storage plan on this test's runtime; every storage call is the
    // plan's from here. The mount it is made for reads the boot's restoration
    // and the panel's two display preferences, so a planned test declares
    // those, here or through the helpers below.
    private BrowserStoragePlan Planned() => BrowserStoragePlan.On(JSInterop);

    // A boot whose restoration reads `restore`, answered, with nothing stored
    // for the display preferences; then the expansions a test needs, each
    // write planned as it will be made (the fold, then the open set after
    // each row). Returns the plan for the test to extend and verify.
    private (IRenderedComponent<FilterPanel> Cut, BrowserStoragePlan Plan) RenderRestoring(
        BrowserStorageReadAnswer restore, params FilterFacet[] expand)
    {
        var plan = Planned();
        plan.ExpectRead(BrowserStorageArea.Local, ConfigKey, restore);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, expand);

        var cut = Render<FilterPanel>();
        if (expand.Length > 0) ExpandFacets(cut, expand);
        return (cut, plan);
    }

    // A boot with nothing stored for the selection and the panel's two
    // display preferences answered as given — for the tests whose subject is
    // how the panel restores them.
    private (IRenderedComponent<FilterPanel> Cut, BrowserStoragePlan Plan) RenderWithPreferences(
        BrowserStorageReadAnswer fold, BrowserStorageReadAnswer rows)
    {
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectRead(BrowserStorageArea.Local, MoreFiltersKey, fold);
        plan.ExpectRead(BrowserStorageArea.Local, DisclosureKey, rows);
        return (Render<FilterPanel>(), plan);
    }

    // The writes ExpandFacets makes on a folded panel with no row open.
    private static void ExpectExpansion(BrowserStoragePlan plan, params FilterFacet[] expand)
    {
        if (expand.Length == 0) return;

        plan.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded);
        var open = new List<FilterFacet>();
        foreach (var facet in expand)
        {
            open.Add(facet);
            plan.ExpectFilterRowsToggle([.. open], BrowserStorageWriteAnswer.Succeeded);
        }
    }

    // The three keys the surface keeps in local storage: the committed
    // selection as one serialized blob, the set of expanded facet rows, and
    // whether the container over those rows is open — the last two user
    // preference, deliberately outside the selection's blob. Independent
    // literals, so a renamed key fails here: a rename silently loses every
    // user's remembered state, which is a migration question, not a refactor.
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
    // The verdicts the shown line speaks: its parts that are true now, not
    // the ones holding the place of a kind it has shown and the list no
    // longer has (halheinrich/backgammon#272).
    private static string[] MatchScoreVerdicts(IRenderedComponent<FilterPanel> cut) =>
        cut.FindAll("#matchScoreFeedback span:not(.invisible)")
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

    // The panel holds no filter state and reports to nobody: everything it
    // shows comes from the app-scoped owner and every gesture goes to it
    // (halheinrich/backgammon#374). So it declares no parameter at all — a
    // callback or a state parameter added here would be a second channel for
    // one fact, the defect the owner exists to end.
    [Fact]
    public void ThePanel_DeclaresNoParameters()
    {
        Assert.DoesNotContain(
            typeof(FilterPanel).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            p => p.GetCustomAttribute<ParameterAttribute>() is not null);
    }

    [Fact]
    public async Task ApplyButton_CommitsTheSelection()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("0.05");

        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Equal(DecisionTypeOption.Both, LastCommit!.DecisionType);
        Assert.Equal(0.05, LastCommit.ErrorMin);
        Assert.Equal(LastCommit, InEffect);
    }

    // ── Apply gate & what is in effect ───────────────────────────────────────
    // The owner holds both the draft and the baseline committed for this
    // setup, so it answers whether the selection on screen is the one in
    // effect, and the Apply gate is that same answer's other face. The tests
    // below pin the two surfaces — the Apply button's disabled state and the
    // host's read of what is in effect — which come from one snapshot and must
    // therefore never disagree.

    // A first visit has chosen nothing, and the empty selection is ready
    // without Apply (§1, halheinrich/backgammon#266): it is in effect, so
    // there is nothing to apply, and the panel says why the button is dark —
    // in words that are true of a selection nobody applied.
    [Fact]
    public void FreshMount_TheEmptySelectionIsInEffect_AndApplyHasNothingToDo()
    {
        var cut = Render<FilterPanel>();

        Assert.Equal(new FilterConfig(), InEffect);
        Assert.Empty(_commits);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Contains("no filter is set", cut.Find("#applyDisabledReason").TextContent);
    }

    // Applying commits: the draft becomes this setup's baseline, it is in
    // effect for the host, the button disables itself, and the panel says why
    // the button is dead.
    [Fact]
    public async Task Apply_DisablesItself_AndPutsTheSelectionInEffect()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("0.05");
        Assert.Null(InEffect);
        await Apply(cut).ClickAsync(new());

        Assert.Equal(0.05, Assert.Single(_commits).ErrorMin);
        Assert.Equal(LastCommit, InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Contains("already applied", cut.Find("#applyDisabledReason").TextContent);
    }

    // Any edit moves the draft off the baseline: nothing is in effect, Apply
    // re-opens, and the disabled-reason disappears rather than going stale.
    [Fact]
    public async Task EditAfterApply_ReEnablesApply_AndNothingIsInEffect()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("0.1");

        Assert.Null(InEffect);
        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // The wedge this design exists to kill. What is in effect is derived from
    // value equality against the baseline — not a one-way dirty flag — so an
    // edit undone back to the applied values is in effect again and the gate
    // closes. Under a dirty flag the selection would stay "dirty" with Apply's
    // own equality check disabling the only control that could clear it: a
    // host gating on the flag would be stranded with no recovery gesture.
    [Fact]
    public async Task EditUndoneBackToCommittedValues_IsInEffectAgain()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        var applied = LastCommit;

        ErrorMin(cut).Input("0.1");
        Assert.Null(InEffect);
        Assert.False(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("0.05");
        Assert.Equal(applied, InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Clear filters is a commit like Apply — the empty selection becomes the
    // baseline and is remembered — so the empty config is in effect, Apply
    // disables, and the next edit re-opens it.
    [Fact]
    public async Task ClearFilters_CommitsTheDefaults_AndDisablesApply()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("0.05");
        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Null(LastCommit!.ErrorMin);
        Assert.Equal(LastCommit, InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("0.05");
        Assert.Null(InEffect);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // A source change ends the setup: the baseline drops, so nothing is in
    // effect and Apply re-arms, and the draft — the user's choices — stays
    // exactly as they left it (§1, "The dir is changed"). The host reports it;
    // the still-mounted panel follows the owner.
    [Fact]
    public async Task ASourceChange_ReArmsApply_LeavesTheDraftUntouched()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        var next = FilterSourceToken.FromGeneration(2);
        await cut.InvokeAsync(() => Setup.ReportSource(next));

        Assert.Null(Setup.Current.ConfigInEffectFor(next));
        Assert.False(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
        Assert.Equal("0.05", ErrorMin(cut).GetAttribute("value"));
    }

    // Staging a saved filter stages, never commits — so staging anything
    // other than the applied selection leaves nothing in effect, and Apply
    // re-opens.
    [Fact]
    public async Task Staging_ADifferentSelection_LeavesNothingInEffect_AndEnablesApply()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { Players = ["Magriel"] }));

        Assert.Null(InEffect);
        Assert.Single(_commits);
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // ...and staging exactly what was applied is a genuinely clean state: it is
    // in effect. The staged instance is built independently, which is the
    // point: the comparison is FilterConfig's value equality, not identity.
    [Fact]
    public async Task Staging_ExactlyTheAppliedSelection_IsInEffect()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());

        ErrorMin(cut).Input("0.9");
        Assert.Null(InEffect);

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { ErrorMin = 0.05 }));

        Assert.Equal(new FilterConfig { ErrorMin = 0.05 }, InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Validity and being in effect compose: a valid draft not in effect is
    // what Apply needs. Here nothing is in effect — the draft moved — yet the
    // unparseable pattern text keeps Apply disabled. The panel volunteers no
    // disabled-reason line for this case: the pattern field's own
    // invalid-feedback already explains it, and repeating it here would be a
    // second encoding of the same rule.
    [Fact]
    public async Task InvalidPositionPattern_DisablesApply_ThoughNothingIsInEffect()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2,]");
        await Apply(cut).ClickAsync(new());
        Assert.True(Apply(cut).HasAttribute("disabled"));

        cut.Find("#positionPattern").Input("[6,2");

        Assert.Null(InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // A reload ends the setup. The boot's restoration puts the stored
    // selection on screen — it does not commit it — so nothing is in effect,
    // nothing was committed, and Apply is offered over a fully populated
    // panel (§4: choices outlive the setup, consent does not).
    [Fact]
    public void ARestoredSelection_IsOnScreen_NotInEffect_AndApplyIsOffered()
    {
        var (cut, plan) = RenderRestoring(FilterSurfaceStorage.RestoreAnswer(new FilterConfig { ErrorMin = 0.05 }));

        Assert.Equal("0.05", ErrorMin(cut).GetAttribute("value"));
        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        Assert.Empty(_commits);
        Assert.Null(InEffect);
        Assert.False(Apply(cut).HasAttribute("disabled"));
        plan.Verify();
    }

    // A second Apply on an unchanged selection commits nothing — no second
    // baseline, no second write. The owner guards Apply on the gate the button
    // renders `disabled` from, so the contract survives an event dispatch
    // that ignores the disabled attribute.
    [Fact]
    public async Task ApplyTwiceWithoutEditing_CommitsOnlyOnce()
    {
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        plan.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.05 }, BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("0.05");
        await Apply(cut).ClickAsync(new());
        await Apply(cut).ClickAsync(new());

        Assert.Single(_commits);
        plan.Verify();
    }

    // The stale-binding half of the silent-splat discipline. Razor emits an
    // unrecognized component attribute without complaint at build time, so a
    // consumer still carrying a binding this panel no longer declares —
    // `OnAppliedStateChanged`, retired with the owner — compiles green. It
    // must not then run green: this panel deliberately declares no
    // CaptureUnmatchedValues catch-all, so the renderer rejects the unmatched
    // attribute outright. Built here through RenderTreeBuilder because that is
    // precisely what a stale Razor binding compiles down to — a named
    // AddAttribute the component has no property for. Adding a catch-all to
    // the panel would silently turn this exception back into a dead handler.
    [Fact]
    public void StaleParameterBinding_ThrowsAtRender()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Render(builder =>
        {
            builder.OpenComponent<FilterPanel>(0);
            builder.AddAttribute(1, "OnAppliedStateChanged", EventCallback.Empty);
            builder.CloseComponent();
        }));

        Assert.Contains("OnAppliedStateChanged", ex.Message);
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

    // The ruled contact-type vocabulary in its ruled order, member token paired
    // with the [Description] label the user reads — an independent literal, not
    // a projection of Enum.GetValues, for the reason RuledLevelVocabulary gives:
    // the panel derives these checkboxes from the enum, so an expectation built
    // the same way moves with the producer and can never fail. A rename,
    // reorder, relabel, insertion or removal in XgFilter_Lib's ContactType
    // changes what the user sees, and breaks this so it is ruled on here
    // (halheinrich/backgammon#162).
    private static readonly (string Member, string Label)[] RuledContactVocabulary =
    [
        ("Contact", "Contact"),
        ("Race",    "Race"),
    ];

    // The contact-type row renders exactly that vocabulary, in exactly that
    // order, labelled exactly that way — the level facet's pin, for the one
    // other enum-derived checkbox group. Sequence equality over the ids pins
    // membership too: an extra or missing checkbox fails here.
    [Fact]
    public void ContactTypeSection_RendersTheRuledVocabularyInTheRuledOrder()
    {
        var cut = RenderExpanded(FilterFacet.ContactTypes);

        Assert.Equal(
            RuledContactVocabulary.Select(v => v.Member),
            cut.FindAll("input[id^='ct_']").Select(el => el.Id!["ct_".Length..]));

        foreach (var (member, label) in RuledContactVocabulary)
        {
            Assert.Equal("checkbox", cut.Find($"#ct_{member}").GetAttribute("type"));
            Assert.Equal(label, cut.Find($"label[for='ct_{member}']").TextContent.Trim());
        }
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

    // The single-key persistence path, in its two halves. Apply writes the
    // committed selection as one blob, in the lib's own JSON for it — the plan
    // holds the write to exactly that value — and a later boot's restoration
    // of that same blob puts the same selection back on screen.
    [Fact]
    public async Task Apply_WritesTheSelectionAsTheLibsJson()
    {
        var applied = new FilterConfig
        {
            Players = ["Hal", "Magriel"],
            ErrorMin = 0.05,
            DecisionType = DecisionTypeOption.CheckerPlaysOnly,
            ContactTypes = [ContactType.Race],
        };
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);
        plan.ExpectWrite(BrowserStorageArea.Local, ConfigKey, applied.ToJson(), BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal, Magriel");
        cut.Find("#errorMin").Input("0.05");
        cut.Find("#dt_CheckerPlaysOnly").Change(true);
        cut.Find("#ct_Race").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        plan.Verify();
    }

    [Fact]
    public void Restore_PutsTheStoredSelectionOnScreen()
    {
        var stored = new FilterConfig
        {
            Players = ["Hal", "Magriel"],
            ErrorMin = 0.05,
            DecisionType = DecisionTypeOption.CheckerPlaysOnly,
            ContactTypes = [ContactType.Race],
        };

        var (restored, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(stored.ToJson()),
            FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

        Assert.Equal("Hal, Magriel", restored.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.Equal("0.05", restored.Find("#errorMin").GetAttribute("value"));
        Assert.True(restored.Find("#dt_CheckerPlaysOnly").HasAttribute("checked"));
        Assert.True(restored.Find("#ct_Race").HasAttribute("checked"));
        plan.Verify();
    }

    // Silent-splat guard for the Contact-type section: an unbound Razor checkbox
    // attribute compiles fine but never mutates state, so check a box, Apply, and
    // assert the emitted config actually carries the selection. Pins that the new
    // #ct_* checkboxes bind to FilterConfig.ContactTypes.
    [Fact]
    public async Task ContactTypeCheckbox_FlowsIntoEmittedConfig()
    {
        var cut = RenderExpanded(FilterFacet.ContactTypes);

        cut.Find("#ct_Contact").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Contains(ContactType.Contact, LastCommit!.ContactTypes);
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
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        cut.Find("#md_Rollout").Change(true);
        CheckModeAndExpandLevels(cut, AnalysisMode.Evaluation);
        cut.Find("#lv_Evaluation_XgRollerPlusPlus").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.True(LastCommit!.IncludeEvaluations);
        Assert.Equal(new[] { AnalysisLevel.XgRollerPlusPlus }, LastCommit.EvaluationLevels);
        Assert.True(LastCommit.IncludeRollouts);
        Assert.Empty(LastCommit.RolloutLevels);
        Assert.False(LastCommit.IncludeBookRollouts);
        Assert.Empty(LastCommit.BookRolloutLevels);
    }

    // Silent-splat guard for the level axis, sharpened to the per-mode
    // contract: levels checked under Book rollouts — including Unknown, the
    // deliberate opt-in for unenriched book hits — land in BookRolloutLevels
    // and only there, never in a sibling mode's list.
    [Fact]
    public async Task LevelCheckbox_FlowsIntoItsOwnModesListOnly()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.BookRollout);
        cut.Find("#lv_BookRollout_Unknown").Change(true);
        cut.Find("#lv_BookRollout_Ply3").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Contains(AnalysisLevel.Unknown, LastCommit!.BookRolloutLevels);
        Assert.Contains(AnalysisLevel.Ply3, LastCommit.BookRolloutLevels);
        Assert.Equal(2, LastCommit.BookRolloutLevels.Count);
        Assert.Empty(LastCommit.EvaluationLevels);
        Assert.Empty(LastCommit.RolloutLevels);
    }

    // The deliberate keep-on-untoggle behavior: unchecking a mode hides its
    // group but keeps the checked levels — in the draft, so re-toggling
    // restores the user's selection, and in the config, where the lib
    // guarantees a level list whose toggle is off is inert (no activation, no
    // constraint). Inert, it restricts nothing: with nothing else set, that is
    // the empty selection, in effect without Apply — and carrying its inert
    // levels. An exploratory untoggle costs nothing.
    [Fact]
    public void LevelSelections_SurviveModeUntoggle()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);
        cut.Find("#lv_Rollout_Ply4").Change(true);

        cut.Find("#md_Rollout").Change(false);
        Assert.Empty(cut.FindAll("input[id^='lv_Rollout_']"));

        Assert.NotNull(InEffect);
        Assert.False(InEffect!.IncludeRollouts);
        Assert.Equal(new[] { AnalysisLevel.Ply4 }, InEffect.RolloutLevels);

        cut.Find("#md_Rollout").Change(true);
        Assert.True(cut.Find("#lv_Rollout_Ply4").HasAttribute("checked"));
    }

    // Every depth control is an edit the owner hears — it publishes the
    // change, so a host's gate follows — and neither disclosure tier is:
    // opening the facet's own row and expanding a level group are both
    // navigation, and the owner publishes nothing for them.
    [Fact]
    public void AnalysisDepthControls_AreEdits_DisclosuresAreNot()
    {
        var cut = Render<FilterPanel>();
        _published = 0;   // the mount's restoration settling is a change of its own

        ExpandFacets(cut, FilterFacet.AnalysisDepth);
        Assert.Equal(0, _published);

        cut.Find("#md_Rollout").Change(true);
        Assert.Equal(1, _published);

        cut.Find("#lvlToggle_Rollout").Click();
        Assert.Equal(1, _published);

        cut.Find("#lv_Rollout_Ply4").Change(true);
        Assert.Equal(2, _published);

        cut.Find("#md_BookRollout").Change(true);
        Assert.Equal(3, _published);
    }

    // The level-group disclosure is deliberately unpersisted — unlike the two
    // disclosures above it, each with its own key, toggling a level group
    // writes nothing: the collapsed badge already carries everything the
    // closed state hides, so there is no choice worth remembering. The plan
    // holds the scenario to the writes the expansion itself makes.
    [Fact]
    public void LevelGroupToggle_WritesNothing()
    {
        var (cut, plan) = RenderRestoring(BrowserStorageReadAnswer.Absent, FilterFacet.AnalysisDepth);
        cut.Find("#md_Rollout").Change(true);

        cut.Find("#lvlToggle_Rollout").Click();
        cut.Find("#lvlToggle_Rollout").Click();

        plan.Verify();
    }

    // Deselecting everything back to nothing is the inactive state — all
    // three toggles off with empty level lists — "facet off," not "reject
    // everything." With nothing else set, that is the empty selection, ready
    // without Apply (halheinrich/backgammon#266): in effect as it stands.
    [Fact]
    public void AnalysisDepth_DeselectedToEmpty_IsTheInactiveState_InEffect()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);
        cut.Find("#lv_Rollout_Ply3").Change(true);
        cut.Find("#lv_Rollout_Ply3").Change(false);
        cut.Find("#md_Rollout").Change(false);

        var inEffect = InEffect;
        Assert.NotNull(inEffect);
        Assert.False(inEffect!.IncludeEvaluations);
        Assert.False(inEffect.IncludeRollouts);
        Assert.False(inEffect.IncludeBookRollouts);
        Assert.Empty(inEffect.EvaluationLevels);
        Assert.Empty(inEffect.RolloutLevels);
        Assert.Empty(inEffect.BookRolloutLevels);
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Clear filters must reset all six depth fields — every toggle off (which
    // also removes the level groups from the DOM) and every level list empty,
    // including levels kept inert by an earlier untoggle: Clear is the
    // full-clear gesture, so nothing survives it.
    [Fact]
    public async Task ClearFilters_ResetsAllSixDepthFields()
    {
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.Rollout);
        cut.Find("#lv_Rollout_Ply4").Change(true);
        cut.Find("#md_Rollout").Change(false);   // Ply4 now kept inert
        cut.Find("#md_Evaluation").Change(true);
        cut.Find("#md_BookRollout").Change(true);

        await cut.Find("#clearFilters").ClickAsync(new());

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));

        Assert.NotNull(LastCommit);
        Assert.False(LastCommit!.IncludeEvaluations);
        Assert.False(LastCommit.IncludeRollouts);
        Assert.False(LastCommit.IncludeBookRollouts);
        Assert.Empty(LastCommit.EvaluationLevels);
        Assert.Empty(LastCommit.RolloutLevels);
        Assert.Empty(LastCommit.BookRolloutLevels);
    }

    // The depth facet through both halves of the persistence path: the six
    // raw-intent members written as the lib writes them (level lists as
    // member-name strings, toggles as booleans), and restored exactly. The
    // restored group mounts collapsed — the disclosure is session state, never
    // persisted — with its badge honestly reporting the restored count before
    // any expansion.
    [Fact]
    public async Task AnalysisDepth_IsWrittenAsTheLibWritesIt()
    {
        var applied = new FilterConfig
        {
            IncludeBookRollouts = true,
            BookRolloutLevels = [AnalysisLevel.Ply3, AnalysisLevel.Ply7],
            IncludeRollouts = true,
        };
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.AnalysisDepth);
        plan.ExpectWrite(BrowserStorageArea.Local, ConfigKey, applied.ToJson(), BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.BookRollout);
        cut.Find("#lv_BookRollout_Ply3").Change(true);
        cut.Find("#lv_BookRollout_Ply7").Change(true);
        cut.Find("#md_Rollout").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        plan.Verify();
    }

    [Fact]
    public void AnalysisDepth_IsRestoredExactly_WithItsGroupsCollapsed()
    {
        var stored = new FilterConfig
        {
            IncludeBookRollouts = true,
            BookRolloutLevels = [AnalysisLevel.Ply3, AnalysisLevel.Ply7],
            IncludeRollouts = true,
        };

        var (restored, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(stored.ToJson()), FilterFacet.AnalysisDepth);

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
        plan.Verify();
    }

    // Ply3Red through both halves of the persistence path, against a literal
    // wire token. It is the newest member of the level vocabulary
    // (halheinrich/backgammon#159) and the one a serializer or producer change
    // is likeliest to drop or fold away, so pin it by name: the blob the
    // panel writes for it — the plan holds the write to exactly this value —
    // carries the string "Ply3Red", re-typed here on purpose rather than read
    // back off the enum; and a restore of that blob checks it under its own
    // "3-ply Red" label.
    //
    // The closing assertion is the one that matters most: Ply3 must come back
    // unchecked. XG's "3-ply Red" was a label variant of Ply3 before the
    // interleave gave it its own identity, and a regression that folded the
    // two back together would round-trip perfectly while quietly widening the
    // user's filter.
    [Fact]
    public async Task AnalysisDepth_Ply3Red_IsWrittenUnderItsOwnWireToken()
    {
        var blob = new FilterConfig { IncludeEvaluations = true, EvaluationLevels = [AnalysisLevel.Ply3Red] }.ToJson();
        Assert.Contains("\"Ply3Red\"", blob);
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.AnalysisDepth);
        plan.ExpectWrite(BrowserStorageArea.Local, ConfigKey, blob, BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderExpanded(FilterFacet.AnalysisDepth);

        CheckModeAndExpandLevels(cut, AnalysisMode.Evaluation);
        cut.Find("#lv_Evaluation_Ply3Red").Change(true);
        await Apply(cut).ClickAsync(new());

        plan.Verify();
    }

    [Fact]
    public void AnalysisDepth_Ply3Red_IsRestoredUnderItsOwnLabel_AndNotAsPly3()
    {
        var blob = new FilterConfig { IncludeEvaluations = true, EvaluationLevels = [AnalysisLevel.Ply3Red] }.ToJson();

        var (restored, plan) = RenderRestoring(BrowserStorageReadAnswer.Stored(blob), FilterFacet.AnalysisDepth);

        restored.Find("#lvlToggle_Evaluation").Click();
        Assert.True(restored.Find("#lv_Evaluation_Ply3Red").HasAttribute("checked"));
        Assert.Equal("3-ply Red",
            restored.Find("label[for='lv_Evaluation_Ply3Red']").TextContent.Trim());
        Assert.DoesNotContain("checked", restored.Find("#lv_Evaluation_Ply3").OuterHtml);
        plan.Verify();
    }

    // Persistence back-compat: a blob saved before the depth pairs existed
    // carries none of the three toggles or level lists. TryFromJson must
    // restore the facet inactive — no toggle checked, no level group rendered —
    // which falls out of System.Text.Json leaving the initialized defaults for
    // the absent members. Verified here rather than assumed.
    [Fact]
    public void LegacyConfigWithoutDepthFields_RestoresToInactive()
    {
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored("{\"DecisionType\":\"Both\"}"), FilterFacet.AnalysisDepth);

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
        plan.Verify();
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
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored("{\"DecisionType\":\"Both\",\"IncludeEvaluations\":true," +
                "\"EvaluationLevels\":[5]}"), FilterFacet.AnalysisDepth);

        // Byte-identical to ConfigWithNamedLevel_Restores below except for the
        // one token, so this pair discriminates: that blob renders the toggle,
        // this one must not. IncludeEvaluations is set precisely so the
        // assertion would FAIL if the ordinal were honoured.
        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
        plan.Verify();
    }

    // The same blob with the level spelled as its member name DOES restore, so
    // the rejection above is about token kind and not a broken read path.
    [Fact]
    public void ConfigWithNamedLevel_Restores()
    {
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored("{\"DecisionType\":\"Both\",\"IncludeEvaluations\":true," +
                "\"EvaluationLevels\":[\"XgRoller\"]}"), FilterFacet.AnalysisDepth);

        cut.Find("#lvlToggle_Evaluation").Click();
        Assert.True(cut.Find("#lv_Evaluation_XgRoller").HasAttribute("checked"));
        plan.Verify();
    }

    // Migration guard: blobs saved under the two retired depth shapes — the
    // flat AnalysisDepthClasses axis and the shared AnalysisLevels list —
    // carry field names no current member answers to. System.Text.Json ignores
    // them as unknown properties, so the facet restores inactive rather than
    // throwing — the accepted reset-on-read path for old saved configs.
    [Fact]
    public void ConfigWithRetiredDepthFields_IsIgnored_RestoresToInactive()
    {
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored("{\"DecisionType\":\"Both\"," +
                "\"AnalysisDepthClasses\":[\"Ply3\",\"RolloutPly7\"]," +
                "\"AnalysisLevels\":[\"Ply3\",\"XgRollerPlus\"]}"), FilterFacet.AnalysisDepth);

        foreach (var mode in SelectableModes)
            Assert.False(cut.Find($"#md_{mode}").HasAttribute("checked"));
        Assert.Empty(cut.FindAll("button[id^='lvlToggle_']"));
        plan.Verify();
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
        var cut = RenderExpanded(FilterFacet.DiceRolls);

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_55").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Contains(new DiceRoll(3, 1), LastCommit!.DiceRolls);
        Assert.Contains(new DiceRoll(5, 5), LastCommit.DiceRolls);
        Assert.Equal(2, LastCommit.DiceRolls.Count);
    }

    // The dice facet through both halves of the persistence path: rolls
    // written as the lib writes them (two-digit tokens via DiceRoll's own
    // converter), and restored checked — the "pre-populated config renders
    // checked" coverage.
    [Fact]
    public async Task DiceRolls_AreWrittenAsTheLibWritesThem()
    {
        var applied = new FilterConfig { DiceRolls = [new DiceRoll(3, 1), new DiceRoll(6, 6)] };
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.DiceRolls);
        plan.ExpectWrite(BrowserStorageArea.Local, ConfigKey, applied.ToJson(), BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderExpanded(FilterFacet.DiceRolls);

        cut.Find("#dr_66").Change(true);
        cut.Find("#dr_31").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        plan.Verify();
    }

    [Fact]
    public void DiceRolls_AreRestoredChecked()
    {
        var stored = new FilterConfig { DiceRolls = [new DiceRoll(3, 1), new DiceRoll(6, 6)] };

        var (restored, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(stored.ToJson()), FilterFacet.DiceRolls);

        Assert.True(restored.Find("#dr_31").HasAttribute("checked"));
        Assert.True(restored.Find("#dr_66").HasAttribute("checked"));
        Assert.DoesNotContain("checked", restored.Find("#dr_21").OuterHtml);
        plan.Verify();
    }

    // Deselecting every checked roll back to none is the inactive state — an
    // empty DiceRolls list, "facet off," not "reject everything" — and, with
    // nothing else set, the empty selection, in effect without Apply.
    [Fact]
    public void DiceRolls_DeselectedToEmpty_IsTheInactiveState_InEffect()
    {
        var cut = RenderExpanded(FilterFacet.DiceRolls);

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_31").Change(false);

        Assert.NotNull(InEffect);
        Assert.Empty(InEffect!.DiceRolls);
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // Silent-splat guard for the Position-pattern field: an unbound text input
    // would compile but never feed BuildConfig, so type a valid bracket list,
    // Apply, and assert the emitted config carries the text as typed
    // (halheinrich/backgammon#269: the text is the stored value, and the
    // panel builds no BoardPattern for the config's sake). The second
    // assertion is the lib's own reading of that text, built from the
    // constraints rather than from text — so the panel is pinned both to
    // hand the user's spelling through untouched and to hand through text
    // that means what was typed.
    [Fact]
    public async Task PositionPattern_FlowsIntoEmittedConfig()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2,] [5,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Equal("[6,2,] [5,,-2]", LastCommit!.PositionPattern);
        Assert.Equal(
            new BoardPattern([new CheckerRange(6, 2, null), new CheckerRange(5, null, -2)]),
            BoardPattern.Parse(LastCommit.PositionPattern!));
    }

    // The range token reaches the wire the same way (halheinrich/backgammon#268):
    // one side's total across a span, the side named by the bounds' sign. Both
    // signs ride in one pattern, so a panel that dropped or re-signed either
    // would emit different text and fail the comparison.
    [Fact]
    public async Task PositionPatternRange_FlowsIntoEmittedConfig()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[7-12,3,] [13-18,,-2]");
        await Apply(cut).ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Equal("[7-12,3,] [13-18,,-2]", LastCommit!.PositionPattern);
    }

    // The range forms the grammar refuses land in the field's invalid state
    // like any other unparseable text: the field reds, its invalid-entry line
    // is the sibling Bootstrap reveals, and Apply is withheld. Each row is one
    // refusal the grammar owns — opposite-signed range bounds, a range that
    // does not run low to high, and the bar rule on a single-location token
    // from each side. The panel runs no check of its own, so these prove only
    // that it asks TryParse and marks what it hears; the rules themselves are
    // pinned in XgFilter_Lib.
    [Theory]
    [InlineData("[7-12,-1,3]")]   // opposite-signed bounds
    [InlineData("[12-7,1,]")]     // start not below end
    [InlineData("[0,1,]")]        // opponent's bar, positive bound
    [InlineData("[25,,-1]")]      // on-roll player's bar, negative bound
    public void RefusedPatternForm_MarksFieldWithItsMessage_AndWithholdsApply(string text)
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input(text);
        cut.Find("#positionPattern").Blur();

        Assert.Contains("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.NotNull(cut.Find("#positionPattern ~ .invalid-feedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // The bar rule belongs to single-location tokens only: a range may take in
    // either bar whichever side it counts. Pinned from the panel so a local
    // pre-check that over-applied the bar rule to ranges would red a pattern
    // the grammar accepts.
    [Theory]
    [InlineData("[24-25,-2,-2]")]  // on-roll player's bar, opponent's count
    [InlineData("[0-6,3,]")]       // opponent's bar, on-roll player's count
    public void RangeAcrossABar_EitherSign_LeavesFieldClean(string text)
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);
        cut.Find("#positionPattern").Input(text);
        cut.Find("#positionPattern").Blur();

        Assert.DoesNotContain("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // Every example the placeholder advertises is a pattern the grammar
    // accepts, and one of them is a range — the placeholder is where most
    // users first meet the syntax, so it must show the token this facet
    // gained. The match-score placeholder's invariant, for this field.
    [Fact]
    public void PositionPatternPlaceholder_IsAnAcceptedPattern_ThatIncludesARange()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        var placeholder = cut.Find("#positionPattern").GetAttribute("placeholder")!;
        Assert.StartsWith("e.g. ", placeholder);

        Assert.True(BoardPattern.TryParse(placeholder["e.g. ".Length..], out var pattern));
        Assert.Contains(pattern!.Constraints, c => c is CheckerSpanRange);
    }

    // Hal's ruling on halheinrich/backgammon#275: the field's hint states how
    // the opponent's "at least n" and "at most n" are written, with the
    // ruling's own examples — the same oracle FilterHelp's section is pinned
    // against, so the two surfaces answer to one ruling.
    [Fact]
    public void PositionPatternHint_StatesTheOpponentsAtLeastAndAtMost_AsRuled()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        OpponentBoundRuling.AssertStatedIn(cut.Find("#positionPattern ~ .form-text").TextContent);
    }

    // The panel is where users type the grammar by hand, so pin the borne-off
    // vocabulary at the wire: an off/opp-off pattern must reach the emitted
    // config spelled exactly as typed — the text is the stored value
    // (halheinrich/backgammon#269), so the panel neither canonicalizes nor
    // pre-chews it — while meaning what the canonical spelling means. The
    // lib parses the names case-insensitively, and its config equality reads
    // the pattern by meaning where both texts parse; a panel that lower-cased
    // the text would fail the first assertion, and one that mangled a name
    // would fail the second.
    [Fact]
    public async Task PositionPatternWithOffTokens_FlowsIntoEmittedConfigAsTyped()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[OFF,10,] [Opp-Off,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Equal("[OFF,10,] [Opp-Off,,-2]", LastCommit!.PositionPattern);
        Assert.Equal(
            new FilterConfig { PositionPattern = "[off,10,] [opp-off,,-2]" }, LastCommit);
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
        cut.Find("#positionPattern").Blur();

        Assert.Contains("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));
    }

    // The pattern through both halves of the persistence path: written as the
    // text it is, and restored as that text.
    [Fact]
    public async Task PositionPattern_IsWrittenAsTypedText()
    {
        var applied = new FilterConfig { PositionPattern = "[6,2,] [5,,-2]" };
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.PositionPattern);
        plan.ExpectWrite(BrowserStorageArea.Local, ConfigKey, applied.ToJson(), BrowserStorageWriteAnswer.Succeeded);
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2,] [5,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        plan.Verify();
    }

    [Fact]
    public void PositionPattern_IsRestoredAsTheStoredText()
    {
        var (restored, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(new FilterConfig { PositionPattern = "[6,2,] [5,,-2]" }.ToJson()),
            FilterFacet.PositionPattern);

        Assert.Equal("[6,2,] [5,,-2]", restored.Find("#positionPattern").GetAttribute("value"));
        plan.Verify();
    }

    // A blank Position-pattern field means "no pattern filter," and the panel
    // commits that as the lib's null rather than as blank text: null is the
    // value a fresh config carries, so a field never touched and a field
    // cleared both build the config a fresh panel builds, and no document is
    // minted carrying an empty pattern the user never wrote.
    [Fact]
    public async Task EmptyPositionPattern_CommitsNullPattern()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);
        ErrorMin(cut).Input("0.05");
        cut.Find("#positionPattern").Input("[6,2,]");
        cut.Find("#positionPattern").Input(string.Empty);

        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Null(LastCommit!.PositionPattern);
    }

    // Invalid bracket-list text must not silently drop the filter: the chosen
    // UX marks the field invalid and gates Apply (disabled) until it parses or
    // is cleared. Clearing the bad text re-enables Apply.
    [Fact]
    public void InvalidPositionPattern_MarksFieldAndGatesApply()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2");
        cut.Find("#positionPattern").Blur();

        Assert.Contains("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.True(cut.Find("button.btn-primary").HasAttribute("disabled"));

        cut.Find("#positionPattern").Input(string.Empty);
        cut.Find("#positionPattern").Blur();

        // Cleared, the field is unmarked and the panel is back to the empty
        // selection — in effect, so Apply is off for the other reason.
        Assert.DoesNotContain("is-invalid", cut.Find("#positionPattern").GetAttribute("class"));
        Assert.Equal(new FilterConfig(), InEffect);
        Assert.Contains("no filter is set", cut.Find("#applyDisabledReason").TextContent);
    }

    // Proves the FilterConfig.TryFromJson tolerant path is wired: a stored
    // value that is not a selection restores the defaults rather than throwing,
    // and the restoration says what it found.
    [Fact]
    public void CorruptStoredConfig_MountsWithDefaults()
    {
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored("}{ not valid json"),
            FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

        Assert.Equal(string.Empty, cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.True(cut.Find("#dt_Both").HasAttribute("checked"));
        Assert.DoesNotContain("checked", cut.Find("#ct_Race").OuterHtml);
        Assert.Equal(FilterRestoration.Unreadable, Setup.Current.Restoration);
        plan.Verify();
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

    // The placeholder's shape, not only its members: PlaceholderExamples trims
    // each entry, so a lost or doubled separator space reads back the same list
    // and passes every membership pin. Rebuilding the attribute from that list
    // with exactly ", " between entries catches it — the same helper on both
    // sides, so the two cannot disagree about what an example is.
    [Fact]
    public void MatchScorePlaceholder_ExamplesAreSeparatedByExactlyCommaSpace()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        Assert.Equal(
            "e.g. " + string.Join(", ", PlaceholderExamples(cut)),
            MatchScores(cut).GetAttribute("placeholder"));
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
        MatchScores(cut).Blur();
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
        MatchScores(cut).Blur();

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
        MatchScores(cut).Blur();

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
        MatchScores(cut).Blur();

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
        MatchScores(cut).Blur();

        var verdict = Assert.Single(MatchScoreVerdicts(cut));
        Assert.Equal([MatchScoreToken.RetiredMoney], RetiredWordsIn(verdict));
        foreach (var replacement in MatchScoreToken.RetiredMoneyReplacements)
            Assert.Contains(replacement, verdict);
    }

    // Two voices, not one line with a swapped noun — the remedies genuinely
    // differ (retype it versus use these instead), so a box holding both
    // kinds gets both, in the order the panel renders them.
    [Fact]
    public void MatchScoreVerdicts_AreOneVoicePerFaultKind()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input("not-a-score");
        MatchScores(cut).Blur();
        var malformed = Assert.Single(MatchScoreVerdicts(cut));

        MatchScores(cut).Input(MatchScoreToken.RetiredMoney);
        MatchScores(cut).Blur();
        var retired = Assert.Single(MatchScoreVerdicts(cut));

        Assert.NotEqual(malformed, retired);

        MatchScores(cut).Input($"not-a-score, {MatchScoreToken.RetiredMoney}");
        MatchScores(cut).Blur();

        Assert.Equal([malformed, retired], MatchScoreVerdicts(cut));
    }

    // One kind of mistake is explained once, however many entries made it —
    // the verdict is per fault kind, not per offending token.
    [Fact]
    public void ManyFaultedTokensOfOneKind_SpeakWithOneVoice()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input("not-a-score, 0a5a, 3a5aC");
        MatchScores(cut).Blur();

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
        MatchScores(cut).Blur();
        Assert.True(Apply(cut).HasAttribute("disabled"));

        MatchScores(cut).Input(MatchScoreToken.MoneyWithoutJacoby);
        MatchScores(cut).Blur();

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
        MatchScores(cut).Blur();

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
        ErrorMin(cut).Blur();

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

    // The lib's documented posture for the retired spelling, end to end: a
    // stored selection written before the money token split still loads,
    // still shows the token it holds, marks the field, and is refused a
    // commit — never silently rewritten to one of the replacements (which
    // would change the user's filter behind their back), never silently
    // dropped.
    [Fact]
    public void StoredConfigWithRetiredMoneyToken_LoadsAndShowsInvalid_WithApplyGated()
    {
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(new FilterConfig { MatchScores = [MatchScoreToken.RetiredMoney] }.ToJson()),
            FilterFacet.MatchScores);

        Assert.Equal(MatchScoreToken.RetiredMoney, MatchScores(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", MatchScores(cut).GetAttribute("class"));
        Assert.Single(MatchScoreVerdicts(cut));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        plan.Verify();
    }

    // Staging a saved filter is an edit: the draft becomes the staged
    // selection, the owner publishes it, and nothing is committed — no
    // baseline, no write of the selection. The plan holds the scenario to
    // the expansion's own writes.
    [Fact]
    public async Task Staging_HydratesTheDraft_WithoutCommitting()
    {
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);
        var cut = Render<FilterPanel>();

        var loaded = new FilterConfig
        {
            Players = ["Magriel"],
            DecisionType = DecisionTypeOption.CubeOnly,
            ContactTypes = [ContactType.Race],
        };
        await cut.InvokeAsync(() => Setup.Stage(loaded));
        ExpandFacets(cut, FilterFacet.Players, FilterFacet.DecisionType, FilterFacet.ContactTypes);

        Assert.Equal("Magriel", cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.True(cut.Find("#dt_CubeOnly").HasAttribute("checked"));
        Assert.True(cut.Find("#ct_Race").HasAttribute("checked"));

        Assert.Empty(_commits);
        Assert.Null(InEffect);
        plan.Verify();
    }

    // The interleaving the owner's gesture count exists for: the boot's
    // restoration is held at its read when a saved filter is staged. Released
    // afterwards with a different stored selection, the restoration must
    // record its outcome and yield the draft — the staged values survive.
    // Bounded on the restoration's own completion, which is the continuation
    // this is about.
    [Fact]
    public async Task Staging_DuringAPendingRestore_TakesPrecedence()
    {
        var plan = Planned();
        var restore = plan.ExpectHeldFilterRestore();
        plan.ExpectFilterPanelMount();
        ExpectExpansion(plan, FilterFacet.Players);
        var cut = Render<FilterPanel>();
        Assert.True(restore.IsReached);

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { Players = ["Hal"] }));

        restore.Release(FilterSurfaceStorage.RestoreAnswer(new FilterConfig { Players = ["Magriel"] }));
        await Setup.RestoreAsync().WaitAsync(DefaultWaitTimeout);

        ExpandFacets(cut, FilterFacet.Players);
        Assert.Equal("Hal", cut.Find("input[placeholder='e.g. Hal, Magriel']").GetAttribute("value"));
        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        plan.Verify();
    }

    // Save-as must capture the live draft, not the last-applied config — the
    // whole point is saving while dirty, before (or instead of) Apply.
    [Fact]
    public void Savable_UnappliedEdits_IsTheLiveDraft()
    {
        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.ContactTypes);

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal");
        cut.Find("#ct_Race").Change(true);

        Assert.True(Setup.Current.TryGetSavable(out var cfg));
        Assert.Equal(["Hal"], cfg!.Players);
        Assert.Contains(ContactType.Race, cfg.ContactTypes);
    }

    // Pattern text the grammar refuses is a state Apply refuses, and exactly
    // the state the save snapshot refuses — the lib's field verdict, read
    // through the same gate and the same build path.
    [Fact]
    public void Savable_InvalidPositionPattern_IsRefused()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern);

        cut.Find("#positionPattern").Input("[6,2");

        Assert.False(Setup.Current.TryGetSavable(out var cfg));
        Assert.Null(cfg);
    }

    // Re-keyed by halheinrich/backgammon#121, and the re-keying is the point.
    // Match-score text used to ride raw through both paths, validated only
    // downstream in FilterConfig.Build(); the grammar has since joined the
    // lib's field table, so GetInvalidFields names the list and the one
    // validity verdict both gates read tightened in the same edit. Save
    // still mirrors Apply exactly — which is why this pin moved rather than
    // being deleted: a saved document minted from a faulted token would be a
    // permanent trap, since loading it reproduces the state with Apply shut.
    [Theory]
    [InlineData("not-a-score")]
    [InlineData(MatchScoreToken.RetiredMoney)]
    public void Savable_FaultedMatchScoreToken_IsRefused(string token)
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);

        MatchScores(cut).Input(token);

        Assert.False(Setup.Current.TryGetSavable(out var cfg));
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
        ErrorMin(cut).Blur();

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
        ErrorMin(cut).Blur();
        ErrorMax(cut).Input("-2");
        ErrorMax(cut).Blur();

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
        ErrorMin(cut).Blur();
        ErrorMax(cut).Input("2");
        ErrorMax(cut).Blur();

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // The bounds are text boxes, so a user really can type a word into one.
    // "NaN" is not a number to the draft's grammar, so it is marked like any
    // other text that is not a number — and the feedback line's "a number,
    // zero or greater" is worded to stay true for exactly this input.
    [Fact]
    public void NaNErrorBound_MarksField_AndGatesApply()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("NaN");
        ErrorMin(cut).Blur();

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
    }

    // ── The bound boxes are text boxes (halheinrich/backgammon#379) ────────

    // A browser's number control hands the page an empty value for text it
    // cannot read yet, so the draft would hold "no bound" while the user is
    // still typing one. The four bounds are text boxes, then, asking a touch
    // keyboard for digits through inputmode, and nothing on the panel is a
    // number control: one coming back would bring the defect with it.
    [Theory]
    [InlineData("#errorMin", "decimal")]
    [InlineData("#errorMax", "decimal")]
    [InlineData("#moveNumberMin", "numeric")]
    [InlineData("#moveNumberMax", "numeric")]
    public void ABoundBox_IsATextBox_AskingForDigits(string box, string inputMode)
    {
        var cut = RenderExpanded(RowFacets);

        var input = cut.Find(box);

        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal(inputMode, input.GetAttribute("inputmode"));
        Assert.Null(input.GetAttribute("step"));
        Assert.Null(input.GetAttribute("min"));
        Assert.Empty(cut.FindAll("input[type='number']"));
    }

    // What the user typed stays in the box, a decimal comma included, through
    // Apply; what is remembered is the config's canonical form, the number.
    [Theory]
    [InlineData("0,05")]
    [InlineData("0.05")]
    [InlineData("5e-2")]
    public async Task ABoundTypedWithEitherMark_StaysAsTyped_AndCommitsTheNumber(string text)
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input(text);
        await Apply(cut).ClickAsync(new());

        Assert.Equal(text, ErrorMin(cut).GetAttribute("value"));
        Assert.Equal(new FilterConfig { ErrorMin = 0.05 }, LastCommit);
        Assert.Equal(new FilterConfig { ErrorMin = 0.05 }, InEffect);
    }

    // Typing a bound that is not finished is not clearing it: the selection
    // stops being the empty one — which was in effect — at the keystroke, and
    // goes back to it only when the box is blank again.
    [Fact]
    public void AnUnfinishedBound_IsNotABlankOne()
    {
        var cut = Render<FilterPanel>();
        Assert.Equal(new FilterConfig(), InEffect);

        ErrorMin(cut).Input("1e-");

        Assert.Null(InEffect);
        Assert.Equal("1e-", ErrorMin(cut).GetAttribute("value"));

        ErrorMin(cut).Input(string.Empty);

        Assert.Equal(new FilterConfig(), InEffect);
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
        cut.Find("#positionPattern").Blur();
        Assert.False(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("-1");
        ErrorMin(cut).Blur();

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
        ErrorMin(cut).Blur();
        ErrorMax(cut).Input("2");
        ErrorMax(cut).Blur();
        Assert.False(Apply(cut).HasAttribute("disabled"));

        cut.Find("#positionPattern").Input("[6,2");
        cut.Find("#positionPattern").Blur();

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
        ErrorMin(cut).Blur();
        Assert.True(Apply(cut).HasAttribute("disabled"));

        ErrorMin(cut).Input("1");
        ErrorMin(cut).Blur();

        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void StoredConfigWithInvalidBound_LoadsAndShowsInvalid_WithApplyGated()
    {
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(new FilterConfig { ErrorMin = -1, ErrorMax = 2 }.ToJson()));

        Assert.Equal("-1", ErrorMin(cut).GetAttribute("value"));
        Assert.Equal("2", ErrorMax(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        plan.Verify();
    }

    // The save gate is Apply's validity gate, whole: an invalid bound refuses
    // the snapshot exactly as an unparseable pattern does, so a saved document
    // can never be minted from a selection Apply would itself have refused.
    [Fact]
    public void Savable_InvalidErrorBound_IsRefused()
    {
        var cut = Render<FilterPanel>();

        ErrorMin(cut).Input("5");
        ErrorMax(cut).Input("2");

        Assert.False(Setup.Current.TryGetSavable(out var cfg));
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
        MoveNumberMin(cut).Blur();

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
        MoveNumberMin(cut).Blur();
        MoveNumberMax(cut).Input("0");
        MoveNumberMax(cut).Blur();

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
        MoveNumberMin(cut).Blur();
        MoveNumberMax(cut).Input("2");
        MoveNumberMax(cut).Blur();

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
        MoveNumberMin(cut).Blur();
        MoveNumberMax(cut).Input("30");
        MoveNumberMax(cut).Blur();

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
        MoveNumberMin(cut).Blur();

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
        ErrorMin(cut).Blur();

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
        MoveNumberMin(cut).Blur();
        Assert.True(Apply(cut).HasAttribute("disabled"));

        MoveNumberMin(cut).Input("1");
        MoveNumberMin(cut).Blur();

        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#moveNumberFeedback"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // The save gate is Apply's validity gate, whole: an out-of-range move
    // bound refuses the snapshot exactly as an invalid error bound does, so a
    // saved document can never be minted from a selection Apply would itself
    // have refused.
    [Fact]
    public void Savable_InvalidMoveNumberBound_IsRefused()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("5");
        MoveNumberMax(cut).Input("2");

        Assert.False(Setup.Current.TryGetSavable(out var cfg));
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
        var (cut, plan) = RenderRestoring(
            BrowserStorageReadAnswer.Stored(new FilterConfig { MoveNumberMin = 0, MoveNumberMax = 30 }.ToJson()),
            FilterFacet.MoveNumberRange);

        Assert.Equal("0", MoveNumberMin(cut).GetAttribute("value"));
        Assert.Equal("30", MoveNumberMax(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#moveNumberFeedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        plan.Verify();
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
        ErrorMin(cut).Blur();
        MoveNumberMax(cut).Input("0");
        MoveNumberMax(cut).Blur();

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", MoveNumberMax(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
        Assert.NotNull(cut.Find("#moveNumberFeedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // ── A box its field cannot be (halheinrich/backgammon#374) ─────────────
    //
    // The draft is the editor's state, not its config: a move-number box
    // holding "1.5" has no config value, and the panel used to parse it as no
    // bound at all — the input dropped, the facet off, the selection counted
    // as having no move filter. It is input that cannot be its field's value,
    // so it is invalid, marked where it was typed, refused at Apply and at
    // save, and a criterion the user meant rather than none.

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void UnrepresentableMoveNumberBound_MarksTheField_AndGatesApplyAndSave(string text)
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input(text);
        MoveNumberMin(cut).Blur();

        Assert.Equal(text, MoveNumberMin(cut).GetAttribute("value"));
        Assert.Contains("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#moveNumberFeedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.False(Setup.Current.TryGetSavable(out _));
    }

    [Fact]
    public void UnrepresentableErrorBound_MarksTheField_AndGatesApply()
    {
        var cut = Render<FilterPanel>();

        ErrorMax(cut).Input("abc");
        ErrorMax(cut).Blur();

        Assert.Contains("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // The defect's sharpest forms. A box its field cannot represent parses
    // as no criterion, so "1.5" in a move-number bound parses to exactly the
    // empty selection on a fresh panel — and to exactly an applied selection
    // with no move bound. It must read as neither: input that cannot be a
    // field's value is not a blank criterion, cannot make the selection
    // ready-empty, and is not the applied selection; Apply is off for the
    // invalid value rather than for "nothing to apply".
    [Fact]
    public void UnrepresentableBound_IsNotTheEmptySelection()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        Assert.NotNull(InEffect);

        MoveNumberMin(cut).Input("1.5");

        Assert.Null(InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    [Fact]
    public async Task UnrepresentableBound_OverTheAppliedSelection_IsNotThatSelection()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        ErrorMin(cut).Input("0.1");
        await Apply(cut).ClickAsync(new());
        Assert.Equal(new FilterConfig { ErrorMin = 0.1 }, InEffect);

        MoveNumberMin(cut).Input("1.5");

        Assert.Null(InEffect);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("#applyDisabledReason"));
    }

    // Not a blank criterion: the row badges set while it is collapsed, and
    // the folded container counts it.
    [Fact]
    public void UnrepresentableBound_BadgesItsRow_AndCountsOnTheFold()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        MoveNumberMin(cut).Input("1.5");

        cut.Find("#facetToggle_MoveNumberRange").Click();

        Assert.Equal("set", cut.Find("#facetBadge_MoveNumberRange").TextContent.Trim());
        FoldMoreFilters(cut);
        Assert.Equal("1 set", cut.Find("#moreFiltersBadge").TextContent.Trim());
    }

    // A whole number spelled with a fraction is that whole number: "3.0" is
    // three, which a move-number bound can be, so it is no fault and applies
    // as three.
    [Fact]
    public async Task WholeMoveNumberSpelledWithAFraction_AppliesAsThatNumber()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);

        MoveNumberMin(cut).Input("3.0");
        MoveNumberMin(cut).Blur();

        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        await Apply(cut).ClickAsync(new());
        Assert.Equal(3, LastCommit!.MoveNumberMin);
    }

    [Fact]
    public void FixingUnrepresentableBound_ClearsMark_AndReEnablesApply()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        MoveNumberMin(cut).Input("1.5");
        MoveNumberMin(cut).Blur();

        MoveNumberMin(cut).Input("2");
        MoveNumberMin(cut).Blur();

        Assert.DoesNotContain("is-invalid", MoveNumberMin(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#moveNumberFeedback"));
        Assert.False(Apply(cut).HasAttribute("disabled"));
    }

    // ── How an invalid field is shown (halheinrich/backgammon#272) ─────────
    //
    // Validity and its display are two facts (SPEC-filtering.md §1).
    // Validity is continuous, because the gates read it: Apply goes dark on
    // the keystroke that makes a value wrong. The mark and the message for a
    // newly typed error appear when the user leaves the box, and clear as
    // soon as the value is corrected; a wrong value that was not typed here
    // shows at once. bUnit can pin all of that. It cannot establish that
    // typing keeps the browser's focus and caret or that the layout holds
    // still — that check is the BgQuiz_Blazor leg's, in a real browser.

    public static TheoryData<FilterFacet?, string, string, string> Boxes => new()
    {
        { null, "#errorMin", "-1", "0.1" },
        { null, "#errorMax", "-1", "0.1" },
        { FilterFacet.MatchScores, "input[placeholder^='e.g. 4a5a']", "not-a-score", "4a5a" },
        { FilterFacet.MoveNumberRange, "#moveNumberMin", "1.5", "3" },
        { FilterFacet.MoveNumberRange, "#moveNumberMax", "0", "3" },
        { FilterFacet.PositionPattern, "#positionPattern", "[6,2", "[6,2,]" },
    };

    // While typing, a wrong value closes Apply and shows nothing: no class, no
    // aria-invalid, no message, the description the hint alone. Leaving the
    // box shows all of it.
    [Theory]
    [MemberData(nameof(Boxes))]
    public void ANewlyTypedError_IsShownOnlyOnLeavingTheBox(
        FilterFacet? facet, string selector, string invalid, string valid)
    {
        _ = valid;
        var cut = facet is { } row ? RenderExpanded(row) : Render<FilterPanel>();

        cut.Find(selector).Input(invalid);

        var typing = cut.Find(selector);
        Assert.True(Apply(cut).HasAttribute("disabled"));
        Assert.DoesNotContain("is-invalid", typing.GetAttribute("class"));
        Assert.False(typing.HasAttribute("aria-invalid"));
        Assert.DoesNotContain(DescribedBy(cut, typing), e => e.ClassList.Contains("invalid-feedback"));
        Assert.Empty(cut.FindAll(".invalid-feedback"));

        cut.Find(selector).Blur();

        var left = cut.Find(selector);
        Assert.Contains("is-invalid", left.GetAttribute("class"));
        Assert.Equal("true", left.GetAttribute("aria-invalid"));
        Assert.Single(DescribedBy(cut, left), e => e.ClassList.Contains("invalid-feedback"));
    }

    // A shown error clears the moment the value is right — no need to leave
    // the box first: the mark, aria-invalid and the description's reference
    // to the message all go, and no element carries the message's id. The
    // message's box stays where it was, with the same words, saying nothing —
    // invisible, aria-hidden, named by nothing — so nothing below it moves
    // while the user types (halheinrich/backgammon#272, Hal's ruling of
    // 2026-10-08).
    [Theory]
    [MemberData(nameof(Boxes))]
    public void AShownError_ClearsAsSoonAsCorrected_AndItsBoxStays(
        FilterFacet? facet, string selector, string invalid, string valid)
    {
        var cut = facet is { } row ? RenderExpanded(row) : Render<FilterPanel>();
        cut.Find(selector).Input(invalid);
        cut.Find(selector).Blur();
        var shown = Assert.Single(cut.FindAll(".invalid-feedback"));
        var id = shown.GetAttribute("id");
        var words = Words(shown);
        var place = PlaceOf(shown);

        cut.Find(selector).Input(valid);

        var corrected = cut.Find(selector);
        Assert.DoesNotContain("is-invalid", corrected.GetAttribute("class"));
        Assert.False(corrected.HasAttribute("aria-invalid"));
        Assert.DoesNotContain(DescribedBy(cut, corrected), e => e.ClassList.Contains("invalid-feedback"));
        Assert.Empty(cut.FindAll($"#{id}"));
        var held = Assert.Single(cut.FindAll(".invalid-feedback"));
        Assert.Equal(id, held.GetAttribute(FilterPanel.HeldLineAttribute));
        Assert.False(held.HasAttribute("id"));
        Assert.Contains("invisible", held.ClassList);
        Assert.Equal("true", held.GetAttribute("aria-hidden"));
        Assert.Equal(words, Words(held));
        Assert.Equal(place, PlaceOf(held));
    }

    // The sequence the host's browser check runs (the review's fourth
    // correction): wrong, left, returned to, corrected, wrong again, left.
    // From the first leave on, the message's box is in one place in every
    // state — shown, held while the correction and then the fresh error are
    // typed (a fresh error still waits for the leave), and shown again on
    // leaving — so nothing is inserted above what follows it, or removed.
    [Theory]
    [MemberData(nameof(Boxes))]
    public void WrongLeftCorrectedWrongAgainLeft_KeepsTheMessagesBoxInOnePlace(
        FilterFacet? facet, string selector, string invalid, string valid)
    {
        var cut = facet is { } row ? RenderExpanded(row) : Render<FilterPanel>();
        cut.Find(selector).Input(invalid);
        cut.Find(selector).Blur();
        var shown = Assert.Single(cut.FindAll(".invalid-feedback"));
        var id = shown.GetAttribute("id");
        var place = PlaceOf(shown);

        // Returning to the box has no handler of its own to fire — the panel
        // keys nothing on focus — so the return is the typing that follows.
        cut.Find(selector).Input(valid);
        Assert.Equal(place, PlaceOf(cut.Find($"[{FilterPanel.HeldLineAttribute}='{id}']")));

        cut.Find(selector).Input(invalid);
        Assert.DoesNotContain("is-invalid", cut.Find(selector).GetAttribute("class"));
        Assert.Equal(place, PlaceOf(cut.Find($"[{FilterPanel.HeldLineAttribute}='{id}']")));

        cut.Find(selector).Blur();
        Assert.Equal(place, PlaceOf(cut.Find($"#{id}")));
        Assert.Contains("is-invalid", cut.Find(selector).GetAttribute("class"));
        Assert.Single(cut.FindAll(".invalid-feedback"));
    }

    // Once held, a box stays through every gesture that is not a fold:
    // leaving the box, Apply, Clear filters, a staged saved filter. Each can
    // land between a press and its release, or while the user is typing.
    [Fact]
    public async Task AHeldBox_StaysThroughLeavingApplyClearAndAStagedFilter()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("-1");
        ErrorMin(cut).Blur();
        ErrorMin(cut).Input("0.1");
        var held = $"[{FilterPanel.HeldLineAttribute}='errorRangeFeedback']";

        ErrorMin(cut).Blur();
        Assert.NotNull(cut.Find(held));

        await Apply(cut).ClickAsync(new());
        Assert.NotNull(cut.Find(held));

        await cut.Find("#clearFilters").ClickAsync(new());
        Assert.NotNull(cut.Find(held));

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { ErrorMin = 0.2 }));
        Assert.NotNull(cut.Find(held));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
    }

    // A restore finishing is not a fold either: the restoration's settling,
    // released after a correction, leaves the held box where it is.
    [Fact]
    public async Task AHeldBox_StaysWhenARestoreFinishes()
    {
        var plan = Planned();
        var restore = plan.ExpectHeldFilterRestore();
        plan.ExpectFilterPanelMount();
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("-1");
        ErrorMin(cut).Blur();
        ErrorMin(cut).Input("0.1");

        restore.Release(FilterSurfaceStorage.RestoreAnswer(new FilterConfig { ErrorMin = 0.3 }));
        await Setup.RestoreAsync().WaitAsync(DefaultWaitTimeout);
        await cut.Instance.MountRestored.WaitAsync(DefaultWaitTimeout);

        cut.WaitForAssertion(() => Assert.Equal(
            nameof(FilterRestoration.Restored), cut.Find($"[{FilterPanel.RestorationAttribute}]").GetAttribute(FilterPanel.RestorationAttribute)));
        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='errorRangeFeedback']"));
        Assert.Equal("0.1", ErrorMin(cut).GetAttribute("value"));
        plan.Verify();
    }

    // A held box gives its space up only when its group folds away: its row
    // closing, or the More filters container over the row. The error range
    // sits outside the container, so folding the container leaves its box.
    [Fact]
    public void AHeldBox_GoesWhenItsRowCloses_OrTheContainerCloses_AndNotOtherwise()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        var heldMoveNumbers = $"[{FilterPanel.HeldLineAttribute}='moveNumberFeedback']";
        var heldErrorRange = $"[{FilterPanel.HeldLineAttribute}='errorRangeFeedback']";
        ErrorMin(cut).Input("-1");
        ErrorMin(cut).Blur();
        ErrorMin(cut).Input("0.1");
        MoveNumberMin(cut).Input("0");
        MoveNumberMin(cut).Blur();
        MoveNumberMin(cut).Input("3");
        Assert.NotNull(cut.Find(heldMoveNumbers));

        cut.Find("#facetToggle_MoveNumberRange").Click();
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("#facetToggle_MoveNumberRange").GetAttribute("aria-expanded")));
        cut.Find("#facetToggle_MoveNumberRange").Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#facetToggle_MoveNumberRange").GetAttribute("aria-expanded")));

        Assert.Empty(cut.FindAll(heldMoveNumbers));

        MoveNumberMin(cut).Input("0");
        MoveNumberMin(cut).Blur();
        MoveNumberMin(cut).Input("3");
        Assert.NotNull(cut.Find(heldMoveNumbers));

        FoldMoreFilters(cut);
        OpenMoreFilters(cut);

        Assert.Empty(cut.FindAll(heldMoveNumbers));
        Assert.NotNull(cut.Find(heldErrorRange));
    }

    // A line can first reach the screen by its group opening rather than by a
    // keystroke or a leave: a wrong value that arrived while the row was
    // closed shows when the row opens, and holds its space from then — by the
    // row's own toggle, by the container's over an open row, or by the
    // panel's restored preferences at mount. Corrected afterwards, its box
    // stays, as any shown line's does.
    [Fact]
    public async Task ALineFirstShownByOpeningItsRow_HoldsItsSpace()
    {
        var cut = Render<FilterPanel>();
        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { MoveNumberMin = 0 }));

        ExpandFacets(cut, FilterFacet.MoveNumberRange);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#moveNumberFeedback")));
        MoveNumberMin(cut).Input("3");

        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='moveNumberFeedback']"));
    }

    [Fact]
    public async Task ALineFirstShownByOpeningTheContainer_HoldsItsSpace()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        FoldMoreFilters(cut);
        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { MoveNumberMin = 0 }));

        OpenMoreFilters(cut);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#moveNumberFeedback")));
        MoveNumberMin(cut).Input("3");

        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='moveNumberFeedback']"));
    }

    [Fact]
    public async Task ALineFirstShownByARestoredOpenRow_HoldsItsSpace()
    {
        Setup.Stage(new FilterConfig { MoveNumberMin = 0 });
        var (cut, plan) = RenderWithPreferences(
            BrowserStorageReadAnswer.Stored(FilterPanel.SerializeFold(true)),
            BrowserStorageReadAnswer.Stored(FilterPanel.SerializeOpenRows([FilterFacet.MoveNumberRange])));
        await cut.Instance.MountRestored.WaitAsync(DefaultWaitTimeout);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#moveNumberFeedback")));

        MoveNumberMin(cut).Input("3");

        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='moveNumberFeedback']"));
        plan.Verify();
    }

    // A wrong value that arrives while its group is on screen — a staged
    // saved filter, or a draft a remount finds — is shown at once, so it
    // holds its space at once: corrected without the box ever being left, its
    // box stays.
    [Fact]
    public async Task AnArrivingWrongValue_HoldsItsSpaceAtOnce()
    {
        var cut = Render<FilterPanel>();
        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { ErrorMin = -1 }));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));

        ErrorMin(cut).Input("0.1");

        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='errorRangeFeedback']"));
    }

    [Fact]
    public async Task AWrongValueARemountFinds_HoldsItsSpaceAtOnce()
    {
        var first = Render<FilterPanel>();
        ErrorMin(first).Input("-1");
        await DisposeComponentsAsync();

        var cut = Render<FilterPanel>();
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
        ErrorMin(cut).Input("0.1");

        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='errorRangeFeedback']"));
    }

    // Held space is this mount's: the panel's next mount starts with none,
    // since nothing it shows has been on screen in it yet.
    [Fact]
    public async Task AHeldBox_GoesWhenThePanelUnmounts()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("-1");
        ErrorMin(cut).Blur();
        ErrorMin(cut).Input("0.1");
        Assert.NotNull(cut.Find($"[{FilterPanel.HeldLineAttribute}='errorRangeFeedback']"));

        await DisposeComponentsAsync();
        var again = Render<FilterPanel>();

        Assert.Empty(again.FindAll(".invalid-feedback"));
    }

    // The match-score line's words differ by fault kind, so its space is per
    // kind: shown with both kinds, then rid of the retired token while typing,
    // the retired part keeps its place, saying nothing, and only the part
    // still true is spoken — in the place it already had.
    [Fact]
    public void AMatchScoreKindCorrectedWhileTyping_KeepsItsPlaceInTheLine()
    {
        var cut = RenderExpanded(FilterFacet.MatchScores);
        MatchScores(cut).Input($"not-a-score, {MatchScoreToken.RetiredMoney}");
        MatchScores(cut).Blur();
        var both = MatchScoreVerdicts(cut);
        Assert.Equal(2, both.Length);

        MatchScores(cut).Input("not-a-score");

        Assert.Equal([both[0]], MatchScoreVerdicts(cut));
        var parts = cut.FindAll("#matchScoreFeedback span");
        Assert.Equal(2, parts.Count);
        Assert.DoesNotContain("invisible", parts[0].ClassList);
        Assert.Contains("invisible", parts[1].ClassList);
        Assert.Equal("true", parts[1].GetAttribute("aria-hidden"));
    }

    // A feedback line's words, whitespace folded — what its box's size is made of.
    private static string Words(IElement line) => Regex.Replace(line.TextContent.Trim(), @"\s+", " ");

    // Where an element sits in the panel: the index of it and of each
    // ancestor among its siblings, up to the panel's root. Equal places mean
    // nothing was inserted before it, or removed, at any level.
    private static string PlaceOf(IElement element)
    {
        var steps = new List<int>();
        for (IElement? node = element; node?.ParentElement is not null; node = node.ParentElement)
        {
            var before = 0;
            for (var sibling = node.PreviousElementSibling; sibling is not null; sibling = sibling.PreviousElementSibling)
            {
                before++;
            }

            steps.Add(before);
        }

        steps.Reverse();
        return string.Join("/", steps);
    }

    // Corrected and then made wrong again, it is a newly typed error again,
    // and waits for the user to leave the box.
    [Fact]
    public void AnErrorTypedAgainAfterACorrection_WaitsForLeavingTheBoxAgain()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("-1");
        ErrorMin(cut).Blur();
        ErrorMin(cut).Input("0.1");

        ErrorMin(cut).Input("-2");

        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        ErrorMin(cut).Blur();
        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
    }

    // Leaving a box shows what is wrong there and nothing elsewhere: an error
    // still being typed in another box stays quiet until that box is left.
    [Fact]
    public void LeavingAValidBox_ShowsNothingElsewhere()
    {
        var cut = RenderExpanded(FilterFacet.MoveNumberRange);
        ErrorMin(cut).Input("-1");

        MoveNumberMin(cut).Input("3");
        MoveNumberMin(cut).Blur();

        Assert.DoesNotContain("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Empty(cut.FindAll("#errorRangeFeedback"));
    }

    // A range's two bounds share one message, and a misordered pair blames
    // both: leaving either shows the pair.
    [Fact]
    public void LeavingOneBound_ShowsWhatItsPairIsBlamedFor()
    {
        var cut = Render<FilterPanel>();
        ErrorMin(cut).Input("5");
        ErrorMax(cut).Input("2");

        ErrorMax(cut).Blur();

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.Contains("is-invalid", ErrorMax(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
    }

    // A wrong value that arrives rather than being typed — a saved filter
    // staged into the panel — shows at once, as a restored one does
    // (StoredConfigWithInvalidBound_LoadsAndShowsInvalid_WithApplyGated).
    [Fact]
    public async Task AStagedInvalidValue_IsShownAtOnce()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { ErrorMin = -1 }));

        Assert.Contains("is-invalid", ErrorMin(cut).GetAttribute("class"));
        Assert.NotNull(cut.Find("#errorRangeFeedback"));
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
    // (GetActiveFacets on the live draft) that the rows' own badges read,
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

    // PRESENCE follows the lib's activation predicate, not the text behind
    // the control: whitespace-only pattern text is a non-empty box and no
    // pattern at all, so the row is not set and the count does not move for
    // it. The rows' own badge pin, one tier up.
    [Fact]
    public void MoreFiltersBadge_FollowsTheLibsActivationPredicate_NotTheBuffer()
    {
        var cut = RenderExpanded(FilterFacet.PositionPattern, FilterFacet.ContactTypes);

        cut.Find("#positionPattern").Input("   ");    // blank — facet off
        cut.Find("#ct_Race").Change(true);            // genuinely set
        FoldMoreFilters(cut);

        Assert.Equal("1 set", cut.Find("#moreFiltersBadge").TextContent.Trim());
    }

    // A host-staged selection lands in collapsed rows behind a folded
    // container; the badge reports it at rest, without anything opening.
    [Fact]
    public async Task Staging_StagedFacets_CountOnTheFoldedContainer()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig
        {
            ContactTypes = [ContactType.Race],
            DiceRolls = [new DiceRoll(3, 1)],
        }));

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Equal("2 set", cut.Find("#moreFiltersBadge").TextContent.Trim());
    }

    // Folding or unfolding is navigation, not an edit — the owner publishes
    // nothing, in either direction, exactly as for a row.
    [Fact]
    public void MoreFiltersToggle_IsNotAnEdit()
    {
        var cut = Render<FilterPanel>();
        _published = 0;   // the mount's restoration settling is a change of its own

        OpenMoreFilters(cut);
        FoldMoreFilters(cut);

        Assert.Equal(0, _published);
    }

    // Each click persists the one bit immediately under the container's own
    // key — never the rows' key, never the selection's: the two preferences
    // are separate because the rows' key speaks a vocabulary of FilterFacet
    // names and this container is not a facet. The plan holds the clicks to
    // exactly these writes, in this order, with these literals.
    [Fact]
    public void MoreFiltersToggle_PersistsUnderItsOwnKeyAlone()
    {
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Absent, BrowserStorageReadAnswer.Absent);
        var opened = plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);
        var folded = plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "false", BrowserStorageWriteAnswer.Succeeded);
        plan.RequireOrder(opened, folded);

        OpenMoreFilters(cut);
        FoldMoreFilters(cut);

        plan.Verify();
    }

    // The remembered state restores across sessions: stored open mounts open,
    // with the rows in the DOM and no click needed.
    [Fact]
    public void StoredOpenContainer_MountsOpen()
    {
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Stored("true"), BrowserStorageReadAnswer.Absent);

        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.NotEmpty(cut.FindAll("button[id^='facetToggle_']"));
        plan.Verify();
    }

    // And the value survives a round trip byte for byte: restored, then
    // written again by a pair of gestures that leave it as they found it, it
    // is the same literal that went in. The literal is written out by hand
    // here, so a change of mechanism that also changed the wire fails here
    // rather than silently refolding every user's panel.
    [Fact]
    public void StoredContainerState_RoundTripsByteIdentical()
    {
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Stored("true"), BrowserStorageReadAnswer.Absent);
        var folded = plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "false", BrowserStorageWriteAnswer.Succeeded);
        var reopened = plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);
        plan.RequireOrder(folded, reopened);

        FoldMoreFilters(cut);   // close…
        OpenMoreFilters(cut);   // …and reopen

        plan.Verify();
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
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Stored(stored), BrowserStorageReadAnswer.Absent);

        Assert.Equal("false", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("button[id^='facetToggle_']"));
        plan.Verify();
    }

    // The rows' pending-restore pin, one tier up: a click landing while the
    // container's read is in flight is a fresh user choice, and the late
    // restore must yield to it rather than clobber it. Bounded on the mount's
    // own completion — the restore's continuation — which a held read keeps
    // pending until released.
    [Fact]
    public async Task MoreFiltersToggle_DuringPendingStoredRestore_UserChoiceWins()
    {
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        var fold = plan.ExpectHeldRead(BrowserStorageArea.Local, MoreFiltersKey);
        plan.ExpectRead(BrowserStorageArea.Local, DisclosureKey, BrowserStorageReadAnswer.Absent);
        plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterPanel>();
        Assert.True(fold.IsReached);

        await cut.Find("#moreFiltersToggle").ClickAsync(new());

        fold.Release(BrowserStorageReadAnswer.Stored("false"));
        await RenderedAfterMount(cut);

        Assert.Equal("true", cut.Find("#moreFiltersToggle").GetAttribute("aria-expanded"));
        plan.Verify();
    }

    // The completion the pending-restore pins wait on is honest: it stays
    // pending while any of the mount's restores is, and completes once they
    // have all settled. Without this, a completion signalled early would let
    // those pins assert before the late restore they are about had run.
    [Fact]
    public async Task MountRestored_CompletesOnlyOnceTheMountsRestoresHaveSettled()
    {
        var plan = Planned();
        var restore = plan.ExpectHeldFilterRestore();
        var fold = plan.ExpectHeldRead(BrowserStorageArea.Local, MoreFiltersKey);
        plan.ExpectRead(BrowserStorageArea.Local, DisclosureKey, BrowserStorageReadAnswer.Absent);
        var cut = Render<FilterPanel>();

        fold.Release(BrowserStorageReadAnswer.Absent);
        await cut.InvokeAsync(() => { });
        Assert.False(cut.Instance.MountRestored.IsCompleted);

        restore.Release(BrowserStorageReadAnswer.Absent);
        await cut.Instance.MountRestored.WaitAsync(DefaultWaitTimeout);
        plan.Verify();
    }

    // The mount's preference restores and the boot's restoration, settled —
    // awaited on the panel's own completion of them (MountRestored), the one
    // observation only that continuation produces: a late restore that
    // rightly yields renders nothing, so no render can prove it ran. Bounded
    // by the test's normal timeout.
    private static Task RenderedAfterMount(IRenderedComponent<FilterPanel> cut) =>
        cut.Instance.MountRestored.WaitAsync(DefaultWaitTimeout);

    // Neither gesture that moves filter values moves the container: staging a
    // saved filter is the host's, clearing is the user's, and which
    // disclosures are open is neither's — the rows' own rule, one tier up.
    [Fact]
    public async Task StagingAndClearFilters_LeaveTheContainerWhereItWas()
    {
        var cut = Render<FilterPanel>();
        OpenMoreFilters(cut);

        await cut.InvokeAsync(() => Setup.Stage(
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

    // ── An invalid field is announced as invalid (halheinrich/backgammon#270) ─
    //
    // The is-invalid class is a colour; a screen reader hears nothing of it,
    // and the feedback line that says why was not part of the field's
    // description. So an invalid input carries aria-invalid="true", and its
    // aria-describedby names — beside the hint it always names — the rendered
    // feedback line; a valid input carries neither mark and keeps its hint.
    // Resolved through the DOM as a screen reader would (follow each id to
    // its element, which must exist), never as an attribute string, and the
    // feedback is recognised by what it is rather than by a literal id. One
    // row per box on the panel that bears a verdict: the panel renders all six
    // from one place (FieldAttributes), which is what makes the rule hold
    // without this list being the rule — a seventh box would join the splat
    // or fail to be announced, and this list is where that shows.
    [Theory]
    [InlineData(null, "#errorMin", "-1")]
    [InlineData(null, "#errorMax", "-1")]
    [InlineData(FilterFacet.MatchScores, "input[placeholder^='e.g. 4a5a']", "not-a-score")]
    [InlineData(FilterFacet.MoveNumberRange, "#moveNumberMin", "0")]
    [InlineData(FilterFacet.MoveNumberRange, "#moveNumberMax", "0")]
    [InlineData(FilterFacet.PositionPattern, "#positionPattern", "[6,2")]
    public void Input_AnnouncesItsInvalidity_AndDropsTheAnnouncementOnceValid(
        FilterFacet? facet, string selector, string invalidValue)
    {
        var cut = facet is { } row ? RenderExpanded(row) : Render<FilterPanel>();

        // Valid at rest: no mark, and the description is the hint alone.
        var input = cut.Find(selector);
        Assert.False(input.HasAttribute("aria-invalid"));
        var hint = Assert.Single(DescribedBy(cut, input));
        Assert.True(hint.ClassList.Contains("text-muted"));

        input.Input(invalidValue);
        input.Blur();

        input = cut.Find(selector);
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Contains("is-invalid", input.GetAttribute("class"));
        var described = DescribedBy(cut, input);
        Assert.Contains(described, e => e.Id == hint.Id);
        var feedback = Assert.Single(described, e => e.ClassList.Contains("invalid-feedback"));
        Assert.NotEmpty(feedback.TextContent.Trim());

        input.Input(string.Empty);
        input.Blur();

        input = cut.Find(selector);
        Assert.False(input.HasAttribute("aria-invalid"));
        Assert.DoesNotContain("is-invalid", input.GetAttribute("class"));
        Assert.Equal(hint.Id, Assert.Single(DescribedBy(cut, input)).Id);
    }

    // The elements an input's aria-describedby names, each id followed to
    // its element the way a screen reader follows it. Find throws for an id
    // that reaches nothing, which is the point: a description that names an
    // element not in the DOM is read as nothing.
    private static IElement[] DescribedBy(IRenderedComponent<FilterPanel> cut, IElement input) =>
        input.GetAttribute("aria-describedby")!
             .Split(' ', StringSplitOptions.RemoveEmptyEntries)
             .Select(id => cut.Find($"#{id}"))
             .ToArray();

    // Every text box inside a row takes its name by reference from
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

    // Nothing the user can type into is left unnamed: every text box on the
    // panel, with every row open, carries both references. The sweep is what
    // makes the two pins above a rule rather than a list — a box added
    // tomorrow with no name fails here without anyone remembering to extend
    // a theory. The bounds declare type="text" and the other boxes no type,
    // which is text too; the selector names both.
    [Fact]
    public void EveryTextInput_CarriesBothReferences()
    {
        var cut = RenderExpanded(RowFacets);

        var boxes = cut.FindAll("input[type='text'], input:not([type])").ToArray();

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

    // Opening or closing a row is navigation, not an edit: the owner
    // publishes nothing, in either direction.
    [Fact]
    public void RowToggle_IsNotAnEdit()
    {
        var cut = Render<FilterPanel>();
        _published = 0;   // the mount's restoration settling is a change of its own

        ExpandFacets(cut, FilterFacet.ContactTypes);
        ExpandFacets(cut, FilterFacet.ContactTypes);

        Assert.Equal(0, _published);
    }

    // Each click persists the whole open set immediately under the rows' own
    // key — a JSON array of facet member names, written in row order however
    // the user got there — and never writes the selection's key: which rows
    // are open is user preference, not filter state. The out-of-order clicks
    // are the point of the ordering rule: one arrangement of open rows has one
    // spelling on disk. The plan holds each write to its literal.
    [Fact]
    public void RowToggles_PersistTheOpenSetUnderTheirOwnKey()
    {
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Absent, BrowserStorageReadAnswer.Absent);
        plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);
        var first = plan.ExpectWrite(BrowserStorageArea.Local, DisclosureKey, "[\"DiceRolls\"]", BrowserStorageWriteAnswer.Succeeded);
        var second = plan.ExpectWrite(BrowserStorageArea.Local, DisclosureKey, "[\"Players\",\"DiceRolls\"]", BrowserStorageWriteAnswer.Succeeded);
        var third = plan.ExpectWrite(BrowserStorageArea.Local, DisclosureKey, "[\"Players\"]", BrowserStorageWriteAnswer.Succeeded);
        plan.RequireOrder(first, second, third);

        ExpandFacets(cut, FilterFacet.DiceRolls);
        ExpandFacets(cut, FilterFacet.Players);
        ExpandFacets(cut, FilterFacet.DiceRolls);   // close it again

        plan.Verify();
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
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Absent, BrowserStorageReadAnswer.Stored(stored));
        plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);
        var closed = plan.ExpectWrite(BrowserStorageArea.Local, DisclosureKey, "[\"Players\",\"DiceRolls\"]", BrowserStorageWriteAnswer.Succeeded);
        var reopened = plan.ExpectWrite(BrowserStorageArea.Local, DisclosureKey, stored, BrowserStorageWriteAnswer.Succeeded);
        plan.RequireOrder(closed, reopened);

        OpenMoreFilters(cut);
        Assert.Equal("true", cut.Find("#facetToggle_MoveNumberRange").GetAttribute("aria-expanded"));

        ExpandFacets(cut, FilterFacet.MoveNumberRange);   // close…
        ExpandFacets(cut, FilterFacet.MoveNumberRange);   // …and reopen

        plan.Verify();
    }

    // The remembered set restores across sessions: the named rows mount open,
    // no click needed, and the rows it does not name stay shut. Opening the
    // container to look is not what opened them — it carries its own key and
    // this scenario stores only the rows' — which is the independence the two
    // keys buy.
    [Fact]
    public void StoredOpenSet_MountsExactlyThoseRowsExpanded()
    {
        var (cut, plan) = RenderWithPreferences(
            BrowserStorageReadAnswer.Absent, BrowserStorageReadAnswer.Stored("[\"Players\",\"PositionPattern\"]"));
        plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);

        OpenMoreFilters(cut);

        Assert.Equal("true", cut.Find("#facetToggle_Players").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("#positionPattern"));
        Assert.Equal("false", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("input[id^='dr_']"));
        plan.Verify();
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
        var (cut, plan) = RenderWithPreferences(BrowserStorageReadAnswer.Absent, BrowserStorageReadAnswer.Stored(stored));
        plan.ExpectWrite(BrowserStorageArea.Local, MoreFiltersKey, "true", BrowserStorageWriteAnswer.Succeeded);

        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
            Assert.Equal("false", cut.Find($"#facetToggle_{facet}").GetAttribute("aria-expanded"));
        plan.Verify();
    }

    // The rows' twin of the staging-during-restore pin: a click landing while
    // the rows' read is in flight is a fresh user choice the late restore must
    // not clobber. Open a row while a stored set naming a different one is
    // pending; the released restore must yield whole — the user's row stays
    // open and the stored one stays shut.
    [Fact]
    public async Task RowToggle_DuringPendingStoredRestore_UserChoiceWins()
    {
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectRead(BrowserStorageArea.Local, MoreFiltersKey, BrowserStorageReadAnswer.Absent);
        var rows = plan.ExpectHeldRead(BrowserStorageArea.Local, DisclosureKey);
        ExpectExpansion(plan, FilterFacet.DiceRolls);
        var cut = Render<FilterPanel>();
        Assert.True(rows.IsReached);

        ExpandFacets(cut, FilterFacet.DiceRolls);

        rows.Release(BrowserStorageReadAnswer.Stored("[\"Players\"]"));
        await RenderedAfterMount(cut);

        Assert.Equal("true", cut.Find("#facetToggle_DiceRolls").GetAttribute("aria-expanded"));
        Assert.Equal("false", cut.Find("#facetToggle_Players").GetAttribute("aria-expanded"));
        plan.Verify();
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
    // draft through the same parsed config Apply commits, which is
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
    // (GetActiveFacets on the live draft), never a re-reading of the text
    // behind the controls. These are the three states where the two answers
    // genuinely differ, so a badge that consulted its box directly would
    // light in each of them and fail here:
    //
    //   · whitespace-only position-pattern text is a non-empty box and
    //     no pattern at all — the facet is off;
    //   · a depth level list whose mode toggle is off is inert by the lib's
    //     guarantee — checked levels, facet still off;
    //   · a player list of nothing but separators splits to no tokens.
    //
    // Each is a real state a user reaches by typing, not a contrivance.
    [Fact]
    public void Badge_PresenceFollowsTheLibsActivationPredicate_NotTheBuffer()
    {
        var blank = RenderWithActiveCollapsedFacet(
            FilterFacet.PositionPattern, c => c.Find("#positionPattern").Input("   "));
        Assert.Empty(blank.FindAll("#facetBadge_PositionPattern"));

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

    // The other side of that pin, and the state halheinrich/backgammon#269
    // moved: a facet is active on PRESENCE, so pattern text the grammar
    // refuses is a set facet — the lib says so, the badge follows, and Apply
    // is withheld by the same lib's field verdict. The malformed score token
    // is the precedent: both rows badge "set" over text that cannot be
    // applied, because what is there is a filter the user meant, not nothing.
    // A badge that re-parsed the text to decide would be a second encoding of
    // the activation predicate, and would fail here.
    [Theory]
    [InlineData(FilterFacet.PositionPattern, "#positionPattern", "[6,2")]
    [InlineData(FilterFacet.MatchScores, "input[placeholder^='e.g. 4a5a']", "not-a-score")]
    public void Badge_LightsOverTextTheGrammarRefuses_AsTheLibRulesPresence(
        FilterFacet facet, string inputSelector, string refusedText)
    {
        var cut = RenderWithActiveCollapsedFacet(facet, c => c.Find(inputSelector).Input(refusedText));

        Assert.Equal("set", cut.Find($"#facetBadge_{facet}").TextContent.Trim());
        Assert.True(Apply(cut).HasAttribute("disabled"));
    }

    // A loaded saved filter can stage values into collapsed rows. Their badges
    // must report it at rest — and staging must not open anything: opening a
    // row is the user's gesture, never staging's.
    [Fact]
    public async Task Staging_StagedFacets_LightBadges_WithoutOpeningRows()
    {
        var cut = Render<FilterPanel>();

        var loaded = new FilterConfig
        {
            ContactTypes = [ContactType.Race],
            DiceRolls = [new DiceRoll(3, 1)],
        };
        await cut.InvokeAsync(() => Setup.Stage(loaded));
        OpenMoreFilters(cut);

        foreach (var facet in RowFacets)
            Assert.Equal("false", cut.Find($"#facetToggle_{facet}").GetAttribute("aria-expanded"));

        Assert.Equal("1 selected", cut.Find("#facetBadge_ContactTypes").TextContent.Trim());
        Assert.Equal("1 selected", cut.Find("#facetBadge_DiceRolls").TextContent.Trim());
        Assert.Equal(2, cut.FindAll("span[id^='facetBadge_']").Count);
    }

    // A restored session with active facets badges them at rest — the boot's
    // restoration hydrates the draft the badges read.
    [Fact]
    public void StoredConfigWithActiveFacets_LightsBadgesAtRest()
    {
        var plan = Planned();
        plan.ExpectFilterRestore(new FilterConfig { ContactTypes = [ContactType.Race] });
        plan.ExpectFilterPanelMount();
        plan.ExpectFilterFoldToggle(open: true, BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterPanel>();

        OpenMoreFilters(cut);

        var badge = Assert.Single(cut.FindAll("span[id^='facetBadge_']"));
        Assert.Equal("facetBadge_ContactTypes", badge.Id);
        plan.Verify();
    }

    // Clear filters empties the draft, so every badge goes out with it.
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
        var cut = RenderExpanded(FilterFacet.ContactTypes);

        cut.Find("#ct_Race").Change(true);
        ExpandFacets(cut, FilterFacet.ContactTypes);   // collapse before applying
        await Apply(cut).ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Equal([ContactType.Race], LastCommit!.ContactTypes);
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
        var cut = RenderExpanded(FilterFacet.Players, FilterFacet.ContactTypes);

        cut.Find("input[placeholder='e.g. Hal, Magriel']").Input("Hal");
        cut.Find("#ct_Race").Change(true);
        cut.Find("#errorMin").Input("0.05");

        await cut.Find("#clearFilters").ClickAsync(new());

        Assert.NotNull(LastCommit);
        Assert.Empty(LastCommit!.GetActiveFacets());
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
    // inside it (staged from a saved filter, so no row was ever opened). Its badge
    // goes out, which is the row moving no further than the filter did.
    [Fact]
    public async Task ClearFilters_LeavesClosedRowsClosed()
    {
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => Setup.Stage(
            new FilterConfig { ContactTypes = [ContactType.Race] }));

        await cut.Find("#clearFilters").ClickAsync(new());
        OpenMoreFilters(cut);

        Assert.Equal("false", cut.Find("#facetToggle_ContactTypes").GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll("span[id^='facetBadge_']"));
    }

    // The gesture's whole persisted side-effect surface is one write: the
    // empty selection under the selection's key. No open-rows write — and
    // host state (e.g. BgQuiz's picked folder) is structurally out of reach:
    // the panel has no parameter or interop path to any.
    [Fact]
    public async Task ClearFilters_WritesOnlyTheEmptySelection()
    {
        var plan = Planned();
        plan.ExpectFilterRestore(FilterRestoration.NothingStored);
        plan.ExpectFilterPanelMount();
        plan.ExpectWrite(BrowserStorageArea.Local, ConfigKey, new FilterConfig().ToJson(), BrowserStorageWriteAnswer.Succeeded);
        var cut = Render<FilterPanel>();

        await cut.InvokeAsync(() => Setup.Stage(new FilterConfig { ContactTypes = [ContactType.Race] }));

        await cut.Find("#clearFilters").ClickAsync(new());

        plan.Verify();
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
