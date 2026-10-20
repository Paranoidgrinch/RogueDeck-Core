using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// A PARTY BUILT FROM THE ROSTER (multiplayer M1): one character per seat, each member carrying its own character's
// start — deck, health, gold, moves, standing rules — and counting as that character when it is offered something.
public class PartyFromRosterTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly StartingStatusSpec Cauldron = new(new StatusDefinitionId("cauldron"), 1);

    private static RunBlueprint Roster() =>
        new RunBlueprint(
            Array.Empty<CardDefinitionId>(),
            new Dictionary<string, EventScript>(),
            Array.Empty<EncounterDefinition>(),
            Array.Empty<CardData>(),
            Array.Empty<EnemyActionData>(),
            new RunMap(Array.Empty<Node>()))
        {
            Characters =
            [
                new RunCharacter("clerk", new RunStart
                {
                    HeroName = "Clerk",
                    MaxHealth = 60,
                    StartingHealth = 60,
                    Resources = new Dictionary<string, int> { [Gold.Value] = 10 },
                    Deck = [new CardDefinitionId("stamp"), new CardDefinitionId("stamp"), new CardDefinitionId("file")],
                }),
                new RunCharacter("witch", new RunStart
                {
                    HeroName = "Witch",
                    MaxHealth = 50,
                    StartingHealth = 45,
                    Resources = new Dictionary<string, int> { [Gold.Value] = 3 },
                    Deck = [new CardDefinitionId("nettle")],
                    CombatActions = [new CardDefinitionId("brew")],
                    CombatStatuses = [Cauldron],
                }, Exclusive: ["nettle"]),
            ],
        };

    [Fact]
    public void Every_seat_is_its_own_character_with_its_own_start()
    {
        var run = Roster().CreateInitialRun(new RunId("run"), party: ["clerk", "witch", "witch"]);

        Assert.Equal(3, run.Party.Count);
        Assert.Equal(["clerk", "witch", "witch"], run.Party.Select(run.CharacterOf));
        Assert.Equal("clerk", run.CharacterId);

        var hero = run.Primary;
        Assert.Equal(3, hero.Deck.Count);
        Assert.Equal(10, hero.GetResource(Gold));
        Assert.Empty(run.CombatActionsOf(hero));

        foreach (var witch in run.Party.Skip(1))
        {
            Assert.Equal(["nettle"], witch.Deck.Select(c => c.DefinitionId.value));
            Assert.Equal((45, 50), (witch.Health.Current, witch.Health.Max));
            Assert.Equal(3, witch.GetResource(Gold));
            Assert.Equal([new CardDefinitionId("brew")], run.CombatActionsOf(witch));
            Assert.Equal([Cauldron], run.CombatStatusesOf(witch));
            Assert.Equal("Witch", witch.DisplayNameKey);
        }
    }

    [Fact]
    public void A_single_seat_is_the_solo_run_of_that_character()
    {
        var run = Roster().CreateInitialRun(new RunId("run"), party: ["witch"]);

        Assert.Single(run.Party);
        Assert.Equal("witch", run.CharacterId);
        Assert.Equal([new CardDefinitionId("brew")], run.CombatActions);
    }

    [Fact]
    public void The_witch_seat_is_offered_the_witchs_cards_and_the_clerk_seat_is_not()
    {
        var run = Roster().CreateInitialRun(new RunId("run"), party: ["clerk", "witch"]);

        Assert.Equal("clerk", CharacterContent.Of(run));
        using (run.PushActiveMember(run.Party[1]))
            Assert.Equal("witch", CharacterContent.Of(run));
        Assert.Equal("clerk", CharacterContent.Of(run));
    }

    [Fact]
    public void Each_members_character_survives_a_save()
    {
        var run = Roster().CreateInitialRun(new RunId("run"), party: ["witch", "clerk", "witch"]);

        var json = RunSaveJson.ToJson(run.Snapshot());
        var back = RunState.Restore(RunSaveJson.FromJson(json), run.Map, content: null);

        Assert.Equal(["witch", "clerk", "witch"], back.Party.Select(back.CharacterOf));
        Assert.Empty(back.CombatActionsOf(back.Party[1]));
        Assert.Equal([new CardDefinitionId("brew")], back.CombatActionsOf(back.Party[0]));
        Assert.Equal([new CardDefinitionId("brew")], back.CombatActionsOf(back.Party[2]));
        Assert.Equal([Cauldron], back.CombatStatusesOf(back.Party[2]));
    }

    // An opening promised to a party member waits for the next fight as THEIRS, across a save.
    [Fact]
    public void A_members_pending_opening_survives_a_save_as_theirs()
    {
        var run = Roster().CreateInitialRun(new RunId("run"), party: ["clerk", "witch"]);
        var rule = new RelicCombatRule { Trigger = "turnStarted", Program = RelicCombatTriggers.Get("turnStarted").NewProgram() };
        run.AddPendingCombatModifier(new HeroOpeningRuleModifier(rule, 2, run.Party[1].Id));
        run.AddPendingCombatModifier(new HeroOpeningRuleModifier(rule));

        var back = RunState.Restore(RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot())), run.Map, content: null);

        var openings = back.ConsumePendingCombatModifiers().Cast<HeroOpeningRuleModifier>().ToList();
        Assert.Equal([run.Party[1].Id, null], openings.Select(o => o.Member));
        var still = Assert.Single(back.ConsumePendingCombatModifiers().Cast<HeroOpeningRuleModifier>());
        Assert.Equal(run.Party[1].Id, still.Member);
    }

    // A member written by hand into a start — the party a blueprint authored before parties came from the roster —
    // counts as the run's character and brings nothing of its own, which is exactly what it did before.
    [Fact]
    public void A_hand_authored_member_counts_as_the_runs_character()
    {
        var blueprint = Roster();
        blueprint = blueprint with
        {
            Characters =
            [
                blueprint.Characters[0] with
                {
                    Start = blueprint.Characters[0].Start with
                    {
                        StartingParty = [new RunMemberData { DisplayNameKey = "Partner", MaxHealth = 20 }],
                    },
                },
            ],
        };
        var run = blueprint.CreateInitialRun(new RunId("run"), characterId: "clerk");

        Assert.Equal("clerk", run.CharacterOf(run.Party[1]));
        Assert.Empty(run.CombatActionsOf(run.Party[1]));
        Assert.Equal((20, 20), (run.Party[1].Health.Current, run.Party[1].Health.Max));
    }
}
