using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Persistent per-fight combatant counters: named integers on a combatant that survive across plays within a
// fight (and across save/restore), written by SetCombatantCounterNode and read by CombatantCounterExpression.
// Turns the previously dead CombatantState._counters store into a usable stat (combo tallies, ritual stacks, …).
public class CombatantCounterTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CounterId Combo = new("combo");

    private sealed record Ctx;

    [Fact]
    public void Relative_writes_accumulate_and_absolute_writes_overwrite()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        Run(combat, registry, new SetCombatantCounterNode<Ctx>(CombatantTargetSelectors.Source, Combo, Const(1)));
        Run(combat, registry, new SetCombatantCounterNode<Ctx>(CombatantTargetSelectors.Source, Combo, Const(1)));
        Assert.Equal(2, combat.GetCombatant(HeroId).GetCounter(Combo));

        // Absolute set overwrites the accumulated value.
        Run(combat, registry, new SetCombatantCounterNode<Ctx>(CombatantTargetSelectors.Source, Combo, Const(5), relative: false));
        Assert.Equal(5, combat.GetCombatant(HeroId).GetCounter(Combo));

        // Negative relative deltas subtract.
        Run(combat, registry, new SetCombatantCounterNode<Ctx>(CombatantTargetSelectors.Source, Combo, Const(-2)));
        Assert.Equal(3, combat.GetCombatant(HeroId).GetCounter(Combo));
    }

    [Fact]
    public void A_combo_counter_grows_across_plays_and_scales_damage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var startHp = combat.GetCombatant(GoblinId).Health.Current;

        // Each "play" bumps the combo, then strikes for the current combo value.
        PlayComboStrike(combat, registry); // combo 1 → 1 damage
        Assert.Equal(startHp - 1, combat.GetCombatant(GoblinId).Health.Current);

        PlayComboStrike(combat, registry); // combo 2 → 2 damage (the counter persisted between plays)
        Assert.Equal(startHp - 1 - 2, combat.GetCombatant(GoblinId).Health.Current);

        PlayComboStrike(combat, registry); // combo 3 → 3 damage
        Assert.Equal(startHp - 1 - 2 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void Counters_survive_save_and_restore_with_an_identical_fingerprint()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        Run(combat, registry, new SetCombatantCounterNode<Ctx>(CombatantTargetSelectors.Source, Combo, Const(4)));

        var snapshot = combat.CreateSnapshot();
        var restored = CombatState.Restore(snapshot);

        Assert.Equal(4, restored.GetCombatant(HeroId).GetCounter(Combo));
        Assert.Equal(
            CombatStateHasher.ComputeHash(snapshot),
            CombatStateHasher.ComputeHash(restored.CreateSnapshot()));
    }

    [Fact]
    public void Standard_package_registers_the_counter_handler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        Assert.IsType<SetCombatantCounterEffectHandler>(
            registry.GetEffectRequestHandler(typeof(SetCombatantCounterEffectRequest)));
    }

    private void PlayComboStrike(CombatState combat, CombatDefinitionRegistry registry)
    {
        Run(combat, registry, new SetCombatantCounterNode<Ctx>(CombatantTargetSelectors.Source, Combo, Const(1)));
        Run(combat, registry, new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget, new CombatantCounterExpression<Ctx>(CombatantTargetSelectors.Source, Combo)));
    }

    private static void Run(CombatState combat, CombatDefinitionRegistry registry, IEffectNode<Ctx> node)
    {
        EffectProgramExecutor.Execute(new EffectProgram<Ctx>(node), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static ConstantExpression<Ctx> Const(int value) => new(value);

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
}
