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

    // Render-only by contract: no JS interop may occur — documenting what the
    // panel persists must not turn this component into a storage participant.
    // JSInterop stays in Strict mode (the BunitContext default) here, so any
    // interop call would throw — rendering cleanly is the proof.
    [Fact]
    public void Render_IssuesNoJsInterop()
    {
        var cut = Render<FilterHelp>();

        Assert.Empty(JSInterop.Invocations);
        Assert.NotNull(cut.Find("#fh-analysis-depth"));
    }

    // Wiring, not content. Per the copy-pin SSOT ruling, an independent literal
    // is the right oracle for "the user can read X" and lives in the e2e suite;
    // here the property under test is that the key names in the copy are the
    // *same* source the panel writes with. Two literals would agree today and
    // drift silently the day a key is renamed, so this assertion deliberately
    // references FilterPanel's constants (visible test-only via
    // InternalsVisibleTo) — that is what makes it catch the drift.
    [Fact]
    public void WhatIsRemembered_NamesTheKeysFromFilterPanelsConstants()
    {
        var cut = Render<FilterHelp>();

        var keys = cut.FindAll("#fh-what-is-remembered ~ ul code")
                      .Select(e => e.TextContent.Trim())
                      .ToArray();

        Assert.Equal(new[] { FilterPanel.ConfigKey, FilterPanel.DisclosureKey }, keys);
    }

    // The section carries a stable anchor id on a heading, like every facet
    // section here — that anchor is the embedding surface a host's own
    // data-ownership copy points at instead of restating what the panel
    // persists. Structure only: the wording is the e2e suite's to pin.
    [Fact]
    public void WhatIsRemembered_HasAnchoredHeading()
    {
        var cut = Render<FilterHelp>();

        Assert.NotNull(cut.Find(".filter-help section h5#fh-what-is-remembered"));
    }
}
