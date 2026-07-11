using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// Character selection (content gap): a blueprint may carry a ROSTER of selectable starting characters, each a full
// RunStart (own name / HP / deck / resources / relics / party). CreateInitialRun seeds the run from the chosen
// character; an empty roster keeps the single Start, so existing single-character blueprints are unchanged. The
// engine only models the roster + the pick — the actual characters are content.
public class CharacterSelectionTests
{
    private static readonly CardDefinitionId Strike = new("strike");
    private static readonly CardDefinitionId Zap = new("zap");
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunBlueprint RosterBlueprint() => new(
        Deck: new[] { Strike },   // the shared deck — used only when a character declares none
        Events: new Dictionary<string, EventScript>(),
        Encounters: Array.Empty<EncounterDefinition>(),
        Cards: Array.Empty<CardData>(),
        EnemyActions: Array.Empty<EnemyActionData>(),
        Map: new RunMap(Array.Empty<Node>()))
    {
        Characters = new[]
        {
            new RunCharacter("knight", new RunStart
            {
                HeroName = "Knight",
                MaxHealth = 40,
                StartingHealth = 30,
                Deck = new[] { Strike, Strike, new CardDefinitionId("defend") },
                Resources = new Dictionary<string, int> { [Gold.Value] = 10 },
            }),
            new RunCharacter("mage", new RunStart
            {
                HeroName = "Mage",
                MaxHealth = 25,
                StartingHealth = 20,
                Deck = new[] { Zap, Zap },
                Resources = new Dictionary<string, int> { [Gold.Value] = 99 },
            }),
        },
    };

    [Fact]
    public void Seeds_the_run_from_the_chosen_character()
    {
        var run = RosterBlueprint().CreateInitialRun(new RunId("r"), randomSeed: 1, characterId: "mage");

        Assert.Equal(20, run.Health.Current);
        Assert.Equal(25, run.Health.Max);
        Assert.Equal(99, run.GetResource(Gold));
        Assert.Equal(new[] { Zap, Zap }, run.Deck.Select(c => c.DefinitionId)); // the mage's own deck, not the shared one
    }

    [Fact]
    public void A_null_or_unknown_id_falls_back_to_the_first_character()
    {
        // The knight is roster[0]: HP 30/40, 3-card deck (the mage would be HP 20/25, 2 cards).
        foreach (var id in new[] { null, "does-not-exist" })
        {
            var run = RosterBlueprint().CreateInitialRun(new RunId("r"), characterId: id);
            Assert.Equal(30, run.Health.Current);
            Assert.Equal(40, run.Health.Max);
            Assert.Equal(3, run.Deck.Count);
        }
    }

    [Fact]
    public void An_empty_roster_uses_the_single_start_unchanged()
    {
        var blueprint = new RunBlueprint(
            Deck: new[] { Strike, Strike },
            Events: new Dictionary<string, EventScript>(),
            Encounters: Array.Empty<EncounterDefinition>(),
            Cards: Array.Empty<CardData>(),
            EnemyActions: Array.Empty<EnemyActionData>(),
            Map: new RunMap(Array.Empty<Node>()))
        {
            Start = new RunStart { MaxHealth = 50, StartingHealth = 45 },
        };

        var run = blueprint.CreateInitialRun(new RunId("r"), characterId: "ignored-when-no-roster");

        Assert.Equal(45, run.Health.Current);
        Assert.Equal(50, run.Health.Max);
        Assert.Equal(new[] { Strike, Strike }, run.Deck.Select(c => c.DefinitionId)); // the shared blueprint deck
    }
}
