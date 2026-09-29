using System.Text.RegularExpressions;
using XgFilter_Lib.Patterns;

namespace XgFilter_Razor.Tests;

/// <summary>
/// The oracle for Hal's ruling on halheinrich/backgammon#275 (2026-09-25): the
/// filter help and the pattern field's copy state that the opponent's "at
/// least n" is written with the upper bound (<c>[1-6,,-1]</c>) and that a
/// lower bound (<c>[1-6,-1,]</c>) means "at most n". Both surfaces are pinned
/// against this one statement of it, so the two cannot be held to different
/// rulings.
/// <para>
/// Independent literals, deliberately: this is a pin on what the copy was
/// <b>ruled</b> to say, the carve-out in this repo's Pitfalls from the
/// no-literal-spellings rule. The two examples are the ruling's own, and each
/// is also asked of the grammar, so the pin reds when the grammar stops
/// reading an example the way the ruling says it reads — the copy is then
/// wrong, whatever it says.
/// </para>
/// </summary>
internal static class OpponentBoundRuling
{
    /// <summary>The ruling's example of the opponent's "at least n": an upper bound.</summary>
    internal const string AtLeastExample = "[1-6,,-1]";

    /// <summary>The ruling's example of the opponent's "at most n": a lower bound.</summary>
    internal const string AtMostExample = "[1-6,-1,]";

    /// <summary>
    /// Asserts that <paramref name="passage"/> states the ruling: both of its
    /// examples, and both of the readings it gives them. Whitespace is
    /// normalized first, so a line break in the markup is not a reword.
    /// </summary>
    internal static void AssertStatedIn(string passage)
    {
        var text = Regex.Replace(passage, @"\s+", " ");

        Assert.Contains(AtLeastExample, text);
        Assert.Contains("at least", text);
        Assert.Contains(AtMostExample, text);
        Assert.Contains("at most", text);

        // The grammar reads each example the way the ruling says: one range
        // counting the opponent's side, bounded above only for "at least" and
        // below only for "at most".
        Assert.True(OpponentRange(AtLeastExample) is { Min: null, Max: < 0 },
            $"'{AtLeastExample}' is no longer the opponent's upper-bound-only range");
        Assert.True(OpponentRange(AtMostExample) is { Min: < 0, Max: null },
            $"'{AtMostExample}' is no longer the opponent's lower-bound-only range");
    }

    private static CheckerSpanRange OpponentRange(string example) =>
        Assert.IsType<CheckerSpanRange>(Assert.Single(BoardPattern.Parse(example).Constraints));
}
