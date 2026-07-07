namespace RogueDeck.Scenario.Scripting;

// One authored step in a scenario script. Steps are pure intent — the ScenarioRunner translates each into
// real engine turn processing / effect requests. They add NO combat semantics of their own.
public abstract record ScenarioStep;

// The hero plays a card, resolved from its hand by card id, at an optional target.
public sealed record HeroPlaysCard(string CardId, string? TargetId = null) : ScenarioStep;

// The hero declares its turn over; the engine ends it and starts the next combatant's turn.
public sealed record HeroEndsTurn : ScenarioStep;

// The hero uses a consumable during combat, running its combat-use program on the live fight.
public sealed record HeroUsesConsumable : ScenarioStep;

// A named enemy executes a registered action. The runner first advances real turns until it is that
// enemy's turn, so the Round/Turn counters and turn automation reflect the true turn structure.
public sealed record EnemyActs(string EnemyId, string ActionId, string? TargetId = null) : ScenarioStep;

// Advance real turns until it is the hero's turn again — ending any remaining enemy turns and wrapping
// the round. This is the "next round" boundary.
public sealed record AdvanceToNextRound : ScenarioStep;
