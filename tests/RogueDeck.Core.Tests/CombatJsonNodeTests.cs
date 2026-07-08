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
    public void A_node_with_a_result_key_round_trips()
    {
        IEffectNode<CardPlayContext> node = new DealDamageNode<CardPlayContext>(
            new EventTargetCombatantTargetSelector(), Const(6),
            resultKey: new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg"));

        var json1 = CombatJson.ToJson(node, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        var deal = Assert.IsType<DealDamageNode<CardPlayContext>>(back);
        Assert.Equal("dmg", deal.ResultKey!.Name);
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

    // ── P2: positional movement nodes ─────────────────────────────────────────

    [Fact]
    public void MoveCombatant_absolute_node_round_trips_its_mode_and_coordinates()
    {
        IEffectNode<CardPlayContext> node = new MoveCombatantNode<CardPlayContext>(
            new SourceCombatantTargetSelector(), MovementMode.ToAbsolute, x: Const(3), y: Const(5));

        var json1 = CombatJson.ToJson(node, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        var move = Assert.IsType<MoveCombatantNode<CardPlayContext>>(back);
        Assert.Equal(MovementMode.ToAbsolute, move.Mode);
        Assert.Equal(3, Assert.IsType<ConstantExpression<CardPlayContext>>(move.X).Value);
        Assert.Equal(5, Assert.IsType<ConstantExpression<CardPlayContext>>(move.Y).Value);
        Assert.Null(move.Step);
    }

    [Fact]
    public void MoveCombatant_relative_nodes_round_trip_their_step()
    {
        foreach (var mode in new[]
        {
            MovementMode.TowardEnemies, MovementMode.AwayFromEnemies,
            MovementMode.PushFromSource, MovementMode.PullToSource,
        })
        {
            IEffectNode<CardPlayContext> node = new MoveCombatantNode<CardPlayContext>(
                new AllEnemiesOfSourceCombatantTargetSelector(), mode, step: Const(2));

            var json = CombatJson.ToJson(node, Options);
            var backNode = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json, Options);

            Assert.Equal(json, CombatJson.ToJson(backNode, Options));
            var back = Assert.IsType<MoveCombatantNode<CardPlayContext>>(backNode);
            Assert.Equal(mode, back.Mode);
            Assert.Equal(2, Assert.IsType<ConstantExpression<CardPlayContext>>(back.Step).Value);
        }
    }

    [Fact]
    public void SwapPositions_node_round_trips_both_selectors()
    {
        IEffectNode<CardPlayContext> node = new SwapPositionsNode<CardPlayContext>(
            new SourceCombatantTargetSelector(), new AllEnemiesOfSourceCombatantTargetSelector());

        var json1 = CombatJson.ToJson(node, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        var swap = Assert.IsType<SwapPositionsNode<CardPlayContext>>(back);
        Assert.IsType<SourceCombatantTargetSelector>(swap.FirstSelector);
        Assert.IsType<AllEnemiesOfSourceCombatantTargetSelector>(swap.SecondSelector);
    }

    [Fact]
    public void SummonCombatant_node_round_trips_an_optional_position()
    {
        IEffectNode<CardPlayContext> node = new SummonCombatantNode<CardPlayContext>(
            StandardCombatIds.EnemyTeam, Const(10),
            new CombatantDefinitionId("standard.goblin"), "combatant.goblin",
            position: new CombatPosition(4, 2));

        var json1 = CombatJson.ToJson(node, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        Assert.Equal(new CombatPosition(4, 2),
            Assert.IsType<SummonCombatantNode<CardPlayContext>>(back).Position);
    }

    [Fact]
    public void SummonCombatant_node_round_trips_its_starting_statuses()
    {
        IEffectNode<CardPlayContext> node = new SummonCombatantNode<CardPlayContext>(
            StandardCombatIds.PlayerTeam, Const(30),
            new CombatantDefinitionId("board.creature"), "combatant.creature",
            startingStatuses: [new StatusGrant(new StatusDefinitionId("board.creature"), Stacks: 1, DurationTurns: 2)]);

        var json1 = CombatJson.ToJson(node, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json1, Options);

        Assert.Equal(json1, CombatJson.ToJson(back, Options));
        var summon = Assert.IsType<SummonCombatantNode<CardPlayContext>>(back);
        var grant = Assert.Single(summon.StartingStatuses);
        Assert.Equal("board.creature", grant.StatusDefinitionId.value);
        Assert.Equal(1, grant.Stacks);
        Assert.Equal(2, grant.DurationTurns);
    }
}
