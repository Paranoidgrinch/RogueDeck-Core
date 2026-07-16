using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Dsl;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run.Tests;

// The composition compiler (S2): deterministic derived ids, the sibling cost-modifier math
// (scope × op, floor, clamp, resource filter, application order), fragment order in the composed
// program, and the unordered recipe matcher.
public class ShredCardSynthesizerTests
{
    private static readonly string Energy = StandardCombatIds.EnergyResource.value;

    private static ShredData Part(string id, int size = 2, int energy = 1,
        IReadOnlyList<ShredModifier>? modifiers = null, bool withProgram = true) =>
        new(id, id.ToUpperInvariant(), size,
            energy > 0 ? new[] { new ResourceCost(StandardCombatIds.EnergyResource, energy) } : [],
            withProgram ? Effects.Program(Effects.GainBlock(Targets.Source, 3)) : null)
        {
            Modifiers = modifiers ?? [],
        };

    private static int EnergyCost(Scenario.Authoring.CardBlueprint card) =>
        card.Costs.Where(c => c.ResourceId == StandardCombatIds.EnergyResource).Sum(c => c.Amount);

    [Fact]
    public void The_derived_id_is_the_ordered_part_list_and_is_deterministic()
    {
        Assert.Equal("shred:a+b+c", ShredCardSynthesizer.DerivedId(new[] { "a", "b", "c" }));
        Assert.NotEqual(
            ShredCardSynthesizer.DerivedId(new[] { "a", "b" }),
            ShredCardSynthesizer.DerivedId(new[] { "b", "a" }));
    }

