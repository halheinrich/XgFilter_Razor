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
- **BgUiPrimitives_Razor** — two primitives. `Notice`, with its selectors
  `NoticeKind` and `NoticeAnnouncement`: the one owner of alert markup
  across the umbrella (`halheinrich/backgammon#248`, under the umbrella's
  `SPEC-notices.md`). This member's boxes render through it, so its
  `.razor` files name what each box *is* and the component owns what that
  means in classes, roles and gestures. And `BrowserStorage`, the one
  guarded browser-storage access (`halheinrich/backgammon#374`): every
  storage call the filter surface makes goes through it, under its ruled
  policy — every call made, refusals as results, only the browser's
  refusal caught. Project reference, not a package; the namespace is
  imported once, in `_Imports.razor`. The test projects also reference its
  `BgUiPrimitives_Razor.TestSupport`, for `BrowserStoragePlan`
  (`halheinrich/backgammon#377`). **It passes an obligation through to
  hosts:** the component's scoped CSS reaches a host inside the host's own
  `{HostAssembly}.styles.css` bundle, also when the host references that
  library only through this one, so a host's root document must link that
  bundle. Without it a dismissible notice's text is not a click target (its
  close button and padding still dismiss) — the safe failure, but a
  failure. That library's `INSTRUCTIONS.md` is the contract.

## Layout

Three of this repo's projects under `XgFilter_Razor.slnx` (which also carries
the dependency projects), governed by repo-root `Directory.Build.props`
(TFM, `TreatWarningsAsErrors`, XML doc generation) and
`Directory.Packages.props` (Central Package Management).

**`XgFilter_Razor/`** — the Razor class library. It ships no static assets.
Three areas:

- **Components** — `Components/`: `FilterSurface`, the one consumer-facing
  filter component, mounting the panel and the saved-filters pick list and
  wiring them to the setup's owner; `FilterPanel`, the filter form it
  mounts, a view over the owner, in the `.Internal` namespace and banned
  from host use; `NamedEntriesPanel`, the generic pick list over any
  `NamedCollection` document, which a host may mount directly;
  `FilterHelp`, the producer-owned documentation of every facet, the panel
  chrome and what the surface persists.
