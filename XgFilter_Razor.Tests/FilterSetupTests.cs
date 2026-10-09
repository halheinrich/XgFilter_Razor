using Bunit;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins <see cref="FilterSetup"/>'s own contract (<c>SPEC-filtering.md</c> §4,
/// halheinrich/backgammon#374), driven directly rather than through a
/// component: what attaching delivers, that only real changes are published,
/// what a source report ends, the gates the operations guard on, the once-per-
/// boot restoration and its four outcomes, and that nothing a consumer holds
/// reaches the owner. The acceptance cases, through the composite, are
/// <see cref="FilterSetupAcceptanceTests"/>.
/// </summary>
public class FilterSetupTests : BunitContext
{
    private static readonly FilterSourceToken TokenA = FilterSourceToken.FromGeneration(1);
    private static readonly FilterSourceToken TokenB = FilterSourceToken.FromGeneration(2);

    private readonly RecordingRefusalSink _refusals = new();
    private readonly BrowserStoragePlan _storage;

    public FilterSetupTests()
    {
        Services.AddSingleton(_refusals);
        Services.AddFilterSurface<RecordingRefusalSink>();
        _storage = BrowserStoragePlan.On(JSInterop);
    }

    private FilterSetup Setup => Services.GetRequiredService<FilterSetup>();

