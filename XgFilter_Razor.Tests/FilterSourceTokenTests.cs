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

    // Equality is ordinal and case-sensitive by contract: the token is opaque,
    // so a host with case-insensitive source identity (a Windows path)
    // normalizes BEFORE minting. This pins that the token itself never folds
    // case on the host's behalf.
    [Fact]
    public void FromPath_DifferentCase_TokensDiffer()
    {
        Assert.NotEqual(
            FilterSourceToken.FromPath(@"D:\XG"),
            FilterSourceToken.FromPath(@"d:\xg"));
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
