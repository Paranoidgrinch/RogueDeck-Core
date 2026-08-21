using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Creating a card hands back its instance id; drawing one now does too. The gap this closes is narrow and
// sharp: a program could draw a replacement card and then have nothing to say ABOUT it — the hand it landed in
// is ordered, but a program cannot index "the newest". Content wants that card by name ("the replacement draw
// gets Open Aisle for this turn"), and a mark is how a later rule recognises it again.
public class DrawnCardOutcomeTests
{
    private static readonly TagId Fresh = new("fresh");
    private static readonly EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>> Drawn = new("drawn");

    // The card the draw produced is the one that gets marked — not the first card in hand, not the last one
    // played, and not a card that was already there.
    [Fact]
    public void The_card_a_draw_just_produced_can_be_named()
    {
        using var play = Start(drawCount: 1, markIndex: 0);
        var combat = play.CombatDriver!.Current!;
        var before = combat.Hand.Select(c => c.Id).ToHashSet();

        play.CombatDriver.PlayCard(combat.Hand.First().Id, EnemyOf(play));
        Assert.Null(play.Session!.Error);

        var hand = play.CombatDriver.Current!.Hand;
        var marked = hand.Where(c => c.HasMark(Fresh)).ToList();
        var card = Assert.Single(marked);
        Assert.DoesNotContain(card.Id, before);
    }

    // With several cards drawn at once the index picks among them, so "the second card you drew" is sayable.
    [Fact]
    public void The_index_picks_among_the_cards_one_draw_produced()
    {
        using var play = Start(drawCount: 3, markIndex: 2);
        var combat = play.CombatDriver!.Current!;
        var before = combat.Hand.Select(c => c.Id).ToHashSet();

        play.CombatDriver.PlayCard(combat.Hand.First().Id, EnemyOf(play));
        Assert.Null(play.Session!.Error);

        var hand = play.CombatDriver.Current!.Hand;
        var fresh = hand.Where(c => !before.Contains(c.Id)).ToList();
        Assert.Equal(3, fresh.Count);
        // Exactly the third of the three, in draw order.
        Assert.Equal([fresh[2].Id], hand.Where(c => c.HasMark(Fresh)).Select(c => c.Id));
    }

    // An index past the end of the draw names no card, and a card operation given no card does nothing — the
    // same quiet answer every other card-instance expression gives when it cannot resolve.
    [Fact]
    public void An_index_past_the_draw_names_nothing()
    {
        using var play = Start(drawCount: 1, markIndex: 4);

        play.CombatDriver!.PlayCard(play.CombatDriver.Current!.Hand.First().Id, EnemyOf(play));
        Assert.Null(play.Session!.Error);

        var zones = play.CombatDriver.Current!.State.GetCardZones(play.CombatDriver.Current!.HeroId);
        Assert.DoesNotContain(
            Enum.GetValues<CardZone>().SelectMany(zones.GetCardsInZone),
            card => card.HasMark(Fresh));
    }

    // It round-trips as data like every other expression: a rule that names a drawn card survives a save.
    [Fact]
    public void The_expression_round_trips_as_data()
    {
        var options = CombatJson.CreateOptions<CardPlayContext>();
        var expression = new DrawCardOutcomeExpression<CardPlayContext>(Drawn, index: 2);

        var restored = JsonSerializer.Deserialize<ICardInstanceExpression<CardPlayContext>>(
            JsonSerializer.Serialize<ICardInstanceExpression<CardPlayContext>>(expression, options), options);

        var typed = Assert.IsType<DrawCardOutcomeExpression<CardPlayContext>>(restored);
        Assert.Equal(2, typed.Index);
        Assert.Equal(Drawn.Name, typed.Key.Name);
    }

    private static CombatantId EnemyOf(RunPlayback play)
    {
        var combat = play.CombatDriver!.Current!;
        return combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
    }

    private static RunPlayback Start(int drawCount, int markIndex)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(drawCount, markIndex), seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        return play;
    }

    // One card: draw N, then mark the one at `markIndex` of that draw.
    private static RunBlueprint Duel(int drawCount, int markIndex)
    {
        var fetch = new CardData
        {
            Id = "fetch",
            NameKey = "Fetch",
            Costs = Array.Empty<ResourceCost>(),
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>(
                [
                    new DrawCardsNode<CardPlayContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<CardPlayContext>(drawCount),
                        resultKey: Drawn),
                    new MarkCardInstanceNode<CardPlayContext>(
                        CombatantTargetSelectors.Source,
                        new DrawCardOutcomeExpression<CardPlayContext>(Drawn, markIndex),
                        Fresh),
                ])),
        };
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };

        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 60, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9)]);

        return new RunBlueprint(
            [.. Enumerable.Repeat(new CardDefinitionId("fetch"), 14)],
            new Dictionary<string, EventScript>(),
            [duel], [fetch], [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 40, StartingHealth = 40 },
        };
    }
}
