using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// Party deckbuilding C2 follow-up (interactive party combat): PartyInteractiveCombatDriver parks the run thread in
// Drive and lets the UI drive the simultaneous phase per member on another thread, then reports each member for
// reconcile. This test mirrors that exact threading — Drive runs on a background task (the run thread) while the
// test thread (the circuit) plays cards and ends turns — proving a human-driven party fight completes and reports
// per-member results, and that a fight with no living enemies never strands the parked run thread.
[Xunit.Collection("Threaded")]
public class PartyInteractiveCombatDriverTests
{
    private static readonly CombatantId GoblinId = new("goblin");
    private static readonly CardDefinitionId Strike = new("strike");

    private static T? WaitFor<T>(Func<T?> read, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (read() is { } value)
                return value;
            Thread.Sleep(10);
        }
        return null;
    }

    // Block until a condition clears (or the timeout elapses) — used to pace against the driver's IsResolving so the
    // next action isn't dropped while the previous one is still resolving on its background task.
    private static void WaitWhile(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(5);
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
    public async Task A_members_play_parks_on_a_card_choice_and_resumes_on_the_supplied_pick()
    {
        using var driver = new PartyInteractiveCombatDriver();

        CombatDriveResult? result = null;
        var runThread = Task.Run(() => result = driver.Drive(ReclaimPartyFight()));

        var party = WaitFor(() => driver.Current, TimeSpan.FromSeconds(30));
        Assert.NotNull(party);

        // The hero plays its first card; the play resolves on a background task and PARKS on the draw-pile choice.
        var hero = new CombatantId("hero");
        var handCard = party!.HandOf(hero)[0].Id;
        driver.PlayCardFor(hero, handCard, GoblinId);

        var candidates = WaitFor(() => driver.PendingCardChoice, TimeSpan.FromSeconds(30));
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates!.Count); // 8 in deck, 5 drawn to the opening hand, 3 left in the draw pile
        Assert.Equal("reclaim a card", driver.PendingCardChoicePurpose);

        // Supply the middle candidate; the play resumes, exhausts it, blasts the goblin, and the fight ends.
        var picked = candidates[1].Id;
        driver.SupplyCardChoice(new[] { picked });

        var finished = await Task.WhenAny(runThread, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(runThread, finished);
        await runThread;

        Assert.NotNull(result);
        Assert.Equal(CombatResult.Victory, result!.Result);
        Assert.Null(driver.PendingCardChoice);
        Assert.Contains(picked, party.State.GetCardZones(hero).ExhaustPile.Select(c => c.Id));
    }

    [Fact]
    public async Task A_human_drives_the_party_fight_to_victory_and_each_member_is_reported()
    {
        using var driver = new PartyInteractiveCombatDriver();

        // The run thread: parks inside Drive until the fight ends, exactly as RunRunner would.
        CombatDriveResult? result = null;
        var runThread = Task.Run(() => result = driver.Drive(PartyFight()));

        // The circuit thread: wait for the fight to surface, then play every active member's hand at the goblin and
        // end its turn, across as many rounds as it takes. Current goes null when the fight finishes.
        var party = WaitFor(() => driver.Current, TimeSpan.FromSeconds(30));
        Assert.NotNull(party);

        // Each play/end-turn now resolves on a background task (so a card-choice could park it), and the driver
        // ignores a new action until the current one clears — so pace each action against IsResolving.
        for (var guard = 0; driver.Current is { IsOver: false } live && guard < 200; guard++)
        {
            foreach (var member in live.ActiveMembers().ToArray())
            {
                foreach (var card in live.HandOf(member).ToArray())
                {
                    driver.PlayCardFor(member, card.Id, GoblinId);
                    WaitWhile(() => driver.IsResolving, TimeSpan.FromSeconds(30));
                }
                driver.EndTurnFor(member);
                WaitWhile(() => driver.IsResolving, TimeSpan.FromSeconds(30));
            }
        }

        // Drive returns once the fight ends; await it (no blocking wait) to observe the result + any exception.
        var finished = await Task.WhenAny(runThread, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(runThread, finished);
        await runThread;
        Assert.NotNull(result);
        Assert.Equal(CombatResult.Victory, result!.Result);

        // The knight (a projected party member) is reported for reconcile; the hero comes back via HeroHpRemaining.
        var knight = Assert.Single(result.Units!);
        Assert.Equal(new CombatantId("knight"), knight.Id);
        Assert.True(knight.Alive);
        Assert.True(result.HeroHpRemaining <= 30);
    }
}
