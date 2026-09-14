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

**S5 — lane profiles bind to strands.** ✔ **DONE 2026-09-11** — Core `2fb9659`. `StrategicStrandProfiles`
(the model) and `StrategicStrandProfileAssigner` (a single pass down a finished topology), with 32 tests.
A profile changes at exactly three kinds of row and nowhere else: the act **opens** (each entrance takes a
flavour of its own, the list rotated by the seed — the only place position decides anything, and legitimate
because row 0 holds doors, not routes yet), a **split** (the parent keeps its profile, the new branch takes a
weighted step away: same 1 / adjacent 4 / distant 2), and a **merge** (the survivor's flavour is drawn with the
older route favoured, 3:1). Every other row inherits — that is the whole difference from `column % count`, under
which a route that shifted one column sideways silently became a different lane. The draws come from the
**Strands** stream, so retuning inheritance cannot reshape an act and reshaping an act cannot reflavour it. Lane
weights themselves are untouched, as the document asks (§30).
**A profile is spans over rows, not one value per strand.** A merge is the only event that can change a LIVING
strand's profile, and it must be able to: a corridor that swallows a young branch may take on its character, and
pretending the absorbed route left no trace would be decorative branching wearing a different hat. Most strands
have exactly one span — an Act I-length act averages 6.3 spans over 5.7 strands. `ProfileOf(NodeId)` is the one
call S6's allocator needs, and it resolves through the strand, never through the column.
**The authored list's order is a conceptual scale, and NOT a ring.** Neighbours are authored as related
flavours; the first and last are not adjacent, because that would make a neighbourhood out of where a list
happens to end. *Measured consequence, worth knowing when authoring:* the ends of the list get about **10 %
fewer rooms** than its middle (35.9k/39.3k/39.5k/35.8k rooms over 2 000 Act III-length acts), because an
interior flavour has two neighbours to be stepped onto from and an end has one. Put the flavours an act should
mostly feel like in the middle of the list.
*Proved:* every BnB act length × 2 000 seeds, one to six authored profiles × 1 000, and five rule extremes
(all weights zero → the document's documented fallback, forks that always agree, forks that always jump,
convergences that always hand over) × 1 000 — **22 000 acts**, every room resolving to a flavour, and a flavour
beginning only where a route was born or where a second route arrived. Both invariants are checked on the
finished assignment rather than argued from the walk. Run suite **678** green, `dotnet format` 0; the v0.0.0
golden untouched (two new files, nothing modified).
**Handover rate, for S13:** with the 3:1 age bias roughly **half** of all acts contain one convergence that
actually changed the survivor's character (1 001 of 2 000 at Act I length, 1 301 of 2 000 at Act IV length).
A 4-row act has none at all — `MinBranchLifeRows` forbids every split after row 0, so such an act is parallel
corridors that only the boss joins.

