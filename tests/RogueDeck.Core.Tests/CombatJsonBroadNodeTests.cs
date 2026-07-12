using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Broader coverage: the health/resource/status/draw operation nodes, the repeat/random control-flow nodes, and
// the remaining combat-state value reads all round-trip (idempotently) for CardPlayContext.
public class CombatJsonBroadNodeTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);

    private static ICombatantTargetSelector Source => new SourceCombatantTargetSelector();
    private static ICombatantTargetSelector Enemy => new EventTargetCombatantTargetSelector();

    private static void RoundTrips(IEffectNode<CardPlayContext> node)
    {
        var json = CombatJson.ToJson(node, Options);
        Assert.Equal(json, CombatJson.ToJson(CombatJson.FromJson<IEffectNode<CardPlayContext>>(json, Options), Options));
    }

    private static void RoundTrips(ICombatExpression<CardPlayContext, int> expr)
    {
        var json = CombatJson.ToJson(expr, Options);
        Assert.Equal(json, CombatJson.ToJson(CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(json, Options), Options));
    }

    [Fact]
    public void Health_and_resource_nodes_round_trip()
    {
        RoundTrips(new ModifyMaxHealthNode<CardPlayContext>(Source, Const(5)));
        RoundTrips(new SetHealthNode<CardPlayContext>(Source, Const(20)));
        RoundTrips(new ModifyDefensivePoolNode<CardPlayContext>(Source, new DefensivePoolId("block"), Const(3)));
        RoundTrips(new LoseResourceNode<CardPlayContext>(Source, new ResourceId("energy"), Const(1)));
        RoundTrips(new RefillResourceNode<CardPlayContext>(Source, new ResourceId("energy"), 3));
        RoundTrips(new ModifyResourceNode<CardPlayContext>(Source, new ResourceId("energy"), Const(2)));
    }

    [Fact]
    public void Status_and_card_nodes_round_trip()
    {
        var burn = new StatusDefinitionId("burn");
        RoundTrips(new RemoveStatusNode<CardPlayContext>(Enemy, burn));
        RoundTrips(new RemoveStatusesByPolarityNode<CardPlayContext>(Source, StatusPolarity.Debuff));
        RoundTrips(new RemoveSelectedStatusNode<CardPlayContext>(Enemy,
            new StatusSelectionSpec(StatusPolarityFilter.Buff, StatusPick.Random)));
        RoundTrips(new RemoveSelectedStatusNode<CardPlayContext>(Enemy,
            new StatusSelectionSpec(StatusPolarityFilter.Debuff, StatusPick.First, Index: 2)));
        RoundTrips(new SetCombatantCounterNode<CardPlayContext>(Source, new CounterId("combo"), Const(1)));
        RoundTrips(new SetCombatantCounterNode<CardPlayContext>(Source, new CounterId("combo"), Const(5), relative: false));
        RoundTrips(new CombatantCounterExpression<CardPlayContext>(Source, new CounterId("combo")));
        RoundTrips(new DealDamageNode<CardPlayContext>(Enemy, Const(8), element: new ElementId("fire")));
        RoundTrips(new ModifySelectedStatusStacksNode<CardPlayContext>(Enemy,
            new StatusSelectionSpec(StatusPolarityFilter.Debuff), Const(-1)));
        RoundTrips(new ModifyStatusStacksNode<CardPlayContext>(Enemy, burn, Const(2)));
        RoundTrips(new ModifyStatusDurationNode<CardPlayContext>(Enemy, burn, Const(1)));
        RoundTrips(new ModifyStatusChargesNode<CardPlayContext>(Enemy, burn, Const(1)));
        RoundTrips(new DrawCardsNode<CardPlayContext>(Source, Const(2)));
        RoundTrips(new MoveAllCardsFromZoneNode<CardPlayContext>(Source, CardZone.Hand, CardZone.DiscardPile));
    }

    [Fact]
    public void Control_flow_nodes_round_trip()
    {
        var body = new DealDamageNode<CardPlayContext>(Enemy, Const(2));
        RoundTrips(new RepeatEffectNode<CardPlayContext>(Const(3), body));
        RoundTrips(new RepeatUntilEffectNode<CardPlayContext>(
            new ComparisonExpression<CardPlayContext>(
                new CombatantCurrentHealthExpression<CardPlayContext>(Enemy), ComparisonOperator.LessOrEqual, Const(0)),
            body));
        RoundTrips(new RandomTargetSelectionNode<CardPlayContext>(new AllEnemiesOfSourceCombatantTargetSelector(), Const(2), body));
    }

    [Fact]
    public void Remaining_combat_state_expressions_round_trip()
    {
        RoundTrips(new CombatantMaxResourceExpression<CardPlayContext>(Source, new ResourceId("energy")));
        RoundTrips(new CombatantMissingResourceExpression<CardPlayContext>(Source, new ResourceId("energy")));
        RoundTrips(new CombatantDefensivePoolExpression<CardPlayContext>(Source, new DefensivePoolId("block")));
        RoundTrips(new CombatantZoneCardCountExpression<CardPlayContext>(Source, CardZone.Hand));
        RoundTrips(new CombatantStatusDurationExpression<CardPlayContext>(Enemy, new StatusDefinitionId("burn")));
        RoundTrips(new CombatantStatusChargesExpression<CardPlayContext>(Enemy, new StatusDefinitionId("burn")));
        RoundTrips(new CombatantStacksByPolarityExpression<CardPlayContext>(Enemy, StatusPolarity.Debuff));
    }
}
