using BgDataTypes_Lib;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins <see cref="FilterDraft"/>, the editor's state (halheinrich/backgammon#374):
/// it holds what is on screen, the parsed config is derived from it and shares
/// nothing with it, and a box whose text its field cannot be is invalid and a
/// criterion rather than quietly no criterion.
/// </summary>
public class FilterDraftTests
{
    private static FilterConfig Spread() => new()
    {
        Players = ["Hal", "Magriel"],
        DecisionType = DecisionTypeOption.CheckerPlaysOnly,
        MatchScores = ["4a5a", MatchScoreToken.MoneyWithJacoby],
        ErrorMin = 0.05,
        ErrorMax = 0.5,
        MoveNumberMin = 3,
        MoveNumberMax = 30,
        ContactTypes = [ContactType.Race],
        IncludeEvaluations = true,
        EvaluationLevels = [AnalysisLevel.Ply2],
        IncludeRollouts = false,
        RolloutLevels = [AnalysisLevel.Ply3],
        IncludeBookRollouts = true,
        DiceRolls = [DiceRoll.All[3], DiceRoll.All[0]],
        PositionPattern = "[6,2,]",
    };

    [Fact]
    public void From_ThenToConfig_GivesBackTheConfig()
    {
        var config = Spread();

        Assert.Equal(config, FilterDraft.From(config).ToConfig());
    }

    [Fact]
    public void Empty_ParsesToTheDefaultConfig()
    {
        Assert.Equal(new FilterConfig(), FilterDraft.Empty.ToConfig());
        Assert.True(FilterDraft.Empty.IsValid);
        Assert.Empty(FilterDraft.Empty.ActiveFacets);
    }

    // A retained config reference reaches nothing: what ToConfig hands out is
    // new on every call, and what From was given is copied, not kept.
    [Fact]
    public void ToConfig_ReturnsANewConfigEachCall_ThatSharesNothingWithTheDraft()
    {
        var draft = FilterDraft.From(Spread());

        var first = draft.ToConfig();
        first.Players.Add("Intruder");
        first.ErrorMin = 0.9;

        Assert.NotSame(first, draft.ToConfig());
        Assert.Equal(Spread(), draft.ToConfig());
    }

    [Fact]
    public void From_CopiesTheConfig_ThatSharesNothingWithIt()
    {
        var config = Spread();
        var draft = FilterDraft.From(config);

        config.Players.Add("Intruder");
        config.ContactTypes.Add(ContactType.Contact);
        config.MoveNumberMin = 99;

        Assert.Equal(Spread(), draft.ToConfig());
    }

    // ── Representation ──────────────────────────────────────────────────────

    public static TheoryData<string, string, FilterField> Unrepresentable => new()
    {
        { nameof(FilterDraft.MoveNumberMinText), "1.5", FilterField.MoveNumberMin },
        { nameof(FilterDraft.MoveNumberMaxText), "abc", FilterField.MoveNumberMax },
        { nameof(FilterDraft.MoveNumberMinText), "1e10", FilterField.MoveNumberMin },
        { nameof(FilterDraft.ErrorMinText), "abc", FilterField.ErrorMin },
        { nameof(FilterDraft.ErrorMaxText), "0.1.2", FilterField.ErrorMax },
    };

    private static FilterDraft WithText(string member, string text) => member switch
    {
        nameof(FilterDraft.ErrorMinText) => FilterDraft.Empty with { ErrorMinText = text },
        nameof(FilterDraft.ErrorMaxText) => FilterDraft.Empty with { ErrorMaxText = text },
        nameof(FilterDraft.MoveNumberMinText) => FilterDraft.Empty with { MoveNumberMinText = text },
        nameof(FilterDraft.MoveNumberMaxText) => FilterDraft.Empty with { MoveNumberMaxText = text },
        _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    };

    // The input is held, named, invalid, and a criterion — never the blank
    // bound its config carries for want of anything else.
    [Theory]
    [MemberData(nameof(Unrepresentable))]
    public void TextItsFieldCannotBe_IsHeld_Invalid_AndACriterion(string member, string text, FilterField field)
    {
        var draft = WithText(member, text);

        Assert.Equal([field], draft.UnrepresentableFields);
        Assert.Contains(field, draft.InvalidFields);
        Assert.False(draft.IsValid);
        Assert.Contains(
            field is FilterField.ErrorMin or FilterField.ErrorMax ? FilterFacet.ErrorRange : FilterFacet.MoveNumberRange,
            draft.ActiveFacets);
        Assert.Equal(text, TextOf(draft, member));
    }

    private static string TextOf(FilterDraft draft, string member) => member switch
    {
        nameof(FilterDraft.ErrorMinText) => draft.ErrorMinText,
        nameof(FilterDraft.ErrorMaxText) => draft.ErrorMaxText,
        nameof(FilterDraft.MoveNumberMinText) => draft.MoveNumberMinText,
        nameof(FilterDraft.MoveNumberMaxText) => draft.MoveNumberMaxText,
        _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    };

