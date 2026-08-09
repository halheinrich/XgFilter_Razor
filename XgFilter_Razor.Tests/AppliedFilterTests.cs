using XgFilter_Lib.Filtering;

namespace XgFilter_Razor.Tests;

public class AppliedFilterTests
{
    private static readonly FilterSourceToken SourceA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken SourceB = FilterSourceToken.FromGeneration(2);

    [Fact]
    public void FreshInstance_NothingAppliedForAnySource()
    {
        var holder = new AppliedFilter();

        Assert.Null(holder.ConfigFor(SourceA));
        Assert.Null(holder.ConfigFor(SourceB));
    }

    [Fact]
    public void Set_KeysTheConfigToItsSource()
    {
        var holder = new AppliedFilter();
        var config = new FilterConfig();

        holder.Set(config, SourceA);

        Assert.Same(config, holder.ConfigFor(SourceA));
        Assert.Null(holder.ConfigFor(SourceB));
    }

    [Fact]
    public void Set_NullConfig_Throws()
    {
        var holder = new AppliedFilter();

        Assert.Throws<ArgumentNullException>(() => holder.Set(null!, SourceA));
    }

    // The one-lifetime contract (halheinrich/backgammon#92): Clear drops the
    // applied state entirely — config and source key together, no residue for
    // any source. This pins the spec's §3 ruling that nothing may answer from
    // filter history: only present ownership exists, and Clear ends it.
    [Fact]
    public void Clear_DropsTheAppliedState_NothingSurvivesForAnySource()
    {
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), SourceA);

        holder.Clear();

        Assert.Null(holder.ConfigFor(SourceA));
        Assert.Null(holder.ConfigFor(SourceB));
    }

    [Fact]
    public void ReSet_ForANewSource_MovesTheKey()
    {
        var holder = new AppliedFilter();
        holder.Set(new FilterConfig(), SourceA);
        var reapplied = new FilterConfig();

        holder.Set(reapplied, SourceB);

        Assert.Same(reapplied, holder.ConfigFor(SourceB));
        Assert.Null(holder.ConfigFor(SourceA));
    }
}
