using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 substrate verification, batch 3 — more self-contained read compositions:
// #39 Cleave, #47 Alternating Current (turn parity), #33 Purge, #10 Momentum (cards-played-this-turn).
public class BatteryReadCompositionBatch3Tests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

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

    private static CardDefinitionId Card(CombatDefinitionRegistryBuilder builder, string id,
        EffectProgram<CardPlayContext> program)
    {
        var cardId = new CardDefinitionId(id);
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            "card.n", "card.d")
        { Program = program });
        return cardId;
    }

    private static void Play(CombatState combat, CombatDefinitionRegistry registry,
        CardDefinitionId cardId, CombatantId? target)
    {
        var hero = combat.GetCombatant(HeroId);
        if (!hero.Resources.ContainsKey(StandardCombatIds.EnergyResource))
            hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        else
            hero.Resources[StandardCombatIds.EnergyResource].SetCurrent(3);
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // #39 Cleave: full damage to the target, half to all other enemies.
    [Fact]
    public void Cleave_FullToTargetHalfToOthers()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = WithEnemies(3); // goblin_000 (target) + _001 + _002
        var target = new CombatantId("goblin_000");

        var cardId = Card(builder, "challenge.cleave", new EffectProgram<CardPlayContext>(
            new SequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(8)),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.Except(
                        CombatantTargetSelectors.AllEnemiesOfSource, CombatantTargetSelectors.EventTarget),
                    new ConstantExpression<CardPlayContext>(4)),
            ])));
        Play(combat, builder.Build(), cardId, target);

        Assert.Equal(42, combat.GetCombatant(target).Health.Current);                 // 50 − 8
        Assert.Equal(46, combat.GetCombatant(new CombatantId("goblin_001")).Health.Current); // 50 − 4
        Assert.Equal(46, combat.GetCombatant(new CombatantId("goblin_002")).Health.Current);
    }

    // #47 Alternating Current: on odd turns deal +3 damage, on even turns gain +3 block. Combat starts
    // on turn 1 (odd) → the damage branch fires and the block branch does not.
    [Fact]
    public void AlternatingCurrent_TurnParityDrivesBranch()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));

        var cardId = Card(builder, "challenge.alternating", new EffectProgram<CardPlayContext>(
            new ConditionalEffectNode<CardPlayContext>(
                new ComparisonExpression<CardPlayContext>(
                    new RemainderExpression<CardPlayContext>(
                        new TurnNumberExpression<CardPlayContext>(),
                        new ConstantExpression<CardPlayContext>(2)),
                    ComparisonOperator.Equal, new ConstantExpression<CardPlayContext>(1)),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(3)),
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(3)))));
        Play(combat, builder.Build(), cardId, GoblinId);

        Assert.Equal(9, combat.GetCombatant(GoblinId).Health.Current); // 12 − 3 (odd-turn damage branch)
        Assert.Equal(0, combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    // #33 Purge: remove all debuffs from yourself; gain 2 block per debuff removed.
    [Fact]
    public void Purge_GainsBlockPerDebuffRemoved()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var removedKey = new EffectResultKey<OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>>("removed");
        var cardId = Card(builder, "challenge.purge", new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new RemoveStatusesByPolarityNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, StatusPolarity.Debuff, resultKey: removedKey),
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new MultiplyExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(2),
                        new PreviousOutcomeFieldExpression<CardPlayContext, RemoveStatusesByPolarityOutcome>(
                            removedKey, o => o.RemovedCount))),
            ])));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));
        // Two debuffs on the hero.
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.WeakStatus, DurationTurns: 2));
        combat.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, StandardCombatIds.VulnerableStatus, DurationTurns: 2));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Play(combat, registry, cardId, GoblinId);

        Assert.Empty(hero.Statuses);  // both debuffs purged
        Assert.Equal(4, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current); // 2 × 2
    }

    // #10 Momentum: a card whose damage scales with the cards already played this turn.
    [Fact]
    public void Momentum_DamageScalesWithCardsPlayedThisTurn()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(GoblinId).Health.SetMax(100);
        combat.GetCombatant(GoblinId).Health.SetCurrent(100);

        var cardId = Card(builder, "challenge.momentum", new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new AddExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(2),
                    new MultiplyExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(2),
                        new CardsPlayedThisTurnExpression<CardPlayContext>(CombatantTargetSelectors.Source))))));
        var registry = builder.Build();

        Play(combat, registry, cardId, GoblinId);
        Play(combat, registry, cardId, GoblinId);
        Play(combat, registry, cardId, GoblinId);

        // The cards-played counter excludes the in-flight card during its own program, so the three
        // plays read counts 0, 1, 2 → damage 2, 4, 6 = 12 total. This is exactly the probe's intended
        // "+2 more than the previous attack" ramp. 100 − 12 = 88.
        Assert.Equal(88, combat.GetCombatant(GoblinId).Health.Current);
    }
}