**S6 — act-wide budgets + the allocator.** ✔ **DONE 2026-09-11** — Core `1375b0f`. `StrategicRoomPlan`
(`RoomBudget{Target,Min,Max}`, `StrategicRoomRules`, `StrategicRoomSpec`, `RoomShortfall`, `ForcedRoom`) and
`StrategicRoomAllocator`, with 36 tests. **A budget is a map-wide count, and that is a different sentence from a
per-path minimum** — deliberately so: "every route holds two elites" can only be kept by a row every route
crosses, which is what the gate funnels are and what they cost the act's shape. "The act holds eight elites, at
least six, at most ten" needs nothing inserted, so the topology stays the one the walk drew and what a single
route holds becomes S8's *measurement* instead of a manufactured promise.
**The score** over the roles a room may legally hold is `RouteWeight × ActBudgetNeed × LocalDiversity`.
`RouteWeight` reads the lane profile S5 bound to the strand: **the lane wins where it names a kind, the act's own
`KindWeights` answers where the lane is silent, and an authored 0 means "never on this route"** — a sentence a
lane could not say while a lane was a column. `ActBudgetNeed` is the act's PACE, not its count: the density the
budget asked for (`Target / eligible rooms`) against the density still wanted, so on schedule scores exactly like
no budget at all (100 %), behind scores higher, and at target it drops to `AtTargetNeedPercent` — a target is not
a ceiling. `LocalDiversity` penalizes a role already standing next door (25 %) and penalizes the other side of
the same fork harder (40 %), because a choice between two shops is not a choice and is invisible in every count
of what an act holds. Combat repeats freely (it is the act's rhythm; penalizing it would push every other role
upward everywhere and quietly override the authored weights).
**Why the document's two weight tables are not multiplied.** §15's `BaseActWeight × StrandAffinity` reads
literally as a product, but both are ABSOLUTE tables in this codebase, and a product squares the author's intent:
a role the act weights 7 and the lane weights 7 would be 49× one both merely allow at 1, a ratio nobody wrote
down. A lane here says what is DIFFERENT about a route, so it overrides per kind and is silent about the rest.
**Eligibility is a hard filter, never a multiplier**: depth gate, ceiling, the route's refusal, and the ban on a
shop or a rest after itself. Minimums are placed **first**, narrowest role first (fewest legal rooms), before
anything is drawn by weight — and a minimum is placed *on the strength of being a minimum*: silence in every
weight table is not a refusal, so "at least two workbenches" works without also authoring a weight, while an
authored 0 still refuses. **Combat is the filler but never the repair target:** a room with nothing legal left
goes to what its ROUTE wants most, which on an errand lane is a shop.
**Nothing throws over an act it cannot satisfy.** An unkept minimum is a `RoomShortfall` (with the reason that
actually blocked it) and a room with no legal role is a `ForcedRoom` naming the rule that yielded — cheapest
first: a ceiling, then a no-repeat ban, then the route's flavour, then the depth gate last. Same reasoning as
`StrategicTopology.Crossings`: an allocator that throws cannot report WHICH promise failed, which is the only
useful thing to say. `plan.Honoured` is the single bit S7's validator, S10's repair and S13's report start from.
*Proved:* **22 000 acts** — every BnB length × 2 000, four budget sets × 1 000, four depth-gate sets × 1 000,
four rule extremes × 1 000 — on one invariant: **every room holds a role, and every rule that did not hold named
itself** (a gate that yielded, a ceiling overshot, a minimum unkept, two shops adjacent — each has to appear in
`Forced` or `Shortfalls`). Run suite **714** green (+36), `dotnet format` 0; the v0.0.0 golden untouched (two new
files, nothing tracked modified).
**The lane effect, as a number for S13** (2 000 Act III acts, rooms grouped by the flavour their route carries):
gauntlet **62 %** combat · wilds 38 % · hoard 41 % · errands **23 %**; shops 0 % on the gauntlet (an authored
refusal) against **13.5 %** on the errand routes; treasure 16.7 % on the hoard against 3.5 % on the gauntlet.
Under `column % LaneProfiles.Count` this table could not be produced at all. And with `AtTargetNeedPercent = 0`
plus `MaxNeedPercent = 100` the act lands on its targets almost exactly (Elite 6.98 against a target of 7), which
is the knob for an act that should hit its numbers rather than breathe around them.
**Open for S12's tuning pass, not a defect:** the BnB budget numbers themselves. The document (§13) explicitly
warns against deriving them by multiplying the old per-path minimums, and the numbers used above are test specs.

