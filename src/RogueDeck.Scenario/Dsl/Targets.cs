using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Dsl;

// Readable facade over the engine's standard target selectors, so authored programs read naturally.
// These are the same pure selectors the engine ships — the DSL adds no new targeting semantics.
public static class Targets
{
    public static ICombatantTargetSelector Source => CombatantTargetSelectors.Source;
    public static ICombatantTargetSelector EventTarget => CombatantTargetSelectors.EventTarget;
    public static ICombatantTargetSelector AllEnemies => CombatantTargetSelectors.AllEnemiesOfSource;
    public static ICombatantTargetSelector AllAllies => CombatantTargetSelectors.AllAlliesOfSource;
    public static ICombatantTargetSelector Explicit(CombatantId id) => CombatantTargetSelectors.Explicit(id);
}
