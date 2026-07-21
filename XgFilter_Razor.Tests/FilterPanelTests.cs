using Bunit;
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

    [Fact]
    public void Render_DefaultParameters_ProducesFilterCardMarkup()
    {
        var cut = Render<FilterPanel>();

        Assert.Contains("Filters", cut.Markup);
        Assert.Contains("Apply Filter", cut.Markup);
        Assert.Contains("Reset", cut.Markup);
    }

    [Fact]
    public void EventCallbacks_AreAccepted()
    {
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig _) => { })
            .Add(p => p.OnFilterDirty, () => { }));

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
        var cut = Render<FilterPanel>();
        Assert.Contains(expectedLabel, cut.Markup);
    }

    // Position type and Play type are shelved for later reintroduction in a
    // modified form: the XgFilter_Lib machinery (FilterConfig.PositionTypes /
    // PlayTypes, the filters, the enums) stays intact, but the UI groups are
    // hidden. Assert both control groups are absent so an accidental re-add — or
    // a future deliberate reintroduction — trips this test rather than shipping
    // silently.
    [Fact]
    public void ShelvedGroups_PositionTypeAndPlayType_AreAbsentFromPanel()
    {
        var cut = Render<FilterPanel>();

        Assert.DoesNotContain("Position type", cut.Markup);
        Assert.DoesNotContain("Play type", cut.Markup);
        Assert.Empty(cut.FindAll("input[id^='pt_']"));
        Assert.Empty(cut.FindAll("input[id^='plt_']"));
    }

    // The single localStorage key the panel persists its whole FilterConfig under.
    private const string ConfigKey = "xg_filter_config";

    // Round-trips through the single-key persistence path: set a spread of
    // controls, Apply (which writes one xg_filter_config blob via
    // FilterConfig.ToJson), then re-mount with the captured blob fed back through
    // the getItem mock and assert the restored controls reflect what was applied.
    [Fact]
    public async Task PersistedConfig_RoundTripsAcrossRemount()
    {
        var cut = Render<FilterPanel>();

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
        var restored = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#ct_Contact").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Contains(ContactType.Contact, capturedConfig!.ContactTypes);
    }

    // Exhaustive render check for the Analysis-depth level axis: every
    // AnalysisLevel member must surface as an #al_<member> checkbox carrying its
    // lib-owned [Description] label (via EnumLabel.ToLabel). Iterating
    // Enum.GetValues means a new member added upstream is covered automatically,
    // and pins that Unknown renders like any other level — its presence in the UI
    // is the deliberate opt-in to admit legacy / unenriched rows on the selected
    // mode.
    [Fact]
    public void AnalysisDepthSection_RendersEveryLevelWithLibLabel()
    {
        var cut = Render<FilterPanel>();

        foreach (var level in Enum.GetValues<AnalysisLevel>())
        {
            Assert.NotNull(cut.Find($"#al_{level}"));
            Assert.Contains(level.ToLabel(), cut.Markup);
        }
    }

    // The two mode toggles are the depth facet's second axis. Pin that each
    // renders as a checkbox and that its label text is the enum's lib-owned
    // [Description] (EnumLabel.ToLabel) — anchored to the label's `for` target so
    // a hardcoded panel string can't satisfy it. Guards the brief's requirement
    // that display text stays owned by AnalysisMode, not the UI.
    [Fact]
    public void AnalysisDepthSection_RendersModeTogglesWithLibLabels()
    {
        var cut = Render<FilterPanel>();

        Assert.Equal("checkbox", cut.Find("#am_Rollout").GetAttribute("type"));
        Assert.Equal("checkbox", cut.Find("#am_BookRollout").GetAttribute("type"));

        Assert.Equal(AnalysisMode.Rollout.ToLabel(),
            cut.Find("label[for='am_Rollout']").TextContent.Trim());
        Assert.Equal(AnalysisMode.BookRollout.ToLabel(),
            cut.Find("label[for='am_BookRollout']").TextContent.Trim());
    }

    // Declaration-order rendering pin: the level checkboxes must appear in
    // Enum.GetValues order (the lib's ascending-rigor order), so no UI-side sort
    // rule silently reorders them. Reads the rendered #al_* inputs in DOM order
    // and compares to the enum's declared order.
    [Fact]
    public void AnalysisLevelCheckboxes_RenderInEnumDeclarationOrder()
    {
        var cut = Render<FilterPanel>();

        var renderedOrder = cut.FindAll("input[id^='al_']")
            .Select(el => Enum.Parse<AnalysisLevel>(el.Id!["al_".Length..]))
            .ToArray();

        Assert.Equal(Enum.GetValues<AnalysisLevel>(), renderedOrder);
    }

    // Canonical selection from the brief: 4-ply checked + Rollouts toggled must
    // emit AnalysisLevels=[Ply4], IncludeRollouts=true, IncludeBookRollouts=false
    // — raw intent, verbatim. The mode-set derivation (that this means "4-ply
    // rollouts only") is FilterConfig.Build()'s job, not the panel's, so this
    // asserts the three config members and nothing about derived modes.
    [Fact]
    public async Task AnalysisDepth_CanonicalSelection_4PlyPlusRollouts_EmitsRawIntent()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#al_Ply4").Change(true);
        cut.Find("#am_Rollout").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Equal(new[] { AnalysisLevel.Ply4 }, capturedConfig!.AnalysisLevels);
        Assert.True(capturedConfig.IncludeRollouts);
        Assert.False(capturedConfig.IncludeBookRollouts);
    }

    // Silent-splat guard for the level axis (cf. the Contact-type guard): an
    // unbound Razor checkbox compiles but never mutates state, so check a spread
    // of levels — including Unknown, the deliberate opt-in — and assert the
    // emitted AnalysisLevels list carries exactly them.
    [Fact]
    public async Task AnalysisLevelCheckbox_FlowsIntoEmittedConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#al_Unknown").Change(true);
        cut.Find("#al_XgRollerPlus").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Contains(AnalysisLevel.Unknown, capturedConfig!.AnalysisLevels);
        Assert.Contains(AnalysisLevel.XgRollerPlus, capturedConfig.AnalysisLevels);
        Assert.Equal(2, capturedConfig.AnalysisLevels.Count);
    }

    // Silent-splat guard for the mode-toggle axis: each toggle binds to its own
    // bool, so flip both and assert the emitted config carries both flags — with
    // no level checked, so this also pins the "toggles alone, any level" intent
    // the empty AnalysisLevels list expresses.
    [Fact]
    public async Task RolloutToggles_FlowIntoEmittedConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#am_Rollout").Change(true);
        cut.Find("#am_BookRollout").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.True(capturedConfig!.IncludeRollouts);
        Assert.True(capturedConfig.IncludeBookRollouts);
        Assert.Empty(capturedConfig.AnalysisLevels);
    }

    // Every depth control must raise OnFilterDirty so the parent can disable Run
    // until Apply. Toggle a level checkbox and each mode toggle and count the
    // firings — one per interaction, none on initial render.
    [Fact]
    public void AnalysisDepthControls_EachFireOnFilterDirty()
    {
        var dirtyCount = 0;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterDirty, () => { dirtyCount++; }));

        cut.Find("#al_Ply4").Change(true);
        Assert.Equal(1, dirtyCount);

        cut.Find("#am_Rollout").Change(true);
        Assert.Equal(2, dirtyCount);

        cut.Find("#am_BookRollout").Change(true);
        Assert.Equal(3, dirtyCount);
    }

    // Deselecting every axis back to nothing must emit the inactive state —
    // empty level list and both toggles off — "facet off," not "reject
    // everything." The Build()-skip on that combination is upstream's job; the
    // panel's contract is only that it round-trips the emptied intent faithfully.
    [Fact]
    public async Task AnalysisDepth_DeselectedToEmpty_EmitsInactiveState()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#al_Ply3").Change(true);
        cut.Find("#am_Rollout").Change(true);
        cut.Find("#al_Ply3").Change(false);
        cut.Find("#am_Rollout").Change(false);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Empty(capturedConfig!.AnalysisLevels);
        Assert.False(capturedConfig.IncludeRollouts);
        Assert.False(capturedConfig.IncludeBookRollouts);
    }

    // Reset must clear the whole depth facet — every level unchecked and both
    // toggles off — in the UI and in the emitted reset config.
    [Fact]
    public async Task Reset_ClearsAnalysisDepthSelections()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#al_Ply4").Change(true);
        cut.Find("#am_Rollout").Change(true);
        cut.Find("#am_BookRollout").Change(true);

        await cut.Find("button.btn-outline-secondary").ClickAsync(new());

        Assert.False(cut.Find("#al_Ply4").HasAttribute("checked"));
        Assert.False(cut.Find("#am_Rollout").HasAttribute("checked"));
        Assert.False(cut.Find("#am_BookRollout").HasAttribute("checked"));

        Assert.NotNull(capturedConfig);
        Assert.Empty(capturedConfig!.AnalysisLevels);
        Assert.False(capturedConfig.IncludeRollouts);
        Assert.False(capturedConfig.IncludeBookRollouts);
    }

    // Round-trips the depth facet through the single-key persistence path: check
    // a couple of levels and a mode toggle, Apply (writes the FilterConfig blob —
    // AnalysisLevels as member-name strings, the toggles as booleans), then
    // re-mount with the captured blob and assert exactly those controls restore.
    [Fact]
    public async Task AnalysisDepth_RoundTripsAcrossRemount()
    {
        var cut = Render<FilterPanel>();

        cut.Find("#al_Ply3").Change(true);
        cut.Find("#al_Ply7").Change(true);
        cut.Find("#am_BookRollout").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = Render<FilterPanel>();

        Assert.True(restored.Find("#al_Ply3").HasAttribute("checked"));
        Assert.True(restored.Find("#al_Ply7").HasAttribute("checked"));
        Assert.True(restored.Find("#am_BookRollout").HasAttribute("checked"));
        Assert.False(restored.Find("#am_Rollout").HasAttribute("checked"));
        Assert.DoesNotContain("checked", restored.Find("#al_XgRoller").OuterHtml);
    }

    // Persistence back-compat: a blob saved before the depth axis existed carries
    // none of AnalysisLevels / IncludeRollouts / IncludeBookRollouts. TryFromJson
    // must restore the facet inactive — no level checked, both toggles off —
    // which falls out of System.Text.Json leaving the initialized defaults for
    // the absent members. Verified here rather than assumed.
    [Fact]
    public void LegacyConfigWithoutDepthField_RestoresToInactive()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\"}");

        var cut = Render<FilterPanel>();

        foreach (var level in Enum.GetValues<AnalysisLevel>())
            Assert.DoesNotContain("checked", cut.Find($"#al_{level}").OuterHtml);
        Assert.False(cut.Find("#am_Rollout").HasAttribute("checked"));
        Assert.False(cut.Find("#am_BookRollout").HasAttribute("checked"));
    }

    // Migration guard: a blob saved under the retired flat depth axis carries an
    // AnalysisDepthClasses array with member names (e.g. "RolloutPly7") that no
    // longer exist on any current enum. System.Text.Json ignores it as an unknown
    // property, so the two-axis facet restores inactive rather than throwing —
    // the reset-on-read path for old saved configs.
    [Fact]
    public void ConfigWithRetiredDepthField_IsIgnored_RestoresToInactive()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\",\"AnalysisDepthClasses\":[\"Ply3\",\"RolloutPly7\"]}");

        var cut = Render<FilterPanel>();

        foreach (var level in Enum.GetValues<AnalysisLevel>())
            Assert.DoesNotContain("checked", cut.Find($"#al_{level}").OuterHtml);
        Assert.False(cut.Find("#am_Rollout").HasAttribute("checked"));
        Assert.False(cut.Find("#am_BookRollout").HasAttribute("checked"));
    }

    // Canonical-order render pin for the dice facet: every roll must surface as a
    // #dr_<token> checkbox, and the checkboxes must appear in DiceRoll.All order
    // (the lib's ascending canonical order) — no UI-side roll list or sort rule.
    // Reads the rendered #dr_* inputs in DOM order, parses each id back to a
    // DiceRoll, and compares the sequence to DiceRoll.All (which is the 21 rolls).
    [Fact]
    public void DiceSection_RendersAll21RollsInCanonicalOrder()
    {
        var cut = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>(parameters => parameters
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
        var cut = Render<FilterPanel>();

        cut.Find("#dr_31").Change(true);
        cut.Find("#dr_66").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>(parameters => parameters
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
        var cut = Render<FilterPanel>(parameters => parameters
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
        var cut = Render<FilterPanel>(parameters => parameters
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
        var cut = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>();

        cut.Find("#positionPattern").Input("[6,2,] [5,,-2]");
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>();

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

        var cut = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>();

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
        var cut = Render<FilterPanel>();

        var placeholder = cut.Find("input[placeholder^='e.g. 4a5a']").GetAttribute("placeholder")!;
        var examples = placeholder
            .Replace("e.g. ", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.NotEmpty(examples);
        // Build() is the lib's Apply-time validation path.
        var cfg = new FilterConfig { MatchScores = [.. examples] };
        Assert.Null(Record.Exception(() => cfg.Build()));
    }
}
