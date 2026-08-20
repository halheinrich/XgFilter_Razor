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
  Analysis-depth facet: the three selectable `AnalysisMode` members label the
  mode toggles and `AnalysisLevel`'s members are each mode's level checkboxes
  (labels via `EnumLabel.ToLabel`). Owned there, not in `XgFilter_Lib.Enums`, because the
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
    FilterSurface.razor              — THE consumer surface: panels + wiring
    FilterPanel.razor                — filter form (.Internal — not consumer surface)
    SavedFiltersPanel.razor          — saved-filter pick list (.Internal — not consumer surface)
    FilterHelp.razor                 — producer-owned facet + storage documentation
  Model/
    AppliedFilter.cs                 — applied-config holder, keyed to its source
    FilterRestoreNotice.cs           — restored-selection notice state, app-scoped
    FilterSourceToken.cs             — opaque host-minted source identity
    IFilterDocumentStorage.cs        — host storage-adapter seam
    FilterStorageException.cs        — the seam's one failure type
    SavedFiltersDocument.cs          — canonical/legacy file names + migration rule
    SavedFiltersStatus.cs            — saved-filters context condition
    SavedFiltersStore.cs             — saved-filters document lifecycle over the seam
  wwwroot/
XgFilter_Razor.Testing/
  XgFilter_Razor.Testing.csproj
  FilterPanelTestState.cs            — stored-selection seeding for host test suites
XgFilter_Razor.Tests/
  XgFilter_Razor.Tests.csproj
  AppliedFilterTests.cs              — holder source-keyed applied contract
  FakeFilterDocumentStorage.cs       — shared recording fake over the seam
  FilterPanelTests.cs                — bUnit tests for FilterPanel
  FilterPanelTestStateTests.cs       — the seeding seam, pinned against a real render
  FilterSourceTokenTests.cs          — token equality rules
  FilterSurfaceTests.cs              — bUnit wire tests for the composite
  SavedFiltersPanelTests.cs          — bUnit tests for SavedFiltersPanel
  SavedFiltersStoreTests.cs          — store transitions over a fake storage seam
  FilterHelpTests.cs                 — bUnit tests for FilterHelp
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

### `FilterSurface` component — the consumer surface

