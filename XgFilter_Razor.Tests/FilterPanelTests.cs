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

    // Exhaustive render check for the Analysis-depth section: every
    // AnalysisDepthClass member must surface as an #ad_<member> checkbox
    // carrying its lib-owned [Description] label (via EnumLabel.ToLabel).
    // Iterating Enum.GetValues means a new member added upstream is covered
    // automatically, and pins that Unknown renders like any other member —
    // its presence in the UI is the deliberate opt-in to admit legacy rows.
    [Fact]
    public void AnalysisDepthSection_RendersEveryMemberWithLibLabel()
    {
        var cut = Render<FilterPanel>();

        foreach (var depth in Enum.GetValues<AnalysisDepthClass>())
        {
            Assert.NotNull(cut.Find($"#ad_{depth}"));
            Assert.Contains(depth.ToLabel(), cut.Markup);
        }
    }

    // Silent-splat guard for the Analysis-depth section (cf. the Contact-type
    // guard): an unbound Razor checkbox compiles but never mutates state, so
    // check a spread of members — including Unknown, the deliberate opt-in — and
    // assert the emitted config's include-list carries exactly them.
    [Fact]
    public async Task AnalysisDepthCheckbox_FlowsIntoEmittedConfig()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#ad_Unknown").Change(true);
        cut.Find("#ad_XgRollerPlus").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Contains(AnalysisDepthClass.Unknown, capturedConfig!.AnalysisDepthClasses);
        Assert.Contains(AnalysisDepthClass.XgRollerPlus, capturedConfig.AnalysisDepthClasses);
        Assert.Equal(2, capturedConfig.AnalysisDepthClasses.Count);
    }

    // Deselecting back to empty must emit an empty include-list — "no depth
    // filter" (inactive), not "reject everything." The Build()-skip on an empty
    // list is upstream's job; the panel's contract is only that the emitted
    // config round-trips the empty state faithfully.
    [Fact]
    public async Task AnalysisDepth_DeselectedToEmpty_EmitsEmptyList()
    {
        FilterConfig? capturedConfig = null;
        var cut = Render<FilterPanel>(parameters => parameters
            .Add(p => p.OnFilterConfigChanged, (FilterConfig c) => { capturedConfig = c; }));

        cut.Find("#ad_Ply3").Change(true);
        cut.Find("#ad_Ply3").Change(false);
        await cut.Find("button.btn-primary").ClickAsync(new());

        Assert.NotNull(capturedConfig);
        Assert.Empty(capturedConfig!.AnalysisDepthClasses);
    }

    // Round-trips the Analysis-depth section through the single-key persistence
    // path: check a couple of members, Apply (writes the FilterConfig blob,
    // AnalysisDepthClasses serialized as member-name strings), then re-mount with
    // the captured blob and assert exactly those boxes restore checked.
    [Fact]
    public async Task AnalysisDepth_RoundTripsAcrossRemount()
    {
        var cut = Render<FilterPanel>();

        cut.Find("#ad_Ply3").Change(true);
        cut.Find("#ad_RolloutPly7").Change(true);
        await cut.Find("button.btn-primary").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"]
            .Last(i => (string?)i.Arguments[0] == ConfigKey)
            .Arguments[1] as string;
        Assert.NotNull(stored);

        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey).SetResult(stored);
        var restored = Render<FilterPanel>();

        Assert.True(restored.Find("#ad_Ply3").HasAttribute("checked"));
        Assert.True(restored.Find("#ad_RolloutPly7").HasAttribute("checked"));
        Assert.DoesNotContain("checked", restored.Find("#ad_Book").OuterHtml);
    }

    // Persistence back-compat: a blob saved before the analysis-depth axis
    // existed carries no AnalysisDepthClasses field. TryFromJson must restore it
    // to an empty (inactive) include-list — no depth checkbox checked — which
    // falls out of System.Text.Json leaving the initialized-empty list untouched
    // for an absent member. Verified here rather than assumed.
    [Fact]
    public void LegacyConfigWithoutDepthField_RestoresToNoneSelected()
    {
        JSInterop.Setup<string?>("localStorage.getItem", ConfigKey)
            .SetResult("{\"DecisionType\":\"Both\"}");

        var cut = Render<FilterPanel>();

        foreach (var depth in Enum.GetValues<AnalysisDepthClass>())
            Assert.DoesNotContain("checked", cut.Find($"#ad_{depth}").OuterHtml);
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
    // placeholder advertises must construct a MatchScoreFilter without throwing.
    // The lib's constructor fails loud on any token it rejects, so this pins "the
    // UI never advertises an example the lib rejects" as a standing invariant
    // rather than a one-time fix — a future placeholder edit that reintroduces a
    // DMP-style un-parseable example trips here.
    [Fact]
    public void MatchScorePlaceholder_ExampleTokensAllParse()
    {
        var cut = Render<FilterPanel>();

        var placeholder = cut.Find("input[placeholder^='e.g. 4a5a']").GetAttribute("placeholder")!;
        var examples = placeholder
            .Replace("e.g. ", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.NotEmpty(examples);
        // Constructing the filter is the lib's Apply-time validation path.
        Assert.Null(Record.Exception(() => new MatchScoreFilter(examples)));
    }
}
