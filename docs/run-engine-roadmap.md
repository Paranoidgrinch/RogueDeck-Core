# Run Engine Roadmap

The run/meta layer above combat. Derived from the "Generic Run & Event Engine Design" idea document,
reconciled with two standing principles:

1. **Run engine = define events (combat is one kind) + run them SEQUENTIALLY.** Path arrangement
   (branching/map graph) is a *separate future feature* that hands the engine a finished linear sequence.
   Do not build branching into the run engine.
2. **Any event is composed from a general substrate** (like combat effects: EffectProgram/nodes). Archetypes
   are pure content with zero engine privilege.

## Architectural decisions (deviations from the idea doc)

- **One trigger substrate for everything.** The doc lists separate RunScheduler / rule-modifier / relic /
  reward-modifier systems. We unify them into **installed triggered programs on `RunState`** — the combat
  engine's "temporary rule" pattern one etage up. A scheduled consequence, a rule modifier, a reward
  modifier and a relic are the same thing: a program that reacts to run events, may hold countdown/state,
  and can uninstall itself.
- **Costs = `canPay` expression + `pay` effects**, reusing the expression + effect layers (no bespoke cost
  engine). Split: `Require` (visibility, hides choice) vs cost (affordability, disables choice).
- **State vocabulary (flags/counters/memory) = generic key-value on `RunState`**, surfaced through the
  existing expression/effect vocabulary — not new subsystems.
- **Map mutation & branching deferred** (idea doc §10.6 / RunMapMutationSystem): conflicts with principle 1.
  "At next shop/elite/before boss" still works — the scheduler filters `NodeEntered` by node type along the
  linear sequence.
- **Quests / rival runs / deck-as-dungeon are content**, buildable once counters, flags, scheduler, memory
  and selectors exist.

## Baseline already in place (as of 2026-07-01)

`RunState` (health/resources/deck/relics/map/position/log/event-history/deterministic RNG); typed event bus
(`IRunEvent` + `RaiseEvent`); `RunEffectProcessor` (queue → fixed point, currently dispatches events to
relics only); program effects (`ComputedResourceRunEffect` / `ConditionalRunEffect` / `DrawEffects` /
`DrawManyEffects`); full expression layer (`RunExpr`: values/conditions/pools/`EventValue`, `RunEvalContext`);
event DSL (`EventScript`/`Situation`/`Choice` + resolvers); relic = run-level triggered program.

## Phases (dependency-ordered; each phase = its own commit + green tests)

- **A — Generalized trigger substrate.** `RunState.InstalledPrograms`; processor dispatches events to relics
  *and* installed programs; `InstallRunProgram` / `UninstallRunProgram` effects; program identity for targeted
  uninstall. Foundation for C, rule modifiers, reward modifiers.
- **B — State vocabulary: flags & counters.** `RunState` flags + counters; effects `SetFlag`/`UnsetFlag`/
  `IncrementCounter`/`SetCounter`; expressions `FlagSet`/`CounterValue`; matching events. Unlocks
  memory-driven conditions.
- **C — Scheduler via installed programs.** Builds on A. A scheduled consequence reacts to `NodeEntered`/
  `CombatResolved`, counts down, fires, uninstalls. Authoring: `RunSchedule.AfterNNodes`/`AfterNCombats`/
  `WhenCounterReaches`. Unlocks contracts, delayed punishment, write-a-future-combat — the biggest lever.
- **D — Costs on choices.** `EventChoice.Costs` (canPay expr + pay effects); `ChoiceBuilder.Cost(...)`;
  selectable = all canPay ∧ visible.
- **E — Vocabulary expansion.** Arithmetic (`Divide`, maybe `Round`/`Floor`/`Ceil`); control-flow effects
  `Repeat` / `ForEach`; computed `Heal`/`Damage`. Aggregates (`Count`/`Sum`) land with F.
- **F — Card instance model + selectors (large; multiple slices).** Deck `List<CardDefinitionId>` →
  `List<RunCardInstance>` (upgrade level / tags / memory / stats); `IRunSelector<T>` over deck/relics
  (filters + modes All/Random/ChooseByPlayer/ChooseN; player choice via provider); card effects
  Add/Remove/Upgrade/Transform/Tag/Memory.
- **G — Combat bridge enrichment.** `CombatSetup` with setup modifiers (start-with-status), reward profiles;
  wire the relic combat-injection face (b).
- **H — Reward system.** Reward profiles / generation / reroll / modifiers (reward modifiers = installed
  programs reacting to reward-generated events).

## Deferred (needs foundations that do not exist yet)

Map mutation / branching (separate map feature); a dedicated quest layer (model via counters/flags/scheduler
first); pure-content ideas (rival run, deck-as-dungeon, living-deck events, etc.).

## Status

- Phase A: done — installed run programs (generalized trigger substrate).
- Phase B: done — flags & counters state vocabulary.
- Phase C: done — scheduler via installed programs (RunSchedule).
- Phase D: next — costs on choices.
