using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the deferred slices: relic disable/re-enable over N combats, and the consumable inventory.
public class RelicDisableAndConsumableTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly ConsumableId FirePotion = new("potion.fire");

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int current = 30, int max = 40)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(current, max), map);
    }

    private static CombatResolvedRunEvent Fight() =>
        new(new NodeId("fight"), CombatResult.Victory, 30, 0);

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    // ── Relic disable ──────────────────────────────────────────────────────────────

    [Fact]
    public void Disabled_relic_does_not_react_then_re_enables_after_n_combats()
    {
        var registry = BuildRegistry();
        var run = NewRun(current: 20, max: 40);
        run.AddRelic(new RelicInstance(StandardRelics.Bloodstone(5))); // heals 5 on victory

        // Disable for the next 2 combats.
        run.EnqueueEffect(new DisableRelicRunEffect(new RelicId("bloodstone"), 2));
        Drain(run, registry);
        Assert.False(run.FindRelic(new RelicId("bloodstone"))!.Enabled);

        // Combat 1 + 2: disabled, no heal (and each combat ticks the re-enable countdown).
        run.RaiseEvent(Fight());
        Drain(run, registry);
        run.RaiseEvent(Fight());
        Drain(run, registry);
        Assert.Equal(20, run.Health.Current); // never healed while disabled
        Assert.True(run.FindRelic(new RelicId("bloodstone"))!.Enabled); // re-enabled after 2 combats

        // Combat 3: active again, heals.
        run.RaiseEvent(Fight());
        Drain(run, registry);
        Assert.Equal(25, run.Health.Current);
    }

    [Fact]
    public void Disabling_an_absent_relic_is_a_no_op()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.EnqueueEffect(new DisableRelicRunEffect(new RelicId("ghost"), 2));
        Drain(run, registry);
        Assert.Empty(run.EventHistory.OfType<RelicDisabledRunEvent>());
    }

    // ── Consumables ────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_then_use_a_consumable_applies_its_effects_and_removes_it()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        run.EnqueueEffect(new AddConsumableRunEffect(
            FirePotion, new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) }));
        Drain(run, registry);
        Assert.Single(run.Consumables);
        Assert.Equal(1, RunExpr.ConsumableCount.Evaluate(run));

        var instance = run.Consumables[0].Id;
        run.EnqueueEffect(new UseConsumableRunEffect(instance));
        Drain(run, registry);

        Assert.Equal(50, run.GetResource(Gold));
        Assert.Empty(run.Consumables); // consumed
        Assert.Single(run.EventHistory.OfType<ConsumableUsedRunEvent>());
    }

    [Fact]
    public void Add_a_consumable_by_id_resolves_its_use_effects_from_content()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetContent(new RunContentRegistryBuilder()
            .RegisterConsumable(new ConsumableDefinition(
                FirePotion, "Fire Potion", new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 50) }))
            .Build());

        run.EnqueueEffect(new AddConsumableByIdRunEffect(FirePotion));
        Drain(run, registry);

        var held = Assert.Single(run.Consumables);
        Assert.Equal(FirePotion, held.DefinitionId);

        // Using it applies the definition's effects (resolved from content, not carried inline).
        run.EnqueueEffect(new UseConsumableRunEffect(held.Id));
        Drain(run, registry);
        Assert.Equal(50, run.GetResource(Gold));
        Assert.Empty(run.Consumables);
    }

    [Fact]
    public void Using_an_absent_consumable_is_a_no_op()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.EnqueueEffect(new UseConsumableRunEffect(new ConsumableInstanceId("nope")));
        Drain(run, registry);
        Assert.Empty(run.EventHistory.OfType<ConsumableUsedRunEvent>());
    }

    [Fact]
    public void Consumable_use_effects_can_read_run_state()
    {
        var registry = BuildRegistry();
        var run = NewRun(current: 25, max: 40); // missing 15

        // A potion that heals half the missing health when used.
        run.EnqueueEffect(new AddConsumableRunEffect(
            FirePotion,
            new IRunEffectRequest[]
            {
                new ComputedHealRunEffect(RunExpr.Divide(RunExpr.MissingHealth, RunExpr.Const(2))),
            }));
        Drain(run, registry);

        run.EnqueueEffect(new UseConsumableRunEffect(run.Consumables[0].Id));
        Drain(run, registry);

        Assert.Equal(32, run.Health.Current); // 25 + (15/2 = 7)
    }
}
