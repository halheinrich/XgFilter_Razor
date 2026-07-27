# XgFilter_Razor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)

## Stack

C# / .NET 10 / Razor class library (`Microsoft.NET.Sdk.Razor`) / bUnit.
Visual Studio 2026 on Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\XgFilter_Razor\XgFilter_Razor.slnx`

## Repo

https://github.com/halheinrich/XgFilter_Razor — branch `main`.

## Depends on

- **XgFilter_Lib** — `FilterConfig` (with `Build()` factory yielding
  `DecisionFilterSet`), `DecisionFilterSet` itself, the enums
  (`DecisionTypeOption`, `PositionType`, `PlayType`), and the
  `EnumLabel.ToLabel<TEnum>()` extension. Project reference, not a
  package.
- **BgDataTypes_Lib** — `AnalysisMode` and `AnalysisLevel`, the two-axis
  taxonomy that replaced the retired flat `AnalysisDepthClass` and drives the
  Analysis-depth facet: `AnalysisLevel`'s members are the level checkboxes and
  `AnalysisMode.Rollout` / `.BookRollout` label the two mode toggles (labels via
  `EnumLabel.ToLabel`). Owned there, not in `XgFilter_Lib.Enums`, because the
  producer (`ConvertXgToJson_Lib`) stamps both axes. Beyond
  that, consumers of `DecisionFilterSet` typically work against
  `IDecisionFilterData`, so the dependency is conceptually direct as well. The
  precedent in `ExtractFromXgToCsv.Client.csproj` is to list every such
  dependency explicitly. Also supplies `DiceRoll` (canonical-unordered roll
  value type) and its `DiceRoll.All` — the 21 distinct rolls in ascending
  canonical order — which drive the dice-roll facet's checkbox grid; the type
  owns both the set and its order, so the panel enumerates `All` and never
  builds a local roll list.

## Directory tree

```
XgFilter_Razor.slnx
Directory.Packages.props
XgFilter_Razor/
  XgFilter_Razor.csproj
  _Imports.razor
  Components/
    FilterPanel.razor                — markup + @code state
    SavedFiltersPanel.razor          — saved-filter pick list, host-mediated
  wwwroot/
XgFilter_Razor.Tests/
  XgFilter_Razor.Tests.csproj
  FilterPanelTests.cs                — bUnit tests for FilterPanel
  SavedFiltersPanelTests.cs          — bUnit tests for SavedFiltersPanel
```

## Architecture

### Thin Razor wrapper

Parallel to `BgDiag_Razor`'s relationship with `BackgammonDiagram_Lib`:
this subproject lets `XgFilter_Lib` stay free of any Blazor / Razor
dependency. All filter logic, classification, the `FilterConfig` DTO,
the `NamedFilterCollection` document, facet activation
(`GetActiveFacets`), and enum labels live in the core lib; this project
only binds those primitives into Blazor components and surfaces the
resulting `FilterConfig` via an `EventCallback`.

### `FilterPanel` component

`FilterPanel` owns the entire filter-form UI as a Bootstrap card with
controls for player names, decision type, match scores, error range, move
number range, contact type, analysis depth, dice rolls, and a position
pattern. The dice-roll control is a checkbox grid enumerated from
`BgDataTypes_Lib.DiceRoll.All` — the lib owns both the 21-roll set and its
ascending canonical order, so the panel imposes no local roll list or sort.
Each checkbox's label is the roll's own canonical token (`DiceRoll.ToString`
→ `"31"`), rendered verbatim rather than hyphenated, keeping display text owned
by the type as the enum facets defer to `ToLabel`. Selections are stored raw in
`FilterConfig.DiceRolls` (empty = facet off); the panel derives nothing —
`Build()` owns materialization into a `DiceRollFilter`, the same SSOT posture as
the depth facet (see Pitfalls). The
analysis-depth control is a **two-axis** facet: one checkbox per
`AnalysisLevel` (rendered in `Enum.GetValues` declaration order) for the level
axis, plus two rule-separated toggle checkboxes for the mode axis, bound to
`FilterConfig.IncludeRollouts` / `IncludeBookRollouts` and labelled from
`AnalysisMode.Rollout` / `.BookRollout`'s `[Description]`s. The panel binds
these three raw-intent members and **never** derives the effective
`AnalysisMode` set — that SSOT is `FilterConfig.Build()` (see Pitfalls).
Position type and play type are shelved for later reintroduction — their UI groups have
been hidden since `ddb9c98`, while the `XgFilter_Lib` machinery behind them
(`FilterConfig.PositionTypes` / `PlayTypes`, the filters, the enums) stays
intact. State is held in private fields on the component instance.

**Information hierarchy** (dogfooding-driven): the error-range section is
first and always visible — it is the panel's most-used control. The other
eight sections (player names, decision type, match scores, move number
range, contact type, analysis depth, dice rolls, position pattern) sit
behind a single disclosure, default hidden. The disclosure is an honest
control — a real `<button>` carrying `aria-expanded` / `aria-controls`
over an always-rendered `#moreFilters` region whose children render only
while expanded (absent from the DOM when collapsed, not styled away).
The expand/collapse choice is the user's, persisted under its own
localStorage key (`xg_moreFiltersExpanded`, values `"true"`/`"false"`;
anything else restores to the default-hidden posture) — never inside the
config blob, and never moved by `LoadConfig` or Clear filters.

