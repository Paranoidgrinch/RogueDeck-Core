using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Can a rule hear a card ARRIVE, and keep that one copy?
//
// The two halves of "the first card that enters your hand outside the normal draw stays until your next turn",
// which is a relic's line and a shape nothing could say before: a status trigger on CardMovedToZone, and the
// per-instance RetainedCardMark on the card the event names.
//
// Three things have to be true, and each is a different way for it to be wrong: the trigger has to fire on a
// card that is PUT into the hand, it has to stay silent for the four cards the turn DREW into the same hand
// (the draw step does not report through this event, and a rule that could not tell them apart would keep the
// whole hand), and the mark has to outlive the turn end that clears everything else away.
public class CardEntersHandTriggerTests
{
    private const string Fetch = "fetch";
    private const string Toss = "toss";
    private const string Conjure = "conjure";

    [Fact]
    public void A_card_put_into_the_hand_is_kept_and_the_drawn_ones_are_not()
    {
        using var play = Fight(Fetch);
        var driver = play.CombatDriver!;
        var hero = driver.Current!.HeroId;
        var drawn = driver.Current.Hand.Select(card => card.Id).ToList();

        driver.PlayCard(drawn[0], null);
        Assert.Null(play.Session!.Error);

        // The fetched card is the one in hand that was not dealt at the start of the turn.
        var fetched = driver.Current!.Hand.Select(card => card.Id).Except(drawn).Single();
        Assert.Equal([fetched], Marked(driver, hero));

        driver.EndTurn();
        Assert.Null(play.Session.Error);

        // Turn two: the kept card is still there, and nothing that was merely drawn survived with it.
        var handAfter = driver.Current!.State.GetCardZones(hero).GetCardsInZone(CardZone.Hand);
        Assert.Contains(fetched, handAfter.Select(card => card.Id));
        Assert.Empty(handAfter.Select(card => card.Id).Intersect(drawn));
    }

    // The other way a card arrives in a hand nobody drew it into: it is made there. Same rule, second trigger
    // — a creation is not a move, so a rule that only heard moves would be silent for every card an enemy
    // pushes into your hand.
    [Fact]
    public void A_card_made_in_the_hand_is_kept_too()
    {
        using var play = Fight(Conjure);
        var driver = play.CombatDriver!;
        var hero = driver.Current!.HeroId;
        var drawn = driver.Current.Hand.Select(card => card.Id).ToList();

        driver.PlayCard(drawn[0], null);
        Assert.Null(play.Session!.Error);

        var made = driver.Current!.Hand.Select(card => card.Id).Except(drawn).Single();
        Assert.Equal([made], Marked(driver, hero));
    }

    [Fact]
    public void A_card_that_leaves_the_hand_is_not_kept()
    {
        using var play = Fight(Toss);
        var driver = play.CombatDriver!;
        var hero = driver.Current!.HeroId;

        driver.PlayCard(driver.Current.Hand.First().Id, null);
        Assert.Null(play.Session!.Error);

        // Same event, the other direction. A rule that read the card's zone instead of the event's would have
        // marked this one too — it is in the discard pile, which is not the hand, but it MOVED out of one.
        Assert.Empty(Marked(driver, hero));
    }

    private static IReadOnlyList<CardInstanceId> Marked(InteractiveCombatDriver driver, CombatantId hero)
    {
        var zones = driver.Current!.State.GetCardZones(hero);
        return Enum.GetValues<CardZone>()
            .SelectMany(zone => zones.GetCardsInZone(zone))
            .Where(card => card.HasMark(StandardCombatIds.RetainedCardMark))
            .Select(card => card.Id)
            .ToList();
    }

    private static RunPlayback Fight(string opener)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(opener), seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        return play;
    }

    // The rule: when one of your cards lands in your hand, that copy is kept. One body, hung on both of the
    // events that can put a card there.
    private static EffectProgram<TContext> Bookmark<TContext>() where TContext : class =>
        new(new ConditionalEffectNode<TContext>(
            new TriggerEventCardZoneExpression<TContext>(CardZone.Hand),
            new MarkCardInstanceNode<TContext>(
                CombatantTargetSelectors.Source,
                new TriggerEventCardInstanceExpression<TContext>(),
                StandardCombatIds.RetainedCardMark)));

    private static CardData Mover(string id, string name, CardZone from, CardZone to) => new()
    {
        Id = id,
        NameKey = name,
        Costs = Array.Empty<ResourceCost>(),
        Program = new EffectProgram<CardPlayContext>(
            new MoveCardToZoneNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new CardInZoneExpression<CardPlayContext>(from, 1),
                to)),
    };

    private static RunBlueprint Duel(string opener)
    {
        // "fetch" pulls a card out of the draw pile; "toss" pushes one out of the hand. Index 1 in either
        // case, so the card doing the moving is never the card that moves.
        var fetch = Mover(Fetch, "Fetch", CardZone.DrawPile, CardZone.Hand);
        var toss = Mover(Toss, "Toss", CardZone.Hand, CardZone.DiscardPile);
        var conjure = new CardData
        {
            Id = Conjure,
            NameKey = "Conjure",
            Costs = Array.Empty<ResourceCost>(),
            Program = new EffectProgram<CardPlayContext>(
                new CreateCardInstanceNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new CardDefinitionId(Toss), CardZone.Hand,
                    new ConstantExpression<CardPlayContext>(1))),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        var bookmark = new StatusData
        {
            Id = "bookmark",
            NameKey = "Bookmark",
            UsesStacks = false,
            Triggers =
            [
                new StatusTriggerData(TriggerEvent.CardMovedToZone.ToString(),
                    JsonSerializer.SerializeToElement(Bookmark<CardMovedToZoneTriggeredEffectContext>(),
                        CombatJson.CreateOptions<CardMovedToZoneTriggeredEffectContext>())),
                new StatusTriggerData(TriggerEvent.CardInstanceCreated.ToString(),
                    JsonSerializer.SerializeToElement(Bookmark<CardInstanceCreatedTriggeredEffectContext>(),
                        CombatJson.CreateOptions<CardInstanceCreatedTriggeredEffectContext>())),
            ],
        };

        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 60, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9)],
            heroStartingStatuses: [new StartingStatusSpec(new StatusDefinitionId("bookmark"), 1)]);

        // A deck of nothing but the card under test, so the shuffle cannot decide what is in hand. The cards
        // are told apart by INSTANCE throughout — which is the only way a per-copy mark can be told apart.
        return new RunBlueprint(
            [.. Enumerable.Repeat(new CardDefinitionId(opener), 12)],
            new Dictionary<string, EventScript>(),
            [duel], [fetch, toss, conjure], [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Statuses = [bookmark],
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }
}
