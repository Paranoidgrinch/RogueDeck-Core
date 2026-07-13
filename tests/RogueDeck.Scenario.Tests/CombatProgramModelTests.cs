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
            // The status-selection ops carry a StatusSelectionSpec (not an id) and have a dedicated round-trip test;
            // the amount-only template here would build them without a selection and mismatch.
            if (CombatProgramModel.UsesStatusSelection(kind))
                continue;
            // Likewise the resource-selection op (ResourceSelectionSpec), the defensive-pool op (pool id) and the
            // counter op (counter id) have their own round-trip tests below; the amount-only template does not fit them.
            if (CombatProgramModel.UsesResourceSelection(kind) || CombatProgramModel.UsesPoolId(kind)
                || CombatProgramModel.UsesCounterId(kind))
                continue;
            // Card-selecting leaves carry a CombatCardSpec (not fit for the amount-only template); dedicated tests cover them.
            if (CombatProgramModel.UsesCard(kind))
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
    public void RepeatUntil_and_randomTargets_composites_round_trip()
    {
        // Phase 1h: repeat-until (stop condition + body) and random-targets (candidate selector + count + body).
        var repeatUntil = CombatNodeModel.RepeatUntil(
            new CombatConditionSpec("compare", "source", "currentResource", ComparisonOperator.LessOrEqual, 0, "standard.energy"),
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3)));
        var randomTargets = CombatNodeModel.RandomTargets(
            "allEnemies", CombatAmountSpec.FromConst(2),
            new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison"));

        Assert.IsType<RepeatUntilEffectNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(repeatUntil).Root);
        Assert.IsType<RandomTargetSelectionNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(randomTargets).Root);

        foreach (var model in new[] { repeatUntil, randomTargets })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void The_new_whole_board_selectors_round_trip()
    {
        // Phase 1h: allCombatants + allDamagedAllies are now in the authoring catalog.
        foreach (var key in new[] { "allCombatants", "allDamagedAllies" })
        {
            var model = new CombatNodeModel("dealDamage", key, CombatAmountSpec.FromConst(4));
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
        }
    }

    [Fact]
    public void Play_and_replay_card_ops_round_trip_with_and_without_a_target()
    {
        // Phase 1g part 2: playCard (optional card target) + replayCardProgram (a card's program at a target).
        var playTargeted = new CombatNodeModel("playCard", "source", Card: new CombatCardSpec("chosen", CardZone.Hand), HasCardTarget: true, ToSelectorKey: "eventTarget");
        var playUntargeted = new CombatNodeModel("playCard", "source", Card: new CombatCardSpec("random", CardZone.Hand), HasCardTarget: false);
        var replay = new CombatNodeModel("replayCardProgram", "eventTarget", Card: new CombatCardSpec("chosen", CardZone.Hand));

        var targeted = Assert.IsType<PlayCardNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(playTargeted).Root);
        Assert.NotNull(targeted.CardTargetSelector);
        Assert.Null(Assert.IsType<PlayCardNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(playUntargeted).Root).CardTargetSelector);
        Assert.IsType<ReplayCardProgramNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(replay).Root);

        foreach (var model in new[] { playTargeted, playUntargeted, replay })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void Create_card_ops_build_their_nodes_and_round_trip()
    {
        // Phase 1g: create card(s) by definition into a zone, and copy a selected card into a zone (count via Amount).
        var create = new CombatNodeModel("createCardInstance", "source", CombatAmountSpec.FromConst(2), ToDefinition: "wound", ToZone: CardZone.DiscardPile);
        var copy = new CombatNodeModel("createCardCopy", "source", CombatAmountSpec.FromConst(1), Card: new CombatCardSpec("chosen", CardZone.Hand), ToZone: CardZone.Hand);

        var createNode = Assert.IsType<CreateCardInstanceNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(create).Root);
        Assert.Equal("wound", createNode.CardDefinitionId.value);
        Assert.Equal(CardZone.DiscardPile, createNode.ToZone);
        Assert.Equal(CardZone.Hand, Assert.IsType<CreateCardCopyNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(copy).Root).ToZone);

        foreach (var model in new[] { create, copy })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void Summon_combatant_round_trips_core_position_and_starting_statuses()
    {
        // Phase 1f (summon slice): team + definition + name + max HP, an optional grid position, and a status list.
        var bare = new CombatNodeModel(
            "summonCombatant", "source", CombatAmountSpec.FromConst(20),
            TeamId: "enemies", SummonDefinitionId: "skeleton", SummonDisplayName: "Skeleton");
        var full = bare with
        {
            PositionX = 1,
            PositionY = 2,
            StartingStatuses = new[] { new StatusGrant(new StatusDefinitionId("poison"), 3, 0, 0), new StatusGrant(new StatusDefinitionId("strength"), 2) },
        };

        var node = Assert.IsType<SummonCombatantNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(full).Root);
        Assert.Equal("enemies", node.TeamId.value);
        Assert.Equal("skeleton", node.DefinitionId.value);
        Assert.Equal(new CombatPosition(1, 2), node.Position);
        Assert.Equal(2, node.StartingStatuses.Count);
        Assert.Empty(Assert.IsType<SummonCombatantNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(bare).Root).StartingStatuses);

        foreach (var model in new[] { bare, full })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void Summon_classifies_after_a_CombatJson_round_trip()
    {
        var model = new CombatNodeModel(
            "summonCombatant", "source", CombatAmountSpec.FromConst(15),
            TeamId: "enemies", SummonDefinitionId: "slime", SummonDisplayName: "Slime",
            PositionX: 0, PositionY: 1, StartingStatuses: new[] { new StatusGrant(new StatusDefinitionId("weak"), 2) });
        var options = CombatJson.CreateOptions<CardPlayContext>();

        var program = CombatProgramModel.Build<CardPlayContext>(model);
        var json = JsonSerializer.Serialize(program, options);
        var back = JsonSerializer.Deserialize<EffectProgram<CardPlayContext>>(json, options)!;

        Assert.Equal(model, CombatProgramModel.Classify(back));
    }

    [Fact]
    public void Combat_control_ops_build_their_nodes_and_round_trip()
    {
        // Phase 1f: lifecycle state / team change (targeted) + set-result / remove-temporary-rule (combat-global).
        var lifecycle = new CombatNodeModel("setCombatantLifecycleState", "eventTarget", LifecycleState: CombatantLifecycleState.Downed);
        var team = new CombatNodeModel("changeCombatantTeam", "eventTarget", TeamId: "players");
        var result = new CombatNodeModel("setCombatResult", "source", CombatResult: CombatResult.Victory);
        var rule = new CombatNodeModel("removeTemporaryRule", "source", RuleId: "rule.enrage");

        Assert.Equal(CombatantLifecycleState.Downed, Assert.IsType<SetCombatantLifecycleStateNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(lifecycle).Root).LifecycleState);
        Assert.Equal("players", Assert.IsType<ChangeCombatantTeamNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(team).Root).TeamId.value);
        Assert.Equal(CombatResult.Victory, Assert.IsType<SetCombatResultNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(result).Root).Result);
        Assert.Equal("rule.enrage", Assert.IsType<RemoveTemporaryRuleNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(rule).Root).RuleId.value);

        foreach (var model in new[] { lifecycle, team, result, rule })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Theory]
    [InlineData(MovementMode.TowardEnemies)]
    [InlineData(MovementMode.AwayFromEnemies)]
    [InlineData(MovementMode.PushFromSource)]
    [InlineData(MovementMode.PullToSource)]
    public void MoveCombatant_relative_modes_round_trip_with_a_step(MovementMode mode)
    {
        var model = new CombatNodeModel("moveCombatant", "eventTarget", MovementMode: mode, MoveStep: CombatAmountSpec.FromConst(2));

        var node = Assert.IsType<MoveCombatantNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(model).Root);
        Assert.Equal(mode, node.Mode);
        Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void MoveCombatant_absolute_round_trips_its_coordinates_and_swap_round_trips_two_selectors()
    {
        // Phase 1e: absolute move (x/y) + swap positions (two selectors, the second via ToSelectorKey).
        var absolute = new CombatNodeModel(
            "moveCombatant", "source", MovementMode: MovementMode.ToAbsolute,
            MoveX: CombatAmountSpec.FromConst(1), MoveY: CombatAmountSpec.FromConst(2));
        var swap = new CombatNodeModel("swapPositions", "source", ToSelectorKey: "eventTarget");

        var move = Assert.IsType<MoveCombatantNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(absolute).Root);
        Assert.Equal(MovementMode.ToAbsolute, move.Mode);
        var swapNode = Assert.IsType<SwapPositionsNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(swap).Root);
        Assert.Equal("eventTarget", CombatProgramModel.Classify(new EffectProgram<CardPlayContext>(swapNode))!.ToSelectorKey);

        foreach (var model in new[] { absolute, swap })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void SetCombatantCounter_round_trips_its_counter_id_and_relative_flag()
    {
        // Phase 1d: setCombatantCounter writes a per-fight counter (relative add or absolute set).
        var relative = new CombatNodeModel("setCombatantCounter", "source", CombatAmountSpec.FromConst(1), CounterId: "combo", Relative: true);
        var absolute = new CombatNodeModel("setCombatantCounter", "source", CombatAmountSpec.FromConst(0), CounterId: "combo", Relative: false);

        var node = Assert.IsType<SetCombatantCounterNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(relative).Root);
        Assert.Equal("combo", node.CounterId.value);
        Assert.True(node.Relative);
        Assert.False(Assert.IsType<SetCombatantCounterNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(absolute).Root).Relative);

        foreach (var model in new[] { relative, absolute })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void A_counter_read_is_a_first_class_amount_source()
    {
        // "deal damage equal to your combo counter" — the counter amount builds a CombatantCounterExpression and
        // classifies back, so it is no longer a JSON escape.
        var model = new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.Counter("source", "combo"));

        var node = Assert.IsType<DealDamageNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(model).Root);
        var counter = Assert.IsType<CombatantCounterExpression<CardPlayContext>>(node.Amount);
        Assert.Equal("combo", counter.CounterId.value);
        Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void A_counter_amount_classifies_after_a_CombatJson_round_trip()
    {
        // The counter amount must survive being saved into a blueprint: serialize + deserialize, then classify.
        var model = new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.Counter("source", "combo"));
        var options = CombatJson.CreateOptions<CardPlayContext>();

        var program = CombatProgramModel.Build<CardPlayContext>(model);
        var json = JsonSerializer.Serialize(program, options);
        var back = JsonSerializer.Deserialize<EffectProgram<CardPlayContext>>(json, options)!;

        Assert.Equal(model, CombatProgramModel.Classify(back));
    }

    [Fact]
    public void DealDamage_round_trips_its_element_and_ignores_block()
    {
        // Phase 1c: the element + pierce flag were previously JSON-escapes; now they round-trip through the model.
        var fire = new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(8), Element: "fire");
        var pierce = new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6), IgnoresBlock: true);

        var fireNode = Assert.IsType<DealDamageNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(fire).Root);
        Assert.Equal("fire", fireNode.Element!.Value.value);
        Assert.True(Assert.IsType<DealDamageNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(pierce).Root).IgnoresBlock);

        foreach (var model in new[] { fire, pierce })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void Untyped_non_piercing_damage_still_round_trips_with_defaults()
    {
        // The common case (no element, respects block) must round-trip unchanged after 1c widened the node.
        var plain = new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(6));
        var node = Assert.IsType<DealDamageNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(plain).Root);

        Assert.Null(node.Element);
        Assert.False(node.IgnoresBlock);
        Assert.Equal(plain, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(plain)));
    }

    [Fact]
    public void Resource_completeness_ops_build_their_nodes_and_round_trip()
    {
        // Phase 1b: refill / modifyDefensivePool / modifySelectedResource + gainResource.DefaultMax + modifyResource.Min/Max.
        var refill = new CombatNodeModel("refillResource", "source", ResourceId: "standard.energy", DefaultMax: 3);
        var pool = new CombatNodeModel("modifyDefensivePool", "source", CombatAmountSpec.FromConst(5), PoolId: "block");
        var selected = new CombatNodeModel(
            "modifySelectedResource", "eventTarget", CombatAmountSpec.FromConst(-2),
            ResourceSelection: new ResourceSelectionSpec(ResourcePoolFilter.NonEmpty, ResourcePick.Highest));
        var cappedGain = new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(2), "standard.energy", DefaultMax: 9);
        var clampedModify = new CombatNodeModel("modifyResource", "source", CombatAmountSpec.FromConst(1), "faith", Min: 0, Max: 5);

        Assert.Equal(3, Assert.IsType<RefillResourceNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(refill).Root).DefaultMax);
        Assert.Equal("block", Assert.IsType<ModifyDefensivePoolNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(pool).Root).PoolId.value);
        Assert.Equal(ResourcePick.Highest, Assert.IsType<ModifySelectedResourceNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(selected).Root).Selection.Pick);
        Assert.Equal(9, Assert.IsType<GainResourceNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(cappedGain).Root).DefaultMax);
        var mr = Assert.IsType<ModifyResourceNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(clampedModify).Root);
        Assert.Equal((0, 5), (mr.Min, mr.Max));

        foreach (var model in new[] { refill, pool, selected, cappedGain, clampedModify })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void Uncapped_gain_and_unclamped_modify_still_round_trip_with_null_bounds()
    {
        // The optional bounds default to null (uncapped / unclamped) — the pre-1b behaviour must round-trip unchanged.
        var gain = new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(2), "standard.energy");
        var modify = new CombatNodeModel("modifyResource", "source", CombatAmountSpec.FromConst(1), "faith");

        Assert.Null(Assert.IsType<GainResourceNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(gain).Root).DefaultMax);
        Assert.Equal(gain, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(gain)));
        Assert.Equal(modify, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(modify)));
    }

    [Fact]
    public void Status_selection_ops_build_their_nodes_and_round_trip()
    {
        // Phase 1a: the status-instance ops (#2/#3) pick ONE status by a StatusSelectionSpec instead of naming an id.
        var remove = new CombatNodeModel(
            "removeSelectedStatus", "eventTarget",
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff, StatusPick.Random));
        var modify = new CombatNodeModel(
            "modifySelectedStatusStacks", "eventTarget", CombatAmountSpec.FromConst(-2),
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Debuff, StatusPick.First, Index: 1));
        var steal = new CombatNodeModel(
            "stealSelectedStatus", "eventTarget",
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff), ToSelectorKey: "source");

        var removeNode = Assert.IsType<RemoveSelectedStatusNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(remove).Root);
        Assert.Equal(StatusPolarityFilter.Buff, removeNode.Selection.Polarity);
        var modifyNode = Assert.IsType<ModifySelectedStatusStacksNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(modify).Root);
        Assert.Equal(1, modifyNode.Selection.Index);
        var stealNode = Assert.IsType<StealSelectedStatusNode<CardPlayContext>>(CombatProgramModel.Build<CardPlayContext>(steal).Root);
        Assert.Equal("eventTarget", CombatProgramModel.Classify(new EffectProgram<CardPlayContext>(stealNode))!.SelectorKey);

        foreach (var model in new[] { remove, modify, steal })
            Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void Status_selection_op_classifies_after_a_CombatJson_round_trip()
    {
        // A deserialized steal holds fresh from/to selector + spec instances; classify must recognise them by type.
        var model = new CombatNodeModel(
            "stealSelectedStatus", "eventTarget",
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff, StatusPick.First, Index: 2), ToSelectorKey: "lowestHealthAlly");
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

    // ── card targeting: the selector widget + targeted card ops in the visual editor ──

    [Fact]
    public void Card_ops_and_forEachCardInZone_are_in_the_palette()
    {
        var kinds = CombatProgramModel.AllKinds.Select(k => k.Kind).ToList();
        Assert.Contains("moveCardToZone", kinds);
        Assert.Contains("transformCard", kinds);
        Assert.Contains("forEachCardInZone", kinds);
        Assert.True(CombatProgramModel.IsComposite("forEachCardInZone"));
    }

    public static IEnumerable<object[]> CardSpecs() => new[]
    {
        new object[] { new CombatCardSpec("inZone", CardZone.DrawPile, 2) },
        new object[] { new CombatCardSpec("chosen", CardZone.Hand, Purpose: "upgrade a card") },
        new object[] { new CombatCardSpec("random", CardZone.DiscardPile) },
        new object[] { new CombatCardSpec("iterated") },
    };

    [Theory]
    [MemberData(nameof(CardSpecs))]
    public void MoveCardToZone_round_trips_each_card_selector(CombatCardSpec card)
    {
        var model = new CombatNodeModel("moveCardToZone", "source", Card: card, ToZone: CardZone.ExhaustPile);

        Assert.Equal(model, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model)));
    }

    [Fact]
    public void MoveCardToZone_round_trips_the_top_placement()
    {
        var model = new CombatNodeModel("moveCardToZone", "source",
            Card: new CombatCardSpec("chosen", CardZone.Hand), ToZone: CardZone.DrawPile, Placement: ZonePlacement.Top);

        var back = CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model));

        Assert.Equal(model, back);
        Assert.Equal(ZonePlacement.Top, back!.Placement);
    }

    [Theory]
    [MemberData(nameof(CardSpecs))]
    public void TransformCard_round_trips_each_card_selector_and_its_target_definition(CombatCardSpec card)
    {
        var model = new CombatNodeModel("transformCard", "source", Card: card, ToDefinition: "strike.plus");

        var back = CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(model));

        Assert.Equal(model, back);
        Assert.Equal("strike.plus", back!.ToDefinition);
    }

    [Fact]
    public void ForEachCardInZone_round_trips_with_and_without_a_filter()
    {
        // "upgrade every Strike in hand": forEachCardInZone (filter strike) over a transformCard body on the loop card.
        var filtered = CombatNodeModel.ForEachCard(
            "source", CardZone.Hand,
            new CombatNodeModel("transformCard", "source",
                Card: new CombatCardSpec("iterated"), ToDefinition: "strike.plus"),
            filter: "strike");
        Assert.Equal(filtered, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(filtered)));

        // Unfiltered ("exhaust your whole hand") — blank filter round-trips as no filter.
        var unfiltered = CombatNodeModel.ForEachCard(
            "source", CardZone.Hand,
            new CombatNodeModel("moveCardToZone", "source",
                Card: new CombatCardSpec("iterated"), ToZone: CardZone.ExhaustPile));
        Assert.Equal(unfiltered, CombatProgramModel.Classify(CombatProgramModel.Build<CardPlayContext>(unfiltered)));
    }

    [Fact]
    public void ChangeKind_into_a_card_op_seeds_a_card_selector()
    {
        var node = new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(6));

        var transform = CombatProgramModel.ChangeKind(node, "transformCard");
        Assert.Equal("transformCard", transform.Kind);
        Assert.NotNull(transform.Card);
        Assert.Equal("strike.plus", transform.ToDefinition);
        Assert.Null(transform.Amount); // a card op carries no amount

        // Switching away clears the card + definition back to canonical defaults.
        var back = CombatProgramModel.ChangeKind(transform, "gainBlock");
        Assert.Null(back.Card);
        Assert.Equal("", back.ToDefinition);
    }
}
