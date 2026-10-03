using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// A character's own ACTIONS (Hedge Witch plan C3/C4): moves the hero uses without drawing them — "set a card
// aside", "brew what is set aside". An action pays its cost and runs its program exactly as a card, but it is
// NOT a card play; it may be refused by its own condition; it may ask a question; and the whole thing replays and
// survives a save. Driven through the REAL host path.
public class CharacterActionTortureTests
{
    private static CombatNodeModel Hit(CombatAmountSpec amount) => new("dealDamage", "eventTarget", amount);

    private static ICombatExpression<CardPlayContext, bool> AsideIs(int count) =>
        new ComparisonExpression<CardPlayContext>(
            new CombatantZoneCardCountExpression<CardPlayContext>(CombatantTargetSelectors.Source, CardZone.SetAsidePile),
            ComparisonOperator.Equal, new ConstantExpression<CardPlayContext>(count));

    private static RunBlueprint Witchy()
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(Hit(CombatAmountSpec.FromConst(1))),
        };
        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 200, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);

        CardData Card(string id) => new()
        {
            Id = id,
            NameKey = id,
            Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
            Program = CombatProgramModel.Build<CardPlayContext>(Hit(CombatAmountSpec.FromConst(2))),
        };

        return new RunBlueprint(
            [new CardDefinitionId("jab"), new CardDefinitionId("jab"), new CardDefinitionId("jab"),
             new CardDefinitionId("jab"), new CardDefinitionId("jab")],
            new Dictionary<string, EventScript>(),
            [duel],
            [
                Card("jab"),
                // "Set a card from your hand aside." Free; refused once two are set aside.
                new CardData
                {
                    Id = "stash",
                    NameKey = "Stash",
                    IsAction = true,
                    PlayCondition = new NotExpression<CardPlayContext>(AsideIs(2)),
                    Program = CombatProgramModel.Build<CardPlayContext>(new CombatNodeModel("moveCardToZone", "source",
                        Card: new CombatCardSpec("chosen", CardZone.Hand, Purpose: "choose a card to set aside"),
                        ToZone: CardZone.SetAsidePile)),
                },
                // "Only with two set aside: deal 10 damage per card set aside, then discard them." 1 Energy.
                new CardData
                {
                    Id = "brew",
                    NameKey = "Brew",
                    IsAction = true,
                    Costs = [new ResourceCost(StandardCombatIds.EnergyResource, 1)],
                    PlayCondition = AsideIs(2),
                    Program = CombatProgramModel.Build<CardPlayContext>(CombatNodeModel.Sequence([
                        Hit(CombatAmountSpec.Binary("mul", CombatAmountSpec.FromConst(10),
                            new CombatAmountSpec("zoneCards", Zone: CardZone.SetAsidePile))),
                        new CombatNodeModel("moveCards", "source", FromZone: CardZone.SetAsidePile,
                            ToZone: CardZone.DiscardPile)])),
                },
            ],
            [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Start = new RunStart
            {
                HeroName = "Witch",
                MaxHealth = 50,
                StartingHealth = 50,
                CombatActions = [new CardDefinitionId("stash"), new CardDefinitionId("brew")],
            },
        };
    }

    private static RunPlayback Start(RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        Assert.NotNull(play.CombatDriver?.Current);
        return play;
    }

    private static void Stash(RunPlayback play)
    {
        var driver = play.CombatDriver!;
        driver.UseAction(new CardDefinitionId("stash"), null);
        Assert.Null(play.Session!.Error);
        var hand = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
        driver.SupplyCardChoice([hand[0].Id]);
        Assert.Null(play.Session!.Error);
    }

    [Fact]
    public void An_action_is_used_not_played_and_refused_until_its_condition_holds()
    {
        using var play = Start(Witchy());
        var combat = play.CombatDriver!.Current!;
        var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
        Assert.Equal(["stash", "brew"], combat.Actions.Select(a => a.value));
        Assert.False(combat.CanUse(new CardDefinitionId("brew"), enemy)); // nothing set aside yet

        Stash(play);
        Stash(play);
        combat = play.CombatDriver!.Current!;
        Assert.Equal(2, combat.State.GetCardZones(combat.HeroId).SetAside.Count);
        Assert.Equal(3, combat.HeroEnergy);                                  // setting aside was free
        Assert.False(combat.CanUse(new CardDefinitionId("stash")));         // two is the most
        Assert.True(combat.CanUse(new CardDefinitionId("brew"), enemy));

        var health = combat.State.GetCombatant(enemy).Health.Current;
        play.CombatDriver!.UseAction(new CardDefinitionId("brew"), enemy);
        combat = play.CombatDriver!.Current!;
        Assert.Equal(health - 20, combat.State.GetCombatant(enemy).Health.Current);
        Assert.Equal(2, combat.HeroEnergy);
        Assert.Empty(combat.State.GetCardZones(combat.HeroId).SetAside);
        // Not one of the three actions was a card play.
        Assert.Equal(0, combat.State.GetCardPlayTurnStats(combat.HeroId).CardsPlayedThisTurn);
    }

    [Fact]
    public void A_fight_with_cards_set_aside_by_an_action_comes_back_from_a_save_the_same()
    {
        var blueprint = Witchy();
        string save;
        string[] aside;
        using (var play = Start(blueprint))
        {
            Stash(play);
            Stash(play);
            var combat = play.CombatDriver!.Current!;
            aside = [.. combat.State.GetCardZones(combat.HeroId).SetAside.Select(c => c.Id.value)];
            save = play.SaveJson()!;
        }

        using var resumed = new RunPlayback(() => { });
        resumed.Resume(blueprint, RunSaveJson.FromJson(save), interactive: true);
        Assert.Null(resumed.Error);
        var back = resumed.CombatDriver!.Current!;
        Assert.Equal(aside, back.State.GetCardZones(back.HeroId).SetAside.Select(c => c.Id.value));
        Assert.Equal(["stash", "brew"], back.Actions.Select(a => a.value));
        var enemy = back.State.Combatants.First(c => c.Id != back.HeroId).Id;
        Assert.True(back.CanUse(new CardDefinitionId("brew"), enemy));
    }

    // THE PLANNER KNOWS THE ACTIONS (plan C7): with three Energy, three jabs are 6 damage; setting two cards
    // aside for free and brewing them is 20, plus two jabs. A search that only played cards would never find it.
    [Fact]
    public void The_planner_finds_the_brew_when_it_is_the_better_turn()
    {
        using var play = Start(Witchy());
        var fight = new RogueDeck.Bot.FightPlanner(horizon: 1, beam: 8, perTurn: 400, turnCap: 1)
            .Play(play.CombatDriver!.Current!);
        var first = Assert.Single(fight.Lines);
        // Two set aside, then the brew (setting more aside afterwards is free here, and the search may well do it).
        var brew = first.IndexOf("!brew", StringComparison.Ordinal);
        Assert.True(brew > 0, first);
        Assert.Equal(2, first[..brew].Split("!stash").Length - 1);
    }
}
