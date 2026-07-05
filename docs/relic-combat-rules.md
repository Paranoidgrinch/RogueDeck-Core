# Relic combat rules — face (b) as data + UI

Status: **design / not built.** Written 2026-07-05 while the Relics tab authors only run-level
reactions (face (a)). This doc scopes the missing feature: letting a relic also inject **combat
rules** (triggered programs into the fight) and authoring them in the Run sandbox.

## What this is

`RelicDefinition` has two faces (`src/RogueDeck.Run/Relics/Relics.cs`):

- **(a) `RunPrograms`** — react to *run* events (node entered, combat resolved, …). Fully data
  (`RelicData`) and authored by the Relics-tab `RelicEditor`. **Done.**
- **(b) `CombatContributions`** — `IReadOnlyList<ITriggeredEffectDefinition>` injected into a
  spawned fight so the relic bends *combat itself* (e.g. "on turn start, deal 3 to the first enemy";
  "when you take damage, gain 2 block"; "each card played heals 1"). **Engine works, data + UI don't.**

## Current state — three layers

| Layer | State | Evidence |
|---|---|---|
| Engine (runtime) | **works** | `CombatNode.cs:192` injects each enabled relic's `CombatContributions` into `blueprint.TriggeredPrograms`. |
| Data / serialization | **blocked** | `RelicData.From` throws `NotSupportedException` when `CombatContributions.Count > 0` (`RelicData.cs:16`). No serialization path. |
| UI | **absent** | `RelicEditor` edits only `RunPrograms`. No combat-effect-program editor exists anywhere visual (the Cards tab edits combat programs as **JSON**). |

## Why it's feasible (the "Func escape" is soft)

A combat contribution is a `TriggeredProgramDefinition<TEventContext>` carrying:

- `EffectProgram<TEventContext> Program` — **serializable** via `CombatJson.CreateOptions<TContext>()`
  (proven context-generic: the same converters close on any `TContext` with zero extra registration —
  see the EnemyActionContext proof in the combat-serialization history).
- `Func ContextFactory` + `Func BuildContext` — the only non-data part, **but not user lambdas**:
  they are the canonical adapters in `TriggeredProgramContextAdapters`, one per event. So they are
  **recoverable from the event key**: `adapter[key].Define(id, program, priority) → definition`.

So the data shape is just **(trigger-event key) + (EffectProgram) + (priority / filters)**. The Func
escape that made the raw definition "non-serializable" disappears behind a catalog lookup.

### Trigger catalog (~30 events, from `TriggeredProgramContextAdapters`)

TurnStarted, TurnEnded, RoundStarted, RoundEnded, DamageDealt, DamageReceived, Healed,
StatusApplied, StatusApplicationBlocked, StatusesRemovedByPolarity, StatusRemoved,
StatusChargesReduced, StatusExpired, StatusMerged, ResourceGained, ResourceLost, ResourceModified,
ResourceRefilled, CardsDrawn, CardMovedToZone, HandDiscarded, DiscardPileShuffledIntoDrawPile,
StatusStacksChanged, StatusDurationChanged, StatusChargesChanged, CombatantLifecycleChanged,
TemporaryRuleActivated, CardPlayed, CardCostPaid, CardInstanceCreated, EnemyActionExecuted.

Each has its own `TEventContext` (e.g. `DamageReceivedTriggeredEffectContext`).

## The real cost — two gaps

### Gap 1: per-context serialization of event-value reads (small–medium)

`RunJson.CreateOptions` today merges combat converters for **CardPlayContext + EnemyActionContext
only** (`RunJson.cs:105-106`). Each trigger context we support needs one
`CombatJson.AddContextConverters<ThatContext>(options, registry)` line.

- **Basic programs** (deal N, apply status, gain block — no event reads) serialize for free once the
  context is added, because the node/expr/selector kinds are open-generic.
- **Event-value reads** are context-specific and NOT registered: e.g.
  `DamageReceivedHealthDamageAmount` / `DamageReceivedBlockedDamageAmount` /
  `FixedDamageReceivedTriggeredEffectAmount` in `DamageReceivedTriggeredEffects.cs`. To author "gain
  block = damage you just took", those amount/expr types must be made public-prop (S0-style) + given
  kinds + registered per context. This is the per-event tail work; do it lazily per event we expose.

### Gap 2: a combat EffectProgram editor (the big lift)

There is **no visual `EffectProgram` builder**. The Cards tab edits programs as a JSON textarea
(`CardEditor`). A Relics-tab combat-rule editor either:

- **(cheap)** embeds a JSON textarea per rule (reuse `CardEditor`'s parse/validate machinery for the
  chosen `TEventContext`), or
- **(expensive)** builds a visual node editor — the long-standing "visual card-program builder"
  future item, which would also upgrade the Cards tab.

## Proposed slice plan

- **R1 — data + engine (no UI). DONE.** `RelicCombatRule { Trigger, Program (object =
  EffectProgram<TContext>), Priority }` + `RelicData.CombatRules`; `RelicCombatTriggers` catalog
  (turnStarted + cardPlayed for now, one `For(...)` line each) closes the adapter + CombatJson over
  each (event, context); `RelicCombatRuleJsonConverter` (registered in `RunJson`) round-trips the
  program by dispatching on the key; `RelicData.ToDefinition` builds one `TriggeredProgramDefinition`
  per rule via the adapter. Tests: RunJson round-trip (incl. two different contexts in one doc),
  build, bridge injection, and a real `AutoPlayCombatDriver` fight proving a data-authored turn-start
  rule fires (hero 30→28). `RelicData.From` still rejects code-built contributions (reverse map is a
  later slice). Run suite 193 green. *(Files: `Relics/RelicCombatRule.cs`,
  `Serialization/RelicCombatRuleJsonConverter.cs`, `Relics/RelicData.cs`, `Serialization/RunJson.cs`.)*
- **R2 — Relics-tab UI (JSON program). DONE.** `RelicEditor` gained a "Combat rules" section per
  relic: trigger `<select>` (catalog) + priority + a JSON `<textarea>` for the effect program. Edit &
  blur parses the JSON in the rule's trigger context (via `RelicCombatTriggers.Deserialize`); on
  failure it surfaces an inline error and reverts (model unchanged). Changing the trigger resets the
  program to that context's default (the program's context is fixed by the trigger). `RunSandbox.
  RegisterRelics` already calls `ToDefinition`, so authored rules fire in sandbox play. Render test
  added. Reverting-on-parse-error is the one rough edge (no per-rule draft state) — acceptable for a
  JSON authoring UI; a structured/visual editor is R4.
- **R3 — event-value exprs per event.** As events are exposed, S0 + register their context-specific
  amount/value types so rules can read the triggering event ("= damage taken", "= card cost").
- **R4 (optional, large) — visual program editor** shared by Cards + Relics combat rules.

R1 is the honest first bounded step; R2 makes it usable; R3/R4 deepen coverage.

## Watch-outs

- Each trigger context is a distinct type — `AddContextConverters<TContext>` per exposed event; don't
  assume one "TriggeredEffectContext".
- Filters (`ITriggeredProgramFilter<TEventContext>`) are also Func-ish; defer (rules without filters
  first) or model a small data filter set later.
- Re-entry/priority semantics already handled by `TriggeredProgramCombatEventHandler`; the data layer
  only needs to carry `priority` + `id`.
