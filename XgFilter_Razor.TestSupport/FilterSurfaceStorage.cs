namespace XgFilter_Razor.TestSupport;

using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using XgFilter_Lib.Enums;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components.Internal;

/// <summary>
/// The filter surface's browser storage, in the surface's own terms, for a
/// host's test: what the surface reads and writes, stated on the test's
/// <see cref="BrowserStoragePlan"/> without naming a key or an interop call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> A host test that plans storage plans every call — the
/// plan is strict, so a call nobody expected fails where it is made — and the
/// filter surface makes calls of its own whenever it is mounted. The surface's
/// keys are deliberately not consumer surface, so a host cannot state those
/// calls itself; before this, the only way was to repeat the key, a literal a
/// producer-side rename would leave behind while the host's test went on
/// passing for the wrong reason. So the host states intent here and the
/// producer supplies the calls (halheinrich/backgammon#374, on
/// halheinrich/backgammon#377's planner).
/// </para>
/// <para>
/// <b>What the surface does, in these terms.</b> Once per app boot — once per
/// test context — the state owner reads the remembered selection, at the
/// first interactive render of the panel (<see cref="ExpectFilterRestore(BrowserStoragePlan, FilterConfig)"/>
/// and its siblings). Every mount of the panel reads its two display
/// preferences (<see cref="ExpectFilterPanelMount"/>). Every commit — Apply,
/// Clear filters — writes the committed selection
/// (<see cref="ExpectFilterCommit"/>). Opening or folding the container
/// over the rows, and opening or closing a row, writes that preference
/// (<see cref="ExpectFilterFoldToggle"/>, <see cref="ExpectFilterRowsToggle"/>).
/// Nothing else is called.
/// </para>
/// <para>
/// Each method declares on the plan it is given and returns what the plan
/// returns, so a test still verifies with <see cref="BrowserStoragePlan.Verify"/>,
/// orders with <see cref="BrowserStoragePlan.RequireOrder"/>, and releases a
/// held call itself.
/// </para>
/// </remarks>
public static class FilterSurfaceStorage
{
    // A stored value no version of the selection's document trio reads — what
    // an unreadable remembered selection looks like. Pinned by this repo's
    // tests to be refused by FilterConfig.TryFromJson.
    private const string UnreadableSelection = "}{ not a filter selection";

