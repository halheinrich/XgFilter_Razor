# XgFilter_Razor

> Collaboration contract: [`../AGENTS.md`](../AGENTS.md)
> Umbrella status & dependency graph: [`../INSTRUCTIONS.md`](../INSTRUCTIONS.md)
> Mission & principles: [`../VISION.md`](../VISION.md)
> Open work: the [tracker](https://github.com/halheinrich/backgammon/issues), label `XgFilter_Razor`.

## What it is for

The filter surface the ecosystem's Blazor apps embed. A host registers it with
one call, mounts `FilterSurface`, tells the owner which source it holds, and
reads back whether a filter is in effect and which. Everything between is here:
the filter form, the saved-filters pick list, what is remembered in the
browser, the notices, and the help text that documents it all. BgQuiz_Blazor
and ExtractFromXgToCsv are its two hosts.

What a filter *means* is XgFilter_Lib's. What this repo owns is the user-facing
state the umbrella's `SPEC-filtering.md` rules: the draft on screen, what is
applied, what a reload restored.

Where to look:

- Embedding the surface in a host: Using the surface.
- A host's tests: Using the surface, then Test-support assembly.
- Changing what a setup does: `SPEC-filtering.md`, then The setup-state owner.
- Changing the form: `FilterPanel` component, then Pitfalls, Validity and the
  panel.
- Adding a notice: the umbrella's `SPEC-notices.md`, then Pitfalls, Notices.
- Keeping another named document: `NamedEntriesPanel` component, then Using the
  surface.

Signatures and per-member contracts are the XML docs'; `Directory.Build.props`
builds them on every build.

## Stack

C# / .NET 10 / Razor class library (`Microsoft.NET.Sdk.Razor`) / bUnit. Visual
Studio 2026 on Windows.

## Solution

`D:\Users\Hal\Documents\Visual Studio 2026\Projects\backgammon\XgFilter_Razor\XgFilter_Razor.slnx`

From this directory:

```bash
dotnet build XgFilter_Razor.slnx
```

```bash
dotnet test XgFilter_Razor.slnx
```

The suite is bUnit and reads no `TestData`. The solution carries its dependency
projects, so those sibling submodules must be checked out. Nothing here runs in
a real browser: there is no app. What only a browser shows (real keystrokes,
focus, the caret, whether pixels hold still) is checked through a host,
BgQuiz_Blazor's `FilterFieldEntryTests`. No screen reader has been run against
the notices (`halheinrich/backgammon#249`).

## Repo

https://github.com/halheinrich/XgFilter_Razor — branch `main`.

## Depends on

- **XgFilter_Lib** — what a filter is: `FilterConfig` and its `Build()`, the
  verdicts on it (`GetInvalidFields()`, `GetActiveFacets()`), the grammars
  (`MatchScoreToken`, `BoardPattern`), `NamedFilterCollection`, the enums and
  `EnumLabel.ToLabel<TEnum>()`.
- **BgDataTypes_Lib** — referenced directly because this project compiles
  against its types: `AnalysisMode` and `AnalysisLevel`, the two axes behind
  the Analysis-depth facet; `DiceRoll` and `DiceRoll.All`, which own the 21
  rolls and their order, so the panel enumerates `All` and never builds a roll
  list; `NamedCollection`, the base of every named document.
- **BgUiPrimitives_Razor** — `Notice`, the one owner of alert markup, and
  `BrowserStorage`, the one guarded browser-storage access; every box this
  member shows and every storage call it makes goes through them. Their rules
  are that library's (`INSTRUCTIONS.md`, "Who announces, and what the live
  region holds", "One holder per dismissal", "Browser storage access"). Its
  `BrowserStorageCondition` is the app's one record of a refusal, which
  `BrowserStorageConditionRefusalSink` (below) reports to. The test projects
  also reference its `BgUiPrimitives_Razor.TestSupport`, for
  `BrowserStoragePlan`, and `BgDataTypes_Lib.TestSupport`, for the recording
  and throwing loggers. The namespace is imported once, in `_Imports.razor`.

  **It passes an obligation through to hosts:** the component's scoped CSS
  reaches a host inside the host's own `{HostAssembly}.styles.css` bundle, also
  when the host references that library only through this one, so a host's root
  document must link that bundle. Without it a dismissible notice's text is not
  a click target (its close button and padding still dismiss). That library's
  Host obligations are the contract.

All three are project references, not packages.

## Layout

Three of this repo's projects under `XgFilter_Razor.slnx` (which also carries
the dependency projects), governed by repo-root `Directory.Build.props` (TFM,
`TreatWarningsAsErrors`, XML doc generation) and `Directory.Packages.props`
(Central Package Management).

**`XgFilter_Razor/`** — the Razor class library. It ships no static assets.

- **Components** — `Components/`: `FilterSurface`, the one consumer-facing
  filter component, mounting the panel and the saved-filters pick list and
  wiring them to the setup's owner; `FilterPanel`, the filter form it mounts, a
  view over the owner, in the `.Internal` namespace and banned from host use;
  `NamedEntriesPanel`, the generic pick list over any `NamedCollection`
  document, which a host may mount directly; `FilterHelp`, the producer-owned
  documentation of every facet, the panel chrome and what the surface persists.
