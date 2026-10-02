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
}
