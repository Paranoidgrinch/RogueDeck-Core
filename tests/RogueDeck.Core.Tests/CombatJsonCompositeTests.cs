using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests for composite/control-flow node serialization and selector-reading value expressions (C-S2).
public class CombatJsonCompositeTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);

    private static IEffectNode<CardPlayContext> Deal(int amount) =>
        new DealDamageNode<CardPlayContext>(new EventTargetCombatantTargetSelector(), Const(amount));

    private static void RoundTripsNode(IEffectNode<CardPlayContext> node) =>
        Assert.Equal(
            CombatJson.ToJson(node, Options),
            CombatJson.ToJson(CombatJson.FromJson<IEffectNode<CardPlayContext>>(CombatJson.ToJson(node, Options), Options), Options));

    [Fact]
    public void A_sequence_of_nodes_round_trips()
    {
        IEffectNode<CardPlayContext> sequence = new SequenceEffectNode<CardPlayContext>(
            new[] { Deal(6), Deal(3) });

        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(
            CombatJson.ToJson(sequence, Options), Options);

        var seq = Assert.IsType<SequenceEffectNode<CardPlayContext>>(back);
        Assert.Equal(2, seq.Children.Count);
        RoundTripsNode(sequence);
    }

    [Fact]
    public void A_conditional_node_round_trips()
    {
        IEffectNode<CardPlayContext> conditional = new ConditionalEffectNode<CardPlayContext>(
            new ComparisonExpression<CardPlayContext>(
                new CombatantCurrentHealthExpression<CardPlayContext>(new SourceCombatantTargetSelector()),
                ComparisonOperator.Greater, Const(10)),
            then: Deal(6),
            @else: new HealNode<CardPlayContext>(new SourceCombatantTargetSelector(), Const(4)));

        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(
            CombatJson.ToJson(conditional, Options), Options);

        var cond = Assert.IsType<ConditionalEffectNode<CardPlayContext>>(back);
        Assert.IsType<DealDamageNode<CardPlayContext>>(cond.Then);
        Assert.IsType<HealNode<CardPlayContext>>(cond.Else);
        RoundTripsNode(conditional);
    }

    [Fact]
    public void A_for_each_target_node_round_trips()
    {
        IEffectNode<CardPlayContext> forEach = new ForEachTargetEffectNode<CardPlayContext>(
            new AllEnemiesOfSourceCombatantTargetSelector(), Deal(2));

        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(
            CombatJson.ToJson(forEach, Options), Options);

        var node = Assert.IsType<ForEachTargetEffectNode<CardPlayContext>>(back);
        Assert.IsType<AllEnemiesOfSourceCombatantTargetSelector>(node.CollectionSelector);
        RoundTripsNode(forEach);
    }

    [Fact]
    public void A_multi_node_program_round_trips()
    {
        var program = new EffectProgram<CardPlayContext>(
            new SequenceEffectNode<CardPlayContext>(new[]
            {
                Deal(6),
                new GainBlockNode<CardPlayContext>(new SourceCombatantTargetSelector(), Const(5)),
            }));

        var json1 = CombatJson.ToJson(program, Options);
        Assert.Equal(json1, CombatJson.ToJson(CombatJson.FromJson<EffectProgram<CardPlayContext>>(json1, Options), Options));
    }

    [Fact]
    public void Selector_reading_expressions_round_trip()
    {
        ICombatExpression<CardPlayContext, int> health =
            new CombatantCurrentHealthExpression<CardPlayContext>(new SourceCombatantTargetSelector());
        var healthJson = CombatJson.ToJson(health, Options);
        Assert.Equal(healthJson, CombatJson.ToJson(CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(healthJson, Options), Options));

        ICombatExpression<CardPlayContext, bool> hasStatus =
            new TargetHasStatusExpression<CardPlayContext>(new SourceCombatantTargetSelector(), new StatusDefinitionId("burn"));
        var back = (TargetHasStatusExpression<CardPlayContext>)
            CombatJson.FromJson<ICombatExpression<CardPlayContext, bool>>(CombatJson.ToJson(hasStatus, Options), Options);
        Assert.Equal(new StatusDefinitionId("burn"), back.StatusId);
    }
}