- **Model** — `Model/`, the non-visual types, in the root namespace. The filter
  setup: `FilterSetup` (the app-scoped setup-state owner),
  `FilterSetupSnapshot` (what consumers see of it), `FilterRestoration` (this
  boot's restoration outcome), `FilterDraft` (the editor's state, internal),
  `FilterSourceToken` (the opaque host-minted source identity); the surface's
  storage — `FilterStorage` (the one seam over `BrowserStorage`, internal) and
  `IFilterStorageRefusalSink` (the host's owner of the refusal occurrence) with
  `BrowserStorageConditionRefusalSink` (its adapter over the shared
  `BrowserStorageCondition`); the named-document machinery — `IDocumentStorage`
  (the host storage seam) and `DocumentStorageException` (its one failure
  type), `NamedDocumentStore` (the document lifecycle over the seam) and
  `NamedDocumentStatus` (its condition), `NamedEntriesSurface` (a pick-list
  mount's copy and element ids); and the saved-filters specialization,
  `SavedFiltersDocument` (the canonical and legacy file names and the migration
  rule) and `SavedFiltersStore` (identity only).
- **Registration** — `FilterSurfaceServiceCollectionExtensions`:
  `AddFilterSurface()` or `AddFilterSurface<TRefusalSink>()`, the one call a
  host makes.
- **Serialization** — `XgFilterRazorJsonContext`: the source-generated JSON
  metadata for the one value this project serializes itself, the panel's
  expanded facet rows.

**`XgFilter_Razor.TestSupport/`** — producer-owned test support for host
suites: `FilterSurfaceStorage` states the surface's storage calls on a host
test's `BrowserStoragePlan` in the surface's own terms, and
`FilterRestorationMarker` gives a host's browser tests the selectors for the
restoration marker. Not packable.

**`XgFilter_Razor.Tests/`** — bUnit over xUnit: one class per component (the
composite's as wire tests), the owner's contract (`FilterSetupTests`) and §4's
acceptance cases through the composite (`FilterSetupAcceptanceTests`), the
storage policy under refusal, the restoration marker, the draft, the source
token and the saved-filters store, plus the test-support helpers pinned against
a real render and the trim-posture pins. `FakeDocumentStorage` is the recording
fake over the saved-filters storage seam the store and composite tests share;
`RecordingRefusalSink` and `RefusalNoticeHost` play a host's refusal sink and
the page that shows it; `OpponentBoundRuling` is the one oracle the help and
panel suites both pin the opponent-bound statement against (see Test project).

## Architecture

### Thin Razor wrapper

Parallel to `BgDiag_Razor`'s relationship with `BackgammonDiagram_Lib`: this
subproject lets `XgFilter_Lib` stay free of any Blazor / Razor dependency. All
filter logic, classification, the `FilterConfig` DTO, the
`NamedFilterCollection` document, facet activation (`GetActiveFacets`), field
validity (`GetInvalidFields`) and enum labels live in the core lib; this
project binds those primitives into Blazor components, produces `FilterConfig`
instances, and holds the user-facing state `SPEC-filtering.md` governs in one
app-scoped owner. Display strings come from `EnumLabel.ToLabel<TEnum>()`, which
reads each member's `[Description]`, so the label contract lives with the enum
and not with the UI.

### The setup-state owner — `FilterSetup`

`SPEC-filtering.md` §4 (halheinrich/backgammon#374) names one owner for the
state its rules govern, because the post-mortem on halheinrich/backgammon#373
found that state spread across lifetimes that end at different times: the panel
held the draft, which died on navigation; the hosts covered the component's
absence with reconcile code of their own; and a commit's callback ran after its
storage write, so it could land in a setup that had already ended.
`FilterSetup` is that owner. It is app-scoped, so it outlives every component
and every page; a full reload is a fresh owner, which is a fresh setup and a
fresh restoration.

**The vocabulary.** §4 lists what the owner holds and derives, and
`FilterSetup`'s remarks say it again for a reader of the code. The words this
doc uses:

- **Generation:** advanced whenever a setup ends, so a consumer can tell "the
  setup ended" from "something in it changed".
- **Draft** (`FilterDraft`, below): the selection on screen as the user left
  it, valid or not.
- **Resolved:** the draft is the user's choice. False only after a failed
  restoration, until a valid Apply or Clear. Current state, not history: it
  travels with the draft through navigation and source changes.
- **Baseline:** the draft last applied in this setup, or none.
- **In effect:** derived, never stored. The baseline while the draft equals it
  as a config, so an edit undone back to the applied values is in effect again;
  or **the ready empty selection** (§1, halheinrich/backgammon#266), a
  resolved, valid draft that restricts nothing, once restoration has settled
  and with a source.

Beyond §4's list the owner holds the two restoration notices, whose occurrence
(this boot's restore) is the outcome's. Nothing is persisted but the committed
selection, which is what the restoration reads back. `FilterSetupSnapshot` is
the derived reading, made once per published state; no derived fact is kept
anywhere as a copy that could change.

**Transitions** are §4's table. What the code adds to it:

- an edit, or a saved filter staged, also ends the restored notice;
- a different source keeps the draft's resolution with the draft;
- Clear also needs a source (Hal's ruling, under `FilterPanel`);
- the restoration settling records its outcome, and hydrates the draft only if
  no gesture touched it since the read began;
- a commit's write completing sends a refusal to the host's sink and, landing,
  ends the failed-restore notice, and does nothing else.

**Order.** Every accepted change takes effect, and is published, before any
storage call it makes. Apply records its baseline and publishes, then writes;
the click's task is the write's, so it completes when the browser has answered.
Because a write's completion changes no draft, baseline or gate, a write that
completes late cannot restore anything older, and §4's "callback from an
earlier generation" has nothing to reject. The restoration is the one read
whose answer changes state, and it changes only a draft no gesture has touched
since the read began — an edit, a staged saved filter or Clear, counted whether
or not it changed the value. It is boot-scoped rather than setup-scoped: a
source change while it is pending neither strands it nor lets it overwrite a
newer draft, and its outcome is still recorded.

**Restoration** runs once per boot: `RestoreAsync` starts the one read at the
panel's first interactive render, where interop exists, and every later call
returns the same task. A restored valid empty selection raises no restored
notice: it is ready as it stands, and nothing differs from a first visit (§1,
Reload). A stored document that reads but holds a field a rule outlaws restores
whole, the field shown and marked (halheinrich/backgammon#269); it is not
unreadable. A failure that is not the browser's refusal — static rendering, a
disconnected circuit, a timed-out call — is not an outcome: it propagates out
of the panel's lifecycle and leaves restoration pending.

**What consumers see.** `Attach(observer)` delivers the current snapshot before
it returns, then each real change, in order, until the handle is disposed;
`Current` answers on demand. A component or page mounted later therefore learns
the existing state without any change being manufactured. `Applied` fires,
after the observers, when a selection is committed into effect: by Apply, by
Clear, and by the restoration settling with the ready empty selection on
screen, which is in effect without Apply. It is the one explicit entry for the
work a committed selection triggers in the host (a count, a preparation), so
that work never hangs off the snapshot stream, which is for rendering, and
never off an edit — an edit that empties the draft puts the empty selection in
effect (`IsInEffectFor`) and commits nothing; the host reads that when it next
prepares (halheinrich/backgammon#421). A snapshot is immutable, and every
config it hands out is new, so nothing a consumer holds can reach the owner
except through its operations. A host reads its gate source-relatively
(`IsInEffectFor(token)`, `ConfigInEffectFor(token)`), so a host that forgot to
report a changed source reads its gate dark, never another corpus's filter (§3:
the need is ownership, not history).

**Storage refusals have one route, and no lifetime stands in it.**
`FilterStorage` (below) tells the host's `IFilterStorageRefusalSink` of every
refusal before the result returns. The sink is the host's own registered
holder, resolved in the owner's scope by `AddFilterSurface<TRefusalSink>`, so a
refusal that arrives after the page that started the call has gone, or after
its setup ended, still reaches the host's occurrence owner, and a page mounted
later shows it with no further failure. Snapshot subscriptions carry state
only: disposing one stops snapshots to that observer and cancels nothing, so no
subscription stands between a storage call and the sink.

**The shared condition's sink — `BrowserStorageConditionRefusalSink`.** A host
whose storage notice reads BgUiPrimitives_Razor's `BrowserStorageCondition`
needs no holder of its own: this public, sealed adapter is the
`IFilterStorageRefusalSink` over it. It lives here because this library depends
on BgUiPrimitives_Razor and not the reverse, so only here are both types
visible. `ReportRefused` forwards to the condition first, every refusal, since
deduplicating is the condition's; then it logs one warning with the browser's
exception, because the surface logs nothing and the condition holds no logger.
A failure of the log call is contained, so a throwing logging provider can keep
neither the occurrence from being recorded nor the surface's operation from
completing. Its constructor does not check its parts
(halheinrich/backgammon#431): dependency injection supplies both, but built by
hand a null condition fails on first use and a null logger's failure vanishes
inside that containment. App-scoped, like the condition.

**Registration and lifetime.** `AddFilterSurface()` (the shared condition) and
`AddFilterSurface<TRefusalSink>()` (a host's own holder) are the one call a
host makes; either registers the owner and the storage seam scoped — one per
app in WebAssembly, one per circuit on a server — and calls
`AddBrowserStorage()`, so a host cannot register the owner without its storage.
The parameterless overload also calls `AddReportingBrowserStorage()`, so the
surface cannot be registered without the condition its refusals reach, and
registers the adapter scoped as the sink. `TryAdd` throughout: the first call's
sink stands. The owner's constructor is internal: a host never makes one, so
the instance a page injects and the one the surface injects are the same by
construction. Not thread-safe: every member is called on the renderer's
synchronization context, which every component event and lifecycle method
already is.

**Why this shape.**

- *One owner injected by both sides, not a holder bound as a parameter.* The
  retired `AppliedFilter` was a host-registered holder bound to the composite;
  a host could bind a second instance, and the composite's mediation was a
  second writer beside the host's reconcile code. Injection makes one instance
  per scope by construction.
- *Snapshots, not events.* Per-gesture events made each host keep its own copy
  of "what is applied" in step. A snapshot carries the derived facts, so there
  is nothing to keep in step.
- *The host reports its source; the surface does not take one.* A host that
  unmounts the surface while no source is held still has to end the setup when
  it latches the next one (§4: A → B → A while unmounted). Only the host is
  there to say so, so it is the one reporter.
- *Publish before persisting, not revision checks.* §4 allows either. With
  publication first, a write's completion has nothing to update, so the
  ordering rule holds structurally rather than through a check each completion
  must remember.
- *A registered sink, not a component's event.* A component's callback dies
  with the component, and a refusal can outlive it.

### The draft — `FilterDraft`

The retained draft is the editor's state, not its parsed config: an immutable
record holding each typed box's text verbatim and every choice, with value
equality over what is on screen. A config cannot carry that: its bounds are
numbers, so `1.5` in a move-number box parses to no bound, the facet goes
quiet, and the selection counts as the empty one. The draft keeps the text, and
the parsed config (`ToConfig`, a new instance each call), the lib's verdicts on
it and the active facets are derived on every ask. Nothing is cached: a
record's `with` copies every field, and a cached verdict would ride into a
draft it was not computed for.

The draft adds one rule beside the lib's, and it is about representation, not
the domain. A range bound that is not a number, or a move-number bound that is
not a whole number, is an `UnrepresentableFields` member. It joins the lib's
`GetInvalidFields` in `InvalidFields`, which the gates, the marks and the save
refusal read, and its facet joins `GetActiveFacets`, so the row badges `set`:
such input is a criterion the user meant, never a blank one, and it cannot make
the selection ready-empty. Blank text is no bound and no fault; unfinished text
(`1e-`, `-`) is not blank, and is unrepresentable until it becomes a number.
`RestrictsNothing` is the empty selection: valid, with no criterion by the
lib's activation predicates. The sets are sorted, so one selection has one
order and one JSON.

**What a bound reads as a number** (`halheinrich/backgammon#379`, Hal's ruling
of 2026-10-08, the four bound boxes only). Every spelling a browser's number
control accepted — a sign, digits with or without a fraction, a bare fraction,
an exponent — plus a leading `+` and a mark with nothing after it (`5.` is 5),
with either `.` or `,` as the decimal mark, whatever the locale. There is no
grouping: the mantissa holds one mark at most, so `0.05` and `0,05` are one
number, `1,234` is 1.234 and never 1234, and a mixed, grouped or repeated mark
is no number at all. Space around the text is not part of it. A move number
must be whole however it is spelled (`3`, `3.0`, `3,0` are three; `3.5`, `3,5`
are not move numbers) and within the field's range. The text stays as typed;
the config, and so what is remembered, carries the number in its own canonical
form. The grammar is `FilterDraft`'s (`BoundNumber`); the domain rules stay
XgFilter_Lib's: `1e999` reads as infinity, and the lib's verdict decides it.
Position Pattern's commas are its own grammar's delimiters, and nothing here
touches that box, the player names or the match scores.

The four bound boxes are text boxes (`type="text"`, with `inputmode` `decimal`
for the error range and `numeric` for the move numbers), never number controls.
A browser's number control hands the page an empty value for text it cannot
read yet — `1e-`, `-`, a decimal comma in a locale that writes a point — so the
draft would hold "no bound" while the user is still typing one. A text box
hands over what is typed. bUnit sets values, it does not type: real keystrokes,
blank versus unfinished text, navigation and correction are BgQuiz_Blazor's
`FilterFieldEntryTests`.

### The surface's storage — `FilterStorage`

The one seam for every storage call the surface makes, over `BrowserStorage`'s
local area: the owner's read and writes of the selection, the panel's reads and
writes of its display preferences. It names the three keys (`ConfigKey`,
`DisclosureKey`, `MoreFiltersKey`) and reports each refusal to the host's sink
before returning the result. Parsing and defaults stay with each reader.
Internal: no consumer sees the keys. The policy underneath — every call made,
refusals as results, only the browser's refusal caught — is
BgUiPrimitives_Razor's "Browser storage access"; this repo's half is the
pitfall below.

### `FilterSurface` component — the consumer surface

The one component hosts embed (umbrella arc halheinrich/backgammon#63 /
halheinrich/backgammon#78 Step 2): it mounts `FilterPanel` and the
saved-filters mount of `NamedEntriesPanel`, owns the `NamedEntriesSurface`
preset that mount renders, and wires them to the owner. Saved filters load by
staging into the owner, save from the owner's draft or refuse, and delete
through the store; the composite also carries the saved-filters degrade
notices. It injects `FilterSetup` and holds no filter state. A host binds only
what is the host's to say here (see Using the surface). Storage refusals reach
the host through its registered sink, not through this component. No
`RenderFragment` slots: a design decision, because neither host needs one.

**The setup-change rule for the saved-filters context.** The composite attaches
to the owner. The first snapshot, delivered inside `Attach`, is the existing
setup learned, and ends nothing. A later snapshot with a new generation is the
end of a setup: the refusal posed against the outgoing context drops, and the
saved-filters context reloads through the seam, or resets when there is no
source. A change to the draft or the baseline moots a stale save refusal.

Nothing awaits that reload — the owner tells the composite inside its own
operation — so the composite observes it (`ReloadAfterSetupEndedAsync`,
`halheinrich/backgammon#374`). The storage's own failure
(`DocumentStorageException`) never reaches it: the store degrades to
`LoadFailed`, and the existing notice says so. Any other exception is an
adapter's bug, and goes to the renderer's error path as the composite's,
through `DispatchExceptionAsync`: to an enclosing `ErrorBoundary`, else to the
host's unhandled-error handling. Discarded, it would die on a task nobody reads
and leave the previous source's saved filters on screen.

The composite owns its `SavedFiltersStore` over the bound adapter (rebuilt on
an adapter reference change), so a remount re-reads the document: a setup-time,
degrade-tolerant read. Notice copy is producer-owned so every host degrades
with identical wording: the save-refusal copy (field-agnostic by design,
because the offending value is already marked, with its own explanation, in the
panel below), the LoadFailed notice (which replaces the panel and names the
*actual* failed file via `SavedFiltersStore.LoadFailedFileName`, canonical or
legacy), and the WriteFailed notice (beside the still-truthful panel).
Saved-section visibility: Ready shows the panel unless read-only *and* empty
(nothing to load, nothing to save: BgQuiz's clutter rule, now producer-owned);
WriteFailed keeps the panel beside its notice; Disabled and LoadFailed render
none.

**The composite's and the panel's boxes render through `Notice`, and each
dismissal has one holder — the owner of its occurrence**
(`halheinrich/backgammon#248`). The umbrella's `SPEC-notices.md` rules what
each box is, and so whether it dismisses; a dismissal belongs to one
occurrence, lives as long as it, and is never stored. Who holds it follows from
who knows when a new occurrence begins and whether the old one outlives the
page:

| Box | What it is | Occurrence | Holder of the dismissal |
| --- | --- | --- | --- |
| `#filterRestoredNotice` | event notice, polite | this boot's restore of a stored selection | the app-scoped `FilterSetup` — the occurrence outlives every mount of the panel, so the panel binds the component's dismissed state to the owner's snapshot and keeps no copy |
| `#filterRestoreFailedNotice` | event notice, polite | this boot's restore of a stored selection it could not read (`halheinrich/backgammon#367`: a failed restore is never silent) | the same `FilterSetup`, its second notice — the unreadable document outlives every mount too; one holder for the two outcomes of one restore. It also ends at a commit whose write lands, replacing the unreadable document; an edit leaves it, and so does a commit whose write the browser refused |
| `#filterSaveError` | error, assertive | one refused save | the composite, *as the refusal itself*: `_saveRefusalNotice` is non-null exactly while a refusal stands, so closing the box clears the field and no dismissed bit exists anywhere |
| `#savedFiltersWriteFailed` | condition notice, assertive | one failed write | the `Notice` instance, keyed by `NamedDocumentStore.LastWriteFailure` — the store names the occurrence because only it knows a write failed; store and notice both die with the composite, so the component may hold the bit |
| `#savedFiltersLoadFailed` | gate reason, polite | — | none: it cannot be closed. It stands where the saved-filters panel would and is the only thing saying why saving is off; it leaves when its cause does |

In the restore notice the user's close gesture and the owning gesture are **one
state change, not two**, because both mean the notice is over for this boot and
nothing reads the difference. Closing a notice is never an edit: it moves no
draft and no gate. `NamedEntriesPanel`'s load confirmation is the same model in
the generic panel; see that component's section for its holder.

### `FilterPanel` component

`FilterPanel` owns the entire filter-form UI as a Bootstrap card. It holds no
filter state and declares no parameter: it renders the owner's snapshot, turns
each gesture into one of the owner's operations (`Edit`, `ApplyAsync`,
`ClearAsync`), and re-renders on each snapshot the owner publishes, so a
remount shows exactly what was on screen before, unapplied edits and invalid
input included. What it keeps of its own is display state: its two persisted
preferences, the level groups' open state, and which wrong fields it shows
(below).

The dice-roll control is a checkbox grid enumerated from `DiceRoll.All`; each
label is the roll's own canonical token (`DiceRoll.ToString` → `"31"`),
rendered verbatim rather than hyphenated, so display text stays owned by the
type as the enum facets defer to `ToLabel`. Selections are held raw in the
draft (none = facet off); `Build()` owns materialization. The analysis-depth
control renders the facet's **three per-mode pairs**: one toggle checkbox per
selectable `AnalysisMode` (labelled from the enum's `[Description]`s; `Unknown`
gets no toggle), each disclosing its own "Analysis level" group while checked,
one checkbox per `AnalysisLevel` in declaration order. Each level group is an
honest disclosure (real button, `aria-expanded` / `aria-controls`), collapsed
by default and deliberately **unpersisted** (see Pitfalls); while collapsed it
carries a count badge, "any" with no level checked, "N selected" otherwise.
Position type and play type are shelved for later reintroduction: their UI
groups are hidden, while the `XgFilter_Lib` machinery behind them
(`FilterConfig.PositionTypes` / `PlayTypes`, the filters, the enums) stays
intact.

**Validity is the lib's ruling; the panel marks it and words it.** One verdict
is the whole of it, the draft's `InvalidFields`:
`FilterConfig.GetInvalidFields()` on the draft's parsed config must name no
field, and no box may hold text its field cannot be at all (the draft's
representation rule, above). That parsed config is the one Apply commits, so
what the panel reds and what `Build()` would throw on are one answer. The
error-range inputs style themselves independently off `FilterField.ErrorMin` /
`ErrorMax` membership, so the lib's attribution rules carry straight to the
screen: a negative Max never reds a Min the user got right, while a misordered
pair blames both and leaves the user to pick an end. The message is the panel's
alone, since the lib returns no strings by design, and covers both violations
in one line, worded to stay true for text that is not a number yet (unfinished
input, or a word such as `NaN`).

Every feedback line renders only while it applies (the `#applyDisabledReason`
idiom) rather than sitting in the DOM for Bootstrap's sibling selector, which
cannot reach the inputs one level down inside the flex row; and since
`halheinrich/backgammon#270`, because a described-by reference to a hidden
element is read out regardless, a line's id exists exactly while the line is
shown. A stored selection whose bound a rule outlaws still loads, shows its
values, marks the offender, and is refused a commit: never silently repaired,
never dropped (the lib's documented posture, pinned).

**What a box shows of its verdict is a second fact**
(`halheinrich/backgammon#272`, `SPEC-filtering.md` §1). Validity is continuous,
because the gates read it: Apply goes dark on the keystroke that makes a value
wrong. The mark and the message for a newly typed error appear when the user
leaves the box, and clear as soon as the value is corrected; a wrong value that
was not typed here — restored, staged from a saved filter, or on screen when
this panel mounts — shows at once. The panel holds which wrong fields it shows
(`_revealed`, per mount), and `ShownFields`, the verdict as far as it is
revealed and never wider, is what `FieldAttributes` and every feedback line
read. Each verdict-bearing box's `onblur` reveals what is wrong in the group
its message speaks for: a range's two bounds share one line, and a misordered
pair blames both. A draft change this panel's own typing made keeps only the
reveals still true (`Edit` marks the panel's gestures, since the owner
publishes inside the call). Any other draft change shows every wrong value at
once. The match-score field holds a list and its line speaks once per kind of
mistake, so the same rule runs per fault kind there (`_revealedScoreFaults`,
`ShownScoreFaults`): a kind typed while another already shows waits for the
leave and takes no space until then; a shown kind clears at its correction;
staged, restored and remount-found kinds show at once. The field's mark follows
the kinds the line speaks, so it never stands over a line that says nothing. So
typing never makes a message appear under the box.

**A shown message's box keeps its place** (`halheinrich/backgammon#272`, Hal's
ruling of 2026-10-08). A message leaves at the keystroke that corrects it, and
so does its announcement: the mark, `aria-invalid` and the description's
reference go, and no element carries the line's id. Its box stays where it was,
with the same words at the same size, saying nothing — `invisible`,
`aria-hidden`, and marked `data-held-for="<the line's id>"`
(`FilterPanel.HeldLineAttribute`) — so the layout below does not move while the
user types. A fresh error typed into the box still waits for the leave, then
shows in the same box. The space is the panel's, per mount (`_heldLines`). It
is held from the moment a line is shown in a visible group, and kept through
leaving a box, Apply, Clear, a staged saved filter and a restore: a blur can
move the clicked button between press and release, and a restore can finish
while the user is typing. It is released only when the group folds away (its
row closes, or the More filters container over it) or the panel unmounts; the
error range sits outside the container, so its space lasts until unmount. The
match-score line's words differ by fault kind, so its space is per kind: a kind
it has shown and the list no longer has keeps its place in the line, saying
nothing (`_heldScoreFaults`). The hold is recorded at the state changes that
can show a line (`HoldShownLines`: a snapshot, a reveal, a group opened by the
user or by a restored preference), not after a render: under a server host
`OnAfterRender` waits for the browser's acknowledgement, and a keystroke
handled in between would find a line shown but not yet held. bUnit pins the
structure: one box, in one place, through the ruled sequence. Whether the
pixels hold still, and whether typing keeps the browser's focus and caret,
bUnit cannot establish; BgQuiz_Blazor's `FilterFieldEntryTests` is that check.

**How an input wears the verdict is rendered from one place**
(`halheinrich/backgammon#270`). The `is-invalid` class is a colour, and a
screen reader hears nothing of it; so an invalid input also carries
`aria-invalid="true"` and an `aria-describedby` that names, beside the hint it
always names, the feedback line that says why, whose id exists exactly while
the field's verdict is shown, so the reference never names an element that is
not there, or a box that is only holding its place. A valid input carries
neither mark and keeps its hint. The private `FieldAttributes` on `FilterPanel`
is the one place that decides all of it, and every box on the panel that bears
a verdict splats its result rather than typing a class expression of its own: a
seventh field joins the splat or fails to be announced, and the suite's per-box
theory is where that shows. Attribution stays the lib's: the field asked for is
the one `GetInvalidFields` names.

**The match-score field's verdict — two voices over one lib seam**
(`halheinrich/backgammon#121`). `FilterField.MatchScores` names the *list*, not
what is wrong in it, so the panel asks `MatchScoreToken.GetFault` per token and
reads back a `MatchScoreTokenFault`. It renders **one voice per distinct fault
kind** into `#matchScoreFeedback`: `Malformed` states the vocabulary and is
answered by retyping; `Retired` says the bare money token is retired and names
its replacements from `MatchScoreToken.RetiredMoneyReplacements`, because that
mistake was correct spelling under an earlier grammar and retyping is not the
remedy. A list holding both kinds gets both lines; many tokens of one kind
still get one. The fault is typed and wordless, the wording is the panel's: the
same division of labour as `BoardPattern.TryParse`.

Each feedback line keys on its **own** fields, never on "the invalid set is
non-empty": the set spans facets, and a facet-blind test would explain the
error bounds over a fault committed three sections down (pinned both ways).

**Token spellings render from the lib's constants, never as literals** (see
Pitfalls). The placeholder's examples of them, the hint line, and both verdicts
all render `MatchScoreToken.MoneyWithJacoby` / `MoneyWithoutJacoby` /
`RetiredMoney`. The grammar also accepts `MatchScoreToken.DoubleMatchPoint` as
a second spelling of `1a1a` (`halheinrich/backgammon#259`), and the field
offers it everywhere it states the vocabulary: an example of its own in the
placeholder, a spelling of `1a1a` in the hint, beside the money tokens in the
malformed verdict, and in `FilterHelp`'s match-scores prose.

**The position-pattern field presents a grammar it does not own.**
XgFilter_Lib's "Patterns" owns the bracket list, its token table and what
parses. The text is the config's text, both ways
(`halheinrich/backgammon#269`): the draft's text rides into
`FilterConfig.PositionPattern` as typed (a blank box as `null`, the lib's "no
pattern") and a restored or loaded config's text lands back in the draft as
stored, so the panel builds no `BoardPattern` for the config's sake and a
stored pattern a newer grammar rule refuses shows the user what they wrote:
marked by the lib's verdict, with Apply withheld, never repaired and never
dropped. The field's copy states the grammar for a user typing it. Hal's ruling
on `halheinrich/backgammon#275` (2026-09-25), stated here and in `FilterHelp`
alike, is that the opponent's "at least n" is written with the upper bound
(`[1-6,,-1]`) and a lower bound (`[1-6,-1,]`) means "at most n"; a friendlier
spelling, and counting points by what they hold, are still open on that issue.
The borne-off names render from `CheckerLocation.PlayerOff` / `OpponentOff`
(their `ToString` is the canonical spelling; the name constants themselves are
`internal`).

The example tokens are literal.
`PositionPatternPlaceholder_IsAnAcceptedPattern_ThatIncludesARange` parses the
placeholder whole and asks it to hold a range, and the ruling's two examples
are asked of the grammar through `OpponentBoundRuling`. The hint's "E.g." list
is parsed by no test, so a grammar change could leave it stale unnoticed. The
invalid-entry line names no single cause, deliberately: `TryParse` reports
none, and a list of causes (opposite-signed bounds, a backwards range, a
wrong-signed bar bound, a duplicate place) would be a second copy of the lib's
rules, incomplete the day the lib adds one. It sends the reader to the rules
stated directly above it.

**Information hierarchy** (dogfooding-driven, re-ruled in
`halheinrich/backgammon#193`, folded once more in
`halheinrich/backgammon#231`): the error-range section is first and always
visible, as the panel's most-used control. Each of the other eight facets is
**its own collapsible row**, one tier, in `FilterFacet` declaration order, and
all eight rows sit **behind one further disclosure**, the container described
below, so the panel at rest is the error range and the two buttons. Every row
is an honest disclosure: a real `<button>` (`#facetToggle_<Facet>`) carrying
`aria-expanded` / `aria-controls` over an always-rendered `#facet_<Facet>`
region whose children render only while expanded (absent from the DOM when
collapsed, not styled away), named by the facet's own `[Description]` via
`ToLabel()`, with a decorative `+`/`−` glyph marked `aria-hidden` because
`aria-expanded` already carries the state. Each section's bracketed
`<small class="text-muted">` hint renders inside the expanded body and never in
the collapsed row, so nothing is said twice.

Every id in a row is built from the facet's enum member name — one mould,
`facetToggle_`, `facet_`, `facetBadge_`, and `facetHint_` where a hint is
referenced — which is what lets a control **name itself by reference** to its
own header rather than repeating the heading. `halheinrich/backgammon#196` made
that pair of references (`aria-labelledby` at the header, `aria-describedby` at
the hint) the rule for every box the user types into (the error-range pair
names itself off its own heading's words, outside the rows). A hint's wrapper
has no `for`: a `<label for>` whose text is the hint would have made the hint
the field's *name* (`halheinrich/backgammon#238` tracks the bare `<label>` left
behind). The regions deliberately carry no `role="group"`: unruled, and the
level groups do not carry it either.

**The container over the rows** (`halheinrich/backgammon#231`) is the rows' own
idiom one tier up: a real `<button>` (`#moreFiltersToggle`) carrying
`aria-expanded` / `aria-controls` over an always-rendered `#moreFilters` region
whose children render only while expanded, with the same decorative glyph.
**Folded is the resting state.** Its ids are the rows' mould without the
per-facet suffix — `moreFiltersToggle`, `moreFilters`, `moreFiltersBadge` — and
deliberately *not* `facetToggle_` / `facet_` / `facetBadge_` shaped: those
prefixes are surveyed as "the rows", here and in both hosts, so a container
answering to one would misreport itself as a ninth row. Its badge counts the
rows whose facet is set (`RowFacets.Count(ActiveFacets.Contains)`, the rows'
own lib ruling, never a second count) and speaks only while folded;
`ErrorRange` is never in it, because it is not behind this fold.

**Its name flips with the fold** (`halheinrich/backgammon#239`): *More filters*
folded, *Fewer filters* expanded, so the control says what the next click does.
The two words and the rule that picks between them have **one owner** —
`MoreFiltersFoldedLabel`, `MoreFiltersExpandedLabel` and the private
`MoreFiltersLabel` on `FilterPanel` — and nothing else spells either: the
markup renders `MoreFiltersLabel`, `FilterHelp` names the control once by both
constants and says "the fold" thereafter, and a source survey in the suite
holds each literal to that one definition.

**The container reads as the rows' parent, and that is drawn, not merely
meant** (`halheinrich/backgammon#239`: in the candidate the user rejected,
drawn flat at the rows' left edge, size, weight and colour, the toggle read as
no collapse widget at all). The `#moreFilters` region indents its rows as a
group with `ms-3`, the step a row's own body already takes under its header;
the toggle takes header weight (`fw-bold`) and **drops `btn-sm`**, since the
small scale marks the repeated tier and the control over it is not of it; and
the region takes an `mt-3` gap after the toggle while it is open. No colour is
added: `btn-link`'s blue already says "activates". It stays a `<button>`,
**never a heading element**: what outline the panel sits in is the host's to
know, which is why `FilterHelp` takes a `HeadingLevel` and this guesses none.

**Row spacing is the body's, not the row's** (`halheinrich/backgammon#239`).
The row wrapper carries no margin, so a stack of one-line headers reads as one
list; the `mb-3` step rides on the `#facet_<Facet>` region and **only while
that region has children**, so an expanded body keeps space beneath it without
putting a blank line back between collapsed headers. The container's own gap is
conditional for the same reason.

**The two display preferences.** Which rows are open is the user's, persisted
under `xg_expandedFilters` as a JSON array of `FilterFacet` member names in row
order. Restore is **all-or-nothing**: anything that is not an array of names a
row answers to — an unknown name, a `FilterFacet` with no row, a numeric token,
malformed JSON — restores every row collapsed rather than salvaging part of it,
because half-honouring corruption would open a set the user never chose.
Whether the **container** is open persists under **its own** key
(`xg_moreFiltersOpen`), as the literal `true` or `false`, rather than joining
the rows' set: that set's vocabulary is `FilterFacet` member names, so a
sentinel name in it would be exactly the corruption its restore exists to
refuse. Anything unreadable leaves the container folded. Neither key is inside
the config blob; staging a saved filter and Clear filters move neither, and
toggling either is navigation, not an edit: the owner hears nothing. A row the
user left open is still open when the container is next opened, which is the
whole reason the two preferences are two keys.

**Row badges**: while collapsed, a row carries a badge (`#facetBadge_<Facet>`)
exactly when its facet holds an active filter. Its **presence** is the lib's
ruling, computed from the live draft's parsed config through
`GetActiveFacets()`, plus the draft's own rule for a box whose text its field
cannot be; see the pitfall for why it re-inspects nothing. Feeding from the
live draft makes it honest at rest and live mid-edit: it tracks staged values
the moment they are typed, not on Apply. Its **words** are the panel's display
concern (`FacetBadgeText`): `set` for the facets chosen by typing into them,
where a count of boxes filled would say nothing, and `N selected` for the
facets chosen by ticking options (decision type, one by construction; contact
type; analysis depth's checked modes; dice rolls). Expanded, no badge renders:
the controls themselves say everything it could.

**Apply and Clear filters are the owner's commits** — never per keystroke.
Apply makes the draft this setup's baseline, resolving it, publishes, then
writes the selection for the next visit. **Clear filters** is the full-clear
gesture: the draft and the baseline become the empty selection, published, then
written. Both are guarded in the owner on the gate the buttons render from, so
a dispatch that ignores the disabled attribute cannot re-commit an unchanged
selection, and Clear waits for restoration to settle. A refused write never
undoes either.

**What is in effect is derived, never latched.** The panel reads whether the
selection on screen is the one in effect, and the Apply gate, from one
snapshot, so they can never disagree. The baseline compares as a config
(`FilterConfig`'s value equality), which is what makes an edit-then-undo
recoverable. Only a valid draft can match the baseline: a box the config cannot
represent parses as no criterion and would otherwise match a baseline without
one.

**The empty selection is ready without Apply** (`halheinrich/backgammon#266`,
§1): a resolved, valid draft that restricts nothing is in effect once
restoration has settled, whether nothing was ever chosen, every criterion was
cleared, or an applied filter was edited back to nothing. A pending
restoration, a failed one, an invalid draft and no source are not evidence that
the user chose no filter, and none is ready. While Apply has nothing to do, the
panel says why — a `title` plus a muted hint line, the `NamedEntriesPanel`
disabled-reason idiom, except that here the panel knows its own reason: "no
filter is set" over the empty selection, "already applied" over a non-empty
one. Neither invalid-value case gets a hint line: the offending field's own
feedback already explains it, once shown.

**Before a source is reported** (Hal's ruling of 2026-10-08): editing is
allowed, so a user can prepare a selection before picking what to filter; Apply
is off; and Clear is off and writes nothing (`CanClear` requires a source, as
`CanApply` does), since Clear commits and a commit belongs to a setup. A
disabled Apply gives no reason while there is no source or while restoration is
pending: either reason would claim a filter state that is not yet known. That
is as built and approved.

**Restoring at mount.** `OnAfterRenderAsync(firstRender: true)` starts the
owner's restoration (once per boot; a later mount finds it settled or in
flight) and restores this mount's two display preferences through
`FilterStorage`: they are read on every mount, not held app-scoped as the
selection is (`halheinrich/backgammon#380` asks whether they should be). A
refused or absent read leaves each preference's default. Each preference
restore re-checks its own touched flag after its await (`_moreFiltersTouched` /
`_disclosureTouched`), so a user toggle landing mid-read wins. The restoration
is awaited last, so a failure that is not a refusal surfaces in the lifecycle.
`MountRestored` completes when all of it has settled: the observation the
pending-restore pins wait on, since a late restore that rightly yields renders
nothing.

**The restoration marker** (`halheinrich/backgammon#346`). The panel's root
carries `data-filter-restoration` (`FilterPanel.RestorationAttribute`,
internal): `Pending` until this mount has applied everything it restores, then
the selection's `FilterRestoration` outcome by name, failures included. Each
mount settles its own marker, so a navigate-back reports `Pending` until its
preferences are back, the restore a click on the container's toggle could
otherwise race. It is the outcome alone and says nothing about the gates. A
host's browser test waits on it through
`XgFilter_Razor.TestSupport.FilterRestorationMarker`; host code reads
`FilterSetupSnapshot.Restoration` instead.

**The restored-selection notice (§4's legibility law).** A reload ends the
setup: the restoration puts the previous session's selection on screen with
nothing applied and Apply re-armed — correct by rule, and exactly what a defect
would look like, so the panel says what happened (`#filterRestoredNotice`). The
owner shows it when the restoration hydrated the draft with a selection that is
not the ready empty one. Nothing stored, unreadable, refused, or a read
superseded by an edit made while it was pending: nothing was put on screen, so
no claim. The notice ends at the first gesture that makes the selection the
user's own — an edit, a saved filter staged, a commit — or when the user closes
it, the same end. A source change is not a gesture and leaves it.

**The restore's other outcome is said too** (`halheinrich/backgammon#367`: a
failed restore is never silent). A stored document `TryFromJson` refuses
restores the defaults and says so (`#filterRestoreFailedNotice`). Nothing
stored is an ordinary first visit and gets no word, and a read the browser
refused is the storage condition, reported to the host's sink rather than by a
notice of the panel's. A document that reads restores whole, a refused pattern
included. The notice ends when the user closes it or a commit's write lands,
whichever commit's write it was. An edit leaves it, and so does a commit whose
write the browser refused, since neither changes what is stored. The unreadable
document itself is left as it is.

### `NamedEntriesPanel` component

A persistence-agnostic pick list over **any** `NamedCollection`, generic in the
payload and the specialization (halheinrich/backgammon#190 leg (D)).
`FilterSurface` is its one mount today; named mix saves
(halheinrich/backgammon#43) would be the second. Both type arguments are
inferred from what is bound to `Document`; nothing spells them at a mount site.
The panel owns no document state and mutates nothing: every gesture is raised
as a request (`OnLoadRequested`, `OnSaveRequested`, `OnSaveAsRequested`,
`OnDeleteRequested`, each carrying the name) for the host to mediate. The host
calls `With` / `Without`, persists wherever it persists, and passes the **new**
collection instance back down through `Document`; the reference change is also
the panel's confirmation channel (it cancels pending inline confirms and clears
the typed save-as name). Selection is deliberately stateless: the "current"
value lives in whatever editor the host wires up (for filters, the filter
setup's draft), so a highlighted row would be a second source of truth that
lies. Every destructive gesture runs through an inline confirm in the panel — a
row's Save, a save-as under an existing name, and delete — and `Contains` keeps
the case-insensitive name rule in the lib. A row's Save overwrites that entry
with the current value, and its confirm copy says so ("Overwrite '\<name\>'
with \<the surface's noun\>?"), deliberately distinguishable from the save-as
overwrite prompt ("Overwrite '\<name\>'?"). A row holds one confirm slot:
requesting Save supersedes a pending Delete confirm and vice versa. Hosts that
cannot persist right now (e.g. BgQuiz without its FS-Access grant) disable
Save/Delete via `CanPersist` + `PersistDisabledReason`; Load stays enabled,
being read-only over a collection already in memory.

**What the generic cannot know arrives as one record.** `Surface`, a required
`NamedEntriesSurface`, carries the card title, the empty-list line, the name
placeholder, the row-Save prompt's noun, and the three element ids. One
parameter rather than seven, so a new mount cannot half-configure itself. A
preset belongs with the composite that mounts it: `FilterSurface` owns
`SavedFilters`, `internal` so this repo's panel suite mounts the one true
instance rather than retyping it.

**The saved-filters wiring:** `OnLoadRequested` → resolve via `TryGet` →
`FilterSetup.Stage` (a miss throws; see Pitfalls); `OnSaveRequested` /
`OnSaveAsRequested` → `FilterSetupSnapshot.TryGetSavable` → `With` → persist.

**The load confirmation is the panel's one box, and the panel holds its
dismissal as the confirmation itself** (`halheinrich/backgammon#248`). "{name}
loaded." is an event notice, so dismissible under the umbrella's
`SPEC-notices.md`, and renders through `Notice` inside the standing
`role="status"` region (the surface's `LoadedNoticeId`), rendered
unconditionally and the only announcer. Its occurrence is one accepted load,
and the panel owns it outright: `_loadedName` is non-null exactly while a
confirmation stands, so closing the box clears the field, as every other
retiring gesture already does, and no dismissed bit exists anywhere. Loading
the same name again assigns the field again and shows fresh: the name was never
the occurrence.

### `FilterHelp` component

Producer-owned documentation for everything the panel offers — every facet, and
the chrome that governs them all — in user-level language, behavior only, never
`FilterConfig` internals or field names. It lives beside the panel it documents
so the prose has one owner. Render-only: it issues no JS interop and touches no
storage or state of its own. Structured for embedding: one `<section>` per
topic, each heading carrying a stable `fh-*` anchor id rendered from a named
constant on the component, facet heading text from the lib's `FilterFacet`
`[Description]`s via `ToLabel()`, so help titles, panel section headings and
the row badges all name a facet identically.

What each section is held to, past reading well:

- **Setting and applying filters** (`fh-using-the-panel`) sits before the
  facets, as the frame a reader needs to find and commit any of them. The
  reject-and-explain posture is documented as behavior, *nothing is guessed at
  or quietly ignored*, while each rule stays with its facet.
- **Error range and Players** say whose decision a listed name admits and whose
  error a range judges (SPEC-filtering §8, as XgFilter_Lib's
  `PlayerErrorFilter` states it): a name admits a cube decision the listed
  player made as the doubler or, where a double was offered, as the taker, a
  double left with no take or pass recorded still admitted by the responding
  player's name, never the opponent's no-double; and a range judges the listed
  player's error, either listed player's when both are, the player on roll's
  with none. Error range also states which error (SPEC-scoring §2a): a checker
  play's error is measured under the ranking the application uses to pick the
  best play, and a move that ranking does not score has no error. The ranking
  is the host's, so the copy names no setting and stays true in a host without
  one.
- **Analysis depth** explains the union semantics, the inner-level distinction
  per mode, the per-mode **Analysis level** disclosure and its badge, and that
  levels under an unchecked mode are kept but inert. It does not say which
  play's depth a checker play is judged by; whether it should waits on Hal's
  ruling (`halheinrich/backgammon#312`).
- **Match scores** renders its spellings from `MatchScoreToken`'s constants and
  deliberately does not offer the retired bare token: explaining a retirement
  belongs to the panel, which meets it where a user still has one typed.
- **Position pattern** teaches the bracket list as behavior, with worked
  examples. It describes the refused forms in words and never shows one. Its
  markup carries the split the pins key on: a concrete token is a `<code>` with
  no `<var>`, and every one must parse through `BoardPattern.TryParse`; a
  schematic form marks its placeholders with `<var>` and is exempt. The pins
  ask the parsed examples for a range of each sign and each zero-bound form,
  not for any wording, except the opponent-bound paragraph, which is ruled copy
  and is pinned as such (see Test project). The borne-off names render from
  `CheckerLocation`.
- The shelved facets (Position types / Play types) are deliberately
  undocumented until their UI returns.

**Heading depth is the host's to state**, via the required `HeadingLevel`
parameter: the lead heading renders at that level and every section one below,
so the block contributes a well-formed two-tier outline wherever it lands. The
`fh-*` anchor ids are unaffected by the level (pinned). See Pitfalls for why it
has no default.

**What the panel remembers** (`StorageSectionAnchorId`) is the
storage-assurance copy: it states in user terms that the panel saves its
settings in the reader's own browser and uploads nothing, and it names each
`localStorage` entry so a reader can verify them in devtools. It is conditional
(halheinrich/backgammon#371, wording approved by Hal on 2026-10-08): the
filters still work "for this visit, including navigation within the app" when
the browser refuses to keep them. Every key name renders from `FilterStorage`'s
own constants, so the copy cannot drift from what the surface actually writes.
**The prose around that list never counts it** (`halheinrich/backgammon#231`):
a sentence saying how many entries there are is a second copy of a fact the
reader can see, and it went stale the day the container's key arrived, leaving
the section saying "three" above the list and "both" below it. Scope is exactly
what the surface persists: a sibling `xg_*` key belonging to a host app is that
host's to document. A host with its own data-ownership copy points *into* this
section rather than restating it (BgQuiz's Help does), through the component's
two `public` constants.

### Non-visual interaction model (`Model/`)

The filter setup's types are described in their own sections above. The rest:

- **`FilterSourceToken`** — opaque, equatable identity of "which source",
  minted by the host via `FromGeneration(int)` / `FromPath(string)`, reported
  to the owner, and only ever *compared* by the producer. Each factory owns
  identity in its domain: `FromPath` **normalizes the path itself** (case-fold,
  trailing `\` / `/` insignificant), so a host passes the spelling it holds and
  cannot mint a token that misses its own previous visit's. `FromPath`'s
  remarks say why the trim is hand-rolled: this runs in WebAssembly, where a
  backslash is not a separator. "No source yet" is `FilterSourceToken?`.
- **`NamedDocumentStore<TValue, TSelf>`** + **`NamedDocumentStatus`** — the
  lifecycle of *any* `NamedCollection` document over the host's storage
  adapter: `LoadAsync` / `SaveAsync` / `DeleteAsync` / `Reset` moving through
  Disabled / Ready / LoadFailed / WriteFailed, over a `Document` the panel
  binds. Degrade, never block: no member throws for storage trouble; LoadFailed
  preserves the file untouched and keeps saving dead; WriteFailed keeps the
  in-memory edit and stops further writes. Round-trip is the document's own
  (`ToJson` / `TryFromJson`), and a null adapter is permanently Disabled, so an
  adapterless host composes with the same store. An internal load-version guard
  makes a superseded in-flight load discard its outcome. `LastWriteFailure`
  names each failed write, since `Status` reads the same after every one.
- **`SavedFiltersStore`** — the saved-filters specialization of that store, and
  the shape every sibling document takes: a sealed derived type supplying
  `FileName` and `LegacyFileName` from `SavedFiltersDocument`, and nothing
  else.
- **`IDocumentStorage`** + **`DocumentStorageException`** — the host seam:
  per-document text I/O keyed by file name (`ReadAsync(name)` returning null
  for absent, `WriteAsync(name, json)`), generalized by document name so a
  sibling document needs no interface change. **The seam knows no document
  kind**, which is why neither name says "filter" or "named"
  (halheinrich/backgammon#190 leg (D)): it moves text for a file name, and
  *which* document that is belongs to the store above it. Adapters follow the
  same rule and are named for where they read and write: BgQuiz's
  `FolderDocumentStorage`, ExtractFromXgToCsv's `HttpDocumentStorage`, this
  repo's `FakeDocumentStorage`.
- **`SavedFiltersDocument`** — the document identity: the public constants
  `FileName` (`xg-filters.json`) and `LegacyFileName` (`bgquiz-filters.json`),
  and the home of the two-name migration rule they imply (see Pitfalls for the
  corrupt-file rationale).

### Test project

bUnit + xUnit, targets .NET 10. Each suite registers the surface as a host does
— `Services.AddFilterSurface<RecordingRefusalSink>()` with the sink registered
beside it, or `AddFilterSurface()` in the adapter's own suite — and a
`BunitContext` is one app boot, so one owner and one restoration per test.
**Storage is planned where it is the subject, and
incidental elsewhere.** A suite whose subject is storage, the gates or the
acceptance cases puts a `BrowserStoragePlan` on the test's runtime
(`halheinrich/backgammon#377`), declares every call in the surface's own terms
(`XgFilter_Razor.TestSupport`'s `FilterSurfaceStorage`, or the keys directly
where a key's spelling is the subject), and ends with `Verify`. A suite where
storage is incidental runs `JSRuntimeMode.Loose`, which answers every read
"nothing stored" and every write "landed" through the real `BrowserStorage`. No
test here spells an interop identifier.

**The acceptance cases** `SPEC-filtering.md` §4 lists are
`FilterSetupAcceptanceTests`, each through the composite with the host's side
played as a host plays it: report the source, read the gate from the owner's
snapshot. `FilterSetupTests` pins the owner's own contract,
`FilterSurfaceStorageUnavailableTests` the storage policy under refusal, and
`FilterRestorationMarkerTests` the marker. A navigation is
`DisposeComponentsAsync()` followed by a fresh render in the same context; a
reload is a fresh context.

**Pin posture: structure and wiring, and copy only where it was ruled.** These
suites assert that a component renders the identity, value, or spelling its
source of truth names — the anchor ids, the storage keys, the score-token
constants — reached through test-only `InternalsVisibleTo` where the source is
`internal`. Wording itself is not pinned here: an independent-literal oracle
for "the user can read X" would be a second copy of the very text under test,
so that oracle lives in the consumer's e2e suite. **Ruled copy is the one
exception**, and it is the carve-out in the no-literal-spellings pitfall, not a
departure from it: where a ruling or a spec requires a surface to make a
statement, or to stop making one, the pin is an oracle for the ruling and holds
the copy to independent literals, naming the ruling it enforces. A statement
ruled for more than one surface is pinned in each against one shared oracle, so
the surfaces cannot be held to different rulings: `OpponentBoundRuling`
(`halheinrich/backgammon#275`) is the shape, and it also asks the grammar that
each of the ruling's examples still reads as the ruling says.

**Notice assertions** here are this repo's own: `AssertBox` in
`FilterSurfaceTests` and the box pins in `NamedEntriesPanelTests` spell the
component's markup (`bg-notice-content`, `btn-close`), written before
`NoticeBox` existed. `halheinrich/backgammon#250` tracks the swap.

### Test-support assembly (`XgFilter_Razor.TestSupport`)

Producer-owned helpers for **host** test suites, referenced by their test
projects only: `IsPackable=false`, and no app-graph project may reference it.
Two members:

- **`FilterSurfaceStorage`** — the surface's storage calls, stated on a host
  test's `BrowserStoragePlan` in the surface's own terms: this boot's
  restoration (`ExpectFilterRestore` with a stored selection or an outcome,
  `ExpectHeldFilterRestore` and `RestoreAnswer` to hold and release it), each
  mount's two display-preference reads (`ExpectFilterPanelMount`,
  `ExpectFilterPanelMountRefused`), each commit's write (`ExpectFilterCommit`,
  `ExpectHeldFilterCommit`), and the toggles' writes (`ExpectFilterFoldToggle`,
  `ExpectFilterRowsToggle`).
- **`FilterRestorationMarker`** — the restoration marker's selectors and
  reading for a host's browser tests: `SettledSelector` to wait on,
  `Selector(outcome)`, `AttributeName` and `Parse`.

Why it exists: the surface's keys, and the marker's attribute, are deliberately
not consumer surface, yet a host's test needs both. Under the strict planner it
must declare every storage call the surface makes, and a browser test must wait
for the restoration it would otherwise race. Spelled in the host's suite,
either would be a literal a producer-side rename leaves behind while the host's
test goes on passing for the wrong reason. So hosts state intent and the
producer supplies the mechanism. The calls come from `FilterStorage`'s keys and
the panel's own serializers, the committed selection is normalized the way the
owner writes it, and nothing here spells an interop identifier: the planner
owns that representation. This repo's `FilterSurfaceStorageTests` and
`FilterRestorationMarkerTests` use the helpers exactly as a host does, against
real renders, so a change the helpers missed fails here.

Dependencies: bunit, and `BgUiPrimitives_Razor.TestSupport` for the planner
whose expectations these are; its bunit and AngleSharp versions are the floor
for this repo's, and both repositories pin the same.

## Using the surface

The consumer surface is `FilterSurface`, `FilterHelp` and `NamedEntriesPanel`
(namespace `XgFilter_Razor.Components`), the filter setup's owner and its
types, the registration, and the other non-visual model types (root
`XgFilter_Razor` namespace). `FilterPanel` alone lives in
`XgFilter_Razor.Components.Internal` and is **not consumer surface** (see
Pitfalls).

### Embedding the surface in a host

```csharp
// Program.cs: the WebAssembly client, and a server that prerenders the surface.
builder.Services.AddFilterSurface();
```

```razor
@* The surface has no required parameter; see below for what a host may bind. *@
<FilterSurface Storage="@savedFiltersStorage" />
```

```csharp
// Where the host latches its source, whether or not the surface is mounted.
var token = FilterSourceToken.FromPath(folder);
filterSetup.ReportSource(token);

// Its gate, and the filter to run.
bool canRun = filterSetup.Current.IsInEffectFor(token);
FilterConfig? config = filterSetup.Current.ConfigInEffectFor(token);
DecisionFilterSet? filters = config?.Build();
```

1. **Register** `services.AddFilterSurface()` when your storage notice reads
   the shared `BrowserStorageCondition`; otherwise
   `services.AddScoped<YourRefusalHolder>()` (implementing
   `IFilterStorageRefusalSink`) and
   `services.AddFilterSurface<YourRefusalHolder>()`. Do it in every project
   that renders the surface or injects the owner. A missing sink registration
   fails when the owner is first created.
2. **Bind what is yours to say.** All optional: `Storage`, an
   `IDocumentStorage?` (null = no saved-filters section at all; the composite
   owns the store over it); `CanPersist` (default true), the host's capability
   half of the persist gate, ANDed with the store's `Ready`; and
   `PersistDisabledReason`, its wording, forwarded only while the host's half
   is false. Nothing about the selection comes back through a parameter.
3. **Report the source** with `FilterSetup.ReportSource(token)` at the moment
   you latch it — a pick, a typed path settled, a selection cleared — and at
   page initialization (an unchanged source costs nothing). Mint the token
   once, in one property, and use it for reports and reads alike.
4. **Read the gate** from `Current.IsInEffectFor(token)` (or attach and keep
   the snapshot), and the filter to run from `ConfigInEffectFor(token)`, a new
   instance each call. The ready empty selection counts: add no exception of
   your own. Re-render off `Attach`, not off component events. An observer
   records what it is given and schedules its own render; it must not throw or
   call the owner's operations.
5. **Start committed work from `Applied`** (a count, a preparation), not from
   the snapshot stream. Key derived facts by your selection, the in-effect
   config and anything else they depend on, recomputing on a snapshot that
   changes those inputs, and discard a result computed for superseded inputs.
6. **Show the storage condition** from your sink's own state, which re-renders
   off the sink's own notification; the surface has no notice for it. A sink is
   told of every refusal, with no deduplication, also after the page that
   started the call is gone.
7. **Link the scoped-CSS bundle** (see Depends on).
8. **Browser tests** wait on `FilterRestorationMarker.SettledSelector` before
   acting on the panel, and read the outcome with `Parse`. **bUnit tests** plan
   the surface's storage with `FilterSurfaceStorage` on a `BrowserStoragePlan`.

### Embedding the help

```razor
<FilterHelp HeadingLevel="4" />
```

One parameter, no callbacks. `HeadingLevel` is the level of the block's lead
heading, valid 1–5; anything else, including the unset default of zero, throws
`ArgumentOutOfRangeException` at parameters-set. Add app-level framing around
the block, never facet or chrome prose of your own. A host's data-ownership
copy links into the storage section with `FilterHelp.StorageSectionAnchorId`
and `StorageSectionHeading`, composing its own sentence around them. The other
`fh-*` ids stay `internal`: no host links to them.

### Keeping another named document

The shape of a sibling document, which no host has built yet;
`FilterSurface.razor` is the worked example. A sealed store supplies identity
and nothing else, and a preset beside the mount supplies the copy and ids
(every name below is illustrative):

```csharp
public sealed class MixSavesStore(IDocumentStorage? storage)
    : NamedDocumentStore<MixPreset, NamedMixCollection>(storage)
{
    protected override string FileName => "xg-mixes.json";
}

internal static readonly NamedEntriesSurface MixSaves = new()
{
    Title = "Saved Mixes",
    EmptyText = "No saved mixes yet.",
    NamePlaceholder = "Mix name",
    OverwriteWithNoun = "the current mix",
    NameInputId = "saveMixName",
    SaveButtonId = "saveMixButton",
    LoadedNoticeId = "savedMixLoadedNotice",
};
```

```razor
<NamedEntriesPanel Document="_store.Document" Surface="MixSaves"
                   OnLoadRequested="HandleLoadRequested"
                   OnSaveRequested="HandleSaveRequested"
                   OnSaveAsRequested="HandleSaveRequested"
                   OnDeleteRequested="HandleDeleteRequested"
                   CanPersist="canPersist"
                   PersistDisabledReason="@reason" />
```

`Document`, `Surface` and the four callbacks are `[EditorRequired]`. `TValue`
and `TSelf` carry the base collection's own constraints and are inferred from
`Document`. The host calls the store's `LoadAsync` when its source is
established and `Reset` when it goes, and mediates each request: a load pushes
the value to its editor, a save or delete goes through the store, and the
store's new `Document` comes back down.

## Pitfalls

### The setup state

- **Apply and Clear are the only commits.** Edits change the draft, which the
  owner publishes on every keystroke, which is how the gates follow, but
  nothing is applied as the user types. A host acting on "the filter changed"
  acts on the in-effect config changing, which edits move only by leaving or
  returning to an applied or empty selection.
- **Publish before persisting — never move state after an await.** Every owner
  operation that changes state does so, and publishes, before its storage call;
  a write's completion may only end the failed-restore notice. The
  obvious-looking alternative, recording the commit when its write returns, is
  the defect halheinrich/backgammon#373 records: the completion can arrive
  after a newer commit, an edit, or the end of the setup, and would put an
  older state back. A new async read that changes state needs the restoration's
  guard, the count of gestures on the draft. Count gestures, never value
  changes: a gesture that leaves the value where it was (an empty saved filter
  over the empty defaults) is still the user's choice, and a value-change guard
  lets the late read overwrite it. Publication is the separate, change-only
  question.
- **What is in effect is derived from equality, never latched.** Don't add a
  dirty flag beside it: a one-way flag never clears on edit-then-undo, and
  since Apply is itself disabled on an unchanged selection, the host's gate
  would be stranded with no recovery gesture. Equally, don't compare in two
  places: the two gates disagreeing is the defect the one reading exists to
  prevent.
- **The baseline is never persisted, and a restoration never sets it.** What is
  stored is the committed selection, and it comes back as a *choice*: the
  restoration puts it in the draft, never in the baseline, so a reload re-arms
  Apply over a non-empty restored selection (§4: your choices outlive the
  setup, your consent does not). Persisting applied-ness was considered and
  rejected in §4.
- **Readiness is the producer's — don't add an empty-filter exception anywhere
  else.** A host or a component that special-cases "no filter set" is a second
  encoding of §1's rule, and it would also have to know that a pending or
  failed restoration, an invalid draft, and no source are not that selection.
  Resolution belongs to the owner too; never rewrite the restoration outcome to
  make Run possible.
- **A mount is not a change.** A remount over an unchanged source keeps the
  draft, the baseline and the notices, and the owner publishes nothing for it.
  Don't add mount-time choreography — no reconcile, no re-arm, no notice arming
  — to either component: what used to need it lives in the owner, which
  outlives the mount. The composite's setup-change rule fires only on a new
  generation after attach.
- **The restoration notices' lifetime is the owner's — never derive it at
  mount.** "A stored selection was restored and nothing is applied" is also
  true of a navigate-back with unapplied edits, and navigation must change
  nothing (§1), so no mount-time condition can tell a fresh boot from a
  remount. The owner's lifetime is the distinguishing fact: a reload is a fresh
  owner, a remount reuses the boot's. Within a boot each notice only moves
  toward its end. A source change leaves the restored notice because a host
  whose source change crosses an unmount must see the same. This is not a
  history fact (§3): it records a pending present-tense state, and gates
  nothing but its own copy.
- **Restoration runs only from an interactive render.** `RestoreAsync` makes an
  interop call, and static rendering cannot; the panel starts it from
  `OnAfterRenderAsync`, which prerendering never runs. Don't start it from a
  constructor, `OnInitialized`, or a host's startup code.
- **The draft is the editor's state — never rebuild it from its config.**
  `FilterDraft.From(draft.ToConfig())` loses any box whose text its field
  cannot be. Hold and retain the draft; derive the config. Don't cache a
  derived value on the draft either: it is a record, and `with` would copy the
  cache into a draft it was not computed for.
- **The owner's operations stay `internal`.** `Edit`, `Stage`, `ApplyAsync`,
  `ClearAsync`, `RestoreAsync` and the notice dismissals are the surface's
  components' to call, and the snapshot's reading for them (the draft, the
  baseline, `CanApply`, the notices) is internal for the same reason. A host
  moves the setup only by reporting its source; widening an operation public
  would hand hosts a second way to change the selection or what is applied,
  uncoordinated with the panel on screen.

### Storage

- **Storage goes through `FilterStorage`, over `BrowserStorage` — never around
  it, and never with a latch.** Don't call `IJSRuntime` or `BrowserStorage`
  directly, and don't skip a call because an earlier one was refused, or report
  only the first refusal: the deduplicating is the host's occurrence owner's
  (halheinrich/backgammon#374). A refused read is "nothing stored" to its
  reader, and a refused write leaves the in-memory choice standing. A
  serialization bug, static rendering's interop failure, or a sink that throws
  is never relabelled as unavailable storage. The rest of the policy is
  BgUiPrimitives_Razor's Pitfalls, Browser storage.
- **`FilterConfig`'s JSON is the lib's.** The Razor side hands typed objects
  out of the snapshot and round-trips the selection through
  `FilterConfig.ToJson` / `TryFromJson`. A consumer serializing a config for
  transport registers no converter: each enum type carries its own, and a loose
  one registered on the consumer's options lowers the floor for itself
  (XgFilter_Lib's Pitfalls, Serialization).
- **Anything this project serializes goes through `XgFilterRazorJsonContext`,
  never a reflection-bound `JsonSerializer` overload.** The assembly is
  `IsTrimmable` and runs the trim analyzer in its own build
  (`halheinrich/backgammon#129`'s gate, this project's half), so such a call is
  IL2026 and, under `TreatWarningsAsErrors`, a build error here. That is the
  point: the row set first shipped on those overloads
  (`halheinrich/backgammon#193`), this build said nothing because it had no
  analyzer, and BgQuiz's trimmed publish is where it failed. A new value gets a
  `[JsonSerializable]` root on the context (internal; metadata-only generation,
  which `XgFilterRazorTrimPostureTests` pins alongside `IsTrimmable`) and a
  `JsonTypeInfo` overload at the call site. Types whose shape the lib owns
  still go through the lib's own document trio.
- **The storage keys are a documented surface — `internal`, and no wider.**
  `ConfigKey` / `DisclosureKey` / `MoreFiltersKey` on `FilterStorage` are
  `internal` so `FilterHelp` can render the names it tells users to look for in
  devtools from the one constant, and so the test support can state the
  surface's calls for a host's test; `InternalsVisibleTo` grants to the test
  project and the test-support assembly are the whole intended audience. They
  must never become `public`: a consumer that can see the key names will
  eventually hardcode one. Renaming a key is a user-facing copy change as well
  as a storage-format change, and a reader's existing entry silently stops
  being found, so renaming `ConfigKey` is a migration question. A display
  preference that resets once is not data: the key `DisclosureKey` replaced is
  neither read nor migrated.
- **Which rows are open never goes into `xg_filter_config`.** That key holds
  the wire-traveling `FilterConfig` DTO whose JSON shape the lib owns;
  `xg_expandedFilters` is UI preference owned by this panel. Folding visibility
  into the config blob would make a saved or loaded filter drag the rows
  around, and opening one is the user's gesture, never the config's.
- **Level-group disclosure state is deliberately unpersisted.** The facet rows
  persist because a closed row hides a whole facet's controls, and its badge
  compresses that to `set` / `N selected`. A level group's collapsed state
  hides only which levels are checked, and its badge already carries that in
  full, so remembering the open state would buy no information at the cost of
  three more localStorage keys and their interop.

### Validity and the panel

- **Never restate a lib validity rule in the panel — ask it.** The error
  bounds' rule lives in `XgFilter_Lib` and is asked through
  `FilterConfig.GetInvalidFields()` on the same config Apply commits. A local
  `if (min < 0)` here would be a second encoding of a rule `Build()` also
  enforces. The same applies to attribution: which *field* to mark is the lib's
  answer (`FilterField` membership), not a facet-wide red.
- **Never type a lib-owned spelling as a literal — render it.** A spelling the
  lib exports as a constant exists on this side only there:
  `MatchScoreToken.MoneyWithJacoby` / `MoneyWithoutJacoby` / `DoubleMatchPoint`
  / `RetiredMoney` (and `RetiredMoneyReplacements`) across the placeholder, the
  hint line, both verdicts and `FilterHelp`'s match-scores prose;
  `CheckerLocation.PlayerOff` / `OpponentOff` (rendered through their canonical
  `ToString`) for the borne-off names; `FilterStorage.ConfigKey` /
  `DisclosureKey` / `MoreFiltersKey` for the storage names;
  `FilterPanel.MoreFiltersFoldedLabel` / `MoreFiltersExpandedLabel` for the
  container toggle's two names; `FilterFacet` / enum `[Description]`s via
  `ToLabel()` for every label. A second literal agrees today and drifts
  silently the day the grammar respells a token, which is what the bare money
  token did in `halheinrich/backgammon#121`, and the reason those constants are
  exported at all. **The rule binds the tests too, with one carve-out that is
  not an exception to it**: a pin that re-types a spelling *it is following* is
  a third copy, so a pin on what the panel renders asks the owner. But a pin on
  what the panel was **ruled** to say is an oracle, and an oracle that imports
  the constant under test asserts only that the constant equals itself. The two
  ruled container labels are pinned as independent literals in
  `FilterPanelTests` for that reason (`halheinrich/backgammon#239`), as
  `RowFacets` is, and a source survey in the same suite holds every *other*
  spelling of them in the tree to the one definition on `FilterPanel`, which
  keeps the oracle from quietly becoming a second owner. Where an *absence*
  must be pinned and a substring check cannot serve (the retired spelling is a
  prefix of both live ones), ask the grammar word by word (`GetFault`).
- **The bound boxes stay text boxes, and their grammar stays the draft's.** A
  `type="number"` input "for the keyboard" brings the defect back: the browser
  withholds text it cannot read, so unfinished input arrives as a blank bound
  (`ABoundBox_IsATextBox_AskingForDigits` sweeps the panel for a number
  control). `inputmode` is what asks for digits. Don't widen the grammar to
  strip a separator, and don't narrow it with a domain rule: those are the
  lib's. The draft decides only what text is a number at all.
- **What a box shows is per mount; validity is not.** The panel's `_revealed`
  set is presentation (halheinrich/backgammon#272). Never gate anything on it,
  since the gates read the owner's verdict continuously, and never move it into
  the owner, where a second mount would inherit the first's half-typed state. A
  new verdict-bearing box needs its `onblur` (`LeaveField`) as well as the
  `FieldAttributes` splat, or its error never shows. The space a shown line
  holds (`_heldLines`) is per-mount presentation too, released only by a fold
  or the unmount: never release it on a blur, a commit, a stage or a restore,
  and never record it after a render instead of at the state change. A new
  feedback line joins `FeedbackLines`, `FieldsOf` and `IsGroupVisible`, and
  renders its box through `FeedbackLineAttributes`, or a correction removes it
  from under the user's caret.
- **A row badge's presence is computed from `GetActiveFacets()`, never by
  re-inspecting config fields or the draft text behind the controls.** The
  activation predicates are the lib's SSOT (the `FacetRules` table behind both
  `Build()` and `GetActiveFacets()`), and a badge that read its own box would
  be a second encoding of one of them. The states where the two answers differ
  are reachable by typing: whitespace-only position-pattern text is no pattern,
  a depth level list whose mode toggle is off is inert, and a player list of
  nothing but separators splits to no tokens; in each the box is non-empty and
  the facet is off. The opposite state is the lib's ruling too
  (`halheinrich/backgammon#269`): a facet is active on *presence*, so pattern
  text the grammar refuses badges `set` exactly as a malformed score token
  does. The badge reads the draft's `ActiveFacets`: the lib's answer, plus the
  facet of any bound whose text its field cannot hold (`1.5` in a move-number
  box is present and refused, the same presence ruling). The shelved facets
  have no row, and the draft has no field for them (`FilterDraft.From` drops
  them and `ToConfig` emits none), so a stale `xg_filter_config` blob carrying
  them badges nothing, and they can never become active through the panel.
- **The depth facet's clause union is derived in `Build()`, not the panel.**
  The Analysis-depth control writes only raw intent, the three per-mode pairs,
  held in the draft and bound verbatim. The panel must not pre-compute clauses
  or a mode list. The semantics, and why, are XgFilter_Lib's "Depth facet
  semantics" and its pitfall.
- **Unchecking a mode keeps its checked levels.** The draft (and the config it
  parses to) retain a group's level selections when its toggle goes off: the
  lib guarantees such a list is inert, so re-toggling the mode restores the
  user's selection instead of punishing an exploratory untoggle. Only Clear
  filters (or a hydrating restore or load) resets the level lists.
- **Clear filters touches filter values only.** It makes the draft and the
  baseline the empty selection and remembers it, and nothing else. No host
  state (the panel has no parameter or interop path to any, so BgQuiz's picked
  folder is out of reach by construction; keep it that way) and no row
  movement. Staging a saved filter likewise sets values without opening or
  closing a row. Clear commits the empty baseline on purpose (§4): editing back
  to a selection applied before the Clear needs Apply again.
- **One config out, never a filter set.** `FilterConfig.Build()` is the
  canonical adapter; handing hosts a `DecisionFilterSet` beside the config
  would be a redundant encapsulation leak. Hosts take
  `ConfigInEffectFor(token)` and call `cfg.Build()` themselves.
- **Razor silent-splat, and where it does and doesn't bite here.** Razor
  doesn't error or warn at *build* time on an unrecognized component attribute,
  so a consumer retaining a stale binding for a removed parameter compiles
  clean while its now-dead handler still looks wired. Neither `FilterPanel` nor
  `FilterSurface` declares a `[Parameter(CaptureUnmatchedValues = true)]`
  catch-all, so the renderer rejects the unmatched attribute on the first
  render
  (`InvalidOperationException: Object of type '…' does not have a property matching the name '…'`,
  pinned for the panel by `StaleParameterBinding_ThrowsAtRender`). Loud, but
  only once the page actually renders: a consumer's *build* stays green, which
  is why a producer-side parameter removal needs each consumer adapted in its
  own leg before any umbrella pointer bump. Never add a
  `CaptureUnmatchedValues` catch-all to either: it would convert that
  render-time exception back into the silent splat. The panel declares no
  parameter at all, and the composite none it requires, which
  `ThePanel_DeclaresNoParameters` and
  `TheSurface_BindsOnlyTheHostsSavedFiltersFacts` hold: a parameter added for
  the selection would be a second channel beside the owner. Neither the
  exception nor those pins proves a host's wiring is *right*, so a host
  supplements them with bUnit tests that apply a filter and assert its own gate
  actually flips. `Notice` has the same trap on a green build; see Notices.
- **`FilterPanel`'s narrowing is `.Internal` + `EditorBrowsable(Never)` — the
  strongest the toolchain allows, and the ban is absolute anyway.** The spike
  (Step 2, ruled): a true `internal` component draws CS0262, because the Razor
  generator hardcodes `public partial` on the component class, so a user
  partial cannot narrow it. Consuming it from a host is banned outright,
  **including host tests: no `FindComponent<FilterPanel>()` carve-out.** Host
  wire tests drive `FilterSurface`'s rendered DOM with real gestures instead.
  If the Razor toolchain ever allows internal components, finish the job then.
  `FilterHelp` stays public as consumer surface, and since
  halheinrich/backgammon#190 leg (D) so is `NamedEntriesPanel`: the pick list
  was only ever narrowed because it was filter-shaped, and it no longer is.

### Notices

The component's rules are BgUiPrimitives_Razor's Pitfalls, Writing a notice;
moving a box onto it is that list's "Migrating a hand-written box". A new box
is a `<Notice>`, and whether the user may close it is the umbrella's
`SPEC-notices.md` section 1 (the gate-reason test), never the box's colour.
Name the `Announcement` explicitly rather than trusting the default. Then find
the occurrence's owner before writing any state. The wrong moves this repo's
own boxes invite:

- **A `_dismissed` field beside a `<Notice>`**, in the panel for the restore
  notice, in the composite for the others. It is a second copy of a bit that
  already has a holder, and a component's copy dies on unmount while the
  occurrence may not: that is the closed restore notice coming back on a
  navigate-back.
- **A second holder for the restore notices**: a "closed by hand" bit beside
  the owner's, or a notice state anywhere but `FilterSetup`. The close gesture
  and the owning gesture are one end, the owner's.
- **Keying the write-failed notice on `Status`, on the file name, or on
  nothing.** `Status` reads `WriteFailed` after every failure alike. Unkeyed,
  it would behave today only by an accident of rendering: a `WriteFailed`
  context refuses further writes, so the next failure needs a reload, and the
  reload's render happens to unmount the notice. A ruled behaviour must not
  rest on that. The store names the failure; pass the name.
- **Treating the refusal's text as its occurrence.** Consecutive refusals carry
  identical copy. The field being assigned again is the new occurrence, which
  is why the composite clears the field on close.
- **Wrapping a bound `<Notice>` in an `@if` on the same fact.** The restore
  notice and the save refusal bind `Dismissed` to their holder's one fact and
  render nothing while it says so; an `@if` around them reads that fact a
  second time and buys nothing.
- **Trusting a green build after the component renames a parameter.** An
  attribute on a `<Notice>` tag that matches no parameter is not an error:
  Razor splats it onto the box and the real parameter takes its default. It
  happened here (`2f46cca`, `halheinrich/backgammon#248`): a green build, and
  both assertive boxes silently polite. Adapt to a producer rename by
  bare-identifier survey, never by following build errors; the suite's box pins
  refuse any attribute on a box beyond the ones this member passes.
- **Spelling the component's markup in a new test.** The role sits on
  `div.bg-notice-content` and not on the box, and a dismissible box also holds
  the close button. This repo's helpers still read that markup (see Test
  project); a new assertion uses `NoticeBox`, that library's rule
  (BgUiPrimitives_Razor's Pitfalls, Consumer tests).
- **Dropping the load confirmation's standing region.** "{name} loaded."
  renders inside `NamedEntriesPanel`'s persistent `role="status"` region, so
  its `<Notice>` is `ByEnclosingRegion`: no role and no live-region attribute
  on any element of the box. Both are pinned
  (`LoadConfirmation_LivesInAPersistentPoliteRegion`,
  `LoadConfirmation_AnnouncesNothingOfItsOwn_TheStandingRegionDoes`). The
  persistent region follows the rationale in this repo's `997ad08`, that a
  region created in the render that fills it is announced unreliably; no screen
  reader has been run against it, and `halheinrich/backgammon#249` tracks that.
  That nesting one live region in another risks a double announcement is an
  assumption too: BgUiPrimitives_Razor's "Who announces, and what the live
  region holds". Do not drop the region, move the surface's id onto the box, or
  make the region conditional. Its dismissal is the panel's `_loadedName` being
  cleared, not a bit and not a key: the name is not the occurrence, and the
  same name loads twice.

### Help and copy

- **Panel documentation has one owner: `FilterHelp`.** Consumers embed the
  component and add app-level framing only: *where the panel sits in this app
  and what pressing Apply unlocks here*, never what the controls do. A consumer
  that writes its own description of a facet's semantics, or of the filter
  rows, the badges, Apply's two disabled states or Clear filters, creates a
  second encoding of behavior that silently drifts when the rules change. If a
  host needs prose `FilterHelp` lacks, extend `FilterHelp` here. A host states
  its own data ownership and points into the storage section for the panel's
  half, with `FilterHelp.StorageSectionAnchorId` rather than a literal.
- **`FilterHelp.HeadingLevel` is `[EditorRequired]` and breaks host builds on
  purpose.** A default would be a level the component cannot know is right: the
  hard-coded `h4`/`h5` pair it replaced skipped a level under the only host
  there was, invisible on screen and visible only in a screen reader's outline
  or an audit. `RZ2012` at build makes each host state its own level; the 1–5
  range check is the belt for the paths `RZ2012` cannot see (reflection,
  dynamic rendering, a test harness), and it refuses rather than clamps.
  Sections are always lead + 1: don't add a second parameter for them. The
  `fh-*` anchor ids never move with the level (pinned).
- **The surface is told, never asks — keep it that way.** It has no parameter
  or interop path to pickers, paths, folder handles, or capabilities: the host
  mints `FilterSourceToken`s and reports them to the owner, and rules
  `CanPersist`; the producer only compares tokens and ANDs the ruling with its
  store's status. Adding any host-domain knowledge (a path parameter "just for
  the notice", a capability enum) re-couples what the seam exists to decouple.
  The same boundary governs copy: degrade-notice and refusal wording is
  producer-owned so every host degrades identically; only host-specific
  *reasons* (FS-Access phrasing) arrive as parameters.
- **The WriteFailed copy promises page-lifetime retention only.** The
  composite-owned store lives and dies with the page, so a failed edit does not
  survive navigation; "kept for this session" would over-promise (ruled). If
  the store's lifetime ever changes, the copy is part of that change.

### Saved filters and the pick list

- **The saved-filters file names are `public` — deliberately opposite to the
  storage keys.** The shared file name is user-facing copy every host must
  render (help pages, the composite's degrade notices), so one public source is
  the SSOT move, and each host renders the constant rather than spelling its
  own copy. Don't "tidy" them internal, and don't widen the storage keys public
  by the same argument in reverse.
- **The two-name migration rule: corrupt does NOT fall back** (ratified ruling,
  Step-1 review). Falling back when the canonical file is present but
  unparseable would resurrect stale legacy data while newer-but-corrupt data
  exists; instead `LoadFailed` both reports the trouble and keeps every write
  dead, so the corrupt file can never be overwritten (enforced by the store's
  status gate, not by any host's `CanPersist` courtesy). The legacy file is
  never deleted: it stays as the user's own backup. `SavedFiltersDocument`
  states the rule in full.
- **Storage adapters must wrap failures in `DocumentStorageException`.** The
  store's degrade-never-block posture rides on a *typed* catch: an adapter that
  lets its native failure type escape will fault the host's flow instead of
  degrading. Wrap everything that means "the I/O failed"; let everything that
  means "the adapter has a bug" propagate. An absent document is `null` from
  `ReadAsync`, never an exception. Where nothing awaits the call (the
  composite's reload at a setup's end) propagating means the renderer's error
  path (`DispatchExceptionAsync`), never a discarded task: a fire-and-forget
  `_ = InvokeAsync(...)` over work that can fault loses the fault
  (`ASourceChangeReload_ThatFailsAfterAnAwait_*`).
- **The save snapshot is Apply's validity gate, and must stay exactly that.**
  `FilterSetupSnapshot.TryGetSavable` refuses exactly the drafts whose values
  Apply refuses. Stricter, and save-as refuses a selection the user could
  apply; looser, and a saved document is minted from a selection Apply refuses,
  a permanent trap, since loading it reproduces the invalid state with Apply
  disabled. When a validity rule is added, it goes into the one verdict both
  read (the draft's `InvalidFields`).
- **Per-row Save snapshots the live draft, exactly as Save-as does.** Unapplied
  edits included; a row Save differs only in taking its name from the row.
  Don't "fix" it to save the last-committed config: saving what the user sees
  staged is the contract, and the confirm copy says so.
- **`NamedEntriesPanel`'s callbacks are `[EditorRequired]` on purpose.** A
  missing binding is `RZ2012` at build, the deliberate alternative to a
  silently splatted, dead affordance. A new required parameter therefore breaks
  every mount's build, which is how a mount learns of it.
- **`HandleLoadRequested` throws on a lookup miss; it must not degrade.** The
  panel raises only names it read from the very document the composite holds,
  so a miss means the two have diverged (halheinrich/backgammon#173). It is
  also the one path where the panel's own "{name} loaded." confirmation could
  stand over filters that never moved, because the panel states that
  confirmation on the request returning without throwing. Turning the throw
  into a silent no-op or a degrade notice restores that lie. This is a bug
  path, not a bad-input path: it has no user-facing wording, deliberately.
- **A new mount of `NamedEntriesPanel` supplies a surface record and nothing
  else.** The panel spells no title, no empty-list line, no placeholder, no
  prompt noun and no element id of its own, and adding one would give every
  other document that document's voice. If a mount needs wording the record
  does not carry, the record grows a member, not a parameter and not a special
  case. Presets live beside their composites, never on `NamedEntriesSurface`,
  which knows no document.
- **A mount's element ids are published surface the moment a host renders
  them.** `saveFilterName`, `saveFilterButton` and `savedFilterLoadedNotice`
  are found by selector in BgQuiz's and ExtractFromXgToCsv's suites as well as
  this repo's. Editing `FilterSurface.SavedFilters` is therefore a
  cross-repository change: nothing in this repo's build will tell you
  otherwise, and the local pin that does notice
  (`SaveAs_NewName_WritesThroughSeam`) only proves the preset and this repo's
  suite still agree.
- **A store specialization supplies identity and nothing else.** Adding any
  member to `SavedFiltersStore` — a convenience read, a domain-shaped wrapper,
  a second load path, a forwarder under an older name — puts behaviour in one
  document's store that every sibling then silently lacks, or a second name on
  one fact. The lifecycle belongs to `NamedDocumentStore`. `LegacyFileName`
  defaults to `null` on purpose: a document minted from here on declares no
  legacy name and never reads a second file, pinned by a test-only
  specialization, because nothing in `SavedFiltersStore`'s own cases can
  distinguish "the fallback ran because this document asked" from "the base
  always tries two".

### Hosts and tests

- **A host reports every source it latches, mounted or not — and `null` only
  for "no source".** The owner cannot see the host's pick; it ends a setup
  exactly when told of a different source. A host that unmounts the surface
  while no source is held must still report the next source when it latches it,
  or A → B → A would keep A's consent (§4). And a `null` later corrected to the
  real token reads as a real change and ends the setup, dropping the baseline
  and re-arming Apply when nothing changed. A host whose source is derived from
  facts it restores asynchronously reports nothing until that derivation is
  settled.
- **Refusals go to the host's sink, never through a component.** The sink is
  resolved by `AddFilterSurface<TRefusalSink>` in the owner's scope, which is
  why it is a registered type and not a delegate a page hands in. Rejecting a
  stale state update must never take the refusal with it.
- **Host-app-specific wrappers stay with the host.** A consumer that needs to
  wrap `FilterConfig` with output-format options (CSV / PPTX selection, output
  paths, etc.) defines that wrapper in the consumer. `FilterConfig` is purely
  the filter selection.
- **A test waits on the operation, never on a render.** BgUiPrimitives_Razor's
  Pitfalls, Consumer tests, says why, for a held call. Here that means waiting,
  bounded by `DefaultWaitTimeout`, on the click's task, on `RestoreAsync()`, or
  on the panel's `MountRestored`. A browser test waits on
  `FilterRestorationMarker.SettledSelector`. `MountRestored` stays honest only
  while it completes after the mount's restores; it is pinned
  (`MountRestored_CompletesOnlyOnceTheMountsRestoresHaveSettled`).