**Hidden-active signal**: while collapsed, the toggle carries a count
badge plus the names of any hidden sections holding active filters. It is
computed from the live edit buffers through the same build path Apply
uses — `BuildConfig().GetActiveFacets()` minus `FilterFacet.ErrorRange`
(the always-visible facet) — never by re-inspecting config fields. The
facet labels are the section headings by the lib's design (`FilterFacet`
`[Description]`s via `ToLabel()`), so the signal names exactly the
sections the user will find on expanding. Feeding from the live buffers
makes it honest at rest after restore, Apply, Clear filters, and
`LoadConfig` staging — and live mid-edit: it tracks staged values the
moment they are typed, not on Apply.

The component emits filter results only on **Apply** (or **Clear
filters**) — not on every keystroke. On any input change (typing, radio
selection, checkbox toggle), `OnFilterDirty` fires so the parent can
disable a Run button until Apply is clicked. Toggling the disclosure is
navigation, not an edit — it never fires `OnFilterDirty`. On Apply, the
component:

1. Persists the whole selection to `localStorage` via `IJSRuntime`.
2. Builds a `XgFilter_Lib.Filtering.FilterConfig` and raises
   `OnFilterConfigChanged`.

**Clear filters** (the old Reset, renamed to say what it does) is the
full-clear gesture: it hydrates every edit buffer back to defaults, then
persists + raises the empty config, which consumers treat as applied. It
touches filter values only — no host state (the panel has no path to
any; the raised config is its only channel) and no disclosure movement.

Consumers that want a `DecisionFilterSet` for in-memory filtering call
`cfg.Build()` themselves; consumers that want to POST the configuration
to a server send `cfg` as JSON. Single callback by design — see Pitfalls
for the encapsulation rationale.

`OnAfterRenderAsync(firstRender: true)` rehydrates both localStorage keys
once on first render — the disclosure choice, then the config — and calls
`StateHasChanged`. Each restore double-checks its guard
(`_disclosureTouched` / `_externalConfigLoaded`) after its await, so a
user toggle or a host `LoadConfig` landing mid-interop is never
clobbered.

### `SavedFiltersPanel` component

A persistence-agnostic pick list over `XgFilter_Lib`'s
`NamedFilterCollection`. The panel owns no document state and mutates
nothing: every gesture is raised as a request — `OnLoadRequested`,
`OnSaveAsRequested`, `OnDeleteRequested`, each carrying the name — for
the host to mediate. The host calls `With` / `Without`, persists wherever
it persists, and passes the **new** collection instance back down through
`Filters`; the reference change is also the panel's confirmation channel
(it cancels pending inline confirms and clears the typed save-as name).
Selection is deliberately stateless — the "current" config lives in
`FilterPanel`'s edit buffers, so a highlighted row would be a second
source of truth that lies. Overwrite and delete run through inline
confirms in the panel; `Contains` keeps the case-insensitive name rule in
the lib. Hosts that cannot persist right now (e.g. BgQuiz without its
FS-Access grant) disable Save/Delete via `CanPersist` +
`PersistDisabledReason`; Load stays enabled — it is read-only over a
collection already in memory. The typical wiring: `OnLoadRequested` →
resolve via `TryGetConfig` → `FilterPanel.LoadConfig`; `OnSaveAsRequested`
→ `FilterPanel.TryGetEditedConfig` → `With` → persist.

### `FilterConfig` provenance

`FilterConfig` lives in `XgFilter_Lib.Filtering`, not here. It is a
JSON-round-trippable DTO whose `Build()` materializes a
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

Both components live in namespace `XgFilter_Razor.Components`.

### `FilterPanel`

Two `EventCallback` parameters:

- `EventCallback<FilterConfig> OnFilterConfigChanged` (`[EditorRequired]`)
  — raised on Apply / Clear filters with the configured
  `XgFilter_Lib.Filtering.FilterConfig`. Consumers that want a
  `DecisionFilterSet` call `cfg.Build()`; consumers that want to ship the
  configuration over the wire serialize `cfg` with `System.Text.Json`.
- `EventCallback OnFilterDirty` — raised on every input change so the
  parent can disable downstream actions until Apply is clicked. Not
  raised by the disclosure toggle (navigation, not an edit).

Two host-facing methods, typically reached via `@ref` (added in the
saved-filters arc):

- `void LoadConfig(FilterConfig)` — stages a config into the edit
  buffers as a bulk edit: no Apply-side effects (no persist, no
  `OnFilterConfigChanged`), `OnFilterDirty` fires once, and the
  first-render localStorage restore is suppressed so a host-startup load
  can't be clobbered. Never moves the disclosure.
