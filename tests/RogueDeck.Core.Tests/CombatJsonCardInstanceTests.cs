using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Coverage for the card-instance expressions + nodes and the remaining pure-data operation nodes (summon,
// create-card, team/lifecycle/result, card move/copy/replay/play).
public class CombatJsonCardInstanceTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);

    private static ICombatantTargetSelector Source => new SourceCombatantTargetSelector();

    private static void RoundTrips(IEffectNode<CardPlayContext> node)
    {
        var json = CombatJson.ToJson(node, Options);
        Assert.Equal(json, CombatJson.ToJson(CombatJson.FromJson<IEffectNode<CardPlayContext>>(json, Options), Options));
    }

    [Fact]
    public void Card_instance_expressions_round_trip()
    {
        ICardInstanceExpression<CardPlayContext> explicitCard =
            new ExplicitCardInstanceExpression<CardPlayContext>(new CardInstanceId("c-1"));
        var back = CombatJson.FromJson<ICardInstanceExpression<CardPlayContext>>(
            CombatJson.ToJson(explicitCard, Options), Options);
        Assert.Equal(new CardInstanceId("c-1"), Assert.IsType<ExplicitCardInstanceExpression<CardPlayContext>>(back).Id);

        foreach (ICardInstanceExpression<CardPlayContext> expr in new ICardInstanceExpression<CardPlayContext>[]
        {
            new PlayedCardInstanceExpression<CardPlayContext>(),
            new TriggerEventCardInstanceExpression<CardPlayContext>(),
            new CreateCardOutcomeExpression<CardPlayContext>(
                new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("created")),
        })
        {
            var json = CombatJson.ToJson(expr, Options);
            Assert.Equal(json, CombatJson.ToJson(CombatJson.FromJson<ICardInstanceExpression<CardPlayContext>>(json, Options), Options));
        }
    }

    [Fact]
    public void Card_instance_nodes_round_trip()
    {
        ICardInstanceExpression<CardPlayContext> card =
            new ExplicitCardInstanceExpression<CardPlayContext>(new CardInstanceId("c-1"));
        RoundTrips(new MoveCardToZoneNode<CardPlayContext>(Source, card, CardZone.DiscardPile));
        RoundTrips(new CreateCardCopyNode<CardPlayContext>(Source, card, CardZone.Hand));
        RoundTrips(new ReplayCardProgramNode<CardPlayContext>(card, Source));
        RoundTrips(new PlayCardNode<CardPlayContext>(Source, card));
    }

    [Fact]
    public void Remaining_data_expressions_round_trip()
    {
        void Int(ICombatExpression<CardPlayContext, int> e) =>
            Assert.Equal(CombatJson.ToJson(e, Options),
                CombatJson.ToJson(CombatJson.FromJson<ICombatExpression<CardPlayContext, int>>(CombatJson.ToJson(e, Options), Options), Options));

        Int(new CardsPlayedThisTurnExpression<CardPlayContext>(Source));
        Int(new DamageDealtThisTurnExpression<CardPlayContext>(Source));
        Int(new ResourceGainedThisTurnExpression<CardPlayContext>(Source));
        Int(new CardCostExpression<CardPlayContext>(
            new ExplicitCardInstanceExpression<CardPlayContext>(new CardInstanceId("c-1")), new ResourceId("energy")));
        Int(new IterationTargetStatusStacksExpression<CardPlayContext>(new StatusDefinitionId("burn")));

        var boolExpr = new IterationTargetHasStatusExpression<CardPlayContext>(new StatusDefinitionId("burn"));
        Assert.Equal(CombatJson.ToJson<ICombatExpression<CardPlayContext, bool>>(boolExpr, Options),
            CombatJson.ToJson(CombatJson.FromJson<ICombatExpression<CardPlayContext, bool>>(
                CombatJson.ToJson<ICombatExpression<CardPlayContext, bool>>(boolExpr, Options), Options), Options));
    }

    [Fact]
    public void Func_backed_result_consumer_expressions_remain_escapes()
    {
        // These read a previous outcome via a Func and cannot be expressed as data.
        ICombatExpression<CardPlayContext, int> escape = new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(
            new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg"), _ => 0);
        Assert.Throws<NotSupportedException>(() => CombatJson.ToJson(escape, Options));
    }

    [Fact]
    public void Remaining_operation_nodes_round_trip()
    {
        RoundTrips(new CreateCardInstanceNode<CardPlayContext>(Source, new CardDefinitionId("wound"), CardZone.DiscardPile));
        RoundTrips(new SummonCombatantNode<CardPlayContext>(
            new TeamId("enemies"), Const(20), new CombatantDefinitionId("goblin"), "enemy.goblin.name"));
        RoundTrips(new SetCombatantLifecycleStateNode<CardPlayContext>(Source, CombatantLifecycleState.Downed));
        RoundTrips(new ChangeCombatantTeamNode<CardPlayContext>(Source, new TeamId("allies")));
        RoundTrips(new SetCombatResultNode<CardPlayContext>(CombatResult.Victory));
        RoundTrips(new RemoveTemporaryRuleNode<CardPlayContext>(new TriggeredEffectDefinitionId("rule-1")));
    }
}
