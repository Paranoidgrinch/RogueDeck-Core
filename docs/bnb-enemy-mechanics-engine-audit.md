# Bureaucrats & Broomsticks — Enemy/Elite/Boss Mechanics: Engine Audit & Minimal-Change Plan

**Date:** 2026-08-18
**Source designs (canonical, Downloads):**
- `..._Master_Standard_Encounter_Pools_Acts_I-IV_FINAL_AUDIT.md` — 110 identities / 162 encounters
- `..._Master_Elite_Pool_Acts_I-IV_FINAL_AUDIT.md` — Act I–IV elites
- `..._Master_Boss_Pool_Acts_I-V_FINAL_AUDIT.md` — Act I–V bosses (Act V = 6-god gauntlet)

**Goal:** cover *all* enemy mechanics with the fewest possible engine changes. Everything the engine
*can* express must stay content (author it through `RunPlayback.BuildContent`, add capability tests —
never bespoke features; see `feedback_no_redundant_features`). Only genuinely missing *primitives*
get built.

---

## Part 1 — Mechanic families → engine coverage

The 25k lines of design reduce to ~15 cross-cutting mechanic families. Each was checked against the
live engine (Core `Combat/*`, Scenario `Authoring/*`).

| # | Mechanic family | Examples | Engine primitive | Verdict |
|---|---|---|---|---|
| 1 | **Source-bound statuses/resources + death cleanup** | Overdue, Trespass, Wergild, Appointment Due, Lost Time, Final Notice | `StatusInstance.SourceCombatantId` / `SourceCardId`; source-bound resources | ✅ composes |
| 2 | **Per-status-instance data** | Safe-Conduct `granted_by`, Replicated flag | `StatusInstance.Tags` + `Counters` (per instance) | ✅ composes |
| 3 | **Scaling effects/intents** | "+3 dmg per Panic, max +9"; Date Discrepancy; Lost Your Place | Effect-program expressions (`CombatantStatusStacks`, `Clamp`, `Multiply`, …) + declarative `PassiveModifierSpec` | ✅ composes |
| 4 | **Reactions to player behaviour** | panic-removal punish, damage/heal/draw/discard triggers, status-gained | Rich `*CombatEvent` surface + `*TriggeredEffects` (Damage/Status/Resource/Card/Turn/Round/Downed…) | ✅ composes |
| 5 | **Tracks / counters** | Queue Position, Return Parcel, Gate Height, Claim, Authority, Royal Favor, Orbit, Flood Gauge | `CombatantState.Counters` + `SetCombatantCounterEffectRequest` + `CombatantCounterExpression` | ✅ composes |
| 6 | **Prevent / replace / redirect** | Safe-Conduct → Trespass, Bookworm → Paperwork | `IStatusApplicationInterceptor` (Allow/Block/Replace, loop-safe depth guard) | ✅ scaffolding exists (status side) |
| 7 | **Temp rules / local law** | Act-III Local Laws, Constitutional Articles, phase rule injection | `InstallTemporaryRuleEffectRequest` + `TemporaryTriggeredProgram` | ✅ composes |
| 8 | **Summons / multi-body / support-first** | duos/trios, Rope-Master + Hauler, choruses | `SummonCombatantEffectRequest`, teams | ✅ composes |
| 9 | **Threshold consequence "at N (from same source) → X"** | Overdue 2→Paperwork+Late Consequence, Trespass 3→Claim | Triggered effect on `StatusApplied`/`StatusStacksChanged` + instance ops | ⚠️ composes, but wants a **source-scoped stack expression** (minor) |
| 10 | **Card-play ordering** | "first non-Junk card type", "third card of that type", prediction openings | `CombatantCardPlayTurnStats` (counts by tag/def) | ⚠️ counts yes, **order/first-type no** → small stat add |
| 11 | **Intent replacement / one-shot override / phase & orbit transitions / telegraphed specials** | "replace next normal intent with X", boss Phase II, Nanna-Sin Orbits, transition "replaces next legal intent" | `EnemyIntentRule` + `EnemyIntentCondition` (health%, round, self/opp-status, AND/OR/NOT) | ⚠️ **condition vocabulary too small** — no counter/resource/card-stat/source-status conditions |
| 12 | **Card-instance marks** | Misfiled, Referenced, Redacted, Counted | combat `CardInstance` = `{Id, DefinitionId, Owner, Zone}` — **no marks** | ❌ **gap (backbone)** |
| 13 | **Card next-play output scaling** | Redacted −50% positive numeric output | `PassiveModifierSpec` is status-owned; `DamageAmountModificationContext` has `SourceCardId` (def) not instance | ❌ **gap — needs a small play-resolution seam** |
| 14 | **Free non-card player actions** | Make Amends, ANSWER/DECLINE, Reclaim the Seal, Steal Permit response, Hold the Moon | generated 0-cost / retained cards; consumables | ⚠️ model as **generated cards** (no engine change) — verify discard-as-cost |
| 15 | **Weighed measure + observers** (Act IV) | Primary Measure + observing enemies | counter recording the result + triggered effects reading it | ⚠️ composes; optional tiny "record measure" convenience |
| 16 | **Recorded-move replay** (Act V) | Nanna-Sin Returning Move, Lunar Echoes | store card ref (counter/memory queue) + create + resolve scaled copy as enemy action | ⚠️ advanced; mostly composes, may want a replay helper |
| 17 | **Whispered Prediction** (Act V) | confirm/contradict a prediction of this turn's play | Turn Record (needs #10) + counters + turn-end evaluation + telegraphed intent (#11) | ⚠️ composes once #10/#11 land |

