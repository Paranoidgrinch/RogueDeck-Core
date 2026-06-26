using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 substrate verification: the long tail of ✅ battery probes that should compose from existing
// read/arithmetic/conditional primitives with NO engine change. Each test pins one probe as composable.
// Batch 1 — self-contained reads (no triggers): #4 Soul Siphon, #44 Bankrupt, #13 Sanctuary, #38 Execute.
public class BatteryReadCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void PlayProgram(CombatState combat, CombatDefinitionRegistryBuilder builder,
        EffectProgram<CardPlayContext> program, CombatantId? target)
    {
        var cardId = new CardDefinitionId("challenge.read_card");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            "card.read.name", "card.read.desc")
        {
            Program = program,
        });
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        if (!hero.Resources.ContainsKey(StandardCombatIds.EnergyResource))
            hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, target));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // #4 Soul Siphon: deal damage equal to twice the target's missing-health percentage.
    [Fact]
    public void SoulSiphon_DamageScalesWithMissingHealthPercent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.Health.SetMax(1000);
        goblin.Health.SetCurrent(400); // 40 % HP → 60 % missing → ×2 = 120 damage

        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new MultiplyExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(2),
                    new SubtractExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(100),
                        new CombatantHealthPercentageExpression<CardPlayContext>(
                            CombatantTargetSelectors.EventTarget))))),
            target: GoblinId);

        Assert.Equal(280, goblin.Health.Current); // 400 − 120
    }

    // #44 Bankrupt: set your energy to 0; deal 4 damage per energy lost this way.
    [Fact]
    public void Bankrupt_DamageScalesWithEnergyZeroed()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var lostKey = new EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>("lost");

        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                // Drain all energy (large negative, clamped at 0) → AppliedDelta = −3.
                new ModifyResourceNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource,
                    new ConstantExpression<CardPlayContext>(-999), min: 0, resultKey: lostKey),
                // Self-damage 4 per energy lost.
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new MultiplyExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(4),
                        new AbsExpression<CardPlayContext>(
                            new PreviousOutcomeFieldExpression<CardPlayContext, ModifyResourceOutcome>(
                                lostKey, o => o.AppliedDelta)))),
            ])),
            target: GoblinId);

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(8, hero.Health.Current); // 20 − (4 × 3)
    }

    // #13 Sanctuary: gain block; the part that exceeds max HP becomes healing instead.
    [Fact]
    public void Sanctuary_ExcessBlockConvertsToHealing()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(10); // 10 / 20
        hero.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(0));

        var blockPool = new CombatantDefensivePoolExpression<CardPlayContext>(
            CombatantTargetSelectors.Source, StandardCombatIds.BlockDefensivePool);
        var maxHp = new CombatantMaxHealthExpression<CardPlayContext>(CombatantTargetSelectors.Source);

        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(30)),
                new ConditionalEffectNode<CardPlayContext>(
                    new ComparisonExpression<CardPlayContext>(blockPool, ComparisonOperator.Greater, maxHp),
                    new CausalSequenceEffectNode<CardPlayContext>([
                        // Heal the excess (block − maxHP), then trim the block pool down to maxHP.
                        new HealNode<CardPlayContext>(
                            CombatantTargetSelectors.Source,
                            new SubtractExpression<CardPlayContext>(blockPool, maxHp)),
                        new ModifyDefensivePoolNode<CardPlayContext>(
                            CombatantTargetSelectors.Source, StandardCombatIds.BlockDefensivePool,
                            new SubtractExpression<CardPlayContext>(maxHp, blockPool)),
                    ])),
            ])),
            target: GoblinId);

        // Block 30 → excess 10 healed (10 → 20 HP) and block trimmed to 20.
        Assert.Equal(20, hero.Health.Current);
        Assert.Equal(20, hero.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    // #38 Execute: deal 8; if that drops the target below 25 % HP, deal 8 more.
    [Fact]
    public void Execute_DealsFollowUpWhenTargetDropsBelowThreshold()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.Health.SetMax(100);
        goblin.Health.SetCurrent(30); // 8 damage → 22 (22 %) < 25 % → +8 → 14

        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(8)),
                new ConditionalEffectNode<CardPlayContext>(
                    new ComparisonExpression<CardPlayContext>(
                        new CombatantHealthPercentageExpression<CardPlayContext>(
                            CombatantTargetSelectors.EventTarget),
                        ComparisonOperator.Less, new ConstantExpression<CardPlayContext>(25)),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(8))),
            ])),
            target: GoblinId);

        Assert.Equal(14, goblin.Health.Current);
    }
}
