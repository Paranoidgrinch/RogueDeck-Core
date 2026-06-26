using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 substrate, batch 4 — resource-drain composition. Going through battery probe #21 Energy Leech
// surfaced a real gap: LoseResourceNode's executor was registered in EffectNodeExecutorRegistry.Default
// but NOT in StandardCombatPackage, so any program using LoseResourceNode failed the registry Build()
// preflight under the standard package. With the executor now registered, the leech chain composes:
// LoseResource (capped at the target's current) → outcome read of the actual amount → gain it + scale
// damage by it.
public class LoseResourceCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // #21 Energy Leech: steal up to 2 energy from the boss (only what it has), gain that much, and deal
    // damage equal to the amount actually stolen.
    [Fact]
    public void EnergyLeech_StealsCappedAmountThenGainsAndDamagesByActual()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var stolenKey = new EffectResultKey<OrderedTargetOutcomes<LoseResourceOutcome>>("stolen");

        var cardId = new CardDefinitionId("challenge.energy_leech");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            "card.leech.name", "card.leech.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    // Drain up to 2 energy from the target — capped at its current by the handler.
                    new LoseResourceNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, StandardCombatIds.EnergyResource,
                        new ConstantExpression<CardPlayContext>(2), resultKey: stolenKey),
                    // Gain the actual amount stolen.
                    new GainResourceNode<CardPlayContext>(
                        CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource,
                        new PreviousOutcomeFieldExpression<CardPlayContext, LoseResourceOutcome>(
                            stolenKey, o => o.LostAmount)),
                    // Deal damage equal to the actual amount stolen.
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new PreviousOutcomeFieldExpression<CardPlayContext, LoseResourceOutcome>(
                            stolenKey, o => o.LostAmount)),
                ])),
        });
        var registry = builder.Build(); // would throw RDCP (no executor) before the fix

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        var goblin = combat.GetCombatant(GoblinId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 3));
        goblin.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 3)); // only 1 to steal

        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Only 1 energy was available → stolen = 1.
        Assert.Equal(0, goblin.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(1, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(11, goblin.Health.Current); // 12 − 1 (damage = amount stolen)
    }
}
