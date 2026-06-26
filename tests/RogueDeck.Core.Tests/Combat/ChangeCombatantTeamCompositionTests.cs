using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #54 Possession: revive a downed enemy at 50% HP onto your team. Closes the team-mutation
// gap with a runtime ChangeCombatantTeam native op (CombatantState.SetTeam already existed; only the
// effect pipeline was missing). The op is AnyCombatantIncludingDowned-eligible so it can convert a downed
// unit; turn order and card zones are untouched — only team membership changes. Revive (lifecycle→Alive)
// and the 50%-HP set (SetHealth) are existing primitives, so Possession is pure composition on top.
public class ChangeCombatantTeamCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void ChangeTeam_ConvertsLivingEnemyToPlayerTeamAndFlipsAllyEnemyRelations()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.convert");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            // Move the chosen (living) enemy onto the player's team.
            Program = new EffectProgram<CardPlayContext>(
                new ChangeCombatantTeamNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.PlayerTeam)),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);
        Assert.Equal(StandardCombatIds.PlayerTeam, goblin.TeamId);
        Assert.True(goblin.IsAlive);
        // It is now an ally of the hero and no longer an enemy.
        Assert.Contains(GoblinId, combat.GetLivingCombatantsOnTeam(StandardCombatIds.PlayerTeam).Select(c => c.Id));
        Assert.DoesNotContain(GoblinId, combat.GetLivingCombatantsOnTeam(StandardCombatIds.EnemyTeam).Select(c => c.Id));
    }

    [Fact]
    public void Possession_RevivesDownedEnemyAtHalfHealthOntoPlayerTeam()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.possession");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            // Revive the named (downed) enemy and convert it — both ops accept a downed target. The
            // explicit selector names the unit regardless of living status.
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new SetCombatantLifecycleStateNode<CardPlayContext>(
                        CombatantTargetSelectors.Explicit(GoblinId),
                        CombatantLifecycleState.Alive),
                    new ChangeCombatantTeamNode<CardPlayContext>(
                        CombatantTargetSelectors.Explicit(GoblinId),
                        StandardCombatIds.PlayerTeam),
                ])),
        });
        var registry = builder.Build();

        // Two enemies so downing the first does not end the combat (which would halt the queue and
        // cancel the Possession program).
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));

        // Down the first enemy with lethal damage; the second keeps the combat ongoing.
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 100, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.False(combat.GetCombatant(GoblinId).IsAlive);

        // Play Possession: revive + convert to player team.
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(GoblinId);
        Assert.True(goblin.IsAlive);
        Assert.Equal(StandardCombatIds.PlayerTeam, goblin.TeamId);

        // 50%-HP set is the existing SetHealth primitive applied to the now-living unit (max 12 → 6).
        combat.EnqueueEffect(new SetHealthEffectRequest(GoblinId, goblin.Health.Max / 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Contains(GoblinId, combat.GetLivingCombatantsOnTeam(StandardCombatIds.PlayerTeam).Select(c => c.Id));
    }
}
