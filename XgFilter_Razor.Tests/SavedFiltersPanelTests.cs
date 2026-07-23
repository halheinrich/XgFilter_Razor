using Bunit;
using XgFilter_Lib.Filtering;
using XgFilter_Razor.Components;

namespace XgFilter_Razor.Tests;

public class SavedFiltersPanelTests : BunitContext
{
    // Captured callback payloads, one list per gesture, so every test can
    // assert both "the right callback fired with the right name" and "the
    // other callbacks stayed silent."
    private readonly List<string> _loadRequests = [];
    private readonly List<string> _saveRequests = [];
    private readonly List<string> _deleteRequests = [];

    private IRenderedComponent<SavedFiltersPanel> RenderPanel(
        NamedFilterCollection filters,
        bool canPersist = true,
        string? persistDisabledReason = null)
        => Render<SavedFiltersPanel>(parameters => parameters
            .Add(p => p.Filters, filters)
            .Add(p => p.OnLoadRequested, (string n) => _loadRequests.Add(n))
            .Add(p => p.OnSaveAsRequested, (string n) => _saveRequests.Add(n))
            .Add(p => p.OnDeleteRequested, (string n) => _deleteRequests.Add(n))
            .Add(p => p.CanPersist, canPersist)
            .Add(p => p.PersistDisabledReason, persistDisabledReason));

    private static NamedFilterCollection Collection(params string[] names)
    {
        var filters = NamedFilterCollection.Empty;
        foreach (var name in names)
            filters = filters.With(name, new FilterConfig());
        return filters;
    }

    // The panel renders Names verbatim — the lib owns the canonical sort. Build
    // the collection out of order so a panel-local re-sort (or insertion-order
    // passthrough) would produce a different sequence and trip this.
    [Fact]
    public void Render_Names_AppearInCollectionOrder()
    {
        var cut = RenderPanel(Collection("beta", "Alpha", "gamma"));

        var rowNames = cut.FindAll("li.list-group-item > span").Select(s => s.TextContent);
        Assert.Equal(Collection("beta", "Alpha", "gamma").Names, rowNames);
    }

    [Fact]
    public void Render_EmptyCollection_ShowsEmptyHint()
    {
        var cut = RenderPanel(NamedFilterCollection.Empty);

        Assert.Contains("No saved filters yet.", cut.Markup);
        Assert.Empty(cut.FindAll("li.list-group-item"));
    }

    // Load is a pure read gesture: one click, one callback, no confirm step,
    // nothing else raised.
    [Fact]
    public async Task LoadButton_RaisesOnLoadRequested_WithRowName()
    {
        var cut = RenderPanel(Collection("Race", "Blitz"));

        await ClickRowButtonAsync(cut, "Blitz", "Load");

        Assert.Equal(["Blitz"], _loadRequests);
        Assert.Empty(_saveRequests);
        Assert.Empty(_deleteRequests);
    }

    // Delete is destructive, so the first click only poses the inline confirm —
    // the callback must not fire until the user commits.
    [Fact]
    public async Task DeleteButton_ShowsConfirm_NoCallbackYet()
    {
        var cut = RenderPanel(Collection("Race"));

        await ClickRowButtonAsync(cut, "Race", "Delete");

        Assert.Contains("Delete 'Race'?", cut.Markup);
        Assert.Empty(_deleteRequests);
    }

    [Fact]
    public async Task DeleteConfirm_RaisesOnDeleteRequested_WithRowName()
    {
        var cut = RenderPanel(Collection("Race"));

        await ClickRowButtonAsync(cut, "Race", "Delete");
        await ClickRowButtonAsync(cut, "Race", "Confirm delete");

        Assert.Equal(["Race"], _deleteRequests);
        Assert.Empty(_loadRequests);
        Assert.Empty(_saveRequests);
    }

    [Fact]
    public async Task DeleteCancel_RaisesNothing_RestoresRowButtons()
    {
        var cut = RenderPanel(Collection("Race"));

        await ClickRowButtonAsync(cut, "Race", "Delete");
        await ClickRowButtonAsync(cut, "Race", "Cancel");

        Assert.Empty(_deleteRequests);
        Assert.DoesNotContain("Delete 'Race'?", cut.Markup);
        // The normal row affordances are back.
        Assert.NotNull(FindRowButton(cut, "Race", "Load"));
        Assert.NotNull(FindRowButton(cut, "Race", "Delete"));
    }