    // A boot whose restoration found nothing, for the tests about what
    // follows it.
    private async Task SettledAsync(FilterSourceToken? source = null)
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        await Setup.RestoreAsync();
        if (source is { } s) Setup.ReportSource(s);
    }

    // ── Registration ────────────────────────────────────────────────────────

    // One owner per app scope, the same instance whoever asks: what lets a
    // host page and the surface it hosts read and change one state.
    [Fact]
    public void Registration_GivesOneOwnerPerScope()
    {
        Assert.Same(Setup, Services.GetRequiredService<FilterSetup>());
        _storage.Verify();
    }

    [Fact]
    public void Registration_WithoutTheHostsSink_FailsWhenTheOwnerIsFirstMade()
    {
        using var host = new BunitContext();
        host.Services.AddFilterSurface<RecordingRefusalSink>();

        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<FilterSetup>());
    }

    // ── Observing ───────────────────────────────────────────────────────────

    // Attaching delivers the current snapshot before Attach returns — a
    // remounted component learns the existing state without any change being
    // manufactured — and then each real change, in order, until disposed.
    [Fact]
    public async Task Attach_DeliversTheCurrentSnapshotFirst_ThenEachChange_UntilDisposed()
    {
        await SettledAsync(TokenA);
        var seen = new List<FilterSetupSnapshot>();

        var attachment = Setup.Attach(seen.Add);
        Assert.Same(Setup.Current, Assert.Single(seen));

        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        Assert.Equal(2, seen.Count);
        Assert.Same(Setup.Current, seen[^1]);

        attachment.Dispose();
        attachment.Dispose();
        Setup.Edit(d => d with { ErrorMinText = "0.2" });
        Assert.Equal(2, seen.Count);
        _storage.Verify();
    }

    // Only real changes: the same source reported again, and an edit or a
    // staged saved filter that leaves the draft as it was, publish nothing —
    // though such a gesture still supersedes a pending restoration
    // (LoadingAnEmptySavedFilter_DuringAPendingRestore_SupersedesIt): the
    // two are separate questions.
    [Fact]
    public async Task NothingIsPublished_WhenNothingChanged()
    {
        await SettledAsync(TokenA);
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        var published = 0;
        Setup.Attach(_ => published++);
        published = 0;

        Setup.ReportSource(TokenA);
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        Setup.Stage(new FilterConfig { ErrorMin = 0.1 });

        Assert.Equal(0, published);
        _storage.Verify();
    }

    // A snapshot describes its moment: nothing that happens afterwards
    // changes one already handed out.
    [Fact]
    public async Task ASnapshot_IsNotChangedByWhatHappensAfterIt()
    {
        await SettledAsync(TokenA);
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();
        var held = Setup.Current;

        Setup.Edit(d => d with { ErrorMinText = "0.9" });
        Setup.ReportSource(TokenB);

        Assert.Equal(TokenA, held.Source);
        Assert.Equal("0.1", held.Draft.ErrorMinText);
        Assert.True(held.IsInEffectFor(TokenA));
        _storage.Verify();
    }

    // ── What a host reads ───────────────────────────────────────────────────

    // Asked source-relatively: the in-effect filter answers only for the
    // source the setup belongs to, so a host that forgot to report a change
    // reads its gate dark rather than another corpus's filter.
    [Fact]
    public async Task InEffect_IsAnsweredOnlyForTheSetupsOwnSource()
    {
        await SettledAsync(TokenA);
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();

        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        Assert.Null(Setup.Current.ConfigInEffectFor(TokenB));
        _storage.Verify();
    }

    // Every config handed out is new, so a consumer may keep or change one
    // without reaching the owner.
    [Fact]
    public async Task ConfigInEffectFor_HandsOutANewConfigEachCall()
    {
        await SettledAsync(TokenA);
        Setup.Edit(d => d with { PlayersText = "Hal" });
        _storage.ExpectFilterCommit(new FilterConfig { Players = ["Hal"] }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();

        var first = Setup.Current.ConfigInEffectFor(TokenA)!;
        first.Players.Add("Intruder");

        Assert.NotSame(first, Setup.Current.ConfigInEffectFor(TokenA));
        Assert.Equal(new FilterConfig { Players = ["Hal"] }, Setup.Current.ConfigInEffectFor(TokenA));
        _storage.Verify();
    }

    // ── Setups ──────────────────────────────────────────────────────────────

    // A different source ends the setup: a new generation, the baseline
    // dropped, the draft kept. The same source again is nothing.
    [Fact]
    public async Task ReportingADifferentSource_EndsTheSetup_KeepingTheDraft()
    {
        await SettledAsync(TokenA);
        Setup.Edit(d => d with { ErrorMinText = "0.1" });
        _storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.1 }, BrowserStorageWriteAnswer.Succeeded);
        await Setup.ApplyAsync();
        var generation = Setup.Current.Generation;

        Setup.ReportSource(TokenB);

        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.Baseline);
        Assert.False(Setup.Current.IsInEffectFor(TokenB));
        Assert.Equal("0.1", Setup.Current.Draft.ErrorMinText);
        _storage.Verify();
    }

    // No source is a source report like any other: it ends the setup, and
    // nothing is in effect without one.
    [Fact]
    public async Task ReportingNoSource_EndsTheSetup()
    {
        await SettledAsync(TokenA);
        var generation = Setup.Current.Generation;

        Setup.ReportSource(null);

        Assert.Equal(generation + 1, Setup.Current.Generation);
        Assert.Null(Setup.Current.Source);
        Assert.False(Setup.Current.CanApply);
        _storage.Verify();
    }

    // ── The gates the operations guard on ──────────────────────────────────

    // Apply needs a source and a settled restoration (§2), and commits
    // nothing otherwise — not even when called past the disabled button.
    [Fact]
    public async Task Apply_WithoutASource_OrBeforeRestorationSettles_CommitsNothing()
    {
        Setup.ReportSource(TokenA);
        Assert.False(Setup.Current.CanApply);
        await Setup.ApplyAsync();
        Assert.Null(Setup.Current.Baseline);

        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        await Setup.RestoreAsync();
        Setup.ReportSource(null);
        Assert.False(Setup.Current.CanApply);
        await Setup.ApplyAsync();

        Assert.Null(Setup.Current.Baseline);
        _storage.Verify();
    }

    [Fact]
    public async Task Apply_OfAnInvalidDraft_CommitsNothing()
    {
        await SettledAsync(TokenA);
        Setup.Edit(d => d with { MoveNumberMinText = "1.5" });

        await Setup.ApplyAsync();

        Assert.Null(Setup.Current.Baseline);
        _storage.Verify();
    }

    // Clear waits for a source and for restoration to settle (§4's
    // transitions table; Hal's ruling of 2026-10-08), then commits the empty
    // selection as the baseline. Before either, it changes nothing and
    // writes nothing — the strict plan has no write for it — while editing
    // goes on.
    [Fact]
    public async Task Clear_WaitsForASourceAndForRestoration_ThenCommitsTheEmptySelection()
    {
        Setup.Edit(d => d with { PlayersText = "Hal" });
        await Setup.ClearAsync();
        Assert.Equal("Hal", Setup.Current.Draft.PlayersText);
        Assert.False(Setup.Current.CanClear);

        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        await Setup.RestoreAsync();
        await Setup.ClearAsync();
        Assert.Equal("Hal", Setup.Current.Draft.PlayersText);
        Assert.Null(Setup.Current.Baseline);
        Assert.False(Setup.Current.CanClear);

        Setup.ReportSource(TokenA);
        Assert.True(Setup.Current.CanClear);
        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Succeeded);
        await Setup.ClearAsync();

        Assert.Equal(FilterDraft.Empty, Setup.Current.Draft);
        Assert.Equal(FilterDraft.Empty, Setup.Current.Baseline);
        _storage.Verify();
    }

    // ── The restoration ────────────────────────────────────────────────────

    // Once per boot: every call shares the first call's read.
    [Fact]
    public async Task Restoration_ReadsOnce_HoweverOftenItIsAskedFor()
    {
        _storage.ExpectFilterRestore(FilterRestoration.NothingStored);

        var first = Setup.RestoreAsync();
        var second = Setup.RestoreAsync();
        await first;

        Assert.Same(first, second);
        Assert.Same(first, Setup.RestoreAsync());
        _storage.Verify();
    }

    public static TheoryData<FilterRestoration, bool> Outcomes => new()
    {
        { FilterRestoration.NothingStored, false },
        { FilterRestoration.Refused, false },
        { FilterRestoration.Unreadable, true },
    };

    // Each outcome is recorded as itself, the defaults stand for every
    // failure, the failed-restore notice is for the unreadable document
    // alone, and a refused read reaches the host's sink.
    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task Restoration_RecordsItsOutcome_AndLeavesTheDefaults(FilterRestoration outcome, bool failureNotice)
    {
        _storage.ExpectFilterRestore(outcome);

        await Setup.RestoreAsync();

        Assert.Equal(outcome, Setup.Current.Restoration);
        Assert.Equal(FilterDraft.Empty, Setup.Current.Draft);
        Assert.Equal(failureNotice, Setup.Current.IsFailureNoticeShowing);
        Assert.False(Setup.Current.IsRestoredNoticeShowing);
        Assert.Equal(outcome == FilterRestoration.Refused ? 1 : 0, _refusals.Refusals.Count);
        _storage.Verify();
    }

    [Fact]
    public async Task Restoration_OfAStoredSelection_PutsItOnScreen_WithTheNotice()
    {
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.1 });

        await Setup.RestoreAsync();

        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        Assert.Equal("0.1", Setup.Current.Draft.ErrorMinText);
        Assert.True(Setup.Current.IsRestoredNoticeShowing);
        _storage.Verify();
    }

    // The restoration changes the draft and nothing else of the setup: no
    // baseline, no generation — a restored selection is a choice, not a
    // consent (§4).
    // ── Readiness (§1, halheinrich/backgammon#266) ────────────────────────

    // The empty selection is ready without Apply once restoration settles —
    // resolved, valid, restricting nothing, with a source — and Apply has
    // nothing to do over it.
    [Fact]
    public async Task TheEmptySelection_IsInEffect_WithoutApply()
    {
        await SettledAsync(TokenA);

        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        Assert.Equal(new FilterConfig(), Setup.Current.ConfigInEffectFor(TokenA));
        Assert.False(Setup.Current.CanApply);
        _storage.Verify();
    }

    // None of these is evidence that the user chose no filter: a pending
    // restoration, a failed one, an invalid draft, and no source at all.
    [Fact]
    public async Task TheEmptySelection_IsNotReady_WhilePending_AfterAFailure_OrWithoutASource()
    {
        Setup.ReportSource(TokenA);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));

        _storage.ExpectFilterRestore(FilterRestoration.Unreadable);
        await Setup.RestoreAsync();
        Assert.False(Setup.Current.IsResolved);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.True(Setup.Current.CanApply);

        Setup.ReportSource(null);
        Assert.False(Setup.Current.IsReadyEmpty);
        _storage.Verify();
    }

    // Having a source is the filter's condition (§2): with none, a settled,
    // resolved, empty draft is still not ready — there is nothing to run it
    // against — so nothing claims it is in effect, not even Apply's reason.
    [Fact]
    public async Task TheEmptySelection_IsNotReady_WithoutASource()
    {
        await SettledAsync();

        Assert.True(Setup.Current.IsResolved);
        Assert.True(Setup.Current.Draft.RestrictsNothing);
        Assert.False(Setup.Current.IsReadyEmpty);
        Assert.False(Setup.Current.IsTheEmptySelectionInEffect);
        _storage.Verify();
    }

    [Fact]
    public async Task AnUnrepresentableBound_IsNotTheEmptySelection()
    {
        await SettledAsync(TokenA);

        Setup.Edit(d => d with { MoveNumberMinText = "1.5" });

        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        Assert.False(Setup.Current.Draft.RestrictsNothing);
        _storage.Verify();
    }

    // Resolution is current state: a failed restoration unresolves the
    // defaults, a valid Apply or Clear resolves the draft, and a source
    // change leaves resolution as it is — it travels with the draft.
    [Fact]
    public async Task Resolution_TravelsWithTheDraft_AcrossASourceChange()
    {
        _storage.ExpectFilterRestore(FilterRestoration.Refused);
        await Setup.RestoreAsync();
        Setup.ReportSource(TokenA);
        _storage.ExpectFilterCommit(new FilterConfig(), BrowserStorageWriteAnswer.Refused);

        await Setup.ClearAsync();
        Setup.ReportSource(TokenB);

        Assert.True(Setup.Current.IsResolved);
        Assert.True(Setup.Current.IsInEffectFor(TokenB));
        Assert.Equal(FilterRestoration.Refused, Setup.Current.Restoration);
        _storage.Verify();
    }

    // A restored valid empty selection is ready, and nothing differs from a
    // first visit, so it raises no restored notice (§1, Reload).
    [Fact]
    public async Task ARestoredEmptySelection_IsReady_AndRaisesNoNotice()
    {
        Setup.ReportSource(TokenA);
        _storage.ExpectFilterRestore(new FilterConfig());

        await Setup.RestoreAsync();

        Assert.Equal(FilterRestoration.Restored, Setup.Current.Restoration);
        Assert.False(Setup.Current.IsRestoredNoticeShowing);
        Assert.True(Setup.Current.IsInEffectFor(TokenA));
        _storage.Verify();
    }

    [Fact]
    public async Task Restoration_TouchesNeitherTheBaselineNorTheGeneration()
    {
        Setup.ReportSource(TokenA);
        var generation = Setup.Current.Generation;
        _storage.ExpectFilterRestore(new FilterConfig { ErrorMin = 0.1 });

        await Setup.RestoreAsync();

        Assert.Null(Setup.Current.Baseline);
        Assert.Equal(generation, Setup.Current.Generation);
        Assert.False(Setup.Current.IsInEffectFor(TokenA));
        _storage.Verify();
    }
}
