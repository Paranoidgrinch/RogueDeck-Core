# The strategic map generator — implementation plan

**Written 2026-09-11**, from a live audit of `RogueDeck-Core @6cccd2c` and `bnb-content @1e6fced` (both clean,
both on `origin/main`), plus a measurement probe run against the REAL BnB act specs at four acts × three seeds.

Source: the user's `BnB_Strategic_Map_Generator_Implementation_Plan.md`. That document's **diagnosis and target
architecture are adopted whole**. This file is what the code says about it: the measured baseline, five places
where the source document describes a repository that does not exist, the two decisions the user has since
made (act length, and shipping both generators side by side), and the step order to build it in.

---

## 1. What today's maps actually are (measured, not assumed)

A throwaway probe generated every act's map from the real `ActMap.Spec` and dumped its shape. The numbers below
are that output, not a reading of the code.

| | Act I | Act II | Act III | Act IV | Act V |
|---|---:|---:|---:|---:|---:|
| authored `steps_before_boss` | 9 | 12 | 17 | 17 | 3 |
| `spec.Rows` (the varied backbone) | **5** | **5** | **5** | **5** | 0 |
| rows GENERATED (incl. boss) | 23–24 | 24 | 25–27 | 36–37 | 3 |
| of those: **uniform** wide rows | **19–20** | **21** | **21–22** | **31** | — |
| of those: genuinely varied rows | **3** | **2–3** | **3–4** | **4–5** | — |
| nodes | 55–78 | 55–80 | 74–86 | 84–124 | 3 |

Three facts follow from this, and all three are worse than the source document claims.

**1.1 Every act has exactly five varied rows — by accident.** `MapSpecBuilder.FreeRows` is
`Math.Max(MinimumFreeRows /*5*/, StepsBeforeBoss − PerPathMinimums.Sum())`. For every act the subtraction is
negative (Act I: 9 − 19 = −10; Act IV: 17 − 31 = −14), so **the floor always wins**. `steps_before_boss` has not
influenced a single BnB map since the per-path table was authored. The act's length is
`4 + sum(per-path minimums)`, and nothing else.

**1.2 Roughly 85 % of an act is rows where every column is the same room.** `WideGuaranteeRows = true` makes a
guarantee row keep the map's width and fill *every column with that one kind*. Act I, seed 1, from row 7 down:

```
r7  R R     r11 M M     r15 C C     r19 T T
r8  T T     r12 C ?     r16 $ $     r20 C C
r9  C C     r13 E E     r17 C C     r21 ? ?
r10 C C     r14 ? ?     r18 R R     r22 C C
```

The player's "choice" at row 7 is *rest, or rest*. The branching is decorative for sixteen consecutive rows.
The per-path ranges say the same thing in numbers — Act I over all 162 routes:

```
Elite 1..1   MultiCombat 1..1   Rest 2..2   Shop 2..2   Treasure 2..3   Event 3..5   Combat 9..12
```

Four of seven room types are **identical on every single route**. This is the defect the rework exists to fix,
and it is not "routes are nudged toward similar composition" — it is *routes are the same list of rooms in a
slightly different order*.

**1.3 The whole tail of an act has one width, drawn once.** `RuleBasedMapGenerator.WidthOf` gives a guarantee
row `branches.Widths[Math.Min(row.BranchIndex, last)]`, and `BranchIndex` saturates at the last branch row — so
every guarantee row past row 5 borrows the **same** width. Measured: `343333322222222222222221` (Act I seed 1),
`432222222244444444444444444444444441` (Act IV seed 20260820). One dice roll decides whether two thirds of an
act is two rooms wide or four. Topology-first generation removes this for free.

---

## 2. Where the source document and the repository disagree

Adopt the source document except for these five points.

**2.1 `Rows = StepsBeforeBoss` does not preserve act length — it halves the game.** The document states act
lengths stay unchanged (§6 of its header, Phases 6 and 22) and derives that from removing the `FreeRows`
subtraction. But guarantee rows are **added on top of** the five free rows, not subtracted from the authored
number. Taking `steps_before_boss` literally:

| | Act I | Act II | Act III | Act IV | Act V | **run** |
|---|---:|---:|---:|---:|---:|---:|
| rooms per route today | 24 | 24 | 26 | 37 | 3 | **~114** |
| rooms per route at `steps_before_boss` | 10 | 13 | 18 | 18 | 3 | **~62** |

