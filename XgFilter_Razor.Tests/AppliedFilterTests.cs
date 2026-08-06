using XgFilter_Lib.Filtering;

namespace XgFilter_Razor.Tests;

public class AppliedFilterTests
{
    private static readonly FilterSourceToken SourceA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken SourceB = FilterSourceToken.FromGeneration(2);

    [Fact]
    public void FreshInstance_NothingApplied_ForAnySource()
    {
        var holder = new AppliedFilter();

        Assert.False(holder.IsApplied);
        Assert.Null(holder.Config);
        Assert.False(holder.WasAppliedFor(SourceA));
    }

    [Fact]
    public void Set_RecordsConfig_AndStampsTheSource()
    {
        var holder = new AppliedFilter();
        var config = new FilterConfig();

        holder.Set(config, SourceA);

        Assert.True(holder.IsApplied);
        Assert.Same(config, holder.Config);
        Assert.True(holder.WasAppliedFor(SourceA));
        Assert.False(holder.WasAppliedFor(SourceB));
    }

    [Fact]
    public void Set_NullConfig_Throws()
    {
        var holder = new AppliedFilter();

        Assert.Throws<ArgumentNullException>(() => holder.Set(null!, SourceA));
    }

    // The two-lifetime contract: an edit clears the *config* (the start-like
    // gate re-closes) but the source stamp survives — "this source has been
    // filtered at least once" is not un-answered by a half-typed edit, so
    // gestures gated on the stamp (BgQuiz's Apply Mix) stay open.
    [Fact]
    public void Clear_DropsConfig_ButTheSourceStampSurvives()
    {
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), SourceA);

        holder.Clear();

        Assert.False(holder.IsApplied);
        Assert.Null(holder.Config);
        Assert.True(holder.WasAppliedFor(SourceA));
    }

    [Fact]
    public void ReSet_ForANewSource_MovesTheStamp()
    {
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), SourceA);

        holder.Set(new FilterConfig(), SourceB);

        Assert.True(holder.WasAppliedFor(SourceB));
        Assert.False(holder.WasAppliedFor(SourceA));
    }
}
