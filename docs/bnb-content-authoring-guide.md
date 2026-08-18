# Bureaucrats & Broomsticks — Content Authoring Guide

**Purpose:** turn the finished enemy *identities* + the encounter list into runnable content, and lay out
the hybrid map. Written against the engine as it stands after the enemy-mechanics arc
(`docs/bnb-enemy-mechanics-engine-audit.md`). This is the bridge from "design identity" → "exact engine
construct".

## Pipeline
- Author combat content as **raw `EffectProgram`s** on `CardData.Program` / `EnemyActionData.Program` —
  they serialize through the CombatJson converters into `game.roguedeck.json` (the Godot export). So a
  mechanic is authorable as soon as its nodes/expressions are registered in CombatJson (all arc primitives
  are).
- The Studio is the **validation/preview** surface (Balance + Map Rules tabs, SVG preview, export gate).
  It does *not* yet expose the new arc nodes in its visual palette (Phase 7, dropped) — that's fine, the
  converter path doesn't need it.
- Every act should get one **headless `RunPlayback` regression** (a torture test) that plays a full run —
  this is what catches BuildContent wiring gaps (as it did in the arc).

---

## Part 1 — Signature → engine-primitive recipe catalogue

Each recurring design pattern and the exact construct that realises it. `sel.source` = the acting enemy,
`sel.eventTarget` = its target (the hero in solo fights).

