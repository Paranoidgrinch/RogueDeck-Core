using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Verify-compose batch for the ✅? battery probes #2 / #19 / #23 — confirm they assemble from existing
// primitives (or the sanctioned interception escape hatch) with no new engine code.
public class BatteryVerifyCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CombatantId Goblin2Id = new("goblin_002");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static void Play(CombatState combat, CombatDefinitionRegistry registry, CardDefinitionId cardId, CombatantId target)
    {
        var hero = combat.GetCombatant(HeroId);
        hero.SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        Resolve(combat, registry);
    }

    // #2 Vampiric Onslaught: a 3-hit attack heals the attacker for the total health damage across the hits.
    // Composes from a causal sequence of three damage nodes + a heal scaled by the summed outcomes.
    [Fact]
    public void Vampiric_ThreeHits_HealsForTotalHealthDamage()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var dmg1 = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg1");
        var dmg2 = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg2");
        var dmg3 = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg3");
        var cardId = new CardDefinitionId("challenge.vampiric");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(4), resultKey: dmg1),
                    new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(4), resultKey: dmg2),
                    new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(4), resultKey: dmg3),
                    new HealNode<CardPlayContext>(
                        CombatantTargetSelectors.Source,
                        new AddExpression<CardPlayContext>(
                            new AddExpression<CardPlayContext>(
                                new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(dmg1, o => o.HealthLost),
                                new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(dmg2, o => o.HealthLost)),
                            new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(dmg3, o => o.HealthLost))),
                ])),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(50);
        combat.GetCombatant(GoblinId).Health.SetCurrent(50);
        combat.GetCombatant(HeroId).Health.SetCurrent(5);

        Play(combat, registry, cardId, GoblinId);

        Assert.Equal(38, combat.GetCombatant(GoblinId).Health.Current); // 50 − 12
        Assert.Equal(17, combat.GetCombatant(HeroId).Health.Current);   // 5 + 12
    }

    // #2 again, the aggregate-sum variant: AoE lifesteal heals by the summed health damage across targets
    // (PreviousOutcomeSum over a single multi-target node's outcomes).
    [Fact]
    public void Vampiric_AoE_HealsForSummedHealthDamageAcrossTargets()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var aoe = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("aoe");
        var cardId = new CardDefinitionId("challenge.lifereap");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(4), resultKey: aoe),
                    new HealNode<CardPlayContext>(
                        CombatantTargetSelectors.Source,
                        new PreviousOutcomeSumExpression<CardPlayContext, DamageOutcome>(aoe, o => o.HealthLost)),
                ])),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        combat.GetCombatant(HeroId).Health.SetCurrent(5);

        Play(combat, registry, cardId, GoblinId);

        Assert.Equal(13, combat.GetCombatant(HeroId).Health.Current); // 5 + (4 + 4) across two goblins
    }

    // #19 Conduit: every 3rd status applied to the enemy deals a free 10 damage. Composes from a hidden
    // neutral counter status (incremented per debuff application via ApplyStatus → merge) read with a
    // modulo. The Debuff-polarity filter keeps the neutral counter's own application from being counted.
    // NOTE: distinct debuffs are applied because a re-applied mergeable status fires StatusMerged, not a
    // fresh StatusApplied — so "status applied" counts distinct applications, which is the faithful reading.
    [Fact]
    public void Conduit_EveryThirdStatusAppliedDealsTen()
    {
        var counter = new StatusDefinitionId("challenge.conduit_counter");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            counter, new PackageId("challenge"), "status.counter.name", "status.counter.desc",
            polarity: StatusPolarity.Neutral, usesStacks: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.StatusApplied.Define(
                new TriggeredEffectDefinitionId("challenge.conduit"),
                new EffectProgram<StatusAppliedTriggeredEffectContext>(
                    new CausalSequenceEffectNode<StatusAppliedTriggeredEffectContext>([
                        new ApplyStatusNode<StatusAppliedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget, counter, new ConstantExpression<StatusAppliedTriggeredEffectContext>(1)),
                        new ConditionalEffectNode<StatusAppliedTriggeredEffectContext>(
                            new ComparisonExpression<StatusAppliedTriggeredEffectContext>(
                                new RemainderExpression<StatusAppliedTriggeredEffectContext>(
                                    new CombatantStatusStacksExpression<StatusAppliedTriggeredEffectContext>(CombatantTargetSelectors.EventTarget, counter),
                                    new ConstantExpression<StatusAppliedTriggeredEffectContext>(3)),
                                ComparisonOperator.Equal,
                                new ConstantExpression<StatusAppliedTriggeredEffectContext>(0)),
                            then: new DealDamageNode<StatusAppliedTriggeredEffectContext>(
                                CombatantTargetSelectors.EventTarget, new ConstantExpression<StatusAppliedTriggeredEffectContext>(10))),
                    ])),
                filters: [new StatusAppliedPolarityTriggerFilter(StatusPolarity.Debuff)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(50);
        combat.GetCombatant(GoblinId).Health.SetCurrent(50);

        // Apply three distinct debuffs to the enemy; only the third trips the conduit. Weak/Frail/Poison
        // are used (not Vulnerable) so none amplifies the conduit's own incoming damage — keeps it at 10.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.WeakStatus, SourceCombatantId: HeroId, Stacks: 1));
        Resolve(combat, registry);
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.FrailStatus, SourceCombatantId: HeroId, Stacks: 1));
        Resolve(combat, registry);
        Assert.Equal(50, combat.GetCombatant(GoblinId).Health.Current); // no conduit yet after 2

        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, StandardCombatIds.PoisonStatus, SourceCombatantId: HeroId, Stacks: 1));
        Resolve(combat, registry);
        Assert.Equal(40, combat.GetCombatant(GoblinId).Health.Current); // 3rd debuff → 10 damage
    }

    // #23 Mirror Ward: the next debuff applied to the wearer is reflected onto its source instead of
    // landing, consuming a charge. Verified through the sanctioned interception escape hatch (a custom
    // IStatusApplicationInterceptor returning Replace retargeted to the source) — the same extension point
    // as ArtifactStatusApplicationInterceptor. A fully declarative replace+retarget remains a future option.
    [Fact]
    public void MirrorWard_ReflectsNextDebuffToSourceAndConsumesCharge()
    {
        var mirrorWard = new StatusDefinitionId("challenge.mirror_ward");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            mirrorWard, new PackageId("challenge"), "status.mw.name", "status.mw.desc",
            polarity: StatusPolarity.Buff, usesCharges: true));
        builder.RegisterStatusApplicationInterceptor(new MirrorWardInterceptor(mirrorWard));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // Hero wears Mirror Ward with one charge.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, mirrorWard, Charges: 1));
        Resolve(combat, registry);

        // Goblin applies Poison to the hero → reflected back onto the goblin; charge consumed.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus, SourceCombatantId: GoblinId, Stacks: 2));
        Resolve(combat, registry);

        Assert.DoesNotContain(combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == StandardCombatIds.PoisonStatus);
        Assert.Equal(2, combat.GetCombatant(GoblinId).Statuses.Where(s => s.DefinitionId == StandardCombatIds.PoisonStatus).Sum(s => s.Stacks));
        Assert.DoesNotContain(combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == mirrorWard && s.Charges > 0);

        // Second debuff is no longer reflected (charge spent).
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.PoisonStatus, SourceCombatantId: GoblinId, Stacks: 1));
        Resolve(combat, registry);
        Assert.Equal(1, combat.GetCombatant(HeroId).Statuses.Where(s => s.DefinitionId == StandardCombatIds.PoisonStatus).Sum(s => s.Stacks));
    }

    // Test-local custom interceptor (escape hatch) — reflects a debuff to its source while the wearer holds
    // a Mirror Ward charge, then consumes the charge.
    private sealed class MirrorWardInterceptor : IStatusApplicationInterceptor
    {
        private readonly StatusDefinitionId _ward;
        public MirrorWardInterceptor(StatusDefinitionId ward) => _ward = ward;

        public string ModifierId => "challenge.mirror_ward";
        public int Priority => 50;

        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
        {
            if (context.StatusDefinition.Polarity != StatusPolarity.Debuff)
                return InterceptionResult.Allow;
            if (context.Request.SourceCombatantId is not { } source)
                return InterceptionResult.Allow;

            var ward = context.TargetCombatant.Statuses.FirstOrDefault(s => s.DefinitionId == _ward && s.Charges > 0);
            if (ward is null)
                return InterceptionResult.Allow;

            context.Combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(context.TargetCombatant.Id, ward.Id));

            return InterceptionResult.Replace(new ApplyStatusEffectRequest(
                TargetCombatantId: source,
                StatusDefinitionId: context.Request.StatusDefinitionId,
                SourceCombatantId: context.TargetCombatant.Id,
                Stacks: context.Request.Stacks,
                DurationTurns: context.Request.DurationTurns,
                Charges: context.Request.Charges));
        }
    }
}
