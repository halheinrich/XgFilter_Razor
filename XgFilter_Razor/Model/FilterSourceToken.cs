namespace XgFilter_Razor;

using System.Globalization;

/// <summary>
/// Opaque, equatable identity of the <i>source</i> a filter selection is applied
/// against — the corpus half of "this config was applied to that source". The
/// host mints one per source via the intent-revealing factories
/// (<see cref="FromGeneration"/> for BgQuiz-style monotonic pick counters,
/// <see cref="FromPath"/> for folder-path sources), supplying the raw
/// host-domain value; everything after that is this type's. Each factory owns
/// what counts as "the same source" in its own domain, and the producer only
/// ever compares the results for equality: it never inspects, parses, or
/// interprets them.
///
/// <para>
/// Equality is the record struct's value equality over the wrapped string, so
/// a factory decides identity by deciding what it wraps. <see cref="FromPath"/>
/// normalizes before wrapping (see its remarks for the rule) because path
/// identity is a property of paths, not of the host that happens to hold one:
/// a host that forgot to fold case would otherwise mint a token that fails to
/// match the one its own previous visit minted, and nothing about that failure
/// is visible at the mint site. <see cref="FromGeneration"/> wraps its counter
/// as-is — an <see cref="int"/> has one spelling. The factories prefix their
/// domain onto the wrapped value, so tokens from different factories can never
/// collide (<c>FromGeneration(5)</c> ≠ <c>FromPath("5")</c>).
/// </para>
///
/// <para>
/// "No source yet" is expressed as <see cref="Nullable{T}"/>
/// (<c>FilterSourceToken?</c>) at use sites — see
/// <see cref="AppliedFilter"/>. A <c>default(FilterSourceToken)</c>
/// (reachable, as for any struct) equals no factory-minted token, so an
/// accidental default can never read as a real source.
/// </para>
/// </summary>
public readonly record struct FilterSourceToken
{
    // The host-supplied identity, prefixed by the minting factory's domain.
    // Null only on default(FilterSourceToken), which no factory produces.
    private readonly string? _value;

    private FilterSourceToken(string value) => _value = value;

    /// <summary>
    /// Mint a token from a monotonic generation counter — the
    /// <c>PickedProblemFolder.PickGeneration</c> idiom, where every pick (and
    /// every clear) bumps the counter so ending a setup expires stale answers
    /// by construction.
    /// </summary>
    /// <param name="generation">The host's current generation number.</param>
    /// <returns>A token equal to exactly the tokens minted from the same generation.</returns>
    public static FilterSourceToken FromGeneration(int generation) =>
        new($"generation:{generation.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// Mint a token from a path-like source identity — a picked folder, a file
    /// selection summary, whatever string the host rules identifies its source.
    /// The path is normalized for identity before it is wrapped, so the same
    /// folder spelled differently mints the same token: the host passes the
    /// path in the spelling it holds and never pre-folds one of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is case-fold to upper invariant, then trailing directory
    /// separators are insignificant. Case, because the sources these hosts pick
    /// are Windows folders, whose identity is case-insensitive; <em>upper</em>
    /// rather than lower because uppercase is the round-trip-safe folding
    /// direction for a comparison key, and invariant so a host's UI culture can
    /// never move a stored selection's source out from under it.
    /// </para>
    /// <para>
    /// The trim is hand-rolled rather than
    /// <see cref="Path.TrimEndingDirectorySeparator(string)"/>, deliberately:
    /// this component runs in WebAssembly, where .NET uses Unix path semantics
    /// and only <c>/</c> counts as a separator — so the BCL call leaves a
    /// trailing <c>\</c> in place, which is the exact spelling the rule exists
    /// to absorb. The paths are Windows-shaped; the runtime is not Windows, so
    /// a platform-conditional call is the wrong tool for a platform-independent
    /// identity rule. Unlike that call this also trims a root's separator
    /// (<c>D:\</c> mints as <c>D:</c>), which is harmless here: nothing ever
    /// reconstitutes a path from a token, and both spellings of one root still
    /// collide to one identity — the whole property being bought.
    /// </para>
    /// <para>
    /// Normalization stops there. <c>Path.GetFullPath</c> would resolve
    /// relative segments against the process working directory — nondeterministic,
    /// and meaningless in a browser where no filesystem stands behind the
    /// string — and unifying <c>/</c> with <c>\</c> would assert a sameness
    /// that is false wherever <c>\</c> is a legal filename character.
    /// </para>
    /// </remarks>
    /// <param name="path">The host's source identity, in the host's own spelling.</param>
    /// <returns>A token equal to exactly the tokens minted from paths that normalize the same.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static FilterSourceToken FromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new($"path:{NormalizeForIdentity(path)}");
    }

    // The path-identity rule itself, stated once. Why each half is here — and
    // why the trim is not Path.TrimEndingDirectorySeparator — is in FromPath's
    // remarks, where a reader of the public contract will find it.
    private static string NormalizeForIdentity(string path) =>
        path.TrimEnd('\\', '/').ToUpperInvariant();

    /// <summary>
    /// The wrapped value, for diagnostics only — logs and test failure messages.
    /// Never parse it: the format is an implementation detail of the factories.
    /// </summary>
    public override string ToString() => _value ?? string.Empty;
}
