using BgDataTypes_Lib.TestSupport;
using BgUiPrimitives_Razor;
using BgUiPrimitives_Razor.TestSupport;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;
using XgFilter_Razor.TestSupport;

namespace XgFilter_Razor.Tests;

/// <summary>
/// Pins the filter surface's route into the shared
/// <see cref="BrowserStorageCondition"/> (halheinrich/backgammon#424): the
/// adapter forwards every refusal and logs it, a failing logger cannot keep
/// the occurrence from being recorded, and the parameterless
/// <c>AddFilterSurface()</c> registers one condition shared with the adapter
/// the surface reports to.
/// </summary>
public class BrowserStorageConditionRefusalSinkTests
{
    private static JSException Refusal() => new("SecurityError: The operation is insecure.");

    // ── The adapter ────────────────────────────────────────────────────────

    [Fact]
    public void ARefusal_ReachesTheCondition_AndIsLoggedOnceWithTheException()
    {
        var condition = new BrowserStorageCondition();
        var logger = new RecordingLogger<BrowserStorageConditionRefusalSink>();
        var refusal = Refusal();

        new BrowserStorageConditionRefusalSink(condition, logger).ReportRefused(refusal);

        Assert.True(condition.HasOccurred);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(refusal, entry.Exception);
    }

    [Fact]
    public void ASecondRefusal_IsTheSameOccurrence_AndIsLoggedAgain()
    {
        var condition = new BrowserStorageCondition();
        var logger = new RecordingLogger<BrowserStorageConditionRefusalSink>();
        var sink = new BrowserStorageConditionRefusalSink(condition, logger);
        var notified = 0;
        using var attachment = condition.Attach(() => notified++);
        var beforeRefusals = notified;

        sink.ReportRefused(Refusal());
        sink.ReportRefused(Refusal());

        Assert.True(condition.HasOccurred);
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(beforeRefusals + 1, notified);
    }

    [Fact]
    public void AThrowingLogger_DoesNotKeepTheOccurrenceFromBeingRecorded_OrThrow()
    {
        var condition = new BrowserStorageCondition();
        var logger = new ThrowingLogger<BrowserStorageConditionRefusalSink>();

        var thrown = Record.Exception(
            () => new BrowserStorageConditionRefusalSink(condition, logger).ReportRefused(Refusal()));

        Assert.Null(thrown);
        Assert.True(condition.HasOccurred);
        Assert.Equal(1, logger.Attempts);
    }

    // ── The registration overload ──────────────────────────────────────────

    private static ServiceProvider ProviderWithTheOverload(int calls = 1)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        for (var i = 0; i < calls; i++)
            services.AddFilterSurface();
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void TheOverload_RegistersTheAdapter_OverOneAppScopedCondition()
    {
        using var provider = ProviderWithTheOverload();
        using var scope = provider.CreateScope();

        var sink = scope.ServiceProvider.GetRequiredService<BrowserStorageConditionRefusalSink>();
        var condition = scope.ServiceProvider.GetRequiredService<BrowserStorageCondition>();

        Assert.Same(condition, scope.ServiceProvider.GetRequiredService<BrowserStorageCondition>());
        Assert.Same(sink, scope.ServiceProvider.GetRequiredService<BrowserStorageConditionRefusalSink>());
        sink.ReportRefused(Refusal());
        Assert.True(condition.HasOccurred);

        using var other = provider.CreateScope();
        Assert.NotSame(condition, other.ServiceProvider.GetRequiredService<BrowserStorageCondition>());
    }

    [Fact]
    public void TheOverloadCalledTwice_RegistersOnce()
    {
        var services = new ServiceCollection();

        services.AddFilterSurface();
        var once = services.Count;
        services.AddFilterSurface();

        Assert.Equal(once, services.Count);
    }

    [Fact]
    public void TheGenericOverload_StillRegistersAHostsOwnSink_AndNotTheAdapter()
    {
        var services = new ServiceCollection();

        services.AddFilterSurface<RecordingRefusalSink>();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(BrowserStorageConditionRefusalSink));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(BrowserStorageCondition));
    }

    [Fact]
    public void TheFirstCallsSinkStands_WhenTheOverloadComesSecond()
    {
        var services = new ServiceCollection();
        services.AddFilterSurface<RecordingRefusalSink>();
        var storage = services.Single(d => d.ServiceType == typeof(FilterStorage));

        services.AddFilterSurface();

        Assert.Same(storage, services.Single(d => d.ServiceType == typeof(FilterStorage)));
    }
}

/// <summary>
/// The overload end to end: a planned refusal of a surface call lands in the
/// shared condition, through the surface's own sink.
/// </summary>
public class FilterSurfaceSharedConditionTests : BunitContext
{
    private static readonly FilterSourceToken Token = FilterSourceToken.FromGeneration(1);

    [Fact]
    public async Task ARefusedCommitWrite_LandsInTheSharedCondition()
    {
        Services.AddFilterSurface();
        var storage = BrowserStoragePlan.On(JSInterop);
        storage.ExpectFilterRestore(FilterRestoration.NothingStored);
        storage.ExpectFilterPanelMount();
        storage.ExpectFilterCommit(new FilterConfig { ErrorMin = 0.05 }, BrowserStorageWriteAnswer.Refused);
        var condition = Services.GetRequiredService<BrowserStorageCondition>();
        Services.GetRequiredService<FilterSetup>().ReportSource(Token);
        var cut = Render<FilterSurface>();
        Assert.False(condition.HasOccurred);

        cut.Find("#errorMin").Input("0.05");
        await cut.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Apply Filter"))
            .ClickAsync(new MouseEventArgs());

        Assert.True(condition.HasOccurred);
        storage.Verify();
    }
}
