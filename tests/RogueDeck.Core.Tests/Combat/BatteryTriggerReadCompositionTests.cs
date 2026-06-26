using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 substrate verification, batch 2 — trigger-based read probes. These exercise reading the
// triggering event's amount in the Effect Program model (the new ContextValueExpression, since event
// amounts were previously only reachable via legacy ICombatValueProvider) plus attacker/receiver
// attribution: #7 Reflect Plating, #16 Riposte, #40 Lifelink. Each trigger is scoped to its wearer via
// a *HasStatus filter so reflected/riposted damage cannot re-trigger into a loop.
public class BatteryTriggerReadCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void Marker(CombatDefinitionRegistryBuilder builder, StatusDefinitionId id) =>
        builder.RegisterStatus(new StatusDefinition(
            id, new PackageId("challenge"), $"status.{id.value}.name", $"status.{id.value}.desc",
            polarity: StatusPolarity.Buff));

    private static CardDefinitionId StrikeCard(CombatDefinitionRegistryBuilder builder, int amount)
    {
        var cardId = new CardDefinitionId($"challenge.strike_{amount}");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            "card.s.name", "card.s.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(amount))),
        });
        return cardId;
    }

    private static void HeroPlays(CombatState combat, CombatDefinitionRegistry registry, CardDefinitionId cardId)
    {
        var hero = combat.GetCombatant(HeroId);
        if (!hero.Resources.ContainsKey(StandardCombatIds.EnergyResource))
            hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Apply(CombatState combat, CombatDefinitionRegistry registry,
        CombatantId target, StatusDefinitionId status) =>
        new CombatQueueProcessor().ResolvePendingQueues(
            EnqueueApply(combat, target, status), registry);

    private static CombatState EnqueueApply(CombatState combat, CombatantId target, StatusDefinitionId status)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(target, status, Stacks: 1));
        return combat;
    }

    // #7 Reflect Plating: when the wearer takes unblocked attack damage, deal that exact amount back
    // to the attacker.
    [Fact]
    public void ReflectPlating_DealsTakenDamageBackToAttacker()
    {
        var reflect = new StatusDefinitionId("challenge.reflect");
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, reflect);
        var strike = StrikeCard(builder, 5);
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.DamageReceived.Define(
                new TriggeredEffectDefinitionId("challenge.reflect_trigger"),
                new EffectProgram<DamageReceivedTriggeredEffectContext>(
                    new DealDamageNode<DamageReceivedTriggeredEffectContext>(
                        CombatantTargetSelectors.Attacker,
                        new ContextValueExpression<DamageReceivedTriggeredEffectContext>(
                            c => c.CombatEvent.HealthDamage))),
                filters: [new DamageReceivedReceiverHasStatusTriggerFilter(reflect)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Apply(combat, registry, GoblinId, reflect);
        HeroPlays(combat, registry, strike);

        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);  // took 5
        Assert.Equal(15, combat.GetCombatant(HeroId).Health.Current);   // 5 reflected
    }

    // #16 Riposte: when the wearer is attacked while holding block, deal their current block back to
    // the attacker — without spending it.
    [Fact]
    public void Riposte_DealsCurrentBlockBackWithoutSpendingIt()
    {
        var riposte = new StatusDefinitionId("challenge.riposte");
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, riposte);
        var strike = StrikeCard(builder, 3);
        var block = new CombatantDefensivePoolExpression<DamageReceivedTriggeredEffectContext>(
            CombatantTargetSelectors.EventTarget, StandardCombatIds.BlockDefensivePool);
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.DamageReceived.Define(
                new TriggeredEffectDefinitionId("challenge.riposte_trigger"),
                new EffectProgram<DamageReceivedTriggeredEffectContext>(
                    new ConditionalEffectNode<DamageReceivedTriggeredEffectContext>(
                        new ComparisonExpression<DamageReceivedTriggeredEffectContext>(
                            block, ComparisonOperator.Greater,
                            new ConstantExpression<DamageReceivedTriggeredEffectContext>(0)),
                        new DealDamageNode<DamageReceivedTriggeredEffectContext>(
                            CombatantTargetSelectors.Attacker, block))),
                filters: [new DamageReceivedReceiverHasStatusTriggerFilter(riposte)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(10));
        Apply(combat, registry, GoblinId, riposte);
        HeroPlays(combat, registry, strike);

        // 3 damage fully absorbed → block 10 → 7, goblin HP unchanged; riposte deals 7 to the hero.
        Assert.Equal(7, goblin.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
        Assert.Equal(12, goblin.Health.Current);
        Assert.Equal(13, combat.GetCombatant(HeroId).Health.Current); // 20 − 7
    }

    // #40 Lifelink: 30 % of all damage the wearer deals returns as healing.
    [Fact]
    public void Lifelink_HealsThirtyPercentOfDamageDealt()
    {
        var lifelink = new StatusDefinitionId("challenge.lifelink");
        var builder = CombatTestFactory.CreateStandardBuilder();
        Marker(builder, lifelink);
        var strike = StrikeCard(builder, 10);
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.DamageDealt.Define(
                new TriggeredEffectDefinitionId("challenge.lifelink_trigger"),
                new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new HealNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.Attacker,
                        new DivideExpression<DamageDealtTriggeredEffectContext>(
                            new MultiplyExpression<DamageDealtTriggeredEffectContext>(
                                new ContextValueExpression<DamageDealtTriggeredEffectContext>(
                                    c => c.CombatEvent.HealthDamage),
                                new ConstantExpression<DamageDealtTriggeredEffectContext>(30)),
                            new ConstantExpression<DamageDealtTriggeredEffectContext>(100)))),
                filters: [new DamageDealtSourceHasStatusTriggerFilter(lifelink)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(10);
        Apply(combat, registry, HeroId, lifelink);
        HeroPlays(combat, registry, strike);

        Assert.Equal(2, combat.GetCombatant(GoblinId).Health.Current); // took 10
        Assert.Equal(13, hero.Health.Current);                          // healed 30 % of 10 = 3
    }
}
