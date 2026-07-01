# Run Engine Modularity Audit

Goal: reach a point where **game design needs no code** — every design idea is expressible by composing
existing building blocks (data), never by writing an engine class or a runtime lambda.

Key distinction: a **designer-facing** lambda (the author must pass a `Func`/`Action` to express an idea) is
a blocker. An **internal** lambda hidden behind a named data helper (e.g. `RunSchedule.WhenCounterAtLeast`,
`RunCombat.HeroStartsWithStatus`, `RunDeckMappers.UpgradeSuffix`) is fine — the designer references a named
block, the lambda is engine implementation.

## KEEP — not a blocker

- `Situation(Action<SituationBuilder>)` / `Choice(Action<ChoiceBuilder>)`: build-time lambdas that assemble
  data; never run as game logic, never serialized. A JSON loader would replace the builder, not these.
- `RunCombat.Custom`, `RewardModifiers.Custom/TransformEach`: deliberate power escape hatches (like combat's
  `SideEffectNode`). Keep — as long as the common cases have named data helpers.
- `RunDeckMappers.Identity/UpgradeSuffix`: already named data strategies; configured once.
- `EventChoice.Require(Func)`: already has the data alternative `Require(IRunExpression<bool>)`.

## HOST — implemented once by the game host, not per design idea

`ICombatDriver`, `IRunEntityChooser` (player I/O), new `INodeResolver`. Correctly code.

## REPLACE — these still force designer-facing code

