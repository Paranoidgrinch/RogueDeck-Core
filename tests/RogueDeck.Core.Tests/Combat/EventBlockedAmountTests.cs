using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// A damage trigger reads the part of the hit Block absorbed, beside the HP part EventAmount reads.
public class EventBlockedAmountTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CounterId Seen = new("test.blocked_seen");

    [Fact]
    public void A_damage_trigger_reads_what_block_absorbed()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.DamageReceived.Define(
            id: new TriggeredEffectDefinitionId("test.read_blocked"),
            program: new EffectProgram<DamageReceivedTriggeredEffectContext>(
                new SetCombatantCounterNode<DamageReceivedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget, Seen,
                    new EventBlockedAmountExpression<DamageReceivedTriggeredEffectContext>(), relative: false))));
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new GainBlockEffectRequest(GoblinId, 4));
        combat.EnqueueEffect(new DealDamageEffectRequest(TargetCombatantId: GoblinId, Amount: 10, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(4, combat.GetCombatant(GoblinId).GetCounter(Seen));
    }

    [Fact]
    public void It_round_trips_as_data()
    {
        var options = CombatJson.CreateOptions<DamageReceivedTriggeredEffectContext>();
        var program = new EffectProgram<DamageReceivedTriggeredEffectContext>(
            new SetCombatantCounterNode<DamageReceivedTriggeredEffectContext>(
                CombatantTargetSelectors.EventTarget, Seen,
                new EventBlockedAmountExpression<DamageReceivedTriggeredEffectContext>(), relative: false));
        var json = JsonSerializer.Serialize(program, options);
        Assert.Contains("eventBlockedAmount", json, StringComparison.Ordinal);
        var back = JsonSerializer.Deserialize<EffectProgram<DamageReceivedTriggeredEffectContext>>(json, options);
        Assert.NotNull(back);
    }
}
