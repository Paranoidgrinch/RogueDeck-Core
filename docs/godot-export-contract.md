# Godot export contract

The Studio's **Export for Godot** button (Run tab) produces one file — `game.roguedeck.json` — that is the whole
game: every card, enemy, encounter, event, shop, relic, status, character, the map, the meta rules, **and** the
presentation manifest that says how it all looks. This document is the contract a playable frontend (Godot or
anything else) builds against.

The exported file is byte-for-byte a `RunBlueprint` serialized by `RunJson` — the same document the Studio edits.
There is no separate "export format"; export = the export gate passing + a normalized re-serialization.

## The consumption model

The engine is **not** re-implemented in the frontend. A Godot (.NET) project references the engine assemblies and
lets them run the rules; the frontend renders state and forwards player input.

| Assembly | Why the frontend needs it |
| --- | --- |
| `RogueDeck.Core` | the combat engine (effect programs, statuses, resources, positional combat) |
| `RogueDeck.Scenario` | the authoring data types cards/actions/statuses deserialize through (`CardData`, …) |
| `RogueDeck.Run` | the run layer: `RunBlueprint`, `RunJson`, `RunRunner`, map, relics, meta progression |

Loading is two calls:

```csharp
var options = RunJson.CreateOptions();
RunBlueprint blueprint = RunJson.BlueprintFromJson(json, options); // upgrades old schemas, rejects newer ones
```

then the host drives it the way the Studio's own playback does: build the combat content from the blueprint,
register a `StandardRunPackage` with a combat driver, and run `RunRunner` with a choice provider that surfaces
event choices / path picks / card picks to the player. The reference host implementation is
`RunPlayback.BuildContent` + the drivers in `src/RogueDeck.Sandbox.Run/Composition/RunPlayback.cs` (Blazor is the
first frontend of this contract; the Godot host reuses the same seam). Runs are deterministic per seed: the same
blueprint + seed + player answers replay to the same state.

## Schema versioning

The document carries a top-level `"SchemaVersion"` (an integer). The rules, implemented by `RunBlueprintSchema`
(`src/RogueDeck.Run/Serialization/RunBlueprintSchema.cs`):

- **Missing stamp = version 0** — the pre-versioning era. Every consumer upgrades it transparently.
- **Older than current**: `RunJson.BlueprintFromJson` migrates the raw JSON up the ladder, step by step, before
  deserializing. Migrations run on `JsonNode`, so a step can reshape structures the current C# model no longer has.
- **Newer than current**: loading fails with a clear "made by a newer Studio" error. A frontend must surface that
  message, never guess.
- The current version **is** the ladder's length: adding a migration step is the version bump; the two cannot drift.

A frontend embedding engine assemblies of version N therefore reads every document with schema ≤ N and cleanly
refuses the rest.

## Document shape

Top level: a single JSON object, **PascalCase** property names, enums as **strings**.

| Property | Content |
| --- | --- |
| `SchemaVersion` | integer, see above |
| `Deck` | shared starting deck: list of card ids |
| `Cards` | card definitions (`CardData`): id, name key, costs, tags, the play/lifecycle effect programs |
| `EnemyActions` | enemy action definitions: id, intent, effect program |
| `Encounters` | fights: enemies (id, HP, action rotation + intent rules), hero resources, grid settings |
| `Events` | id → event script (situations, choices, run effects) |
| `Shops` | id → shop definition (stock, prices, reroll, services) |
| `Map` | `Nodes` (id, type, payload ref), `Edges` (from→to), `EntryNodeIds`, `Layout` (authored x/y per node) |
| `Statuses` | authored status definitions |
| `Relics` | relics as data: run-triggered programs + combat rules |
| `CombatResources` | run-global energy-like resources |
| `Consumables` | consumable kinds |
| `Start` | hero name, HP, starting deck/relics/consumables/resources, board units, party |
| `Characters` | selectable roster (each with a full `Start`, optional `UnlockFlag`) |
| `MetaRules` | run-end meta progression rules |
| `Presentation` | the look manifest — see below |

Polymorphic nodes (effects, expressions, selectors, rewards, payloads, …) always use the envelope

```json
{ "kind": "fx.heal", "value": { ... } }
```

where `kind` comes from the closed registry in `RunJson.DefaultRegistry()` and combat effect programs use the
`CombatJson` registries. Unknown kinds fail loading — the vocabulary is versioned with the schema, not open.

## The presentation manifest

`Presentation` is the Godot-facing half of the document: **the engine never reads it**, so it can never change
gameplay. Shape:

```json
"Presentation": {
  "Cards":       { "smite":       { "Art": "cards/smite.png", "FlavorText": "…", "Tags": ["rare"], "Extra": { "frame": "gold" } } },
  "Relics":      { "windfall":    { "Art": "relics/windfall.png" } },
  "Consumables": { … }, "Statuses": { … }, "Enemies": { … }, "Encounters": { … },
  "Characters":  { … }, "Events": { … }, "Shops": { … },
  "Game": { "Art": "title.png", "Extra": { "theme": "dark" } }
}
```

Per section, entity id → `EntityPresentation`:

- `Art` — an asset id/relative path **in the frontend's own asset scheme**. The contract deliberately does not say
  what an art id means; the consuming game maps it (a Godot project typically resolves it under its own
  `res://assets/` root).
- `FlavorText` — flavor only; rules text derives from the entity's definition.
- `Tags` — freeform labels to key visual treatments off ("rare", "fire", "boss").
- `Extra` — arbitrary per-game key→value hints for anything the named fields don't cover (frame styles, VFX ids,
  sound cues).

Semantics: an entity **without** an entry gets the frontend's default look; an entry pointing at a non-existent
entity is flagged by validation and cannot pass the export gate. `Enemies` is keyed by the enemy definition id used
inside encounters — the same id means the same look everywhere. `Game` is the one non-per-entity slot (title art,
global theme hints).

## What the export gate guarantees

An exported document passed `RunDocumentValidator.ValidateForExport`, so a frontend may assume:

- every reference resolves: deck→cards, enemy→actions, map node→encounter/event/shop, character/party
  decks/relics/consumables→definitions, presentation entries→entities; no duplicate ids;
- the map is non-empty and, when it declares edges, a forward-only DAG with valid endpoints and reachable nodes;
- every way the run can start yields a non-empty deck;
- every card cost names a resource that some combat resource or encounter actually defines.

Defensive loading is still correct engineering, but these classes of error are authoring errors the Studio keeps,
not runtime conditions the frontend must design UI for.
