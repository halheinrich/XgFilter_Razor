namespace XgFilter_Razor;

using System.Globalization;

/// <summary>
/// Opaque, equatable identity of the <i>source</i> a filter selection is applied
/// against — the corpus half of "this config was applied to that source". The
/// host mints one per source via the intent-revealing factories
/// (<see cref="FromGeneration"/> for BgQuiz-style monotonic pick counters,
/// <see cref="FromPath"/> for folder-path sources) and the producer only ever
/// compares tokens for equality: it never inspects, parses, or interprets them.
/// "What counts as the same source" is therefore entirely the host's ruling —
/// the token just carries that ruling across the producer boundary.
///
/// <para>
/// Equality is the record struct's value equality over the wrapped string —
/// ordinal, case-sensitive, no normalization. A host whose source identity is
/// case-insensitive (say, a Windows path) normalizes <em>before</em> minting;
/// the token stays deliberately dumb so its equality can never disagree with
/// the host's intent. The factories prefix their domain onto the wrapped value,
/// so tokens from different factories can never collide
/// (<c>FromGeneration(5)</c> ≠ <c>FromPath("5")</c>).
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
    /// Compared ordinally and case-sensitively; a host with case-insensitive
    /// source identity normalizes before minting.
    /// </summary>
    /// <param name="path">The host's source identity. Never interpreted, only compared.</param>
    /// <returns>A token equal to exactly the tokens minted from the same string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public static FilterSourceToken FromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new($"path:{path}");
    }

    /// <summary>
    /// The wrapped value, for diagnostics only — logs and test failure messages.
    /// Never parse it: the format is an implementation detail of the factories.
    /// </summary>
    public override string ToString() => _value ?? string.Empty;
}
