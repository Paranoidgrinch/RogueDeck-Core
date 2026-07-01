using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests that the combat serialization is genuinely context-generic (works for EnemyActionContext with no extra
// registration, since the kinds are open generic definitions closed on the context), and covers ApplyStatus.
public class CombatJsonContextTests
{
    [Fact]
    public void ApplyStatus_node_round_trips_for_card_play()
    {
        var options = CombatJson.CreateOptions<CardPlayContext>();
        IEffectNode<CardPlayContext> node = new ApplyStatusNode<CardPlayContext>(
            new EventTargetCombatantTargetSelector(),
            new StatusDefinitionId("burn"),
            new ConstantExpression<CardPlayContext>(2),
            durationTurns: 3);

        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(
            CombatJson.ToJson(node, options), options);

        var apply = Assert.IsType<ApplyStatusNode<CardPlayContext>>(back);
        Assert.Equal(new StatusDefinitionId("burn"), apply.StatusDefinitionId);
        Assert.Equal(3, apply.DurationTurns);
        Assert.Equal(2, Assert.IsType<ConstantExpression<CardPlayContext>>(apply.Stacks).Value);
    }

    [Fact]
    public void The_same_infra_serializes_an_enemy_action_program_with_no_extra_registration()
    {
        // A different context — proves the open-generic kind registry closes per context.
        var options = CombatJson.CreateOptions<EnemyActionContext>();
        var program = new EffectProgram<EnemyActionContext>(
            new SequenceEffectNode<EnemyActionContext>(new IEffectNode<EnemyActionContext>[]
            {
                new DealDamageNode<EnemyActionContext>(
                    new EventTargetCombatantTargetSelector(), new ConstantExpression<EnemyActionContext>(4)),
                new ApplyStatusNode<EnemyActionContext>(
                    new EventTargetCombatantTargetSelector(),
                    new StatusDefinitionId("weak"),
                    new ConstantExpression<EnemyActionContext>(1)),
            }));

        var json1 = CombatJson.ToJson(program, options);
        var back = CombatJson.FromJson<EffectProgram<EnemyActionContext>>(json1, options);

        Assert.Equal(json1, CombatJson.ToJson(back, options));
        var seq = Assert.IsType<SequenceEffectNode<EnemyActionContext>>(back.Root);
        Assert.Equal(2, seq.Children.Count);
    }
}
