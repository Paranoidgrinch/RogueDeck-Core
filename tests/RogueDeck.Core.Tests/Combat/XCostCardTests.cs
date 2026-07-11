using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// X-cost cards (StS parity): a card whose effect scales with, and spends, ALL of a resource. The engine already has
// the pieces — CombatantCurrentResourceExpression reads current energy as an amount, and LoseResource drains it — so
// an X-cost is a two-step program: use current energy as the effect amount, then lose all of it. This proves the
// pattern (playable at 0 energy for 0 effect, just like the source game).
public class XCostCardTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private sealed record Ctx;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(combat, combat.GetCombatant(HeroId), GoblinId),
                new TriggeredEffectActionSource(HeroId)));

    private static ICombatExpression<Ctx, int> CurrentEnergy() =>
        new CombatantCurrentResourceExpression<Ctx>(CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource);

    // "Deal X damage, X = energy spent": deal current energy, then drain it. Two nodes over the existing vocabulary.
    private static EffectProgram<Ctx> XStrike() =>
        new(new SequenceEffectNode<Ctx>(new IEffectNode<Ctx>[]
        {
            new DealDamageNode<Ctx>(CombatantTargetSelectors.EventTarget, CurrentEnergy()),
            new LoseResourceNode<Ctx>(CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource, CurrentEnergy()),
        }));

    [Fact]
    public void An_x_cost_card_scales_with_and_spends_all_energy()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(current: 3, max: 3));
        var goblinHp = combat.GetCombatant(GoblinId).Health.Current;

        EffectProgramExecutor.Execute(XStrike(), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(goblinHp - 3, combat.GetCombatant(GoblinId).Health.Current); // X = 3 damage
        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current); // all energy spent
    }

    [Fact]
    public void At_zero_energy_an_x_cost_card_does_nothing()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(current: 0, max: 3));
        var goblinHp = combat.GetCombatant(GoblinId).Health.Current;

        EffectProgramExecutor.Execute(XStrike(), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(goblinHp, combat.GetCombatant(GoblinId).Health.Current); // X = 0 → no damage, still valid to play
    }
}
