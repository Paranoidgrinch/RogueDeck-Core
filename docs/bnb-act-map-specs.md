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

## Global run rules
- **No encounter template repeats within a run** (each combat/elite/boss fight the player meets is a
  distinct template).
- **Mimic:** a Treasure node flips to a combat (~weak elite of the act) with a per-act chance
  **5 / 10 / 15 / 20 %** (Acts I–IV) — already supported (`TreasureMimicChancePercent`).

## Engine coverage
- ✅ Role per-path minimums → `MapGenerationSpec.PerPathMinimums` (Combat/Elite/Event/Rest/Treasure/Shop) +
  `MinEnemiesPerPath`. Enforced today by gate funnels + `MapConstraintValidator`.
- ✅ Mimic → `TreasureMimicChancePercent` + `Encounters[Mimic]`.
- ❌ **No-repeat across a run** → new: encounter selection without replacement (design decision below).
- ❌ **Per-path multi-encounter minimum** → new: constraint on the chosen encounter's enemy count, not the
  node role.

## Concrete PerPathMinimums (once the two constraints land)
```
Act I : Combat 8,  MultiCombat 1, Elite 1, Event 3, Rest 2, Treasure 2, Shop 2   | MimicChance 5
Act II: Combat 8,  MultiCombat 2, Elite 2, Event 3, Rest 2, Treasure 1, Shop 2   | MimicChance 10
Act III:Combat 8,  MultiCombat 2, Elite 3, Event 3, Rest 2, Treasure 1, Shop 2   | MimicChance 15
Act IV: Combat 12, MultiCombat 3, Elite 4, Event 4, Rest 3, Treasure 2, Shop 3   | MimicChance 20
```