- `bool TryGetEditedConfig(out FilterConfig?)` — snapshots the live
  buffers (including unapplied edits) for host-driven save-as. Gate is
  exactly Apply's: fails only on non-blank, unparseable position-pattern
  text.

### `SavedFiltersPanel`

Parameters (all callbacks `[EditorRequired]`, as is `Filters`):

- `NamedFilterCollection Filters` — the immutable document to render;
  the host passes each new instance back down after mediating a change.
- `EventCallback<string> OnLoadRequested` / `OnSaveAsRequested` /
  `OnDeleteRequested` — request-only gestures carrying the filter name;
  the panel mutates nothing.
- `bool CanPersist` (default `true`) + `string? PersistDisabledReason` —
  gate Save/Delete as one switch when the host cannot persist; Load
  stays enabled.

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
- **The depth facet's mode set is derived in `Build()`, not the panel.**
  The Analysis-depth control writes only raw intent — a checked-level set
  (`AnalysisLevels`) plus the two independent toggles (`IncludeRollouts`,
  `IncludeBookRollouts`) — and calls `MarkDirty()`. The mapping from those
  toggles to the effective `AnalysisMode` set (Rollouts→`Rollout`, Book
  rollouts→`BookRollout`, neither→`Evaluation`, plus the "no level checked and
  neither toggle = facet off" rule) lives **only** in `FilterConfig.Build()` —
  it is the single source of truth, and XgFilter_Lib's Pitfalls flag
  re-encoding it in a consumer as a silent-drift hazard. The panel must not
  pre-compute a mode list; it binds the three members verbatim and lets
  `Build()` own the semantics.
- **Single callback by design.** `FilterConfig.Build()` is the canonical
  `FilterConfig` → `DecisionFilterSet` adapter; a parallel callback
  raising `DecisionFilterSet` would be a redundant encapsulation leak.
  Consumers needing a `DecisionFilterSet` call `cfg.Build()` themselves.
- **Razor silent-splat on stale bindings.** Razor doesn't error or warn
  on unrecognized component attributes — it silently splats them as
  HTML. A consumer that retains a stale binding for a removed
  `EventCallback` parameter compiles clean and renders, but the dead
  handler keeps referencing its now-unused C# method while the new
  wiring never fires. Defense: `[EditorRequired]` on required
  `EventCallback` parameters catches missing-binding (yields `RZ2012`)
  but not stale-binding; supplement with bUnit integration tests that
  fire Apply and assert the consumer's downstream state actually flips.
- **Host-app-specific wrappers stay with the host.** A consumer that
  needs to wrap `FilterConfig` with output-format options (CSV / PPTX
  selection, output paths, etc.) defines that wrapper in the consumer,
  not here. `FilterConfig` is purely the filter selection.
- **Disclosure state never goes into `xg_filter_config`.** The panel
  persists under two keys with different owners: `xg_filter_config` is
  the wire-traveling `FilterConfig` DTO whose JSON shape the lib owns,
  and `xg_moreFiltersExpanded` is UI preference owned by this panel.
  Folding visibility into the config blob would make a saved or loaded
  filter drag the disclosure around — expanding is the user's gesture,
  never the config's.
- **The hidden-active signal is computed from `GetActiveFacets()`, never
  by re-inspecting config fields or edit buffers.** The activation
  predicates are the lib's SSOT (the `FacetRules` table behind both
  `Build()` and `GetActiveFacets()` — the `DecisionFilterSet.IsEmpty`
  ruling); the panel only excludes `ErrorRange` as the always-visible
  facet. The signal reads the live buffers via `BuildConfig()`, so it is
  honest for everything the panel can apply. The shelved facets
  (`PositionTypes` / `PlayTypes`) are outside that scope by pre-existing
  panel behavior — `HydrateFrom` / `BuildConfig` ignore them — so a stale
  `xg_filter_config` blob carrying them is not surfaced by the signal;
  since the panel is the only apply path, they also can never become
  active through it.
- **Clear filters touches filter values only.** It hydrates the buffers
  to defaults and persists + raises the empty config — nothing else. No
  host state (the panel has no parameter or interop path to any — e.g.
  BgQuiz's picked folder is out of reach by construction; keep it that
  way) and no disclosure movement. `LoadConfig` likewise stages values
  without moving the disclosure; visibility changes only on the user's
  toggle.

## Subproject-internal next steps

- **Add a `FilterPanel.razor.cs` code-behind partial.** The `@code`
  block runs over 100 lines and would be more navigable as a separate
  `.cs` file mirroring `BgDiag_Razor`'s `BackgammonDiagram.razor.cs`
  pattern. Pure refactor; no behavior change.
- **Migrate `localStorage` calls behind a `Persistence` abstraction.**
  Once a non-WASM consumer (or a unit-test harness wanting real
  state-rehydration coverage) appears, factor the `localStorage.getItem`
  / `setItem` block into an injected `IFilterStateStore` so the
  component is host-agnostic. Until a second consumer exists, this is
  speculative and YAGNI applies.
