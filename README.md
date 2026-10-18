<div align="center">

# ⚙️ RogueDeck Core

### Build the mechanic. Keep the core clean.

A deterministic, modular C# engine for roguelike deckbuilders — combat, whole runs, and a no-code Studio.
Strange mechanics compose *into* it instead of turning the core into a giant switch statement.

<br>

<a href="https://github.com/Paranoidgrinch/RogueDeck-Core">
  <img src="https://img.shields.io/badge/STAR_THE_ENGINE-181717?style=for-the-badge&logo=github&logoColor=white" alt="Star RogueDeck Core">
</a>
<a href="https://github.com/Paranoidgrinch/RogueDeck-Core/tree/main/src/RogueDeck.Core/Combat">
  <img src="https://img.shields.io/badge/EXPLORE_THE_ENGINE-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt="Explore the engine">
</a>
<a href="https://studio.moonvineforge.com">
  <img src="https://img.shields.io/badge/OPEN_ROGUEDECK_STUDIO-6B7D58?style=for-the-badge" alt="Open RogueDeck Studio">
</a>

<br><br>

[![CI](https://github.com/Paranoidgrinch/RogueDeck-Core/actions/workflows/ci.yml/badge.svg)](https://github.com/Paranoidgrinch/RogueDeck-Core/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square\&logo=dotnet\&logoColor=white)
![Language](https://img.shields.io/badge/language-C%23-512BD4?style=flat-square\&logo=dotnet\&logoColor=white)
![Status](https://img.shields.io/badge/combat_engine-v1_complete-3FB950?style=flat-square)
![Tests](https://img.shields.io/badge/tests-2%2C700%2B-3FB950?style=flat-square)
![Architecture](https://img.shields.io/badge/core-UI_independent-394B59?style=flat-square)
[![License](https://img.shields.io/badge/license-PolyForm_Noncommercial-394B59?style=flat-square)](LICENSE)

</div>

---

## Why it exists

Most combat code rots the same way: every new card, relic or status teaches the *whole* engine what it is,
until the core is one enormous switch statement nobody dares to touch.

RogueDeck Core takes the opposite bet. Mechanics are **composed** from a small set of primitives on a
deterministic queue, so the hundredth mechanic is no harder to add than the first — and the same final state
is reproducible from the same seed, every time.

It is a tested **engine you can use today**: drive it from your own game, script fights in code, or design a
whole game in [RogueDeck Studio](https://studio.moonvineforge.com) without writing a line of C#. It is not a
game itself — it carries no rendering, input or themes, only the reusable rules beneath a deckbuilder:
combat, the run around it, and the tools to author, play-test and replay both.

---

## The rule above all others

> **Adding a new effect must not require changing the central resolver.**

Everything is built around composition rather than special cases:

* no central switch over concrete effects
* no hardcoded `PoisonStacks`, `IsStunned` or `Thorns` fields
* no game-specific rules hidden inside turn processing
* no effects bypassing the deterministic queue
* no unrelated systems changing whenever a new mechanic appears

A mechanic joins the engine through focused definitions, handlers, providers and packages — not by teaching
the entire core what that mechanic is.

---

## Compose mechanics instead of hardcoding them

Triggered mechanics are separated into five questions:

```text
WHEN       What happened?
IF         Which conditions must match?
WHO        Which combatants are affected?
HOW MUCH   How is the value calculated?
WHAT       Which primitive effect is requested?
```

An existing event combines with an existing effect without another event-specific engine class:

```text
WHEN damage is received          WHEN a skill card is played
IF   the target has Thorns       IF   it is the second skill this turn
WHO  the attacker                WHO  the player
HOW  the current Thorns value    HOW  1
WHAT deal damage                 WHAT draw cards
```

The content describes the interaction. The engine resolves it.

---

## How resolution works

```mermaid
flowchart LR
    A[Combat event] --> B[Typed trigger]
    B --> C[Filters]
    C --> D[Target and value providers]
    D --> E[Effect program]
    E --> F[Deterministic queue]
    F --> G[Typed effect handler]
    G --> H[State change and new events]
    H --> F
```

Effects and events continue through the queue until the chain is complete or combat ends. Chain ancestry,
re-entry rules and configurable depth limits keep reactive mechanics powerful without allowing accidental
infinite loops — and a snapshot can be hashed at any point, so identical inputs prove identical outcomes.

---

## Getting started

### Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* Git

### Get the code and verify it

```bash
git clone https://github.com/Paranoidgrinch/RogueDeck-Core.git
cd RogueDeck-Core
dotnet restore
dotnet test          # ~2,700 tests across the solution
```

CI runs the same suite on Windows + Linux, verifies formatting with `dotnet format`, and builds the
benchmarks on every push and pull request to `main`.

### Build a game without code — RogueDeck Studio

**[studio.moonvineforge.com](https://studio.moonvineforge.com)** runs the Studio entirely in your browser
(Blazor WebAssembly, deployed from `src/RogueDeck.Studio.Web` on every push). To run it locally instead:

```bash
dotnet run --project src/RogueDeck.Studio
```

The Studio is one project document edited through focused tabs — hero, characters, cards, statuses, enemies
and encounters, relics, events, shops, consumables, workbenches and recipes, map rules, meta progression and
balance — plus a **Playtest** tab that plays the result on the real engine. Effect programs are edited
visually, every operation, trigger, selector and interceptor of the engine is reachable, drafts autosave, and
the finished game exports as a versioned `game.roguedeck.json` blueprint that any RogueDeck host can run.

### Author a fight in code

The scenario harness is the ergonomic way to *use* the engine: describe a fight, script the turns, run them
on the real combat loop, and read what happened.

```csharp
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

var fight = new ScenarioBlueprint();

// A card: deal 8 damage to the chosen target, for 1 energy.
fight.Cards.Add(new CardBlueprint("strike")
{
    Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 8)),
}.Cost(StandardCombatIds.EnergyResource, 1));

// An enemy action: smash the hero for 12.
fight.EnemyActions.Add(new EnemyActionBlueprint("smash", new ActionIntent("Smash", IntentKind.Attack))
{
    Program = new EffectProgram<EnemyActionContext>(
        new DealDamageNode<EnemyActionContext>(
            CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(12))),
});

// The hero (with a small deck) and the enemy.
fight.Hero = new HeroBlueprint("knight")
{
    MaxHealth = 50,
    Deck = { new DeckEntry(new CardDefinitionId("strike"), Count: 3) },
};
fight.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, Current: 3, Max: 3));

var ogre = new EnemyBlueprint("ogre") { MaxHealth = 40 };
ogre.Actions.Add(new EnemyActionDefinitionId("smash"));
fight.Enemies.Add(ogre);

// Script the turns…
var script = new ScenarioScript()
    .HeroPlays("strike", "ogre")
    .HeroEndsTurn()
    .EnemyActs("ogre", "smash", "knight")
    .Build();

// …run them on the real engine, and read the narrative log.
var report = new ScenarioRunner().Run(new Playthrough(fight, script, combatId: "demo"));
Console.WriteLine(new NarrativeLogRenderer().Render(report));
```

Need lower-level control for your own game loop? Drop down to `RogueDeck.Core` directly — build a
`CombatDefinitionRegistryBuilder`, register a `StandardCombatPackage`, and drive a `CombatState` with the
`CombatReplayRunner` (`PlayCardCommand` / `EndTurnCommand`). `src/RogueDeck.ConsoleSandbox/Program.cs` is a
complete, runnable end-to-end example of exactly that.

### Let a bot play it

`roguedeck-bot` plays whole runs of any exported game headlessly — many seeds in parallel, with a fight
planner, map analysis and full logs — and replays recorded player runs step for step:

```bash
dotnet run --project src/RogueDeck.Bot.Cli -c Release -- --game game.roguedeck.json --runs 20
dotnet run --project src/RogueDeck.Bot.Cli -c Release -- --game game.roguedeck.json --replay-run run.json
```

A replay prints `REPRODUCED` or the first room where it diverges — the engine's determinism, checked against
real play.

### Embed it in your project

The engine is a set of plain .NET 10 libraries with no game dependencies. Reference `src/RogueDeck.Core`
(combat) and `src/RogueDeck.Run` (runs) from your solution, or `dotnet pack` them into NuGet packages you
can consume locally. `docs/godot-export-contract.md` describes how a frontend hosts a run. Everything you
author is pure C#, deterministic, and unit-testable without a UI.

---

## What you get

The combat core ships with:

* deterministic combat state and seeded randomness (snapshot → stable hash → replay)
* modular effect-request / handler registries and ordered effect + event queues
* turn, round and combat-result lifecycles
* cards, card instances and zones — draw, discard, exhaust, banish, retain
* card costs and cost modifiers; reusable resources and resource lifecycles
* status definitions with stacking, duration and charges
* damage, healing, block and defensive pools
* typed target selectors and value expressions
* card, status, resource and lifecycle triggers, with re-entry protection
* a modifier / interceptor pipeline (damage, block, cost, status application, death prevention)
* package-based registration, combat logging, and architecture-guard tests

Mechanics are authored as **Effect Programs** — typed trees of nodes (sequence, conditional, causal, repeat,
for-each) over native operations, value expressions and selectors — validated by a build-time preflight and
executed on the deterministic queue. Cards, enemy actions, triggered programs and runtime temporary rules all
run through the same runtime.

### The run around the fight

`RogueDeck.Run` turns single fights into a complete roguelike run, built on the same composition rules:

* characters and parties, starting decks, inventories and run-level resources
* relics with both a run face and combat rules, consumables, shops, workbenches and recipes
* narrative events with choices and effects; encounter pools for normal, elite and boss fights
* multi-act runs on generated branching maps (layered, rule-based and strategic generators, all seeded)
* rewards, card upgrades and removal, meta progression across runs, tutorials
* saves, run recording and deterministic replay

### The toolchain around the core

| Project | What it gives you |
| --- | --- |
| **`RogueDeck.Studio`** / **`RogueDeck.Studio.Web`** | The no-code authoring Studio — server-hosted for local work, WebAssembly for the browser. |
| **`RogueDeck.Sandbox.Combat`** / **`RogueDeck.Sandbox.Run`** | The Studio's editors and interactive combat and run sessions, as Razor component libraries. |
| **`RogueDeck.Scenario`** | A fluent DSL to author a fight as blueprints + scripted turns, run it through the *real* engine, and render a readable play-by-play. |
| **`RogueDeck.Bot`** / **`RogueDeck.Bot.Cli`** | A bot that plays and replays whole runs headlessly — breadth testing, balance sweeps and run-recording verification. |
| **`RogueDeck.ConsoleSandbox`** | A runnable end-to-end example of driving combat directly; CI runs it as a smoke test. |

---

## Repository structure

```text
RogueDeck-Core
├── src
│   ├── RogueDeck.Core            # the combat engine (Cards, Effects, Resources, …)
│   ├── RogueDeck.Run             # the run engine (map, relics, events, shops, meta, saves, replay)
│   ├── RogueDeck.Scenario        # scripting harness: blueprints, fluent DSL, runner, narrative log
│   ├── RogueDeck.Sandbox.Combat  # Studio editors + interactive combat (Razor library)
│   ├── RogueDeck.Sandbox.Run     # Studio editors + interactive runs (Razor library)
│   ├── RogueDeck.Studio          # the Studio, server-hosted
│   ├── RogueDeck.Studio.Web      # the Studio, WebAssembly (studio.moonvineforge.com)
│   ├── RogueDeck.Bot             # the run-playing bot
│   ├── RogueDeck.Bot.Cli         # roguedeck-bot: batch runs and run replay
│   └── RogueDeck.ConsoleSandbox  # runnable end-to-end smoke demo
├── tests
│   ├── RogueDeck.Core.Tests      # the main engine suite (the executable specification)
│   ├── RogueDeck.Run.Tests       # run engine, map generators (incl. fuzz tests)
│   ├── RogueDeck.Scenario.Tests
│   ├── RogueDeck.Sandbox.Tests
│   └── RogueDeck.Core.Benchmarks # BenchmarkDotNet
├── docs                          # design notes: run engine, maps, party, relics, Godot export contract
└── .github/workflows             # ci.yml (build, format, test) · deploy-studio.yml (Studio to Pages)
```

This README is the front door; the engine documents itself through code and tests. Start in
**`src/RogueDeck.Core/Combat`** for combat, **`src/RogueDeck.Run`** for runs, and **`tests/`** for a focused
test of every behaviour they guarantee (including the architecture-guard tests that enforce the rules above).

---

## Status

**Combat Engine v1: complete (strict) and green.** The full closure series shipped end to end — typed and
cardinality preflight, the complete generic trigger-parity matrix, temporary rules and delayed programs,
deterministic trace identities, snapshots + hashing + replay, and the modifier / interceptor pipeline.
Formatting is clean and the suite runs on Windows and Linux on every push.

**Run engine and Studio: in active development.** They carry a complete five-act game —
[Bureaucrats & Broomsticks](#built-with-roguedeck) — and the Studio can author, play-test and export one.

Two behaviours are documented, stable v1 decisions rather than gaps: `CombatResultChanged` is
observable-but-non-triggerable (the queue stops at a terminal result), and only the `Combatant` target domain
exists (the machinery is enforced; other domains are a later phase). The Card Composition Engine (snippet
system / card generation) is intentionally **deferred** as a separate future project.

---

## Built with RogueDeck

### 🧙 Bureaucrats and Broomsticks

A satirical fantasy roguelike deckbuilder about magic, paperwork and deeply questionable career choices —
five acts from the City Offices to the Divine Ledger, running entirely on this engine:

| Repository | Role |
| --- | --- |
| **RogueDeck-Core** (this one) | the rules: combat and run engine, bot, replay |
| [**bnb-content**](https://github.com/Paranoidgrinch/bnb-content) | the game as data — builds and validates `game.roguedeck.json` |
| [**bnb-godot**](https://github.com/Paranoidgrinch/bnb-godot) | the Godot 4 (.NET) frontend |
| [**bnb-runs**](https://github.com/Paranoidgrinch/bnb-runs) | recorded playtest runs, replayable with `roguedeck-bot` |

The game began as a [hardcoded Python terminal prototype](https://github.com/Paranoidgrinch/bureaucrats-and-broomsticks-v2).
Building it revealed the cost of tightly coupled effects and event-specific action classes. RogueDeck Core is
the answer:

> Build the next mechanic without making the previous hundred harder to understand.

---

## Try to break the architecture

Have a mechanic that *should* be hard to represent? Bring it. A card, relic, status, resource, curse, enemy
action, triggered interaction or completely unreasonable combat rule — the most useful ideas are not the
balanced ones, they are the ones that force the engine to prove its modularity.

For a mechanic proposal, describe: **what causes it, which conditions apply, who it targets, how its value is
calculated, and what effect it produces.**

<div align="center">

<a href="https://github.com/Paranoidgrinch/RogueDeck-Core/issues/new">
  <img src="https://img.shields.io/badge/OPEN_AN_ISSUE-Describe_the_mechanic-181717?style=for-the-badge&logo=github&logoColor=white" alt="Open an issue">
</a>
<a href="https://moonvineforge.com/card-forge.html">
  <img src="https://img.shields.io/badge/BUILD_YOUR_OWN_CARD-Enter_the_Community_Forge-6B7D58?style=for-the-badge" alt="Enter the Community Card Forge">
</a>

</div>

---

<div align="center">

### New effects should extend the language — not expand the switch statement.

<br>

<a href="https://github.com/Paranoidgrinch/RogueDeck-Core">
  <img src="https://img.shields.io/badge/STAR_ROGUEDECK_CORE-181717?style=for-the-badge&logo=github&logoColor=white" alt="Star RogueDeck Core">
</a>
<a href="https://github.com/Paranoidgrinch">
  <img src="https://img.shields.io/badge/FOLLOW_ME_ON_GITHUB-181717?style=for-the-badge&logo=github&logoColor=white" alt="Follow me on GitHub">
</a>

<br><br>

<a href="https://moonvineforge.com"><strong>Moonvine Forge Studios</strong></a>

<br>

<em>Small studio. Careful craft. Unusual worlds.</em>

</div>

---

## License

RogueDeck Core is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE): you may use, change and
share it for any noncommercial purpose, as long as you credit the original
(`Required Notice: Copyright (c) 2026 Paranoidgrinch`). Commercial use needs a separate license — open an
issue or get in touch through [Moonvine Forge Studios](https://moonvineforge.com).

Versions published before October 2026 were released under the MIT License and remain available under it.
