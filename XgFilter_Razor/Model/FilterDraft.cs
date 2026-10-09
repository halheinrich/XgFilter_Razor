using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using BgDataTypes_Lib;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;

namespace XgFilter_Razor;

/// <summary>
/// The filter selection as the user is editing it: the editor's own state, not
/// the <see cref="FilterConfig"/> it parses to (halheinrich/backgammon#374).
/// Every value is held as the user left it — the text of each typed box
/// verbatim, valid or not, finished or not — so converting to a config and
/// rebuilding the editor can never quietly lose what is on screen.
///
/// <para>
/// <b>Why the text, not the config.</b> A config cannot hold every input: its
/// bounds are numbers, so a move-number box holding <c>1.5</c> has no config
/// value at all. Rebuilt from its config, such a draft used to come back with
/// the box empty and the facet off — an invalid input silently turned into
/// "no filter", and the selection then counted as the empty one. Holding the
/// text keeps the input, and <see cref="UnrepresentableFields"/> names it.
/// </para>
///
/// <para>
/// <b>What is derived, never stored.</b> The parsed config
/// (<see cref="ToConfig"/>) and the verdicts on it are computed from the held
/// values on every ask, and nothing caches them: this is a record, and a
/// <c>with</c> expression copies every field, so a cached verdict would ride
/// into a draft it was not computed for. The domain rules stay
/// <c>XgFilter_Lib</c>'s — <see cref="InvalidFields"/> is the lib's
/// <see cref="FilterConfig.GetInvalidFields"/> on the parsed config, joined by
/// the one rule that is this editor's own: a box whose text cannot be its
/// field's value at all.
/// </para>
///
/// <para>
/// Immutable: an edit is a new draft (<c>with</c>, or the toggling helpers
/// below), so a draft held anywhere describes the moment it was made and
/// nothing that holds one can change another's. The sets are sorted, so one
/// selection has one order — which is also the order <see cref="ToConfig"/>
/// writes its lists in, and so the order its JSON carries.
/// </para>
/// </summary>
internal sealed partial record FilterDraft
{
    /// <summary>The draft of a panel nobody has touched: every box empty, nothing checked.</summary>
    public static FilterDraft Empty { get; } = new();

    /// <summary>The player-names box, as typed (comma-separated).</summary>
    public string PlayersText { get; init; } = string.Empty;

    /// <summary>The decision-type choice.</summary>
    public DecisionTypeOption DecisionType { get; init; } = DecisionTypeOption.Both;

    /// <summary>The match-scores box, as typed (comma-separated tokens).</summary>
    public string MatchScoresText { get; init; } = string.Empty;

    /// <summary>The error range's lower box, as typed.</summary>
    public string ErrorMinText { get; init; } = string.Empty;

    /// <summary>The error range's upper box, as typed.</summary>
    public string ErrorMaxText { get; init; } = string.Empty;

    /// <summary>The move-number range's lower box, as typed.</summary>
    public string MoveNumberMinText { get; init; } = string.Empty;

    /// <summary>The move-number range's upper box, as typed.</summary>
    public string MoveNumberMaxText { get; init; } = string.Empty;

    /// <summary>The checked contact types.</summary>
    public ImmutableSortedSet<ContactType> ContactTypes { get; init; } = [];

    /// <summary>Whether the evaluation mode's toggle is checked.</summary>
    public bool IncludeEvaluations { get; init; }

    /// <summary>The evaluation mode's checked levels — kept while its toggle is off, where the lib makes them inert.</summary>
    public ImmutableSortedSet<AnalysisLevel> EvaluationLevels { get; init; } = [];

    /// <summary>Whether the rollout mode's toggle is checked.</summary>
    public bool IncludeRollouts { get; init; }

    /// <summary>The rollout mode's checked levels.</summary>
    public ImmutableSortedSet<AnalysisLevel> RolloutLevels { get; init; } = [];

    /// <summary>Whether the book-rollout mode's toggle is checked.</summary>
    public bool IncludeBookRollouts { get; init; }

    /// <summary>The book-rollout mode's checked levels.</summary>
    public ImmutableSortedSet<AnalysisLevel> BookRolloutLevels { get; init; } = [];

