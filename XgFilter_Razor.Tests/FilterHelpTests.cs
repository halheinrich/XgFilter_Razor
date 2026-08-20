using Bunit;
using XgFilter_Lib.Enums;
using XgFilter_Razor.Components;
using XgFilter_Razor.Components.Internal;

namespace XgFilter_Razor.Tests;

// Deliberately minimal pins — FilterHelp is prose that will evolve, so these
// assert structure (it renders, every documented topic has an anchored
// heading, the outline the host asked for is the outline it gets), not copy.
// No Loose JSInterop needed: the component is render-only with no interop by
// contract.
public class FilterHelpTests : BunitContext
{
    // The heading level these tests embed at, when the level itself is not
    // what is under test. Arbitrary — that is the point of the parameter.
    private const int TestHeadingLevel = 3;

    private IRenderedComponent<FilterHelp> RenderHelp(int headingLevel = TestHeadingLevel) =>
        Render<FilterHelp>(parameters => parameters.Add(p => p.HeadingLevel, headingLevel));

    // The facets the panel offers, as (facet, anchor id) pairs — the shelved
    // Position types / Play types are absent by design and pinned so below.
    // The ids come from FilterHelp's own constants (internal, visible here via
    // InternalsVisibleTo). An anchor id is structure, not copy, so per the
    // copy-pin SSOT ruling these assertions are wiring — each section renders
    // under the identity the component names — and a second literal here would
    // only be a spare copy of a value the component now has one of. The
    // independent-literal oracle for what a reader can actually link to lives
    // in the e2e suite.
    private static readonly (FilterFacet Facet, string AnchorId)[] DocumentedFacets =
    [
        (FilterFacet.ErrorRange, FilterHelp.ErrorRangeAnchorId),
        (FilterFacet.Players, FilterHelp.PlayersAnchorId),
        (FilterFacet.DecisionType, FilterHelp.DecisionTypeAnchorId),
        (FilterFacet.MatchScores, FilterHelp.MatchScoresAnchorId),
        (FilterFacet.MoveNumberRange, FilterHelp.MoveNumberRangeAnchorId),
        (FilterFacet.ContactTypes, FilterHelp.ContactTypesAnchorId),
        (FilterFacet.AnalysisDepth, FilterHelp.AnalysisDepthAnchorId),
        (FilterFacet.DiceRolls, FilterHelp.DiceRollsAnchorId),
        (FilterFacet.PositionPattern, FilterHelp.PositionPatternAnchorId),
    ];

    [Fact]
    public void Render_Succeeds()
    {
        var cut = RenderHelp();

        Assert.NotNull(cut.Find(".filter-help"));
        Assert.NotNull(cut.Find($"#{FilterHelp.LeadAnchorId}"));
    }

    // Every facet the panel offers gets a heading whose text is the lib's
    // FilterFacet [Description] (via ToLabel) — the same label the panel's
    // section headings and the hidden-active signal use — under a stable
    // fh-* anchor id for embedding hosts to link to.
    [Fact]
    public void EveryOfferedFacet_HasAnchoredHeadingWithLibLabel()
    {
        var cut = RenderHelp();

        foreach (var (facet, anchorId) in DocumentedFacets)
            Assert.Equal(facet.ToLabel(), cut.Find($"#{anchorId}").TextContent.Trim());
    }

    // The shelved facets stay undocumented until their UI returns — help that
    // describes a control the panel doesn't offer is worse than none.
    [Fact]
    public void ShelvedFacets_AreNotDocumented()
    {
        var cut = RenderHelp();

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
        var cut = RenderHelp();

        Assert.Empty(JSInterop.Invocations);
        Assert.NotNull(cut.Find($"#{FilterHelp.AnalysisDepthAnchorId}"));
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
        var cut = RenderHelp();

        var keys = cut.FindAll($"#{FilterHelp.StorageSectionAnchorId} ~ ul code")
                      .Select(e => e.TextContent.Trim())
                      .ToArray();

        Assert.Equal(new[] { FilterPanel.ConfigKey, FilterPanel.DisclosureKey }, keys);
    }

