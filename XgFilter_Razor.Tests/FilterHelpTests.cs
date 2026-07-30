using Bunit;
using XgFilter_Lib.Enums;
using XgFilter_Razor.Components;

namespace XgFilter_Razor.Tests;

// Deliberately minimal pins — FilterHelp is prose that will evolve, so these
// assert structure (it renders, every offered facet has an anchored heading),
// not copy. No Loose JSInterop needed: the component is render-only with no
// interop by contract.
public class FilterHelpTests : BunitContext
{
    // The facets the panel offers, as (facet, anchor id) pairs — the shelved
    // Position types / Play types are absent by design and pinned so below.
    private static readonly (FilterFacet Facet, string AnchorId)[] DocumentedFacets =
    [
        (FilterFacet.ErrorRange, "fh-error-range"),
        (FilterFacet.Players, "fh-players"),
        (FilterFacet.DecisionType, "fh-decision-type"),
        (FilterFacet.MatchScores, "fh-match-scores"),
        (FilterFacet.MoveNumberRange, "fh-move-number-range"),
        (FilterFacet.ContactTypes, "fh-contact-type"),
        (FilterFacet.AnalysisDepth, "fh-analysis-depth"),
        (FilterFacet.DiceRolls, "fh-dice-rolls"),
        (FilterFacet.PositionPattern, "fh-position-pattern"),
    ];

    [Fact]
    public void Render_Parameterless_Succeeds()
    {
        var cut = Render<FilterHelp>();

        Assert.NotNull(cut.Find(".filter-help"));
        Assert.NotNull(cut.Find("#fh-filters"));
    }

    // Every facet the panel offers gets a heading whose text is the lib's
    // FilterFacet [Description] (via ToLabel) — the same label the panel's
    // section headings and the hidden-active signal use — under a stable
    // fh-* anchor id for embedding hosts to link to.
    [Fact]
    public void EveryOfferedFacet_HasAnchoredHeadingWithLibLabel()
    {
        var cut = Render<FilterHelp>();

        foreach (var (facet, anchorId) in DocumentedFacets)
            Assert.Equal(facet.ToLabel(), cut.Find($"#{anchorId}").TextContent.Trim());
    }

    // The shelved facets stay undocumented until their UI returns — help that
    // describes a control the panel doesn't offer is worse than none.
    [Fact]
    public void ShelvedFacets_AreNotDocumented()
    {
        var cut = Render<FilterHelp>();

        Assert.DoesNotContain(FilterFacet.PositionTypes.ToLabel(), cut.Markup);
        Assert.DoesNotContain(FilterFacet.PlayTypes.ToLabel(), cut.Markup);
    }

    // Render-only by contract: no JS interop may occur. JSInterop stays in
    // Strict mode (the BunitContext default) here, so any interop call would
    // throw — rendering cleanly is the proof.
    [Fact]
    public void Render_IssuesNoJsInterop()
    {
        var cut = Render<FilterHelp>();

        Assert.Empty(JSInterop.Invocations);
        Assert.NotNull(cut.Find("#fh-analysis-depth"));
    }
}