    /// <summary>The checked dice rolls.</summary>
    public ImmutableSortedSet<DiceRoll> DiceRolls { get; init; } = [];

    /// <summary>The position-pattern box, as typed — never trimmed, parse or not.</summary>
    public string PositionPatternText { get; init; } = string.Empty;

    /// <summary>
    /// The editor showing <paramref name="config"/> — what the panel puts on
    /// screen for a restored, loaded or resumed selection. The values are
    /// copied, so the draft shares nothing with <paramref name="config"/>,
    /// which its caller may go on to change. A stored value a rule outlaws
    /// comes across exactly as stored, to be marked rather than repaired. A
    /// list the document carried as JSON <c>null</c> is the empty list, the
    /// lib's own reading of it. The shelved facets (position types, play
    /// types) have no box, so they do not come across.
    /// </summary>
    /// <param name="config">The configuration to show. Read, never kept.</param>
    /// <returns>A draft whose <see cref="ToConfig"/> equals <paramref name="config"/>
    /// in every facet the panel offers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    public static FilterDraft From(FilterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new()
        {
            PlayersText = string.Join(", ", config.Players ?? []),
            DecisionType = config.DecisionType,
            MatchScoresText = string.Join(", ", config.MatchScores ?? []),
            ErrorMinText = Format(config.ErrorMin),
            ErrorMaxText = Format(config.ErrorMax),
            MoveNumberMinText = Format(config.MoveNumberMin),
            MoveNumberMaxText = Format(config.MoveNumberMax),
            ContactTypes = [.. config.ContactTypes ?? []],
            IncludeEvaluations = config.IncludeEvaluations,
            EvaluationLevels = [.. config.EvaluationLevels ?? []],
            IncludeRollouts = config.IncludeRollouts,
            RolloutLevels = [.. config.RolloutLevels ?? []],
            IncludeBookRollouts = config.IncludeBookRollouts,
            BookRolloutLevels = [.. config.BookRolloutLevels ?? []],
            DiceRolls = [.. config.DiceRolls ?? []],
            PositionPatternText = config.PositionPattern ?? string.Empty,
        };
    }

    /// <summary>
    /// The configuration this draft parses to — a new instance on every call,
    /// so a caller may keep or change what it gets without reaching this
    /// draft. A box left blank is no criterion. A box holding text its field
    /// cannot be also comes out as no criterion, because the config has
    /// nothing else to carry it as; such a draft is invalid
    /// (<see cref="UnrepresentableFields"/>), so this config is never
    /// committed, saved or counted as in effect. The pattern text rides as
    /// typed, a blank box as <see langword="null"/>, the lib's "no pattern".
    /// </summary>
    /// <returns>The parsed configuration.</returns>
    public FilterConfig ToConfig()
    {
        TryReadNumber(ErrorMinText, out var errorMin);
        TryReadNumber(ErrorMaxText, out var errorMax);
        TryReadWholeNumber(MoveNumberMinText, out var moveNumberMin);
        TryReadWholeNumber(MoveNumberMaxText, out var moveNumberMax);

        return new()
        {
            Players = SplitTokens(PlayersText),
            DecisionType = DecisionType,
            MatchScores = SplitTokens(MatchScoresText),
            ErrorMin = errorMin,
            ErrorMax = errorMax,
            MoveNumberMin = moveNumberMin,
            MoveNumberMax = moveNumberMax,
            ContactTypes = [.. ContactTypes],
            IncludeEvaluations = IncludeEvaluations,
            EvaluationLevels = [.. EvaluationLevels],
            IncludeRollouts = IncludeRollouts,
            RolloutLevels = [.. RolloutLevels],
            IncludeBookRollouts = IncludeBookRollouts,
            BookRolloutLevels = [.. BookRolloutLevels],
            DiceRolls = [.. DiceRolls],
            // Null for a blank box: null is the lib's "no pattern filter", so a
            // box never touched and a box cleared build what a fresh config
            // does, and no document is minted with an empty pattern the user
            // never wrote.
            PositionPattern = string.IsNullOrWhiteSpace(PositionPatternText) ? null : PositionPatternText,
        };
    }

    /// <summary>
    /// The boxes holding text their field cannot be: a range bound that is
    /// not a number, unfinished input such as <c>1e-</c> or <c>-</c>
    /// included, and a move-number bound that is not a whole number. A number
    /// takes either <c>.</c> or <c>,</c> as its decimal mark and has no
    /// grouping (halheinrich/backgammon#379). This is the editor's one rule of
    /// its own, and it is about representation, not the domain — whether a
    /// number is an admissible bound stays the lib's question, asked of the
    /// parsed config (<see cref="FilterConfig.GetInvalidFields"/>). Blank text
    /// is no bound and never appears here.
    /// </summary>
    public IReadOnlySet<FilterField> UnrepresentableFields
    {
        get
        {
            var fields = new SortedSet<FilterField>();

            if (!TryReadNumber(ErrorMinText, out _)) fields.Add(FilterField.ErrorMin);
            if (!TryReadNumber(ErrorMaxText, out _)) fields.Add(FilterField.ErrorMax);
            if (!TryReadWholeNumber(MoveNumberMinText, out _)) fields.Add(FilterField.MoveNumberMin);
            if (!TryReadWholeNumber(MoveNumberMaxText, out _)) fields.Add(FilterField.MoveNumberMax);

            return fields;
        }
    }

    /// <summary>
    /// Every field whose value is wrong: the lib's verdict on the parsed
    /// config (<see cref="FilterConfig.GetInvalidFields"/>) together with
    /// <see cref="UnrepresentableFields"/>. The gates, the field marks and the
    /// save refusal all read this one set.
    /// </summary>
    public IReadOnlySet<FilterField> InvalidFields
    {
        get
        {
            var fields = new SortedSet<FilterField>(ToConfig().GetInvalidFields());
            fields.UnionWith(UnrepresentableFields);
            return fields;
        }
    }

    /// <summary>Whether no field is wrong — the state in which this draft may be committed or saved.</summary>
    public bool IsValid => InvalidFields.Count == 0;

    /// <summary>
    /// Whether this draft is the empty selection: valid, and holding no
    /// criterion by the lib's own activation predicates
    /// (<see cref="FilterConfig.GetActiveFacets"/>), so it filters nothing out
    /// (<c>SPEC-filtering.md</c> §1; halheinrich/backgammon#266). A property of
    /// the selection, not of how it got here — never chosen, every criterion
    /// cleared, or edited back to nothing. A box whose text its field cannot
    /// be makes the draft invalid, so input like that is never the empty
    /// selection, however its config reads.
    /// </summary>
    public bool RestrictsNothing => IsValid && ToConfig().GetActiveFacets().Count == 0;

    /// <summary>
    /// The facets holding a criterion: the lib's verdict on the parsed config
    /// (<see cref="FilterConfig.GetActiveFacets"/>), together with the facet
    /// of every unrepresentable box. Text that cannot be its field's value is
    /// not a blank criterion — it is a filter the user meant and has not
    /// finished, the lib's own reading of refused pattern text — so its row
    /// badges <c>set</c> rather than going quiet.
    /// </summary>
    public IReadOnlySet<FilterFacet> ActiveFacets
    {
        get
        {
            var facets = new SortedSet<FilterFacet>(ToConfig().GetActiveFacets());

            foreach (var unrepresentable in UnrepresentableFields)
            {
                facets.Add(unrepresentable is FilterField.ErrorMin or FilterField.ErrorMax
                    ? FilterFacet.ErrorRange
                    : FilterFacet.MoveNumberRange);
            }

            return facets;
        }
    }

    /// <summary>Whether <paramref name="mode"/>'s toggle is checked.</summary>
    /// <param name="mode">A selectable analysis mode.</param>
    /// <returns>The toggle's state.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> has no toggle.</exception>
    public bool Includes(AnalysisMode mode) => mode switch
    {
        AnalysisMode.Evaluation => IncludeEvaluations,
        AnalysisMode.Rollout => IncludeRollouts,
        AnalysisMode.BookRollout => IncludeBookRollouts,
        _ => throw NoToggle(mode),
    };

    /// <summary>The levels checked under <paramref name="mode"/>.</summary>
    /// <param name="mode">A selectable analysis mode.</param>
    /// <returns>The checked levels.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> has no toggle.</exception>
    public ImmutableSortedSet<AnalysisLevel> LevelsOf(AnalysisMode mode) => mode switch
    {
        AnalysisMode.Evaluation => EvaluationLevels,
        AnalysisMode.Rollout => RolloutLevels,
        AnalysisMode.BookRollout => BookRolloutLevels,
        _ => throw NoToggle(mode),
    };

    /// <summary>This draft with <paramref name="mode"/>'s toggle set to <paramref name="include"/>; its levels are kept either way.</summary>
    /// <param name="mode">A selectable analysis mode.</param>
    /// <param name="include">Whether the mode is checked.</param>
    /// <returns>The edited draft.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> has no toggle.</exception>
    public FilterDraft WithIncludes(AnalysisMode mode, bool include) => mode switch
    {
        AnalysisMode.Evaluation => this with { IncludeEvaluations = include },
        AnalysisMode.Rollout => this with { IncludeRollouts = include },
        AnalysisMode.BookRollout => this with { IncludeBookRollouts = include },
        _ => throw NoToggle(mode),
    };

    /// <summary>This draft with <paramref name="level"/> checked or unchecked under <paramref name="mode"/>.</summary>
    /// <param name="mode">A selectable analysis mode.</param>
    /// <param name="level">The level.</param>
    /// <param name="selected">Whether the level is checked.</param>
    /// <returns>The edited draft.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> has no toggle.</exception>
    public FilterDraft WithLevel(AnalysisMode mode, AnalysisLevel level, bool selected)
    {
        var levels = Toggled(LevelsOf(mode), level, selected);

        return mode switch
        {
            AnalysisMode.Evaluation => this with { EvaluationLevels = levels },
            AnalysisMode.Rollout => this with { RolloutLevels = levels },
            AnalysisMode.BookRollout => this with { BookRolloutLevels = levels },
            _ => throw NoToggle(mode),
        };
    }

    /// <summary>This draft with <paramref name="contactType"/> checked or unchecked.</summary>
    /// <param name="contactType">The contact type.</param>
    /// <param name="selected">Whether it is checked.</param>
    /// <returns>The edited draft.</returns>
    public FilterDraft WithContactType(ContactType contactType, bool selected) =>
        this with { ContactTypes = Toggled(ContactTypes, contactType, selected) };

    /// <summary>This draft with <paramref name="roll"/> checked or unchecked.</summary>
    /// <param name="roll">The roll.</param>
    /// <param name="selected">Whether it is checked.</param>
    /// <returns>The edited draft.</returns>
    public FilterDraft WithDiceRoll(DiceRoll roll, bool selected) =>
        this with { DiceRolls = Toggled(DiceRolls, roll, selected) };

    /// <summary>
    /// Value equality over every held value, the text compared ordinally — a
    /// draft is what is on screen, so two drafts are equal exactly when the
    /// screen would show the same thing. This is not the config comparison
    /// that decides whether a selection is in effect: <c>0.10</c> and
    /// <c>0.1</c> are different drafts that parse to one config.
    /// </summary>
    /// <param name="other">The draft to compare against, or <see langword="null"/>.</param>
    /// <returns>Whether the two hold the same values.</returns>
    public bool Equals(FilterDraft? other) =>
        other is not null
        && string.Equals(PlayersText, other.PlayersText, StringComparison.Ordinal)
        && DecisionType == other.DecisionType
        && string.Equals(MatchScoresText, other.MatchScoresText, StringComparison.Ordinal)
        && string.Equals(ErrorMinText, other.ErrorMinText, StringComparison.Ordinal)
        && string.Equals(ErrorMaxText, other.ErrorMaxText, StringComparison.Ordinal)
        && string.Equals(MoveNumberMinText, other.MoveNumberMinText, StringComparison.Ordinal)
        && string.Equals(MoveNumberMaxText, other.MoveNumberMaxText, StringComparison.Ordinal)
        && ContactTypes.SetEquals(other.ContactTypes)
        && IncludeEvaluations == other.IncludeEvaluations
        && EvaluationLevels.SetEquals(other.EvaluationLevels)
        && IncludeRollouts == other.IncludeRollouts
        && RolloutLevels.SetEquals(other.RolloutLevels)
        && IncludeBookRollouts == other.IncludeBookRollouts
        && BookRolloutLevels.SetEquals(other.BookRolloutLevels)
        && DiceRolls.SetEquals(other.DiceRolls)
        && string.Equals(PositionPatternText, other.PositionPatternText, StringComparison.Ordinal);

    /// <summary>A hash consistent with <see cref="Equals(FilterDraft)"/>.</summary>
    /// <returns>The hash.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(PlayersText, StringComparer.Ordinal);
        hash.Add(DecisionType);
        hash.Add(MatchScoresText, StringComparer.Ordinal);
        hash.Add(ErrorMinText, StringComparer.Ordinal);
        hash.Add(ErrorMaxText, StringComparer.Ordinal);
        hash.Add(MoveNumberMinText, StringComparer.Ordinal);
        hash.Add(MoveNumberMaxText, StringComparer.Ordinal);
        hash.Add(IncludeEvaluations);
        hash.Add(IncludeRollouts);
        hash.Add(IncludeBookRollouts);
        hash.Add(PositionPatternText, StringComparer.Ordinal);

        // The sets are sorted, so their elements hash in one order for one
        // set — consistent with SetEquals above.
        foreach (var contactType in ContactTypes) hash.Add(contactType);
        foreach (var level in EvaluationLevels) hash.Add(level);
        foreach (var level in RolloutLevels) hash.Add(level);
        foreach (var level in BookRolloutLevels) hash.Add(level);
        foreach (var roll in DiceRolls) hash.Add(roll);

        return hash.ToHashCode();
    }

    // A range bound's text read as its field: blank text is no bound (true,
    // null); a number (BoundNumber) is that number; anything else is
    // unrepresentable (false, null), unfinished text such as "1e-" included.
    // The one mark the grammar admits is the decimal one, so reading it as a
    // point gives the number the user wrote, never another. Whether the number
    // is an admissible bound is the lib's verdict on the parsed config
    // (FilterConfig.GetInvalidFields()), and is not restated here: "1e999"
    // reads as infinity, and the lib decides it.
    private static bool TryReadNumber(string text, out double? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return true;

        if (!BoundNumber().IsMatch(trimmed)) return false;

        value = double.Parse(trimmed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        return true;
    }

    // What the four bound boxes accept as a number (halheinrich/backgammon#379,
    // Hal's ruling of 2026-10-08): every spelling a browser's number control
    // accepted — a sign, digits with or without a fraction or a bare
    // fraction, and an exponent — plus a leading '+', a mark with nothing
    // after it ("5." is 5), and either '.' or ',' as the decimal mark,
    // whatever the locale. There is no grouping: the mantissa holds one mark
    // at most, so "1,234" is 1.234, never 1234, and a second mark of either
    // kind is no number at all — nothing is removed to make one. ASCII digits
    // only; the space around the text is not part of it. The pattern box's
    // commas are its own grammar's and never come here.
    [GeneratedRegex(@"\A[+-]?(?:[0-9]+(?:[.,][0-9]*)?|[.,][0-9]+)(?:[eE][+-]?[0-9]+)?\z")]
    private static partial Regex BoundNumber();

    // A move-number bound's text read as its field: a number that is a whole
    // one within int's range — "3", "3.0", "3,0" and "3e0" alike, since all
    // are three — and no other. "1.5" and "1,5" are numbers but not move
    // numbers, so they are unrepresentable here rather than quietly no bound;
    // so is one past int's range, infinity included, which the config's field
    // cannot hold. The floor is the lib's.
    private static bool TryReadWholeNumber(string text, out int? value)
    {
        value = null;
        if (!TryReadNumber(text, out var number)) return false;
        if (number is not { } n) return true;

        if (!double.IsFinite(n) || n != Math.Floor(n) || n < int.MinValue || n > int.MaxValue) return false;

        value = (int)n;
        return true;
    }

    private static List<string> SplitTokens(string text) =>
        [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string Format<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

    private static ImmutableSortedSet<T> Toggled<T>(ImmutableSortedSet<T> set, T item, bool selected) =>
        selected ? set.Add(item) : set.Remove(item);

    private static ArgumentOutOfRangeException NoToggle(AnalysisMode mode) =>
        new(nameof(mode), mode, "Not an analysis mode the panel gives a toggle.");
}
