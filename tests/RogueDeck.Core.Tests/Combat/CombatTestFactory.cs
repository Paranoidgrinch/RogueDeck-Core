using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

internal static class CombatTestFactory
{
    // A builder pre-loaded with the standard package — for tests that register extra
    // definitions before building.
    public static CombatDefinitionRegistryBuilder CreateStandardBuilder()
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage().RegisterDefinitions(builder);
        return builder;
    }

    // A built, immutable registry with only the standard package — for tests that do not
    // register anything extra.
    public static CombatDefinitionRegistry CreateStandardRegistry() =>
        CreateStandardBuilder().Build();

    public static CombatState CreateCombatWithHero()
    {
        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var hero = new CombatantState(
            new CombatantId("hero_001"),
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            StandardCombatIds.PlayerTeam,
            new HealthState(current: 20, max: 20));

        combat.AddCombatant(hero);

        return combat;
    }

    public static CombatState CreateCombatWithHeroAndGoblin()
    {
        var combat = CreateCombatWithHero();

        var goblin = new CombatantState(
            new CombatantId("goblin_001"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(goblin);

        return combat;
    }

    public static CombatState CreateCombatWithHeroAndTwoGoblins()
    {
        var combat = CreateCombatWithHeroAndGoblin();

        var secondGoblin = new CombatantState(
            new CombatantId("goblin_002"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(secondGoblin);

        return combat;
    }
}
