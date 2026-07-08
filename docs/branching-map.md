# Branching Run-Map

**★ STATUS: ARC COMPLETE (2026-07-08).** All of B0–B4 done + committed. `RunMap` is now additively graph-shaped
(`MapEdge` + `Edges`/`EntryNodeIds`); `RunRunner` walks a graph by player-chosen path (`WalkGraph`) while a linear
map keeps the exact index loop byte-for-byte; `RunMapValidator` enforces a forward-only DAG with full reachability;
`RunMapBuilder` + `LayeredMapGenerator` author graph maps (the latter a deterministic Slay-the-Spire act); worked
examples drive real combat + event content end-to-end. Suite: Run 287 (+41 across the arc) / Scenario 512 / Sandbox
121 / Core 1324, all green, `dotnet format` clean. Commits: B0 e17caa4 · B1 af8c393 · B2 08eb30a · B3 54b52e8 ·
B4 7f9b4bc (plan @ 25c4cf1). Optional not-done: B5 map mutation (add/remove nodes/edges mid-run); exposing the
branching map in the Studio UI when Studio comes off hold (node layout row/col coords were deferred to that phase).

--- ORIGINAL PLAN (historical) ---

**Status:** planned 2026-07-08 (not started). The next arc after positional-combat, chosen from a fresh
gap-analysis: with positional combat + a persistent board done, the **branching run map** is the single
remaining Tier-1 roguelike-deckbuilder structural gap. Every node *kind* it needs (combat / event / shop /
rest / elite / boss) already exists as content on the run engine; what is missing is the **map topology** and
**player path choice**.

## Why now / where it sits

`RunMap` is, by explicit design, a **linear ordered list** (`Map/RunMap.cs`: "a branching graph … is a later
slice"). `RunRunner.Run` walks it with a bare `for (index) over Map.Nodes` loop, calling `run.AdvanceTo(index)`;
`RunState.Position` is an int index. The run-engine roadmap makes this **Principle 1**: *"Run engine = define
events + run them SEQUENTIALLY. Path arrangement (branching/map graph) is a separate future feature that hands
the engine a finished linear sequence. Do not build branching into the run engine."*

This arc honours that principle by drawing the seam in the right place: **node *resolution* stays
sequence-agnostic** (resolvers, effects, relics, the between-nodes interlude — all untouched). Only the
**traversal** generalizes: instead of "iterate every node in order," the runner asks the map graph for the
reachable successors of the current node and lets the player choose one. The chosen node is resolved by the
exact same machinery. Branching is therefore an **additive overlay on traversal**, not a rewrite of the engine
— the same discipline that made positional combat safe.

## Hard constraint (mirrors positional-combat): strictly additive, opt-in

A **linear map must behave byte-for-byte as today.** Enforced by invariants, checked every phase:

1. **Linear fallback.** A `RunMap` with no edges drives the exact index loop it does today; the full existing
   Run suite passes every phase.
2. **Everything additive & optional.** Edges, entry nodes, and layout coords are all optional; `RunState.Position`
   (int) stays; the linear `RunMap(nodes)` constructor stays.
3. **Serialization back-compat.** A map JSON with no `edges` deserializes to a linear map (old saves load).
4. **Resolution untouched.** Resolvers / effects / relics / `NodeEntered` / interlude fire identically — only
   *next-node selection* changes.
5. **Deterministic headless.** Player path choice reuses the existing `IRunChoiceProvider`; a headless run gets a
   deterministic default (seeded pick of a reachable successor), so balancing sims stay reproducible.

## Data model

Today: `RunMap(IReadOnlyList<Node> Nodes)`, `Node(NodeId Id, NodeType Type, object Payload)`.

Add (all optional, additive):
- `MapEdge(NodeId From, NodeId To)` — a directed edge. The graph = nodes + edges (adjacency).
- `RunMap.Edges` (default `[]`) and `RunMap.EntryNodeIds` (default `[]`). **No edges ⇒ linear** (consume `Nodes`
  in order, as today). Edges present ⇒ graph traversal.
- Entry nodes: where the walk can start. Absent ⇒ the first node (or all in-degree-0 nodes). A Slay-the-Spire
  act with several starting columns lists them explicitly; the player picks one.
- **Layout is UI-only.** Optional row/column coords on a node (like `CombatPosition` on a combatant: an inert
  overlay) let a map screen render the fork; **traversal reads only edges**, never coords. Keeps geometry out
  of the engine.

## Traversal / state

- `RunState.CurrentNodeId` (`NodeId?`) + a **visited set** + `AdvanceToNode(NodeId)`. `Position` (int) stays for
  the linear path.
- Step: resolve the current node → compute **reachable successors** = edge targets from the current node that
  are not yet visited → if >1, the player chooses via `IRunChoiceProvider`; if exactly 1, auto-advance; if 0,
  the branch (run) ends — a leaf node is the boss/finish.
- Expose the current reachable set on `RunState` so a map UI can render the offered fork; log/emit path events
  (`NodeChosen`) alongside the existing `NodeEntered`.

## Phases (dependency-ordered; each = its own commit + green tests; invariants held every phase)

- **B0 — Graph substrate (data only, zero behavior change).** `MapEdge`; `RunMap.Edges` + `EntryNodeIds`
  (optional, default empty); optional node layout coords for UI. Serialization round-trips; a map with no edges
  is unchanged. Invariant: existing linear runs byte-identical. *No traversal change yet.*
- **B1 — Graph traversal in `RunRunner`.** When the map has edges, drive by graph: start at an entry node (pick
  if several) → resolve → offer reachable successors → player picks (or auto-advance on one) → end at a leaf.
  `RunState.CurrentNodeId` + visited set + `AdvanceToNode`. A map with **no** edges keeps the exact index loop.
  `NodeChosen` event + path log.
- **B2 — Reachability & selection rules.** Forward-only / DAG validator (no cycles, boss reachable); successors
  offered = reachable ∧ unvisited; a node with no outgoing edges ends the run. Surface the reachable set on
  `RunState` for a UI. Guard against dead-ends (validator warns).
- **B3 — Map generation helpers (content, no engine privilege).** A `RunMapBuilder` / layered generator
  (StS-style act: N rows, branching columns, distribution of combat/elite/rest/shop, boss at the top) that
  emits a `RunMap` graph — deterministic from the run seed. Node *kinds* remain plain `NodeType` + payload.
- **B4 — Worked examples / validation.** End-to-end: a StS-style act (player picks a route → different nodes
  visited per choice, boss gate), a diamond/reconverging path, a linear-with-detours (Monster-Train-ish). Prove
  determinism and that unchosen paths are never forced.

Optional **B5 — map mutation** (the roadmap's deferred `RunMapMutationSystem`: add/remove edges/nodes mid-run) —
only if a game needs it.

## Grounding (verified 2026-07-08)

- `RunRunner.Run` (`src/RogueDeck.Run/RunRunner.cs`): the `for (index) over run.Map.Nodes` loop + `AdvanceTo` is
  the single traversal site — the whole change lands here plus the `RunMap`/`RunState` data.
- `RunState.Position` int index + `AdvanceTo(int)`; `RunMap(IReadOnlyList<Node>)`; `Node(Id, Type, Payload)`
  (payload untyped ⇒ new node kinds need no core change — already true).
- Player choice substrate exists: `IRunChoiceProvider` (events already use it), so the fork picker needs no new
  plumbing — a between-node "choose your path" is the same shape as an event choice.
- Node resolution is `NodeType → INodeResolver` with no kind switch in the runner — untouched by this arc.
