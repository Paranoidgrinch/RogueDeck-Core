using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// A choice the player may call off once inside it (playtest feedback 2, B4): "improve a card" at a campfire, and
// then not. A Declinable choice whose pick is declined was never taken — nothing paid, the situation offered again.
public class EventDeclineTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static EventScript Camp(bool declinable) => new("camp",
    [
        new EventSituation("camp", "camp",
        [
            new EventChoice("amend",
                [new UpgradeCardsRunEffect(RunSelectors.DeckCards.ChooseByPlayer(1, "improve", allowSkip: true))],
                Costs: [new RunCost(new RunConstantBoolExpression(true), [new ChangeResourceRunEffect(Gold, -5)])],
                Declinable: declinable),
            new EventChoice("leave", []),
        ]),
    ]);

    private sealed class DecliningChooser : IRunEntityChooser
    {
        public int Declined { get; private set; }
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToArray();
        public IReadOnlyList<T> ChooseEntities<T>(
            IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip)
        {
            if (!allowSkip)
                return ChooseEntities(candidates, count, purpose);
            Declined++;
            return Array.Empty<T>();
        }
    }

    private static RunState Run(IRunEntityChooser chooser, bool declinable, params string[] choices)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, 20);
        run.AddDeckCard(new CardDefinitionId("a"));
        run.SetEntityChooser(chooser);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider(choices), Registry(), new RunEffectProcessor());
        new EventNodeResolver().Resolve(context, new Node(new NodeId("e"), StandardRunIds.EventNode, Camp(declinable)));
        return run;
    }

    [Fact]
    public void Calling_off_a_declinable_choice_pays_nothing_and_asks_again()
    {
        var chooser = new DecliningChooser();
        var run = Run(chooser, declinable: true, "amend", "amend", "leave");

        Assert.Equal(2, chooser.Declined);          // offered again after the first decline
        Assert.Equal(20, run.GetResource(Gold));
        Assert.Equal(0, Assert.Single(run.Deck).UpgradeLevel);
    }

    [Fact]
    public void A_declinable_choice_taken_is_paid_and_done()
    {
        var run = Run(new ScriptedChoiceProvider(), declinable: true, "amend");

        Assert.Equal(15, run.GetResource(Gold));
        Assert.Equal(1, Assert.Single(run.Deck).UpgradeLevel);
    }

    // The old shape: a choice that is not Declinable is spent by the decline — paid, and the event over.
    [Fact]
    public void A_choice_that_is_not_declinable_is_spent_by_a_decline()
    {
        var chooser = new DecliningChooser();
        var run = Run(chooser, declinable: false, "amend", "amend", "leave");

        Assert.Equal(1, chooser.Declined);
        Assert.Equal(15, run.GetResource(Gold));
    }

    [Fact]
    public void Declinable_round_trips_and_a_plain_choice_writes_what_it_always_did()
    {
        var options = RunJson.CreateOptions();
        var json = RunJson.ToJson(Camp(declinable: true), options);
        Assert.Contains("\"Declinable\": true", json);
        Assert.True(RunJson.FromJson<EventScript>(json, options).Situations["camp"].Choices[0].Declinable);
        Assert.DoesNotContain("Declinable", RunJson.ToJson(Camp(declinable: false), options));
    }

    // Playtest feedback 2, G2: a gamble leads to one of its outcomes, by weight, from the run's RNG — the seed
    // decides, and over many seeds the weights hold.
    private static EventScript Wheel() => new("wheel",
    [
        new EventSituation("wheel", "wheel",
        [
            new EventChoice("spin", [], Outcomes: [new EventOutcome("win", 1), new EventOutcome("lose", 3)]),
        ]),
        new EventSituation("win", "win", [new EventChoice("take", [new ChangeResourceRunEffect(Gold, 100)])]),
        new EventSituation("lose", "lose", [new EventChoice("take", [new ChangeResourceRunEffect(Gold, -10)])]),
    ]);

    [Fact]
    public void A_gamble_falls_by_its_weights_and_the_seed_repeats_it()
    {
        int Spin(int seed)
        {
            var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()), seed);
            run.SetResource(Gold, 20);
            var context = new NodeResolveContext(run, new ScriptedChoiceProvider("spin", "take"), Registry(), new RunEffectProcessor());
            new EventNodeResolver().Resolve(context, new Node(new NodeId("e"), StandardRunIds.EventNode, Wheel()));
            return run.GetResource(Gold);
        }
        var results = Enumerable.Range(1, 2000).Select(Spin).ToList();
        var wins = results.Count(gold => gold == 120);
        Assert.Equal(2000, wins + results.Count(gold => gold == 10));
        Assert.InRange(wins, 430, 570);                 // 1 in 4
        Assert.Equal(Spin(17), Spin(17));

        var options = RunJson.CreateOptions();
        var json = RunJson.ToJson(Wheel(), options);
        Assert.Equal(2, RunJson.FromJson<EventScript>(json, options).Situations["wheel"].Choices[0].Outcomes!.Count);
        Assert.DoesNotContain("Outcomes", RunJson.ToJson(Camp(declinable: false), options));
    }
}