    // The With contract says the caller normalizes the name; the panel is that
    // caller's proxy, so the raised payload is trimmed. The input clears on the
    // confirmation channel — the host's new Filters instance arriving — not on
    // the raise, so the raise alone leaves the typed name in place.
    [Fact]
    public async Task SaveAs_NewName_RaisesTrimmedName_ClearsInputOnSwap()
    {
        var cut = RenderPanel(NamedFilterCollection.Empty);

        cut.Find("#saveFilterName").Input("  Race  ");
        await ClickSaveButtonAsync(cut);

        // Raised trimmed; nothing cleared yet — no new document has arrived.
        Assert.Equal(["Race"], _saveRequests);
        Assert.DoesNotContain("Overwrite", cut.Markup);

        // The host mediated the save and passed the new collection down; that
        // swap is the confirmation channel that clears the input.
        cut.Render(parameters => parameters.Add(p => p.Filters, Collection("Race")));

        Assert.Equal(string.Empty, cut.Find("#saveFilterName").GetAttribute("value"));
    }

    // The behavioral fix this change exists for: a save the host refuses (an
    // invalid pattern) or that fails to persist raises no new Filters instance,
    // so the typed name must survive for the user to fix and retry. The old
    // optimistic clear ate it on hope; the clear now waits for confirmation.
    [Fact]
    public async Task SaveAs_NoFiltersSwap_KeepsTypedName()
    {
        var cut = RenderPanel(NamedFilterCollection.Empty);

        cut.Find("#saveFilterName").Input("Race");
        await ClickSaveButtonAsync(cut);

        Assert.Equal(["Race"], _saveRequests);
        Assert.Equal("Race", cut.Find("#saveFilterName").GetAttribute("value"));
    }

    // Blank (after trim) can never satisfy With's name contract, so Save is
    // disabled — and the handler guard holds even for programmatic dispatch,
    // which ignores the disabled attribute.
    [Fact]
    public async Task SaveAs_BlankName_SaveDisabled_NoCallback()
    {
        var cut = RenderPanel(NamedFilterCollection.Empty);

        cut.Find("#saveFilterName").Input("   ");
        var saveButton = FindSaveButton(cut);

        Assert.True(saveButton.HasAttribute("disabled"));
        await saveButton.ClickAsync(new());
        Assert.Empty(_saveRequests);
    }

    // Saving under an existing name is add-or-replace at the document level;
    // the panel poses the inline overwrite confirm first and raises only after
    // the user commits.
    [Fact]
    public async Task SaveAs_ExistingName_RaisesOnlyAfterOverwriteConfirm()
    {
        var cut = RenderPanel(Collection("Race"));

        cut.Find("#saveFilterName").Input("Race");
        await ClickSaveButtonAsync(cut);

        Assert.Contains("Overwrite 'Race'?", cut.Markup);
        Assert.Empty(_saveRequests);

        await ClickButtonByTextAsync(cut, "Overwrite");

        // Raised — but the overwrite prompt, like the typed name, rides the
        // confirmation swap, not the click. Until the host passes the new
        // collection down the prompt stays posed, so a refused overwrite is
        // retryable in place.
        Assert.Equal(["Race"], _saveRequests);
        Assert.Contains("Overwrite 'Race'?", cut.Markup);

        cut.Render(parameters => parameters.Add(p => p.Filters, Collection("Race")));

        Assert.DoesNotContain("Overwrite 'Race'?", cut.Markup);
        Assert.Equal(string.Empty, cut.Find("#saveFilterName").GetAttribute("value"));
    }

    // Pins that overwrite detection flows through Filters.Contains — the
    // lib's OrdinalIgnoreCase name rule — not a panel-local comparison, which
    // would default to ordinal and silently miss this.
    [Fact]
    public async Task SaveAs_ExistingNameDifferentCase_TriggersOverwriteConfirm()
    {
        var cut = RenderPanel(Collection("Race"));

        cut.Find("#saveFilterName").Input("RACE");
        await ClickSaveButtonAsync(cut);

        Assert.Contains("Overwrite 'RACE'?", cut.Markup);
        Assert.Empty(_saveRequests);
    }

    [Fact]
    public async Task SaveAs_OverwriteCancel_RaisesNothing_KeepsTypedName()
    {
        var cut = RenderPanel(Collection("Race"));

        cut.Find("#saveFilterName").Input("Race");
        await ClickSaveButtonAsync(cut);
        await ClickButtonByTextAsync(cut, "Cancel");

        Assert.Empty(_saveRequests);
        // The typed name survives a cancelled overwrite — the user declined
        // the replace, not the name.
        Assert.Equal("Race", cut.Find("#saveFilterName").GetAttribute("value"));
    }