- **Model** — `Model/`, the non-visual types. The filter setup:
  `FilterSetup` (the app-scoped setup-state owner), `FilterSetupSnapshot`
  (what consumers see of it), `FilterRestoration` (this boot's restoration
  outcome), `FilterDraft` (the editor's state, internal),
  `FilterSourceToken` (the opaque host-minted source identity); the
  surface's storage — `FilterStorage` (the one seam over `BrowserStorage`,
  internal) and `IFilterStorageRefusalSink` (the host's owner of the
  refusal occurrence); the named-document machinery — `IDocumentStorage`
  (the host storage seam) and `DocumentStorageException` (its one failure
  type), `NamedDocumentStore` (the document lifecycle over the seam) and
  `NamedDocumentStatus` (its condition), `NamedEntriesSurface` (a pick-list
  mount's copy and element ids); and the saved-filters specialization,
  `SavedFiltersDocument` (the canonical and legacy file names and the
  migration rule) and `SavedFiltersStore` (identity only).
- **Registration** — `FilterSurfaceServiceCollectionExtensions`:
  `AddFilterSurface<TRefusalSink>()`, the one call a host makes.
- **Serialization** — `XgFilterRazorJsonContext`: the source-generated JSON
  metadata for the one value this project serializes itself, the panel's
  expanded facet rows.

**`XgFilter_Razor.Testing/`** — producer-owned test support for host suites:
`FilterSurfaceStorage` states the surface's storage calls on a host test's
`BrowserStoragePlan` in the surface's own terms, and
`FilterRestorationMarker` gives a host's browser tests the selectors for the
restoration marker. Not packable.

**`XgFilter_Razor.Tests/`** — bUnit over xUnit: one class per component
(the composite's as wire tests), the owner's contract (`FilterSetupTests`)
and §4's acceptance cases through the composite
(`FilterSetupAcceptanceTests`), the storage policy under refusal, the
restoration marker, the draft, the source token and the saved-filters
store, plus the test-support helpers pinned against a real render and the
trim-posture pins. `FakeDocumentStorage` is the recording fake over the
saved-filters storage seam the store and composite tests share;
`RecordingRefusalSink` and `RefusalNoticeHost` play a host's refusal sink
and the page that shows it; `OpponentBoundRuling` is the one oracle the help
and panel suites both pin the opponent-bound statement against (see Test
project).

## Architecture

### Thin Razor wrapper

Parallel to `BgDiag_Razor`'s relationship with `BackgammonDiagram_Lib`:
this subproject lets `XgFilter_Lib` stay free of any Blazor / Razor
dependency. All filter logic, classification, the `FilterConfig` DTO,
the `NamedFilterCollection` document, facet activation
(`GetActiveFacets`), field validity (`GetInvalidFields`) and enum labels
live in the core lib; this project binds those primitives into Blazor
components and holds the user-facing state the umbrella's
`SPEC-filtering.md` governs — the draft, what is applied, what was
restored — in one app-scoped owner.

### The setup-state owner — `FilterSetup`

`SPEC-filtering.md` §4 (halheinrich/backgammon#374) names one owner for the
state its rules govern, because the v1.12.1 post-mortem
(halheinrich/backgammon#373) found that state spread across lifetimes that
end at different times: the panel held the draft, which died on navigation;
the hosts covered the component's absence with reconcile code of their own;
and a commit's callback ran after its storage write, so it could land in a
setup that had already ended. `FilterSetup` is that owner. It is
app-scoped, so it outlives every component and every page; a full reload is
a fresh owner, which is a fresh setup and a fresh restoration (§4's last
row).

**What it holds.**

- **The setup's identity:** the source the host last reported
  (`ReportSource`), and a generation the owner advances whenever a setup
  ends.
- **The draft** (`FilterDraft`, below): the selection on screen as the user
  left it, valid or not. With it, whether the draft is **resolved**: false
  only after a failed restoration, until a valid Apply or Clear makes it
  the user's choice. Resolution is current state, not history. It travels
  with the draft through navigation and source changes, and only a fresh
  boot's failed restoration unsets it.
- **The committed baseline:** the draft last applied in this setup, or
  none.
- **This boot's restoration outcome** (`FilterRestoration`: `Pending`, then
  one of `Restored`, `NothingStored`, `Refused`, `Unreadable`), and the two
  restoration notices, whose occurrence — this boot's restore — is the
  outcome's.

Nothing is persisted but the committed selection, which is what the
restoration reads back.

**What it derives, never stores.** Whether a filter is in effect, and
which: the baseline while the draft equals it as a config, so an edit
undone back to the applied values is in effect again; or **the ready empty
selection** (§1, halheinrich/backgammon#266), a resolved, valid draft that
restricts nothing, once restoration has settled and with a source. And the
two gates' filter terms, §2's definitions, which govern:

- Apply needs a source, a settled restoration, a valid draft, and a draft
  not already in effect.
- Run needs a filter in effect for the current setup.

`FilterSetupSnapshot` is that reading, made once per published state; no
derived fact is kept anywhere as a copy that could change.

**Transitions** (§4's table):

| Event | Effect |
| --- | --- |
| the host reports its source, unchanged | nothing — how a remount and an unchanged-path return keep their setup |
| the host reports a different source (`null` included) | the setup ends: a new generation, the baseline dropped, the draft and its resolution kept |
| A → B → A, mounted or not | two endings; A's consent never returns |
| an edit, or a saved filter staged | the draft changes; the restored notice ends |
| Apply (gate open) | the draft becomes the baseline and is resolved |
| Clear (a source reported, restoration settled) | the draft and the baseline become the empty selection, resolved |
| the restoration settles | its outcome is recorded; the draft is hydrated only if no gesture touched it since the read began |
| a commit's write completes | a refusal goes to the host's sink; a write that landed ends the failed-restore notice; nothing else |

**Order.** Every accepted change takes effect, and is published, before any
storage call it makes. Apply records its baseline and publishes, then
writes; the click's task is the write's, so it completes when the browser
has answered. Because a write's completion changes no draft, baseline or
gate, a write that completes late — after a newer commit, after an edit,
after the setup ended — cannot restore anything older. There is no stale
callback left to reject. The restoration is the one read whose answer
changes state, and it changes only a draft no gesture has touched since the
read began — an edit, a staged saved filter or Clear, counted whether or
not it changed the value, so loading an empty saved filter over the empty
defaults supersedes the restore too. It is boot-scoped rather than setup-scoped: a source
change while it is pending neither strands it nor lets it overwrite a newer
draft, and its outcome is still recorded.

**Restoration** runs once per boot: `RestoreAsync` starts the one read at
the panel's first interactive render — where interop exists, never during
prerender — and every later call returns the same task. A restored valid
empty selection raises no restored notice: it is ready as it stands, and
nothing differs from a first visit (§1, Reload). A stored document that
reads but holds a field a rule outlaws restores whole, the field shown and
marked (halheinrich/backgammon#269); it is not unreadable. A failure that
is not the browser's refusal — static rendering, a disconnected circuit, a
timed-out call — is not an outcome: it propagates out of the panel's
lifecycle and leaves restoration pending.

**What consumers see.** `Attach(observer)` delivers the current snapshot
before it returns, then each real change, in order, until the handle is
disposed; `Current` answers on demand. A component or page mounted later
therefore learns the existing state without any change being manufactured,
and the owner publishes only when the state differs. A snapshot is
immutable, and every config it hands out is new, so nothing a consumer
holds — a snapshot, a config it read, a config it handed in to be staged —
can reach the owner except through its operations. A host reads its gate
source-relatively (`IsInEffectFor(token)`, `ConfigInEffectFor(token)`), so
a host that forgot to report a changed source reads its gate dark, never
another corpus's filter (§3: the need is ownership, not history).

**Storage refusals have one route, and no lifetime stands in it.**
`FilterStorage` (below) tells the host's `IFilterStorageRefusalSink` of
every refusal before the result returns. The sink is the host's own
registered holder, resolved in the owner's scope by
`AddFilterSurface<TRefusalSink>`, so a refusal that arrives after the page
that started the call has gone, or after its setup ended, still reaches the
host's occurrence owner, and a page mounted later shows it with no further
failure. Snapshot subscriptions carry state only. Disposing one stops
snapshots to that observer and cancels nothing, so rejecting a stale state
update can never discard a refusal: no subscription stands between a
storage call and the sink.

**Registration and lifetime.** `AddFilterSurface<TRefusalSink>()` registers
the owner and the storage seam scoped — one per app in WebAssembly, one per
circuit on a server — and calls `AddBrowserStorage()`, so a host cannot
register the owner without its storage. The owner's constructor is
internal: a host never makes one, so the instance a page injects and the
one the surface injects are the same by construction. Not thread-safe:
every member is called on the renderer's synchronization context, which
every component event and lifecycle method already is.

**Why this shape.**

- *One owner injected by both sides, not a holder bound as a parameter.*
  The retired `AppliedFilter` was a host-registered holder bound to the
  composite; a host could bind a second instance, and the composite's
  mediation was a second writer beside the host's reconcile code. Injection
  makes one instance per scope by construction.
- *Snapshots, not events.* Per-gesture events
  (`OnFilterConfigChanged`/`OnAppliedStateChanged`) made each host keep its
  own copy of "what is applied" in step. A snapshot carries the derived
  facts, so there is nothing to keep in step.
- *The host reports its source; the surface does not take one.* A host that
  unmounts the surface while no source is held still has to end the setup
  when it latches the next one (§4: A → B → A while unmounted). Only the
  host is there to say so, so it is the one reporter.
- *Publish before persisting, not revision checks.* §4 allows either. With
  publication first, a write's completion has nothing to update, so the
  ordering rule holds structurally rather than through a check each
  completion must remember.
- *A registered sink, not a component's event.* A component's callback
  dies with the component, and a refusal can outlive it.

### The draft — `FilterDraft`

The retained draft is the editor's state, not its parsed config: an
immutable record holding each typed box's text verbatim and every choice,
with value equality over what is on screen. Converting to `FilterConfig`
and rebuilding the editor used to lose input — an integer parse turned
`1.5` in a move-number box into no bound, so the facet went quiet and the
selection counted as the empty one. The draft keeps the text, and the
parsed config (`ToConfig`, a new instance each call), the lib's verdicts on
it and the active facets are derived on every ask. Nothing is cached: a
record's `with` copies every field, and a cached verdict would ride into a
draft it was not computed for.

The draft adds one rule beside the lib's, and it is about representation,
not the domain. A range bound that is not a number, or a move-number bound
that is not a whole number, is an `UnrepresentableFields` member. It joins
the lib's `GetInvalidFields` in `InvalidFields`, which the gates, the marks
and the save refusal read, and its facet joins `GetActiveFacets`, so the
row badges `set`: such input is a criterion the user meant, never a blank
one, and it cannot make the selection ready-empty. Blank text is no bound
and no fault; unfinished text (`1e-`, `-`) is not blank, and is
unrepresentable until it becomes a number. `RestrictsNothing` is the empty
selection: valid, with no criterion by the lib's activation predicates. The
sets are sorted, so one selection has one order and one JSON.

**What a bound reads as a number** (`halheinrich/backgammon#379`, Hal's
ruling of 2026-10-08, the four bound boxes only). Every spelling a
browser's number control accepted — a sign, digits with or without a
fraction, a bare fraction, an exponent — plus a leading `+` and a mark with
nothing after it (`5.` is 5), with either `.` or `,` as the decimal mark,
whatever the locale. There is no grouping: the mantissa holds one mark at
most, so `0.05` and `0,05` are one number, `1,234` is 1.234 and never 1234,
and a mixed, grouped or repeated mark is no number at all — nothing is
removed to make one. Space around the text is not part of it. A move
number must be whole however it is spelled (`3`, `3.0`, `3,0` are three;
`3.5`, `3,5` are not move numbers) and within the field's range. The text
stays as typed; the config, and so what is remembered, carries the number
in its own canonical form. The grammar is `FilterDraft`'s
(`BoundNumber`); the domain rules — floors, order, finiteness, `NaN` —
stay XgFilter_Lib's and are not restated: `1e999` reads as infinity, and
the lib's verdict decides it. Position Pattern's commas are its own
grammar's delimiters, and nothing here touches that box, the player names
or the match scores.

The four bound boxes are text boxes (`type="text"`, with `inputmode`
`decimal` for the error range and `numeric` for the move numbers), never
number controls. A browser's number control hands the page an empty value
for text it cannot read yet — `1e-`, `-`, a decimal comma in a locale that
writes a point — so the draft would hold "no bound" while the user is
still typing one, and the selection could read as the empty one. A text box
hands over what is typed. Whether real keystrokes, blank versus unfinished
text, navigation and correction behave so in a browser is the BgQuiz_Blazor
leg's check; bUnit sets values, it does not type.

### The surface's storage — `FilterStorage`

The one seam for every storage call the surface makes, over
`BrowserStorage`'s local area: the owner's read and writes of the
selection, the panel's reads and writes of its display preferences. It
names the three keys (`ConfigKey`, `DisclosureKey`, `MoreFiltersKey`) and
reports each refusal to the host's sink before returning the result. There
is no latch: a refused call disables nothing, and the next call is simply
made. A refused read is "nothing stored" to its reader, and a refused write
leaves the in-memory choice standing. Parsing and defaults stay with each
reader, and a failure that is not the browser's refusal is not caught here
or below. Internal: no consumer sees the keys.

### `FilterSurface` component — the consumer surface

The one component hosts embed (umbrella arc halheinrich/backgammon#63 /
halheinrich/backgammon#78 Step 2): it mounts `FilterPanel` and the
saved-filters mount of `NamedEntriesPanel`, owns the `NamedEntriesSurface`
preset that mount renders, and wires them to the owner. Saved filters load
by staging into the owner, save from the owner's draft or refuse, and
delete through the store; the composite also carries the saved-filters
degrade notices. It injects `FilterSetup` and holds no filter state. A host
binds only what is the host's to say here: an `IDocumentStorage?` adapter
(null = no saved-filters context), and its `CanPersist` capability ruling
with its host-specific `PersistDisabledReason` wording. Storage refusals
reach the host through its registered sink, not through this component.

**The setup-change rule for the saved-filters context.** The composite
attaches to the owner. The first snapshot, delivered inside `Attach`, is
the existing setup learned, and ends nothing: a remount over an unchanged
source keeps everything, which is what the owner is for. A later snapshot
with a new generation is the end of a setup. The refusal posed against the
outgoing context drops, and the saved-filters context reloads through the
seam — or resets, when there is no source. A change to the draft or the
baseline moots a stale save refusal, as any gesture on the panel always
has.

Nothing awaits that reload — the owner tells the composite inside its own
operation — so the composite observes it (`ReloadAfterSetupEndedAsync`,
`halheinrich/backgammon#374`). The storage's own failure
(`DocumentStorageException`) never reaches it: the store degrades to
`LoadFailed`, and the existing notice says so. Any other exception is an
adapter's bug, and goes to the renderer's error path as the composite's,
through `DispatchExceptionAsync`: to an enclosing `ErrorBoundary`, else to
the host's unhandled-error handling. Discarded, it would die on a task
nobody reads and leave the previous source's saved filters on screen.

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

**The composite's and the panel's boxes render through `Notice`, and each
dismissal has one holder — the owner of its occurrence**
(`halheinrich/backgammon#248`). The umbrella's `SPEC-notices.md` rules what
each box is, and so whether it dismisses; a dismissal belongs to one
occurrence, lives as long as it, and is never stored. Who holds it follows
from who knows when a new occurrence begins and whether the old one outlives
the page:

| Box | What it is | Occurrence | Holder of the dismissal |
| --- | --- | --- | --- |
| `#filterRestoredNotice` | event notice, polite | this boot's restore of a stored selection | the app-scoped `FilterSetup` — the occurrence outlives every mount of the panel, so the panel binds the component's dismissed state to the owner's snapshot and keeps no copy |
| `#filterRestoreFailedNotice` | event notice, polite | this boot's restore of a stored selection it could not read (`halheinrich/backgammon#367`: a failed restore is never silent) | the same `FilterSetup`, its second notice — the unreadable document outlives every mount too; one holder for the two outcomes of one restore. It also ends at a commit whose write lands, replacing the unreadable document; an edit leaves it, and so does a commit whose write the browser refused |
| `#filterSaveError` | error, assertive | one refused save | the composite, *as the refusal itself*: `_saveRefusalNotice` is non-null exactly while a refusal stands, so closing the box clears the field and no dismissed bit exists anywhere |
| `#savedFiltersWriteFailed` | condition notice, assertive | one failed write | the `Notice` instance, keyed by `NamedDocumentStore.LastWriteFailure` — the store names the occurrence because only it knows a write failed; store and notice both die with the composite, so the component may hold the bit |
| `#savedFiltersLoadFailed` | gate reason, polite | — | none: it cannot be closed. It stands where the saved-filters panel would and is the only thing saying why saving is off; it leaves when its cause does |

In the restore notice the user's close gesture and the owning gesture are
**one state change, not two** — the notice ends either way — because both
mean the notice is over for this boot and nothing reads the difference.
Closing a notice is never an edit: it moves no draft and no gate.
`NamedEntriesPanel`'s load confirmation is the same model in the generic
panel — see that component's section for its holder.

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
facet rows' badge ruling one tier down. The panel binds the six raw-intent
members (`IncludeEvaluations`+`EvaluationLevels`, `IncludeRollouts`+
`RolloutLevels`, `IncludeBookRollouts`+`BookRolloutLevels`) verbatim and
**never** derives the clause union — that SSOT is `FilterConfig.Build()`
(see Pitfalls).
Position type and play type are shelved for later reintroduction — their UI
groups have been hidden since the FilterPanel hide pass, while the
`XgFilter_Lib` machinery behind them (`FilterConfig.PositionTypes` /
`PlayTypes`, the filters, the enums) stays intact. The panel holds no
filter state and declares no parameter: it renders the owner's snapshot,
turns each gesture into one of the owner's operations (`Edit`, `ApplyAsync`,
`ClearAsync`), and re-renders on each snapshot the owner publishes, so a
remount shows exactly what was on screen before, unapplied edits and
invalid input included. What it keeps of its own is display state: its two
persisted preferences, the level groups' open state, and which wrong fields
it shows (below).

**Validity is the lib's ruling; the panel marks it and words it.** One
verdict is the whole of it — the draft's `InvalidFields`:
`FilterConfig.GetInvalidFields()` on the draft's parsed config must name no
field (non-negative error bounds, `min ≤ max`, `NaN` rejected, a score token
the grammar faults, and — since `halheinrich/backgammon#269` — pattern text
`BoardPattern.TryParse` refuses; the lib's rule throughout, asked of the
same parsed config Apply commits, so what the panel reds and what `Build()`
would throw on are one answer), and no box may hold text its field cannot
be at all (the draft's representation rule, above). The panel keeps no
validity rule of its own beside it, the position-pattern field's included. The error-range inputs style
themselves independently off `FilterField.ErrorMin` / `ErrorMax`
membership, so the lib's attribution rules carry straight to the screen: a
negative Max never reds a Min the user got right, while a misordered pair
blames both and leaves the user to pick an end. The message is the panel's
alone — the lib returns no strings by design — and covers both violations
in one line, worded to stay true for text that is not a number yet
(unfinished input, or a word such as `NaN`). Shown only while it
applies (the `#applyDisabledReason` idiom) rather than left in the DOM for
Bootstrap's sibling selector, which cannot reach the inputs one level down
inside the flex row — and since `halheinrich/backgammon#270` every
feedback line on the panel renders that way, the pattern's included,
because a described-by reference to a hidden element is read out
regardless; so a line's id, the one thing that reference names, exists
exactly while the line is shown. Once shown, its box keeps its place
(below). A stored selection whose bound a rule outlaws still
loads, shows its values, marks the offender, and is refused a commit —
never silently repaired, never dropped (the lib's documented posture,
pinned).

**What a box shows of its verdict is a second fact**
(`halheinrich/backgammon#272`, `SPEC-filtering.md` §1). Validity is
continuous, because the gates read it: Apply goes dark on the keystroke that
makes a value wrong. The mark and the message for a newly typed error appear
when the user leaves the box, and clear as soon as the value is corrected; a
wrong value that was not typed here — restored, staged from a saved filter,
or on screen when this panel mounts — shows at once. The panel holds which
wrong fields it shows (`_revealed`, per mount), and `ShownFields` — the
verdict as far as it is revealed, never wider — is what `FieldAttributes`
and every feedback line read. Each verdict-bearing box's `onblur` reveals
what is wrong in the group its message speaks for: a range's two bounds
share one line, and a misordered pair blames both. A draft change this
panel's own typing made keeps only the reveals still true (`Edit` marks the
panel's gestures, since the owner publishes inside the call). Any other
draft change shows every wrong value at once. So typing never makes a
message appear under the box.

**A shown message's box keeps its place** (`halheinrich/backgammon#272`,
Hal's ruling of 2026-10-08). A message leaves at the keystroke that
corrects it, and so does its announcement: the mark, `aria-invalid` and
the description's reference go, and no element carries the line's id. Its
box stays where it was, with the same words at the same size, saying
nothing — `invisible`, `aria-hidden`, and marked
`data-held-for="<the line's id>"` (`FilterPanel.HeldLineAttribute`) — so
the layout below does not move while the user types. A fresh error typed
into the box still waits for the leave, then shows in the same box. The
space is the panel's, per mount (`_heldLines`). It is held from the moment
a line is shown in a visible group, and kept through leaving a box, Apply,
Clear, a staged saved filter and a restore: a blur can move the clicked
button between press and release, and a restore can finish while the user
is typing. It is released only when the group folds away (its row closes,
or the More filters container over it) or the panel unmounts; the error
range sits outside the container, so its space lasts until unmount. The
match-score line's words differ by fault kind, so its space is per kind: a
kind it has shown and the list no longer has keeps its place in the line,
saying nothing (`_heldScoreFaults`). The hold is recorded at the state
changes that can show a line (`HoldShownLines`: a snapshot, a reveal, a
group opened by the user or by a restored preference), not after a
render: under a server host `OnAfterRender` waits for the browser's
acknowledgement, and a keystroke handled in between would find a line
shown but not yet held. bUnit pins the structure — one box, in one place,
through the ruled sequence. Whether the pixels hold still, and whether
typing keeps the browser's focus and caret, is a real-browser check,
carried into the BgQuiz_Blazor leg with the sequence invalid → leave →
return → correct → invalid again; bUnit cannot establish it.

**How an input wears the verdict is rendered from one place**
(`halheinrich/backgammon#270`). The `is-invalid` class is a colour, and a
screen reader hears nothing of it; so an invalid input also carries
`aria-invalid="true"` and an `aria-describedby` that names, beside the hint
it always names, the feedback line that says why — whose id exists exactly
while the field's verdict is shown, so the reference never names an
element that is not there, or a box that is only holding its place — and
a valid input carries neither mark and keeps its hint. The
private `FieldAttributes` on `FilterPanel` is the one place that decides
all of it, and every box on the panel that bears a verdict splats its result
(class, `aria-describedby`, and `aria-invalid` when it applies) rather
than typing a class expression of its own: a seventh field joins the splat
or fails to be announced, and the suite's per-box theory is where that
shows. Attribution stays the lib's — the field asked for is the one
`GetInvalidFields` names.

**The match-score field's verdict — two voices over one lib seam**
(`halheinrich/backgammon#121`). The score-token grammar joined the lib's
field table when the money token split by the Jacoby rule, so this field
is the second to red itself off `GetInvalidFields()` membership
(`FilterField.MatchScores`) and the second to close Apply through the
same verdict — the draft's `InvalidFields`, which the owner's `CanApply`
reads — so Apply-gating parity with the position-pattern field comes for
free from that one verdict, and is pinned. The seam has a second tier the error bounds do not need: the
field names the *list*, not what is wrong in it, so the panel asks
`MatchScoreToken.GetFault` per token and reads back a
`MatchScoreTokenFault`. It renders **one voice per distinct fault kind**
into `#matchScoreFeedback` — `Malformed` states the vocabulary and is
answered by retyping; `Retired` says the bare money token is retired and
names its replacements from `MatchScoreToken.RetiredMoneyReplacements`,
because that mistake was correct spelling under an earlier grammar and
retyping is not the remedy. A list holding both kinds gets both lines;
many tokens of one kind still get one. The lib returns no strings — the
fault is typed and wordless, the wording is the panel's, the same
division of labour as `BoardPattern.TryParse`.

Both feedback lines key on their **own** fields, never on "the invalid
set is non-empty": the set now spans two facets, and a facet-blind test
would explain the error bounds over a fault committed three sections
down (pinned both ways).

**Token spellings render from the lib's constants, never as literals.**
The placeholder's examples, the hint line, and both verdicts all render
`MatchScoreToken.MoneyWithJacoby` / `MoneyWithoutJacoby` /
`RetiredMoney` — see Pitfalls. The grammar also accepts
`MatchScoreToken.DoubleMatchPoint` as a second spelling of `1a1a`
(`halheinrich/backgammon#259`), and the field offers it everywhere it
states the vocabulary: an example of its own in the placeholder, a
spelling of `1a1a` in the hint, beside the money tokens in the malformed
verdict, and in `FilterHelp`'s match-scores prose.

**The position-pattern field presents a grammar it does not own.**
`XgFilter_Lib`'s `BoardPattern` (and its constraint types) owns the
bracket list; see that repo's Patterns section. The text is the config's
text, both ways (`halheinrich/backgammon#269`): the draft's text rides
into `FilterConfig.PositionPattern` as typed (a blank box as `null`, the
lib's "no pattern") and a restored or loaded config's text lands back in
the draft as stored, so the panel builds no `BoardPattern` for the
config's sake and a stored pattern a newer grammar rule refuses shows the
user what they wrote — marked by the lib's verdict, with Apply withheld,
never repaired and never dropped. The field's copy states
the grammar for a user typing it: the single-location token, the
`[a-b,min,max]` range token (`halheinrich/backgammon#268`) with the sign
of its bounds naming the side, the other side's checkers ignored, the
three zero-bound forms, the bar rule, and — Hal's ruling on
`halheinrich/backgammon#275` (2026-09-25), stated here and in `FilterHelp`
alike — that the opponent's "at least n" is written with the upper bound
(`[1-6,,-1]`) and a lower bound (`[1-6,-1,]`) means "at most n". The
borne-off names render from
`CheckerLocation.PlayerOff` / `OpponentOff` (their `ToString` is the
canonical spelling; the name constants themselves are `internal`). The
example tokens are literal and pinned to parse. The placeholder must be a
pattern the grammar accepts and must include a range. The invalid-entry
line names no single cause, deliberately: `TryParse` reports none, and a
list of causes (opposite-signed bounds, a backwards range, a wrong-signed
bar bound, a duplicate place) would be a second copy of the lib's rules,
incomplete the day the lib adds one. It sends the reader to the rules
stated directly above it.

**Information hierarchy** (dogfooding-driven, re-ruled in
`halheinrich/backgammon#193`, folded once more in
`halheinrich/backgammon#231`): the error-range section is first and always
visible — it is the panel's most-used control. Each of the other eight
facets is **its own collapsible row**, one tier, in `FilterFacet`
declaration order (player names, decision type, match scores, move number
range, contact type, analysis depth, dice rolls, position pattern), and all
eight rows sit **behind one further disclosure** — the container described
below — so the panel at rest is the error range and the two buttons. Every
row is an honest disclosure — a real `<button>`
(`#facetToggle_<Facet>`) carrying `aria-expanded` / `aria-controls` over an
always-rendered `#facet_<Facet>` region whose children render only while
expanded (absent from the DOM when collapsed, not styled away), named by the
facet's own `[Description]` via `ToLabel()`, with a decorative `+`/`−` glyph
marked `aria-hidden` because `aria-expanded` already carries the state. Each
section's bracketed `<small class="text-muted">` hint renders inside the
expanded body and never in the collapsed row: the heading it used to sit
beside is now the button's name, so nothing is said twice.

Every id in a row is built from the facet's enum member name — one mould,
`facetToggle_`, `facet_`, `facetBadge_`, and `facetHint_` where a hint is
referenced — which is what lets a control **name itself by reference** to its
own header rather than repeating the heading. `#positionPattern` was the
first to do that (`aria-labelledby` at the header, `aria-describedby` at
the hint), and its hint's wrapper is a plain container: a `<label for>`
whose text is the hint would have made the hint the field's *name*.
`halheinrich/backgammon#196` made the pair of references the rule for every
box on the panel the user types into (the error-range pair names itself off
its own heading's words, outside the rows), and
`halheinrich/backgammon#270` made the description carry the field's
invalid verdict as well — see the validity paragraph above. The regions
deliberately carry no `role="group"` — unruled, and the level groups do
not carry it either.

**The container over the rows** (`halheinrich/backgammon#231`) is the rows'
own idiom one tier up: a real `<button>` (`#moreFiltersToggle`) carrying
`aria-expanded` / `aria-controls` over an always-rendered `#moreFilters`
region whose children render only while expanded, with the same decorative
`aria-hidden` `+`/`−` glyph. **Folded is the resting state.** Its ids are the
rows' mould without the per-facet suffix a singleton has no use for —
`moreFiltersToggle`, `moreFilters`, `moreFiltersBadge` — and deliberately
*not* `facetToggle_` / `facet_` / `facetBadge_` shaped: those prefixes are
surveyed as "the rows", here and in both hosts, so a container answering to
one would misreport itself as a ninth row. Its badge counts the rows whose
facet is set (`RowFacets.Count(ActiveFacets.Contains)`, the rows' own lib
ruling, never a second count) and speaks only while folded; `ErrorRange` is
never in it, because it is not behind this fold.

**Its name flips with the fold** (`halheinrich/backgammon#239`): *More
filters* folded, *Fewer filters* expanded, so the control says what the next
click does rather than describing the state the reader is looking at. The two
words and the rule that picks between them have **one owner** —
`MoreFiltersFoldedLabel`, `MoreFiltersExpandedLabel` and the private
`MoreFiltersLabel` on `FilterPanel` — and nothing else spells either: the
markup renders `MoreFiltersLabel`, `FilterHelp` names the control once by
both constants and says "the fold" thereafter, and a source survey in the
suite holds each literal to that one definition. The accessible name follows
because it *is* the visible label — the button's own text, with only the
glyph held out of it.

**The container reads as the rows' parent, and that is drawn, not merely
meant** (`halheinrich/backgammon#239` — drawn flat in the `1.11.0` candidate the
user rejected, where the toggle sat at the rows' left edge, size, weight and
colour and the user read it as no collapse widget at all; nothing shipped
that way). The marks are structural: the
`#moreFilters` region indents its rows as a group with `ms-3`, the step a
row's own body already takes under its header, so the panel says "inside" one
way at both tiers; the toggle takes header weight (`fw-bold`, the card
header's `<strong>` in utility form) and **drops `btn-sm`**, which was the
rows' tier borrowed — the small scale marks the repeated tier and the control
over it is not of it; and the region takes an `mt-3` gap after the toggle
while it is open. No colour is added — `btn-link`'s blue already says
"activates". It stays a `<button>`, **never a heading element**: what outline
the panel sits in is the host's to know, which is why `FilterHelp` takes a
`HeadingLevel` and this guesses none.

**Row spacing is the body's, not the row's** (`halheinrich/backgammon#239`).
The row wrapper carries no margin, so a stack of one-line headers reads as
one list; the `mb-3` step rides on the `#facet_<Facet>` region and **only
while that region has children**, so an expanded body keeps space beneath it
without putting a blank line back between collapsed headers. The container's
own gap is conditional for the same reason: folded, it keeps its tight line
to the buttons below.

Which rows are open is the user's, persisted under its own localStorage key
(`xg_expandedFilters`, a JSON array of `FilterFacet` member names written in
row order) — never inside the config blob, and never moved by staging a
saved filter or by Clear filters. Restore is **all-or-nothing**: anything that is not an
array of names a row answers to — an unknown name, a `FilterFacet` with no
row, a numeric token, malformed JSON — restores every row collapsed rather
than salvaging part of it. The panel only ever writes row names, so anything
else is corruption, and half-honouring it would open a set the user never
chose. The key this replaced is neither read nor migrated: a display
preference that resets once is not data.

Whether the **container** is open is the user's in the same way, and it
persists under **its own** key (`xg_moreFiltersOpen`) rather than joining the
rows' set: that set's vocabulary is `FilterFacet` member names and the
container is not a facet, so a sentinel name in it would be exactly the
corruption its all-or-nothing restore exists to refuse. The value is the
literal `true` or `false` — one bit needs no serializer, which leaves the
rows' key the only value this panel serializes itself — and anything
unreadable leaves the container folded, the posture a fresh visit gets.
Staging a saved filter and Clear filters never move it, and toggling it is
navigation, not an edit: the owner hears nothing. A row the user left open is still
open when the container is next opened, whether or not the container was
folded over it in between — which is the whole reason the two preferences are
two keys.

**Row badges**: while collapsed, a row carries a badge
(`#facetBadge_<Facet>`) exactly when its facet holds an active filter. The
badge divides in two. Its **presence** is the lib's ruling, computed from the
live draft's parsed config — the config Apply commits — through
`GetActiveFacets()`, never by re-inspecting config fields or the text behind
the controls, which would be a second encoding of an activation predicate;
the draft's `ActiveFacets` adds the facet of a box whose text its field
cannot be, a criterion the user meant rather than none. Feeding from the
live draft makes it honest at rest after restore, Apply, Clear filters and a
saved filter's staging — and live mid-edit: it tracks staged values the
moment they are typed, not on Apply.
Its **words** are the panel's display concern: `set` for the facets chosen by
typing into them (player names, match scores, move number range, position
pattern), where a count of boxes filled would say nothing, and `N selected`
for the facets chosen by ticking options (decision type — one by
construction — contact type, analysis depth's checked modes, dice rolls).
Expanded, no badge renders: the controls themselves say everything it
could — the level groups' ruling one tier down.

**Apply and Clear filters are the owner's commits** — never per
keystroke. Apply makes the draft this setup's baseline (resolving it),
publishes, then writes the selection for the next visit. **Clear filters**
(the old Reset, renamed to say what it does) is the full-clear gesture: the
draft and the baseline become the empty selection, published, then
written. It touches filter values only — no host state (the panel has no
path to any) and no disclosure movement. Both are guarded in the owner on
the gate the buttons render from, so a dispatch that ignores the disabled
attribute cannot re-commit an unchanged selection, and Clear waits for
restoration to settle. A refused write never undoes either.

**What is in effect is derived, never latched.** The owner holds the draft
and the baseline, so it answers whether the selection on screen is the one
in effect, and the Apply gate is the same answer's other face; the panel
reads both from one snapshot, so they can never disagree. The baseline
compares as a config (`FilterConfig`'s value equality), which is what makes
an edit-then-undo recoverable: typing a change and typing it back lands on
the applied values, so the selection is in effect again. A one-way dirty
flag would leave a host's gate stuck with no recovery gesture, because
Apply — the only control that could clear it — is itself disabled on an
unchanged selection. Only a valid draft can match the baseline: a box the
config cannot represent parses as no criterion and would otherwise match a
baseline without one.

**The empty selection is ready without Apply** (`halheinrich/backgammon#266`,
§1): a resolved, valid draft that restricts nothing is in effect once
restoration has settled, whether nothing was ever chosen, every criterion
was cleared, or an applied filter was edited back to nothing. A pending
restoration, a failed one, an invalid draft and no source are not evidence
that the user chose no filter, and none is ready. While Apply has nothing to
do, the panel says why — a `title` plus a muted hint line, the
`NamedEntriesPanel` disabled-reason idiom, except that here the panel knows
its own reason: "no filter is set" over the empty selection, "already
applied" over a non-empty one. Neither invalid-value case gets a hint line:
the offending field's own feedback already explains it, once shown.

**Before a source is reported** (Hal's ruling of 2026-10-08): editing is
allowed, so a user can prepare a selection before picking what to filter;
Apply is off; and Clear is off and writes nothing (`CanClear` requires a
source, as `CanApply` does), since Clear commits and a commit belongs to a
setup. A disabled Apply gives no reason while there is no source or while
restoration is pending — either reason would claim a filter state that is
not yet known — and that is as built and approved.

**Restoring at mount.** `OnAfterRenderAsync(firstRender: true)` starts the
owner's restoration (once per boot; a later mount finds it settled or in
flight) and restores this mount's two display preferences — the container's
bit and the open-row set — through `FilterStorage`. A refused or absent
read leaves each preference's default. Each preference restore re-checks its
own touched flag after its await (`_moreFiltersTouched` /
`_disclosureTouched`), so a user toggle landing mid-read wins. The
restoration is awaited last, so a failure that is not a refusal surfaces in
the lifecycle. `MountRestored` completes when all of it has settled — the
observation the pending-restore pins wait on, since a late restore that
rightly yields renders nothing.

**The restoration marker** (`halheinrich/backgammon#346`). The panel's root
carries `data-filter-restoration` (`FilterPanel.RestorationAttribute`,
internal): `Pending` until this mount has applied everything it restores,
then the selection's `FilterRestoration` outcome by name, failures
included. Each mount settles its own marker, so a navigate-back reports
`Pending` until its preferences are back — the restore a click on the
container's toggle could otherwise race. It is the outcome alone and says
nothing about the gates. A host's browser test waits on it through
`XgFilter_Razor.Testing.FilterRestorationMarker`; host code reads
`FilterSetupSnapshot.Restoration` instead.

**The restored-selection notice (§4's legibility law).** A reload ends the
setup: the restoration puts the previous session's selection on screen with
nothing applied and Apply re-armed — correct by rule, and exactly what a
defect would look like, so the panel says what happened
(`#filterRestoredNotice`: restored from a previous session, not in effect
until Apply). The owner shows it when the restoration hydrated the draft
with a selection that is not the ready empty one; a restored valid empty
selection raises nothing, since it is already in effect and nothing differs
from a first visit. Nothing stored, unreadable, refused, or a read
superseded by an edit made while it was pending: nothing was put on screen,
so no claim. The notice ends at the first gesture that makes the selection
the user's own — an edit, a saved filter staged, a commit — or when the user
closes it, the same end. A source change is not a gesture and leaves it.
Because the owner holds it, a remount within a setup shows exactly what was
showing, and a closed notice stays closed (navigation changes nothing, in
both directions).

**The restore's other outcome is said too** (`halheinrich/backgammon#367`:
a failed restore is never silent). A stored document `TryFromJson` refuses
restores the defaults and says so (`#filterRestoreFailedNotice`). Nothing
stored is an ordinary first visit and gets no word, and a read the browser
refused is the storage condition, reported to the host's sink rather than
by a notice of the panel's. A document that reads restores whole, a refused
pattern included, which is the restored case above. The failure notice ends
when the user closes it or a commit's write lands, replacing the unreadable
document — whichever commit's write it was. An edit leaves it, and so does a
commit whose write the browser refused, since neither changes what is
stored. The unreadable document itself is left as it is.

### `NamedEntriesPanel` component

A persistence-agnostic pick list over **any** `NamedCollection` — generic
in the payload and the specialization, so the saved-filters document and
the queued mix-saves document are two mounts of one component
(halheinrich/backgammon#190 leg (D)). Both type arguments are inferred
from what is bound to `Document`; nothing spells them at a mount site.
The panel owns no document state and mutates nothing: every gesture is
raised as a request — `OnLoadRequested`, `OnSaveRequested` (per-row Save,
halheinrich/backgammon#38), `OnSaveAsRequested`, `OnDeleteRequested`,
each carrying the name — for the host to mediate. The host calls `With` /
`Without`, persists wherever it persists, and passes the **new**
collection instance back down through `Document`; the reference change is
also the panel's confirmation channel (it cancels pending inline confirms
and clears the typed save-as name). Selection is deliberately stateless —
the "current" value lives in whatever editor the host wires up (for
filters, the filter setup's draft), so a highlighted row would be a
second source of truth that lies. Every destructive gesture runs through
an inline confirm in the panel — a row's Save, a save-as under an
existing name, and delete; `Contains` keeps the case-insensitive name
rule in the lib. A row's Save overwrites that entry with the current
value — the same live-edit snapshot save-as takes, the name coming from
the row instead of the input — and its confirm copy says so ("Overwrite
'\<name\>' with \<the surface's noun\>?"), deliberately distinguishable
from the save-as overwrite prompt ("Overwrite
'\<name\>'?"). A row holds one confirm slot: requesting Save supersedes a
pending Delete confirm and vice versa. Hosts that cannot persist right
now (e.g. BgQuiz without its FS-Access grant) disable Save/Delete via
`CanPersist` + `PersistDisabledReason`; Load stays enabled — it is
read-only over a collection already in memory.

**What the generic cannot know arrives as one record.** `Surface`, a
required `NamedEntriesSurface`, carries the card title, the empty-list
line, the name placeholder, the row-Save prompt's noun, and the three
element ids (`NameInputId` / `SaveButtonId` / `LoadedNoticeId`). One
parameter rather than seven, so a new mount cannot half-configure itself.
A preset belongs with the composite that mounts it, not with this
component: `FilterSurface` owns `SavedFilters`, which spells
`saveFilterName`, `saveFilterButton`, `savedFilterLoadedNotice`, "Saved
Filters", "No saved filters yet.", "Filter name" and "the current
filters" — the ids and copy three repositories' suites pin. That preset
is `internal` so this repo's panel suite mounts the one true instance
rather than retyping it.

The saved-filters wiring: `OnLoadRequested` → resolve via `TryGet` →
`FilterSetup.Stage` (a miss throws — see Pitfalls);
`OnSaveRequested` / `OnSaveAsRequested` →
`FilterSetupSnapshot.TryGetSavable` → `With` → persist.

**The load confirmation is the panel's one box, and the panel holds its
dismissal as the confirmation itself** (`halheinrich/backgammon#248`).
"{name} loaded." is an event notice, so dismissible under the umbrella's
`SPEC-notices.md`, and renders through `Notice` inside the standing
`role="status"` region (the surface's `LoadedNoticeId`), which is rendered
unconditionally and stays the only announcer: the notice is
`ByEnclosingRegion` and carries no role or live-region attribute of its own.
Its occurrence is one accepted load, and the panel owns it outright —
`_loadedName` is non-null exactly while a confirmation stands — so closing
the box clears the field, as every other retiring gesture already does, and
no dismissed bit exists anywhere. Loading the same name again assigns the
field again and shows fresh: the name was never the occurrence. Every mount
gets this with no wiring, like the confirmation itself.

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
and the row badges all name a facet identically. The depth section
explains the union semantics (each checked mode admits its decisions;
more checked = more matched; nothing checked = facet off), the
inner-level distinction per mode, and the per-mode **Analysis level**
disclosure: what its `any` / `N selected` badge says without opening it,
and that levels under an unchecked mode are kept but inert. The
error-range section states whose error is filtered (SPEC-scoring §2a): a
checker play's error is measured under the ranking the application uses to
pick the best play, and a move that ranking does not score has no error, so
no error range admits it. The ranking is the host's — this component
neither owns nor chooses one — so the copy names no setting and stays true
in a host without one. The
match-scores section teaches the two rule-bearing money tokens, that
admitting either rule means listing both, and that double match
point may be written `1a1a` or as the `DoubleMatchPoint` alias; it renders those
spellings from `MatchScoreToken`'s constants and deliberately does not
offer the retired bare token — explaining a retirement belongs to the
panel, which meets it where a user still has one typed. The
position-pattern section teaches the bracket list as behavior, with worked
examples: single-location tokens and what a signed bound on a point sees,
the bars' and trays' one-sided bounds, the range token
(`halheinrich/backgammon#268`) counting one side named by its sign with the
other side ignored, the three zero-bound forms, and the opponent's "at
least n" as the upper bound and "at most n" as the lower
(`halheinrich/backgammon#275`, the ruling's own two examples). It describes the
refused forms in words and never shows one. Its markup carries the split
the pins key on: a concrete token is a `<code>` with no `<var>`, and every
one must parse through `BoardPattern.TryParse`. A schematic form marks its
placeholders with `<var>` and is exempt. The pins ask the parsed examples
for a range of each sign and each zero-bound form, not for any wording —
except the opponent-bound paragraph, which is ruled copy and is pinned as
such (see Test project).
The borne-off names render from `CheckerLocation`. The shelved
facets (Position types / Play types) are deliberately undocumented until
their UI returns.

The chrome section, **Setting and applying filters**
(`fh-using-the-panel`), sits before the facets — it is the frame a reader
needs in order to find and commit any of them. It documents the
filter rows and their badges (a filter set earlier is never quietly out of
sight), Apply as the only commit and both of its disabled
states (nothing changed, which the panel says under the button; and a
value that is not usable as a filter, which the offending box marks and
explains where it was typed), and Clear filters as the one-gesture return
to the unfiltered set that leaves the open rows alone. The
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
on their own machine and uploads nothing, and it names each `localStorage`
entry the panel writes — the applied config, the open-row preference, and
whether the fold over those rows is open — so a reader can verify them in
devtools. Every key name is **rendered from `FilterPanel`'s own constants**
(`ConfigKey` / `DisclosureKey` / `MoreFiltersKey`, `internal` for exactly
this), never written as prose literals, so the copy cannot drift from what
the panel actually writes. **The prose around that list never counts it**
(`halheinrich/backgammon#231`): the list is the count, so a sentence saying
how many entries there are is a second copy of a fact the reader can see, and
it goes stale the day a key is added — which is what happened when the
container's key arrived and left the one section saying "three" above the
list and "both" below it. The copy points at the list instead ("the entries
below", "these entries"), so the next key cannot re-break it. Scope is
exactly what `FilterPanel` persists: a sibling `xg_*` key belonging to a host
app is that host's to document. A host with its own
data-ownership copy points *into* this section rather than restating it
(BgQuiz's Help does that) — the same one-owner rule as the facet prose.
That link is a code contract, not a prose one: the section's id and its
heading text are the component's two `public` constants, and the heading
renders from the same pair the host links with (see Host surface).

### Non-visual interaction model (`Model/`)

Plain C# beside the components — namespace `XgFilter_Razor` (root), while
components stay in `XgFilter_Razor.Components`. The filter setup's types
(`FilterSetup`, `FilterSetupSnapshot`, `FilterRestoration`, `FilterDraft`,
`FilterStorage`, `IFilterStorageRefusalSink`) are described in their own
sections above. The rest:

- **`FilterSourceToken`** — opaque, equatable identity of "which source",
  minted by the host via `FromGeneration(int)` / `FromPath(string)`,
  reported to the owner, and only ever *compared* by the producer. Value
  equality over the wrapped
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
- **`NamedDocumentStore<TValue, TSelf>`** + **`NamedDocumentStatus`** — the
  lifecycle of *any* `NamedCollection` document over the host's storage
  adapter: `LoadAsync` / `SaveAsync` / `DeleteAsync` / `Reset` moving
  through Disabled / Ready / LoadFailed / WriteFailed, over a `Document`
  the panel binds. Degrade, never block: no member throws for storage
  trouble; LoadFailed preserves the file untouched and keeps saving dead;
  WriteFailed keeps the in-memory edit and stops further writes.
  Round-trip is the document's own (`NamedCollection.ToJson` /
  `TryFromJson`) — the store owns no serializer options. A null adapter =
  permanently Disabled, so an adapterless host composes with the same
  store. An internal load-version guard (the producer edition of BgQuiz's
  `PickGeneration` discipline) makes a superseded in-flight load discard
  its outcome. The two-name migration rule runs here too, but only for a
  specialization that declares a legacy name — a document with one name
  never reads a second file.
- **`SavedFiltersStore`** — the saved-filters specialization of that store,
  and the shape every sibling document takes: a sealed derived type
  supplying `FileName` and `LegacyFileName` from `SavedFiltersDocument`,
  and nothing else. No domain-named forwarder for `Document` — a second
  name for it would be a second thing to keep true. The queued mix-saves
  sibling is another such specialization, not a second copy of the
  lifecycle (umbrella arc halheinrich/backgammon#190 leg (D), superseding
  the earlier hardcoded-store ruling).
- **`IDocumentStorage`** + **`DocumentStorageException`** — the host
  seam: per-document text I/O keyed by file name (`ReadAsync(name)`
  returning null for absent, `WriteAsync(name, json)`), generalized by
  document name so a sibling document needs zero interface change — a
  generalization `NamedDocumentStore` has since taken up, and the queued
  mix-saves document will ride without touching it. Adapters wrap every
  native failure in `DocumentStorageException` — the one type the store
  catches (see Pitfalls). **The seam knows no document kind**, which is
  why neither name says "filter" or "named"
  (halheinrich/backgammon#190 leg (D)): it moves text for a file name,
  and *which* document that is belongs to the store above it. Adapters
  follow the same rule and are named for where they read and write —
  `HttpDocumentStorage`, `PickedFolderDocumentStorage`, this repo's
  `FakeDocumentStorage`.
- **`SavedFiltersDocument`** — the document identity: public constants
  `FileName` (`xg-filters.json`) and `LegacyFileName`
  (`bgquiz-filters.json`), which `SavedFiltersStore` hands to the base,
  plus the stated two-name migration rule they imply — read canonical
  first, fall back to legacy only when canonical is *absent*, write only
  canonical, never delete the legacy file (see Pitfalls for the
  corrupt-file rationale).

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

bUnit + xUnit, targets .NET 10. Each suite registers the surface as a host
does — `Services.AddFilterSurface<RecordingRefusalSink>()` with the sink
registered beside it — and a `BunitContext` is one app boot, so one owner
and one restoration per test. **Storage is planned where it is the
subject, and incidental elsewhere.** A suite whose subject is storage, the
gates or the acceptance cases puts a `BrowserStoragePlan` on the test's
runtime (`halheinrich/backgammon#377`), declares every call in the
surface's own terms (`XgFilter_Razor.Testing`'s `FilterSurfaceStorage`, or
the keys directly where a key's spelling is the subject), and ends with
`Verify`. The pending and ordering cases hold the calls they are about and
wait, bounded by `DefaultWaitTimeout`, on the operation itself — the
click's task, `RestoreAsync`, or the panel's `MountRestored` — never on a
render or a timing sleep. A suite where storage is incidental runs
`JSRuntimeMode.Loose`, which answers every read "nothing stored" and every
write "landed" through the real `BrowserStorage`. No test here spells an
interop identifier.

**The acceptance cases** `SPEC-filtering.md` §4 lists are
`FilterSetupAcceptanceTests`, each through the composite with the host's
side played as a host plays it: report the source, read the gate from the
owner's snapshot. `FilterSetupTests` pins the owner's own contract,
`FilterSurfaceStorageUnavailableTests` the storage policy under refusal,
and `FilterRestorationMarkerTests` the marker. A navigation is
`DisposeComponentsAsync()` followed by a fresh render in the same context;
a reload is a fresh context.

**Pin posture: structure and wiring, and copy only where it was ruled.** These suites assert
that a component renders the identity, value, or spelling its source of
truth names — the anchor ids, the storage keys, the score-token
constants — reached through test-only `InternalsVisibleTo` where the
source is `internal`. Wording itself is not pinned here: an
independent-literal oracle for "the user can read X" would be a second
copy of the very text under test, so that oracle lives in the consumer's
e2e suite. The corollary is the no-literal-spellings rule in Pitfalls,
which binds these pins as much as the markup. **Ruled copy is the one
exception**, and it is the carve-out in that rule, not a departure from it:
where a ruling or a spec requires a surface to make a statement, or to stop
making one, the pin is an oracle for the ruling and holds the copy to
independent literals, naming the ruling it enforces. A statement ruled
for more than one surface is pinned in each against one shared oracle, so
the surfaces cannot be held to different rulings — `OpponentBoundRuling`
(`halheinrich/backgammon#275`) is the shape, and it also asks the grammar
that each of the ruling's examples still reads as the ruling says.

### Test-support assembly (`XgFilter_Razor.Testing`)

Producer-owned helpers for **host** test suites, referenced by their test
projects only — `IsPackable=false`, and no app-graph project may reference
it. Two members:

- **`FilterSurfaceStorage`** — the surface's storage calls, stated on a
  host test's `BrowserStoragePlan` in the surface's own terms: this boot's
  restoration (`ExpectFilterRestore` with a stored selection or an outcome,
  `ExpectHeldFilterRestore` and `RestoreAnswer` to hold and release it),
  each mount's two display-preference reads (`ExpectFilterPanelMount`,
  `ExpectFilterPanelMountRefused`), each commit's write
  (`ExpectFilterCommit`, `ExpectHeldFilterCommit`), and the toggles'
  writes (`ExpectFilterFoldToggle`, `ExpectFilterRowsToggle`).
- **`FilterRestorationMarker`** — the restoration marker's selectors and
  reading for a host's browser tests: `SettledSelector` to wait on,
  `Selector(outcome)`, `AttributeName` and `Parse`.

Why it exists: the surface's keys, and the marker's attribute, are
deliberately not consumer surface, yet a host's test needs both. Under the
strict planner it must declare every storage call the surface makes, and a
browser test must wait for the restoration it would otherwise race. Spelled
in the host's suite, either would be a literal a producer-side rename leaves
behind while the host's test goes on passing for the wrong reason. So hosts
state intent and the producer supplies the mechanism. The calls come from
`FilterStorage`'s keys and the panel's own serializers, the committed
selection is normalized the way the owner writes it, and nothing here spells
an interop identifier: the planner owns that representation. This repo's
`FilterSurfaceStorageTests` and `FilterRestorationMarkerTests` use the
helpers exactly as a host does, against real renders, so a change the
helpers missed fails here.

Dependencies: bunit, and `BgUiPrimitives_Razor.TestSupport` for the planner
whose expectations these are; its bunit and AngleSharp versions are the
floor for this repo's, and both repositories pin the same.

## Public API

The consumer surface is `FilterSurface` + `FilterHelp` +
`NamedEntriesPanel` (namespace `XgFilter_Razor.Components`), the filter
setup's owner and its types, the registration, and the other non-visual
model types (root `XgFilter_Razor` namespace). `FilterPanel` alone lives
in `XgFilter_Razor.Components.Internal` with `[EditorBrowsable(Never)]` and
is **not consumer surface** — consuming it from a host is banned outright,
host tests included (see Pitfalls for the narrowing record). Its contract
below remains documented because `FilterSurface` builds on it and this
repo's tests pin it. `NamedEntriesPanel` left that narrowing behind in
halheinrich/backgammon#190 leg (D): it is the reusable piece the arc exists
for, and the queued mix-saves document mounts it from BgQuiz.

### The filter setup

```csharp
public sealed class FilterSetup                       // app-scoped; registered by AddFilterSurface
{
    public FilterSetupSnapshot Current { get; }
    public IDisposable Attach(Action<FilterSetupSnapshot> observer);
    public void ReportSource(FilterSourceToken? source);
}

public sealed class FilterSetupSnapshot               // immutable
{
    public FilterSourceToken? Source { get; }
    public int Generation { get; }
    public FilterRestoration Restoration { get; }
    public bool IsInEffectFor(FilterSourceToken source);
    public FilterConfig? ConfigInEffectFor(FilterSourceToken source);   // a new config each call
}

public enum FilterRestoration { Pending, Restored, NothingStored, Refused, Unreadable }

public interface IFilterStorageRefusalSink
{
    void ReportRefused(JSException refusal);
}

public static class FilterSurfaceServiceCollectionExtensions
{
    public static IServiceCollection AddFilterSurface<TRefusalSink>(this IServiceCollection services)
        where TRefusalSink : class, IFilterStorageRefusalSink;
}
```

Contracts:

- **`Attach`** calls the observer with `Current` before it returns, then
  with each real change, synchronously and in order, until the handle is
  disposed (twice is harmless). Disposing stops snapshots and cancels
  nothing. An observer records what it is given and schedules its own
  render; it must not throw or call the owner's operations.
- **`ReportSource`**: a different source — `null` included — ends the
  setup (a new `Generation`, the applied baseline dropped, the draft kept);
  the same source again does nothing.
- **`IsInEffectFor(token)`** is the filter half of a Run gate: true when
  `token` is the setup's source and a filter is in effect — the applied
  selection, or the ready empty selection, which needs no Apply. The host
  adds no exception of its own. **`ConfigInEffectFor(token)`** is that
  filter, a new instance each call, or `null`.
- **`Restoration`** is `Pending` until the boot's one restoration read
  settles, then exactly one outcome. It is a diagnostic, not a gate.
- **`ReportRefused`** is called on the renderer's synchronization context
  once per refused storage call — every refusal, no deduplication, also
  after the page that started the call is gone. A page showing the
  condition re-renders off the sink's own notification.
- **`AddFilterSurface<TRefusalSink>`** registers the owner and the
  surface's storage scoped and calls `AddBrowserStorage()`; the host
  registers `TRefusalSink` itself, at app scope. A second call adds
  nothing, and the first call's sink stands. A missing sink registration
  fails when the owner is first created.
- **Everything else on these types is internal**: the operations the
  surface's components drive (`Edit`, `Stage`, `ApplyAsync`, `ClearAsync`,
  `RestoreAsync`, the notice dismissals) and the snapshot's reading for
  them (the draft, the baseline, `CanApply`, the notices). No host moves
  the setup but by reporting its source.

### `FilterSurface`

Parameters, none required:

- `IDocumentStorage? Storage` — the saved-filters seam; null = no
  saved-filters section at all. The composite owns the store over it.
- `bool CanPersist` (default true) + `string? PersistDisabledReason` — the
  host's capability half of the persist gate and its wording; ANDed with
  the store's `Ready` before reaching the panel. The reason is forwarded
  only while the host's half is false (WriteFailed explains itself with
  its own notice).

It injects `FilterSetup`. Everything about the selection is the owner's,
read by the host from the owner, and storage refusals reach the host's
sink; nothing comes back through a parameter.

### Host contract

1. **Register** `services.AddScoped<YourRefusalHolder>()` (implementing
   `IFilterStorageRefusalSink`) and `services.AddFilterSurface<YourRefusalHolder>()`
   in every project that renders the surface or injects the owner — the
   WebAssembly client, and a server that prerenders it.
2. **Report the source** with `FilterSetup.ReportSource(token)` at the
   moment you latch it — a pick, a typed path settled, a selection
   cleared — whether or not the surface is mounted, and at page
   initialization (an unchanged source costs nothing). Mint the token once,
   in one property, and use it for reports and reads alike. Report `null`
   only for "no source", never for "not yet known".
3. **Read the gate** from `Current.IsInEffectFor(token)` (or attach and keep
   the snapshot), and the filter to run from `ConfigInEffectFor(token)`.
   Re-render off `Attach`, not off component events.
4. **Key derived facts** (a match count) by your selection, the in-effect
   config and anything else they depend on, recomputing on a snapshot that
   changes those inputs. Discard a result computed for superseded inputs.
5. **Show the storage condition** from your sink's own state; the surface
   has no notice for it.
6. **Browser tests** wait on `FilterRestorationMarker.SettledSelector`
   before acting on the panel, and read the outcome with `Parse`. **bUnit
   tests** plan the surface's storage with `FilterSurfaceStorage` on a
   `BrowserStoragePlan`.

### Migrating a host from the `AppliedFilter` surface (v1.12.2)

For BgQuiz_Blazor's and ExtractFromXgToCsv's legs; this subsection goes
when both have migrated.

- **Removed types:** `AppliedFilter` (read the snapshot instead) and
  `FilterRestoreNotice` (the owner holds both restore notices; delete its
  registration and binding).
- **Removed `FilterSurface` parameters:** `AppliedFilter`, `RestoreNotice`,
  `Source`, `OnFilterConfigChanged`, `OnAppliedStateChanged`,
  `OnStorageUnavailable`. A host's build fails first wherever it declared
  one of the removed types. A binding to a removed parameter that survives
  that compiles, and throws `InvalidOperationException` at first render,
  because the composite declares no catch-all: adapt every mount site, not
  only the ones the compiler names.
- **Source:** where you bound `Source="token"`, call `ReportSource(token)`
  where the token changes instead. Delete end-of-setup choreography that
  existed to cover an unmounted surface (`AppliedFilter.Clear()` at a pick,
  the mount gate that withheld the surface until a restored path settled,
  any copy-back of a resumed selection): reporting the source is the whole
  of it.
- **Gates:** `AppliedFilter.ConfigFor(token)` becomes
  `Current.ConfigInEffectFor(token)`. The empty selection is now in effect
  without Apply (halheinrich/backgammon#266), so delete any host-side
  "Apply required" or empty-filter exception, and any copy that says Apply
  is needed for the empty filter.
- **Events:** `OnFilterConfigChanged` / `OnAppliedStateChanged` handlers
  become a snapshot observer. A count started on commit starts on the
  in-effect config changing instead, and is reused on a remount when its
  inputs match.
- **Storage refusals:** `OnStorageUnavailable` becomes the registered
  sink; it is told of every refusal, including ones after the page has
  gone.
- **Tests:** `FilterPanelTestState.SeedStoredSelection` becomes
  `plan.ExpectFilterRestore(config)` on a `BrowserStoragePlan`, beside
  `ExpectFilterPanelMount()` per mount and `ExpectFilterCommit(...)` per
  commit. Delete any mirrored storage key, and wait on the restoration
  marker in browser tests.
- **Copy:** FilterHelp's storage section is conditional now
  (halheinrich/backgammon#371, wording approved by Hal on 2026-10-08): the
  filters still work "for this visit, including navigation within the app"
  when the browser refuses to keep them. A host's own data-ownership copy
  that promised remembering should say the same.
- **Before a source:** Clear is off and writes nothing now, as Apply is; a
  host test that cleared before reporting its source reports it first.

### `FilterPanel` (`.Internal` — via `FilterSurface` only)

No parameters: it injects `FilterSetup` and `FilterStorage`, renders the
owner's snapshot, and sends each gesture to the owner. A host never mounts
it. Its internal members:

- `MountRestored` — completes when this mount's restores (its two
  preferences and the boot's restoration) have settled.
- `RestorationAttribute` (`data-filter-restoration`) — the marker's
  attribute, read through `XgFilter_Razor.Testing.FilterRestorationMarker`.
- `SerializeFold(bool)` / `SerializeOpenRows(IEnumerable<FilterFacet>)` —
  the two preferences' stored spellings, for the test support's toggle
  expectations.
- `MoreFiltersFoldedLabel` (*More filters*) / `MoreFiltersExpandedLabel`
  (*Fewer filters*) — the container toggle's two names, rendered into the
  markup through the private `MoreFiltersLabel` rule and into the help's
  one naming sentence. The rule stays `private`: only the panel renders
  *a* label, and the help names both and leaves the choosing alone.

The storage keys are `FilterStorage`'s `internal const string`s —
`ConfigKey` (`xg_filter_config`), `DisclosureKey` (`xg_expandedFilters`),
`MoreFiltersKey` (`xg_moreFiltersOpen`) — and **`internal` is the whole
point of them**: `FilterHelp` renders them as the names a reader verifies in
devtools, and the test support arranges them, so the copy, the arrangement
and what the surface writes have one source. Not `public` — no consumer may
see, let alone depend on, the surface's storage keys or name the panel's
chrome.

### `NamedEntriesPanel<TValue, TSelf>`

Consumer surface. `TValue : IJsonDocument<TValue>` and
`TSelf : NamedCollection<TValue, TSelf>, INamedCollectionSpecialization<TValue, TSelf>`
— the base collection's own constraints — and both are inferred from
`Document`, so a mount names neither.

Parameters (all callbacks `[EditorRequired]`, as are `Document` and
`Surface`):

- `NamedCollection<TValue, TSelf> Document` — the immutable document to
  render; the host passes each new instance back down after mediating a
  change.
- `NamedEntriesSurface Surface` — this mount's copy and element ids; see
  the model types below.
- `EventCallback<string> OnLoadRequested` / `OnSaveRequested` /
  `OnSaveAsRequested` / `OnDeleteRequested` — request-only gestures
  carrying the entry name; the panel mutates nothing. `OnSaveRequested`
  is the per-row Save (halheinrich/backgammon#38): overwrite that entry
  with the host editor's current live state — the host mediates it
  exactly as save-as (for filters, `TryGetSavable` → `With` →
  persist), the name coming from the row.
- `bool CanPersist` (default `true`) + `string? PersistDisabledReason` —
  gate Save/Save-as/Delete as one switch when the host cannot persist;
  Load stays enabled.

### Non-visual model types

- `FilterSourceToken` — `readonly record struct`; factories
  `FromGeneration(int)` / `FromPath(string)`; value-equal, with
  `FromPath` normalizing path identity itself (case, trailing separator).
- `NamedEntriesSurface` — sealed immutable record; seven `required init`
  members: `Title`, `EmptyText`, `NamePlaceholder`, `OverwriteWithNoun`,
  `NameInputId`, `SaveButtonId`, `LoadedNoticeId`. Two guards, both at
  construction because neither failure is discoverable later: `required`
  stops a preset being half-built, and every member rejects null, blank
  and untrimmed at `init` (`ArgumentException` naming the member) so it
  cannot be built wrong. A `with` expression re-validates whatever it
  changes. A preset lives beside the composite that mounts it, never on
  this type — the record is the shape and knows no document.
- `NamedDocumentStore<TValue, TSelf>` — ctor
  `(IDocumentStorage? storage)` (`protected`); `TSelf Document`,
  `NamedDocumentStatus Status`, `string? LoadFailedFileName` (non-null
  exactly while `LoadFailed`, naming the actual file — canonical or
  legacy — the failed load was about, so degrade copy never guesses),
  `object? LastWriteFailure` (the identity of the most recent failed
  write: a distinct value per failure, `null` until one fails, never
  cleared. Opaque — compared, never inspected; it names *which* failure
  and leaves whether one stands to `Status`. It is what a write-failed
  notice takes as its occurrence key),
  `Task LoadAsync()`, `Task SaveAsync(string, TValue)`,
  `Task DeleteAsync(string)`, `void Reset()`. Never throws for storage
  trouble; mutating members no-op unless `Status == Ready`. A
  specialization overrides `protected abstract string FileName` and, only
  where an older name is superseded, `protected virtual string?
  LegacyFileName` (default `null` = one name, one read).
- `SavedFiltersStore` — that store over `FilterConfig` /
  `NamedFilterCollection`; public ctor `(IDocumentStorage? storage)`
  and the two identity overrides, nothing more. Read the document through
  the inherited `Document`.
- `IDocumentStorage` — `Task<string?> ReadAsync(string fileName)`
  (null = absent), `Task WriteAsync(string fileName, string json)`;
  failures signalled as `DocumentStorageException` only.
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

`FilterStorage`'s key constants are `internal`, not public: the copy
naming them lives here, in the producer, so a consumer never sees or
depends on the surface's key names. Test-only
`InternalsVisibleTo("XgFilter_Razor.Tests")` lets the wiring test pin the
rendered names to those constants, and
`InternalsVisibleTo("XgFilter_Razor.Testing")` lets the test-support
assembly state the surface's calls on a host suite's behalf — both grants
are producer-side, so neither widens what consumers can see.

## Pitfalls

- **Storage goes through `FilterStorage`, over `BrowserStorage` — never
  around it, and never with a latch.** Every call the surface makes — the
  owner's selection, the panel's preferences — is a `FilterStorage` call,
  which makes the call and tells the host's sink of a refusal before
  returning. Don't call `IJSRuntime` or `BrowserStorage` directly, and
  don't skip a call because an earlier one was refused, or report only the
  first refusal: the ruled policy is every requested call made and every
  refusal told, with the deduplicating done by the host's occurrence owner
  (halheinrich/backgammon#374). A refused read is "nothing stored" to its
  reader, and a refused write leaves the in-memory choice standing. Only
  the browser's refusal is caught, below in `BrowserStorage`: a
  serialization bug, static rendering's interop failure, or a sink that
  throws is never relabelled as unavailable storage.
- **JSON round-trip needs `JsonStringEnumConverter`.** Consumers that
  serialize `FilterConfig` for HTTP transport must register
  `JsonStringEnumConverter` (e.g. on `JsonSerializerOptions.Converters`
  or via `[JsonConverter]` attributes) so `DecisionType`, `PositionTypes`,
  and `PlayTypes` serialize as their string member names rather than
  underlying integer values. This is the lib's stated contract — see
  `FilterConfig`'s type-level remarks. The Razor side itself never
  serializes for transport; it hands typed C# objects out of the snapshot.
  The converter requirement applies to the consumer's HTTP plumbing.
- **Apply and Clear are the only commits.** Edits change the draft, which
  the owner publishes on every keystroke — that is how the gates follow —
  but nothing is applied as the user types: the contract is "user thinks,
  then commits via Apply". A host acting on "the filter changed" acts on
  the in-effect config changing, which edits move only by leaving or
  returning to an applied or empty selection.
- **Publish before persisting — never move state after an await.** Every
  owner operation that changes state does so, and publishes, before its
  storage call; a write's completion may only end the failed-restore
  notice. The obvious-looking alternative — record the commit when its
  write returns — is the v1.12.1 defect (halheinrich/backgammon#373): the
  completion can arrive after a newer commit, an edit, or the end of the
  setup, and would put an older state back. The restoration is the one
  read whose answer changes state, and it is guarded by the count of
  gestures on the draft; a new async read that changes state needs the
  same guard. Count gestures, never value changes: a gesture that leaves
  the value where it was (an empty saved filter over the empty defaults)
  is still the user's choice, and a value-change guard lets the late read
  overwrite it. Publication is the separate, change-only question.
- **What is in effect is derived from equality, never latched.** The Apply
  gate and the host's gate are one snapshot's reading of the draft against
  the baseline (and of the ready empty selection). Don't add a dirty flag
  beside it: a one-way flag never clears on edit-then-undo, and since Apply
  is itself disabled on an unchanged selection, the host's gate would be
  stranded with no recovery gesture. Equally, don't compare in two places
  — the two gates disagreeing is the defect the one reading exists to
  prevent.
- **The baseline is never persisted, and a restoration never sets it.**
  What is stored is the committed selection, and it comes back as a
  *choice*: the restoration puts it in the draft, never in the baseline,
  so a reload re-arms Apply over a non-empty restored selection (§4: your
  choices outlive the setup, your consent does not). Persisting applied-ness
  was considered and rejected in §4.
- **Readiness is the producer's — don't add an empty-filter exception
  anywhere else.** The ready empty selection is in effect without Apply
  (halheinrich/backgammon#266), and `IsInEffectFor` already says so. A host
  or a component that special-cases "no filter set" is a second encoding
  of §1's rule, and it would also have to know that a pending or failed
  restoration, an invalid draft, and no source are not that selection.
  Resolution belongs to the owner too; never rewrite the restoration
  outcome to make Run possible.
- **The depth facet's clause union is derived in `Build()`, not the panel.**
  The Analysis-depth control writes only raw intent — three per-mode pairs,
  each a toggle plus its own checked-level set — held in the draft. The
  mapping to
  `AnalysisDepthFilter` clauses (one clause per enabled toggle
  carrying its own level list, empty list = any level, all toggles off =
  facet off, inert level lists) lives **only** in `FilterConfig.Build()` — it
  is the single source of truth, and XgFilter_Lib's Pitfalls flag re-encoding
  it in a consumer as a silent-drift hazard. The panel must not pre-compute
  clauses or a mode list; it binds the six members verbatim and lets
  `Build()` own the semantics.
- **Level-group disclosure state is deliberately unpersisted.** The facet
  rows persist under `xg_expandedFilters` because which rows a user works in
  is a chosen layout worth remembering across sessions, and a closed row
  hides a whole facet's controls — the badge compresses that to `set` /
  `N selected`, not to what is in them. A level group's collapsed state hides
  only one thing — which levels are checked — and its badge ("any" /
  "N selected") already carries that in full, so remembering the open state
  would buy no information at the cost of three more localStorage keys and
  their interop. Each group therefore mounts
  collapsed, and toggling it is navigation: the owner hears nothing, and
  nothing is written.
- **Unchecking a mode keeps its checked levels.** The draft (and the
  config it parses to) retain a group's level selections when its toggle goes
  off: the lib guarantees a level list whose toggle is off is inert — no
  activation, no constraint, no validation — so re-toggling the mode
  restores the user's selection instead of punishing an exploratory
  untoggle. Only Clear filters (or a hydrating restore/load) resets the
  level lists.
- **One config out, never a filter set.** `FilterConfig.Build()` is the
  canonical `FilterConfig` → `DecisionFilterSet` adapter; handing hosts a
  `DecisionFilterSet` beside the config would be a redundant
  encapsulation leak. Hosts take `ConfigInEffectFor(token)` and call
  `cfg.Build()` themselves.
- **Razor silent-splat, and where it does and doesn't bite here.** Razor
  doesn't error or warn at *build* time on an unrecognized component
  attribute — it emits it like any other, so a consumer retaining a stale
  binding for a removed parameter compiles clean while its now-dead
  handler still looks wired. Where it lands after that depends on the
  component. Neither `FilterPanel` nor `FilterSurface` declares a
  `[Parameter(CaptureUnmatchedValues = true)]` catch-all, so the renderer
  rejects the unmatched attribute on the first render —
  `InvalidOperationException: Object of type '…' does not have a property
  matching the name '…'` (pinned for the panel by
  `StaleParameterBinding_ThrowsAtRender`, with `OnAppliedStateChanged`,
  retired with the owner). Loud, but only once the page actually renders:
  a consumer's *build* stays green, which is exactly why a producer-side
  parameter removal needs each consumer adapted in its own leg before any
  umbrella pointer bump. Never add a `CaptureUnmatchedValues` catch-all to
  either — it would convert that render-time exception back into the
  silent splat the whole discipline exists to avoid. The panel declares no
  parameter at all, and the composite none it requires, which
  `ThePanel_DeclaresNoParameters` and
  `TheSurface_BindsOnlyTheHostsSavedFiltersFacts` hold: a parameter added
  for the selection would be a second channel beside the owner. Neither the
  exception nor those pins proves a host's wiring is *right*, so a host
  supplements them with bUnit tests that apply a filter and assert its own
  gate actually flips.
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
  literal. **It covers the chrome as well as the facets**: the filter
  rows and their badges, Apply's two disabled states, Clear
  filters. Those are the panel's behavior, not the app's, so a host
  describing them is the same drift hazard one tier up — app-level
  framing means *where the panel sits in this app and what pressing Apply
  unlocks here*, never what the controls do.
- **The storage keys are a documented surface now — `internal`, and no
  wider.** `ConfigKey` / `DisclosureKey` / `MoreFiltersKey` on
  `FilterStorage` are `internal` so `FilterHelp` can render the names it
  tells users to look for in devtools from the one constant, and so the
  test support can state the surface's calls for a host's test. Two
  consequences. (1) Renaming a
  key is a user-facing copy change as well as a storage-format change:
  the name in the help text follows automatically, but a reader's
  existing entry silently stops being found, so treat a rename as a
  migration question, not a refactor. (2) They must never become
  `public`. A consumer that can see the surface's key names will
  eventually hardcode one, and the point of siting the copy and the test
  support here is that no consumer needs to know them. The test project
  and the test-support assembly reach them through `InternalsVisibleTo` in
  the csproj — those grants are the whole intended audience.
- **Host-app-specific wrappers stay with the host.** A consumer that
  needs to wrap `FilterConfig` with output-format options (CSV / PPTX
  selection, output paths, etc.) defines that wrapper in the consumer,
  not here. `FilterConfig` is purely the filter selection.
- **Which rows are open never goes into `xg_filter_config`.** The panel
  persists under two keys with different owners: `xg_filter_config` is
  the wire-traveling `FilterConfig` DTO whose JSON shape the lib owns — so
  the panel never touches a serializer for it — and `xg_expandedFilters` is
  UI preference owned by this panel, which is the one value it *does*
  serialize itself, because the shape is nobody else's. Folding visibility
  into the config blob would make a saved or loaded filter drag the rows
  around — opening one is the user's gesture, never the config's.
- **Anything this project serializes goes through `XgFilterRazorJsonContext`,
  never a reflection-bound `JsonSerializer` overload.** The assembly is
  `IsTrimmable` and runs the trim analyzer in its own build
  (`halheinrich/backgammon#129`'s gate, this project's half), so a
  `JsonSerializer.Serialize<T>(value)` / `Deserialize<T>(json)` call is
  IL2026 and, under `TreatWarningsAsErrors`, a build error here. That is the
  point: the row set first shipped on those overloads
  (`halheinrich/backgammon#193`), this build said nothing because it had no
  analyzer, and BgQuiz's trimmed publish is where it failed. A new value
  gets a `[JsonSerializable]` root on the context (internal; metadata-only
  generation, which `XgFilterRazorTrimPostureTests` pins alongside
  `IsTrimmable`) and a `JsonTypeInfo` overload at the call site. Types whose shape the lib owns
  still go through the lib's own document trio, never through this context.
- **A row badge's presence is computed from `GetActiveFacets()`, never by
  re-inspecting config fields or the draft text behind the controls.** The
  activation predicates are the lib's SSOT (the `FacetRules` table behind
  both `Build()` and `GetActiveFacets()` — the `DecisionFilterSet.IsEmpty`
  ruling), and a badge that read its own box would be a second encoding
  of one of them. The states where the two answers differ are real and
  reachable by typing, which is what makes this a live hazard rather than a
  style rule: whitespace-only position-pattern text is no pattern, a depth
  level list whose mode toggle is off is inert by the lib's guarantee, and
  a player list of nothing but separators splits to no tokens — in each the
  box is non-empty and the facet is off. The opposite state is the lib's
  ruling too (`halheinrich/backgammon#269`): a facet is active on
  *presence*, so pattern text the grammar refuses badges `set` exactly as a
  malformed score token does — what is there is a filter the user meant,
  refused at Apply by the lib's field verdict, not nothing. A badge that
  re-parsed the text to decide would be the second encoding again. The badge
  reads the draft's `ActiveFacets` — the lib's answer over the draft's
  config, plus the facet of any bound whose text its field cannot hold
  (`1.5` in a move-number box is present and refused, the same presence
  ruling) — so it is honest for everything the panel holds. `ErrorRange`
  needs no exclusion any more: it has no
  row, so nothing it does can badge one. The shelved facets
  (`PositionTypes` / `PlayTypes`) have no row either and are outside scope by
  pre-existing panel behavior — the draft has no field for them, so
  `FilterDraft.From` drops them and `ToConfig` emits none —
  so a stale `xg_filter_config` blob carrying them badges nothing; since the
  panel is the only apply path, they also can never become active through
  it. The badge's *words* are the opposite kind of fact — panel-owned
  display copy, chosen per facet — and live in `FacetBadgeText`.
- **Clear filters touches filter values only.** It makes the draft and
  the baseline the empty selection and remembers it — nothing else. No
  host state (the panel has no parameter or interop path to any — e.g.
  BgQuiz's picked folder is out of reach by construction; keep it that
  way) and no row movement. Staging a saved filter likewise sets values
  without opening or closing a row; which rows are open changes only on
  the user's toggle. Clear commits the empty baseline on purpose (§4):
  editing back to a selection applied before the Clear needs Apply again.
- **The saved-filters file names are `public` — deliberately opposite to
  the internal storage-key rule.** `FilterStorage.ConfigKey` /
  `DisclosureKey` / `MoreFiltersKey` stay `internal` because no consumer
  may know or depend on the surface's localStorage keys. `SavedFiltersDocument.FileName` /
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
- **Storage adapters must wrap failures in `DocumentStorageException`.**
  The store's degrade-never-block posture rides on a *typed* catch: an
  adapter that lets its native failure type escape (`JSException`,
  `IOException`, an HTTP exception) will fault the host's flow instead of
  degrading to `LoadFailed` / `WriteFailed`. Wrap everything that means
  "the I/O failed"; let everything that means "the adapter has a bug"
  propagate. An absent document is `null` from `ReadAsync`, never an
  exception. Where nothing awaits the call — the composite's reload at a
  setup's end — propagating means the renderer's error path
  (`DispatchExceptionAsync`), never a discarded task: a fire-and-forget
  `_ = InvokeAsync(...)` over work that can fault loses the fault
  (`ASourceChangeReload_ThatFailsAfterAnAwait_*`).
- **Never restate a lib validity rule in the panel — ask it.** The error
  bounds' rule (non-negative, `min ≤ max`, `NaN` rejected) lives in
  `XgFilter_Lib` and is asked through `FilterConfig.GetInvalidFields()` on
  the same config Apply commits (the draft's `ToConfig()`, inside its
  `InvalidFields`). A local
  `if (min < 0)` here would be a second encoding of a rule `Build()` also
  enforces, and the two would drift the day the lib's rule moves — the
  depth-facet scenario again, one tier down. The same applies to
  attribution: which *field* to mark is the lib's answer (`FilterField`
  membership), not a facet-wide red. What the panel does own is the
  wording: `GetInvalidFields` deliberately returns no message strings, the
  same division of labour as `BoardPattern.TryParse`.
- **Never type a lib-owned spelling as a literal — render it.** Anything
  the user types that the lib parses has an exported constant, and that
  constant is the only place the spelling exists on this side:
  `MatchScoreToken.MoneyWithJacoby` / `MoneyWithoutJacoby` /
  `DoubleMatchPoint` / `RetiredMoney` (and `RetiredMoneyReplacements`
  for what to offer in place of the retired one) across the placeholder, the hint line, both
  verdicts, and `FilterHelp`'s match-scores prose; `CheckerLocation.PlayerOff`
  / `OpponentOff` (rendered through their canonical `ToString`, the
  public face of the lib's `internal` name constants) for the borne-off
  names in the position-pattern field's copy and `FilterHelp`'s
  position-pattern prose; `FilterPanel.ConfigKey`
  / `DisclosureKey` / `MoreFiltersKey` for the storage names;
  `FilterPanel.MoreFiltersFoldedLabel` / `MoreFiltersExpandedLabel` for
  the container toggle's two names, in the markup and in the help alike;
  `FilterFacet` / enum
  `[Description]`s via `ToLabel()` for every label. A second literal
  agrees today and drifts silently the day the grammar respells a token —
  which is exactly what the bare money token did in
  `halheinrich/backgammon#121`, and the reason those constants are
  exported at all. **The rule binds the tests too, with one carve-out that
  is not an exception to it**: a pin that re-types a spelling *it is
  following* is a third copy, and it would keep passing through the drift
  it exists to catch — so a pin on what the panel renders asks the owner.
  But a pin on what the panel was **ruled** to say is an oracle, and an
  oracle that imports the constant under test asserts only that the
  constant equals itself: it passes against an emptied one. The two ruled
  container labels are pinned as independent literals in
  `FilterPanelTests` for exactly that reason
  (`halheinrich/backgammon#239`), as `RowFacets` is, and a source survey
  in the same suite holds every *other* spelling of them in the tree to
  the one definition on `FilterPanel` — which is what keeps the oracle
  from quietly becoming a second owner. Where an *absence* must be pinned
  and a substring check cannot serve — the retired spelling is a prefix of
  both live ones — ask the grammar word by word (`GetFault`) rather than
  reaching for a literal.
- **The save snapshot is Apply's validity gate, and must stay exactly
  that.** `FilterSetupSnapshot.TryGetSavable` refuses exactly the drafts
  whose values Apply refuses. Both directions matter. Stricter, and
  save-as refuses a selection the user could apply; looser, and a saved
  document is minted from a selection Apply refuses — a permanent trap,
  since loading it reproduces the invalid state with Apply disabled. When a
  validity rule is added, it goes into the one verdict both read (the
  draft's `InvalidFields`); the composite's refusal copy stays
  field-agnostic for the same reason (the offending field is already
  marked, with its own explanation, in the panel).
- **Per-row Save snapshots the live draft — exactly as Save-as does.**
  Both save gestures capture what `TryGetSavable` hands over, unapplied
  edits included; a row Save differs only in taking its name from the
  row. Don't "fix" it to save the last-committed config —
  saving what the user sees staged is the contract, and the confirm copy
  ("…with the current filters") says so.
- **`OnSaveRequested` is `[EditorRequired]` and breaks host builds on
  purpose.** Both consumers compile-fail (`RZ2012`) until their own
  migration legs bind the per-row Save — the deliberate alternative to a
  silently splatted, dead affordance (see the Razor silent-splat entry
  above).
- **The owner's operations stay `internal`.** `Edit`, `Stage`,
  `ApplyAsync`, `ClearAsync`, `RestoreAsync` and the notice dismissals are
  the surface's components' to call. A host moves the setup only by
  reporting its source; widening an operation public would hand hosts a
  second way to change the selection or what is applied, uncoordinated
  with the panel on screen. The snapshot's reading for the components (the
  draft, the baseline, `CanApply`, the notices) is internal for the same
  reason, and a host has no use for it: its gate is `IsInEffectFor`.
- **`FilterPanel`'s narrowing is `.Internal` + `EditorBrowsable(Never)` —
  the strongest the toolchain allows, and the ban is absolute anyway.** The
  spike (Step 2, ruled): a true `internal` component draws CS0262 — the
  Razor generator hardcodes `public partial` on the component class, so a
  user partial cannot narrow it. The ruled fallback is what stands:
  `FilterPanel` lives in `XgFilter_Razor.Components.Internal` with
  `[EditorBrowsable(Never)]`, and consuming it from a host is banned
  outright — **including host tests: no `FindComponent<FilterPanel>()`
  carve-out.** Host wire tests drive `FilterSurface`'s rendered DOM with
  real gestures instead (both hosts' existing tests do reach the panel type
  today; their migration legs carry that rewrite). If the Razor toolchain
  ever allows internal components, finish the job then. `FilterHelp` stays
  public — it is consumer surface, and since
  halheinrich/backgammon#190 leg (D) so is `NamedEntriesPanel`: the pick
  list was only ever narrowed because it was filter-shaped, and it no
  longer is.
- **The surface is told, never asks — keep it that way.** It has no
  parameter or interop path to pickers, paths, folder handles, or
  capabilities: the host mints `FilterSourceToken`s and reports them to the
  owner, and rules `CanPersist`; the producer only compares tokens and ANDs
  the ruling with its store's status. Adding any host-domain knowledge (a
  path parameter "just for the notice", a capability enum) re-couples what
  the seam exists to decouple. The same boundary governs copy:
  degrade-notice and refusal wording is producer-owned here so every host
  degrades identically; only host-specific *reasons* (FS-Access phrasing)
  arrive as parameters. A host writing its own copy for these states is the
  facet-prose drift hazard again.
- **A host reports every source it latches, mounted or not — and `null`
  only for "no source".** The owner cannot see the host's pick; it ends a
  setup exactly when told of a different source. A host that unmounts the
  surface while no source is held must still report the next source when
  it latches it, or A → B → A would keep A's consent (§4). And a `null`
  later corrected to the real token reads as a real change and ends the
  setup — the baseline dropped, Apply re-armed — which is exactly wrong
  when nothing changed. A host whose source is derived from facts it
  restores asynchronously reports nothing until that derivation is
  settled, rather than a placeholder.
- **A mount is not a change.** The first snapshot reaches a component
  inside `Attach`, and is the existing setup learned: a remount over an
  unchanged source keeps the draft, the baseline and the notices, and the
  owner publishes nothing for it. Don't add mount-time choreography — no
  reconcile, no re-arm, no notice arming — to either component: what used
  to need it now lives in the owner, which outlives the mount. The
  composite's setup-change rule fires only on a new generation after
  attach.
- **The restoration notices' lifetime is the owner's — never derive it at
  mount.** "A stored selection was restored and nothing is applied" is
  also true of a navigate-back with unapplied edits, and navigation must
  change nothing (§1), so no mount-time condition can tell a fresh boot
  from a remount. The owner's lifetime is the distinguishing fact: a
  reload is a fresh owner, a remount reuses the boot's. Within a boot,
  each notice only moves toward its end. The restored notice ends at the
  first gesture that makes the selection the user's own (an edit, a staged
  saved filter, a commit) or at the user's close. A source change is not a
  gesture and leaves it, because a host whose source change crosses an
  unmount must see the same. The failure notice ends at the user's close or
  a commit whose write lands. This is not a history fact (§3): it records
  a pending present-tense state, and gates nothing but its own copy.
- **Restoration runs only from an interactive render.** `RestoreAsync` makes
  an interop call, and static rendering cannot; the panel starts it from
  `OnAfterRenderAsync`, which prerendering never runs. Don't start it from a
  constructor, `OnInitialized`, or a host's startup code. A failure that is
  not a refusal propagates out of the panel's lifecycle and leaves
  restoration `Pending`; it is a fault, never an outcome.
- **The draft is the editor's state — never rebuild it from its config.**
  `FilterDraft.From(draft.ToConfig())` loses any box whose text its field
  cannot be (`1.5` in a move-number bound becomes no bound), which is the
  defect the draft exists to end. Hold and retain the draft; derive the
  config. Don't cache a derived value on the draft either: it is a record,
  and `with` would copy the cache into a draft it was not computed for.
- **The bound boxes stay text boxes, and their grammar stays the draft's.**
  A `type="number"` input "for the keyboard" brings the defect back: the
  browser withholds text it cannot read, so unfinished input arrives as a
  blank bound (`ABoundBox_IsATextBox_AskingForDigits` sweeps the panel for
  a number control). `inputmode` is what asks for digits. Don't widen the
  grammar to strip a separator, and don't narrow it with a domain rule —
  finiteness, floors and order are the lib's; the draft decides only what
  text is a number at all. The pattern box's commas are its own grammar's:
  no decimal reading reaches it.
- **What a box shows is per mount; validity is not.** The panel's
  `_revealed` set is presentation (halheinrich/backgammon#272): it starts at
  "every wrong value shown" on each mount, and keys on the panel's own
  typing through `Edit`. Never gate anything on it — the gates read the
  owner's verdict, continuously — and never move it into the owner, where a
  second mount would inherit the first's half-typed state. A new
  verdict-bearing box needs its `onblur` (`LeaveField`) as well as the
  `FieldAttributes` splat, or its error never shows. The space a shown line
  holds (`_heldLines`) is per-mount presentation too, and it is released
  only by a fold or the unmount: never release it on a blur, a commit, a
  stage or a restore, and never record it after a render instead of at the
  state change. A new feedback line joins `FeedbackLines`, `FieldsOf` and
  `IsGroupVisible`, and renders its box through `FeedbackLineAttributes`,
  or a correction removes it from under the user's caret.
- **Refusals go to the host's sink, never through a component.** A
  refusal can complete after the component that started its call is gone,
  so a component event would lose it. The sink is resolved by
  `AddFilterSurface<TRefusalSink>` in the owner's scope, which is why it is
  a registered type and not a delegate a page hands in. Rejecting a stale
  state update must never take the refusal with it.
- **A test waits on the operation, never on a render.** A held storage call
  completes on release, and its continuation runs afterwards; a stale
  completion that is rightly rejected renders nothing. Wait — bounded by
  `DefaultWaitTimeout` — on the click's task, on `RestoreAsync()`, or on
  the panel's `MountRestored`. A browser test waits on the restoration
  marker (`FilterRestorationMarker.SettledSelector`). `MountRestored`
  stays honest only while it completes after the mount's restores; it is
  pinned (`MountRestored_CompletesOnlyOnceTheMountsRestoresHaveSettled`).
- **Never type alert markup here, and never add a second holder for a
  dismissal.** A new box is a `<Notice>`; whether the user may close it is
  the umbrella's `SPEC-notices.md` section 1 (the gate-reason test), never
  the box's colour, and `Dismissible` defaults to `false` on purpose. Then
  find the occurrence's owner before writing any state. The wrong moves,
  each of which a future edit will be tempted by:
  - **A `_dismissed` field beside a `<Notice>`** — in the panel for the
    restore notice, in the composite for the others. It is a second copy
    of a bit that already has a holder, and a component's copy dies on
    unmount while the occurrence may not: that is the closed restore
    notice coming back on a navigate-back.
  - **A second holder for the restore notices** — a "closed by hand" bit
    beside the owner's, or a notice state anywhere but `FilterSetup`. The
    close gesture and the owning gesture are one end, the owner's; a
    second bit would be a second holder for the restoration to forget.
  - **Keying the write-failed notice on `Status`, on the file name, or on
    nothing.** `Status` reads `WriteFailed` after every failure alike.
    Unkeyed, it would behave today only by an accident of rendering — a
    `WriteFailed` context refuses further writes, so the next failure
    needs a reload, and the reload's render happens to unmount the
    notice — and a ruled behaviour must not rest on that. The store names
    the failure; pass the name.
  - **Treating the refusal's text as its occurrence.** Consecutive
    refusals carry identical copy. The field being assigned again is the
    new occurrence, which is why the composite clears the field on close
    rather than remembering that this text was closed.
  - **Wrapping a bound `<Notice>` in an `@if` on the same fact.** The
    restore notice and the save refusal bind `Dismissed` to their holder's
    one fact and render nothing while it says so; an `@if` around them
    reads that fact a second time and buys nothing.
  - **Leaving `role`, `@onclick` or an `alert*` class on the tag.** The
    component throws on the first two by design and would emit the third
    twice. Pass only marker and spacing classes, `id` and `style`, and
    name the `Announcement` explicitly (`status` was `Polite`, `alert` was
    `Assertive`) rather than trusting the default.
  - **Trusting a green build after the component renames a parameter.** An
    attribute on a `<Notice>` tag that matches no parameter is not an
    error: Razor splats it onto the box and the real parameter takes its
    default. That happened once (`halheinrich/backgammon#248`) — 0
    warnings, and both assertive boxes silently polite. Adapt to a
    producer rename by bare-identifier survey, never by following build
    errors; the suite's box pins refuse any attribute on a box beyond the
    ones this member passes, which is what catches the next one.
  - **Reading a box's `role` on the box.** The live region is the
    `div.bg-notice-content` child, so that the close button — its sibling
    — is not part of what an atomic region announces. The box carries no
    role under any `Announcement`.
  - **Pinning a box's copy by its children.** The content sits inside
    `div.bg-notice-content`, and a dismissible box also holds the close
    button; `TextContent` on the box still reads the copy, a direct-child
    selector does not.
- **The load confirmation is announced by its standing region, never by
  itself** (`halheinrich/backgammon#248`). "{name} loaded." renders inside
  `NamedEntriesPanel`'s persistent `role="status"` region, which is pinned as
  contract (a live region created in the render that fills it is announced
  unreliably), so its `<Notice>` is `ByEnclosingRegion`: no role and no
  live-region attribute on any element of the box. `Polite` there would nest
  one live region inside another — announced twice by some screen readers,
  and not what the ARIA model means by either. Do not drop the region, move
  the surface's id onto the box, or make the region conditional; and its
  dismissal is the panel's `_loadedName` being cleared, not a bit and not a
  key — the name is not the occurrence, and the same name loads twice.
- **A new mount of `NamedEntriesPanel` supplies a surface record and
  nothing else.** Copy and ids belong to the mount, not the component: the
  panel spells no title, no empty-list line, no placeholder, no prompt
  noun and no element id of its own, and adding one would give every other
  document that document's voice. If a mount needs wording the record does
  not carry, the record grows a member — it does not grow a parameter, and
  the panel does not grow a special case. Presets live beside their
  composites (`FilterSurface.SavedFilters`), never on
  `NamedEntriesSurface`, which knows no document.
- **A mount's element ids are published surface the moment a host renders
  them.** `saveFilterName`, `saveFilterButton` and
  `savedFilterLoadedNotice` are found by fifty-odd sites across BgQuiz's
  unit and e2e suites, ExtractFromXgToCsv's tests and this repo's own.
  Editing `FilterSurface.SavedFilters` is therefore a cross-repository
  change, not a rename — nothing in this repo's build will tell you
  otherwise, and the local pin that does notice
  (`SaveAs_NewName_WritesThroughSeam`) only proves the preset and this
  repo's suite still agree.
- **`HandleLoadRequested` throws on a lookup miss; it must not degrade.**
  The panel raises only names it read from the very document the composite
  holds, so a miss means the two have diverged
  (halheinrich/backgammon#173). It is also the one path where the panel's
  own "{name} loaded." confirmation could stand over filters that never
  moved, because the panel states that confirmation on the request
  returning without throwing. Turning the throw back into a silent no-op —
  or into a degrade notice — restores exactly that lie. This is a bug
  path, not a bad-input path: it has no user-facing wording, deliberately.
- **The store's two renames are not forwarded, deliberately.** `Filters`
  became `Document` and `SavedFiltersStatus` became `NamedDocumentStatus`
  when the store generalized (halheinrich/backgammon#190 leg (D)). Neither
  keeps a compatibility alias: the generic base cannot know a domain noun,
  and an alias would be a second name for one fact — the very thing this
  arc exists to remove. Host call sites move; the compile break *is* the
  migration, and it is cheaper than the drift an alias invites.
- **A store specialization supplies identity and nothing else.**
  `SavedFiltersStore` is a constructor plus `FileName` and
  `LegacyFileName`. Adding any member to it — a convenience read, a
  domain-shaped wrapper, a second load path — puts behaviour in one
  document's store that every sibling document then silently lacks. The
  lifecycle belongs to `NamedDocumentStore`, or it does not exist.
  `LegacyFileName` defaults to `null` on purpose: a document minted from
  here on declares no legacy name and therefore never reads a second file
  — pinned by a test-only specialization rather than trusted, because
  nothing in `SavedFiltersStore`'s own cases can distinguish "the fallback
  ran because this document asked" from "the base always tries two".
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