    // What makes it the defect: rebuilt from its config, such a draft comes
    // back without the input. Holding the draft — never rebuilding it from
    // its config — is the only way to keep it.
    [Fact]
    public void UnrepresentableText_DoesNotSurviveARoundTripThroughTheConfig()
    {
        var draft = FilterDraft.Empty with { MoveNumberMinText = "1.5" };

        var rebuilt = FilterDraft.From(draft.ToConfig());

        Assert.Equal(string.Empty, rebuilt.MoveNumberMinText);
        Assert.NotEqual(draft, rebuilt);
    }

    // A move number is whole however it is spelled: either decimal mark, a
    // bare trailing mark, a sign, an exponent (halheinrich/backgammon#379).
    [Theory]
    [InlineData("3", 3)]
    [InlineData("3.0", 3)]
    [InlineData("3,0", 3)]
    [InlineData("3.", 3)]
    [InlineData("3,", 3)]
    [InlineData("+3", 3)]
    [InlineData("3e0", 3)]
    [InlineData("1,2e1", 12)]
    [InlineData(" 7 ", 7)]
    public void AWholeMoveNumber_IsThatNumber(string text, int expected)
    {
        var draft = FilterDraft.Empty with { MoveNumberMinText = text };

        Assert.Empty(draft.UnrepresentableFields);
        Assert.Equal(expected, draft.ToConfig().MoveNumberMin);
        Assert.Equal(text, draft.MoveNumberMinText);
    }

    // A number that is not whole is no move number, whichever mark it uses;
    // nor is one the config's field cannot hold, infinity included.
    [Theory]
    [InlineData("3.5")]
    [InlineData("3,5")]
    [InlineData("1e-1")]
    [InlineData("1e999")]
    public void AMoveNumberThatIsNotWhole_IsUnrepresentable(string text)
    {
        var draft = FilterDraft.Empty with { MoveNumberMaxText = text };

        Assert.Equal([FilterField.MoveNumberMax], draft.UnrepresentableFields);
        Assert.Null(draft.ToConfig().MoveNumberMax);
    }

    // ── The bound boxes' numbers (halheinrich/backgammon#379) ───────────────

    // Hal's ruling of 2026-10-08: '.' or ',' is the decimal mark, whatever the
    // locale, so 0.05 and 0,05 are one number; every spelling the browser's
    // number control took is kept, an exponent included, with a leading '+'
    // and a bare trailing mark besides. There is no grouping, so "1,234" is
    // 1.234 — never 1234. The text is held as typed whatever it reads as.
    [Theory]
    [InlineData("0.05", 0.05)]
    [InlineData("0,05", 0.05)]
    [InlineData("1,234", 1.234)]
    [InlineData("1.234", 1.234)]
    [InlineData(".5", 0.5)]
    [InlineData(",5", 0.5)]
    [InlineData("5.", 5)]
    [InlineData("5,", 5)]
    [InlineData("+0,5", 0.5)]
    [InlineData("-0,5", -0.5)]
    [InlineData("1e-3", 0.001)]
    [InlineData("1,5e-3", 0.0015)]
    [InlineData("2E+1", 20)]
    [InlineData(" 0,05 ", 0.05)]
    public void AnErrorBound_ReadsEitherDecimalMark(string text, double expected)
    {
        var draft = FilterDraft.Empty with { ErrorMinText = text, ErrorMaxText = text };

        Assert.Empty(draft.UnrepresentableFields);
        Assert.Equal(expected, draft.ToConfig().ErrorMin);
        Assert.Equal(expected, draft.ToConfig().ErrorMax);
        Assert.Equal(text, draft.ErrorMinText);
    }

    // Marks mixed, grouped or repeated are no number: nothing is removed to
    // make one, so none of these reads as a different value. Nor is a word,
    // or a spelling outside the number grammar.
    [Theory]
    [InlineData("1.234,5")]
    [InlineData("1,234.5")]
    [InlineData("1,234,567")]
    [InlineData("1.234.567")]
    [InlineData("1 234")]
    [InlineData("1..2")]
    [InlineData("1,,2")]
    [InlineData("..5")]
    [InlineData("1e3,5")]
    [InlineData("1_000")]
    [InlineData("0x10")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void ABoundWithMixedGroupedOrRepeatedMarks_IsNoNumber(string text)
    {
        var draft = FilterDraft.Empty with { ErrorMinText = text, MoveNumberMinText = text };

        Assert.Equal([FilterField.ErrorMin, FilterField.MoveNumberMin], draft.UnrepresentableFields);
        Assert.Null(draft.ToConfig().ErrorMin);
        Assert.Null(draft.ToConfig().MoveNumberMin);
        Assert.Equal(text, draft.ErrorMinText);
    }

    // Unfinished input is not blank. Blank text is no bound and the empty
    // selection; text on its way to being a number is a criterion the user
    // has not finished — invalid, badged, and never the empty selection.
    [Theory]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData(".")]
    [InlineData(",")]
    [InlineData("1e")]
    [InlineData("1e-")]
    [InlineData("e5")]
    public void UnfinishedText_IsNotBlank(string text)
    {
        var unfinished = FilterDraft.Empty with { ErrorMaxText = text };
        var blank = FilterDraft.Empty with { ErrorMaxText = string.Empty };

        Assert.True(blank.RestrictsNothing);
        Assert.False(unfinished.RestrictsNothing);
        Assert.False(unfinished.IsValid);
        Assert.Equal([FilterField.ErrorMax], unfinished.UnrepresentableFields);
        Assert.Contains(FilterFacet.ErrorRange, unfinished.ActiveFacets);
        Assert.Equal(text, unfinished.ErrorMaxText);
    }

