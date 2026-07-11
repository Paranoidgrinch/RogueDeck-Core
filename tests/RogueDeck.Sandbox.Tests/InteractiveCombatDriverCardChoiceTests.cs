using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// In-combat card targeting (Tier-2 card domain), final slice: a human-driven fight can prompt the player to CHOOSE
// a card mid-resolution (Armaments-style). The chosen-card selection blocks the resolving thread, so the driver runs
// each play on a background task and parks it on a UiCombatCardChooser until the UI supplies a pick. This test
// mirrors that exact threading — Drive runs on a background task (the run thread), the test thread (the circuit)
// plays a card, observes the fight PARK on a card choice, supplies the pick, and sees the fight resolve to victory —
// proving the park-and-resume works end to end and never strands the run thread.
[Xunit.Collection("Threaded")]
public class InteractiveCombatDriverCardChoiceTests
{
    private static readonly CombatantId GoblinId = new("goblin");
    private static readonly CardDefinitionId Reclaim = new("reclaim");

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

    // A hero whose whole deck is the "reclaim" card: on play it first asks the player to pick a card in the DRAW pile
    // (which parks the fight on the card chooser), banishes that pick to exhaust, then blasts the goblin for 100 — so
    // supplying the pick both proves the chosen card was affected and ends the fight. 8 copies: 5 draw into the
    // opening hand, leaving 3 in the draw pile as choice candidates.
    private static Playthrough ReclaimFight()
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("reclaim")
        {
            Program = new EffectProgram<CardPlayContext>(new SequenceEffectNode<CardPlayContext>(new IEffectNode<CardPlayContext>[]
            {
                new MoveCardToZoneNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new ChosenCardInZoneExpression<CardPlayContext>(CardZone.DrawPile, "reclaim a card from your draw pile"),
                    CardZone.ExhaustPile),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(100)),
            })),
        });
        blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < 8; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(Reclaim));
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 10 };
        blueprint.Enemies.Add(goblin);
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    [Fact]
    public async Task A_played_card_parks_on_a_card_choice_and_resumes_on_the_supplied_pick()
    {
        using var driver = new InteractiveCombatDriver();

        // The run thread: parks inside Drive until the fight ends, exactly as RunRunner would.
        CombatDriveResult? result = null;
        var runThread = Task.Run(() => result = driver.Drive(ReclaimFight()));

        // The circuit thread: wait for the fight, then play the first card in hand at the goblin.
        var live = WaitFor(() => driver.Current, TimeSpan.FromSeconds(5));
        Assert.NotNull(live);
        var handCard = live!.Hand[0].Id;
        driver.PlayCard(handCard, GoblinId);

        // The play resolves on a background task and PARKS on the draw-pile card choice — three candidates remain.
        var candidates = WaitFor(() => driver.PendingCardChoice, TimeSpan.FromSeconds(5));
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates!.Count);
        Assert.Equal("reclaim a card from your draw pile", driver.PendingCardChoicePurpose);
        Assert.False(live.IsOver); // still parked, not resolved

        // Supply the middle candidate; the task resumes, exhausts that card, blasts the goblin, and the fight ends.
        var picked = candidates[1].Id;
        driver.SupplyCardChoice(new[] { picked });

        var finished = await Task.WhenAny(runThread, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(runThread, finished);
        await runThread;

        Assert.NotNull(result);
        Assert.Equal(CombatResult.Victory, result!.Result);
        Assert.Null(driver.PendingCardChoice); // the choice was consumed
        // The player's pick was the card affected: it moved from the draw pile to the exhaust pile.
        Assert.Contains(picked, live.State.GetCardZones(live.HeroId).ExhaustPile.Select(c => c.Id));
    }
}