The one component hosts embed (umbrella arc #63/#78 Step 2): it owns
`FilterPanel` + `SavedFiltersPanel` and the interaction wiring end to end —
load→stage, save/save-as→snapshot-or-refuse, delete, applied-state
mediation onto the host's `AppliedFilter` holder, the saved-filters degrade
notices, and the source-change rule. Hosts bind the holder (host-registered,
at whatever lifetime their start-gate must survive — a *parameter* by
necessity, since the composite dies with its page while BgQuiz's gates must
survive navigation), the `FilterRestoreNotice` (host-registered, app-scoped
— the restored-selection notice's state; see the panel section and
Pitfalls), a `FilterSourceToken?` for the current source, an
`IFilterDocumentStorage?` adapter (null = no saved-filters context), the
host's `CanPersist` capability ruling with its host-specific
`PersistDisabledReason` wording, and the two panel-shaped events
(`OnFilterConfigChanged` / `OnAppliedStateChanged`), re-raised after
mediation with the panel's exact names, payloads, and per-gesture contract.

**The source-change rule is composite-owned — "told, never asks."** The
composite never sees pickers, paths, or capabilities; the host mints tokens
and the composite only compares them. When the bound token changes, the
setup ends: the holder is cleared (the applied state drops entirely — no
residue survives), the panel forget-commits (Apply re-arms; the host is told
through the normal event path), the save-refusal notice clears, and the
saved-filters context reloads through the seam — or resets, on a change to
null. **The first parameters-set initializes the comparison token and loads
the context — nothing else** (ruled pin): a remount over an unchanged
source leaves an already-applied holder untouched and the host's gate
armed, which is the holder's whole purpose.

**The first-mount reconcile** (#82) is the other half of that survival.
The panel's committed config dies each mount while the holder does not, so
a remount over an already-filtered source would restore the applied
selection as merely *staged* and re-arm Apply with nothing to do. At its
first render the composite seeds the panel's committed config from the
holder — `SeedCommitted`, the mirror of the source-change rule's
`ForgetCommitted` — via the keyed lookup `ConfigFor(Source)`, which yields
a config exactly when one is applied *and* it belongs to the current
`Source`. It runs from
`OnAfterRenderAsync(firstRender: true)`, not the first parameters-set,
because `@ref` is null until after the first render. The seed is
**silent** (no `OnAppliedStateChanged`) and comes **from the holder, never
from `localStorage`** — both non-negotiable; see Pitfalls for the lock-out
that storage-seeding produces and why the asymmetry with `ForgetCommitted`
is deliberate.

The composite owns its `SavedFiltersStore` over the bound adapter (rebuilt
on an adapter reference change), so a remount re-reads the document — a
setup-time, degrade-tolerant read. Notice copy is producer-owned so every
host degrades with identical wording: the save-refusal copy (field-agnostic
by design — it names no rule, because the offending value is already
marked, with its own explanation, in the panel below), the LoadFailed notice (which replaces the panel and names the
*actual* failed file via `SavedFiltersStore.LoadFailedFileName` — canonical
or legacy), and the WriteFailed notice (beside the still-truthful panel,
promising **page-lifetime retention only** — the composite-owned store dies
with the page, so "kept for this session" would over-promise; ruled pin).
Saved-section visibility: Ready shows the panel unless read-only *and*
empty (nothing to load, nothing to save — BgQuiz's clutter rule, now
producer-owned); WriteFailed keeps the panel beside its notice; Disabled
and LoadFailed render none. No `RenderFragment` slots — verified against
both hosts: neither interleaves anything between the composite's children.

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
analysis-depth control renders the facet's **three per-mode pairs**: one
toggle checkbox per selectable `AnalysisMode` (labelled from the enum's
`[Description]`s; `Unknown` gets no toggle), each disclosing its own
"Analysis level" group while checked — one checkbox per `AnalysisLevel` in
`Enum.GetValues` declaration order. Each level group is an honest disclosure
(real button, `aria-expanded` / `aria-controls`), collapsed by default and
deliberately **unpersisted** (see Pitfalls); while collapsed it carries a
count badge — "any" with no level checked, "N selected" otherwise — the
hidden-active-signal ruling one tier down. The panel binds the six raw-intent
members (`IncludeEvaluations`+`EvaluationLevels`, `IncludeRollouts`+
`RolloutLevels`, `IncludeBookRollouts`+`BookRolloutLevels`) verbatim and
**never** derives the clause union — that SSOT is `FilterConfig.Build()`
(see Pitfalls).
Position type and play type are shelved for later reintroduction — their UI
groups have been hidden since the FilterPanel hide pass, while the
`XgFilter_Lib` machinery behind them (`FilterConfig.PositionTypes` /
`PlayTypes`, the filters, the enums) stays intact. State is held in private
fields on the component instance.

**Validity is the lib's ruling; the panel marks it and words it.** Two
rules compose into one `IsCommittable` member — the position-pattern text
must parse (`BoardPattern.TryParse`, this panel's own field) and
`FilterConfig.GetInvalidFields()` must name no field (non-negative error
bounds, `min ≤ max`, `NaN` rejected — the lib's rule, asked through the
same `BuildConfig()` path Apply commits through, so what the panel reds and
what `Build()` would throw on are one answer). The error-range inputs style
themselves independently off `FilterField.ErrorMin` / `ErrorMax`
membership, so the lib's attribution rules carry straight to the screen: a
negative Max never reds a Min the user got right, while a misordered pair
blames both and leaves the user to pick an end. The message is the panel's
alone — the lib returns no strings by design — and covers both violations
in one line, worded to stay true for the literal `NaN` that
`double.TryParse` accepts and the lib rejects. Rendered only while it
applies (the `#applyDisabledReason` idiom) rather than left in the DOM for
Bootstrap's sibling selector, which cannot reach the inputs one level down
inside the flex row. A stored selection whose bound a rule outlaws still
loads, shows its values, marks the offender, and is refused a commit —
never silently repaired, never dropped (the lib's documented posture,
pinned).

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
filters**) — not on every keystroke. On Apply, the component:

1. Builds a `XgFilter_Lib.Filtering.FilterConfig` from the edit buffers
   and records it as the **last-committed config**.
2. Persists the whole selection to `localStorage` via `IJSRuntime`.
3. Raises `OnFilterConfigChanged` with that config.
4. Raises `OnAppliedStateChanged` with it too — the buffers now equal it.

**Clear filters** (the old Reset, renamed to say what it does) is the
full-clear gesture: it hydrates every edit buffer back to defaults, then
persists + raises the empty config, which consumers treat as applied. It
touches filter values only — no host state (the panel has no path to
any; the raised config is its only channel) and no disclosure movement.
It runs the same commit path as Apply, so it moves the last-committed
config too.

**Cleanliness is derived, never latched.** The panel is the only party
holding both the live edit buffers and the config it last committed, so
it — not the consumer — owns the answer to "is this selection still the
applied one?". That answer is one computed member: the committed config
the buffers currently equal (`FilterConfig`'s value equality), or `null`
when they equal none. Two surfaces consume that one computation and can
therefore never disagree:

- **The Apply gate.** Apply is offered only when the selection differs
  from the last-committed config *and* the selection is **committable** —
  the position-pattern text parses *and* `FilterConfig.GetInvalidFields()`
  names no field. `ApplyAsync` guards on the same condition it renders
  `disabled` from, so programmatic dispatch cannot re-commit either.
  While Apply is disabled *because nothing changed*, the panel says so —
  a `title` plus a muted hint line, the `SavedFiltersPanel`
  disabled-reason idiom, except that here the panel knows its own reason
  rather than being told it by the host. Neither invalid-value case gets a
  hint line: the offending field's own `invalid-feedback` already explains
  it.
- **`OnAppliedStateChanged`**, raised after every buffer-affecting
  gesture — a control edit, `LoadConfig` staging, Apply, Clear filters —
  carrying that same value. Toggling either disclosure tier is
  navigation, not an edit, and raises nothing.

Deriving from equality rather than latching a dirty flag is what makes
an edit-then-undo recoverable: typing a change and typing it back lands
on the committed values, so the panel goes clean again and re-reports.
A one-way flag would leave the consumer's gate stuck with no recovery
gesture, because Apply — the only control that could clear it — is
itself disabled on an unchanged selection.

The last-committed config is plain component-instance state that dies on
unmount, and is deliberately **never persisted**: the first-render
`localStorage` restore *stages* a selection, it does not commit one, so a
fresh panel has committed nothing, raises neither event, and starts with
Apply enabled. A host that remounts the panel (BgQuiz on a new folder
pick) therefore gets a re-enabled Apply for free, with no host-side reset
call.

Two `internal` methods move that reference point programmatically, and
they are exact mirrors:

- `void ForgetCommitted()` — for a panel kept mounted across a source
  change: it drops the last-committed config (buffers, persisted state,
  and disclosure all untouched) so Apply re-arms and
  `OnAppliedStateChanged` re-reports (necessarily `null`) through the
  normal path.
- `void SeedCommitted(FilterConfig)` — for a fresh mount resuming an
  earlier mount's commit: it adopts the given config as last-committed, so
  Apply does not re-arm over a selection that is already applied. Same
  untouched buffers, same nothing written. **Silent**, unlike its mirror:
  forgetting is news the consumer can only hear through the event, while a
  seed derives from applied state the caller already holds. That
  asymmetry is contract, not oversight.

Both are internal by design — the composite is their only intended caller
(see Pitfalls). Because of `SeedCommitted`, "a fresh mount starts with
Apply enabled" is the *panel's* posture in isolation; under the composite
a remount over an already-filtered source starts with Apply disabled, the
mount having reconciled from the holder.

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

**The restored-selection notice (§4's legibility law).** A reload ends the
setup: the config restore stages the previous session's selection with
nothing applied and Apply re-armed — correct by rule, and exactly what a
defect would look like, so the panel says what happened
(`#filterRestoredNotice`: restored from a previous session, not in effect
until Apply). The state behind it is the app-scoped `FilterRestoreNotice`
(host-registered, forwarded through the composite): the panel *arms* it
when the first-render restore genuinely parses a stored config (nothing
stored or unreadable = nothing restored = no claim; an *empty* stored
config does arm it — the empty filter is still a choice), and *dismisses*
it at the first gesture that makes the selection the user's own — any
buffer-affecting gesture (edit, `LoadConfig` staging, Clear filters) or a
commit. Dismissal is one-way for the app lifetime, which is what makes a
remount within a setup quiet after an edit while an untouched remount
re-shows the same notice (navigation changes nothing, in both directions).
Disclosure toggles are navigation and keep it; `ForgetCommitted` is
choreography and keeps it (see Pitfalls).

### `SavedFiltersPanel` component

A persistence-agnostic pick list over `XgFilter_Lib`'s
`NamedFilterCollection`. The panel owns no document state and mutates
nothing: every gesture is raised as a request — `OnLoadRequested`,
`OnSaveRequested` (per-row Save, #38), `OnSaveAsRequested`,
`OnDeleteRequested`, each carrying the name — for
the host to mediate. The host calls `With` / `Without`, persists wherever
it persists, and passes the **new** collection instance back down through
`Filters`; the reference change is also the panel's confirmation channel
(it cancels pending inline confirms and clears the typed save-as name).
Selection is deliberately stateless — the "current" config lives in
`FilterPanel`'s edit buffers, so a highlighted row would be a second
source of truth that lies. Every destructive gesture runs through an
inline confirm in the panel — a row's Save, a save-as under an existing
name, and delete; `Contains` keeps the case-insensitive name rule in
the lib. A row's Save overwrites that saved filter with the current
filters — the same live-edit-buffers snapshot save-as takes, the name
coming from the row instead of the input — and its confirm copy says so
("Overwrite '\<name\>' with the current filters?"), deliberately
distinguishable from the save-as overwrite prompt ("Overwrite
'\<name\>'?"). A row holds one confirm slot: requesting Save supersedes a
pending Delete confirm and vice versa. Hosts that cannot persist right
now (e.g. BgQuiz without its FS-Access grant) disable Save/Delete via
`CanPersist` + `PersistDisabledReason`; Load stays enabled — it is
read-only over a collection already in memory. The typical wiring:
`OnLoadRequested` → resolve via `TryGetConfig` → `FilterPanel.LoadConfig`;
`OnSaveRequested` / `OnSaveAsRequested` → `FilterPanel.TryGetEditedConfig`
→ `With` → persist.

### `FilterHelp` component

Producer-owned documentation for everything the panel offers — every
facet, and the chrome that governs them all — in user-level language,
behavior only, never `FilterConfig` internals or field names. It lives
beside the panel it documents so the prose has one owner: consumers embed
this component and add only app-level framing (BgQuiz's Help does exactly
that); they must never write their own facet or chrome prose — a second
description of the panel's semantics is a second encoding that silently
drifts. Render-only: it issues no JS interop and touches no storage or
state of its own — it *documents* what `FilterPanel` persists without
participating in it. Structured for embedding: one
`<section>` per topic, each heading carrying a stable `fh-*` anchor id —
rendered from a named constant on the component, never a re-typed
literal — facet heading text from the lib's `FilterFacet`
`[Description]`s via `ToLabel()`, so help titles, panel section headings,
and the hidden-active signal all name a facet identically. The depth section
explains the union semantics (each checked mode admits its decisions;
more checked = more matched; nothing checked = facet off), the
inner-level distinction per mode, and the per-mode **Analysis level**
disclosure: what its `any` / `N selected` badge says without opening it,
and that levels under an unchecked mode are kept but inert. The shelved
facets (Position types / Play types) are deliberately undocumented until
their UI returns.

The chrome section, **Setting and applying filters**
(`fh-using-the-panel`), sits before the facets — it is the frame a reader
needs in order to find and commit any of them. It documents the
disclosure and its hidden-active signal (a filter set earlier is never
quietly out of sight), Apply as the only commit and both of its disabled
states (nothing changed, which the panel says under the button; and a
value that is not usable as a filter, which the offending box marks and
explains where it was typed), and Clear filters as the one-gesture return
to the unfiltered set that leaves the disclosure alone. The
reject-and-explain posture is documented as behavior — *nothing is
guessed at or quietly ignored* — while each rule stays with its facet:
the error facet's own section carries the non-negative / ordered-bounds
rule and why an impossible range is refused rather than applied.

**Heading depth is the host's to state**, via the required `HeadingLevel`
parameter: the lead heading renders at that level and every section one
below, so the block contributes a well-formed two-tier outline wherever it
lands. Only the host knows the outline it is embedding into — see Pitfalls
for why the parameter is `[EditorRequired]` rather than defaulted, and
for the migration it forces. The `fh-*` anchor ids are unaffected by the
level (pinned).

A final non-facet section, **What the panel remembers**
(`StorageSectionAnchorId`), is the storage-assurance copy: it states in
user terms that the panel saves its settings in the reader's own browser
on their own machine and uploads nothing, and it names both
`localStorage` entries — the applied config and the disclosure
preference — so a reader can verify them in devtools. Both key names are
**rendered from `FilterPanel`'s own constants** (`ConfigKey` /
`DisclosureKey`, `internal` for exactly this), never written as prose
literals, so the copy cannot drift from what the panel actually writes.
Scope is exactly what `FilterPanel` persists: a sibling `xg_*` key
belonging to a host app is that host's to document. A host with its own
data-ownership copy points *into* this section rather than restating it
(BgQuiz's Help does that) — the same one-owner rule as the facet prose.
That link is a code contract, not a prose one: the section's id and its
heading text are the component's two `public` constants, and the heading
renders from the same pair the host links with (see Host surface).

### Non-visual interaction model (`Model/`)

Plain C# beside the components — namespace `XgFilter_Razor` (root), while
components stay in `XgFilter_Razor.Components`. Hoisted from BgQuiz's
app-side originals (its `AppliedFilter` / `SavedFiltersStore`) so both
consumer apps share one encoding of the filter interaction lifecycle
(umbrella arc #63 / #78 / #38); hosts register these at whatever lifetime
their gates must survive (BgQuiz: Scoped), and `FilterSurface` drives them.

- **`AppliedFilter`** — holder for the config the user deliberately
  applied, **keyed to the source it was applied against**: one nullable
  (config, source) pair with one lifetime, read only through the
  source-relative lookup `ConfigFor(token)` — there is deliberately no
  bare "what is applied?" accessor, so a config applied against some
  other source can never read as applied (spec §3: the need is
  ownership, not history; nothing anywhere answers "has this source ever
  been filtered"). Edit-coupled: the panel reporting uncommitted edits
  must `Clear()` it, re-gating Start-like actions, and `Clear()` drops
  the pair entirely — no residue survives. In-memory only, never
  persisted.
- **`FilterRestoreNotice`** — app-scoped state for the restored-selection
  notice (§4): armed by the panel's first-render restore of a stored
  selection, dismissed one-way at the first buffer-affecting gesture or
  commit. The *instance lifetime* is the trigger fact: a reload constructs
  a fresh one, a remount within a setup reuses the boot's — which is what
  the mount-time condition alone cannot distinguish (see Pitfalls). Hosts
  register and bind it, nothing more: the movers (`Arm`/`Dismiss`) and the
  read (`IsVisible`) are producer-internal. In-memory only, never
  persisted.
- **`FilterSourceToken`** — opaque, equatable identity of "which source",
  minted by the host via `FromGeneration(int)` / `FromPath(string)` and
  only ever *compared* by the producer. Value equality over the wrapped
  string, so each factory owns identity in its domain by owning what it
  wraps: `FromPath` **normalizes the path itself** — upper-invariant
  case-fold, trailing `\` / `/` insignificant — so a host passes the
  spelling it holds and cannot mint a token that misses its own previous
  visit's. The trim is hand-rolled rather than
  `Path.TrimEndingDirectorySeparator` because this runs in WebAssembly,
  where .NET's Unix path semantics do not recognize a backslash at all;
  it also trims a root's separator, which is harmless for an identity
  string nothing reconstitutes into a path. `FromGeneration` wraps its
  counter as-is. Factory domains are prefixed, so tokens from different
  factories never collide. "No source yet" is `FilterSourceToken?`.
- **`SavedFiltersStore`** + **`SavedFiltersStatus`** — the saved-filters
  document lifecycle over the host's storage adapter: `LoadAsync` /
  `SaveAsync` / `DeleteAsync` / `Reset` moving through Disabled / Ready /
  LoadFailed / WriteFailed. Degrade, never block: no member throws for
  storage trouble; LoadFailed preserves the file untouched and keeps
  saving dead; WriteFailed keeps the in-memory edit and stops further
  writes. Round-trip is the document's own
  (`NamedFilterCollection.ToJson` / `TryFromJson`) — the store owns no
  serializer options. A null adapter = permanently Disabled, so an
  adapterless host composes with the same store. An internal load-version
  guard (the producer edition of BgQuiz's `PickGeneration` discipline)
  makes a superseded in-flight load discard its outcome. Deliberately
  hardcoded to the saved-filters document — the queued mix-saves sibling
  is a new store over its own identity (umbrella ruling).
- **`IFilterDocumentStorage`** + **`FilterStorageException`** — the host
  seam: per-document text I/O keyed by file name (`ReadAsync(name)`
  returning null for absent, `WriteAsync(name, json)`), generalized by
  document name so the queued sibling needs zero interface change.
  Adapters wrap every native failure in `FilterStorageException` — the
  one type the store catches (see Pitfalls).
- **`SavedFiltersDocument`** — the document identity: public constants
  `FileName` (`xg-filters.json`) and `LegacyFileName`
  (`bgquiz-filters.json`), plus the stated two-name migration rule the
  store implements — read canonical first, fall back to legacy only when
  canonical is *absent*, write only canonical, never delete the legacy
  file (see Pitfalls for the corrupt-file rationale).

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

### Test-support assembly (`XgFilter_Razor.Testing`)

Producer-owned arrangement helpers for **host** test suites, referenced by
their test projects only — `IsPackable=false`, and no app-graph project
may reference it. Today it holds one member:
`FilterPanelTestState.SeedStoredSelection(BunitJSInterop, FilterConfig)`,
which arranges "a previous visit left a stored selection behind".

Why it exists: the panel's `localStorage` keys are deliberately not
consumer surface, but a host test arranging navigate-back or reload
genuinely needs that state, and the only way to get it was to repeat the
key as a literal in the host's suite. Keeping the constant `internal`
never prevented that dependency — it only made it untyped, so a
producer-side rename would leave the literal behind and the host's test
would keep passing for the wrong reason, arranging nothing and asserting
the "no stored selection" path. The fact therefore moves to where it can
be kept true: hosts state intent, the producer supplies mechanism, and
the key never leaves this repo. This repo's own
`FilterPanelTestStateTests` uses the seam exactly as a host does — never
naming a key — so a rename that missed the seeder fails here.

The bUnit dependency is first-class, not incidental: the thing being
seeded *is* a bUnit JSInterop fake, and any seam avoiding the reference
would have to hand the key back to the caller — the coupling the assembly
exists to remove.

## Public API

The consumer surface is `FilterSurface` + `FilterHelp` (namespace
`XgFilter_Razor.Components`) and the non-visual model types (root
`XgFilter_Razor` namespace). `FilterPanel` and `SavedFiltersPanel` live in
`XgFilter_Razor.Components.Internal` with `[EditorBrowsable(Never)]` and
are **not consumer surface** — consuming them from a host is banned
outright, host tests included (see Pitfalls for the narrowing record).
Their contracts below remain documented because `FilterSurface` builds on
them and this repo's tests pin them.

### `FilterSurface`

Parameters:

- `AppliedFilter AppliedFilter` `[EditorRequired]` — the host's holder
  instance, mediated by the composite: commits `Set` it keyed to
  `Source`, uncommitted-edit reports `Clear` it, clean re-affirms re-`Set`
  it. Hosts read their gates from the holder (`ConfigFor` against their
  own current token) and re-render off the events below.
- `FilterRestoreNotice RestoreNotice` `[EditorRequired]` — the host's
  app-scoped notice-state instance, forwarded to the inner panel, which
  owns arming and dismissal. Hosts register and bind it only — its members
  are producer-internal, so no host can move or read it.
- `FilterSourceToken? Source` — the current source's token; null = none
  (applies are not recorded). Changing it triggers the composite-owned
  source-change rule; the first parameters-set only initializes and loads.
- `IFilterDocumentStorage? Storage` — the saved-filters seam; null = no
  saved-filters section at all. The composite owns the store over it.
- `bool CanPersist` (default true) + `string? PersistDisabledReason` — the
  host's capability half of the persist gate and its wording; ANDed with
  the store's `Ready` before reaching the panel. The reason is forwarded
  only while the host's half is false (WriteFailed explains itself with
  its own notice).
- `EventCallback<FilterConfig> OnFilterConfigChanged` +
  `EventCallback<FilterConfig?> OnAppliedStateChanged`, both
  `[EditorRequired]` — the inner panel's events re-raised after mediation,
  with identical names, payloads, and contracts (per-gesture, stateless,
  idempotent — see the `FilterPanel` section below and Pitfalls).

### `FilterPanel` (`.Internal` — via `FilterSurface` only)

Two `EventCallback` parameters, both `[EditorRequired]`:

- `EventCallback<FilterConfig> OnFilterConfigChanged` — raised on Apply /
  Clear filters with the configured
  `XgFilter_Lib.Filtering.FilterConfig`. Consumers that want a
  `DecisionFilterSet` call `cfg.Build()`; consumers that want to ship the
  configuration over the wire serialize `cfg` with `System.Text.Json`.
- `EventCallback<FilterConfig?> OnAppliedStateChanged` — raised after
  every gesture that touches the edit buffers (control edit, `LoadConfig`
  staging, Apply, Clear filters), carrying **the committed config the
  buffers now equal, or `null` when they equal none**. A consumer gating
  a downstream action on "is the panel's selection still the one I acted
  on?" compares the payload against the config it last received from
  `OnFilterConfigChanged`; `null` always means uncommitted edits are
  pending. Not raised by either disclosure toggle (navigation, not an
  edit), and not raised at all by the first-render restore. **Per-gesture,
  not transition-only** — see Pitfalls for why, and handle it statelessly
  and idempotently.

Two host-facing methods, typically reached via `@ref` (added in the
saved-filters arc):

- `void LoadConfig(FilterConfig)` — stages a config into the edit
  buffers as a bulk edit: no Apply-side effects (no persist, no
  `OnFilterConfigChanged`, no move of the last-committed config),
  `OnAppliedStateChanged` fires once — `null` normally, or the committed
  config when the load stages exactly it — and the first-render
  localStorage restore is suppressed so a host-startup load can't be
  clobbered. Never moves the disclosure.
- `bool TryGetEditedConfig(out FilterConfig?)` — snapshots the live
  buffers (including unapplied edits) for host-driven save-as. Gate is
  exactly Apply's validity gate (`IsCommittable`): fails on non-blank,
  unparseable position-pattern text and on any field
  `FilterConfig.GetInvalidFields()` names. No stricter either — match-score
  tokens ride raw through both paths — so a saved document is never minted
  from a selection Apply would itself have refused.

Two further methods, `ForgetCommitted()` and `SeedCommitted(FilterConfig)`,
are deliberately `internal` — `FilterSurface` is their only intended
caller (its source-change rule and its first-mount reconcile
respectively), so neither is host-facing surface (see Architecture and
Pitfalls).

### `SavedFiltersPanel` (`.Internal` — via `FilterSurface` only)

Parameters (all callbacks `[EditorRequired]`, as is `Filters`):

- `NamedFilterCollection Filters` — the immutable document to render;
  the host passes each new instance back down after mediating a change.
- `EventCallback<string> OnLoadRequested` / `OnSaveRequested` /
  `OnSaveAsRequested` / `OnDeleteRequested` — request-only gestures
  carrying the filter name; the panel mutates nothing. `OnSaveRequested`
  is the per-row Save (#38): overwrite that saved filter with the current
  live edit buffers — the host mediates it exactly as save-as
  (`TryGetEditedConfig` → `With` → persist), the name coming from the
  row.
- `bool CanPersist` (default `true`) + `string? PersistDisabledReason` —
  gate Save/Save-as/Delete as one switch when the host cannot persist;
  Load stays enabled.

### Non-visual model types

- `AppliedFilter` — `FilterConfig? ConfigFor(FilterSourceToken)`,
  `void Set(FilterConfig, FilterSourceToken)`, `void Clear()`. The
  surface is source-relative only — no bare `Config` / `IsApplied` — and
  `Clear()` drops the applied state entirely.
- `FilterRestoreNotice` — public type, deliberately opaque to hosts: a
  public parameterless ctor for DI registration and nothing else callable
  from outside the producer (`Arm` / `Dismiss` / `IsVisible` are
  `internal`; tests reach them via `InternalsVisibleTo`). A host's whole
  contract is register at app scope, bind to `FilterSurface`.
- `FilterSourceToken` — `readonly record struct`; factories
  `FromGeneration(int)` / `FromPath(string)`; value-equal, with
  `FromPath` normalizing path identity itself (case, trailing separator).
- `SavedFiltersStore` — ctor `(IFilterDocumentStorage? storage)`;
  `NamedFilterCollection Filters`, `SavedFiltersStatus Status`,
  `string? LoadFailedFileName` (non-null exactly while `LoadFailed`,
  naming the actual file — canonical or legacy — the failed load was
  about, so degrade copy never guesses), `Task LoadAsync()`,
  `Task SaveAsync(string, FilterConfig)`, `Task DeleteAsync(string)`,
  `void Reset()`. Never throws for storage trouble; mutating members
  no-op unless `Status == Ready`.
- `IFilterDocumentStorage` — `Task<string?> ReadAsync(string fileName)`
  (null = absent), `Task WriteAsync(string fileName, string json)`;
  failures signalled as `FilterStorageException` only.
- `SavedFiltersDocument` — `const string FileName = "xg-filters.json"`,
  `const string LegacyFileName = "bgquiz-filters.json"`; public by
  design (see Pitfalls).

### `FilterHelp`

One parameter, no callbacks, no host-facing methods — embed it where the
host's help lives and add app-level framing around it.

- `int HeadingLevel` `[EditorRequired]` — the level of the block's lead
  heading; every section renders one below. Valid 1–5 (a lead at `h6`
  would leave its sections nowhere to go); anything else, including the
  unset default of zero, throws `ArgumentOutOfRangeException` at
  parameters-set. Required because only the host knows the outline it is
  embedding into — see Pitfalls.

- `const string StorageSectionAnchorId` / `const string
  StorageSectionHeading` — the storage-assurance section's anchor id and
  heading text: the deep-link surface a host's data-ownership copy points
  at, composing its own sentence around them rather than spelling either
  as a literal. The heading renders from the same pair, so the link and
  what it lands on cannot drift. Stable across `HeadingLevel`; renamed
  only as a deliberate breaking change. The members' own docs carry the
  rest — including why the other eleven `fh-*` ids are constants too but
  `internal`.

`FilterPanel`'s two `localStorage` key constants are `internal`, not
public: the copy naming them lives here, in the producer, so a consumer
never sees or depends on this panel's key names. Test-only
`InternalsVisibleTo("XgFilter_Razor.Tests")` lets the wiring test pin
the rendered names to those constants, and
`InternalsVisibleTo("XgFilter_Razor.Testing")` lets the test-support
assembly seed the config key on a host suite's behalf — both grants are
producer-side, so neither widens what consumers can see.

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
  events as the user types — only `OnAppliedStateChanged`. The contract
  is "user thinks, then commits via Apply." Don't wire a downstream
  consumer to assume `OnFilterConfigChanged` fires per keystroke.
- **`OnAppliedStateChanged` is per-gesture, not transition-only — don't
  "optimize" it.** The obvious-looking cleanup is to fire only when the
  reported value changes. It is wrong, and the failure is silent. The
  last-committed config is component-instance state that dies on unmount,
  so a freshly mounted panel has committed nothing; the user's first edit
  is not a transition from anything *this* panel knows about, and a
  transition-only event would say nothing. Meanwhile a consumer whose own
  applied state survived the navigation (BgQuiz's `AppliedFilter`) is
  still holding a config from the previous mount and gating on it — a
  stale "clean" belief that only this event can correct. Silent in exactly
  the state where the consumer is most wrong. Consumers must therefore
  handle it statelessly and idempotently: assign from the payload, never
  diff it against a remembered previous one.
- **Cleanliness is derived from equality, never latched.** The Apply gate
  and the event payload both read one computed member — the committed
  config the buffers currently equal. Don't add a `_isDirty` flag beside
  it: a one-way flag never clears on edit-then-undo, and since Apply is
  itself disabled on an unchanged selection, the consumer's gate would be
  stranded with no recovery gesture. (That wedge is why the old
  payload-less `OnFilterDirty` had to go: both consumers used re-clicking
  Apply as their implicit recovery, which an equality-derived gate
  removes.) Equally, don't compare in two places — the gate and the
  payload disagreeing is the defect the single member exists to prevent.
- **The last-committed config is never persisted.** The first-render
  `localStorage` restore *stages* a selection; it does not commit one. So
  a fresh panel raises neither event and would start with Apply enabled
  even with every control populated — which is what makes "a new folder
  re-enables Apply" fall out of a host remount for free. Persisting it, or
  hoisting it into a holder, would break both properties at once. The
  composite's first-mount reconcile narrows *when* that re-arm is offered
  without touching either property: it seeds the panel in memory from the
  applied holder, writing nothing (next entry).
- **The depth facet's clause union is derived in `Build()`, not the panel.**
  The Analysis-depth control writes only raw intent — three per-mode pairs,
  each a toggle plus its own checked-level set — and calls
  `ReportAppliedState()`. The mapping to
  `AnalysisDepthFilter` clauses (one clause per enabled toggle
  carrying its own level list, empty list = any level, all toggles off =
  facet off, inert level lists) lives **only** in `FilterConfig.Build()` — it
  is the single source of truth, and XgFilter_Lib's Pitfalls flag re-encoding
  it in a consumer as a silent-drift hazard. The panel must not pre-compute
  clauses or a mode list; it binds the six members verbatim and lets
  `Build()` own the semantics.
- **Level-group disclosure state is deliberately unpersisted.** The
  panel-level disclosure persists under `xg_moreFiltersExpanded` because its
  collapsed state hides *which* sections hold what — the hidden-active signal
  compresses that to a count-plus-names summary, and the user's chosen
  layout is worth remembering across sessions. A level group's collapsed
  state hides only one thing — which levels are checked — and its badge
  ("any" / "N selected") already carries that in full, so remembering the
  open state would buy no information at the cost of three more
  localStorage keys and their interop. Each group therefore mounts
  collapsed, and toggling it is navigation: no `OnAppliedStateChanged`, no
  write.
- **Unchecking a mode keeps its checked levels.** The buffer (and the
  emitted config) retain a group's level selections when its toggle goes
  off: the lib guarantees a level list whose toggle is off is inert — no
  activation, no constraint, no validation — so re-toggling the mode
  restores the user's selection instead of punishing an exploratory
  untoggle. Only Clear filters (or a hydrating restore/load) resets the
  level lists.
- **Single callback by design.** `FilterConfig.Build()` is the canonical
  `FilterConfig` → `DecisionFilterSet` adapter; a parallel callback
  raising `DecisionFilterSet` would be a redundant encapsulation leak.
  Consumers needing a `DecisionFilterSet` call `cfg.Build()` themselves.
- **Razor silent-splat, and where it does and doesn't bite here.** Razor
  doesn't error or warn at *build* time on an unrecognized component
  attribute — it emits it like any other, so a consumer retaining a stale
  binding for a removed `EventCallback` parameter compiles clean while its
  now-dead handler still looks wired. Where it lands after that depends on
  the component. `FilterPanel` declares no
  `[Parameter(CaptureUnmatchedValues = true)]` catch-all, so the renderer
  rejects the unmatched attribute on the first render —
  `InvalidOperationException: Object of type '…FilterPanel' does not have
  a property matching the name '…'` (pinned by
  `StaleParameterBinding_ThrowsAtRender`). Loud, but only once the page
  actually renders: a consumer's *build* stays green, which is exactly why
  a producer-side parameter removal needs each consumer adapted in its own
  leg before any umbrella pointer bump. Never add a `CaptureUnmatchedValues`
  catch-all to this panel — it would convert that render-time exception
  back into the silent splat the whole discipline exists to avoid.
  For the opposite failure — a binding *missing* rather than stale —
  `[EditorRequired]` yields `RZ2012` at build. Every `FilterPanel` callback
  carries it, including `OnAppliedStateChanged`, whose predecessor
  `OnFilterDirty` was deliberately optional. It is not optional now: it is
  the only channel telling a consumer its applied state went stale, and an
  unbound one fails silently at runtime as a gate that never re-opens.
  Neither attribute nor exception proves the wiring is *right*, so
  supplement both with bUnit integration tests that fire Apply and assert
  the consumer's downstream state actually flips.
- **`FilterHelp.HeadingLevel` is `[EditorRequired]` and breaks host
  builds on purpose** — the `OnSaveRequested` precedent, for the same
  reason. A default would be a level the component cannot know is right:
  the hard-coded `h4`/`h5` pair it replaced was right for nobody in
  particular, and the only host in tree (BgQuiz's Help, whose sections are
  `h2`) had been skipping a level under it the whole time. That is the
  failure mode a default preserves — invisible on screen, visible only in
  a screen reader's outline or an audit, and silently wrong again in the
  next host. `RZ2012` at build makes each host state its own level in its
  own migration leg instead; the 1–5 range check is the belt for the paths
  `RZ2012` cannot see (reflection, dynamic rendering, a test harness), and
  it refuses rather than clamps — silently emitting an `h0` would defeat
  the point of making the level explicit. Sections are always lead + 1:
  don't add a second parameter for them, and don't let a host set them
  independently. The `fh-*` anchor ids never move with the level (pinned)
  — hosts may already link to them, and `StorageSectionAnchorId` says so
  in the type system.
- **Panel documentation has one owner: `FilterHelp`.** Consumers embed
  the component and add app-level framing only — a consumer that writes
  its own description of a facet's semantics creates a second encoding
  of lib behavior that silently drifts when the lib's rules change
  (exactly the depth-facet redesign scenario). If a host needs prose
  `FilterHelp` lacks, the fix is to extend `FilterHelp` here, not to
  write it host-side. That rule is why the storage-assurance copy for
  the panel's own keys is producer-owned too — a host states its own
  data ownership and points into the storage section for the panel's
  half, linking with `FilterHelp.StorageSectionAnchorId` rather than a
  literal. **It covers the chrome as well as the facets**: the disclosure
  and its hidden-active signal, Apply's two disabled states, Clear
  filters. Those are the panel's behavior, not the app's, so a host
  describing them is the same drift hazard one tier up — app-level
  framing means *where the panel sits in this app and what pressing Apply
  unlocks here*, never what the controls do.
- **The storage keys are a documented surface now — `internal`, and no
  wider.** `ConfigKey` / `DisclosureKey` on `FilterPanel` are `internal`
  solely so `FilterHelp` can render the names it tells users to look for
  in devtools from the one constant. Two consequences. (1) Renaming a
  key is a user-facing copy change as well as a storage-format change:
  the name in the help text follows automatically, but a reader's
  existing entry silently stops being found, so treat a rename as a
  migration question, not a refactor. (2) They must never become
  `public`. A consumer that can see this panel's key names will
  eventually hardcode one, and the point of siting the copy here is that
  no consumer needs to know them. The test project reaches them through
  test-only `InternalsVisibleTo` in the csproj — that grant is the whole
  intended audience.
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
- **The saved-filters file names are `public` — deliberately opposite to
  the internal storage-key rule.** `FilterPanel.ConfigKey` /
  `DisclosureKey` stay `internal` because no consumer may know or depend
  on this panel's localStorage keys. `SavedFiltersDocument.FileName` /
  `LegacyFileName` are the opposite kind of fact: the shared file name is
  user-facing copy every host must render — help pages, the composite's
  degrade notices — so one public source is the SSOT move, and each host
  renders the constant rather than spelling its own copy of the name.
  Don't "tidy" them internal (it would force each host to hardcode the
  name), and don't widen the storage keys public by the same argument in
  reverse.
- **The two-name migration rule: corrupt does NOT fall back** (ratified
  ruling, Step-1 review). The store reads `xg-filters.json` first and
  falls back to `bgquiz-filters.json` only when the canonical file is
  *absent* — never when it is present but unparseable. Falling back on
  corrupt would resurrect stale legacy data while newer-but-corrupt data
  exists; instead `LoadFailed` both reports the trouble and keeps every
  write dead, so the corrupt file can never be overwritten
  (preserve-file-on-corrupt is enforced by the store's status gate, not
  by any host's `CanPersist` courtesy). Writes go only to the canonical
  name, and the legacy file is never deleted — it stays as the user's own
  backup, going stale from the first canonical write onward.
- **Storage adapters must wrap failures in `FilterStorageException`.**
  The store's degrade-never-block posture rides on a *typed* catch: an
  adapter that lets its native failure type escape (`JSException`,
  `IOException`, an HTTP exception) will fault the host's flow instead of
  degrading to `LoadFailed` / `WriteFailed`. Wrap everything that means
  "the I/O failed"; let everything that means "the adapter has a bug"
  propagate. An absent document is `null` from `ReadAsync`, never an
  exception.
- **Never restate a lib validity rule in the panel — ask it.** The error
  bounds' rule (non-negative, `min ≤ max`, `NaN` rejected) lives in
  `XgFilter_Lib` and is asked through `FilterConfig.GetInvalidFields()` on
  the same `BuildConfig()` output Apply commits. A local
  `if (min < 0)` here would be a second encoding of a rule `Build()` also
  enforces, and the two would drift the day the lib's rule moves — the
  depth-facet scenario again, one tier down. The same applies to
  attribution: which *field* to mark is the lib's answer (`FilterField`
  membership), not a facet-wide red. What the panel does own is the
  wording: `GetInvalidFields` deliberately returns no message strings, the
  same division of labour as `BoardPattern.TryParse`.
- **`TryGetEditedConfig` is Apply's validity gate, and must stay exactly
  that.** Both directions matter. Stricter, and save-as refuses a
  selection the user could apply; looser, and a saved document is minted
  from a selection Apply refuses — a permanent trap, since loading it
  reproduces the invalid state with Apply disabled. When a validity rule is
  added, it goes into the one `IsCommittable` member both read; the
  composite's refusal copy stays field-agnostic for the same reason (the
  offending field is already marked, with its own explanation, in the
  panel).
- **Per-row Save snapshots the live edit buffers — exactly as Save-as
  does.** Both save gestures capture what `TryGetEditedConfig` hands
  over, unapplied edits included; a row Save differs only in taking its
  name from the row. Don't "fix" it to save the last-committed config —
  saving what the user sees staged is the contract, and the confirm copy
  ("…with the current filters") says so.
- **`OnSaveRequested` is `[EditorRequired]` and breaks host builds on
  purpose.** Both consumers compile-fail (`RZ2012`) until their own
  migration legs bind the per-row Save — the deliberate alternative to a
  silently splatted, dead affordance (see the Razor silent-splat entry
  above).
- **`ForgetCommitted` and `SeedCommitted` stay `internal`.**
  `FilterSurface` is their only intended caller — its source-change rule
  and its first-mount reconcile; a host either remounts the panel (getting
  the re-arm for free) or hosts the composite, which owns both rules. And
  only the composite mediates the applied holder, so only it can say what
  a fresh panel was committed to. Widening either public would hand hosts a
  second, uncoordinated way to move the committed state that the
  applied-state events were designed around.
- **The panels' narrowing is `.Internal` + `EditorBrowsable(Never)` — the
  strongest the toolchain allows, and the ban is absolute anyway.** The
  spike (Step 2, ruled): a true `internal` component draws CS0262 — the
  Razor generator hardcodes `public partial` on the component class, so a
  user partial cannot narrow it. The ruled fallback is what stands:
  `FilterPanel` / `SavedFiltersPanel` live in
  `XgFilter_Razor.Components.Internal` with `[EditorBrowsable(Never)]`,
  and consuming them from a host is banned outright — **including host
  tests: no `FindComponent<FilterPanel>()` carve-out.** Host wire tests
  drive `FilterSurface`'s rendered DOM with real gestures instead (both
  hosts' existing tests do reach the panel types today; their migration
  legs carry that rewrite). If the Razor toolchain ever allows internal
  components, finish the job then. `FilterHelp` stays public — it is
  consumer surface.
- **`FilterSurface` is told, never asks — keep it that way.** It has no
  parameter or interop path to pickers, paths, folder handles, or
  capabilities: the host mints `FilterSourceToken`s and rules `CanPersist`;
  the composite only compares tokens and ANDs the ruling with its store's
  status. Adding any host-domain knowledge (a path parameter "just for the
  notice", a capability enum) re-couples what the seam exists to decouple.
  The same boundary governs copy: degrade-notice and refusal wording is
  producer-owned here so every host degrades identically; only
  host-specific *reasons* (FS-Access phrasing) arrive as parameters. A
  host writing its own copy for these states is the facet-prose drift
  hazard again.
- **`FilterSurface`'s first parameters-set is initialization, not a source
  change** (ruled pin). It sets the comparison token and loads the
  saved-filters context — no holder clear, no forget-commit, no notice
  choreography. A remount over an unchanged source (navigate-back) must
  leave an already-applied holder untouched and the host's gate armed —
  that survival is the holder's documented purpose, and an end-setup on
  mount would silently revoke it. The end-setup choreography runs only on
  an actual token change against the initialized value; pinned by
  `Mount_OverSameSource_LeavesAppliedHolderUntouched_RaisesNothing`.
  **The host-side half of that pin: publish `null` for `Source` only when
  you mean *no source exists* — never as *not yet known*.** The composite
  only compares tokens; it cannot tell a placeholder apart from an answer.
  A `null` that is later corrected to the real token is therefore read as a
  genuine source change and ends the setup — holder cleared, commitment
  forgotten, Apply re-armed — which is exactly wrong when nothing about the
  source actually changed. A host whose source is derived from facts it
  restores asynchronously must withhold the composite until that derivation
  is settled rather than mount it against a placeholder;
  `ExtractFromXgToCsv`'s `_restoreComplete` gate (#85) is the consumer-side
  statement of this rule.
- **The first-mount reconcile seeds from the holder — NEVER from
  `localStorage`** (ruled, #82). Apply is offered only when there is
  something to do: a filter change or a source change. A remount over an
  already-filtered source has neither, so the composite seeds the fresh
  panel's committed config from `AppliedFilter` at its first render. The
  tempting shortcut — seed from the restored `localStorage` selection,
  which the panel already has in hand — is a **lock-out**. That blob
  survives a full browser reload; the holder deliberately does not. After a
  reload, storage-seeding would disable Apply while nothing is applied:
  the host's start gate closed and the one control that could re-open it
  greyed out. Pinned from the other side by
  `Mount_EmptyHolder_WithRestorableStorage_LeavesApplyEnabled`, which is
  exactly the test that fails if anyone ever makes that swap.
  Three more properties are load-bearing:
  - **It runs from `OnAfterRenderAsync(firstRender: true)`, not the first
    parameters-set.** `@ref` is null until after the first render, so
    copying `EndSetup`'s `_filterPanel?.` idiom into the parameters-set
    branch compiles, reads correctly, and silently does nothing.
  - **It is silent** — no `OnAppliedStateChanged`. `ForgetCommitted`
    reports because it creates news; a reconcile derives from the holder,
    which already agrees, so there is none. A raise would also break the
    mount pin above and its named test.
  - **It asks the holder source-relatively — `ConfigFor(Source)` — the
    only way the holder answers.** "Something is applied" and "it was
    applied to *this* source" are one question under the keyed surface: a
    config applied against another source reads as nothing applied, and
    the seed correctly declines.
  Ordering against the panel's own `localStorage` restore is safe but not
  accidental: the child's after-render runs first and parks on its interop
  await, so the seed can land before the buffers hydrate. It converges only
  because cleanliness is an equality comparison re-evaluated by the
  restore's `StateHasChanged`, not a latched flag — one more reason that
  comparison must never become a flag.
  **Reachability note** (re-checked after #85, and the reason the keyed
  lookup's decline path is defence in depth rather than the load-bearing
  part): neither host can present a fresh mount with a holder keyed to a
  *different* source — but each for its own host-side reason, and in
  neither case because `Source` is null. BgQuiz gates the composite behind
  `HasFiles`, so every source change crosses an unmount, and
  `EndCurrentSetupAsync` — which runs at the pick click, not after it —
  clears the holder first. ExtractFromXgToCsv clears the mismatched holder
  itself: since #85 its first-render restore calls `AppliedFilter.Clear()`
  whenever the holder carries nothing keyed to the source it just restored,
  and that restore completes *before* the page lets the composite mount
  (`_restoreComplete`). So the holder the composite meets on its first
  render is already either this source's or empty. Note what this does *not* rest on, since it is the
  tempting re-derivation: Extract's `Source` is truthful at that first
  render rather than a placeholder — non-null whenever a source was in
  fact restored — so over an unchanged folder the reconcile genuinely
  *fires* there, opening with Apply disabled and the "already applied"
  notice. That is the reconcile seeding the fresh panel, not a non-null
  guard declining. (A restored blank path is the other truthful answer:
  `Source` is null, the reconcile declines, and the host's restore has
  already cleared the holder on the same condition.)
  Re-check this if either host's mount-time source derivation changes.
- **A host that gates the composite behind source-existence never fires
  the in-place source-change rule — and still owes one line of end-setup
  choreography** (proven in BgQuiz's migration). When `FilterSurface`
  renders inside an `@if` tied to "a source is held" (BgQuiz's `HasFiles`
  gate), every source change crosses an unmount: the composite is disposed
  before it can observe the new token, and the fresh mount's first
  parameters-set deliberately only initializes the token and loads the
  saved-filters context (the navigate-back pin above). Remount therefore
  delivers Apply re-arm, context re-read and notice death for free — but it
  cannot clear a host-registered `AppliedFilter` holder that outlives the
  page, so such a host must keep an `AppliedFilter.Clear()` at its
  setup-ending gesture (BgQuiz's `EndCurrentSetupAsync` is the precedent).
  Since #82 that `Clear()` also carries the re-arm: the fresh mount
  reconciles from the holder, so a holder left applied would keep Apply
  disabled for the *new* source. Both hosts clear a mismatched holder
  before any fresh mount can see it — at different moments, by different
  mechanisms — which is why the reconcile cannot adopt a stale config; see
  the reachability note in the reconcile entry above, and the two gate
  shapes below.
  Two gate shapes exist and only one of them is this bullet's subject.
  BgQuiz's is *ongoing*: the `@if` tracks source existence for the page's
  whole life, so every source change crosses an unmount. Extract's
  `_restoreComplete` is *one-shot at page mount*: it withholds the
  composite until the restore has settled `Source`, then mounts it and
  leaves it mounted. `Source` going null afterwards — the user blanks the
  folder path — unmounts nothing, so Extract remains an always-mounted host
  for every rule in #78 and the in-place source-change rule still owns its
  source changes. Do not read the one-shot gate as buying remount-for-free.
  Nor does it excuse the `Clear()`: a DI-scoped holder outlives the page in
  either host, and nothing else drops a config keyed to a source this
  visit no longer has, so Extract owes the same line and since #85 carries
  it inside the restore. Read the pair as gated-ongoing plus `Clear()` at
  the setup-ending gesture (BgQuiz), or always-mounted plus `Clear()` in
  the mount-time restore (Extract) — neither host gets to skip it. Leave
  the source-change rule wired in gated hosts regardless: it is harmless
  defence in depth if render timing ever changes.
- **The restored-selection notice cannot be derived at mount time — its
  app-scoped instance IS the trigger.** The tempting condition — "a stored
  selection was restored AND nothing is applied for the current source" —
  is also true of a navigate-back with unapplied edits: same setup, the
  panel remounts, restores, and finds the holder empty (the edit cleared
  it). §1 rules that navigation changes nothing, including no new notice,
  so that condition misfires exactly where the user is mid-work. The
  distinguishing fact is app-boot identity, and it is carried by lifetime,
  not by a recorded flag: a reload constructs a fresh
  `FilterRestoreNotice`, a remount reuses the boot's already-dismissed
  one. Three properties are load-bearing:
  - **Dismissal is one-way per app lifetime and rides on user gestures
    only** — buffer edits, `LoadConfig` staging, and commits dismiss;
    disclosure toggles (navigation) and `ForgetCommitted` (source-change
    choreography) do not. The `ForgetCommitted` half matters for host
    symmetry: a gated host's source change crosses an unmount and runs no
    panel code, so if the in-place rule dismissed, the notice's fate would
    differ by host mechanics. Hence the panel's
    `ReportAppliedState` (gesture: dismiss + raise) /
    `RaiseAppliedState` (raw raise) split — route new callers
    deliberately.
  - **This is not a history fact.** §3 bans behaviour from depending on
    what has ever happened; this records a *pending* present-tense state
    (this boot's restored selection, not yet the user's own), moves only
    toward death within a boot, resets solely by instance death, and
    gates nothing but its own copy. No behaviour may branch on it.
  - **The composite's forwarding is load-bearing.** `FilterPanel`'s
    parameter defaults to a fresh panel-scoped instance so the panel is
    coherent bare (tests); only the forwarded app-scoped instance makes
    remounts quiet after an edit. Dropping the forwarding is silent at
    compile time and pinned by
    `Remount_WithinSetup_AfterAnEdit_DoesNotResurrectTheNotice`.
- **The WriteFailed copy promises page-lifetime retention only.** The
  composite-owned store lives and dies with the page, so a failed edit
  does not survive navigation — "kept for this session" would over-promise
  (ruled). If the store's lifetime ever changes, the copy is part of that
  change.

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