    // An exponent past double's range reads as infinity, and whether that is
    // an admissible bound is the lib's verdict on the config: the editor
    // states no finite rule of its own (Hal's ruling, 2026-10-08).
    [Fact]
    public void AnOverflowingErrorBound_IsInfinity_AndTheLibRulesOnIt()
    {
        var draft = FilterDraft.Empty with { ErrorMaxText = "1e999" };

        Assert.Empty(draft.UnrepresentableFields);
        Assert.Equal(double.PositiveInfinity, draft.ToConfig().ErrorMax);
        Assert.Equal(draft.ToConfig().GetInvalidFields().Order(), draft.InvalidFields.Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankText_IsNoBound_AndNoFault(string text)
    {
        var draft = FilterDraft.Empty with { ErrorMinText = text, MoveNumberMaxText = text };

        Assert.True(draft.IsValid);
        Assert.Empty(draft.ActiveFacets);
        Assert.Null(draft.ToConfig().ErrorMin);
        Assert.Null(draft.ToConfig().MoveNumberMax);
    }

    // Representation is the editor's only rule. A negative error bound and a
    // move number of zero are numbers, so they are representable; whether
    // they are admissible bounds is the lib's verdict, which still names them.
    [Fact]
    public void AnInadmissibleNumber_IsTheLibsFault_NotARepresentationFault()
    {
        var draft = FilterDraft.Empty with { ErrorMinText = "-1", MoveNumberMinText = "0" };

        Assert.Empty(draft.UnrepresentableFields);
        Assert.Equal([FilterField.ErrorMin, FilterField.MoveNumberMin], draft.InvalidFields);
    }

    // ── Equality, order, and the document's nulls ───────────────────────────

    [Fact]
    public void Equality_IsOverTheHeldValues_InAnyOrderTheyWereChecked()
    {
        var a = FilterDraft.Empty
            .WithDiceRoll(DiceRoll.All[5], true)
            .WithDiceRoll(DiceRoll.All[1], true)
            .WithContactType(ContactType.Race, true);
        var b = FilterDraft.Empty
            .WithContactType(ContactType.Race, true)
            .WithDiceRoll(DiceRoll.All[1], true)
            .WithDiceRoll(DiceRoll.All[5], true);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        // One selection, one order — so one JSON.
        Assert.Equal(a.ToConfig().ToJson(), b.ToConfig().ToJson());
    }

    // A draft is what the screen shows, so different text is a different
    // draft even where it parses to the same config.
    [Fact]
    public void DifferentText_IsADifferentDraft_ThoughTheConfigsAgree()
    {
        var a = FilterDraft.Empty with { ErrorMinText = "0.10" };
        var b = FilterDraft.Empty with { ErrorMinText = "0.1" };

        Assert.NotEqual(a, b);
        Assert.Equal(a.ToConfig(), b.ToConfig());
    }

    [Fact]
    public void ADepthLevelsUnderAnUncheckedMode_AreKept()
    {
        var draft = FilterDraft.Empty
            .WithIncludes(AnalysisMode.Rollout, true)
            .WithLevel(AnalysisMode.Rollout, AnalysisLevel.Ply3, true)
            .WithIncludes(AnalysisMode.Rollout, false);

        Assert.False(draft.Includes(AnalysisMode.Rollout));
        Assert.Equal([AnalysisLevel.Ply3], draft.LevelsOf(AnalysisMode.Rollout));
        Assert.Equal([AnalysisLevel.Ply3], draft.ToConfig().RolloutLevels);
    }

    // A list a document carried as JSON null is the empty list, the lib's
    // own reading — the editor shows it empty rather than failing to show it.
    [Fact]
    public void From_AConfigWhoseListsAreNull_ShowsThemEmpty()
    {
        var config = new FilterConfig
        {
            Players = null!,
            MatchScores = null!,
            ContactTypes = null!,
            EvaluationLevels = null!,
            RolloutLevels = null!,
            BookRolloutLevels = null!,
            DiceRolls = null!,
        };

        var draft = FilterDraft.From(config);

        Assert.Equal(FilterDraft.Empty, draft);
    }

    [Fact]
    public void ADepthModeWithNoToggle_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterDraft.Empty.Includes(AnalysisMode.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterDraft.Empty.LevelsOf(AnalysisMode.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => FilterDraft.Empty.WithIncludes(AnalysisMode.Unknown, true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => FilterDraft.Empty.WithLevel(AnalysisMode.Unknown, AnalysisLevel.Ply1, true));
    }
}