**S7 — depth bands + the spec validator.** ✔ **DONE 2026-09-11** — Core `8188443`. `DepthBandBudget` beside
`RoomBudget`, `StrategicActSpec` (the act as one authored thing) and `StrategicActSpecValidator`, with 50 tests.
**An act-wide budget is silent about depth, and a number silent about depth is satisfied by every shop standing in
the opening third.** A band is the smallest sentence that forbids it: the same `Min`/`Target`/`Max` vocabulary over
one slice of `MapDepth.Percent` — the measure every authored gate already uses, so a band and a gate speak one
language. A band is the half-open range `[Start, End)` so quarters written 0-25 / 25-50 do not both claim the 25 %
row; the band ending at 100 includes 100 or the act's deepest row would fall in none; bands **may not overlap**
(refused by name, because otherwise the order of a list would be a rule nobody wrote); and they **need not cover
the act** — a room in no band is a room no band has an opinion about, not an error.
**Three places a band enters the allocator, and only one of them is a score.** A band ceiling is a HARD FILTER
exactly as the act's is, because "at most one shop this early" has to be a rule rather than a hope.
`BandBudgetNeed` is the same pace arithmetic over the band's own rooms — extracted into one shared `Pace` rather
than copied, so "the act is behind on elites" and "this third of it is" cannot drift apart. And a band minimum is a
promise of the same kind as the act's, and a **narrower** one: its rooms are a subset, so S6's existing
"fewest legal rooms first" ordering puts it ahead by its own rule rather than by a new one. A band minimum **counts
toward the act's own** — "at least three elites in the last quarter" plus "at least four in the act" is four
elites, not seven, which falls out of there being one count per role.
**A band never lifts a depth gate** (source document §14, its own example to the number): a role earliest at 35 %
cannot stand in a 0-25 % band however loudly that band asks, so such a band asks for nothing — and that is now
said before a seed is spent rather than rediscovered once per act.
*Regression guard:* bands that ask for nothing score **bit-identically** to no bands at all (the need factor
cancels exactly at 100 %), asserted over 300 acts, so every seed recorded under S6 is untouched.
**The validator's one interesting decision is that it has two answers, and they mean different things.**
`IMPOSSIBLE` = the WIDEST act this spec permits cannot satisfy it, so no seed can — a defect. `TIGHT` = the widest
can and the NARROWEST cannot, so some seeds will report a shortfall and some will not. That is why a width is a
range here and not a number: the walk draws its opening width per seed and stays between `MinWidth` and `MaxWidth`
for the whole act, so `rows × MinWidth` and `rows × MaxWidth` are both genuinely reachable and both honest bounds.
It catches S4's short-act refusal (generalized, and asserted to agree with the generator's own throw), an act no
row of which may ever fork (S5's 4-row finding, as a `TIGHT`), a band no row of an act this long falls into — with
the depths the act *does* have, because that is the number the author must author against — a band minimum above
the act's ceiling, bands that cover the act and cannot between them reach its floor, a minimum or a band weighed
against what a depth gate leaves, a role **every** lane profile weights 0 (an authored 0 means "never on this
route"), a role listed as both repeating freely and never repeating, and **a target nothing weights** — which can
place nothing at all, since a target bends a draw and cannot create one, and was the subtlest trap in the whole
spec vocabulary. It **reports rather than throws** for the reason `Crossings` and `RoomShortfall` do;
`ThrowIfImpossible` is the gate S11's export validation wants. A MALFORMED spec (overlapping bands, a negative
width) comes back as an `IMPOSSIBLE` reason rather than an exception, because a UI asking "is this act buildable"
wants one list and not a list plus a catch.
**`StrategicActSpec` is new vocabulary and deliberate:** "can this act hold two shops in its opening quarter" is a
question about length, widths, gates, budgets and lanes at once, and a validator taking six loose parameters is a
validator nobody calls from a UI. S11 binds it onto `RunAct`/`RunBlueprint`; it knows nothing about documents or
saves yet.
*Proved:* **11 000 banded acts** — four band configurations × 1 000 and every BnB length × 1 000 — on S6's single
invariant, now grown band clauses (a band ceiling overshot or a band minimum unkept has to name itself *and* name
the band). A band demanding 20 shops in six rows reports **1 000 shortfalls and 0 forced rooms**. A 4-row act has
**exactly one quarter with no room in it, in every seed** — precisely the case the validator now refuses up front.
And **2 000 acts** over the four BnB lengths under a spec the validator calls clean: **2 000 honoured**, which is
the number that says how close "clean" and "honoured" actually are, since the validator is arithmetic and
therefore necessary and not sufficient. Run suite **764** green (+50), `dotnet format` 0; the v0.0.0 golden
untouched — nothing outside the strategic path is referenced by anything else.
**Open for S12, not a defect:** `NodeRefMinimumDepthPercent` and `EncounterMinimumDepthPercent` stay authoritative
as the document asks, and they are read where the *content* is realized rather than where a role is placed — so
they enter the strategic path at S11's seam, and the validator will gain them there.

**S8 — PathPressure.** ✔ **DONE 2026-09-14** — Core `StrategicPathPressure.cs` (`PathPressureRules`,
`PressureRoute`, `PathPressureReport`), `StrategicActSpec.PathPressure`, a pressure check in
`StrategicActSpecValidator`, and a graph-agnostic `WeightedPathEvaluator.Score` — 14 tests, Run suite **778**.
This is what replaces `MinEnemiesPerPath` and the per-path role minima: a route must carry enough CHALLENGE,
however it comes by it, and no single role is promised anywhere — which is why nothing has to be inserted
anywhere to keep it. `Minimum` is the promise (reported unkept; S10's repair is what acts on it), `Maximum` is
only ever a remark, because whether a spike is too hard depends on the deck and the relics and the generator can
see neither.

Three decisions worth the ink. **Pressure is authored in whole points** — the plan's 1.0 / 1.5 / 2.5 is
10 / 15 / 25 and a floor of 11 is 110 — for the reason `StrategicRoomRules` gives for its percentages: a map is a
contract with a seed, and a threshold a route clears on one machine and misses on another is a bug nobody can
reproduce. **The DP now takes a graph, not a `RunMap`**: the same pass scores a `StrategicTopology` (which has no
RunMap anywhere near it until S11) and a finished v0.0.0 map, which is what makes the two generators' numbers
comparable at all — and it reports the extreme ROUTES, not just their scores, because "worth 9, and 11 was
promised" is only actionable once you know which rooms to look at. **The spec validator says IMPOSSIBLE and
never TIGHT here:** the richest route a spec can permit is arithmetic (one room per row, each the most demanding
role its depth gate and the act's own ceiling allow), a FLOOR on the thin route is not — it depends on where the
seed put the elites — so the floor is checked on the finished plan and repaired there.

**MEASURED — what v0.0.0 actually asks, with the plan's own table (Combat 10 / MultiCombat 15 / Elite 25),
100 seeds per act:**

```
              thinnest route        richest route       spread of the act
  Act I     120..150 (mean 131)   140..190 (mean 162)   0..54 %  (mean 24 %)
  Act II    160..190 (mean 172)   180..230 (mean 207)   3..32 %  (mean 20 %)
  Act III   185..220 (mean 195)   205..255 (mean 237)  10..35 %  (mean 22 %)
  Act IV    265..305 (mean 275)   275..345 (mean 318)   4..30 %  (mean 16 %)
  Act V       0 (three boss rooms — the gauntlet act has no chosen room at all)
```

§6 guessed Act I's worst route at 13 and its richest at 16 from the measured totals; the instrument says 13.1 and
16.2. **The starting numbers in §6 stand as authored** (they are now measurements rather than estimates), and
seed 1 of Act I shows what the spread is made of:

```
  thinnest 130 · CTC?C$?RTCCM?E?C$CRTC?CB
  richest  160 · CCC?C$CRTCCMCE?C$CRTC?CB      ← three rooms out of twenty-four differ
```

For comparison, the strategic generator on a 23-row act with the same table over 500 seeds: thinnest **30..170**
(mean 101), spread **12..650 %** (mean 119 %). Both halves of that are the point. The routes genuinely differ —
v0.0.0's fork decides three rooms, this one decides the act — and the thin end really can fall to 30, which is
the walk-round-everything route the old per-path minimums existed to forbid. That is S10's job, and S12 authors
the floor per act; on today's evidence Act I's floor wants to sit near v0.0.0's own thin route (≈ 120), not at
the 110 §6 sketched.

**S9 — fork quality, measurement only.** ✔ **DONE 2026-09-14** — Core `ChoiceSignature.cs`
(`ChoiceSignature`, `WeightedSignature`, `ForkQualityRules`, `BranchQuality`, `ForkQuality`,
`ForkQualityReport`, `ForkQualityEvaluator`), `StrategicActSpec.ForkQuality`, two more checks in
`StrategicActSpecValidator` — 12 tests, Run suite **790**. Reported and NOT repaired, deliberately (source
document PR 8): a ranking has to exist and be trusted before anything acts on it, or S10 becomes a generator
optimizing a number instead of a map.

A room is scored on the document's five dimensions (§21, in points: its +1 is 10), a branch is the EXPECTATION
over the futures it leads into for the next `HorizonRows` rows, and a fork's contrast is the weighted Manhattan
distance between two such futures (§23). Three decisions. **The expectation is kept as an exact fraction** — a
weighted sum over a route count — and divided exactly once, when two branches are compared by cross-
multiplication; subtracting two rounded averages would make a fork's score move when a branch gains a room that
changes nothing about it. **A fork is scored by its WEAKEST pair**, not its sharpest: a three-way fork with one
redundant pair offers two real options and a decoy, and the decoy is the defect, because it is the side a player
spends thought on for nothing. **The horizon is capped at six rows** rather than merely documented: the futures
of a branch are counted exactly, there are exponentially many of them, and past six the cross-multiplied
comparison stops fitting in a `long` on a very wide act — a silently overflowed score is a fork ranked at random.

The spec validator gains the two things that can be said before a seed is spent: a threshold above the sharpest
pairing the act's own roles could ever draw, repeated for every row of the horizon, is IMPOSSIBLE (110 × 3 = 330
for the BnB roles); a threshold on an act that cannot fork at all is TIGHT and inert, the same sentence S7 says
about such an act's fork weights.

**MEASURED — and this is the number the whole rework exists for.** v0.0.0, 100 seeds per act, with the
document's own signature table:

```
             forks per act   contrast (mean)   HOLLOW — forks whose two ways are the same future
  Act I          15.3         0..90 (10.4)     72.6 %
  Act II         16.3         0..87 ( 9.3)     75.2 %
  Act III        16.5         0..90 (11.3)     71.2 %
  Act IV         23.4         0..90 ( 8.2)     79.6 %
  Act V           none (the gauntlet act is one room wide)
```

Act I, seed 1: `forks 12 · contrast 0..45 (mean 9) · hollow 9`. §1 said "twelve forks between identical rooms"
from reading the grid; the instrument says nine of the twelve decide nothing at all, and the other three decide
very little. The strategic generator over 500 acts of the same length: **2.7 forks per act, contrast 0..210
(mean 84), hollow 3.5 %**. Fewer forks, and each of them a choice — which is the trade S4 made when it drew a
row as an operation instead of a width, now with a number on it.

**S10 — repair.** ✔ **DONE 2026-09-14** — Core `MapRepair.cs` (`MapDefects`, `RepairOperation`, `RepairRules`,
`RepairedAct`, `MapRepair`) and `StrategicMapGenerator.cs` (`GeneratedAct`, `StrategicMapGenerationException`),
plus `StrategicActSpec.Repair` / `.MaxGenerationAttempts` — 25 tests, Run suite **815**. Repairs A and B only;
C–F are deferred as planned.

**The repair is a first-improvement hill climb over `MapDefects`, and the four defects are compared
LEXICOGRAPHICALLY rather than added up.** An unkept minimum, a room holding an illegal role, a route under the
floor and a fork that decides nothing are not commensurable, nobody has evidence for an exchange rate between
them, and a repair that traded a promise for a prettier fork would be a repair nobody asked for. Two properties
then fall out of the shape instead of being asserted about it: a repair can never make an act worse, because a
change is only taken when the comparison says better; and the same act repairs the same way every time, because
nothing in it draws a random number.

**Legality is not re-implemented.** The allocator's four hard filters became `internal` and the repair asks
*those*, so `Forced` — the count of rooms whose role breaks one — makes an illegal change reject itself through
the comparison rather than through a second copy of the rulebook that could disagree with the first. The one
reconstruction is which of the two ROUTE rules applies: the allocator asks the weaker question of a role it is
placing to keep a promise (silence is not a refusal) and the stronger one of a role it is merely drawing, so a
role still at or under its minimum is treated as a promise here too.

**The Repair stream stays unused, deliberately.** S3 reserved one (§26) and a hill climb over a heuristically
ordered candidate list has nothing to spend it on — a repair that depends on a draw is a repair nobody can
reason about, and ordering the candidates is a better use of the information than shuffling them. The ordering
is what makes the source document's own worked example (§42) come out in ONE swap: a fork offering the same shop
twice, an event standing where it decides nothing, and the candidate tried first is the one that stands furthest
from what the other way already offers.

**Failing is loud, and it is the author's choice.** An act that promises nothing cannot break a promise, so the
defaults never throw. Where something *was* promised: up to `MaxGenerationAttempts` whole acts, each from
`MapSeedStreams.Attempt(n)`, and then a `StrategicMapGenerationException` carrying §25's six facts — seed,
attempts, what went unkept, what the act held, what its thinnest route was worth, what its forks were worth. A
spec no seed can satisfy is refused before the first seed is spent, in the S7 validator's words: "six elites in
a twelve-row act" is an answer, "the generator gave up" is not.

*Proved:* **10 000 acts (four BnB lengths × 2 500 seeds, floor 120, fork threshold 40) in 42 s, zero failures**,
4.2 % needing a second whole act and 1.6 repairs per act. The suite keeps 3 000 of them as a standing gate, with
every promise read back off the act that came out — budgets inside their bounds, no shortfall, no forced room,
every route over the floor, every fork over the threshold, the shape untouched and zero crossings.

**S11 — the document seam + the run remembers its generator.** ✔ **DONE 2026-09-14** —
`StrategicMapGeneration` on `RunAct`/`RunBlueprint`, `StrategicMapRealizer`, `MapGenerators`,
`RunSaveData.MapGenerator`, `RunState.GeneratedMapGenerator`, the `mapGenerator` parameter through
`CreateInitialRun`/`BuildActPlan`/`BuildRunMap`/`RunPlayback.Start`/`.Resume`, `ReadOnlySetJsonConverterFactory`,
two validator checks + the export gate, the whole second-generator section of `MapRulesTab.razor` (with the
preview switchable between generators), and `docs/godot-export-contract.md`. 12 tests; Run **816**, Sandbox
**384**.

**The content half never moved.** A strategic spec says how many rooms of each kind and where; it says nothing
about which fight stands in one, and it was never going to: `MapGenerationSpec` keeps the encounter pools, the
node refs and the balance targets, and `StrategicMapRealizer` fills the strategic act's rooms by CALLING the
rule-based generator's own two selections rather than copying them. So an act from either generator holds content
drawn by the same rules, and a document with strategic rules and no `MapGeneration` is refused by name — it would
be an act with rooms and nothing to put in them.

**`RunMap.Layout` is now emitted** (§2.4), because S4's promise that the edges do not cross holds only when each
row is drawn in column order, and a promise nobody records is one the frontends cannot keep. The rule-based
generator still emits none: it never promised an order, and inventing one for it would be a claim.

**The choice is part of the run, not of the menu** (§4b). `RunSaveData.MapGenerator` is an `init` property with
no migration — a save written before this step has nothing there, and nothing there means v0.0.0 — and
`RunPlayback.Resume` passes the saved value back, so a run resumed after a restart rebuilds ITS map rather than
the one the current default would draw. The act-plan cache got the generator in its key for the same reason.
Proved by test: a run started on v0.0.1, saved, and rebuilt from the save comes back byte-identical **including
what stands in every room**, and a run with no generator recorded rebuilds identically to an explicit v0.0.0.

**One serializer gap**, found by writing the round trip rather than by reasoning about it: `System.Text.Json`
writes an `IReadOnlySet<T>` and cannot read one back, so an act's "these roles may never repeat" would have saved
and then failed to load. Taught rather than worked around — a converter factory, and the properties keep saying
set, because membership without order or duplicates is what they ARE.

**S12 — BnB integration.** ✔ **DONE 2026-09-14** — bnb-content `ActRules` (the four acts authored for both
generators), `MapSpecBuilder.Strategic`, `BlueprintAssembler`, the manifests' `steps_before_boss`, a
`--generator` flag on the walker and `MAP_DUMP_GENERATOR` on the probe; Core `RunSetup` (one defect, below).
New `Tests/StrategicMapTests.cs` (26 cases) plus rewrites in `ActSeamTests`, `ActFiveGauntletTests` and
`WholeRunTests`; bnb-content suite **1495**.

**One deviation from the step as written, and it is the whole shape of the step.** The plan said `ActRules`
*loses* `PerPathMinimums` / `PerPathMaximums`. It keeps them, frozen, because §4b decided the opposite thing
three sections earlier: both generators ship and the player picks between them at "New run ▸". A v0.0.0 whose
per-path table had been deleted is not the generator every run has used so far — it is a third, worse generator
that nobody chose, and the comparison the whole arc is for would be a comparison against nothing. So S12 is
**additive**: the act gains a second description beside the first, `Tests/Golden/map-v0.0.0.txt` is untouched,
and what actually retires is the per-path table's status as *the design* — which is what the tests now say.
Deleting the configuration is S14's business, and S14 should reconsider whether it ever should be.

`MapSpecBuilder.FreeRows` and `MinimumFreeRows` did go. The formula's floor won for every act in the game
(§1.1), so it described nothing; v0.0.0's backbone is now the literal five it always produced, written down as
`ActRules.RuleBasedFreeRows`, and `steps_before_boss` is re-authored to 23 / 24 / 25 / 35 where it states the
act's real length and is read by the strategic spec. The treasure pool is sized off the Treasure budget's `Max`
(§2.5), which is an act-wide question being answered with an act-wide number for the first time.

**A defect this step found, in Core rather than in BnB.** `RunSetup.BuildActPlan` read
`act.StrategicMapGeneration ?? blueprint.StrategicMapGeneration`. An act's two specs are one description, and
falling back on only one half of it paired the Divine Ledger's content rules — three bosses, no treasure room,
no combat pool — with Act I's twenty-three-row shape, which then asked for a treasure the act does not have.
The two now fall back together: an act that authors its own map rules reads its own, and only an act that
declares none at all reads the document's. Found by a test that walks Act V on v0.0.1, not by reading the code.

**MEASURED — the four acts, 200 seeds each, on the authored numbers:**

```
          generated  clean  attempts  repairs   thinnest route      weakest fork      hollow
  Act I    200/200   200/200  ≤ 0     1.2 (≤4)  120..165 (129)    30..170 (mean 60)   0.0 %
  Act II   200/200   200/200  ≤ 1     2.9 (≤9)  160..185 (164)    30..175 (mean 57)   0.0 %
  Act III  200/200   200/200  ≤ 3     4.1 (≤11) 185..205 (187)    30..120 (mean 55)   0.0 %
  Act IV   200/200   200/200  ≤ 2     4.8 (≤12) 265..285 (267)    30..115 (mean 50)   0.0 %
```

Not one seed in eight hundred failed to generate and not one act came out with a defect still on it. The floors
are v0.0.0's own thinnest routes, measured in S8 (120 / 160 / 185 / 265), so no walk through a strategic act is
worth less than the easiest walk through the old one — and the fork threshold of 30 turns S9's headline number
over completely: **0 % hollow forks against v0.0.0's 72–80 %**.

**And §1.2, inverted.** The defect the rework exists for was that four of Act I's seven room types were
identical on every single route. The same measurement — one reverse-topological pass per role over all routes at
once — on the strategic acts:

```
  v0.0.0  Act I  Elite 1..1  MultiCombat 1..1  Rest 2..2  Shop 2..2  Treasure 2..3  Event 3..5  Combat  9..12
  v0.0.1  Act I  Elite 0..2  MultiCombat 0..2  Rest 1..3  Shop 0..3  Treasure 0..6  Event 2..4  Combat  8..12
  v0.0.1  Act IV Elite 2..7  MultiCombat 0..7  Rest 3..4  Shop 1..4  Treasure 1..4  Event 4..8  Combat  9..14
```

Zero room types identical on every route, on every act and every seed measured, and a route that meets no shop
and no jar at all is an ordinary Act I. What did NOT improve is the pressure SPREAD (Act IV: 15 % against
v0.0.0's 16 %), and that is the floor doing its job rather than a disappointment — a promise about the thinnest
route is a promise that compresses the low end. The claim "the routes differ" belongs to the table above and to
the fork contrast, not to the spread, and the tests say so.

**Open for S13, not defects:** Act III and Act IV sit at 4–5 repairs an act with the ceiling at 12, which is
comfortable but not roomy; and the thinnest route lands within a few points of the floor on most seeds, because
the repair stops at the promise. Both are worth a report before they are worth a change.

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
RoomBudgets (map-wide)        Target  Min  Max      PathPressure (points per room, S8)
  Elite                          5     3    8         Combat        10
  Shop                           5     4    7         MultiCombat   15
  Rest                           6     4    9         Elite         25
  Treasure                       6     4    9         everything else 0
  Event                         11     8   15
  MultiCombat                    4     2    7       Minimum 110  (v0.0.0's worst route: 131 measured)
  Combat                    the filler (~29)        Maximum 200  (v0.0.0's richest:     162 measured)
```

Acts II–IV scale the same way off their own measured totals; S8's table of what v0.0.0 asks per act is the
evidence to scale them against. Topology: `ContinueWeight 6`, `SplitWeight 2`,
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
