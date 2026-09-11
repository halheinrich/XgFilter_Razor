using System.Reflection;
using System.Text.Json.Serialization;
using XgFilter_Razor.Components.Internal;

namespace XgFilter_Razor.Tests;

// The declarations that make this project's half of the trim gate a gate
// rather than a suggestion (halheinrich/backgammon#193, in the mould of
// XgFilter_Lib's posture pins for halheinrich/backgammon#129). The analyzer's
// own verdict is enforced by the build under TreatWarningsAsErrors; what
// these pin is that nobody quietly switches the premises off — flipping
// IsTrimmable out of the csproj, or moving the row set's context off
// metadata-only generation, fails a test rather than silently reopening the
// path that broke BgQuiz's trimmed publish.
public class XgFilterRazorTrimPostureTests
{
    // IsTrimmable surfaces in the built assembly as SDK-emitted metadata,
    // which is the one trace of the csproj setting a test can read. The
    // analyzer switch beside it leaves no such trace; it is exercised instead
    // by the build itself, which fails on a reflection-bound serializer call.
    [Fact]
    public void TheAssembly_DeclaresItselfTrimmable()
    {
        Assert.Contains(
            typeof(FilterPanel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
            a => a.Key == "IsTrimmable" && a.Value == "True");
    }

    // Metadata-only generation, the repo-wide rule every context declares: a
    // default-mode fast-path handler binds resolution to the context's own
    // private options. Pinned here so the one context this project owns keeps
    // the same declaration as every other link in the repo.
    [Fact]
    public void TheRowSetContext_GeneratesMetadataOnly()
    {
        var options = typeof(XgFilterRazorJsonContext)
            .GetCustomAttribute<JsonSourceGenerationOptionsAttribute>();

        Assert.NotNull(options);
        Assert.Equal(JsonSourceGenerationMode.Metadata, options!.GenerationMode);
    }
}