| # | Seam | Data replacement |
|---|------|------------------|
| R1 | `TriggeredRunEffect<TEvent>(Func<evt,run,effects>)` (relics, installed programs, rules) | Declarative triggered program: `{ EventType, condition: IRunExpression<bool>, effect templates }`, evaluated with the event in `RunEvalContext` at dispatch. Effects/condition read event data via named accessors. |
| R2 | `RunSchedule.When<TEvent>(Func<evt,bool>)` | `IRunExpression<bool>` overload (event in context). Feasible today via `EventValue`. |
| R3 | `ExpandRunEffect(Func<run,effects>)` (ForEach) | Data `ForEach(selector, effectTemplate)` + an iteration-target selector/expression so body effects reference "the current card" (like combat's `IterationTarget`). |
| R4 | `WhereSelector(Func<T,bool>)` (`.Where`) | Selector filter vocabulary as data: predicate expressions over tag/kind/upgrade/memory/comparison (WithTag/OfKind/Upgradable are the start; add memory/comparison/and/or). |
| R5 | `EventValue(Func<TEvent,int>)`, `Sum(Func<T,int>)` | Named value catalog: per-event field accessors (`RunEventValues.CombatDamageTaken`…) and per-entity accessors (`CardValue.UpgradeLevel`, `CardValue.Memory(key)`) as data. |
| R6 | `OfferRewardRunEffect.GenerateOffers Func`, `Rewards.FromPool` | `RewardTable` as a serializable data object (offer pool + draw count); `OfferReward` takes a table. |
| R7 | `CombatNodePayload(Func<run,Playthrough>)` (a whole fight is a closure) | `EncounterDefinition` as data (enemies/pools/setup) referenced by id; bridge builds the Playthrough from data + run projection. Needs combat content addressable by id. |

## The other half of "covers everything"

Removing designer-facing lambdas is necessary but not sufficient. To never need a new `IRunEffectRequest`
(a new verb class), the built-in effect/expression catalog must be complete enough. Measured against the
design doc §10, missing verbs include: MaxHP ±, relic remove/disable, consumables. (Map & quests are
deliberately deferred to their own foundations.) A **catalog-gap pass** is part of the goal.

## Sequence (each stage removes code for a whole class of ideas)

1. **R2 → R1** (declarative event-reading triggered program; needs a starter R5 event-value catalog).
2. **R5 + R4** (value & filter vocabulary) — makes R1/R3 powerful enough.
3. **R3** (data ForEach with iteration target).
4. **R6** (reward tables).
5. **Catalog-gap pass** (missing verbs).
6. **R7** (encounters as data) — the big combat-side step.
7. *(Then)* serialization + content registry/validation + a run Sandbox UI = the real "never touch code".

## Status

- **R2 done** — `RunSchedule.When(IRunExpression<bool>)`.
- **R5 (starter) done** — `RunEventValues` named event-field accessors (int + bool).
- **R1 done** — `DataTriggeredRunEffect` + `RunPrograms.On/When` + `RunEffectTemplates` (condition and effects
  read the event at dispatch). `StandardRelics` (Bloodstone/Leech) now authored declaratively; the old
  `RelicPrograms.GainResourceOn` and the `TriggeredRunEffect` lambda are no longer needed for content
  (the lambda stays only as an internal/escape mechanism).
- **R4 done** — `RunEvalContext.Card` (card in scope) + `.Matching(IRunExpression<bool>)` selector; the
  WithTag/OfKind/Upgradable shorthands are now predicates. `.Where(Func)` stays as escape.
- **R5 done** — `CardValue` accessors (UpgradeLevel/Memory/HasTag/IsKind/Upgraded) compose with the ordinary
  combinators; `RunExpr.SumCards` is the data-first Sum over cards.
- **R3 done** — contexts unified (chose option b): `RunSelectorContext` removed; `RunEvalContext` now carries
  Run + Event + Card + Chooser and is the single context for expressions, selectors, and templates.
  `RunSelectors.Instance(id)` (single-copy selector, survives to drain); "this card" templates
  (UpgradeThisCard/TagThisCard/RemoveThisCard/SetThisCardMemory/TransformThisCard); `ForEachCardRunEffect`
  applies templates per selected card with that card in scope. `.ForEachCard(selector, templates)` sugar;
  the lambda overload stays as escape.
- **R6 done** — reward generation is data: `IRewardSource` (`RewardTable.Of` / `FromPool` / `Custom` escape);
  `OfferRewardRunEffect` takes it; the generation Func is gone from content.
- **Catalog-gap (partial) done** — `ChangeMaxHealthRunEffect` (GainMaxHealth heals too; LoseMaxHealth caps,
  min 1) and `RemoveRelicRunEffect`. Still open: relic disable (needs per-relic active state) and consumables
  (a run inventory of one-shot items) — each its own slice.
- **R7a done** — `AutoPlayCombatDriver`: plays a combat headlessly with no authored script (hero plays hand
  at the first living enemy; enemies cycle their actions; round cap guards stalemate), on top of the existing
  `InteractiveCombat`. Removes the script dependency and enables headless run simulation.
- **R7b done** — data-defined encounters: `CombatContentLibrary` (authored-once card/action/status defs),
  `EncounterDefinition` (enemy roster + hero combat resources), `EncounterCatalog` assembles the fight from
  library + encounter + run projection. A combat node carries an `EncounterRef(EncounterId)`; the resolver
  resolves it via an optional catalog (Func payload kept as escape). `StandardRunPackage(driver, encounters)`.

**All REPLACE seams (R1-R7) done.** Every designer-facing lambda now has a data path; the run map (events +
combats) is expressible as data.

Post-audit slices delivered:
- **Relic-disable + consumables** — done.
- **Content registry** — done: `RunContentRegistry` (events/encounters/relics/reward tables by id) +
  `EventRef` + seal-time map validation.

Endgame remaining: **serialization** (multi-slice) + a run Sandbox UI. Serialization order:
- S0 — make data nodes serialization-friendly: several expression/selector classes keep operands in PRIVATE
  fields (e.g. AddExpression._left/_right); System.Text.Json cannot read those. Expose them as public
  properties / records. Prerequisite for everything below.
- S0 — done: data nodes expose operands as public properties.
- S1 — done: `RunJson` — `RunJsonRegistry` (kind ↔ type) + `PolymorphicRunJsonConverter<TBase>` (envelope
  `{"kind":..,"value":..}`, recursive). The expression family (values + conditions) is registered and
  round-trips (verified by evaluation-equality); escapes (Func-backed) throw NotSupportedException.
- S2 — done: effects, card selectors, and reward sources registered; nested effect lists / expressions /
  selectors / RunPool entries recurse. Round-trips verified (idempotence + a functional deserialize-and-run).
- S3 — done: event accessors are serializable — `RunEventFields` (string field-key catalog + readers) and
  key-based `EventIntValueExpression` / `EventBoolValueExpression`; `RunEventValues` rebuilt on them. The old
  Func-backed `RunExpr.EventValue` stays as the escape.

**Serialization S0-S3 complete.** The run content tree — expressions (incl. event-reading), effects,
selectors, reward sources — round-trips as JSON. Non-serializable by design (escapes / content objects):
Func-backed nodes (`.Where`, `EventValue`, `Custom` modifiers, `ExpandRunEffect`), and effects that embed
content objects (`AddRelic` embeds a RelicInstance; `InstallRunProgram`; `AddRewardModifier`/`AddCombatModifier`).
Follow-ups delivered: effect-templates are now concrete public serializable types (LiteralEffectTemplate,
GainResource/Heal/Damage, and the "this card" templates), so ForEachCard serializes; a Custom template stays as
the escape. Granting a relic is now data via AddRelicByIdRunEffect (resolves from the run's content catalog,
which RunState carries run-scoped like the chooser); AddRelicRunEffect(RelicInstance) stays as the escape.

Combat card EffectPrograms remain combat-authoring (referenced by id). Remaining endgame: a run Sandbox UI.
Note: full relic *definition* serialization is intentionally out (a relic's combat contributions are combat
content) — relics are authored, then referenced/granted by id.
