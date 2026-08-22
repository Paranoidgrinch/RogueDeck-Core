using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Counting the cards in a zone could ask what KIND of card they are (the definition's tag) but not what has
// been DONE to them (the per-instance mark). A rule that marks cards almost always needs to count them again
// later — "how many are still Misfiled", "are two Words still unspoken" — so the count now asks both.
public class ZoneCardCountMarkTests
{
    private static readonly TagId Chosen = new("chosen");
    private static readonly TagId Deed = new("deed");

    // Marking two of five cards and then counting by that mark answers two, not five.
    [Fact]
    public void A_zone_can_be_counted_by_what_was_done_to_its_cards()
    {
        using var play = Start(markCount: 2, tagged: true, countTag: null);
        Play(play);
        Assert.Equal(2, Block(play));
    }

    // The two filters compose: cards that are Deeds AND marked. Here nothing is a Deed, so the same two marked
    // cards count as zero.
    [Fact]
    public void The_mark_and_the_kind_are_both_asked_when_both_are_given()
    {
        using var play = Start(markCount: 2, tagged: false, countTag: Deed);
        Play(play);
        Assert.Equal(0, Block(play));

        using var deeds = Start(markCount: 2, tagged: true, countTag: Deed);
        Play(deeds);
        Assert.Equal(2, Block(deeds));
    }

    // No mark given means the whole zone, exactly as before — the field is additive, not a behaviour change.
    [Fact]
    public void Asking_for_no_mark_still_counts_the_whole_zone()
    {
        using var play = Start(markCount: 0, tagged: true, countTag: null, countMark: false);
        Play(play);
        // The whole opening hand — five, including the counting card itself, which is still in hand while its
        // own program runs.
        Assert.Equal(5, Block(play));
    }

    private static void Play(RunPlayback play)
    {
        var combat = play.CombatDriver!.Current!;
        var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId).Id;
        play.CombatDriver.PlayCard(combat.Hand.First(c => c.DefinitionId == new CardDefinitionId("tally")).Id, enemy);
        Assert.Null(play.Session!.Error);
    }

    private static int Block(RunPlayback play)
    {
        var combat = play.CombatDriver!.Current!;
        return combat.State.GetCombatant(combat.HeroId)
            .DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;
    }

    private static RunPlayback Start(int markCount, bool tagged, TagId? countTag, bool countMark = true)
    {
        var play = new RunPlayback(() => { });
        play.Start(Duel(markCount, tagged, countTag, countMark), seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        return play;
    }

    // One card: mark N cards in hand, then gain Block equal to the counted cards.
    private static RunBlueprint Duel(int markCount, bool tagged, TagId? countTag, bool countMark)
    {
        var steps = new List<IEffectNode<CardPlayContext>>();
        if (markCount > 0)
            steps.Add(new ForEachCardInZoneNode<CardPlayContext>(
                CombatantTargetSelectors.Source, CardZone.Hand,
                new MarkCardInstanceNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new IteratedCardExpression<CardPlayContext>(), Chosen),
                takeFirst: markCount));
        steps.Add(new GainBlockNode<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new CombatantZoneCardCountExpression<CardPlayContext>(
                CombatantTargetSelectors.Source, CardZone.Hand,
                tag: countTag, mark: countMark ? Chosen : null)));

        var tally = new CardData
        {
            Id = "tally",
            NameKey = "Tally",
            Costs = Array.Empty<ResourceCost>(),
            Program = new EffectProgram<CardPlayContext>(new CausalSequenceEffectNode<CardPlayContext>(steps)),
        };
        var filler = new CardData
        {
            Id = "filler",
            NameKey = "Filler",
            Costs = Array.Empty<ResourceCost>(),
            Tags = tagged ? [Deed] : [],
            Program = CombatProgramModel.Build<CardPlayContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
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

        // The tally sits on top so it is always in the opening hand, with fillers behind it.
        return new RunBlueprint(
            [new CardDefinitionId("tally"), .. Enumerable.Repeat(new CardDefinitionId("filler"), 11)],
            new Dictionary<string, EventScript>(),
            [duel], [tally, filler], [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Start = new RunStart { HeroName = "Counter", MaxHealth = 40, StartingHealth = 40 },
        };
    }
}
