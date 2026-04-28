# XgFilter_Razor

> Session conventions: [`../CLAUDE.md`](../CLAUDE.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Razor Class Library (`Microsoft.NET.Sdk.Razor`) / bUnit.
Visual Studio 2026 on Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\XgFilter_Razor\XgFilter_Razor.slnx`

## Repo

https://github.com/halheinrich/XgFilter_Razor — branch `main`.

## Depends on

- **XgFilter_Lib** — `DecisionFilterSet`, the seven concrete filters
  (`PlayerFilter`, `DecisionTypeFilter`, `MatchScoreFilter`,
  `ErrorRangeFilter`, `MoveNumberFilter`, `PositionTypeFilter`,
  `PlayTypeFilter`), the enums (`DecisionTypeOption`, `PositionType`,
  `PlayType`), and the `EnumLabel.ToLabel<TEnum>()` extension. Project
  reference, not a package.
- **BgDataTypes_Lib** — referenced explicitly even though
  `FilterPanel.razor` does not use any `BgDataTypes_Lib` type directly
  (XgFilter_Lib brings it in transitively). Explicit because consumers of
  `DecisionFilterSet` typically work against `IDecisionFilterData`, and
  the precedent in `ExtractFromXgToCsv.Client.csproj` is to list every
  directly-conceptual dependency.

## Directory tree

```
XgFilter_Razor.slnx
XgFilter_Razor/
  XgFilter_Razor.csproj
  _Imports.razor
  Components/
    FilterPanel.razor                — markup + @code state
  Shared/
    FilterConfig.cs                  — JSON DTO mirroring DecisionFilterSet
  wwwroot/
XgFilter_Razor.Tests/
  XgFilter_Razor.Tests.csproj
  FilterPanelTests.cs                — bUnit smoke tests
```

## Architecture

### Thin Razor wrapper, by design

Parallel to `BgDiag_Razor`'s relationship with `BackgammonDiagram_Lib`:
this subproject lets `XgFilter_Lib` stay free of any Blazor / Razor
dependency. All filter logic, classification, and enum labels live in the
core lib; this project only binds those primitives into a Blazor component
and surfaces the resulting `DecisionFilterSet` via an `EventCallback`.

### Single component: `FilterPanel`

`FilterPanel` owns the entire filter-form UI as a Bootstrap card with
controls for player names, decision type, match scores, error range, move
number range, position type, and play type. State is held in private
fields on the component instance.

The component emits filter results only on **Apply** (or **Reset**) — not
on every keystroke. While the user types, `OnFilterDirty` fires so the
parent can disable a Run button until Apply is clicked. On Apply, the
component:

1. Persists each control value to `localStorage` via `IJSRuntime`.
2. Builds a `DecisionFilterSet` and raises `OnFiltersChanged` (consumed
   by in-memory / WASM filtering paths).
3. Builds a `FilterConfig` DTO and raises `OnFilterConfigChanged`
   (consumed by HTTP-POST paths that need a JSON-serializable payload).

`OnAfterRenderAsync(firstRender: true)` rehydrates state from
`localStorage` once on first render and calls `StateHasChanged`.

### `FilterConfig` shape

`FilterConfig` is a flat DTO with property-per-filter-input, mirroring
`DecisionFilterSet` for HTTP transport. It is *not* a Razor type —
hosting it here is provisional. See umbrella Deferred for a possible
move to `XgFilter_Lib` (or `BgDataTypes_Lib`) once the consumer is
updated.

### Enum labels

Display strings come from `XgFilter_Lib.Enums.EnumLabel.ToLabel<TEnum>()`,
which reads `[Description]` attributes on each enum value. The label
contract lives with the enum, not with the UI.

### Test project

bUnit + xUnit, targets .NET 10. `BunitContext` with
`JSInterop.Mode = JSRuntimeMode.Loose` so `OnAfterRenderAsync`'s
`localStorage.getItem` calls return `default` (treated as "no persisted
state").

## Public API

`FilterPanel` component, namespace `XgFilter_Razor.Components`.

**Parameters (all `EventCallback`):**

- `EventCallback<DecisionFilterSet> OnFiltersChanged` — raised on Apply
  / Reset with the constructed filter set for in-memory filtering.
- `EventCallback<FilterConfig> OnFilterConfigChanged` — raised on
  Apply / Reset with the JSON-serializable mirror DTO.
- `EventCallback OnFilterDirty` — raised on every input change so the
  parent can disable downstream actions until Apply is clicked.

`FilterConfig` DTO, namespace `XgFilter_Razor.Shared`. Public mutable
properties: `Players`, `DecisionType` (string), `MatchScores`,
`ErrorMin`/`ErrorMax`, `MoveNumberMin`/`MoveNumberMax`, `PositionTypes`,
`PlayTypes`. Designed for `System.Text.Json` round-trip.

## Pitfalls

- **Two preserved encapsulation leaks — tracked-as-bugs.** This component
  is a verbatim relocation from
  `ExtractFromXgToCsv.Client/Components/FilterPanel.razor`; the umbrella
  Deferred list tracks two known issues that were *intentionally not
  fixed* in the relocation commit:
  - `Components/FilterPanel.razor:116` — `<label … for="pt_@pt">@pt</label>`
    renders the bare `PositionType` identifier instead of
    `@pt.ToLabel()`. Consistent with the play-type label one section
    below would use `@pt.ToLabel()`.
  - `Components/FilterPanel.razor:288-294` — local `DecisionTypeLabel`
    switch duplicates what `EnumLabel.ToLabel<DecisionTypeOption>()`
    returns from each member's `[Description]` attribute. The switch
    should be deleted and replaced with `@opt.ToLabel()` at the call
    site (`Components/FilterPanel.razor:45`).
  Both leaks are the same shape: the component reaching past the enum
  label contract that already lives in `XgFilter_Lib.Enums.EnumLabel`.
  Fix lands in a follow-up single-session cleanup; preserve until then
  to keep the relocation diff reviewable as pure relocation.
- **`IJSRuntime` / localStorage coupling.** `FilterPanel` depends on
  `Microsoft.JSInterop.IJSRuntime` and assumes the host provides a
  browser-style `localStorage` global (Blazor WebAssembly, Blazor
  Server, MAUI Blazor Hybrid all qualify). A non-Blazor host or a
  rendering harness without JS interop will see exceptions on the
  `localStorage.getItem` / `localStorage.setItem` calls. Tests in this
  subproject paper over this with `JSInterop.Mode = JSRuntimeMode.Loose`
  on `BunitContext`. Real-host consumers must register `IJSRuntime` in
  DI (Blazor's defaults do).
- **`FilterConfig` is *not* a Razor type.** It's a JSON-serializable
  DTO that happens to live in this Razor library because that's where
  the only producer (`FilterPanel`) lives today. Don't add Razor- or
  Blazor-specific concerns to it; it must remain server-and-client
  neutral so it can deserialize on the server side too.
- **Apply, not on-change.** The component does not raise filter-change
  events as the user types — only `OnFilterDirty`. The contract is
  "user thinks, then commits via Apply." Don't wire a downstream
  consumer to assume `OnFiltersChanged` fires per keystroke.
- **`ProcessRequest` and `OutputFormat` were intentionally left
  behind.** The source `Shared/FilterConfig.cs` in
  `ExtractFromXgToCsv.Client` also contains `ProcessRequest` (which
  references `OutputFormat`). Both are host-app-specific (CSV / PPTX
  output plumbing) and stay with the host. If a future consumer needs
  to wrap a `FilterConfig` plus output options, define that wrapper in
  the consumer, not here.

## Subproject-internal next steps

- **Land the encapsulation-leak fix.** Single-session cleanup: replace
  the `@pt` bare-identifier label with `@pt.ToLabel()` and delete the
  `DecisionTypeLabel` switch (use `@opt.ToLabel()` at the call site).
  Tracked separately on the umbrella Deferred list — this entry is the
  subproject-side reminder.
- **Add a `FilterPanel.razor.cs` code-behind partial.** The `@code`
  block has grown to ~140 lines and would be more navigable as a
  separate `.cs` file mirroring `BgDiag_Razor`'s
  `BackgammonDiagram.razor.cs` pattern. Pure refactor; no behaviour
  change.
- **Migrate `localStorage` calls behind a `Persistence` abstraction.**
  Once a non-WASM consumer (or a unit-test harness wanting real
  state-rehydration coverage) appears, factor the `localStorage.getItem`
  / `setItem` block into an injected `IFilterStateStore` so the
  component is host-agnostic. Until a second consumer exists, this is
  speculative and YAGNI applies.
