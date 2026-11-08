using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// A damage trigger reads what the hit asked for — more than the HP it took, when the target ran out of HP first.
public class EventRequestedAmountTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CounterId Seen = new("test.requested_seen");

    private static EffectProgram<DamageReceivedTriggeredEffectContext> Reader() =>
        new(new SetCombatantCounterNode<DamageReceivedTriggeredEffectContext>(
            CombatantTargetSelectors.EventTarget, Seen,
            new EventRequestedAmountExpression<DamageReceivedTriggeredEffectContext>(), relative: false));

    [Fact]
    public void A_damage_trigger_reads_what_the_hit_asked_for_beyond_the_hp_it_took()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.DamageReceived.Define(
            id: new TriggeredEffectDefinitionId("test.read_requested"), program: Reader()));
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var health = combat.GetCombatant(GoblinId).Health.Current;

        combat.EnqueueEffect(new DealDamageEffectRequest(TargetCombatantId: GoblinId, Amount: health + 7, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(health + 7, combat.GetCombatant(GoblinId).GetCounter(Seen));
    }

    [Fact]
    public void It_round_trips_as_data()
    {
        var options = CombatJson.CreateOptions<DamageReceivedTriggeredEffectContext>();
        var json = JsonSerializer.Serialize(Reader(), options);
        Assert.Contains("eventRequestedAmount", json, StringComparison.Ordinal);
        Assert.NotNull(JsonSerializer.Deserialize<EffectProgram<DamageReceivedTriggeredEffectContext>>(json, options));
    }
}