**Design constraints** the docs repeat — deterministic solvability, no infinite punishment loops,
telegraphing one action window ahead, full death/phase cleanup — are *authoring rules*, not engine
features. The engine already supports them (interceptor depth guard; intent metadata is telegraph;
cleanup = triggered effects on `CombatantDowned`).

**Conclusion:** the engine's substrate (effect-program expressions + triggered-effect events +
counters/resources + temp-rules + interceptors + summons) already covers the reactive, scaling,
stateful, and multi-body surface of *every* act. No architectural rewrite is required. The real work
is a **small, additive set of primitives** (below), after which the 110 standard + all elite + all
boss identities are **content**.

---

## Part 2 — Confirmed engine gaps (the only build work)

### A. Card-instance marks in combat *(Core)* — the backbone
Combat `CardInstance` must be able to carry mutable, per-instance marks (like `StatusInstance` already
does): a small `Tags` set + `Counters` dict + optional `SourceCombatantId`. This one facility unlocks
**Misfiled, Referenced, Redacted, Counted** and any future card-marking mechanic.
- New effect requests: mark / unmark a card instance (add/remove tag, set/adjust instance counter, set source).
- New expressions: "does this card instance carry mark X", instance counter value — usable in the
  existing card-in-zone / played-card / iterated-card selectors.
- Reactive wiring: marks survive zone moves (they live on the instance); Misfiled handled on
  `CardsDrawnCombatEvent`, Referenced-unplayed on `HandDiscarded` / `CardMovedToZone`.
- Serialization + Studio authoring + Godot export contract for the new mark ops.

### B. Redacted-style next-play output scaling *(Core)* — one small seam
When a card instance carrying a "reduce next play" mark resolves, scale its **positive numeric
outputs** (damage, block, heal, draw, energy gain, positive player statuses, negative enemy statuses)
by a factor (floor), then clear the mark. Implement as a single hook at play-effect resolution keyed
to the instance mark — *not* by threading instance ids through every modifier pipeline.

