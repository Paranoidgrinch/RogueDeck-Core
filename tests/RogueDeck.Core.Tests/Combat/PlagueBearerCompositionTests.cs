using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #12 Plague Bearer: when a poisoned enemy dies, apply its remaining Poison stacks to all
// other living enemies. Closes the gap that no selector addressed the *downed* event combatant — the
// new SourceIncludingDowned selector lets a CombatantDowned-triggered program read the just-downed
// unit's status stacks (the living-only Source/EventTarget selectors resolve to nothing for a downed
// combatant). The AoE target "all other living enemies" is the downed unit's living allies.
public class PlagueBearerCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static CombatState WithEnemies(int n)
    {
        var combat = CombatTestFactory.CreateCombatWithHero();
        for (var i = 0; i < n; i++)
            combat.AddCombatant(new CombatantState(
                new CombatantId($"goblin_{i:D3}"),
                new CombatantDefinitionId("standard.goblin"),
                "combatant.goblin", StandardCombatIds.EnemyTeam,
                new HealthState(current: 50, max: 50)));
        return combat;
    }

    [Fact]
    public void PlagueBearer_OnPoisonedEnemyDeathSpreadsItsPoisonToOtherLivingEnemies()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CombatantDowned.Define(
                new TriggeredEffectDefinitionId("challenge.plague_bearer"),
                new EffectProgram<CombatantDownedTriggeredEffectContext>(
                    new ApplyStatusNode<CombatantDownedTriggeredEffectContext>(
                        CombatantTargetSelectors.AllAlliesOfSource, // downed unit's living teammates
                        StandardCombatIds.PoisonStatus,
                        new CombatantStatusStacksExpression<CombatantDownedTriggeredEffectContext>(
                            CombatantTargetSelectors.SourceIncludingDowned, StandardCombatIds.PoisonStatus))),
                filters: [new CombatantDownedTeamTriggerFilter(StandardCombatIds.EnemyTeam)]));
        var registry = builder.Build();

        var combat = WithEnemies(3); // goblin_000 (the carrier) + _001 + _002
        var carrier = new CombatantId("goblin_000");
        combat.EnqueueEffect(new ApplyStatusEffectRequest(carrier, StandardCombatIds.PoisonStatus, Stacks: 4));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Kill the poisoned carrier with lethal damage; downing fires the CombatantDowned trigger.
        combat.EnqueueEffect(new DealDamageEffectRequest(carrier, 100, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.False(combat.GetCombatant(carrier).IsAlive);
        Assert.Equal(4, Poison(combat, "goblin_001"));
        Assert.Equal(4, Poison(combat, "goblin_002"));
        Assert.Equal(0, Poison(combat, HeroId.value)); // different team — not an ally of the carrier
    }

    private static int Poison(CombatState combat, string id) =>
        combat.GetCombatant(new CombatantId(id)).Statuses
            .Where(s => s.DefinitionId == StandardCombatIds.PoisonStatus)
            .Sum(s => s.Stacks);
}
