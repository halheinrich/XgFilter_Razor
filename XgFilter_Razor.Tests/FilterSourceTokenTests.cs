namespace XgFilter_Razor.Tests;

public class FilterSourceTokenTests
{
    [Fact]
    public void FromGeneration_SameGeneration_TokensAreEqual()
    {
        Assert.Equal(FilterSourceToken.FromGeneration(7), FilterSourceToken.FromGeneration(7));
    }

    [Fact]
    public void FromGeneration_DifferentGenerations_TokensDiffer()
    {
        Assert.NotEqual(FilterSourceToken.FromGeneration(7), FilterSourceToken.FromGeneration(8));
    }

    [Fact]
    public void FromPath_SamePath_TokensAreEqual()
    {
        Assert.Equal(
            FilterSourceToken.FromPath(@"D:\xg\matches"),
            FilterSourceToken.FromPath(@"D:\xg\matches"));
    }

    // Path identity is the FACTORY's, not the host's: FromPath folds case
    // itself, so a host that passes the path in whatever spelling it holds
    // cannot mint a token that fails to match its own previous visit's. This
    // pin is the inverse of the one it replaced — the contract flipped.
    [Fact]
    public void FromPath_DifferentCase_TokensAreEqual()
    {
        Assert.Equal(
            FilterSourceToken.FromPath(@"D:\XG"),
            FilterSourceToken.FromPath(@"d:\xg"));
    }

    // The other half of the rule: a trailing separator is insignificant, in
    // either spelling and however many. Hand-rolled rather than
    // Path.TrimEndingDirectorySeparator because this runs in WebAssembly, where
    // that call would not recognize the backslash at all — so the backslash
    // case here is the one that would regress if the BCL call ever crept back.
    [Theory]
    [InlineData(@"D:\xg\matches", @"D:\xg\matches\")]
    [InlineData(@"D:\xg\matches", @"D:\xg\matches\\")]
    [InlineData("D:/xg/matches", "D:/xg/matches/")]
    public void FromPath_TrailingSeparator_IsInsignificant(string bare, string trailing)
    {
        Assert.Equal(FilterSourceToken.FromPath(bare), FilterSourceToken.FromPath(trailing));
    }

    // The trim's deliberate limit: a trailing separator is insignificant, but
    // the separator CHARACTER is not normalized — `/` and `\` are not asserted
    // to be the same character, because that sameness is false wherever `\` is
    // a legal filename character. Pinned so the rule cannot quietly widen.
    [Fact]
    public void FromPath_SeparatorSpelling_IsNotUnified()
    {
        Assert.NotEqual(
            FilterSourceToken.FromPath(@"D:\xg\matches"),
            FilterSourceToken.FromPath("D:/xg/matches"));
    }

    // Both halves at once, which is how a real host respelling arrives.
    [Fact]
    public void FromPath_CaseAndTrailingSeparatorTogether_TokensAreEqual()
    {
        Assert.Equal(
            FilterSourceToken.FromPath(@"D:\XG\Matches"),
            FilterSourceToken.FromPath(@"d:\xg\matches\"));
    }

    // Normalization folds spellings together; it must not fold sources
    // together. Two genuinely different folders stay two sources — including
    // the sibling case that a careless "trim everything" rule would collapse.
    [Theory]
    [InlineData(@"D:\xg\matches", @"D:\xg\sessions")]
    [InlineData(@"D:\xg\matches", @"E:\xg\matches")]
    [InlineData(@"D:\xg\matches", @"D:\xg\matches\archive")]
    public void FromPath_DistinctPaths_TokensDiffer(string left, string right)
    {
        Assert.NotEqual(FilterSourceToken.FromPath(left), FilterSourceToken.FromPath(right));
    }

    // FromGeneration is untouched by the path rule: it wraps its counter as-is,
    // so its identity remains exactly "same number, same token".
    [Fact]
    public void FromGeneration_IsUnaffectedByThePathNormalizationRule()
    {
        Assert.Equal(FilterSourceToken.FromGeneration(12), FilterSourceToken.FromGeneration(12));
        Assert.NotEqual(FilterSourceToken.FromGeneration(12), FilterSourceToken.FromGeneration(13));
    }

    // The factories prefix their domain onto the wrapped value, so tokens from
    // different factories can never collide even when the host-supplied parts
    // render identically.
    [Fact]
    public void FromGeneration_And_FromPath_NeverCollide()
    {
        Assert.NotEqual(FilterSourceToken.FromGeneration(5), FilterSourceToken.FromPath("5"));
    }

    [Fact]
    public void FromPath_NullPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FilterSourceToken.FromPath(null!));
    }

    // default(FilterSourceToken) is reachable (any struct's is) but must never
    // read as a real source — no factory-minted token equals it.
    [Fact]
    public void Default_EqualsNoMintedToken()
    {
        Assert.NotEqual(default, FilterSourceToken.FromGeneration(0));
        Assert.NotEqual(default, FilterSourceToken.FromPath(string.Empty));
    }
}
