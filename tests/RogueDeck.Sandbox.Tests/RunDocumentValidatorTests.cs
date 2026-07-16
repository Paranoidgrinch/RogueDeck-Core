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
    public void A_presentation_entry_for_a_defined_entity_is_fine()
    {
        var bp = Valid() with
        {
            Presentation = new PresentationManifest
            {
                Cards = new Dictionary<string, EntityPresentation> { ["strike"] = new() { Art = "strike.png" } },
                Enemies = new Dictionary<string, EntityPresentation> { ["goblin"] = new() { Art = "goblin.png" } },
                Encounters = new Dictionary<string, EntityPresentation> { ["fight"] = new() { Art = "cave.png" } },
                Game = new EntityPresentation { Art = "title.png" },
            },
        };
        Assert.Empty(RunDocumentValidator.Validate(bp));
    }

    [Fact]
    public void Flags_a_presentation_entry_whose_entity_does_not_exist()
    {
        var bp = Valid() with
        {
            Presentation = new PresentationManifest
            {
                Cards = new Dictionary<string, EntityPresentation> { ["ghost"] = new() { Art = "ghost.png" } },
                Relics = new Dictionary<string, EntityPresentation> { ["missing-relic"] = new() },
                Events = new Dictionary<string, EntityPresentation> { ["no-such-event"] = new() },
            },
        };
        var problems = RunDocumentValidator.Validate(bp);
        Assert.Contains(problems, p => p.Contains("card 'ghost'") && p.StartsWith("Cards:"));
        Assert.Contains(problems, p => p.Contains("relic 'missing-relic'") && p.StartsWith("Hero:"));
        Assert.Contains(problems, p => p.Contains("event 'no-such-event'") && p.StartsWith("Run:"));
    }

    // ── Export gate (Godot bridge 3b) ───────────────────────────────────────────────

    [Fact]
    public void A_consistent_document_passes_the_export_gate() =>
        Assert.Empty(RunDocumentValidator.ValidateForExport(Valid()));

    [Fact]
    public void Export_flags_an_empty_starting_deck()
    {
        var bp = Valid() with { Deck = Array.Empty<CardDefinitionId>() };
        Assert.Contains(RunDocumentValidator.ValidateForExport(bp),
            p => p.Contains("starting deck is empty") && p.StartsWith("Run:"));
        // ...but plain Validate stays quiet: an empty deck is fine mid-authoring.
        Assert.Empty(RunDocumentValidator.Validate(bp));
    }

    [Fact]
    public void Export_flags_a_character_whose_effective_deck_is_empty()
    {
        var bp = Valid() with
        {
            Deck = Array.Empty<CardDefinitionId>(),
            Characters = new[]
            {
                new RunCharacter("warrior", new RunStart { Deck = new[] { new CardDefinitionId("strike") } }),
                new RunCharacter("pauper", new RunStart()),
            },
        };
        var problems = RunDocumentValidator.ValidateForExport(bp);
        Assert.Contains(problems, p => p.Contains("character 'pauper'") && p.Contains("empty deck"));
        Assert.DoesNotContain(problems, p => p.Contains("character 'warrior'"));
    }

    [Fact]
    public void Export_flags_a_card_costing_an_undefined_resource()
    {
        var mana = new CardData { Id = "bolt", Costs = new[] { new ResourceCost(new ResourceId("mana"), 2) } };
        var bp = Valid() with { Cards = new[] { new CardData { Id = "strike" }, mana } };
        Assert.Contains(RunDocumentValidator.ValidateForExport(bp),
            p => p.Contains("card 'bolt'") && p.Contains("resource 'mana'"));
    }

    [Fact]
    public void Export_accepts_a_cost_covered_by_a_run_global_combat_resource()
    {
        var mana = new CardData { Id = "bolt", Costs = new[] { new ResourceCost(new ResourceId("mana"), 2) } };
        var bp = Valid() with
        {
            Cards = new[] { new CardData { Id = "strike" }, mana },
            CombatResources = new[] { new CombatResourceData { Id = "mana", Max = 3 } },
        };
        Assert.Empty(RunDocumentValidator.ValidateForExport(bp));
    }

    [Fact]
    public void Export_accepts_a_cost_covered_by_an_encounter_hero_resource()
    {
        var mana = new CardData { Id = "bolt", Costs = new[] { new ResourceCost(new ResourceId("mana"), 2) } };
        var encounter = new EncounterDefinition(
            new EncounterId("fight"),
            new[] { new EncounterEnemy("goblin", 5, new[] { new EnemyActionDefinitionId("jab") }) },
            heroResources: new[] { new ResourceSpec(new ResourceId("mana"), 3, 3) });
        var bp = Valid() with
        {
            Cards = new[] { new CardData { Id = "strike" }, mana },
            Encounters = new[] { encounter },
        };
        Assert.Empty(RunDocumentValidator.ValidateForExport(bp));
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
    public void A_valid_character_roster_has_no_problems()
    {
        var character = new RunCharacter("warrior",
            new RunStart { Deck = new[] { new CardDefinitionId("strike") }, StartingRelics = new[] { "bloodstone" } });
        var bp = Valid() with { Characters = new[] { character } };
        Assert.Empty(RunDocumentValidator.Validate(bp));
    }

    [Fact]
    public void Flags_a_character_deck_card_with_no_definition()
    {
        var character = new RunCharacter("mage", new RunStart { Deck = new[] { new CardDefinitionId("ghost") } });
        var bp = Valid() with { Characters = new[] { character } };
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Characters:") && p.Contains("character 'mage'") && p.Contains("deck card 'ghost'"));
    }

    [Fact]
    public void Flags_a_duplicate_character_id()
    {
        var bp = Valid() with
        {
            Characters = new[] { new RunCharacter("hero", new RunStart()), new RunCharacter("hero", new RunStart()) },
        };
        Assert.Contains(RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Characters:") && p.Contains("duplicate character id 'hero'"));
    }

    [Fact]
    public void Flags_a_map_node_pointing_at_an_unknown_shop()
    {
        var bp = Valid() with
        {
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.ShopNode, new ShopRef(new ShopId("store"))),
            }),
        };
        Assert.Contains(RunDocumentValidator.Validate(bp), p => p.Contains("unknown shop 'store'"));
    }

    [Fact]
    public void A_referenced_shop_that_exists_has_no_shop_problem()
    {
        var shop = new ShopDefinition(System.Array.Empty<ShopEntry>(), OfferCount: 3);
        var bp = Valid() with
        {
            Shops = new Dictionary<string, ShopDefinition> { ["store"] = shop },
            Map = new RunMap(new Node[]
            {
                new(new NodeId("n1"), StandardRunIds.ShopNode, new ShopRef(new ShopId("store"))),
            }),
        };
        Assert.DoesNotContain(RunDocumentValidator.Validate(bp), p => p.Contains("shop"));
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
