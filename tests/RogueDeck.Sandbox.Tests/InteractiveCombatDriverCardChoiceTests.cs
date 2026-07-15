using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// In-combat card targeting (Tier-2 card domain), final slice: a human-driven fight can prompt the player to CHOOSE
// a card mid-resolution (Armaments-style). Under deterministic replay (see ReplayScript) the chosen-card selection
// PARKS the replay attempt (ReplayParkedException) with the candidates surfaced on the driver; supplying the pick
// records it and the next attempt resolves the play to completion — proving park-and-resume works end to end.
public class InteractiveCombatDriverCardChoiceTests
{
    private static readonly CombatantId GoblinId = new("goblin");
    private static readonly CardDefinitionId Reclaim = new("reclaim");

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
    public void A_played_card_parks_on_a_card_choice_and_resumes_on_the_supplied_pick()
    {
        using var driver = new InteractiveCombatDriver();

        // One replay attempt: park at the next unanswered prompt (null) or finish the fight (the result).
        CombatDriveResult? Attempt()
        {
            driver.ResetForReplay();
            try
            {
                return driver.Drive(ReclaimFight());
            }
            catch (ReplayParkedException)
            {
                return null;
            }
        }

        // The first attempt parks awaiting the first player action.
        Assert.Null(Attempt());
        var live = driver.Current;
        Assert.NotNull(live);

        // Record the play; the replay PARKS on the draw-pile card choice — three candidates remain.
        driver.PlayCard(live!.Hand[0].Id, GoblinId);
        Assert.Null(Attempt());
        var candidates = driver.PendingCardChoice;
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates!.Count);
        Assert.Equal("reclaim a card from your draw pile", driver.PendingCardChoicePurpose);
        Assert.False(driver.Current!.IsOver); // still parked mid-play, not resolved

        // Supply the middle candidate; the replay resumes the play (the pick is exhausted, the goblin blasted) and
        // the fight ends. The pick-moves-the-chosen-card behaviour itself is engine-covered in the Scenario suite.
        driver.SupplyCardChoice(new[] { candidates[1].Id });
        var result = Attempt();

        Assert.NotNull(result);
        Assert.Equal(CombatResult.Victory, result!.Result);
        Assert.Null(driver.PendingCardChoice); // the choice was consumed
        Assert.Null(driver.Current);           // the fight resolved and handed the run its result
    }
}
