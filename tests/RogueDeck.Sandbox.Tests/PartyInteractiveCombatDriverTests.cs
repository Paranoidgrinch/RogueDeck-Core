using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// Party deckbuilding C2 follow-up (interactive party combat): PartyInteractiveCombatDriver lets the UI drive the
// simultaneous phase per member under deterministic replay (see ReplayScript) — each recorded action re-runs the
// fight to the next unanswered prompt, then reports each member for reconcile. Proves a human-driven party fight
// completes with per-member results and that a mid-play card choice parks and resumes.
public class PartyInteractiveCombatDriverTests
{
    private static readonly CombatantId GoblinId = new("goblin");
    private static readonly CardDefinitionId Strike = new("strike");

    // One replay attempt: park at the next unanswered prompt (null) or finish the fight (the result).
    private static CombatDriveResult? Attempt(PartyInteractiveCombatDriver driver, Func<Playthrough> fight)
    {
        driver.ResetForReplay();
        try
        {
            return driver.Drive(fight());
        }
        catch (ReplayParkedException)
        {
            return null;
        }
    }

    // A two-member party (hero + knight, each a 5-card 5-damage strike deck) vs a 60-HP goblin that slams a player
    // for 4 — big enough to survive round 1, so the enemy phase runs before the party finishes it in round 2.
    private static Playthrough PartyFight(int goblinHp = 60)
    {
        var blueprint = new ScenarioBlueprint { SimultaneousTeamTurns = true };
        blueprint.Cards.Add(new CardBlueprint("strike")
        {
            Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(5))),
        });
        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 5; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(Strike));
        var knight = new AllyBlueprint("knight") { MaxHealth = 25 };
        knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 5; i++)
            knight.Deck.Add(new DeckEntry(Strike));
        blueprint.Allies.Add(knight);
        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = goblinHp };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    // A party fight where the hero's whole deck is a "reclaim" card: on play it prompts a pick from the DRAW pile
    // (parking the fight on the card chooser), banishes that pick to exhaust, then blasts the goblin for 100 — so
    // supplying the pick both proves the chosen card was affected and ends the fight.
    private static Playthrough ReclaimPartyFight()
    {
        var blueprint = new ScenarioBlueprint { SimultaneousTeamTurns = true };
        blueprint.Cards.Add(new CardBlueprint("reclaim")
        {
            Program = new EffectProgram<CardPlayContext>(new SequenceEffectNode<CardPlayContext>(new IEffectNode<CardPlayContext>[]
            {
                new MoveCardToZoneNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new ChosenCardInZoneExpression<CardPlayContext>(CardZone.DrawPile, "reclaim a card"),
                    CardZone.ExhaustPile),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(100)),
            })),
        });
        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 8; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("reclaim")));
        var knight = new AllyBlueprint("knight") { MaxHealth = 25 };
        knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 8; i++)
            knight.Deck.Add(new DeckEntry(new CardDefinitionId("reclaim")));
        blueprint.Allies.Add(knight);
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 10 };
        blueprint.Enemies.Add(goblin);
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    [Fact]
    public void A_members_play_parks_on_a_card_choice_and_resumes_on_the_supplied_pick()
    {
        using var driver = new PartyInteractiveCombatDriver();

        // The first attempt parks awaiting the first player action.
        Assert.Null(Attempt(driver, ReclaimPartyFight));
        var party = driver.Current;
        Assert.NotNull(party);

        // The hero plays its first card; the replay PARKS on the draw-pile choice.
        var hero = new CombatantId("hero");
        driver.PlayCardFor(hero, party!.HandOf(hero)[0].Id, GoblinId);
        Assert.Null(Attempt(driver, ReclaimPartyFight));

        var candidates = driver.PendingCardChoice;
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates!.Count); // 8 in deck, 5 drawn to the opening hand, 3 left in the draw pile
        Assert.Equal("reclaim a card", driver.PendingCardChoicePurpose);

        // Supply the middle candidate; the replay resumes the play (the pick is exhausted, the goblin blasted) and
        // the fight ends. The pick-moves-the-chosen-card behaviour itself is engine-covered in the Scenario suite.
        driver.SupplyCardChoice(new[] { candidates[1].Id });
        var result = Attempt(driver, ReclaimPartyFight);

        Assert.NotNull(result);
        Assert.Equal(CombatResult.Victory, result!.Result);
        Assert.Null(driver.PendingCardChoice);
        Assert.Null(driver.Current);
    }

    [Fact]
    public void A_human_drives_the_party_fight_to_victory_and_each_member_is_reported()
    {
        using var driver = new PartyInteractiveCombatDriver();

        // Park at the fight, then record one action per attempt — every active member plays its hand at the goblin
        // and ends its turn, across as many rounds as it takes — until the fight resolves.
        var result = Attempt(driver, () => PartyFight());
        Assert.Null(result);
        Assert.NotNull(driver.Current);

        for (var guard = 0; result is null && guard < 400; guard++)
        {
            var live = driver.Current;
            Assert.NotNull(live);
            var member = live!.ActiveMembers().First();
            var hand = live.HandOf(member);
            if (hand.Count > 0)
                driver.PlayCardFor(member, hand[0].Id, GoblinId);
            else
                driver.EndTurnFor(member);
            result = Attempt(driver, () => PartyFight());
        }

        Assert.NotNull(result);
        Assert.Equal(CombatResult.Victory, result!.Result);

        // The knight (a projected party member) is reported for reconcile; the hero comes back via HeroHpRemaining.
        var knight = Assert.Single(result.Units!);
        Assert.Equal(new CombatantId("knight"), knight.Id);
        Assert.True(knight.Alive);
        Assert.True(result.HeroHpRemaining <= 30);
    }
}
