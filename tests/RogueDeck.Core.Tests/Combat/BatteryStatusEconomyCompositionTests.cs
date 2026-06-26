using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probes #34 Corruption and #46 Tax.
//   #34 needs one small read atom (sum of stacks by polarity); the rest composes.
//   #46 composes entirely (enemy-action resource spend + a declarative damage-scaling status gated on the
//       depleted resource), no engine code.
public class BatteryStatusEconomyCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static int Stacks(CombatState combat, CombatantId id, StatusDefinitionId s) =>
        combat.GetCombatant(id).Statuses.Where(x => x.DefinitionId == s).Sum(x => x.Stacks);

    // #34 Corruption: convert all of the target's buffs into the equivalent stacks of debuffs. Reads the
    // total buff stacks with the new CombatantStacksByPolarity atom, applies that many stacks of a
    // corruption debuff, then removes all buffs (the debuff is unaffected by the buff-polarity removal).
    [Fact]
    public void Corruption_ConvertsAllBuffStacksIntoEquivalentDebuffStacks()
    {
        var corruption = new StatusDefinitionId("challenge.corruption");
        var cardId = new CardDefinitionId("challenge.corrupt");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            corruption, new PackageId("challenge"), "s.n", "s.d",
            polarity: StatusPolarity.Debuff, usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "c.n", "c.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new ApplyStatusNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, corruption,
                        new CombatantStacksByPolarityExpression<CardPlayContext>(CombatantTargetSelectors.EventTarget, StatusPolarity.Buff)),
                    new RemoveStatusesByPolarityNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, StatusPolarity.Buff),
                ])),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        // Goblin holds 3 Strength + 2 Dexterity = 5 buff stacks.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.StrengthStatus, Stacks: 3));
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.DexterityStatus, Stacks: 2));
        Resolve(combat, registry);

        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        Resolve(combat, registry);

        Assert.Equal(0, Stacks(combat, GoblinId, StandardCombatIds.StrengthStatus));
        Assert.Equal(0, Stacks(combat, GoblinId, StandardCombatIds.DexterityStatus));
        Assert.Equal(5, Stacks(combat, GoblinId, corruption)); // equivalent debuff stacks
    }

    // #46 Tax: the boss's actions cost it 1 energy; once it can't pay (energy depleted), its damage is
    // halved. The spend is an EnemyActionExecuted → LoseResource; the halving is a declarative
    // DamageDealt ScalePercent-50 status applied when the energy hits 0 (gated, no bespoke modifier).
    [Fact]
    public void Tax_DepletedEnergyHalvesBossDamage()
    {
        var exhausted = new StatusDefinitionId("challenge.tax_exhausted");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            exhausted, new PackageId("challenge"), "s.n", "s.d", polarity: StatusPolarity.Debuff,
            passiveModifiers: [new PassiveModifierSpec(PassiveModifierPipeline.DamageDealt, PassiveModifierOperation.ScalePercent, 50)]));
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.EnemyActionExecuted.Define(
                new TriggeredEffectDefinitionId("challenge.tax"),
                new EffectProgram<EnemyActionExecutedTriggeredEffectContext>(
                    new CausalSequenceEffectNode<EnemyActionExecutedTriggeredEffectContext>([
                        new LoseResourceNode<EnemyActionExecutedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource, new ConstantExpression<EnemyActionExecutedTriggeredEffectContext>(1)),
                        new ConditionalEffectNode<EnemyActionExecutedTriggeredEffectContext>(
                            new ComparisonExpression<EnemyActionExecutedTriggeredEffectContext>(
                                new CombatantCurrentResourceExpression<EnemyActionExecutedTriggeredEffectContext>(CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource),
                                ComparisonOperator.Equal,
                                new ConstantExpression<EnemyActionExecutedTriggeredEffectContext>(0)),
                            then: new ApplyStatusNode<EnemyActionExecutedTriggeredEffectContext>(
                                CombatantTargetSelectors.Source, exhausted, new ConstantExpression<EnemyActionExecutedTriggeredEffectContext>(1))),
                    ]))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var boss = combat.GetCombatant(GoblinId);
        boss.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 3));

        var actionId = new EnemyActionDefinitionId("challenge.boss_attack");

        // While the boss still has energy, its attack lands at full strength.
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 10, GoblinId));
        Resolve(combat, registry);
        Assert.Equal(10, combat.GetCombatant(HeroId).Health.Current); // 20 − 10

        // The boss acts → spends its last energy → becomes exhausted.
        combat.EnqueueEvent(new EnemyActionExecutedCombatEvent(actionId, GoblinId, HeroId));
        Resolve(combat, registry);
        Assert.Equal(0, boss.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Contains(boss.Statuses, s => s.DefinitionId == exhausted);

        // Now its damage is halved.
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 10, GoblinId));
        Resolve(combat, registry);
        Assert.Equal(5, combat.GetCombatant(HeroId).Health.Current); // 10 − (10 × 50%)
    }
}
