using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Tests;

// Phase 1a: the shared visual-editor model of a combat EffectProgram. A modelled leaf must Build into a real
// program and Classify back unchanged (context-generically), and anything outside the subset (composite root,
// result key, arithmetic amount, unlisted selector) must Classify to null so the UI keeps the JSON escape.
public class CombatProgramModelTests
{
    public static IEnumerable<object[]> LeafCases()
    {
        foreach (var kind in CombatProgramModel.NodeKinds.Select(n => n.Kind))
        {
            // removeStatus / cleanse carry no amount, so they don't fit this amount-bearing template — dedicated
            // round-trip tests cover them below.
            if (!CombatProgramModel.UsesAmount(kind))
                continue;
            // ResourceId is part of a resource leaf's identity (gain/lose/modify); StatusId of a status leaf's.
            var resourceId = CombatProgramModel.UsesResourceId(kind) ? "standard.energy" : "";
            var statusId = CombatProgramModel.UsesStatusId(kind) ? "poison" : "";
            foreach (var selector in CombatProgramModel.SelectorKeys)
            {
                yield return [new CombatNodeModel(kind, selector, CombatAmountSpec.FromConst(4), resourceId, StatusId: statusId)];
                yield return [new CombatNodeModel(kind, selector, CombatAmountSpec.Event, resourceId, StatusId: statusId)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(LeafCases))]
    public void Build_then_Classify_round_trips_for_CardPlayContext(CombatNodeModel model)
    {
        var program = CombatProgramModel.Build<CardPlayContext>(model);

        var back = CombatProgramModel.Classify(program);

        Assert.Equal(model, back);
    }

    [Fact]
    public void Classifies_a_program_round_tripped_through_CombatJson()
    {
        // A deserialized program holds fresh (non-singleton) selector instances; classify must still recognise them
        // by type — otherwise a loaded card/relic would never show the visual editor.
        var model = CombatNodeModel.Repeat(
            CombatAmountSpec.FromConst(2),
            new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.Event));
        var options = CombatJson.CreateOptions<CardPlayContext>();

        var program = CombatProgramModel.Build<CardPlayContext>(model);
        var json = JsonSerializer.Serialize(program, options);
        var back = JsonSerializer.Deserialize<EffectProgram<CardPlayContext>>(json, options)!;

        Assert.Equal(model, CombatProgramModel.Classify(back));
    }

    [Fact]
    public void Same_model_builds_for_a_different_context()
    {
        // Context-generic: the identical model closes on EnemyActionContext with no extra work.
        var model = new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(6));

        var program = CombatProgramModel.Build<EnemyActionContext>(model);

        Assert.Equal(model, CombatProgramModel.Classify(program));
        Assert.IsType<DealDamageNode<EnemyActionContext>>(program.Root);
    }

