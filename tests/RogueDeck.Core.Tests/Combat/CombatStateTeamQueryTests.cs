using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatStateTeamQueryTests
{
    [Fact]
    public void HasLivingCombatantsOnTeamReturnsTrueWhenTeamHasLivingCombatant()
    {
        var combat = CreateCombatWithHeroAndGoblin();

        Assert.True(combat.HasLivingCombatantsOnTeam(new TeamId("player")));
        Assert.True(combat.HasLivingCombatantsOnTeam(new TeamId("enemy")));
    }

    [Fact]
    public void HasLivingCombatantsOnTeamReturnsFalseWhenTeamHasNoLivingCombatants()
    {
        var combat = CreateCombatWithHeroAndGoblin();

        var goblin = combat.GetCombatant(new CombatantId("goblin_001"));

        goblin.SetLifecycleState(CombatantLifecycleState.Downed);

        Assert.True(combat.HasLivingCombatantsOnTeam(new TeamId("player")));
        Assert.False(combat.HasLivingCombatantsOnTeam(new TeamId("enemy")));
    }

    [Fact]
    public void GetLivingCombatantsOnTeamReturnsOnlyLivingCombatantsOnRequestedTeam()
    {
        var combat = CreateCombatWithHeroAndTwoGoblins();

        var firstGoblin = combat.GetCombatant(new CombatantId("goblin_001"));

        firstGoblin.SetLifecycleState(CombatantLifecycleState.Downed);

        var livingEnemies = combat.GetLivingCombatantsOnTeam(new TeamId("enemy"));

        var enemy = Assert.Single(livingEnemies);

        Assert.Equal(new CombatantId("goblin_002"), enemy.Id);
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

    private static CombatState CreateCombatWithHeroAndTwoGoblins()
    {
        var combat = CreateCombatWithHeroAndGoblin();

        var secondGoblin = new CombatantState(
            new CombatantId("goblin_002"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(secondGoblin);

        return combat;
    }
}
