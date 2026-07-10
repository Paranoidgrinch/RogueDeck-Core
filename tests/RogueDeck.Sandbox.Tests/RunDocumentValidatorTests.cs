using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Cross-reference validation of the whole run document (deck→cards, enemy→actions, map→encounters/events, dup ids).
public class RunDocumentValidatorTests
{
    // A consistent document: one card ("strike") in a 1-card deck, one action ("jab") run by the lone enemy in the
    // "fight" encounter, and a map with a single combat node pointing at it.
    private static RunBlueprint Valid()
    {
        var strike = new CardData { Id = "strike" };
        var jab = new EnemyActionData { Id = "jab", Intent = new ActionIntent("Jab") };
        var encounter = new EncounterDefinition(
            new EncounterId("fight"),
            new[] { new EncounterEnemy("goblin", 5, new[] { new EnemyActionDefinitionId("jab") }) });
        var map = new RunMap(new Node[]
        {
            new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("fight"))),
        });
        return new RunBlueprint(
            new[] { new CardDefinitionId("strike") }, new Dictionary<string, EventScript>(),
            new[] { encounter }, new[] { strike }, new[] { jab }, map);
    }

    [Fact]
    public void A_consistent_document_has_no_problems() =>
        Assert.Empty(RunDocumentValidator.Validate(Valid()));

    [Fact]
    public void Flags_a_deck_card_with_no_definition()
    {
        var bp = Valid() with { Deck = new[] { new CardDefinitionId("ghost") } };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("card 'ghost'") && p.StartsWith("Cards:"));
    }

    [Fact]
    public void Flags_an_enemy_running_an_undefined_action()
    {
        var bp = Valid() with { EnemyActions = System.Array.Empty<EnemyActionData>() };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("action 'jab'") && p.StartsWith("Encounters:"));
    }

    [Fact]
    public void Flags_a_map_node_pointing_at_an_unknown_encounter()
    {
        var bp = Valid() with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("boss"))),
            }),
        };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("unknown encounter 'boss'"));
    }

    [Fact]
    public void Flags_a_map_node_pointing_at_an_unknown_event()
    {
        var bp = Valid() with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            }),
        };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("unknown event 'shrine'"));
    }

    [Fact]
    public void A_valid_party_member_has_no_problems()
    {
        // The member's deck references the defined "strike" card and it starts with the built-in bloodstone relic.
        var member = new RunMemberData
        {
            DisplayNameKey = "Mage",
            Deck = new[] { "strike" },
            StartingRelics = new[] { "bloodstone" },
        };
        var bp = Valid() with { Start = Valid().Start with { StartingParty = new[] { member } } };
        Assert.Empty(RunDocumentValidator.Validate(bp));
    }

    [Fact]
    public void Flags_a_party_member_deck_card_with_no_definition()
    {
        var member = new RunMemberData { DisplayNameKey = "Mage", Deck = new[] { "ghost" } };
        var bp = Valid() with { Start = Valid().Start with { StartingParty = new[] { member } } };
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Cards:") && p.Contains("deck card 'ghost'") && p.Contains("Mage"));
    }

    [Fact]
    public void Flags_party_member_starting_relics_and_consumables_with_no_definition()
    {
        var member = new RunMemberData
        {
            DisplayNameKey = "Rogue",
            StartingRelics = new[] { "phantom-relic" },
            StartingConsumables = new[] { "phantom-potion" },
        };
        var bp = Valid() with { Start = Valid().Start with { StartingParty = new[] { member } } };
        var problems = RunDocumentValidator.Validate(bp);
        Assert.Contains(problems, p => p.StartsWith("Hero:") && p.Contains("relic 'phantom-relic'"));
        Assert.Contains(problems, p => p.StartsWith("Hero:") && p.Contains("consumable 'phantom-potion'"));
    }

    [Fact]
    public void Flags_a_duplicate_card_id()
    {
        var bp = Valid() with { Cards = new[] { new CardData { Id = "strike" }, new CardData { Id = "strike" } } };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("duplicate card id 'strike'"));
    }

    [Fact]
    public void Flags_an_empty_map()
    {
        var bp = Valid() with { Map = new RunMap(System.Array.Empty<Node>()) };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("map is empty"));
    }

    [Fact]
    public void ForTab_returns_only_that_tabs_problems()
    {
        // A deck-card problem (Cards) and an unknown-encounter problem (Run) at once.
        var bp = Valid() with
        {
            Deck = new[] { new CardDefinitionId("ghost") },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("boss"))),
            }),
        };
        var cards = RunDocumentValidator.ForTab(bp, RunDocumentValidator.CardsTab);
        Assert.All(cards, p => Assert.StartsWith("Cards:", p));
        Assert.Contains(cards, p => p.Contains("card 'ghost'"));
        Assert.DoesNotContain(cards, p => p.Contains("boss"));
    }

    // A map node pointing at the (existing) "fight" encounter, so only graph structure — not content — is at play.
    private static Node Fight(string id) =>
        new(new NodeId(id), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("fight")));

    [Fact]
    public void A_valid_branching_map_has_no_problems()
    {
        var bp = Valid() with
        {
            Map = new RunMap(new[] { Fight("n1"), Fight("n2") })
            {
                Edges = new[] { new MapEdge(new NodeId("n1"), new NodeId("n2")) },
                EntryNodeIds = new[] { new NodeId("n1") },
            },
        };

        Assert.Empty(RunDocumentValidator.Validate(bp));
    }

    [Fact]
    public void A_cyclic_branching_map_is_flagged_under_the_run_tab()
    {
        var bp = Valid() with
        {
            Map = new RunMap(new[] { Fight("n1"), Fight("n2") })
            {
                Edges = new[]
                {
                    new MapEdge(new NodeId("n1"), new NodeId("n2")),
                    new MapEdge(new NodeId("n2"), new NodeId("n1")),
                },
            },
        };

        Assert.Contains(
            RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Run:", StringComparison.Ordinal) && p.Contains("cycle"));
    }

    [Fact]
    public void An_edge_to_a_missing_node_is_flagged_under_the_run_tab()
    {
        var bp = Valid() with
        {
            Map = new RunMap(new[] { Fight("n1") })
            {
                Edges = new[] { new MapEdge(new NodeId("n1"), new NodeId("ghost")) },
            },
        };

        Assert.Contains(
            RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Run:", StringComparison.Ordinal) && p.Contains("unknown target node 'ghost'"));
    }
}