That is not a map rework, it is a 45 % cut to run length, fight count, gold, card rewards and deck growth, and
it would land in the same commit as the generator — making both unmeasurable. It also contradicts the document's
own §55 ("the first BnB version should try to preserve the current overall content density while changing
**where** that content appears"). §55 is the instruction that matches the intent; the header is a mistake about
the baseline. **See §3 — this is the one open decision.**

**2.2 The new spec has to survive serialization, export and the Studio — the document never mentions it.**
`MapGenerationSpec` is not just a generator input: it round-trips through `RunJson`, hangs off
`RunAct.MapGeneration` and `RunBlueprint.MapGeneration`, is checked by
`RunDocumentValidator.CheckMapGeneration` + the export gate (`ValidateForExport` actually *generates* a map),
rides inside `game.roguedeck.json` to Godot (`docs/godot-export-contract.md`), and is authored by
`MapRulesTab.razor` (461 lines). A new spec type means work in all five. This is a real chunk of the project and
the document prices it at zero.

**2.3 The generator cannot be selected "by the caller".** The document's Phase 39 suggests the caller picks.
The caller is `RunSetup.Generate`, which reads the generator choice out of the *document*. So the switch must be
a document field: a nullable `StrategicMapGeneration` beside the existing `MapGeneration` on both `RunAct` and
`RunBlueprint`. Present ⇒ strategic; absent ⇒ rule-based. Additive, so no `RunBlueprintSchema` ladder step is
needed (old documents simply lack the property) — but the round-trip test must cover it.

**2.4 Planarity has to reach the screen, and today it only does by luck.** `MapView.Layout` →
`MapGraphLayout.Resolve` assigns a node's lane by **insertion order within its depth column**, not by its id.
It matches the generator's columns only because the generator happens to add nodes left-to-right. A topology
that *guarantees* no crossings should say so in the data: the strategic generator emits `RunMap.Layout` (the
field already exists, for authored maps), and the frontends stop guessing. Cheap, and it is the only way the
planarity work is visible to a player.

**2.5 Two live dependencies on the retired fields.**
- `MapSpecBuilder` sizes the treasure-event pool as `PerPathMaximums[Treasure] + 1`. Retiring per-path maxima
  breaks that line; it must read the Treasure budget's `Max`.
- Today a treasure **on a guarantee row never flips to a mimic** (it is the promise). With map-wide budgets that
  special case disappears and *every* treasure becomes flippable — so the effective mimic rate roughly doubles
  at an unchanged `TreasureMimicChancePercent`. Decide deliberately: either accept it and halve the percentages,
  or keep a "one unflippable treasure per act" rule in the budget.

Everything else in the source document survives contact with the code. Two claims worth confirming as correct:
`MapConstraintValidator` really is an O(V+E) reverse-topological DP and generalizes to weighted scores as
described; and the current wiring really can cross (`MapWiring.WireRows` may emit `i → mid+1` alongside
`i+1 → mid`), so "no crossings" is a new guarantee, not a restatement.

---

## 3. Act length — DECIDED (user, 2026-09-11): keep it

> "aktlänge beibehalten"

`Rows` is authored explicitly per act at **23 / 24 / 25 / 35** — what the guarantee rows add up to today — so
the rework changes *shape only*: the same ~114-room run, the same fight count, the same reward economy, and a
playtest that measures one variable. Option (B) (`steps_before_boss`, a 62-room run) stays available later as a
four-number edit plus a budget rescale; it is a balance pass and belongs in its own arc.

Consequence: `steps_before_boss` (9 / 12 / 17 / 17) has described nothing since the per-path table was authored
(§1.1). It is **re-authored in the act manifests to the real numbers** in S12, so the field means what it says,
and `MinimumFreeRows` / `FreeRows` go with it.

## 4. Target architecture

New files, all in `src/RogueDeck.Run/Map/`:

```
WeightedPathEvaluator.cs      min/max weighted path score (generalizes MapConstraintValidator's DP)
MapSeedStreams.cs             named deterministic RNG streams from one run seed
StrategicMapGenerationSpec.cs the new config record (+ RoomBudget, DepthBandBudget, StrategicTopologyRules,
                              PathPressureRules, ForkQualityRules, RepairRules)
StrategicTopology.cs          topology-only IR: rows, slots, edges, strand assignments
StrategicTopologyGenerator.cs the CONTINUE/SPLIT/MERGE walk, strand identity, planarity, branch lifetime
RoomAllocator.cs              scored role assignment against act + band budgets
ChoiceSignature.cs            fork signatures + decision-horizon aggregation + contrast
MapRepair.cs                  deterministic swap-first repair
StrategicMapGenerator.cs      the pipeline + bounded attempts + diagnostic exception
MapDiagnostics.cs             per-node/whole-map diagnostics + the ASCII dump
```

Untouched: `RuleBasedMapGenerator`, `MapGenerationSpec`, `MapConstraintValidator`, `MapWiring`,
`EncounterSelector`, `MapNodeRealizer`, `LayeredMapGenerator`. The legacy generator keeps its golden output and
its seed formulas exactly; it is the guarantee-oriented generator and stays supported.

Seam: `RunAct.StrategicMapGeneration` / `RunBlueprint.StrategicMapGeneration` (nullable) → `RunSetup.Generate`
picks. `MapNodeRealizer` is reused verbatim — content realization does not change at all.

---

## 4b. Both generators ship, and the player picks — DECIDED (user, 2026-09-11)

> "ich würde außerdem den alten map generator noch drinbehalten und im hauptmenü bei einem neuen spielstart
> einmal abfragen ob man den v.0.0.0 oder v0.0.1 map gen benutzen will"

The legacy generator was already staying (§4). What is new is that the choice becomes **player-facing**: the
title screen asks at "New run ▸" which map generator the run uses, and that answer is a property of the run for
as long as it lives.

**That last part is the whole difficulty, and it is not in the UI.** A BnB map is never saved — it is
*regenerated* on resume, as a pure function of `(seed, startingLoadout)`, both of which the save carries
(`RunSetup.BuildActPlan`, `RunSaveData.MapGenerationLoadout`, `RunPlayback.Resume`). A generator choice that
lives only in the menu would therefore **change a running save's map on the next resume** — the player closes
the game standing in front of an elite and comes back to a shop. So:

- `RunSaveData` gains `public string? MapGenerator { get; init; }` — an `init` property with a default, exactly
  like `MapGenerationLoadout`, so **every existing save keeps the legacy generator** with no migration.
- `RunState` carries it beside `GeneratedMapLoadout` (`SetGeneratedMapGenerator`), and
  `BuildActPlan` / `BuildRunMap` / `CreateInitialRun` take it as a parameter. Absent ⇒ rule-based.
- `RunPlayback.Resume` passes the saved value. A resumed run rebuilds its own map, not the current default's.

Naming, as the user set it: **v0.0.0** = the rule-based generator (guaranteed routes, what every run has used so
far), **v0.0.1** = the strategic generator. Both labels appear in the dialog with one line of plain English
each, because "v0.0.1" tells a playtester nothing about what they are choosing.

Godot side (`bnb-godot`): "New run ▸" opens a small dialog in the existing overlay pattern (veil + dim +
centered panel, the one `BugReportPanel` uses), two options, last pick preselected and remembered in the meta
store, then `host.StartNewRun(seed, character, generator)`. The option only becomes selectable once S11 has
landed — until then the dialog would offer a generator that cannot build a map.

**And it joins the bug report.** `BugReport.Diagnostics` prints a `map` line already; it must now name the
generator the run was started with. A report about a broken map is close to worthless without it, and from the
moment two generators ship, every report is ambiguous until it says which one it is about. This is a two-line
change in `bnb-godot/scripts/BugReport.cs` and it is not optional.

## 5. Step order

Each step is one commit or a short series, builds green, and is pushed before the next. Suites to keep green:
Core / Scenario / Run / Sandbox, plus `bnb-content` 925.

**S1 — the measurement tool first.** ✔ **DONE 2026-09-11** — Core `fe60be0` + `6dc06f0`, bnb-content (golden).
`MapDiagnostics.Of(GeneratedMap)` measures rows, widths, nodes, edges, entries, forks, merges, room totals, the
per-route spread per role (via the existing O(V+E) DP — never by walking routes), and two numbers nothing
measured before: **uniform rows** and **crossing edges**. `MapDepth.Percent` is now the single depth formula the
generator and the diagnostics share. `Render()` is the summary + grid, `Detail()` one line per room with the
fight or door it drew. `bnb-content/Tests/MapGoldenTests.cs` pins v0.0.0's **topology and roles** for Acts I–V ×
3 seeds in `Tests/Golden/map-v0.0.0.txt` (551 lines) — deliberately *not* encounter ids, which would rewrite the
file every time an act gains content; re-bless with `UPDATE_MAP_GOLDEN=1`, and a failure drops an
`.actual.txt` beside it to diff. `Tests/MapDumpProbe.cs` prints any act/seed on demand
(`MAP_DUMP_ACT=4 MAP_DUMP_SEED=99 dotnet test --filter MapDumpProbe --logger "console;verbosity=detailed"`).
*Proved:* the dump reproduces §1's tables exactly, and the golden passed un-reblessed across `6dc06f0` — the
first refactor it was asked to vouch for. **New fact it turned up:** v0.0.0 emits crossing edges —
**30 crossing pairs in 1 155 edges** across the fifteen sampled maps, which confirms §2's claim with numbers.

**S2 — `WeightedPathEvaluator`.** ✔ **DONE 2026-09-11** — Core `c6559e2` + `86a92f6`.
`MinimumPathScore` / `MaximumPathScore` over `Func<NodeId, MapNodeKind, double>`, one reverse-topological O(V+E)
pass, proved on hand-drawn diamonds and ladders (11 tests, including the source document's §40 example).
`MapConstraintValidator` now runs on it and has lost its own copy of the traversal, its Kahn ordering and its
entry-node fallback. Two edges of the definition are deliberate: a map nobody can walk scores **0, not infinity**
(a caller comparing ±∞ against a threshold would silently pass or fail everything), and a room the role map never
placed is worth nothing.
*Proved inert:* the refactor is behaviour-SENSITIVE — the generator sizes its guarantee gates from
`WorstPathCount`, so an off-by-one would rewrite every map — and the v0.0.0 golden passed byte-identical across
it on all fifteen real act maps. Core 1469/755/**603**/373 green, `dotnet format` 0.

**S3 — `MapSeedStreams`.** ✔ **DONE 2026-09-11** — Core `49d3131`. `From(seed)` → `.Topology` / `.Strands` /
`.Rooms` / `.Repair` / `.Content`, plus `For(name)` for a stream a later stage needs and `Attempt(index)` for a
deterministic retry family (attempt 0 is the original). The legacy generator's `seed * 31 + 7` formulas stay
where they are.
Two things are frozen on purpose. The hash is **written out, not borrowed**: `string.GetHashCode()` is randomized
per process in .NET, so a stream salted with it would lay out a different map on every launch — the one thing a
seed exists to prevent. And the derived numbers are **pinned as literals** in the tests, computed twice by two
independent implementations and agreed to the digit, so altering the mixing fails a test instead of silently
invalidating every recorded seed and both golden files. 7 tests; Core 1469/755/**610**/373 green; the v0.0.0
golden unaffected, as nothing in the legacy path was touched.

**S4 — topology.** ✔ **DONE 2026-09-11** — Core `a38f7f1`. `StrategicTopology` (the IR: rows, slots, edges,
strands), `StrategicTopologyGenerator` (the walk) and `StrategicTopologyValidator`, with 36 tests. A row is an
OPERATION on the live routes, not a width: CONTINUE, SPLIT (child inserted beside its parent) or MERGE
(neighbours only), weights 6/2/2, and the edges are what the operation means rather than a repair fitted to two
mismatched widths. Strand identity survives continues, sideways shifts and convergences (the **older** strand
keeps its name, leftmost on a tie — which lane PROFILE a merged strand carries is S5's question, deliberately
not settled here). `MinBranchLifeRows = 3` is enforced twice over: a merge needs both strands old enough, and no
split happens in the act's last three rows, so the boss's own convergence cannot cut a branch short either.
*Proved:* 10 000 seeds × act lengths 4/5/12/23/24/25/35, plus 1 000 each over five width configurations
(1..2 through 2..6), four branch lifetimes (0/1/5/9) and the short acts — **80 000 acts in ten seconds**, zero
validator problems, **zero crossings**, shortest absorbed branch exactly 3 rows. Cheap enough to stay a
permanent gate rather than a claim in a document.
**Measured shape** (10 000 seeds each, BnB widths 2..4): Act I-length acts average **2.7 forks · 4.0 merges ·
5.7 strands**, Act IV-length **4.5 · 5.8 · 7.5**. That is far fewer forks than v0.0.0's twelve — and the point:
v0.0.0's twelve are forks between identical rooms, these are the only places a route can differ. Raising
`SplitWeight` moves it little, because every split above `MaxWidth` must be paid for by a merge; the real knob is
`MaxWidth`. Carried into S13's report as a tuning question, not settled here.
**One real defect the sweep found:** an act too short for a branch to live its minimum cannot honour both
promises, because the act's OPENING WIDTH is already a set of branches and the boss absorbs them after
`rows − bossRooms` rows. A 2- or 3-row act at width ≥ 2 with a 3-row branch minimum is now **refused by name**
instead of quietly bent. (A gauntlet act — nothing but boss rooms — has no branches and stays legal, as does a
minimum width of 1.) S7 generalizes this into the spec validator.
**`Rows` means the WHOLE act here, boss rooms included** — deliberately unlike the rule-based
`MapGenerationSpec.Rows`, which counts only the backbone and then grows by however many gates it needs. §12's
"act length is a structural invariant" is only true if the authored number is the number a route walks, so S12
authors 23/24/25/35 as totals.

**S5 — lane profiles bind to strands.** `StrandId → MapLaneProfile`, with the document's conservative split
(child keeps the parent's profile, sibling takes a different one) and merge (older strand weighted higher,
deterministic tie-break) inheritance. Lane weights themselves are **not** retuned here.

**S6 — act-wide budgets + the allocator.** `RoomBudget{Target,Min,Max}`, the scored assignment
(`BaseActWeight × StrandAffinity × ActBudgetNeed × LocalDiversity`), constrained-roles-first ordering, depth
eligibility as a hard filter. Combat is the filler but never the universal repair target.

**S7 — depth bands.** `DepthBandBudget` over normalized depth, existing `RoleMinimumDepthPercent` /
`NodeRefMinimumDepthPercent` / `EncounterMinimumDepthPercent` stay authoritative, and a spec validator that
catches impossible band/eligibility combinations *before* generation.

**S8 — PathPressure.** Role weights through `WeightedPathEvaluator`; `Minimum` is a hard constraint, `Maximum`
is a diagnostic at first. This is what replaces `MinEnemiesPerPath` and the per-path role minima.

**S9 — fork quality, measurement only.** `ChoiceSignature`, decision horizon (default 3 rows), pairwise
contrast. Reported, not repaired, so the seed reports can rank forks before anything acts on the ranking.

**S10 — repair.** Swap-first (Repair A), single reassignment second (B), bounded passes, then deterministic full
regeneration from `Hash(seed, attempt)`. C–F from the document are deferred; a diagnostic exception naming seed,
attempt count, violated constraints, budget state, pressure range and worst fork replaces any silent degradation.

**S11 — the document seam + the run remembers its generator.** `StrategicMapGeneration` on
`RunAct`/`RunBlueprint`, `RunSetup.Generate`, `RunJson` round-trip, `RunDocumentValidator` checks + export gate,
`MapRulesTab.razor` authoring, and the strategic generator emitting `RunMap.Layout` (§2.4). Plus the whole
persistence chain from §4b: `RunSaveData.MapGenerator`, `RunState`, `BuildActPlan`/`BuildRunMap`/
`CreateInitialRun`, `RunPlayback.Resume`. `docs/godot-export-contract.md` updated.
*Done when:* a save written under one generator resumes on that generator with a byte-identical map, a save
written before this step resumes on v0.0.0, and the round trip is covered by a test.

**S12 — BnB integration.** `ActRules` gains `Rows`, `RoomBudgets`, `DepthBands`, `Topology`, `PathPressure`,
`ForkQuality` and loses `PerPathMinimums` / `PerPathMaximums`; `MapSpecBuilder` drops `FreeRows` and
`MinimumFreeRows`, builds the strategic spec, and reads the treasure-pool size from the Treasure budget (§2.5).
Acts I–IV switch over; Act V keeps `BuildGauntlet` untouched. BnB tests move from per-path counts to intent:
act length, boss last, 2–4 wide, budgets in range, depth gates held, every route meets minimum pressure, all
content resolvable. `ActSeamTests` (16 references to the retired fields) is the largest rewrite.

**S13 — the statistical report.** Per act over 1 000–10 000 seeds: invalid maps, node/fork/merge/branch-life
averages, fork contrast min/mean/max, pressure min/mean/max, per-role count ranges, repair operations, full
regenerations, and a named outlier seed per category. CI fails on hard constraints only; quality metrics are
exported for reading.

**S14 — retire the BnB guarantee configuration** and rewrite `docs/bnb-act-map-specs.md`, which currently
states the per-path promises as the design (see §7). BnB keeps BOTH specs per act (§4b), so nothing here removes
`MapGenerationSpec` or the rule-based generator — only BnB's dependence on per-path guarantees as the *default*.

**S15 — the choice reaches the player** (`bnb-godot`). The "New run ▸" dialog, the remembered preference, the
generator on the `StartNewRun` call, and the generator named in `BugReport.Diagnostics` (§4b). A `--smoke-*`
probe that starts a run on each generator and reports act length + room counts, in the house style.
*Done when:* both generators are startable from the title screen, a run resumed after a restart has the map it
had before, and a bug report names its generator.

---

## 6. Starting numbers for BnB (Act I, worked through)

Derived from the measured totals in §1 for a 23-row act (≈ 66 nodes at width 2–4), not from multiplying the old
per-path minimums — paths share nodes, so that multiplication is meaningless.

```
RoomBudgets (map-wide)        Target  Min  Max      PathPressure (role weights)
  Elite                          5     3    8         Combat       1.0
  Shop                           5     4    7         MultiCombat  1.5
  Rest                           6     4    9         Elite        2.5
  Treasure                       6     4    9         everything else 0
  Event                         11     8   15
  MultiCombat                    4     2    7       Minimum 11   (today's worst route = 13)
  Combat                    the filler (~29)        Maximum 20   (today's richest  = 16)
```

Acts II–IV scale the same way off their own measured totals. Topology: `ContinueWeight 6`, `SplitWeight 2`,
`MergeWeight 2`, `MinBranchLifeRows 3`, `DecisionHorizonRows 3`. Four depth bands at 25 % each. All of it is
tuning, and all of it is expected to move after the first playtest — which is why S13 exists.

---

## 7. What the map looks like afterwards

Act I, same 23 rows, but every row is now drawn on its own and the strands carry the flavour. A plausible
generated act:

```
                  A:C     B:?     C:C                 A/B/C/D = strand      C combat   M multi
                   |      / \      |                                        E elite    ? event
                  A:C   B:$ D:C   C:R                  A = "the long queue" R rest     $ shop
                   |     |   |    / \                  B = "errands"        T treasure
                  A:M   B:? D:C  C:T C:C               C = "the quiet        B boss
                   \     /    \    |  /                     corridor"
                    A:E       D:C  C:?
                     |         \    /
                    A:C         D:$
                     ...         ...
                        \       /
                          r22:B
```

The differences a player can actually see:

- **A route can lack a room type entirely.** Strand A through the long queue may hold three elites and no shop;
  strand C through the quiet corridor may hold two rests, two treasures and one elite. Today both hold exactly
  one elite, two rests and two shops.
- **A fork is a real question.** Left is an elite two rows deep and a treasure behind it; right is a shop and a
  campfire. Today a fork is "rest or rest".
- **The act breathes.** Width walks 2 → 3 → 4 → 3 → 2 in coherent stretches of splits and merges instead of
  jumping per row and then freezing for twenty rows at whatever the fifth row rolled.
- **Branches live long enough to matter.** A split cannot re-merge for three rows, so choosing a side is a
  commitment rather than a one-room detour.
- **No crossing lines.** Merges only happen between neighbours, and the generator writes the visual column into
  `RunMap.Layout`, so `MapView` draws what the generator guaranteed.
- **The act is the same length and holds the same amount of everything** (§3). What changed is *where*, and
  therefore *whether two routes differ*.
- **And the old map is still there.** A run started on v0.0.0 is the act above; the two can be played back to
  back from the same title screen and compared (§4b).
- **Unchanged on purpose:** which concrete elite and which boss stand on the map is still decided at generation
  time (so the frontend can keep revealing them), boss relics are still random, Act V is still three gods back
  to back, treasure still flips to a mimic, and every fight still pays out.

`docs/bnb-act-map-specs.md` stops being true the moment S12 lands: its per-path table is the thing being
retired. It gets rewritten in the same step to state map-wide budgets and minimum path pressure, with a note
that the per-path era ran until 2026-09-11 and why it ended.

---

## 8. Out of scope (from the source document's §54, confirmed)

Act-length rebalance beyond the one decision in §3 · boss relic selection · Act V · map artwork · a Godot map
redesign · meta progression · encounter, card or relic balance · encounter-threat-driven pressure · blended
continuous lane-affinity vectors · map mutation from events or relics · player-visible route scoring ·
`LayeredMapGenerator` · and the legacy generator's own features, which stay in the engine.