    [Fact]
    public void Classify_returns_null_for_an_unmodelled_node()
    {
        // NoOp is a real node but outside the modelled subset (as is conditional, deferred) → JSON escape.
        var program = new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>());

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_arithmetic_amount()
    {
        var program = new EffectProgram<CardPlayContext>(
            new HealNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new AddExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(1),
                    new ConstantExpression<CardPlayContext>(2))));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_unlisted_selector()
    {
        var program = new EffectProgram<CardPlayContext>(
            new GainBlockNode<CardPlayContext>(
                CombatantTargetSelectors.SourceIncludingDowned, // not in the CombatProgramModel.Selectors catalog
                new ConstantExpression<CardPlayContext>(1)));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void GainResource_round_trips_its_resource_id()
    {
        var model = new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(2), "standard.energy");

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        var node = Assert.IsType<GainResourceNode<CardPlayContext>>(program.Root);
        Assert.Equal("standard.energy", node.ResourceId.value);
        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Resource_leaves_build_their_distinct_nodes_and_carry_the_resource_id()
    {
        // The B2a widening added lose/modify resource alongside gain — each is a distinct native node, so classify
        // stays unambiguous, and the resource id round-trips.
        var lose = new CombatNodeModel("loseResource", "source", CombatAmountSpec.FromConst(1), "standard.energy");
        var modify = new CombatNodeModel("modifyResource", "source", CombatAmountSpec.FromConst(2), "faith");

        var loseProgram = CombatProgramModel.Build<CardPlayContext>(lose);
        var modifyProgram = CombatProgramModel.Build<CardPlayContext>(modify);

        Assert.Equal("standard.energy", Assert.IsType<LoseResourceNode<CardPlayContext>>(loseProgram.Root).ResourceId.value);
        Assert.Equal("faith", Assert.IsType<ModifyResourceNode<CardPlayContext>>(modifyProgram.Root).ResourceId.value);
        Assert.Equal(lose, CombatProgramModel.Classify(loseProgram));
        Assert.Equal(modify, CombatProgramModel.Classify(modifyProgram));
    }

    [Fact]
    public void ApplyStatus_leaf_round_trips_status_id_stacks_duration_and_charges()
    {
        var model = new CombatNodeModel(
            "applyStatus", "eventTarget", CombatAmountSpec.FromConst(3),
            StatusId: "poison", DurationTurns: 2, Charges: 1);

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        var node = Assert.IsType<ApplyStatusNode<CardPlayContext>>(program.Root);
        Assert.Equal("poison", node.StatusDefinitionId.value);
        Assert.Equal(2, node.DurationTurns);
        Assert.Equal(1, node.Charges);
        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Fact]
    public void RemoveStatus_and_cleanse_leaves_round_trip_without_an_amount()
    {
        var remove = new CombatNodeModel("removeStatus", "eventTarget", StatusId: "weak");
        var cleanse = new CombatNodeModel("cleanse", "source", Polarity: StatusPolarity.Debuff);

        var removeProgram = CombatProgramModel.Build<CardPlayContext>(remove);
        var cleanseProgram = CombatProgramModel.Build<CardPlayContext>(cleanse);

        Assert.Equal("weak", Assert.IsType<RemoveStatusNode<CardPlayContext>>(removeProgram.Root).StatusDefinitionId.value);
        Assert.Equal(StatusPolarity.Debuff, Assert.IsType<RemoveStatusesByPolarityNode<CardPlayContext>>(cleanseProgram.Root).Polarity);
        Assert.Equal(remove, CombatProgramModel.Classify(removeProgram));
        Assert.Equal(cleanse, CombatProgramModel.Classify(cleanseProgram));
    }

    [Fact]
    public void Status_leaf_classifies_after_a_CombatJson_round_trip()
    {
        // A status leaf nested in control flow, serialized + deserialized, must still classify (fresh selector +
        // status-id instances by value/type) so a loaded card shows the visual editor.
        var model = CombatNodeModel.Conditional(
            new CombatConditionSpec("hasStatus", "eventTarget", Id: "weak"),
            new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(2), StatusId: "poison", DurationTurns: 3));
        var options = CombatJson.CreateOptions<CardPlayContext>();

        var program = CombatProgramModel.Build<CardPlayContext>(model);
        var json = JsonSerializer.Serialize(program, options);
        var back = JsonSerializer.Deserialize<EffectProgram<CardPlayContext>>(json, options)!;

        Assert.Equal(model, CombatProgramModel.Classify(back));
    }

    [Fact]
    public void ModifyStatus_trio_build_their_nodes_and_round_trip()
    {
        var stacks = new CombatNodeModel("modifyStatusStacks", "eventTarget", CombatAmountSpec.FromConst(2), StatusId: "poison");
        var duration = new CombatNodeModel("modifyStatusDuration", "source", CombatAmountSpec.FromConst(-1), StatusId: "weak");
        var charges = new CombatNodeModel("modifyStatusCharges", "eventTarget", CombatAmountSpec.Event, StatusId: "thorns");

        Assert.IsType<ModifyStatusStacksNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(stacks).Root);
        Assert.IsType<ModifyStatusDurationNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(duration).Root);
        Assert.IsType<ModifyStatusChargesNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(charges).Root);

        foreach (var model in new[] { stacks, duration, charges })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void MoveCards_leaf_round_trips_its_zones_without_an_amount()
    {
        var model = new CombatNodeModel("moveCards", "source", FromZone: CardZone.Hand, ToZone: CardZone.ExhaustPile);

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        var node = Assert.IsType<MoveAllCardsFromZoneNode<CardPlayContext>>(program.Root);
        Assert.Equal(CardZone.Hand, node.FromZone);
        Assert.Equal(CardZone.ExhaustPile, node.ToZone);
        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Health_and_draw_leaves_build_their_nodes()
    {
        var maxHp = CombatProgramModel.Build<CardPlayContext>(CombatProgramModel.NewNode("modifyMaxHealth"));
        var setHp = CombatProgramModel.Build<CardPlayContext>(CombatProgramModel.NewNode("setHealth"));
        var draw = CombatProgramModel.Build<CardPlayContext>(CombatProgramModel.NewNode("drawCards"));

        Assert.IsType<ModifyMaxHealthNode<CardPlayContext>>(maxHp.Root);
        Assert.IsType<SetHealthNode<CardPlayContext>>(setHp.Root);
        Assert.IsType<DrawCardsNode<CardPlayContext>>(draw.Root);
    }

    [Fact]
    public void ChangeKind_into_a_resource_leaf_seeds_a_default_resource_id()
    {
        var node = new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(3));

        Assert.Equal("standard.energy", CombatProgramModel.ChangeKind(node, "loseResource").ResourceId);
        Assert.Equal("standard.energy", CombatProgramModel.ChangeKind(node, "modifyResource").ResourceId);
        // Switching to a non-resource leaf clears it again.
        var withId = CombatProgramModel.ChangeKind(node, "modifyResource");
        Assert.Equal("", CombatProgramModel.ChangeKind(withId, "drawCards").ResourceId);
    }

    // ── Phase 1b: control flow ─────────────────────────────────────────────────────

    public static IEnumerable<object[]> ControlFlowCases()
    {
        var leaf = new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(5));
        var eventLeaf = new CombatNodeModel("gainBlock", "source", CombatAmountSpec.Event);

        yield return [CombatNodeModel.Sequence(new[] { leaf, eventLeaf })];
        yield return [CombatNodeModel.ForEach("allEnemies", leaf)];
        yield return [CombatNodeModel.Repeat(CombatAmountSpec.FromConst(3), leaf)];
        // Nested to depth: repeat { for-each { sequence [ deal, heal ] } }.
        yield return
        [
            CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2),
                CombatNodeModel.ForEach("allEnemies",
                    CombatNodeModel.Sequence(new[]
                    {
                        new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(4)),
                        new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(2)),
                    }))),
        ];
    }

    [Theory]
    [MemberData(nameof(ControlFlowCases))]
    public void Control_flow_round_trips(CombatNodeModel model)
    {
        var program = CombatProgramModel.Build<CardPlayContext>(model);

        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Theory]
    [InlineData("sequence")]
    [InlineData("forEachTarget")]
    [InlineData("repeat")]
    public void NewNode_composite_round_trips(string kind)
    {
        var model = CombatProgramModel.NewNode(kind);

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        Assert.True(CombatProgramModel.IsComposite(kind));
        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Model_equality_is_structural_over_children()
    {
        var a = CombatNodeModel.Sequence(new[] { new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(3)) });
        var b = CombatNodeModel.Sequence(new[] { new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(3)) });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, CombatNodeModel.Sequence(new[] { new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(9)) }));
    }

    [Fact]
    public void Classify_returns_null_when_a_composite_child_is_advanced()
    {
        // A sequence whose second child has an arithmetic (advanced) amount is not fully modelled → JSON escape.
        var program = new EffectProgram<CardPlayContext>(
            new SequenceEffectNode<CardPlayContext>(new IEffectNode<CardPlayContext>[]
            {
                new GainBlockNode<CardPlayContext>(CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(1)),
                new HealNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new AddExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(1), new ConstantExpression<CardPlayContext>(2))),
            }));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_repeat_with_non_default_max_count()
    {
        var program = new EffectProgram<CardPlayContext>(
            new RepeatEffectNode<CardPlayContext>(
                new ConstantExpression<CardPlayContext>(2),
                new GainBlockNode<CardPlayContext>(CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(5)),
                maxCount: 10));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    // ── Phase 1b-cond: conditional + condition spec ─────────────────────────────────

    public static IEnumerable<object[]> ConditionCases()
    {
        yield return [new CombatConditionSpec("compare", "source", "currentHealth", ComparisonOperator.LessOrEqual, 10)];
        yield return [new CombatConditionSpec("compare", "lowestHealthEnemy", "healthPercentage", ComparisonOperator.Less, 50)];
        yield return [new CombatConditionSpec("compare", "source", "currentResource", ComparisonOperator.GreaterOrEqual, 2, "standard.energy")];
        yield return [new CombatConditionSpec("compare", "source", "statusStacks", ComparisonOperator.Greater, 0, "poison")];
        // Conditions read a single target — see SingleTargetSelectorKeys (multi-target reads throw as scalars).
        yield return [new CombatConditionSpec("hasStatus", "lowestHealthEnemy", Id: "poison")];
        yield return [new CombatConditionSpec("isAlive", "source")];
        yield return [new CombatConditionSpec("downed", "lowestHealthEnemy")];
        yield return [new CombatConditionSpec("exists", "highestHealthAlly")];
    }

    [Theory]
    [MemberData(nameof(ConditionCases))]
    public void Condition_round_trips(CombatConditionSpec spec)
    {
        var built = CombatProgramModel.BuildCondition<CardPlayContext>(spec);

        Assert.Equal(spec, CombatProgramModel.ClassifyCondition(built));
    }

    [Fact]
    public void Conditional_node_round_trips_with_then_and_else()
    {
        var model = CombatNodeModel.Conditional(
            new CombatConditionSpec("compare", "source", "missingHealth", ComparisonOperator.GreaterOrEqual, 10),
            new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(6)),
            new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(4)));

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Conditional_node_round_trips_without_else()
    {
        var model = CombatNodeModel.Conditional(
            new CombatConditionSpec("hasStatus", "source", Id: "weak"),
            new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.Event));

        var program = CombatProgramModel.Build<CardPlayContext>(model);
        var back = CombatProgramModel.Classify(program);

        Assert.Equal(model, back);
        Assert.Single(back!.ChildrenOrEmpty); // then only, no else
    }

    [Fact]
    public void NewNode_conditional_round_trips()
    {
        var model = CombatProgramModel.NewNode("conditional");

        Assert.True(CombatProgramModel.IsComposite("conditional"));
        Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void ChangeKind_between_leaves_preserves_selector_and_amount()
    {
        var node = new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.Event);

        var healed = CombatProgramModel.ChangeKind(node, "heal");
        Assert.Equal(new CombatNodeModel("heal", "allEnemies", CombatAmountSpec.Event), healed);

        // gainResource gains a default resource id; switching away clears it.
        var resource = CombatProgramModel.ChangeKind(node, "gainResource");
        Assert.Equal("standard.energy", resource.ResourceId);
        Assert.Equal("", CombatProgramModel.ChangeKind(resource, "gainBlock").ResourceId);
    }

    [Fact]
    public void ChangeKind_between_composites_carries_the_body()
    {
        var body = new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(7));
        var forEach = CombatNodeModel.ForEach("allEnemies", body);

        var repeat = CombatProgramModel.ChangeKind(forEach, "repeat");

        Assert.Equal("repeat", repeat.Kind);
        Assert.Equal(body, Assert.Single(repeat.ChildrenOrEmpty));
    }

    // ── P1: positional (2D-grid) selectors in the authoring catalog ─────────────────

    [Fact]
    public void Positional_selectors_are_in_the_authoring_catalog()
    {
        foreach (var key in new[]
        {
            "adjacent", "sameColumn", "sameRow", "allInColumn", "allInRow",
            "frontmostEnemy", "backmostEnemy", "nearestEnemy", "opposingInColumn",
        })
        {
            Assert.Contains(key, CombatProgramModel.SelectorKeys);
        }

        // The three single-target positional selectors are also valid as scalar condition reads.
        foreach (var key in new[] { "frontmostEnemy", "backmostEnemy", "nearestEnemy" })
            Assert.Contains(key, CombatProgramModel.SingleTargetSelectorKeys);
    }

    [Fact]
    public void Positional_selector_leaf_round_trips_through_build_and_classify()
    {
        // A deal-damage leaf targeting the front of the enemy line — the P1 vocabulary flowing through the editor.
        var model = new CombatNodeModel("dealDamage", "frontmostEnemy", CombatAmountSpec.FromConst(6));

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        Assert.Same(CombatantTargetSelectors.FrontmostEnemyOfSource,
            Assert.IsType<DealDamageNode<CardPlayContext>>(program.Root).TargetSelector);
        Assert.Equal(model, CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Positional_single_target_condition_round_trips()
    {
        var spec = new CombatConditionSpec("compare", "frontmostEnemy", "currentHealth", ComparisonOperator.LessOrEqual, 8);

        var built = CombatProgramModel.BuildCondition<CardPlayContext>(spec);

        Assert.Equal(spec, CombatProgramModel.ClassifyCondition(built));
    }

    [Fact]
    public void Classify_returns_null_for_conditional_with_advanced_condition()
    {
        // An And condition is outside the modelled set → the whole conditional is JSON.
        var program = new EffectProgram<CardPlayContext>(
            new ConditionalEffectNode<CardPlayContext>(
                new AndExpression<CardPlayContext>(
                    new TargetIsAliveExpression<CardPlayContext>(CombatantTargetSelectors.Source),
                    new TargetExistsExpression<CardPlayContext>(CombatantTargetSelectors.Source)),
                new GainBlockNode<CardPlayContext>(CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(3))));

        Assert.Null(CombatProgramModel.Classify(program));
    }
}