    /// <summary>
    /// Expect this boot's restoration to read <paramref name="stored"/> as the
    /// remembered selection — a previous visit's commit.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="stored">The selection the previous visit committed.</param>
    /// <returns>The expectation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static BrowserStorageExpectation ExpectFilterRestore(this BrowserStoragePlan plan, FilterConfig stored)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ExpectRead(BrowserStorageArea.Local, FilterStorage.ConfigKey, RestoreAnswer(stored));
    }

    /// <summary>
    /// Expect this boot's restoration to settle as <paramref name="outcome"/>
    /// without a selection: nothing remembered, the read refused, or a
    /// remembered value that is not a selection.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="outcome">
    /// <see cref="FilterRestoration.NothingStored"/>, <see cref="FilterRestoration.Refused"/> or
    /// <see cref="FilterRestoration.Unreadable"/>.
    /// </param>
    /// <returns>The expectation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcome"/> is <see cref="FilterRestoration.Restored"/>, which needs the
    /// selection (the other overload), or <see cref="FilterRestoration.Pending"/>, which is
    /// not an answer (hold the read instead).
    /// </exception>
    public static BrowserStorageExpectation ExpectFilterRestore(this BrowserStoragePlan plan, FilterRestoration outcome)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ExpectRead(BrowserStorageArea.Local, FilterStorage.ConfigKey, RestoreAnswer(outcome));
    }

    /// <summary>
    /// Expect this boot's restoration read and hold it, so the test can act
    /// while restoration is pending. Release it with <see cref="RestoreAnswer(FilterConfig)"/>
    /// or <see cref="RestoreAnswer(FilterRestoration)"/>.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <returns>The held read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    public static BrowserStorageHeldRead ExpectHeldFilterRestore(this BrowserStoragePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ExpectHeldRead(BrowserStorageArea.Local, FilterStorage.ConfigKey);
    }

    /// <summary>The browser's answer to the restoration read when <paramref name="stored"/> was remembered.</summary>
    /// <param name="stored">The remembered selection.</param>
    /// <returns>The answer, in the lib's own JSON for the selection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stored"/> is <see langword="null"/>.</exception>
    public static BrowserStorageReadAnswer RestoreAnswer(FilterConfig stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return BrowserStorageReadAnswer.Stored(stored.ToJson());
    }

    /// <summary>The browser's answer to the restoration read that settles it as <paramref name="outcome"/>.</summary>
    /// <param name="outcome">
    /// <see cref="FilterRestoration.NothingStored"/>, <see cref="FilterRestoration.Refused"/> or
    /// <see cref="FilterRestoration.Unreadable"/>.
    /// </param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcome"/> is <see cref="FilterRestoration.Restored"/> or <see cref="FilterRestoration.Pending"/>.
    /// </exception>
    public static BrowserStorageReadAnswer RestoreAnswer(FilterRestoration outcome) => outcome switch
    {
        FilterRestoration.NothingStored => BrowserStorageReadAnswer.Absent,
        FilterRestoration.Refused => BrowserStorageReadAnswer.Refused,
        FilterRestoration.Unreadable => BrowserStorageReadAnswer.Stored(UnreadableSelection),
        FilterRestoration.Restored => throw new ArgumentException(
            "A restored selection is stated with the selection: RestoreAnswer(FilterConfig).", nameof(outcome)),
        _ => throw new ArgumentException(
            $"{outcome} is not an answer the browser gives; hold the read to keep restoration pending.",
            nameof(outcome)),
    };

    /// <summary>
    /// Expect <paramref name="times"/> mounts of the filter panel, each reading
    /// its two display preferences — which rows are open, and whether the
    /// container over them is — and finding nothing stored, so each mount
    /// starts folded with every row closed.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="times">How many mounts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than one.</exception>
    public static void ExpectFilterPanelMount(this BrowserStoragePlan plan, int times = 1) =>
        ExpectMount(plan, BrowserStorageReadAnswer.Absent, times);

    /// <summary>
    /// <see cref="ExpectFilterPanelMount"/> in a browser that refuses storage:
    /// each mount's two reads are refused, so each mount starts folded with
    /// every row closed, and each refusal reaches the host's sink.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="times">How many mounts.</param>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than one.</exception>
    public static void ExpectFilterPanelMountRefused(this BrowserStoragePlan plan, int times = 1) =>
        ExpectMount(plan, BrowserStorageReadAnswer.Refused, times);

    /// <summary>
    /// Expect a commit — Apply or Clear filters — to write
    /// <paramref name="committed"/> as the remembered selection, answered with
    /// <paramref name="answer"/>.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="committed">The selection committed; Clear filters commits <c>new FilterConfig()</c>.</param>
    /// <param name="answer">How the browser answers the write.</param>
    /// <param name="times">How many such commits.</param>
    /// <returns>The expectation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than one.</exception>
    public static BrowserStorageExpectation ExpectFilterCommit(
        this BrowserStoragePlan plan, FilterConfig committed, BrowserStorageWriteAnswer answer, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ExpectWrite(BrowserStorageArea.Local, FilterStorage.ConfigKey, SelectionJson(committed), answer, times);
    }

    /// <summary>
    /// Expect one commit's write of <paramref name="committed"/> and hold it,
    /// so the test can act while it is pending.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="committed">The selection committed.</param>
    /// <returns>The held write.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static BrowserStorageHeldWrite ExpectHeldFilterCommit(this BrowserStoragePlan plan, FilterConfig committed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ExpectHeldWrite(BrowserStorageArea.Local, FilterStorage.ConfigKey, SelectionJson(committed));
    }

    /// <summary>
    /// Expect the container over the rows to be opened (<paramref name="open"/>
    /// true) or folded, and that choice written.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="open">Whether the toggle opens the container.</param>
    /// <param name="answer">How the browser answers the write.</param>
    /// <param name="times">How many such toggles.</param>
    /// <returns>The expectation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than one.</exception>
    public static BrowserStorageExpectation ExpectFilterFoldToggle(
        this BrowserStoragePlan plan, bool open, BrowserStorageWriteAnswer answer, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.ExpectWrite(
            BrowserStorageArea.Local, FilterStorage.MoreFiltersKey, FilterPanel.SerializeFold(open), answer, times);
    }

    /// <summary>
    /// Expect a row to be opened or closed, leaving exactly
    /// <paramref name="openRows"/> open, and that set written.
    /// </summary>
    /// <param name="plan">The test's storage plan.</param>
    /// <param name="openRows">The rows open after the toggle, in any order.</param>
    /// <param name="answer">How the browser answers the write.</param>
    /// <param name="times">How many such toggles.</param>
    /// <returns>The expectation.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is less than one.</exception>
    public static BrowserStorageExpectation ExpectFilterRowsToggle(
        this BrowserStoragePlan plan, IEnumerable<FilterFacet> openRows, BrowserStorageWriteAnswer answer, int times = 1)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(openRows);
        return plan.ExpectWrite(
            BrowserStorageArea.Local, FilterStorage.DisclosureKey, FilterPanel.SerializeOpenRows([.. openRows]),
            answer, times);
    }

    private static void ExpectMount(BrowserStoragePlan plan, BrowserStorageReadAnswer answer, int times)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.ExpectRead(BrowserStorageArea.Local, FilterStorage.MoreFiltersKey, answer, times);
        plan.ExpectRead(BrowserStorageArea.Local, FilterStorage.DisclosureKey, answer, times);
    }

    // The selection as the owner writes it: the lib's own JSON of the config
    // the draft showing it parses to, so a test's config and the commit agree
    // on one spelling however the test built it.
    private static string SelectionJson(FilterConfig committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        return FilterDraft.From(committed).ToConfig().ToJson();
    }
}