    [Fact]
    public void Same_parts_synthesize_identical_cards()
    {
        var parts = new[] { Part("a"), Part("b", energy: 2) };

        Assert.True(ShredCardSynthesizer.TrySynthesize(parts, out var first, out _));
        Assert.True(ShredCardSynthesizer.TrySynthesize(parts, out var second, out _));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.NameKey, second.NameKey);
        Assert.Equal(first.Costs, second.Costs);
        Assert.Equal("shred:a+b", first.Id);
        Assert.Equal("A + B", first.NameKey);
        Assert.Equal(3, EnergyCost(first));
    }

    [Fact]
    public void A_below_scope_percent_modifier_halves_later_parts_only()
    {
        // Part 0 halves everything below it; its own cost is untouched.
        var parts = new[]
        {
            Part("head", energy: 3,
                modifiers: new[] { new ShredModifier(ShredModifierScope.Below, ShredModifierOp.CostFactorPercent, 50) }),
            Part("body", energy: 3), // 3 -> 1 (floor of 1.5)
            Part("tail", energy: 2), // 2 -> 1
        };

        Assert.True(ShredCardSynthesizer.TrySynthesize(parts, out var card, out _));
        Assert.Equal(3 + 1 + 1, EnergyCost(card));
    }

    [Fact]
    public void Scopes_target_the_right_siblings()
    {
        // Above: the last part discounts everything before it by 1.
        var above = new[]
        {
            Part("a", energy: 2),
            Part("b", energy: 2),
            Part("z", energy: 2,
                modifiers: new[] { new ShredModifier(ShredModifierScope.Above, ShredModifierOp.CostDelta, -1) }),
        };
        Assert.True(ShredCardSynthesizer.TrySynthesize(above, out var aboveCard, out _));
        Assert.Equal(1 + 1 + 2, EnergyCost(aboveCard));

        // Others: middle part discounts both neighbours, not itself.
        var others = new[]
        {
            Part("a", energy: 2),
            Part("m", energy: 2,
                modifiers: new[] { new ShredModifier(ShredModifierScope.Others, ShredModifierOp.CostDelta, -1) }),
            Part("c", energy: 2),
        };
        Assert.True(ShredCardSynthesizer.TrySynthesize(others, out var othersCard, out _));
        Assert.Equal(1 + 2 + 1, EnergyCost(othersCard));

        // All: includes the carrier itself.
        var all = new[]
        {
            Part("a", energy: 2,
                modifiers: new[] { new ShredModifier(ShredModifierScope.All, ShredModifierOp.CostDelta, -1) }),
            Part("b", energy: 2),
        };
        Assert.True(ShredCardSynthesizer.TrySynthesize(all, out var allCard, out _));
        Assert.Equal(1 + 1, EnergyCost(allCard));
    }

    [Fact]
    public void Cost_never_goes_below_zero_and_zero_entries_are_dropped()
    {
        var parts = new[]
        {
            Part("a", energy: 1,
                modifiers: new[] { new ShredModifier(ShredModifierScope.All, ShredModifierOp.CostDelta, -5) }),
            Part("b", energy: 2),
        };
        Assert.True(ShredCardSynthesizer.TrySynthesize(parts, out var card, out _));
        Assert.Empty(card.Costs);
    }

    [Fact]
    public void A_resource_filter_narrows_the_modifier_to_one_resource()
    {
        var mana = new ResourceId("mana");
        var dual = new ShredData("dual", "Dual", 2,
            new[] { new ResourceCost(StandardCombatIds.EnergyResource, 2), new ResourceCost(mana, 2) },
            Effects.Program(Effects.GainBlock(Targets.Source, 1)));
        var discounter = Part("disc", energy: 0,
            modifiers: new[] { new ShredModifier(ShredModifierScope.Below, ShredModifierOp.CostDelta, -1, "mana") });

        Assert.True(ShredCardSynthesizer.TrySynthesize(new[] { discounter, dual }, out var card, out _));

        Assert.Equal(2, card.Costs.Single(c => c.ResourceId == StandardCombatIds.EnergyResource).Amount);
        Assert.Equal(1, card.Costs.Single(c => c.ResourceId == mana).Amount);
    }

    [Fact]
    public void Modifiers_apply_in_part_order()
    {
        // Part 0 halves below (3 -> 1), then part 1 (the target itself) has no modifier; but a LATER
        // discounter's delta applies after the earlier factor: (3 -> 1) then -1 => 0, not floor((3-1)/2)=1.
        var parts = new[]
        {
            Part("halver", energy: 0,
                modifiers: new[] { new ShredModifier(ShredModifierScope.Below, ShredModifierOp.CostFactorPercent, 50) }),
            Part("payload", energy: 3),
            Part("discounter", energy: 0,
                modifiers: new[] { new ShredModifier(ShredModifierScope.Above, ShredModifierOp.CostDelta, -1) }),
        };
        Assert.True(ShredCardSynthesizer.TrySynthesize(parts, out var card, out _));
        Assert.Equal(0, EnergyCost(card));
    }

    [Fact]
    public void The_composed_program_runs_fragments_in_arrangement_order()
    {
        var first = new ShredData("first", "First", 1, [],
            Effects.Program(Effects.GainBlock(Targets.Source, 1)));
        var second = new ShredData("second", "Second", 1, [],
            Effects.Program(Effects.DealDamage(Targets.EventTarget, 2)));

        Assert.True(ShredCardSynthesizer.TrySynthesize(new[] { first, second }, out var card, out _));

        var sequence = Assert.IsType<SequenceEffectNode<CardPlayContext>>(card.Program!.Root);
        Assert.Equal(2, sequence.Children.Count);
        Assert.Same(first.Program!.Root, sequence.Children[0]);
        Assert.Same(second.Program!.Root, sequence.Children[1]);
    }

    [Fact]
    public void A_single_fragment_keeps_its_root_without_a_wrapper()
    {
        var only = new ShredData("only", "Only", 1, [], Effects.Program(Effects.GainBlock(Targets.Source, 2)));
        Assert.True(ShredCardSynthesizer.TrySynthesize(new[] { only }, out var card, out _));
        Assert.Same(only.Program!.Root, card.Program!.Root);
    }

    [Fact]
    public void Cost_only_parts_synthesize_a_programless_card()
    {
        Assert.True(ShredCardSynthesizer.TrySynthesize(
            new[] { Part("weight", withProgram: false) }, out var card, out _));
        Assert.Null(card.Program);
        Assert.Equal(1, EnergyCost(card));
    }

    [Fact]
    public void An_empty_part_list_is_not_buildable()
    {
        Assert.False(ShredCardSynthesizer.TrySynthesize(Array.Empty<ShredData>(), out _, out var error));
        Assert.Contains("at least one", error);
    }

    [Fact]
    public void Tags_union_in_first_occurrence_order()
    {
        var a = Part("a") with { Tags = ["block", "iron"] };
        var b = Part("b") with { Tags = ["iron", "fire"] };
        Assert.True(ShredCardSynthesizer.TrySynthesize(new[] { a, b }, out var card, out _));
        Assert.Equal(new[] { new TagId("block"), new TagId("iron"), new TagId("fire") }, card.Tags);
    }

    // ── RecipeMatcher ────────────────────────────────────────────────────────────

    [Fact]
    public void Recipes_match_as_unordered_multisets()
    {
        var recipes = new[]
        {
            new RecipeData("expert-parry", new[] { "guard", "guard", "ember" }, "parry-card"),
        };

        Assert.NotNull(RecipeMatcher.Match(recipes, new[] { "ember", "guard", "guard" }));
        Assert.NotNull(RecipeMatcher.Match(recipes, new[] { "guard", "ember", "guard" }));

        // Near misses: wrong count of a duplicate, extra part, missing part.
        Assert.Null(RecipeMatcher.Match(recipes, new[] { "guard", "ember" }));
        Assert.Null(RecipeMatcher.Match(recipes, new[] { "guard", "guard", "guard", "ember" }));
        Assert.Null(RecipeMatcher.Match(recipes, new[] { "guard", "guard", "spark" }));
    }

    [Fact]
    public void The_first_matching_recipe_wins()
    {
        var recipes = new[]
        {
            new RecipeData("first", new[] { "a", "b" }, "card-1"),
            new RecipeData("second", new[] { "b", "a" }, "card-2"),
        };
        Assert.Equal("first", RecipeMatcher.Match(recipes, new[] { "a", "b" })!.Id);
    }
}
