using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using XgFilter_Razor.Components;

namespace XgFilter_Razor.Tests;

/// <summary>
/// A host page as these suites need one: it hosts <see cref="FilterSurface"/>
/// and shows its own storage notice (<c>#hostStorageNotice</c>) from its
/// app-scoped refusal sink — re-rendering off the sink's change notification,
/// as a real host's page does — so a test can pin that a refusal reaches what
/// the host renders, including one arriving after the first render or while
/// the page was gone.
/// </summary>
public sealed class RefusalNoticeHost : ComponentBase, IDisposable
{
    [Inject] private RecordingRefusalSink Refusals { get; set; } = default!;

    protected override void OnInitialized() => Refusals.Reported += OnReported;

    private void OnReported() => _ = InvokeAsync(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (Refusals.Refusals.Count > 0)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "id", "hostStorageNotice");
            builder.AddContent(2, $"{Refusals.Refusals.Count} refused");
            builder.CloseElement();
        }

        builder.OpenComponent<FilterSurface>(3);
        builder.CloseComponent();
    }

    /// <inheritdoc/>
    public void Dispose() => Refusals.Reported -= OnReported;
}
