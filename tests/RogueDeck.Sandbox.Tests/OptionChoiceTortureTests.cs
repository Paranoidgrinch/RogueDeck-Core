using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// "Choose one: …" — a card that offers named options and lets the player pick which of them happen.
//
// Malediction Review offers a Censure for yourself or one for the enemy; Grand Dispensation offers four
// things and takes two DIFFERENT ones. The prompt parks the fight the way an in-combat card choice does, and
// with nobody to ask (headless play) the first options are taken, so such a card always resolves to
// something. Driven through the REAL host path.
public class OptionChoiceTortureTests
{
    private static CombatNodeModel Damage(int amount) =>
        new("dealDamage", "eventTarget", CombatAmountSpec.FromConst(amount));

    private static CombatNodeModel Block(int amount) =>
        new("gainBlock", "source", CombatAmountSpec.FromConst(amount));

    private static RunBlueprint Duel(IReadOnlyList<string>? deck = null)
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(Damage(1)),
        };

        // "Choose one: gain 9 Block; or deal 9 damage."
        var either = new CardData
        {
            Id = "either",
            NameKey = "Either",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(CombatNodeModel.ChooseOptions(
                1, ["gain 9 Block", "deal 9 damage"], [Block(9), Damage(9)], "choose one")),
        };

        // "Choose 2 different options."
        var two = new CardData
        {
            Id = "two",
            NameKey = "Two",
            Costs = Array.Empty<ResourceCost>(),
            Program = CombatProgramModel.Build<CardPlayContext>(CombatNodeModel.ChooseOptions(
                2, ["gain 4 Block", "deal 4 damage", "draw 1 card"],
                [Block(4), Damage(4), new CombatNodeModel("drawCards", "source", CombatAmountSpec.FromConst(1))],
                "choose two")),
        };

        var duel = new EncounterDefinition(new EncounterId("duel"), new[]
        {
            new EncounterEnemy("dummy", 200, new[] { new EnemyActionDefinitionId("nip") }, DisplayName: "Dummy"),
        },
            new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 9, 9) });

        deck ??= ["either", "two"];

        return new RunBlueprint(
            deck.Select(id => new CardDefinitionId(id)).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { duel },
            new[] { either, two },
            new[] { nip },
            new RunMap(new[]
            {
                new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel"))),
            }))
        {
            Start = new RunStart { HeroName = "Filer", MaxHealth = 200, StartingHealth = 200 },
        };
    }

    private static RunPlayback Start(RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        return play;
    }

    [Fact]
    public void The_card_parks_on_its_prompt_and_runs_what_the_player_took()
    {
        var play = Start(Duel(["either", "either"]));
        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var enemyHealth = combat.State.GetCombatant(enemyId).Health.Current;

            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "either").Id, enemyId);
            Assert.Null(play.Session!.Error);

            // Parked: nothing has happened yet, and the options are on the table.
            Assert.Equal(["gain 9 Block", "deal 9 damage"], play.CombatDriver.PendingOptionChoice);
            Assert.Equal(1, play.CombatDriver.PendingOptionChoiceCount);
            Assert.Equal("choose one", play.CombatDriver.PendingOptionChoicePurpose);
            Assert.Equal(enemyHealth, play.CombatDriver.Current!.State.GetCombatant(enemyId).Health.Current);

            play.CombatDriver.SupplyOptionChoice([1]); // deal 9 damage
            Assert.Null(play.Session.Error);

            Assert.Null(play.CombatDriver.PendingOptionChoice);
            Assert.Equal(enemyHealth - 9, play.CombatDriver.Current!.State.GetCombatant(enemyId).Health.Current);
            Assert.Equal(0, Block(play));
        }
    }

    [Fact]
    public void Two_different_options_both_resolve_in_the_order_they_were_picked()
    {
        var play = Start(Duel(["two", "two"]));
        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var enemyHealth = combat.State.GetCombatant(enemyId).Health.Current;
            var hand = combat.Hand.Count;

            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "two").Id, enemyId);
            Assert.Equal(2, play.CombatDriver.PendingOptionChoiceCount);

            play.CombatDriver.SupplyOptionChoice([1, 0]); // damage, then Block
            Assert.Null(play.Session!.Error);

            Assert.Equal(enemyHealth - 4, play.CombatDriver.Current!.State.GetCombatant(enemyId).Health.Current);
            Assert.Equal(4, Block(play));
            Assert.Equal(hand - 1, play.CombatDriver.Current!.Hand.Count); // the card left; no draw was taken
        }
    }

    [Fact]
    public void An_option_cannot_be_taken_twice()
    {
        var play = Start(Duel(["two", "two"]));
        using (play)
        {
            var combat = play.CombatDriver!.Current!;
            var enemyId = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
            var enemyHealth = combat.State.GetCombatant(enemyId).Health.Current;

            play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId.value == "two").Id, enemyId);
            play.CombatDriver.SupplyOptionChoice([1, 1]); // the same option twice
            Assert.Null(play.Session!.Error);

            // It happens once, not twice.
            Assert.Equal(enemyHealth - 4, play.CombatDriver.Current!.State.GetCombatant(enemyId).Health.Current);
        }
    }

    [Fact]
    public void With_nobody_to_ask_the_first_options_are_taken()
    {
        // A headless run installs no option chooser, and a card that offers a choice must still resolve.
        var play = new RunPlayback(() => { });
        play.Start(Duel(["either", "either"]), seed: 1, interactive: false);
        Assert.Null(play.Error);

        using (play)
        {
            Assert.NotNull(play.Session);
            // The auto-play driver fought the whole node without stalling on the prompt.
            Assert.Null(play.Session!.Error);
        }
    }

    [Fact]
    public void A_choice_round_trips_through_the_authoring_model()
    {
        var model = CombatNodeModel.ChooseOptions(
            2, ["gain 4 Block", "deal 4 damage"], [Block(4), Damage(4)], "choose two");

        Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    private static int Block(RunPlayback play)
    {
        var combat = play.CombatDriver!.Current!;
        return combat.State.GetCombatant(combat.HeroId)
            .DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
    }
}
