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

- **XgFilter_Lib** — `FilterConfig` (the wire/UI-state DTO with `Build()`
  factory yielding a `DecisionFilterSet`), `DecisionFilterSet` itself,
  the enums (`DecisionTypeOption`, `PositionType`, `PlayType`), and the
  `EnumLabel.ToLabel<TEnum>()` extension. Project reference, not a
  package.
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
  wwwroot/
XgFilter_Razor.Tests/
  XgFilter_Razor.Tests.csproj
  FilterPanelTests.cs                — bUnit smoke tests
```

## Architecture

### Thin Razor wrapper, by design

Parallel to `BgDiag_Razor`'s relationship with `BackgammonDiagram_Lib`:
this subproject lets `XgFilter_Lib` stay free of any Blazor / Razor
dependency. All filter logic, classification, the `FilterConfig` DTO,
and enum labels live in the core lib; this project only binds those
primitives into a Blazor component and surfaces the resulting
`FilterConfig` via an `EventCallback`.

### Single component: `FilterPanel`

`FilterPanel` owns the entire filter-form UI as a Bootstrap card with
controls for player names, decision type, match scores, error range, move
number range, position type, and play type. State is held in private
fields on the component instance.

The component emits filter results only on **Apply** (or **Reset**) — not
on every keystroke. On any input change (typing, radio selection,
checkbox toggle), `OnFilterDirty` fires so the parent can disable a Run
button until Apply is clicked. On Apply, the component:

1. Persists each control value to `localStorage` via `IJSRuntime`.
2. Builds a `XgFilter_Lib.Filtering.FilterConfig` and raises
   `OnFilterConfigChanged`. Consumers that want a `DecisionFilterSet`
   for in-memory filtering call `cfg.Build()` themselves; consumers that
   want to POST the configuration to a server send `cfg` as JSON. The
   single-callback design replaced an earlier dual-callback shape — see
   the encapsulation note in Pitfalls.

`OnAfterRenderAsync(firstRender: true)` rehydrates state from
`localStorage` once on first render and calls `StateHasChanged`.

### `FilterConfig` provenance

`FilterConfig` lives in `XgFilter_Lib.Filtering`, not here. It is a
typed-enum, JSON-round-trippable DTO whose `Build()` materialises a
`DecisionFilterSet`. The Razor `FilterPanel` is purely a producer of
`FilterConfig` instances — it doesn't define the type.

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

- `EventCallback<FilterConfig> OnFilterConfigChanged` — raised on Apply
  / Reset with the configured `XgFilter_Lib.Filtering.FilterConfig`.
  Consumers that want a `DecisionFilterSet` call `cfg.Build()`; consumers
  that want to ship the configuration over the wire serialize `cfg`
  with `System.Text.Json`.
- `EventCallback OnFilterDirty` — raised on every input change so the
  parent can disable downstream actions until Apply is clicked.

## Pitfalls

- **`IJSRuntime` / localStorage coupling.** `FilterPanel` depends on
  `Microsoft.JSInterop.IJSRuntime` and assumes the host provides a
  browser-style `localStorage` global (Blazor WebAssembly, Blazor
  Server, MAUI Blazor Hybrid all qualify). A non-Blazor host or a
  rendering harness without JS interop will see exceptions on the
  `localStorage.getItem` / `localStorage.setItem` calls. Tests in this
  subproject paper over this with `JSInterop.Mode = JSRuntimeMode.Loose`
  on `BunitContext`. Real-host consumers must register `IJSRuntime` in
  DI (Blazor's defaults do).
- **JSON round-trip needs `JsonStringEnumConverter`.** Consumers that
  serialize `FilterConfig` for HTTP transport must register
  `JsonStringEnumConverter` (e.g. on `JsonSerializerOptions.Converters`
  or via `[JsonConverter]` attributes) so `DecisionType`, `PositionTypes`,
  and `PlayTypes` serialize as their string member names rather than
  underlying integer values. This is the lib's stated contract — see
  `FilterConfig`'s type-level remarks. The Razor side itself never
  serializes; it hands typed C# objects via `EventCallback`. The
  converter requirement applies to the consumer's HTTP plumbing.
- **Apply, not on-change.** The component does not raise filter-change
  events as the user types — only `OnFilterDirty`. The contract is
  "user thinks, then commits via Apply." Don't wire a downstream
  consumer to assume `OnFilterConfigChanged` fires per keystroke.
- **Single-callback API by design.** The component used to expose two
  callbacks (`OnFiltersChanged` raising a `DecisionFilterSet` plus
  `OnFilterConfigChanged` raising the DTO). Now that `FilterConfig.Build()`
  is the canonical adapter, the dual-callback shape was a redundant
  encapsulation leak. Consumers that need a `DecisionFilterSet` call
  `cfg.Build()` themselves — don't reintroduce a parallel callback.
- **Migration trap (post-design-arc).** The pre-arc API surface
  (`OnFiltersChanged EventCallback<DecisionFilterSet>`) is gone. A
  consumer migrating from the pre-arc shape MUST remove the old binding
  from its Razor markup, not merely add the new one alongside it. Razor
  does not error or warn on unrecognized component attributes — it
  silently splats them as additional HTML attributes — so a consumer
  that retains the stale `OnFiltersChanged="..."` binding compiles
  clean and renders, but the dead binding still references its handler
  in C#. The result: stale handler stays alive, downstream wiring
  (e.g. a Run button gated on the post-arc callback) silently breaks,
  and the migration appears successful in the UI until the user clicks
  through. `[EditorRequired]` on `OnFilterConfigChanged` (yields
  `RZ2012`) catches the *adjacent* bug class — consumer forgot to bind
  the callback at all — but not this one. **Recommended consumer-side
  defense:** bUnit integration tests that exercise the wire end-to-end
  (fire Apply on a hosted `<FilterPanel />`, assert the consumer's
  downstream state actually flips), not pure compile-time verification.
  This trap is not hypothetical — the prior consumer-adaptation session
  in `ExtractFromXgToCsv` hit it exactly, leaving a permanently-disabled
  Run button on the host's primary export action. Treat the precedent
  as load-bearing.
- **`ProcessRequest` and `OutputFormat` were intentionally left
  behind.** The original `Shared/FilterConfig.cs` in
  `ExtractFromXgToCsv.Client` also contained `ProcessRequest` (which
  references `OutputFormat`). Both are host-app-specific (CSV / PPTX
  output plumbing) and stay with the host. If a future consumer needs
  to wrap a `FilterConfig` plus output options, define that wrapper in
  the consumer, not here.

## Subproject-internal next steps

- **Add a `FilterPanel.razor.cs` code-behind partial.** The `@code`
  block runs over 100 lines and would be more navigable as a separate
  `.cs` file mirroring `BgDiag_Razor`'s `BackgammonDiagram.razor.cs`
  pattern. Pure refactor; no behaviour change.
- **Migrate `localStorage` calls behind a `Persistence` abstraction.**
  Once a non-WASM consumer (or a unit-test harness wanting real
  state-rehydration coverage) appears, factor the `localStorage.getItem`
  / `setItem` block into an injected `IFilterStateStore` so the
  component is host-agnostic. Until a second consumer exists, this is
  speculative and YAGNI applies.
