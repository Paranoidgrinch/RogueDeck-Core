# Bureaucrats & Broomsticks — Act Map Specs

Per-**path** minimums: **every single entry→boss path** on a generated act map must contain at least these.
Maps are hybrid — fixed structure, random layout each run. One `MapGenerationSpec` per act.

## Per-path minimums

| Role | Act I | Act II | Act III | Act IV |
|---|---:|---:|---:|---:|
| Normal combats (`Combat`) | 8 | 8 | 8 | 12 |
| — of which **multi-encounter** (2+ enemies) | 1 | 2 | 2 | 3 |
| Elites (`Elite`) | 1 | 2 | 3 | 4 |
| Events (`Event`) | 3 | 3 | 3 | 4 |
| Campfires (`Rest`) | 2 | 2 | 2 | 3 |
| Treasure (`Treasure`) | 2 | 1 | 1 | 2 |
| Shops (`Shop`) | 2 | 2 | 2 | 3 |
| Boss | 1 (fixed top) | 1 | 1 | 1 |

**Act V is not in the table**: it is a gauntlet — three boss rooms back to back, drawn from six, and nothing
else at all (no rooms, no recovery, no spoils). Its spec is `Rows = 0` with `BossRooms = 3`.

## Global run rules
- **No encounter template repeats within a run** (each combat/elite/boss fight the player meets is a
  distinct template).
- **Mimic:** a Treasure node flips to a combat (~weak elite of the act) with a per-act chance
  **5 / 10 / 15 / 20 %** (Acts I–IV) — already supported (`TreasureMimicChancePercent`).

## Engine coverage
- ✅ Role per-path minimums → `MapGenerationSpec.PerPathMinimums` (Combat/Elite/Event/Rest/Treasure/Shop) +
  `MinEnemiesPerPath`. Enforced today by gate funnels + `MapConstraintValidator`.
- ✅ Mimic → `TreasureMimicChancePercent` + `Encounters[Mimic]`.
- ✅ **No-repeat across a run** → encounter selection is now WITHOUT replacement across the whole map
  (shared used-set, balance-filtered, graceful fallback once a pool is exhausted). Keep each role's pool
  ≥ the nodes that draw it.
- ✅ **Per-encounter earliest depth** → `MapGenerationSpec.EncounterMinimumDepthPercent`, keyed by encounter
  id: the elite masters' "earliest depth/stage" tables name individual fights, which neither the role gate
  (`RoleMinimumDepthPercent`) nor the weighted pool can express. Where a row can honour none of a role's
  candidates the gate yields, so a combat node is never left without a fight.
- ✅ **A gauntlet act** → `MapGenerationSpec.BossRooms` (default 1) plus `Rows = 0`: the act ends on several
  boss rooms rather than one, each its own row, each drawing its fight from the Boss pool without replacement
  (so the three gods of one run are three different gods, and the run knows which three from the moment its
  map is built). An act with no branch rows can keep no per-path promise, and saying both is a spec error.
- ✅ **Per-path multi-encounter minimum** → new role `MapNodeKind.MultiCombat`: placed via gate funnels like
  any per-path minimum, draws from `Encounters[MultiCombat]` (list the duo/trio templates there), realizes
  as a normal combat, counts as an enemy. Set `PerPathMinimums[MultiCombat]` per act.

## Concrete PerPathMinimums (once the two constraints land)
```
Act I : Combat 8,  MultiCombat 1, Elite 1, Event 3, Rest 2, Treasure 2, Shop 2   | MimicChance 5
Act II: Combat 8,  MultiCombat 2, Elite 2, Event 3, Rest 2, Treasure 1, Shop 2   | MimicChance 10
Act III:Combat 8,  MultiCombat 2, Elite 3, Event 3, Rest 2, Treasure 1, Shop 2   | MimicChance 15
Act IV: Combat 12, MultiCombat 3, Elite 4, Event 4, Rest 3, Treasure 2, Shop 3   | MimicChance 20
```
