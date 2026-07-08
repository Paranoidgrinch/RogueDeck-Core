# Party Deckbuilding (up to 4 players, simultaneous turns)

**Status:** planned 2026-07-08 (not started; created this session, to implement next). The chosen next arc after the
branching-map arc closed. Replaces the single-hero (+ summons/board-units) model with a **party of 1–4 player
characters**, each a full player entity: its own health, deck, hand, energy/resources, relics, consumables, and
currency. Motivated by multiplayer: several players must play **concurrently**, not wait for each other.

## Locked design decisions (user, 2026-07-08)

1. **Turn model = simultaneous player phase.** Not round-robin. All living party members are "in their turn" at
   the same time — each with its own hand/energy/deck — and each **ends its turn independently**. When every living
   member has ended, the **enemy phase** runs, then the next round. Single-player is the degenerate case (one
   member, or one controller driving all) and must behave byte-for-byte as today.
   - **"Parallel" = un-gated control, not parallel execution.** The engine stays single-threaded and resolves
     effect requests in submission order (one card resolves after another). Concurrency means no player is blocked
     by another's turn; the networking layer feeds concurrent players' inputs into the one effect queue.
     Determinism (replay/sim) = the sequence of submitted requests.
2. **Downed member = out for the fight, run continues.** At 0 HP a member is removed from the current fight; while
   ≥ 1 member lives the fight/run continues. After the fight the member is available again (rest-HP / healing is
   content). No baseline permadeath, no baseline in-combat revive (both are possible later as content/flags).
   Reconciliation is per member (extends today's board-unit reconciliation).
3. **Everything per member, including gold.** Deck, relics, consumables, combat resources (energy) AND currency are
   strictly per member — each has its own wallet. No shared party pool in the baseline (uniform data model; a
   shared pool could be added later as an opt-in overlay if a game wants it).
4. **Party size 1–4.**

## Grounding — what the engine ALREADY supports (verified 2026-07-08)

The combat core is largely multi-actor; the single-hero assumption lives mostly in the run layer and thin wrappers.

- **Per-combatant everything in `CombatState`/`CombatantState`:** card zones (`_cardZonesByCombatant`), `Resources`,
  `Statuses`, `DefensivePools`, `Counters`; team-agnostic `TurnOrder` + `ActiveCombatantId`; `OwnerId`/`ControllerId`
  already on `CombatantState`.
- **Draw / discard / play are per-combatant, not hero-bound:** `DrawCardsOnTurnStartedHandler` draws for
  `event.CombatantId` (the combatant whose turn started); `DiscardHandOnTurnEndedHandler` likewise;
  `PlayCardEffectRequest(combatantId, …)` is combatant-parameterized. Card-play validators are skipped on the
  `PlayCardEffectRequest` path, so playing as a non-"active" combatant is already mechanically feasible.
- **Turn loop is team-agnostic round-robin:** `CombatTurnProcessor` cycles `TurnOrder` one `ActiveCombatantId` at a
  time; P5a already gives every player-team unit its own turn.
- **Blueprint hierarchy:** `CombatantBlueprint` base (HP, `Resources`, `StartingStatuses`, `Position`);
  `HeroBlueprint` adds `Deck` + `OpeningTemporaryRules`; `AllyBlueprint`/`EnemyBlueprint`. Allies already carry
  resources/statuses/position — they just get **no deck dealt** today.
- **Per-entity run persistence already exists:** `RunUnit` (board roster) projects into an `AllyBlueprint`
  (HP/position/statuses) and reconciles back (`CombatNodeResolver.ApplyRunProjection` / `ReconcileUnits`). A party
  member is this pattern **plus a deck + resources + relics + consumables**, and controllable.

**Single-hero assumptions to dissolve:** (a) `ScenarioCombatFactory` deals only `Hero.Deck`; (b) `InteractiveCombat`
is hero-centric (`_heroId`, `IsHeroTurn`, `PlayCard` as hero); (c) `RunState` is single-hero (one `Health`, `Deck`,
`Resources`, `Relics`, `Consumables`); relics inject combat contributions **run-globally**, not per member; (d) enemy
actions target "the hero"/EventTarget — with N players, enemies must **choose which player** to hit.

## Hard constraint / invariants (mirror the positional + branching arcs)

A party of one member, with no extra data and the simultaneous flag off, must behave **byte-for-byte as today**.
Checked every phase:

1. **Degenerate = today.** One member + `SimultaneousPlayerTurns` off ⇒ the exact current single-hero flow; the full
   existing Core/Scenario/Run suites pass every phase.
2. **Additive / opt-in.** `SimultaneousPlayerTurns` is a combat flag (default off ⇒ round-robin); `RunState.Party`
   defaults to one member = the hero via delegating shims; per-member decks are dealt only for members that have one.
3. **Serialization back-compat.** An old single-hero `RunBlueprint`/`RunState` JSON loads as a one-member party.
4. **Engine stays single-threaded + deterministic by request order.** "Parallel" is a control/UI property; the effect
   queue is untouched.
5. **Resolution primitives unchanged.** Only turn/phase gating, N-deck dealing, and per-member projection/targeting
   are added — damage/status/card resolution is the same.

## Target model

- **`PartyMember`** (run layer): a controllable player character — id, definition id, name, `HealthState`,
  `Position`, statuses, **`Deck`, `Resources`, `Relics`, `Consumables`, currency**. Fully per member.
- **`RunState.Party`** — 1–4 `PartyMember`s. The former single hero is **member 0**; `RunState.Health`/`Deck`/… become
  shims over member 0 so existing code/tests keep working.
- **`RunUnit` (auto-acting board units) stays** as a separate, non-card roster (summons/minions that act via intent
  markers). PartyMember and RunUnit share the project→reconcile machinery; PartyMember additionally plays cards.
- **Combat**: each party member projects to a player-team `CombatantState` with its own dealt deck + resources +
  statuses + position + its relics' combat contributions. A **simultaneous player phase** lets them all act at once.

## Phases (dependency-ordered; each = its own commit + green tests; invariants held every phase)

### Part A — Combat engine: per-member decks + simultaneous player phase
- **A1 — Multiple dealt decks (no turn change).** Add `Deck` to `CombatantBlueprint` (or `AllyBlueprint`);
  `ScenarioCombatFactory.Build` deals EVERY player-team combatant's deck into its zones and applies its resources +
  opening rules. Because draw/discard/play are already per-combatant, a second player-team combatant already draws
  its hand at its round-robin turn and plays its own cards. *Small, safe first step — multi-deck works even under
  today's round-robin.* Test: two player combatants each draw + play their own decks.
- **A2 — Simultaneous player phase (the turn-model change; riskiest).** Opt-in `SimultaneousPlayerTurns` on the
  combat. A team-phase model: at player-phase start every living player member gets turn-start (draw + energy
  refill) and enters an "active, not-ended" state; each can play cards from its own hand/energy at any time; a
  member's turn-start/turn-end fire per member; the player phase completes when all living members have ended, then
  the enemy phase runs. `ActiveCombatantId` (single) generalizes to an **active-player-member set** during the
  player phase (a set of one ⇒ identical to today). Sub-slices: **A2a** phase/active-set state model + transitions;
  **A2b** relax card-play gating to "any active, un-ended player member"; **A2c** enemy phase fires after all-ended.
  Invariant: full combat suite green with the flag off; a new suite covers the phase model.
- **A3 — Party-aware interactive driver.** Generalize `InteractiveCombat`: per-member `Hand`/`Energy`/`Ended`,
  `PlayCard(memberId, card, target)`, `EndTurn(memberId)`; keep `HeroId`/`IsHeroTurn`/`PlayCard(card,target)` as
  member-0 shims. This is the surface a UI or networked driver talks to.

### Part B — Run layer: the party
- **B1 — Party data model.** `PartyMember` + `RunState.Party` (1–4); hero = member 0 with delegating shims for
  `Health`/`Deck`/`Resources`/`Relics`/`Consumables`/currency so all existing single-hero code + Run tests pass.
  Sub-slices: **B1a** model + shims; **B1b** RunJson round-trip (old JSON ⇒ one-member party); **B1c** authoring
  (`RunBlueprint`/`RunStart` party; each member = identity + starting deck/relics/resources/consumables).
- **B2 — Projection + reconciliation per member.** `ApplyRunProjection` projects each member as a player-team
  combatant (deck dealt, resources, position, statuses) and injects **that member's** relics as combat
  contributions (relics become per member, not run-global). Reconcile each member's HP/position/statuses back;
  a member downed in the fight is marked out but kept in the party (run continues if ≥ 1 alive). Sub-slices: **B2a**
  N-member deck/resource projection; **B2b** per-member relic combat face; **B2c** per-member reconciliation +
  downed handling; **B2d** **enemy targeting among players** — enemy actions choose which player to hit (extend
  enemy target selection from "the hero" to "a player member", e.g. lowest-HP / random / positional).
- **B3 — Run-level per-member economy.** Member-scoped run effects (heal/damage/gain-currency/add-card/add-relic/
  add-consumable target a member id); a "which member" selector for events/rewards (a specific member, the whole
  party, or a player-chosen member); run defeat = all members down. Sub-slices: **B3a** member-scoped effects +
  selector; **B3b** events/rewards target a member; **B3c** run-defeat = whole party down.

### Part C — Control model + multiplayer seam + Studio UI
- **C1 — Control / multiplayer seam.** A `player slot → member` mapping; the existing driver abstractions
  (`ICombatDriver`, `IRunChoiceProvider`, `IRunEntityChooser`) are the plug-in point for a local or networked
  player. Ship a local driver (hotseat / one controller drives all members); prove the MP shape by driving N
  members from N independent driver instances whose card-plays interleave into one deterministic queue. Netcode is
  out of engine scope — the seam and determinism-by-request-order are the deliverable.
- **C2 — Studio UI.** Author a party (1–4 members) in the Run/Hero tab; the playtest view shows each member's
  hand/HP/energy/inventory, per-member end-turn, and the phase / who-has-ended state. Extends `RunSessionView`.

### Part D — Worked examples / validation
- 2- and 4-member party fights under the simultaneous phase (each plays its own deck/energy; target an enemy and
  buff an ally); a member downed mid-fight while others finish and the run continues; a full party run end-to-end
  (map → combats → per-member rewards); a **parallel-input** test: two driver instances submit interleaved
  card-plays for their own members, deterministic by submission order — proving the multiplayer shape without netcode.

## Risks & sub-decisions to lock during implementation

- **Active-set state model (A2)** is the sharpest change: generalizing `ActiveCombatantId` to a set + per-member
  ended flags while keeping single-member byte-identical. Sub-slice and test heavily.
- **PartyMember vs RunUnit**: keep both (controllable card-player vs auto-acting minion), sharing project/reconcile.
- **"Which member" targeting** (run effects, events, and especially enemy AI targeting a player) — pick a small,
  composable selector vocabulary (by id / lowest-HP / random / positional / player-chosen).
- **Shared pools** (gold, etc.) are explicitly NOT in the baseline (all per member) but the model should not
  preclude an opt-in shared pool later.
