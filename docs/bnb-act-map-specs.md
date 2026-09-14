# Bureaucrats & Broomsticks — Act Map Specs

**Rewritten 2026-09-14** (map rework S14). What an act's map is has changed shape, and this file used to state
the old shape as the design. It now states the new one, and keeps the old one where it belongs: as the frozen
configuration of a generator that still ships.

**Two generators ship and the player picks at "New run ▸"** (`docs/strategic-map-generator-plan.md` §4b).
**v0.0.0** is the rule-based generator every run used until now; **v0.0.1** is the strategic one. A run remembers
which one laid its maps out, for as long as it lives — a BnB map is regenerated on resume rather than saved, so
the generator is part of the run's identity and not a menu setting. Both read the same act record
(`bnb-content/Converter/ActRules.cs`), and they read different halves of it.

---

## 1. What an act is — v0.0.1, and the design

An act is a **length**, a **budget of rooms**, a **depth profile**, a **floor of challenge on every route**, and
a **threshold below which a fork is not a choice**. None of these says what any one route must contain, and that
is the point of them: routes are allowed to differ.

| | Act I | Act II | Act III | Act IV |
|---|---:|---:|---:|---:|
| **Rooms a route walks** (`Rows`, boss included) | 23 | 24 | 25 | 35 |
| Width | 2–4 | 2–4 | 2–4 | 2–4 |
| Boss rooms | 1 | 1 | 1 | 1 |
| **Minimum path pressure** | 120 | 160 | 185 | 265 |
| Maximum (a remark, never a promise) | 200 | 240 | 270 | 360 |
| **Minimum fork contrast** | 30 | 30 | 30 | 30 |
| Mimic chance | 5 % | 10 % | 15 % | 20 % |

**Path pressure** is what one walk through the act asks of the player, in points: `Combat 10`,
`MultiCombat 15`, `Elite 25`, and everything else **0**. A campfire, a shop, a jar and a door ask nothing, which
is exactly why a route made of them is the route the floor catches. The floors above are **v0.0.0's own measured
thinnest routes**, so no walk through a strategic act is worth less than the easiest walk through the old one.

**Room budgets** are act-wide — `min .. target .. max`. Combat is not budgeted: it is the filler, and what it
comes to is whatever the rest leave.

| Role | Act I | Act II | Act III | Act IV |
|---|---|---|---|---|
| Elite | 3 · **5** · 8 | 4 · **6** · 9 | 5 · **7** · 10 | 7 · **10** · 14 |
| MultiCombat | 2 · **4** · 7 | 3 · **5** · 8 | 3 · **5** · 8 | 5 · **8** · 12 |
| Event | 8 · **11** · 15 | 8 · **11** · 15 | 9 · **12** · 16 | 12 · **16** · 21 |
| Rest | 4 · **6** · 9 | 4 · **6** · 9 | 4 · **6** · 9 | 7 · **9** · 12 |
| Treasure | 4 · **6** · 9 | 3 · **5** · 8 | 3 · **5** · 8 | 5 · **7** · 10 |
| Shop | 4 · **5** · 7 | 4 · **5** · 7 | 4 · **5** · 8 | 5 · **7** · 10 |
| Combat | ≈ 32 | ≈ 33 | ≈ 34 | ≈ 47 |

**Depth gates** — the shallowest point in the act at which a role may stand, as a percentage of the act's own
depth. Read by BOTH generators, because an elite that may not stand in the city's first third may not stand
there whichever generator laid the city out.

| Role | Act I | Act II | Act III | Act IV |
|---|---:|---:|---:|---:|
| Rest | 10 % | 10 % | 10 % | 8 % |
| Shop | 12 % | 10 % | 8 % | 8 % |
| MultiCombat | 20 % | 12 % | 15 % | 12 % |
| Elite | 35 % | 22 % | 18 % | 18 % |

**Depth bands** — each act in four quarters, with what each quarter must and may hold. A campfire in every
quarter so no stretch is unsurvivable; a ceiling on elites per quarter so they spread instead of queueing at the
end; the crowded fight kept rare while the deck is still a handful of cards. A band never relaxes a gate — a
quarter the gates keep a role out of simply cannot have it, however loudly the band asks.

---

## 2. Act V is not in any of those tables

