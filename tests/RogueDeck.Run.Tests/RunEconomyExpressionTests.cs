using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Three small questions the run layer could not answer, each of which a shop relic turns on: which resource a
// change was about, whether the player is standing in a shop, and how to move a counter by a number worked out
// from the run.
public class RunEconomyExpressionTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunResourceId Voucher = new("voucher");
    private static readonly RunCounterId Debt = new("debt");
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    // A rule that skims a share of every Gold gain must not fire when a Voucher arrives — and every other field
    // of a resource change is readable except which resource it was.
    [Fact]
    public void A_change_says_which_resource_it_was_about()
    {
        var run = NewRun();
        var gold = new RunEvalContext(run, new ResourceChangedRunEvent(Gold, 0, 10, 10));
        var voucher = new RunEvalContext(run, new ResourceChangedRunEvent(Voucher, 0, 1, 1));

        Assert.True(RunEventValues.ResourceIs(Gold).Evaluate(gold));
        Assert.False(RunEventValues.ResourceIs(Gold).Evaluate(voucher));
    }

    [Fact]
    public void Asking_which_resource_outside_a_resource_change_says_so()
    {
        var run = NewRun();
        var context = new RunEvalContext(run, new RunStartedRunEvent(run.Id));

        var error = Assert.Throws<InvalidOperationException>(
            () => RunEventValues.ResourceIs(Gold).Evaluate(context));

        Assert.Contains("RunStartedRunEvent", error.Message, StringComparison.Ordinal);
    }

    // A purchase and a mugging are both just Gold leaving; only where it happened tells them apart.
    [Fact]
    public void The_run_knows_whether_the_player_is_in_a_shop()
    {
        var run = NewRun();
        Assert.False(RunExpr.InShop.Evaluate(run));

        run.BeginShopVisit(new ShopShelf(run, new ShopDefinition([], OfferCount: 0)));
        Assert.True(RunExpr.InShop.Evaluate(run));

        run.EndShopVisit();
        Assert.False(RunExpr.InShop.Evaluate(run));
    }

    // "Pay down as much of the debt as this gain covers" — a size the flat increment cannot express.
    [Fact]
    public void A_counter_can_move_by_an_amount_worked_out_from_the_run()
    {
        var run = NewRun();
        run.SetCounter(Debt, 50);
        run.SetResource(Gold, 30);

        Resolve(run, new ComputedCounterRunEffect(
            Debt, RunExpr.Negate(RunExpr.Min(RunExpr.Resource(Gold), RunExpr.Counter(Debt)))));

        Assert.Equal(20, run.GetCounter(Debt));
    }

    [Fact]
    public void A_computed_counter_move_of_nothing_changes_nothing()
    {
        var run = NewRun();
        run.SetCounter(Debt, 7);

        Resolve(run, new ComputedCounterRunEffect(Debt, RunExpr.Const(0)));

        Assert.Equal(7, run.GetCounter(Debt));
        Assert.Empty(run.EventHistory.OfType<RunCounterChangedRunEvent>());
    }

    [Fact]
    public void All_three_round_trip_as_data()
    {
        var resourceIs = RunJson.FromJson<IRunExpression<bool>>(
            RunJson.ToJson(RunEventValues.ResourceIs(Gold), Options), Options);
        var inShop = RunJson.FromJson<IRunExpression<bool>>(
            RunJson.ToJson(RunExpr.InShop, Options), Options);
        var counter = RunJson.FromJson<IRunEffectRequest>(
            RunJson.ToJson<IRunEffectRequest>(
                new ComputedCounterRunEffect(Debt, RunExpr.Const(3)), Options), Options);

        Assert.Equal(Gold, Assert.IsType<EventResourceIsExpression>(resourceIs).Resource);
        Assert.IsType<InShopExpression>(inShop);
        Assert.Equal(Debt, Assert.IsType<ComputedCounterRunEffect>(counter).Counter);
    }

    private static RunState NewRun() =>
        new(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));

    private static void Resolve(RunState run, IRunEffectRequest effect)
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        run.EnqueueEffect(effect);
        new RunEffectProcessor().ResolvePending(run, builder.Build());
    }
}