    // CanPersist gates the mutating affordances only: Save and Delete are
    // disabled (with the host's reason surfaced as title and hint), the
    // handler guards hold against programmatic clicks, and Load — read-only —
    // keeps working.
    [Fact]
    public async Task CanPersistFalse_DisablesSaveAndDelete_LoadStillWorks()
    {
        const string reason = "Grant folder access to save filters.";
        var cut = RenderPanel(Collection("Race"), canPersist: false, persistDisabledReason: reason);

        cut.Find("#saveFilterName").Input("New");

        Assert.True(FindSaveButton(cut).HasAttribute("disabled"));
        Assert.True(FindRowButton(cut, "Race", "Delete")!.HasAttribute("disabled"));
        Assert.Equal(reason, FindSaveButton(cut).GetAttribute("title"));
        Assert.Equal(reason, FindRowButton(cut, "Race", "Delete")!.GetAttribute("title"));
        Assert.Contains(reason, cut.Find(".form-text").TextContent);

        // Programmatic dispatch ignores the disabled attribute; the handler
        // guards are what pin the contract. Each element is re-found just
        // before its click — every dispatch re-renders, staling old refs.
        await ClickSaveButtonAsync(cut);
        await ClickRowButtonAsync(cut, "Race", "Delete");
        await ClickRowButtonAsync(cut, "Race", "Load");

        Assert.Empty(_saveRequests);
        Assert.Empty(_deleteRequests);
        Assert.DoesNotContain("Delete 'Race'?", cut.Markup);
        Assert.Equal(["Race"], _loadRequests);
    }

    // A new Filters instance means the host acted (or reloaded); an inline
    // confirm posed against the old document must not survive the swap.
    [Fact]
    public async Task FiltersParameterSwap_ClearsPendingConfirms()
    {
        var cut = RenderPanel(Collection("Race"));

        await ClickRowButtonAsync(cut, "Race", "Delete");
        Assert.Contains("Delete 'Race'?", cut.Markup);

        cut.Render(parameters => parameters.Add(p => p.Filters, Collection("Race", "Blitz")));

        Assert.DoesNotContain("Delete 'Race'?", cut.Markup);
        Assert.Empty(_deleteRequests);
    }

    // The clear rides ANY new document, not just the user's own save. A delete
    // (or a host re-pick / reload) swaps Filters and clears a half-typed name
    // too — the panel does not track which gesture caused the swap. This pins
    // that deliberate tradeoff: one invariant (a new document invalidates all
    // in-flight view-state), no causation guessing, at the cost of a rare
    // retype after an event the user themselves caused.
    [Fact]
    public void FiltersParameterSwap_ClearsTypedSaveName()
    {
        var cut = RenderPanel(Collection("Race", "Blitz"));

        cut.Find("#saveFilterName").Input("Half-typed");
        // The host deletes a different filter and passes the smaller document
        // down — a swap the typing did not cause.
        cut.Render(parameters => parameters.Add(p => p.Filters, Collection("Race")));

        Assert.Equal(string.Empty, cut.Find("#saveFilterName").GetAttribute("value"));
    }

    // ── Gesture helpers ─────────────────────────────────────────────────────
    // Rows are located by their name span, buttons within a row by their text,
    // so tests read as user gestures rather than CSS selectors.

    private static AngleSharp.Dom.IElement? FindRowButton(
        IRenderedComponent<SavedFiltersPanel> cut, string name, string buttonText)
    {
        var row = cut.FindAll("li.list-group-item")
            .Single(li => li.QuerySelector("span")?.TextContent == name);
        return row.QuerySelectorAll("button")
            .SingleOrDefault(b => b.TextContent.Trim() == buttonText);
    }

    private static async Task ClickRowButtonAsync(
        IRenderedComponent<SavedFiltersPanel> cut, string name, string buttonText)
    {
        var button = FindRowButton(cut, name, buttonText);
        Assert.NotNull(button);
        await button.ClickAsync(new());
    }

    private static AngleSharp.Dom.IElement FindSaveButton(
        IRenderedComponent<SavedFiltersPanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save");

    private static Task ClickSaveButtonAsync(IRenderedComponent<SavedFiltersPanel> cut) =>
        FindSaveButton(cut).ClickAsync(new());

    private static async Task ClickButtonByTextAsync(
        IRenderedComponent<SavedFiltersPanel> cut, string buttonText)
    {
        var button = cut.FindAll("button").Single(b => b.TextContent.Trim() == buttonText);
        await button.ClickAsync(new());
    }
}
