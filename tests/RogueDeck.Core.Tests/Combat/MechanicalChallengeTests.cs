using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Phase 9 §16 — Mechanical challenge suite.
//
// Each test proves that a complex game mechanic can be expressed as a composed
// Effect Program without a mechanic-specific handler. If a mechanic cannot be
// expressed with the current language, §16.2 applies: identify the gap, prefer
// a reusable node/expression over a new handler, and document the decision.
//
// Gaps discovered in this phase are noted in comments marked [GAP].
//
// Legend for node abbreviations used in comments:
//   CS = CausalSequenceEffectNode   (reactions settle between children)
//   FE = ForEachTargetEffectNode
//   IF = ConditionalEffectNode
//   RP = RepeatEffectNode
public class MechanicalChallengeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CombatDefinitionRegistryBuilder, CombatState) Setup()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        return (builder, combat);
    }

    private static CardDefinitionBuilder FreeCard(
        CardDefinitionId id,
        EffectProgram<CardPlayContext> program)
    {
        var card = new CardDefinitionBuilder(id, new PackageId("challenge"), $"card.{id}.name", $"card.{id}.desc");
        card.Program = program;
        return card;
    }

    private static CardInstance GiveCard(CombatState combat, CombatantId owner, CardDefinitionId def)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), def, owner, CardZone.Hand);
        combat.GetCardZones(owner).AddCard(inst);
        return inst;
    }

    private static void PlayCard(CombatState combat, CombatDefinitionRegistry registry,
        CombatantId source, CardInstance card, CombatantId? target = null)
    {
        combat.EnqueueEffect(new PlayCardEffectRequest(source, card.Id, target));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CAUSAL OUTCOMES
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: deal damage, then heal the source for the health damage actually dealt.
    // CS[ DealDamageNode(→ dmgKey), HealNode(amount = dmgKey.HealthLost) ]
    [Fact]
    public void Challenge_HealForActualHealthDamageDealt()
    {
        var (builder, combat) = Setup();
        var dmgKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");

        var cardId = new CardDefinitionId("challenge.vampiric_strike");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(7),
                    resultKey: dmgKey),
                new HealNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(
                        dmgKey, o => o.HealthLost)),
            ])));
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        hero.Health.SetCurrent(10);  // hero at 10/20
        var inst = GiveCard(combat, HeroId, cardId);

        PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);

        // Goblin took 7 damage: 12 → 5
        Assert.Equal(5, combat.GetCombatant(GoblinId).Health.Current);
        // Hero healed for 7 (capped at max 20): 10 + 7 = 17
        Assert.Equal(17, hero.Health.Current);
    }

    // Mechanic: drain a defensive pool and deal damage equal to the amount drained.
    // CS[ ModifyDefensivePool(goblin, block, -999 → poolKey), DealDamage(hero, abs(poolKey.AppliedDelta)) ]
    [Fact]
    public void Challenge_DamageForActualBlockDrained()
    {
        var (builder, combat) = Setup();
        var poolKey = new EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>("pool");

        // Give goblin 5 Block
        combat.GetCombatant(GoblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        var cardId = new CardDefinitionId("challenge.shatter");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                // Drain all Block from goblin (large negative delta, clamped to current pool)
                new ModifyDefensivePoolNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<CardPlayContext>(-999),
                    resultKey: poolKey),
                // Deal damage equal to block drained (AppliedDelta is negative, so abs it)
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new AbsExpression<CardPlayContext>(
                        new PreviousOutcomeFieldExpression<CardPlayContext, PoolChangeOutcome>(
                            poolKey, o => o.AppliedDelta))),
            ])));
        builder.RegisterCard(card);

        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);

        // 5 Block drained → 5 damage → 12 - 5 = 7
        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(0, combat.GetCombatant(GoblinId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    // Mechanic: gain resource, then draw cards equal to the amount actually gained.
    // CS[ GainResource(hero, 2, → gainKey), DrawCards(count = gainKey.GainedAmount) ]
    [Fact]
    public void Challenge_DrawCardsEqualToResourceGained()
    {
        var (builder, combat) = Setup();
        var gainKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gain");
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 3));

        // Put 4 cards in draw pile to have something to draw
        var drawPileCardId = new CardDefinitionId("challenge.dummy");
        builder.RegisterCard(FreeCard(drawPileCardId, new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>())));
        for (var i = 0; i < 4; i++)
        {
            var c = new CardInstance(combat.CreateNextCardInstanceId(), drawPileCardId, HeroId, CardZone.DrawPile);
            combat.GetCardZones(HeroId).AddCard(c);
        }

        var cardId = new CardDefinitionId("challenge.font_of_power");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new GainResourceNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    StandardCombatIds.EnergyResource,
                    new ConstantExpression<CardPlayContext>(2),
                    resultKey: gainKey),
                new DrawCardsNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, GainResourceOutcome>(
                        gainKey, o => o.GainedAmount)),
            ])));
        builder.RegisterCard(card);

        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst);

        // Gained 2 energy (1+2=3, capped at 3 → GainedAmount = 2), drew 2 cards
        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(2, combat.GetCardZones(HeroId).Hand.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // MULTI-TARGET AND AGGREGATION
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: deal damage to every poisoned enemy.
    // FE[ IF(IterationTargetHasStatus(poison)) { DealDamage(iteration, stacks) } ]
    [Fact]
    public void Challenge_DamageEveryPoisonedEnemy()
    {
        var (builder, combat) = Setup();

        // Add a second goblin
        var goblin2Id = new CombatantId("goblin_002");
        var goblin2 = new CombatantState(goblin2Id, new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(8, 8));
        combat.AddCombatant(goblin2);

        var poisonId = new StatusDefinitionId("standard.poison");

        var cardId = new CardDefinitionId("challenge.plague_burst");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new ForEachTargetEffectNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new ConditionalEffectNode<CardPlayContext>(
                    new IterationTargetHasStatusExpression<CardPlayContext>(poisonId),
                    then: new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.IterationTarget,
                        new IterationTargetStatusStacksExpression<CardPlayContext>(poisonId))))));
        builder.RegisterCard(card);

        // Poison goblin_001 with 3 stacks; goblin_002 has no poison
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, poisonId, Stacks: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst);

        // Goblin_001 had 3 poison → took 3 damage: 12 → 9
        Assert.Equal(9, combat.GetCombatant(GoblinId).Health.Current);
        // Goblin_002 had no poison → took 0 damage: 8 → 8
        Assert.Equal(8, combat.GetCombatant(goblin2Id).Health.Current);
    }

    // Mechanic: deal damage to all enemies, then gain Block equal to total health lost.
    // CS[ DealDamage(all enemies, 4, → dmgKey), ModifyDefensivePool(source, +sum(HealthLost)) ]
    [Fact]
    public void Challenge_GainBlockEqualToTotalHealthLostAcrossTargets()
    {
        var (builder, combat) = Setup();
        var goblin2Id = new CombatantId("goblin_002");
        combat.AddCombatant(new CombatantState(goblin2Id, new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(12, 12)));

        // Give goblins 2 Block each so health loss < damage requested
        combat.GetCombatant(GoblinId).AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(2));
        combat.GetCombatant(goblin2Id).AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(2));

        var dmgKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");
        var cardId = new CardDefinitionId("challenge.blade_storm");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                // Deal 4 damage to every enemy (2 blocked by block pool → 2 health lost each)
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<CardPlayContext>(4),
                    resultKey: dmgKey),
                // Gain block = total health lost (2 + 2 = 4)
                new ModifyDefensivePoolNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    StandardCombatIds.BlockDefensivePool,
                    new PreviousOutcomeSumExpression<CardPlayContext, DamageOutcome>(dmgKey, o => o.HealthLost)),
            ])));
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst);

        // Each goblin: 4 damage - 2 block = 2 health lost → hero gets 4 block total
        Assert.Equal(10, combat.GetCombatant(GoblinId).Health.Current);   // 12 - 2 = 10
        Assert.Equal(10, combat.GetCombatant(goblin2Id).Health.Current);   // 12 - 2 = 10
        var heroBlock = hero.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var bp) ? bp.Current : 0;
        Assert.Equal(4, heroBlock);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STATUS INTERACTION
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: consume status stacks and deal damage equal to those stacks.
    // CS[ ModifyStatusStacks(goblin, poison, -999, → stacksKey), DealDamage(goblin, stacksKey.OldStacks) ]
    [Fact]
    public void Challenge_ConsumeStatusStacksAndDealDamage()
    {
        var (builder, combat) = Setup();
        var poisonId = new StatusDefinitionId("standard.poison");

        var stacksKey = new EffectResultKey<OrderedTargetOutcomes<ModifyStatusStacksOutcome>>("stacks");
        var cardId = new CardDefinitionId("challenge.venom_burst");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                // Drain all stacks (large negative delta)
                new ModifyStatusStacksNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    poisonId,
                    new ConstantExpression<CardPlayContext>(-999),
                    resultKey: stacksKey),
                // Deal damage = stacks before drain
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new PreviousOutcomeFieldExpression<CardPlayContext, ModifyStatusStacksOutcome>(
                        stacksKey, o => o.OldStacks)),
            ])));
        builder.RegisterCard(card);

        // Apply 5 poison stacks to goblin
        combat.EnqueueEffect(new ApplyStatusEffectRequest(GoblinId, poisonId, Stacks: 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());
        Assert.Equal(5, combat.GetCombatant(GoblinId).Statuses[0].Stacks);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);

        // Goblin had 5 stacks → took 5 damage: 12 - 5 = 7; and now has 0 poison
        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
    }

    // Mechanic: apply a status; if it was blocked, deal damage instead.
    // CS[ ApplyStatus(goblin, poison, → applyKey), IF(applyKey.Blocked) { DealDamage(goblin, 4) } ]
    [Fact]
    public void Challenge_BranchOnBlockedStatusApplication()
    {
        var (builder, combat) = Setup();

        // Register an interceptor that blocks Poison on goblin
        builder.RegisterStatusApplicationInterceptor(new BlockAllStatusInterceptor());

        var poisonId = new StatusDefinitionId("standard.poison");
        var applyKey = new EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>("apply");
        var cardId = new CardDefinitionId("challenge.toxic_strike");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    poisonId,
                    stacks: new ConstantExpression<CardPlayContext>(3),
                    resultKey: applyKey),
                new ConditionalEffectNode<CardPlayContext>(
                    new PreviousOutcomeBoolFieldExpression<CardPlayContext, ApplyStatusOutcome>(
                        applyKey, o => o.Blocked),
                    then: new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(4))),
            ])));
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);

        // Status blocked → fallback damage of 4: 12 - 4 = 8
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // REPETITION
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: hit three times; reactions (e.g. triggers) settle between each hit.
    // RP(3)[ DealDamage(goblin, 3) ]  — causal between iterations by design of RepeatEffectNode
    [Fact]
    public void Challenge_MultiHitWithReactionsBetweenHits()
    {
        var (builder, combat) = Setup();

        // Register a trigger that heals the goblin for 1 each time it takes damage
        var triggerId = new TriggeredEffectDefinitionId("challenge.pain_reflex");
        var trigger = TriggeredProgramContextAdapters.DamageReceived.Define(
            id: triggerId,
            program: new EffectProgram<DamageReceivedTriggeredEffectContext>(
                new HealNode<DamageReceivedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<DamageReceivedTriggeredEffectContext>(1))));
        builder.RegisterTriggeredEffectDefinition(trigger);

        var cardId = new CardDefinitionId("challenge.triple_strike");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new RepeatEffectNode<CardPlayContext>(
                count: new ConstantExpression<CardPlayContext>(3),
                body: new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(3)))));
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);

        // 3 hits × 3 damage = 9 damage total. After each hit, reflex heals 1 → net -3+1 per hit = -2 net per hit
        // 3 × 2 net damage = 6 net damage. 12 - 6 = 6
        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current);
    }

    // Mechanic: repeat N times where N is the target's current status stack count.
    // RP(poisonStacks)[ GainBlock(1) ]
    [Fact]
    public void Challenge_RepeatCountDrivenByStatusStacks()
    {
        var (builder, combat) = Setup();
        var poisonId = new StatusDefinitionId("standard.poison");

        var cardId = new CardDefinitionId("challenge.antidote_burst");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new RepeatEffectNode<CardPlayContext>(
                count: new CombatantStatusStacksExpression<CardPlayContext>(
                    CombatantTargetSelectors.Source, poisonId),
                body: new ModifyDefensivePoolNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<CardPlayContext>(1)))));
        builder.RegisterCard(card);

        // Hero has 4 Poison stacks
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, poisonId, Stacks: 4));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst);

        // Hero had 4 Poison → repeated 4 times → gained 4 Block
        var block = hero.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var bp) ? bp.Current : 0;
        Assert.Equal(4, block);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: deal damage; if it downed the target, set the combat result to Victory.
    // CS[ DealDamage(goblin, lethal, → dmgKey), IF(goblin downed) { SetCombatResult(Victory) } ]
    [Fact]
    public void Challenge_DownTargetThenSetCombatResultConditionally()
    {
        var (builder, combat) = Setup();
        var lcKey = new EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>("lc");

        var cardId = new CardDefinitionId("challenge.finishing_blow");
        var card = FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                // Deal lethal damage
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(999)),
                // Explicitly down (SetLifecycleState after health hits 0 the engine may auto-down,
                // but here we use SetCombatantLifecycleStateNode directly for the conditional branch).
                new SetCombatantLifecycleStateNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    CombatantLifecycleState.Downed,
                    resultKey: lcKey),
                // If it actually changed (was alive → downed), declare Victory.
                new ConditionalEffectNode<CardPlayContext>(
                    new PreviousOutcomeBoolFieldExpression<CardPlayContext, SetCombatantLifecycleStateOutcome>(
                        lcKey, o => o.WasChanged),
                    then: new SetCombatResultNode<CardPlayContext>(CombatResult.Victory)),
            ])));
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = GiveCard(combat, HeroId, cardId);
        PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);

        Assert.Equal(CombatantLifecycleState.Downed, combat.GetCombatant(GoblinId).LifecycleState);
        Assert.Equal(CombatResult.Victory, combat.Result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DETERMINISM
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: same program, same initial state → same final hash.
    [Fact]
    public void Challenge_SameProgramSameInitialState_ProducesSameHash()
    {
        var (builder, combat1) = Setup();
        var (_, combat2) = Setup();

        var cardId = new CardDefinitionId("challenge.strike_det");
        builder.RegisterCard(FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(4)))));

        foreach (var combat in new[] { combat1, combat2 })
        {
            combat.GetCombatant(HeroId).AddResource(
                StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
            var inst = GiveCard(combat, HeroId, cardId);
            PlayCard(combat, builder.Build(), HeroId, inst, GoblinId);
        }

        var hash1 = CombatStateHasher.ComputeHash(combat1.CreateSnapshot());
        var hash2 = CombatStateHasher.ComputeHash(combat2.CreateSnapshot());
        Assert.Equal(hash1, hash2);
    }

    // Mechanic: different programs → different final hash.
    [Fact]
    public void Challenge_DifferentPrograms_ProduceDifferentHash()
    {
        var (builder, combat1) = Setup();
        var (_, combat2) = Setup();

        var cardId1 = new CardDefinitionId("challenge.strike_a");
        var cardId2 = new CardDefinitionId("challenge.strike_b");
        builder.RegisterCard(FreeCard(cardId1, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(3)))));
        builder.RegisterCard(FreeCard(cardId2, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(6)))));

        combat1.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        combat2.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));

        PlayCard(combat1, builder.Build(), HeroId, GiveCard(combat1, HeroId, cardId1), GoblinId);
        PlayCard(combat2, builder.Build(), HeroId, GiveCard(combat2, HeroId, cardId2), GoblinId);

        var hash1 = CombatStateHasher.ComputeHash(combat1.CreateSnapshot());
        var hash2 = CombatStateHasher.ComputeHash(combat2.CreateSnapshot());
        Assert.NotEqual(hash1, hash2);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TRIGGER / RULE MECHANICS (delayed effects, temporary rules) — gate 6
    // ══════════════════════════════════════════════════════════════════════════

    // Mechanic: "at the start of your next turn, deal 4 to all enemies" — a delayed AoE
    // assembled purely from typed nodes (InstallTemporaryRuleNode carrying a TurnStarted program
    // of DealDamageNode), no mechanic-specific handler and no SideEffectNode.
    [Fact]
    public void Challenge_DelayedDamageOnNextTurnStart()
    {
        var (builder, combat) = Setup();

        var delayedRule = TriggeredProgramContextAdapters.TurnStarted.Define(
            id: new TriggeredEffectDefinitionId("challenge.delayed_aoe"),
            program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                new DealDamageNode<TurnStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<TurnStartedTriggeredEffectContext>(4))));

        var cardId = new CardDefinitionId("challenge.schedule_delayed");
        builder.RegisterCard(FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new InstallTemporaryRuleNode<CardPlayContext>(delayedRule, TemporaryRuleLifetime.OneShot))));

        var registry = builder.Build();
        combat.SetActiveCombatant(HeroId);

        PlayCard(combat, registry, HeroId, GiveCard(combat, HeroId, cardId), GoblinId);
        // The card only scheduled the effect; the goblin is unharmed so far.
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);

        // Hero's next turn starts → the delayed AoE fires exactly once.
        FireTurnStarted(combat, registry, HeroId);
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);

        // It was one-shot: a later turn start does nothing more.
        FireTurnStarted(combat, registry, HeroId);
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    // Mechanic: "until the end of this round, gain 2 block whenever you take damage" — a temporary
    // rule that fires while live and is gone after the round, assembled from typed nodes.
    [Fact]
    public void Challenge_UntilEndOfRoundBlockOnDamage()
    {
        var (builder, combat) = Setup();
        var registry = builder.Build();
        combat.SetActiveCombatant(HeroId);

        // On DamageReceived, the damaged combatant (EventTarget) gains 2 block.
        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.DamageReceived.Define(
                id: new TriggeredEffectDefinitionId("challenge.block_on_damage"),
                program: new EffectProgram<DamageReceivedTriggeredEffectContext>(
                    new GainBlockNode<DamageReceivedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<DamageReceivedTriggeredEffectContext>(2)))),
            TemporaryRuleLifetime.UntilEndOfRound(1));

        // Hero takes damage this round → the rule fires and grants block.
        Damage(combat, registry, HeroId, 1);
        Assert.True(HeroBlock(combat) > 0, "rule should have granted block while live");

        // Advance past the round → the rule expired and was pruned.
        combat.AdvanceRound();
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    private static int HeroBlock(CombatState combat) =>
        combat.GetCombatant(HeroId).DefensivePools
            .TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;

    // Mechanic: "deal damage to each enemy equal to the number of enemies" — a ForEach whose body
    // evaluates an aggregate (SumOverTargets) that itself iterates. Proves nested iteration scopes
    // compose: the inner aggregate does not clobber the outer loop's iteration target.
    [Fact]
    public void Challenge_ForEachBodyWithNestedAggregate()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var goblinB = new CombatantId("goblin_002");

        var cardId = new CardDefinitionId("challenge.foreach_aggregate");
        builder.RegisterCard(FreeCard(cardId, new EffectProgram<CardPlayContext>(
            new ForEachTargetEffectNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.IterationTarget,
                    new SumOverTargetsExpression<CardPlayContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<CardPlayContext>(1)))))));
        var registry = builder.Build();

        PlayCard(combat, registry, HeroId, GiveCard(combat, HeroId, cardId), GoblinId);

        // Two enemies → SumOverTargets = 2 → each correct enemy takes 2 (not clobbered by the
        // aggregate's internal iteration).
        Assert.Equal(10, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(10, combat.GetCombatant(goblinB).Health.Current);
    }

    // Mechanic: an enemy action schedules a delayed effect by installing a temporary rule — proving
    // enemy actions are first-class authors of delayed/temporary mechanics, from typed nodes.
    [Fact]
    public void Challenge_EnemyActionInstallsTemporaryRule()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var delayed = TriggeredProgramContextAdapters.TurnStarted.Define(
            id: new TriggeredEffectDefinitionId("challenge.enemy_delayed"),
            program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                new DealDamageNode<TurnStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<TurnStartedTriggeredEffectContext>(3))));

        var actionId = new EnemyActionDefinitionId("challenge.scheme");
        builder.RegisterEnemyAction(new EnemyActionDefinitionBuilder(
            actionId, new PackageId("challenge"), "a.name", "a.desc")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new InstallTemporaryRuleNode<EnemyActionContext>(delayed, TemporaryRuleLifetime.OneShot)),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(GoblinId);

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, actionId, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Single(combat.TemporaryTriggeredPrograms);
        Assert.Equal(20, combat.GetCombatant(HeroId).Health.Current); // not fired yet

        // The goblin's next turn starts → the scheduled rule hits the goblin's enemies (the hero).
        combat.EnqueueEvent(new TurnStartedCombatEvent(GoblinId, combat.CurrentRound, combat.CurrentTurn));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(17, combat.GetCombatant(HeroId).Health.Current);
        Assert.Empty(combat.TemporaryTriggeredPrograms);
    }

    private static void FireTurnStarted(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId combatantId)
    {
        combat.EnqueueEvent(new TurnStartedCombatEvent(combatantId, combat.CurrentRound, combat.CurrentTurn));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Damage(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId targetId, int amount)
    {
        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: targetId, Amount: amount, SourceCombatantId: GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STUBS
    // ══════════════════════════════════════════════════════════════════════════

    // Blocks all status applications — used to test the "branch on blocked" challenge.
    private sealed class BlockAllStatusInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "challenge.block_all";
        public int Priority => 0;
        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context) =>
            InterceptionResult.Block;
    }
}
