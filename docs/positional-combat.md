# Positional (spatial) combat + player board — additive plan

Goal: add **2D positioning** (a grid: adjacency, rows/columns, front/back, distance) AND a **persistent
player-controlled board of units** so board/lane roguelike deckbuilders (Monster Train, Inscryption, Wildfrost,
Across the Obelisk) become expressible — the two biggest structural gaps from the top-5 analysis (Tier-1 #1 +
#2). Hard requirement: **do not constrain the engine in its current form.** Positioning and the board are a
strictly *opt-in overlay*; a combat that uses neither must behave byte-for-byte as it does today.

## Non-constraint invariants (the hard guarantees, checked every phase)
1. **Position is optional (nullable), the board is opt-in.** No positions + a single hero (as today) ⇒ every
   existing selector / effect / rule / turn behaves identically. Flat team-arena combat is unchanged.
2. **Everything new is additive.** New position field, selectors, effect nodes, expressions, events, and the
   player-unit machinery are *registered alongside* the existing ones (CombatJson + CombatProgramModel +
   RunJson catalogs). Nothing existing is modified, renamed, or removed.
3. **Graceful on absence.** A positional selector in a combat with no positions resolves to *empty* (never
   throws). Flat combats never reference the new selectors, so they never fire there.
4. **Serialization is back-compatible.** `Position` and the run's unit roster default to absent; existing
   blueprints / run docs round-trip unchanged (no new required fields).
5. **Turn + team model untouched at its core.** Teams and the hero-turn→enemy-turns loop stay. Player units act
   through the *existing* enemy-action/intent machinery applied to the player team — no new turn engine.
6. **The full existing test suite passes unmodified at every phase.** New behavior ships behind new content only.

## Position model — 2D grid (X, Y)  *(chosen over lane+slot)*
Replace the vestigial `CombatantState.PositionKey` (string, only stored, referenced nowhere) with an optional
grid coordinate:

```
CombatPosition(int X, int Y)              // absolute grid cell; interpretation (column/row/depth) is up to selectors
CombatantState.Position : CombatPosition? // null = not placed = today's flat behavior
```

Semantics + choices:
- **Cells are non-exclusive** (a coordinate is a label, several combatants may share it). Keeps P0 trivial and
  additive; occupancy/pushing rules are content concerns layered on later, not a core invariant.
- **X = column/lane, Y = depth/row** by convention; "front/back" is computed **team-relative** by selectors
  (front = toward the opposing team along Y). Adjacency/same-row/same-column use absolute coords directly.
- 2D (vs 1D lane+slot) is warranted by **Wildfrost** (front/back rows × position-in-row) and generalizes the
  1D games (Monster Train floors = a column axis; Inscryption = one row of columns).
- `CombatantTargetSelectionContext` already carries the full `CombatState`, so positional selectors query every
  combatant's coord with no plumbing change — verified.

## Phase plan (each bounded, green + pushed; invariants above hold throughout)

### Part A — positional vocabulary (P0–P4): opt-in spatial combat, no persistent units yet

**P0 — Position substrate (data only, ZERO behavior change).**
- `CombatPosition` value + optional `CombatantState.Position` (supersedes the dead `PositionKey`).
- Optional starting position on the authoring shapes (`EncounterEnemy`, `HeroBlueprint`); `ScenarioCombatFactory
  .AddCombatant` sets it (the single placement hook). Absent ⇒ null.
- Serialization round-trip (CombatJson / RunJson) with `Position` absent by default.
- A `CombatantMovedCombatEvent` type (unused yet) for P2/P3.
- **Nothing reads Position yet** → whole suite unchanged. Safe foundation.

**P1 — Positional targeting (selectors). ✅ DONE.** 9 new `ICombatantTargetSelector`s (additive, in
`PositionalTargetSelectors.cs`), each resolving EMPTY when the source is unplaced and skipping any unplaced
candidate: `AdjacentToSource` (Manhattan distance 1, any team), `SameColumnAsSource` / `SameRowAsSource` (same
X / Y, excluding source), `AllInSourceColumn` / `AllInSourceRow` (same X / Y, INCLUDING source — a full line),
`FrontmostEnemyOfSource` / `BackmostEnemyOfSource` (team-relative along Y via `PositionalTargeting.ForwardSign` —
front = enemy end nearest the source's team; ties break by column then id), `NearestEnemyOfSource` (min Manhattan
distance), `OpposingInColumn` (enemies sharing source's X — the lane-duel target). Registered in CombatJson
(`sel.adjacent` … `sel.opposingInColumn`) + the CombatProgramModel catalog (all 9 in `Selectors`; the three
single-target ones also in `SingleTargetSelectorKeys` for scalar condition reads). Tests: resolution on a canonical
grid, team-relative mirror, unplaced-candidate skipping, "no-positions ⇒ empty" for every selector (source
unplaced + source null), JSON round-trip, catalog membership + build↔classify. Full suite green (Core 1289).

**P2 — Positional movement (effects). ✅ DONE.** One core `MoveCombatantEffectRequest` + handler (sets Position,
raises `CombatantMovedCombatEvent` only on an actual change, logs `CombatantMoved`) — every movement node reduces
to it. A single mode-parameterized `MoveCombatantNode<TContext>` (`MovementMode`: `ToAbsolute` reads X/Y exprs;
`TowardEnemies`/`AwayFromEnemies` step along depth via the mover's team-relative `ForwardSign`;
`PushFromSource`/`PullToSource` step along the source→mover depth axis) + a `SwapPositionsNode<TContext>` (exchanges
the first target of each selector). Geometry lives as testable statics in `PositionalTargeting`
(`StepAlongDepthTowardEnemies`, `StepAlongDepthFromSource`) — depth (Y) axis only, X preserved; return null (→
executor skips) when the move is undefined (unplaced mover / no enemy to orient toward). `SummonCombatant` gained an
optional `Position` (request + node + core; silent placement, no move event). Registered additively: handler in
StandardCombatPackage, executors in both registries, `node.moveCombatant`/`node.swapPositions` in CombatJson.
NOT added to the CombatProgramModel visual catalog (Studio on hold) — movement is JSON-escape in the editor for
now. Tests: handler (set/no-op/place), geometry, end-to-end played-card moves (MoveTo/Push/Swap/unplaced no-op),
summon-at-position, JSON round-trip for every mode. Full suite green (Core 1305).

**P3 — Positional reads & reactions. ✅ DONE.** Two int expressions (in `EffectProgramExpressions.cs`, both inert
→ 0 in a flat combat): `CombatantCoordExpression(selector, GridAxis X|Y)` — a target's column/depth ("damage =
your column") — and `GridDistanceExpression(from, to)` — Manhattan distance between two single-target selectors, so
`from = Source, to = FrontmostEnemyOfSource` is "distance to front". Registered in CombatJson (`combatantCoord`,
`gridDistance`). `CombatantMovedCombatEvent` is now a triggerable event: `CombatantMovedTriggeredEffectContext`
(+ target resolver, Source = the moved combatant) + `TriggeredProgramContextAdapters.CombatantMoved` + handler
registered in StandardCombatPackage. The event fires AFTER the move applies, so a positional read on Source inside
the triggered program sees the NEW cell. Tests: coord-as-amount, flat-inert, distance-to-front, Moved-trigger fires
+ reads new cell, Moved never fires in a flat combat, JSON round-trip (both exprs + axis). NOT added to the
CombatProgramModel visual condition/amount catalog (Studio on hold) — usable via hand-built/JSON programs. Full
suite green (Core 1311).

**P4 — Game-shape composition (validation). ✅ DONE.** Three worked-example integration tests
(`PositionalGameShapesTests.cs`) proving the P0–P3 vocabulary expresses the signature spatial mechanics with NO new
engine code: **Monster Train** — a single TurnStarted rule `MoveCombatantNode(AllEnemiesOfSource, TowardEnemies, 1)`
advances the enemy column one row toward the player each turn (verified over two turns); **Inscryption** — a lane
attack `DealDamage(OpposingInColumn)` strikes only the enemy across the hero's column, sparing other lanes;
**Wildfrost** — `DealDamage(FrontmostEnemyOfSource)` hits the front row and the back row is shielded until the front
dies, after which the back becomes frontmost and takes the hit. Full suite green (Core 1314). This closes Part A
(positional vocabulary P0–P4).

### Part B — persistent player-controlled board (P5): the "field your own units" gap (Tier-1 #2)
Builds directly on Part A (positions + summon-at-position). Reuses the **enemy-action/intent** machinery on the
player team so player units act with no new turn engine (invariant #5).

**P5a — Multiple acting player units in one combat. ✅ DONE (no engine code).** Verified the turn loop is already
team-agnostic: `AddCombatant` puts every combatant (any team) into `TurnOrder`, `CombatTurnProcessor` cycles them
by living-combatant regardless of team, and `ExecuteEnemyActionEffectRequest` / triggered programs / selectors are
all relative to the acting `Source`. So a player-team unit beyond the hero already gets its own turn and acts on the
ENEMY team through the existing machinery — invariant #5 (no new turn engine) holds by construction. "Auto-action"
is the idiomatic marker-status-filtered TurnStarted-triggered program (the same pattern enemies/rules already use).
Proven by `PlayerBoardUnitsTests.cs`: a fielded player unit takes its turn (hero → ally → goblin) and strikes the
enemy team while the enemy strikes the player team; multiple player units each act. No Core change needed; full
suite green (Core 1316).

**P5b — Cards that field units onto the board (deck→board). ✅ DONE.** One additive engine primitive: a summon can
be born with innate statuses. `StatusGrant(StatusDefinitionId, Stacks, DurationTurns, Charges)` record +
`SummonCombatantEffectRequest.StartingStatuses` (handler applies them via the normal ApplyStatus pipeline right
after creation) + `SummonCombatantNode.StartingStatuses` (core/node/executor/serialization). A "creature card" is
then just `SummonCombatantNode(PlayerTeam, hp, def, pos, startingStatuses: [marker])` — the summon-at-position (P2)
gives placement, the marker + a registered TurnStarted rule (P5a) gives auto-action. `FieldUnitCardTests.cs` proves
the whole loop: a played card summons a player unit at its cell carrying the marker, it enters the turn order, and
on its turn it strikes the enemy team. Full suite green (Core 1318).

**P5c — Run-level unit roster + run↔combat persistence.** A run holds an optional roster of player units
(definition + carried state: HP, statuses, position). Projected into each combat's player team at start;
survivors reconcile back to the roster after the fight (extends today's hero-HP reconciliation to N units).
`RunBlueprint` gains an optional starting roster; RunJson round-trips it. Absent ⇒ today's single-hero run.

- **P5c-1 — roster data model + storage + seeding + serialization. ✅ DONE.** `RunUnitData` (authored: def id,
  name, max HP, optional position, `StartingStatuses` reusing P5b's `StatusGrant`) + `RunUnit` (live carried state:
  a `HealthState`, position, statuses) + `RunUnitInstanceId`. `RunState` holds the roster (`Units`, `AddUnit` /
  `FindUnit` / `RemoveUnit`, deterministic `unit#N` ids). `RunStart.StartingUnits` (init-only, default empty)
  round-trips through RunJson and is seeded into `RunState.Units` at full HP by `RunSetup`. Absent ⇒ empty roster =
  today's single-hero run. Run 239 green. No combat wiring yet.
- **P5c-2a — Scenario ally support. ✅ DONE.** `AllyBlueprint : CombatantBlueprint` + `ScenarioBlueprint.Allies` +
  `CompiledScenario.Allies`; `ScenarioCombatFactory` adds allies on the PLAYER team (after the hero, before enemies)
  with their position + starting statuses, reusing the generic combatant placement. No driver changes needed:
  `InteractiveCombat.EndTurn` already loops every non-hero combatant (enemy-intent returns null for allies) and an
  ally auto-acts via its marker-TurnStarted rule (P5a) when its turn starts. `ScenarioAllyTests.cs`: a fielded ally
  joins the player team placed on the grid and auto-attacks the enemy on its own turn. Scenario 512 green.
  **P5c-2b (run→combat projection + reconciliation) next.**

**P5d — Unit control model + placement UX-agnostic hooks.** Decide per-unit control: auto (intent-driven, for
Monster Train / Inscryption) vs directed (player chooses the target/lane). Default auto (reuses intents); a
directed hook via the existing `IRunEntityChooser` / combat-driver interaction points if a game needs it. Engine
hooks only — no Studio UI (the UI is on hold).

**P5e — Game-shape composition for board games.** Worked examples: a Monster-Train-style floor defense (field
units, enemies ascend, units auto-fight) and an Inscryption-style lane board (creatures face across columns),
end-to-end as data. Proves Part B against real games.

## What each phase unlocks
- After **P1+P2:** front/back targeting, adjacency AoE, push/pull, lane/column effects — the positional combat
  vocabulary usable by cards/enemy-actions/relics/statuses today, opt-in.
- After **P4:** Monster-Train floors, Inscryption lane duels, Wildfrost rows authorable as *enemy-side* / single-
  hero spatial combat.
- After **P5:** full board deckbuilders — field your own persistent units, they fight in space across combats.

## Risk / effort
- **P0–P4 are low risk** (additive + optional; flat combats never touch the new paths; invariants make
  regressions structurally hard).
- **P5 is the heavy part** — run↔combat unit persistence and multi-unit reconciliation are new run-layer surface.
  Its risk is contained by reusing the enemy-action/intent machinery for player units (no new turn engine) and by
  keeping the roster optional (absent ⇒ today's single-hero run).
- Only *shape* decision locked: 2D grid, non-exclusive cells. Revisit only if a game needs cell exclusivity with
  automatic collision resolution (handle as content/rules when it arises).