| Design pattern (from the pools) | Engine construct |
|---|---|
| **Status with a "at N → consequence" threshold** (Overdue 2→Paperwork+Late Consequence, Trespass 3→Claim) | A `StatusDefinition` (source-bound via the applying source) + a triggered effect on `StatusApplied`/`StatusStacksChanged` whose condition is `combatantStatusStacksFromSource(target, status, source) >= N` → then the consequence effects + remove N stacks. |
| **"N from the SAME source"** (each enemy tracks its own) | `combatantStatusStacksFromSource` (never plain `combatantStatusStacks`). |
| **Scaling attack "+X per <status>, cap Y"** (Panic/Doubt cash-out) | `DealDamage(amount = Add(base, Clamp(Multiply(combatantStatusStacks(target,status), X), 0, Y)))`. Status not consumed = don't remove it. |
| **Card-instance mark** (Misfiled / Referenced / Redacted / Counted) | `node.markCardInstance` (owner selector + a card-instance expression + mark tag, optional source). Read with `cardInstanceHasMark`. |
| **Enemy marks a PLAYER card** | card expression `cardInstance.inOwnerZone` / `cardInstance.randomInOwnerZone` with owner = `sel.eventTarget` (NOT `inZone`, which is the acting enemy's own zones). |
| **Misfiled** (next draw → discard + redraw, clear) | triggered effect on `CardsDrawn`: `forEachCardInZone(hero Hand)` → `conditional(cardInstanceHasMark(iterated, misfiled))` → moveCardToZone(iterated→Discard) + markCardInstance(remove) + drawCards(1). |
| **Referenced** (played → clears; leaves hand unplayed → Overdue from source) | mark bound to source; triggered effects on `CardPlayed` (clear) and `HandDiscarded`/`CardMovedToZone` (apply 1 Overdue from source, clear). |
| **Redacted** (next play −50% output) | `node.setCardInstanceMarkCounter` sets the reserved `StandardCombatIds.CardOutputScale{Numerator,Denominator}Counter` to 1 / 2 on the target card. The play pipeline consumes it and halves that play's output. |
| **One-shot special intent / phase / orbit transition** ("replace next intent with X") | `SelfHasCounterCondition(counter, >=, 1)` intent rule at high Priority → the special/transition action; a triggered effect sets the counter when the trigger fires (track fills, HP crossed, round advanced); the action's program resets the counter to 0. |
| **Non-HP orbits** (Nanna-Sin) | counter `++` on `RoundEnded`; intent rules gated on `SelfHasCounterCondition(orbit, >=, k)`. |
| **Boss phase (HP-driven)** | `EnemyHealthPercentCondition` intent rules for phase-II actions; a one-time transition via a `DamageReceived` triggered effect guarded by a "transitioned" counter; inject phase rules with `InstallTemporaryRule`. |
| **Card-type sequencing** ("first non-Junk type", "third of that type") | `firstCardPlayedHasTag(hero, <typeTag>)`, `cardsPlayedThisTurnWithTag(hero, <typeTag>)`. Card types = tags (attack/skill/power/junk). |
| **Whispered Prediction** (confirm/contradict this turn's play) | telegraph the chosen prediction as the intent (Special); at `TurnEnded` evaluate with `cardsPlayedThisTurn` / `firstCardPlayedHasTag` / `cardsPlayedThisTurnWithTag`; grant Authority/Contradiction counters. Habit selection uses `cardsPlayedLastTurn(+WithTag)` / `firstCardPlayedHasTag(lastTurn)`. |
| **Returning Move / Lunar Echo** (replay a recorded player card, scaled) | mark played cards "counted" (mark node on `CardPlayed`); the enemy action `replayCardProgram(card = firstMarkedCardInOwnerZone(hero, Discard, counted), target = eventTarget, scale = 3/5)`. Full-Moon amplify = scale 3/2. |
| **Prevent / redirect a status** (Safe-Conduct → Trespass, Bookworm → Paperwork) | `IStatusApplicationInterceptor` (Allow/Block/Replace, loop-safe) — author as an interceptor status/relic. |
| **Tracks / resources** (Queue Position, Claim, Authority, Royal Favor, Gate Height, Flood Gauge) | combatant `Counters` via `node.setCombatantCounter`; read with `combatantCounter`. |
| **Local law / temp rules** (Act-III laws, Constitutional Articles) | `node.installTemporaryRule` (+ remove). |
| **Summons / support-first / duos-trios** | `node.summonCombatant`; author the encounter with multiple `EncounterEnemy` entries. |
| **Wergild "Make Amends" free action** | a generated 0-cost card in the hero's hand (pay Energy or discard); author as a card with the appropriate cost/effect. |

**Guardrails baked into the design docs (respect while authoring):** telegraph major consequences one
action ahead (use the intent label); source-bound resources cleaned up on `CombatantDowned`; damage-event
recursion avoided (use HP-loss where the docs say so); no self-triggering loops (cap/consume/cooldown).

---

## Part 2 — Hybrid map authoring (fixed idea, random layout)

The map engine is **done** (`RuleBasedMapGenerator` + `MapGenerationSpec` + `EncounterDistribution`). You
author **one `MapGenerationSpec` per act** as data; each run gets a fresh random layout that still honours
the fixed structure. No map code to write.

Per-act spec fields to set:

- **Backbone:** `Rows`, `MinWidth`/`MaxWidth` (branchiness). Boss is the fixed top row; a Rest is placed
  just below it automatically.
- **Kind mix on wide rows:** `KindWeights` (Combat-heavy + some Event/Shop/Treasure/…).
- **Guarantees:** `PerPathMinimums` (e.g. Shop ≥1, Treasure ≥1, Elite ≥2 later acts), `MinEnemiesPerPath`,
  optional `MapWideMinimums`. Met by width-1 "gate funnel" rows.
- **Fights:** `Encounters.ByRole` — weighted pools for `Combat`, `Elite`, `Boss`, and **`Mimic`**.
- **Non-combat refs:** `NodeRefs` — the authored `Shop` id, and the `Event`/`Rest`/`Treasure` event ids.
- **Balance band:** `BalanceTargets` (StartNet, NetDropPerRow, LoadoutGrowthPerRow, Tolerance) — the
  generator keeps each fight's net (loadout strength + encounter threat, from the Balance manifest) inside
  the band as depth increases. Seed these from the design docs' per-stage HP + intent budgets.

### Mimic (per the design)
Set **`TreasureMimicChancePercent`** per act: **5 / 10 / 15 / 20** for Acts I–IV. Fill
**`Encounters.ByRole[Mimic]`** with a fight tuned **≈ a weak elite of that act**. A Treasure node then flips
into that combat with the given chance (deterministic per seed); otherwise it stays the normal
treasure/relic event. Nothing else to wire — it realizes as a normal `CombatNode`.

---

## Part 3 — Encounter-authoring plan (what to fill, in order)

Source of truth: the **Master Standard/Elite/Boss FINAL_AUDIT** pools (mechanics) + the detailed per-act
lists (HP/intents/compositions; Act I is the `..._Standard_Encounter_Pools_...(1).md` list). Author per act:

1. **Statuses/vocabulary** for the act (Panic/Doubt/Paperwork/Fatigue/Bookworm; Overdue/Misfiled/Referenced/
   Redacted; Safe-Conduct/Trespass/Claim/Wergild; Weighed/Inscribed/Burdened/Entombed/Embalmed) as
   `StatusDefinition`s. Card-independent — can be done now.
2. **Enemies** → `EnemyActionData` (intents as programs) + intent rules, one per identity, using Part 1.
3. **Encounters** → `EncounterDefinition` composing enemies (solo/duo/trio) with the per-encounter HP from
   the list.
4. **Elites, then Boss** (phases via Part 1).
5. **Mimic pool** (≈ weak elite) + the act's **`MapGenerationSpec`** (Part 2).

**Process:** get **Act I fully playable end-to-end in Godot first** (all node types incl. a mimic), with a
headless `RunPlayback` regression, before filling Acts II–IV.

> Enemies + encounters + statuses + the map spec are on the engine side (mine to prepare); cards, relics,
> consumables, events and the shop are being authored in parallel. Encounters reference card *types* (tags),
> not specific cards, so enemy authoring is largely unblocked by the card pool.
