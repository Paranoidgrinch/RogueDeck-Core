using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StatusDurationLifecycleTests
{
    [Fact]
    public void EndCurrentTurnReducesDurationStatusByOne()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.weak"),
                DurationTurns: 2));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);
        var weak = Assert.Single(hero.Statuses);

        Assert.Equal(new StatusDefinitionId("standard.weak"), weak.DefinitionId);
        Assert.Equal(1, weak.DurationTurns);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "StatusDurationReduced");
    }

    [Fact]
    public void EndCurrentTurnRemovesDurationStatusWhenItReachesZero()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.weak"),
                DurationTurns: 1));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);

        Assert.Empty(hero.Statuses);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "StatusExpired");
    }

    [Fact]
    public void EndCurrentTurnDoesNotRemoveStackOnlyStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.poison"),
                Stacks: 3));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);
        var poison = Assert.Single(hero.Statuses);

        Assert.Equal(new StatusDefinitionId("standard.poison"), poison.DefinitionId);
        Assert.Equal(3, poison.Stacks);
    }

    private static CombatState CreateCombatWithHeroAndGoblin()
    {
        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var hero = new CombatantState(
            new CombatantId("hero_001"),
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            new TeamId("player"),
            new HealthState(current: 20, max: 20));

        var goblin = new CombatantState(
            new CombatantId("goblin_001"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(hero);
        combat.AddCombatant(goblin);

        return combat;
    }
}
