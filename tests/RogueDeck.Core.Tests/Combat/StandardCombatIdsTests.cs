using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StandardCombatIdsTests
{
    [Fact]
    public void StandardCombatIdsExposeExpectedStatusIds()
    {
        Assert.Equal(new StatusDefinitionId("standard.poison"), StandardCombatIds.PoisonStatus);
        Assert.Equal(new StatusDefinitionId("standard.weak"), StandardCombatIds.WeakStatus);
        Assert.Equal(new StatusDefinitionId("standard.strength"), StandardCombatIds.StrengthStatus);
        Assert.Equal(new StatusDefinitionId("standard.thorns"), StandardCombatIds.ThornsStatus);
        Assert.Equal(new StatusDefinitionId("standard.stun"), StandardCombatIds.StunStatus);
    }

    [Fact]
    public void StandardCombatIdsExposeExpectedTagIds()
    {
        Assert.Equal(new TagId("buff"), StandardCombatIds.BuffTag);
        Assert.Equal(new TagId("debuff"), StandardCombatIds.DebuffTag);
        Assert.Equal(new TagId("damage_over_time"), StandardCombatIds.DamageOverTimeTag);
        Assert.Equal(new TagId("damage_modifier"), StandardCombatIds.DamageModifierTag);
        Assert.Equal(new TagId("triggered_damage"), StandardCombatIds.TriggeredDamageTag);
        Assert.Equal(new TagId("control"), StandardCombatIds.ControlTag);
    }

    [Fact]
    public void StandardCombatIdsExposeExpectedTeamIds()
    {
        Assert.Equal(new TeamId("player"), StandardCombatIds.PlayerTeam);
        Assert.Equal(new TeamId("enemy"), StandardCombatIds.EnemyTeam);
    }
}
