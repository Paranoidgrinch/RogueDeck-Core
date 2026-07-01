using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests for combat node + program serialization (C-S2, leaf nodes). Round-trips a leaf effect node and a whole
// single-node EffectProgram for CardPlayContext, structurally and by idempotence. Composite nodes and non-null
// result keys are later slices.
public class CombatJsonNodeTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);

    [Fact]
    public void A_leaf_node_round_trips()
    {
        IEffectNode<CardPlayContext> node = new DealDamageNode<CardPlayContext>(
            new EventTargetCombatantTargetSelector(), Const(6));

        var json1 = CombatJson.ToJson(node, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        var deal = Assert.IsType<DealDamageNode<CardPlayContext>>(back);
        Assert.IsType<EventTargetCombatantTargetSelector>(deal.TargetSelector);
        Assert.Equal(6, Assert.IsType<ConstantExpression<CardPlayContext>>(deal.Amount).Value);
    }

    [Fact]
    public void Heal_and_gain_block_nodes_round_trip()
    {
        IEffectNode<CardPlayContext> heal = new HealNode<CardPlayContext>(
            new SourceCombatantTargetSelector(), Const(4));
        IEffectNode<CardPlayContext> block = new GainBlockNode<CardPlayContext>(
            new SourceCombatantTargetSelector(), new AddExpression<CardPlayContext>(Const(2), Const(3)));

        foreach (var node in new[] { heal, block })
        {
            var json = CombatJson.ToJson(node, Options);
            Assert.Equal(json, CombatJson.ToJson(CombatJson.FromJson<IEffectNode<CardPlayContext>>(json, Options), Options));
        }
    }

    [Fact]
    public void A_single_node_card_program_round_trips()
    {
        // The demo "smite" card: deal 6 to the event target.
        var program = new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(new EventTargetCombatantTargetSelector(), Const(6)));

        var json1 = CombatJson.ToJson(program, Options);
        var back = CombatJson.FromJson<EffectProgram<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        var deal = Assert.IsType<DealDamageNode<CardPlayContext>>(back.Root);
        Assert.Equal(6, Assert.IsType<ConstantExpression<CardPlayContext>>(deal.Amount).Value);
    }
}