It is a **gauntlet**: three boss rooms back to back, drawn from six without repetition, and nothing else at all —
no rooms, no recovery, no spoils. `Rows = 0`, `BossRooms = 3` on the rule-based spec, and **no strategic spec at
all**, because there is not one chosen room in it for a budget to be about. A run started on v0.0.1 therefore
walks Act V on v0.0.0, and the two agree about that act to the room.

## 3. Rules that hold whichever generator drew the map

- **No encounter template repeats within a run.** Every combat, elite and boss the player meets is a distinct
  template; selection is without replacement across the whole map, with a graceful fallback once a pool runs dry.
  Keep each role's pool at least as large as the nodes that draw it.
- **A treasure may bite.** A `Treasure` flips to a weak-elite fight at the act's own rate (5 / 10 / 15 / 20 %).
  Under v0.0.0 a treasure standing on a guarantee row never flipped — it *was* the promise — so with map-wide
  budgets the effective rate rises to the authored one for the first time.
- **Per-encounter earliest depth.** The elite masters' tables name individual fights, which no role gate can
  express; those are keyed by encounter id. Where a row can honour none of a role's candidates the gate yields,
  so a combat room is never left without a fight.

---

## 4. v0.0.0's per-path table — retired as the design, kept as the configuration

**The per-path era ran until 2026-09-11 and this is why it ended.** The promise was that *every single
entry→boss path* holds at least the table below. It was kept, and the way it was kept was the problem: each
promise became a full row that every route crosses, so roughly 85 % of an act was rows where every column held
the same room. Measured on Act I over all 162 of its routes:

```
Elite 1..1   MultiCombat 1..1   Rest 2..2   Shop 2..2   Treasure 2..3   Event 3..5   Combat 9..12
```

Four of seven room types identical on every route. The branching was decorative, the player's "choice" at a fork
was *rest, or rest*, and three quarters of all forks led into the same future twice. A promise about every route
is a promise that makes every route the same.

The table is **not deleted**, because v0.0.0 still ships and a baseline that quietly moved would be no baseline:
it is what a playtester compares the new maps against, and `bnb-content/Tests/Golden/map-v0.0.0.txt` pins what it
produces. It is frozen, and it is no longer a statement about what a BnB act is.

| Role | Act I | Act II | Act III | Act IV |
|---|---:|---:|---:|---:|
| Normal combats (`Combat`) | 8 | 8 | 8 | 12 |
| — of which **multi-encounter** | 1 | 2 | 2 | 3 |
| Elites (`Elite`) | 1 | 2 | 3 | 4 |
| Events (`Event`) | 3 | 3 | 3 | 4 |
| Campfires (`Rest`) | 2 | 2 | 2 | 3 |
| Treasure (`Treasure`) | 2 | 1 | 1 | 2 |
| Shops (`Shop`) | 2 | 2 | 2 | 3 |

Plus five free rows per act (`ActRules.RuleBasedFreeRows`) — the number the retired `FreeRows` formula produced
for every act in the game, written down rather than computed, since the computation's floor always won.

---

## 5. Engine coverage

Everything in §1 is engine, not BnB: `StrategicActSpec` (`Rows`, `BossRooms`, `MinWidth`/`MaxWidth`, `Topology`,
`LaneProfiles`, `Rooms`, `PathPressure`, `ForkQuality`, `Repair`, `MaxGenerationAttempts`), reachable from
`RunAct.StrategicMapGeneration` / `RunBlueprint.StrategicMapGeneration`, authored in the Studio's Map Rules tab
and exported to Godot. `StrategicActSpecValidator` says before a seed is spent whether an act's numbers can be
met at all; `StrategicActStatistics` says what they produce over a thousand seeds.

What §4 configures is `MapGenerationSpec.PerPathMinimums` / `PerPathMaximums` / `MinEnemiesPerPath` /
`WideGuaranteeRows`, enforced by gate funnels and `MapConstraintValidator`.

What §2 and §3 configure is shared: `BossRooms`, `TreasureMimicChancePercent`, `Encounters[Mimic]`,
`RoleMinimumDepthPercent`, `NodeRefMinimumDepthPercent`, `EncounterMinimumDepthPercent`.

**Measured, 1 000 acts per act on v0.0.1** (`--map-report`, and the numbers in
`docs/strategic-map-generator-plan.md` S13): all 4 000 clean, **no hollow forks at all**, and zero room types
identical on every route.
