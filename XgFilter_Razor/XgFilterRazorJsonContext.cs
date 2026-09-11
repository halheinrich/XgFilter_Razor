using System.Text.Json.Serialization;

namespace XgFilter_Razor;

/// <summary>
/// The source-generated <see cref="JsonSerializerContext"/> for the one value
/// this project serializes itself: the set of expanded facet rows the filter
/// panel persists under its disclosure key, a JSON array of facet member
/// names. Trim-safe <c>System.Text.Json</c> metadata produced at compile time
/// instead of by runtime reflection — the mould of XgFilter_Lib's
/// <c>XgFilterJsonContext</c> (halheinrich/backgammon#129), applied here in
/// halheinrich/backgammon#193 after the reflection-bound overloads the row set
/// first shipped on broke BgQuiz's trimmed publish. The mechanism changes, the
/// bytes do not: the stored value is byte-identical to the reflection path's,
/// pinned by test against a literal.
///
/// <para>
/// <b>What is declared, and why.</b> <see cref="string"/>[] only. The filter
/// selection itself never passes through this context: its shape is
/// XgFilter_Lib's, and the panel round-trips it through that library's own
/// document trio (<c>FilterConfig.ToJson</c> / <c>TryFromJson</c>) without
/// naming it to a serializer. The row set is the exception because its shape
/// belongs to no other party — it is this project's view-state.
/// </para>
///
/// <para>
/// <b>Internal, deliberately</b> — unlike XgFilter_Lib's context, which is
/// public because a consumer names that library's types to serializers it
/// does not own. Nothing here is a wire type anyone else sees: the row set
/// lives in one browser's <c>localStorage</c>, is written and read only by
/// the panel, and a consumer that could resolve its metadata would be a
/// consumer that knew the panel's storage format, which the storage keys'
/// own <c>internal</c> posture exists to prevent.
/// </para>
///
/// <para>
/// <b>Metadata-only generation</b>, the arc's binding rule, kept even though
/// this context chains nothing: a default-mode fast-path handler would bind
/// resolution to this context's private options, and declaring the same mode
/// as every other context in the repo means nobody has to re-derive whether
/// that matters for this one.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(string[]))]
internal sealed partial class XgFilterRazorJsonContext : JsonSerializerContext
{
}
