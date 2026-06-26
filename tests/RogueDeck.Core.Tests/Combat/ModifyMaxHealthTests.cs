using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate: the ModifyMaxHealth write-primitive (battery probes #1 Frailty,
// #8 Berserk, #15 Entropy). Max HP is a signed-delta write: lowering max below current HP clamps
// current down; raising max never auto-heals; max is floored at 1. The node exposes an outcome so a
// causal follow-up can read how much max HP actually changed.
public class ModifyMaxHealthTests
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

    // ── Handler-level semantics (direct request, no card machinery) ─────────────

    [Fact]
    public void LoweringMaxBelowCurrent_ClampsCurrentDown()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        // hero at full 20/20

        RunRequest(combat, StandardRegistry(),
            new ModifyMaxHealthEffectRequest(HeroId, Delta: -5));

        Assert.Equal(15, hero.Health.Max);
        Assert.Equal(15, hero.Health.Current);
    }

    [Fact]
    public void LoweringMax_AboveCurrent_LeavesCurrentUnchanged()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(8); // 8/20

        RunRequest(combat, StandardRegistry(),
            new ModifyMaxHealthEffectRequest(HeroId, Delta: -5));

        Assert.Equal(15, hero.Health.Max);
        Assert.Equal(8, hero.Health.Current);
    }

    [Fact]
    public void RaisingMax_DoesNotAutoHeal()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(10); // 10/20

        RunRequest(combat, StandardRegistry(),
            new ModifyMaxHealthEffectRequest(HeroId, Delta: +5));

        Assert.Equal(25, hero.Health.Max);
        Assert.Equal(10, hero.Health.Current);
    }

    [Fact]
    public void HugeReduction_FloorsMaxAtOne()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        RunRequest(combat, StandardRegistry(),
            new ModifyMaxHealthEffectRequest(HeroId, Delta: -1000));

        Assert.Equal(1, hero.Health.Max);
        Assert.Equal(1, hero.Health.Current);
    }

    [Fact]
    public void Resolution_WritesOutcomeSlotAndLog()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetCurrent(10);
        var slot = new ModifyMaxHealthOutcomeSlot();

        RunRequest(combat, StandardRegistry(),
            new ModifyMaxHealthEffectRequest(HeroId, Delta: -6, OutcomeSlot: slot));

        Assert.True(slot.IsCompleted);
        var outcome = slot.Value!;
        Assert.Equal(-6, outcome.RequestedDelta);
        Assert.Equal(-6, outcome.AppliedDelta);
        Assert.Equal(20, outcome.PreviousMax);
        Assert.Equal(14, outcome.NewMax);
        Assert.Equal(10, outcome.PreviousCurrent);
        Assert.Equal(10, outcome.NewCurrent);

        Assert.Contains(combat.CombatLog,
            e => e.Type == StandardCombatLogTypes.MaxHealthChanged);
    }

    // ── Node / executor / outcome through a real card program ───────────────────

    // Frailty-style: reduce the target's max HP, then deal damage equal to the reduction —
    // proves the node's outcome slot feeds a causal follow-up expression.
    [Fact]
    public void Node_ReduceMaxHp_ThenDamageByReduction()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var maxKey = new EffectResultKey<OrderedTargetOutcomes<ModifyMaxHealthOutcome>>("maxhp");

        var cardId = new CardDefinitionId("challenge.frailty_strike");
        var card = new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new ModifyMaxHealthNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(-4),
                        resultKey: maxKey),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new AbsExpression<CardPlayContext>(
                            new PreviousOutcomeFieldExpression<CardPlayContext, ModifyMaxHealthOutcome>(
                                maxKey, o => o.AppliedDelta))),
                ])),
        };
        builder.RegisterCard(card);

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var goblin = combat.GetCombatant(GoblinId);
        var goblinMaxBefore = goblin.Health.Max; // 12
        // Headroom below the post-reduction max (8) so the max change itself never clamps current —
        // isolating the 4 follow-up damage from the max-reduction clamp.
        goblin.Health.SetCurrent(6);

        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.Equal(goblinMaxBefore - 4, goblin.Health.Max); // 8
        // 4 damage dealt equal to the max-HP reduction: 6 → 2.
        Assert.Equal(2, goblin.Health.Current);
    }
}