    // The two non-facet sections carry stable anchor ids on headings, like
    // every facet section here — those anchors are the embedding surface a
    // host deep-links into instead of restating what they document. Structure
    // only: the wording is the e2e suite's to pin.
    [Theory]
    [InlineData(FilterHelp.ChromeSectionAnchorId)]
    [InlineData(FilterHelp.StorageSectionAnchorId)]
    public void NonFacetSection_HasAnchoredHeading(string anchorId)
    {
        var cut = RenderHelp();

        Assert.NotNull(cut.Find($".filter-help section h{TestHeadingLevel + 1}#{anchorId}"));
    }

    // The one exported section identity, pinned to the DOM it names. Wiring,
    // not copy: a host links to StorageSectionAnchorId and writes
    // StorageSectionHeading into its own sentence, so the export is worthless
    // unless the heading the reader lands on is rendered from that same pair.
    // Re-typing either value into the markup — or renaming a constant without
    // the markup following — breaks this, which is the whole point of the
    // promotion: the id/heading contract is the compiler's and this test's to
    // keep, no longer prose's.
    [Fact]
    public void StorageSection_RendersItsHeadingFromTheExportedConstants()
    {
        var cut = RenderHelp();

        var heading = cut.Find($".filter-help section > #{FilterHelp.StorageSectionAnchorId}");

        Assert.Equal(FilterHelp.StorageSectionHeading, heading.TextContent.Trim());
    }

    // ── Heading level ──────────────────────────────────────────────────────

    // The outline is the host's to state and this block's to honor exactly:
    // the lead heading renders at the level given, every section one below.
    // Both levels move together — a host embedding a tier deeper must not end
    // up with sections that outrank their own lead.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void HeadingLevel_PutsLeadAtThatLevel_AndSectionsOneBelow(int level)
    {
        var cut = RenderHelp(level);

        Assert.Equal($"H{level}", cut.Find($"#{FilterHelp.LeadAnchorId}").TagName);

        foreach (var (_, anchorId) in DocumentedFacets)
            Assert.Equal($"H{level + 1}", cut.Find($"#{anchorId}").TagName);
    }

    // The anchor ids are the embedding contract — hosts may already link to
    // them — so they must survive the levels moving underneath them.
    [Fact]
    public void HeadingLevel_DoesNotDisturbTheAnchorIds()
    {
        var shallow = RenderHelp(2);
        var deep = RenderHelp(5);

        foreach (var (_, anchorId) in DocumentedFacets)
        {
            Assert.NotNull(shallow.Find($"#{anchorId}"));
            Assert.NotNull(deep.Find($"#{anchorId}"));
        }

        // The exported one especially: its docs promise hosts a link that
        // survives whatever level the host embeds at.
        Assert.NotNull(shallow.Find($"#{FilterHelp.StorageSectionAnchorId}"));
        Assert.NotNull(deep.Find($"#{FilterHelp.StorageSectionAnchorId}"));
    }

    // A level change on a live instance re-tags every heading. Worth pinning
    // rather than assuming: the headings are built as render fragments with
    // computed element names, and an element-name change is the one edit a
    // render-tree diff must handle by replacing the node rather than patching
    // it.
    [Fact]
    public void HeadingLevel_ChangedOnALiveInstance_ReTagsEveryHeading()
    {
        var cut = RenderHelp(2);

        cut.Render(parameters => parameters.Add(p => p.HeadingLevel, 4));

        Assert.Equal("H4", cut.Find($"#{FilterHelp.LeadAnchorId}").TagName);
        Assert.Equal("H5", cut.Find($"#{FilterHelp.ErrorRangeAnchorId}").TagName);
        Assert.Equal("H5", cut.Find($"#{FilterHelp.StorageSectionAnchorId}").TagName);
    }

    // Out of range is refused, not clamped and not rendered: an h0 or an h6
    // lead with h7 sections is malformed markup, and silently emitting it
    // would defeat the whole point of making the level explicit. Zero is the
    // unset default, which is the case EditorRequired catches at build time in
    // a host — this is the belt for the paths RZ2012 cannot see.
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void HeadingLevel_OutOfRange_Throws(int level)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => RenderHelp(level));

        Assert.Equal(nameof(FilterHelp.HeadingLevel), ex.ParamName);
    }

    // Unbound is out of range by construction (the default is zero), so a host
    // that ignores the RZ2012 — or a caller that never sees it — fails loudly
    // rather than rendering somebody else's outline.
    [Fact]
    public void HeadingLevel_Unbound_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Render<FilterHelp>());
    }
}
