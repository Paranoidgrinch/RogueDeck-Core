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
                CombatStatuses = [new StartingStatusSpec(new StatusDefinitionId("pot_keeper"), 1)],
            },
            Statuses = [new StatusData { Id = "pot_keeper", NameKey = "Pot Keeper", UsesStacks = true }],
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

    // THE WITCH IN SOMEONE ELSE'S PARTY (multiplayer M1): a seat taken by the witch brings her moves and her
    // standing rule into the fight as hers — the clerk beside her has neither — and she works the pot from her
    // own hand and her own energy, question and all, through the replay.
    [Fact]
    public void A_witch_seated_beside_a_clerk_brings_her_own_actions_and_rules_into_the_party_fight()
    {
        var witchy = Witchy();
        var blueprint = witchy with
        {
            Characters =
            [
                new RunCharacter("clerk", new RunStart { HeroName = "Clerk", MaxHealth = 50, StartingHealth = 50 }),
                new RunCharacter("witch", witchy.Start),
            ],
        };
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true, party: ["clerk", "witch"]);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        var driver = play.PartyCombatDriver!;
        var combat = driver.Current!;
        var clerk = combat.ActiveMembers()[0];
        var witch = combat.ActiveMembers()[1];
        var enemy = combat.State.Combatants.First(c => c.TeamId == StandardCombatIds.EnemyTeam).Id;

        Assert.Empty(combat.ActionsOf(clerk));
        Assert.Equal(["stash", "brew"], combat.ActionsOf(witch).Select(a => a.value));
        Assert.Contains(combat.State.GetCombatant(witch).Statuses, s => s.DefinitionId.value == "pot_keeper");
        Assert.DoesNotContain(combat.State.GetCombatant(clerk).Statuses, s => s.DefinitionId.value == "pot_keeper");
        Assert.False(combat.CanUse(clerk, new CardDefinitionId("stash")));

        for (var i = 0; i < 2; i++)
        {
            driver.UseActionFor(witch, new CardDefinitionId("stash"), null);
            var hand = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
            driver.SupplyCardChoice([hand[0].Id]);
            Assert.Null(play.Session.Error);
        }
        combat = driver.Current!;
        Assert.Equal(2, combat.State.GetCardZones(witch).SetAside.Count);
        Assert.True(combat.CanUse(witch, new CardDefinitionId("brew"), enemy));

        var health = combat.State.GetCombatant(enemy).Health.Current;
        driver.UseActionFor(witch, new CardDefinitionId("brew"), enemy);
        combat = driver.Current!;
        Assert.Equal(health - 20, combat.State.GetCombatant(enemy).Health.Current);
        Assert.Equal(2, combat.EnergyOf(witch));
        Assert.Equal(3, combat.EnergyOf(clerk));
    }

    // An action that ASKS (which card to set aside) parks the replay, and a question is not a problem: it used to
    // be recorded as "Step threw … ReplayParkedException", which the game showed as an error toast on every press
    // of the Witch's pot.
    [Fact]
    public void An_action_that_asks_a_question_records_no_problem()
    {
        using var play = Start(Witchy());
        play.CombatDriver!.UseAction(new CardDefinitionId("stash"), null);
        // Parked at the question — where the game reads the steps for problems to show.
        Assert.NotNull(play.CombatDriver.PendingCardChoice);
        Assert.DoesNotContain(play.CombatDriver.Current!.Steps, step => step.HasProblems);
    }

    // An action is not a card played, so a rule about CARDS does not price it: "every card you play costs 1 more"
    // (Burdened) leaves the pot alone, while a discount aimed at the action by its tag ("the first ingredient each
    // turn is free") still reaches it. Player report 2026-10-04: under Burdened the free ingredient cost 1 and the
    // pot greyed out with no Energy left.
    [Fact]
    public void A_tax_on_cards_does_not_reach_an_action_but_a_discount_aimed_at_it_does()
    {
        var blueprint = Witchy();
        var tagged = blueprint.Cards.Select(c => c.Id == "brew" ? c with { Tags = [new TagId("brew_action")] } : c).ToList();
        CombatStatuses(blueprint, tagged, out var taxed);
        using var play = Start(taxed);
        var combat = play.CombatDriver!.Current!;
        var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;

        // Brew is printed at 1: the tax (+1, any card) is not its business, the discount (−1, brew_action) is.
        Assert.Equal(0, combat.ActionCost(new CardDefinitionId("brew"), enemy).Sum(c => c.Amount));
        // A card still pays the tax: jab is printed at 1 and costs 2.
        play.CombatDriver!.PlayCard(combat.Hand[0].Id, enemy);
        Assert.Equal(1, play.CombatDriver!.Current!.HeroEnergy);
    }

    private static void CombatStatuses(RunBlueprint blueprint, IReadOnlyList<CardData> cards, out RunBlueprint taxed) =>
        taxed = blueprint with
        {
            Cards = cards,
            Start = blueprint.Start with
            {
                CombatStatuses = [.. blueprint.Start.CombatStatuses,
                    new StartingStatusSpec(new StatusDefinitionId("tax"), 1),
                    new StartingStatusSpec(new StatusDefinitionId("discount"), 1)],
            },
            Statuses = [.. blueprint.Statuses,
                new StatusData
                {
                    Id = "tax", NameKey = "Tax", UsesStacks = true,
                    PassiveModifiers = [new PassiveModifierData(PassiveModifierPipeline.CardCost, PassiveModifierOperation.AddFlat, 1, RestrictDamageKind: null)],
                },
                new StatusData
                {
                    Id = "discount", NameKey = "Discount", UsesStacks = true,
                    PassiveModifiers = [new PassiveModifierData(PassiveModifierPipeline.CardCost, PassiveModifierOperation.AddFlat, -1,
                        RestrictDamageKind: null, RestrictSourceCardTag: "brew_action")],
                }],
        };

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
        // The character's own standing status, brought into the fight and back out of the save with it.
        Assert.Contains(back.State.GetCombatant(back.HeroId).Statuses, s => s.DefinitionId.value == "pot_keeper");
        // …and the RUN still knows them, so the next fight is given them too, not only the one in the save.
        Assert.Equal(["pot_keeper"], resumed.Session!.Run.CombatStatuses.Select(s => s.Status.value));
        Assert.Equal(["stash", "brew"], resumed.Session.Run.CombatActions.Select(a => a.value));
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