### C. Card-play ordering stat *(Core)* — small
Add to `CombatantCardPlayTurnStats`: the **first non-Junk card type played this turn** (the "opening
type"), and retain the previous turn's snapshot (for prediction / "again" habits). New expressions to
read them.

### D. Intent-condition expansion *(Scenario)* — additive, low-risk
Add `EnemyIntentCondition` subtypes so intent selection can react to encounter state:
- `SelfHasCounterCondition` (counter cmp N) — **enables one-shot intent override and non-HP
  phases/orbits**: a triggered effect bumps a `pending`/`orbit` counter, a high-priority intent rule
  fires the transition/special action, the action resets the counter.
- `SelfResourceCondition`, `CardsPlayedLastTurnCondition` / `CardsPlayedThisTurnCondition`,
  `SelfStatusFromSourceCondition`.
These make phase transitions, Orbits, telegraphed specials, and prediction-intent selection expressible.

### E. Source-scoped status-stack expression *(Core)* — minor
`CombatantStatusStacksFromSourceExpression` (count stacks of a definition contributed by a specific
source combatant) so "2 Overdue from the same source" / "3 Trespass from the same source" thresholds
are clean content.

### F. Verify-only (expected: no build)
- Free non-card actions → generated 0-cost / retained cards. Verify **discard-as-cost** and
  always-available generation exist; if a gap, it is tiny.
- Death cleanup of source-bound resources → confirm `CombatantDowned` triggered-effect coverage.

Nothing above is a rewrite. A–C + E touch Core additively; D touches Scenario additively; F is a check.

---

## Part 3 — Phased implementation plan (fewest changes first)

Ordering is by leverage: each phase unblocks the most content per line of engine change. Every phase
ends with capability tests driven **through `RunPlayback.BuildContent`** (per the Shred-engine lesson:
never test through hand-built registries), all suites green, and a push to `origin/main`.

- **Phase 1 — Card-instance marks (A).** The backbone. CardInstance mark facility + effect requests +
  expressions + reactive Misfiled/Referenced handling + serialization + Studio + export. Capability
  tests: Misfiled redirect-and-redraw, Referenced fulfil/lapse-to-Overdue.
- **Phase 2 — Redacted output scaling (B).** Play-resolution scaling seam + mark integration.
  Capability test: Redacted halves damage/block/heal/draw on next play, then clears.
- **Phase 3 — Card-play ordering + source-scoped stacks (C, E).** Stats + expressions. Capability
  tests: Wrong-Window Scribe, Triplicate Examiner, Overdue/Trespass thresholds.
- **Phase 4 — Intent-condition expansion (D).** New conditions + previous-turn stat retention.
  Capability tests: one-shot intent override (Queue "Everyone Moves at Once"), non-HP orbit phase,
  boss phase-II transition replacing next intent.
- **Phase 5 — Representative content proofs.** Author a vertical slice per mechanic family through
  BuildContent as living capability tests: Act-I signature (Panic cash-out + Bookworm), Act-II
  (Overdue + Misfiled + Referenced + Redacted), Act-III (Safe-Conduct/Trespass/Claim/Wergild via
  generated Make-Amends card), a boss phase transition, and one Act-V exemplar (an Orbit boss).
- **Deferred (follow-up arc):** full recorded-move replay (F16), full Whispered-Prediction catalogue,
  Weighed measure/observe convenience — all compose on the Phase 1–4 primitives; built when the
  corresponding content is authored, not speculatively.

---

## Part 4 — Verified engine facts (evidence)

- `StatusInstance`: has `SourceCombatantId`, `SourceCardId`, per-instance `Tags` + `Counters`. → families 1, 2.
- `CombatantState`: `Resources`, `Statuses`, `Counters`, `GetCounter/SetCounter`. → family 5.
- Effect-program expressions (`EffectProgramExpressions.cs`, ~1650 lines): status stacks, stacks-by-polarity,
  resource, counter, HP/health%/missing-health, round/turn, cards-played-this-turn, damage/resource-this-turn,
  card-in-zone (chosen/random/iterated), zone card count, arithmetic/clamp/min/max/and/or/not. → families 3, 4, 9.
- Combat event surface: Turn/Round Started/Ended, Damage Dealt/Received, Healed, Status Applied/Removed/
  Merged/StacksChanged/…, Resource Gained/Lost/Modified, Card InstanceCreated/Played/Drawn/MovedToZone/
  Transformed, HandDiscarded, CombatantLifecycle/Downed, EnemyActionExecuted, TemporaryRuleActivated. → family 4.
- Effect requests: SetCombatantCounter, TransformCard, StealStatusInstance, ModifyStatusInstanceStacks,
  RemoveStatusInstance, InstallTemporaryRule, SummonCombatant, CreateCardInstance, DrawCards, DiscardHand,
  MoveCardToZone, DealDamage/GainBlock/Heal/ApplyStatus. → families 5, 7, 8.
- `IStatusApplicationInterceptor`: Allow/Block/Replace, loop-safe `InterceptionDepth`. → family 6.
- `EnemyIntentRule` + `EnemyIntentCondition`: health%, round, self-status, opponent-status, AND/OR/NOT;
  fallback = round-based `Actions` cycle. → family 11 (the intent-layer gap).
- Combat `CardInstance` = `{Id, DefinitionId(mutable), OwnerId, Zone}` — **no marks**. → gap A.
- `DamageAmountModificationContext` = `{…, SourceCardId(def), …}` — **no instance**. → gap B.
- `CombatantCardPlayTurnStats`: counts by definition/tag, damage, resource — **no order/first-type**. → gap C.
