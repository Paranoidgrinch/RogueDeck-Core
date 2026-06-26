using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate: block-ignoring ("true") damage (battery probes #20 Brittle Curse,
// #42 Frostbite). DealDamage gained an IgnoresBlock flag: when set, the target's Block pool is bypassed
// entirely (untouched) and the full post-modifier amount hits HP. The damage-amount modifier pipeline
// and the DamageDealt/Received events + zero-HP downing still apply exactly as for ordinary damage.
public class TrueDamageTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static CombatDefinitionRegistry StandardRegistry() =>
        CombatTestFactory.CreateStandardBuilder().Build();

    private static void RunRequest(CombatState combat, CombatDefinitionRegistry registry, IEffectRequest request)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    // ── Handler-level semantics (direct request) ────────────────────────────────

    [Fact]
    public void TrueDamage_BypassesBlock_FullAmountHitsHealth()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId); // 12/12
        goblin.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        RunRequest(combat, StandardRegistry(),
            new DealDamageEffectRequest(GoblinId, Amount: 4, IgnoresBlock: true));

        // Block untouched, all 4 damage to HP: 12 → 8.
        Assert.Equal(5, goblin.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
        Assert.Equal(8, goblin.Health.Current);
    }

    [Fact]
    public void OrdinaryDamage_StillAbsorbedByBlock_RegressionGuard()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId);
        goblin.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        // Default IgnoresBlock = false.
        RunRequest(combat, StandardRegistry(),
            new DealDamageEffectRequest(GoblinId, Amount: 4));

        // 4 absorbed by block: block 5 → 1, HP unchanged.
        Assert.Equal(1, goblin.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
        Assert.Equal(12, goblin.Health.Current);
    }

    [Fact]
    public void TrueDamage_TraceRecordsBypass()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var collector = new CombatTraceCollector();
        combat.TraceListener = collector;
        var goblin = combat.GetCombatant(GoblinId);
        goblin.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        RunRequest(combat, StandardRegistry(),
            new DealDamageEffectRequest(GoblinId, Amount: 3, IgnoresBlock: true));

        var trace = Assert.Single(collector.Events.OfType<DamageResolvedTraceEvent>());
        Assert.True(trace.IgnoresBlock);
        Assert.Equal(0, trace.BlockedAmount);
        Assert.Equal(3, trace.HealthLost);
    }

    // ── Node / executor through a real card program ─────────────────────────────

    // Frostbite-style: deal true damage equal to the target's current block — block not removed.
    [Fact]
    public void Node_TrueDamageEqualToTargetBlock_BlockNotRemoved()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var goblin = combat.GetCombatant(GoblinId); // 12/12
        goblin.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(5));

        var cardId = new CardDefinitionId("challenge.frostbite");
        var card = new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new CombatantDefensivePoolExpression<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, StandardCombatIds.BlockDefensivePool),
                    ignoresBlock: true)),
        };
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // True damage of 5 (= block) hits HP; block stays at 5: 12 → 7.
        Assert.Equal(5, goblin.DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
        Assert.Equal(7, goblin.Health.Current);
    }
}
