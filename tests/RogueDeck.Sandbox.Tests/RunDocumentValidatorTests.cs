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

    // ── Balance manifest + map-generation validation (map-generation arc, Phase 5) ────────────────────────

    [Fact]
    public void Flags_a_balance_value_pointing_at_no_entity()
    {
        var bp = Valid() with
        {
            Balance = new BalanceManifest { Cards = new Dictionary<string, int> { ["ghost"] = 5 } },
        };
        Assert.Contains(
            RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Balance:", StringComparison.Ordinal) && p.Contains("card 'ghost'"));
    }

    // A blueprint whose map is generated: an empty authored map plus a feasible spec drawing the "fight" encounter.
    private static RunBlueprint Generated() => Valid() with
    {
        Map = new RunMap(System.Array.Empty<Node>()),
        MapGeneration = new MapGenerationSpec
        {
            Rows = 4,
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                    [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                },
            },
        },
    };

    [Fact]
    public void A_generated_map_blueprint_is_clean_and_not_flagged_as_empty()
    {
        var problems = RunDocumentValidator.Validate(Generated());
        Assert.DoesNotContain(problems, p => p.Contains("the map is empty"));
        Assert.DoesNotContain(problems, p => p.StartsWith("Map Rules:", StringComparison.Ordinal));
    }

    [Fact]
    public void Flags_a_generation_role_drawing_an_unknown_encounter()
    {
        var bp = Generated();
        bp = bp with
        {
            MapGeneration = bp.MapGeneration! with
            {
                Encounters = new EncounterDistribution
                {
                    ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                    {
                        [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("phantom")) },
                        [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                    },
                },
            },
        };
        Assert.Contains(
            RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Map Rules:", StringComparison.Ordinal) && p.Contains("phantom"));
    }

    [Fact]
    public void A_valid_generated_blueprint_passes_the_export_gate()
    {
        // Generated() has a non-empty deck and a feasible, content-resolving spec.
        Assert.Empty(RunDocumentValidator.ValidateForExport(Generated()));
    }

    // MultiCombat and Mimic are FIGHTS: they draw from the encounter pools like Combat, and asking them for a
    // node ref would reject a perfectly good act (the Mimic role appears as soon as treasure can bite).
    [Fact]
    public void The_export_gate_accepts_multi_combat_and_mimic_backed_by_encounter_pools()
    {
        var bp = Generated();
        bp = bp with
        {
            MapGeneration = bp.MapGeneration! with
            {
                PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.MultiCombat] = 1 },
                TreasureMimicChancePercent = 10,
                Encounters = new EncounterDistribution
                {
                    ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                    {
                        [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                        [MapNodeKind.MultiCombat] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                        [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                        [MapNodeKind.Mimic] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                    },
                },
            },
        };

        Assert.DoesNotContain(RunDocumentValidator.ValidateForExport(bp),
            p => p.StartsWith("Map Rules:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_export_gate_blocks_a_generation_role_with_no_resolvable_content()
    {
        // A shop can appear (weighted) but no NodeRefs entry realizes it — generation would fail, so export is blocked.
        var bp = Generated();
        bp = bp with
        {
            MapGeneration = bp.MapGeneration! with
            {
                KindWeights = new Dictionary<MapNodeKind, int>
                {
                    [MapNodeKind.Combat] = 5,
                    [MapNodeKind.Shop] = 1,
                },
            },
        };
        Assert.Contains(
            RunDocumentValidator.ValidateForExport(bp),
            p => p.StartsWith("Map Rules:", StringComparison.Ordinal));
    }

    [Fact]
    public void Flags_a_non_combat_role_without_a_node_ref()
    {
        var bp = Generated();
        bp = bp with
        {
            MapGeneration = bp.MapGeneration! with
            {
                // A shop can now appear (weighted), but no NodeRefs entry realizes it.
                KindWeights = new Dictionary<MapNodeKind, int>
                {
                    [MapNodeKind.Combat] = 5,
                    [MapNodeKind.Shop] = 1,
                },
            },
        };
        Assert.Contains(
            RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Map Rules:", StringComparison.Ordinal) && p.Contains("Shop") && p.Contains("NodeRefs"));
    }

    // ── Per-act generation rules ────────────────────────────────────────────────────
    // A multi-act game keeps its rules per act, so the gate has to read every act's spec: the second act is the
    // one nobody sees until an hour in, and a broken spec there is a run that cannot continue.

    [Fact]
    public void Flags_a_later_acts_rules_and_names_the_act()
    {
        var generated = Generated();
        var broken = generated.MapGeneration! with
        {
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("fight")) },
                    [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("phantom")) },
                },
            },
        };
        var bp = generated with
        {
            Acts = new[]
            {
                new RunAct("act-one", generated.MapGeneration),
                new RunAct("act-two", broken),
            },
        };

        Assert.Contains(
            RunDocumentValidator.Validate(bp),
            p => p.StartsWith("Map Rules:", StringComparison.Ordinal)
                && p.Contains("act 'act-two'") && p.Contains("phantom"));
        Assert.NotEmpty(RunDocumentValidator.ValidateForExport(bp));
    }

    [Fact]
    public void A_two_act_blueprint_whose_acts_carry_their_own_rules_passes_the_export_gate()
    {
        var generated = Generated();
        var bp = generated with
        {
            // No blueprint-level spec at all: the acts are the only rules, and an empty authored map is right.
            MapGeneration = null,
            Acts = new[]
            {
                new RunAct("act-one", generated.MapGeneration),
                new RunAct("act-two", generated.MapGeneration! with { Rows = 6 }),
            },
        };

        Assert.Empty(RunDocumentValidator.ValidateForExport(bp));
        Assert.DoesNotContain(RunDocumentValidator.Validate(bp), p => p.Contains("the map is empty"));
    }
}
