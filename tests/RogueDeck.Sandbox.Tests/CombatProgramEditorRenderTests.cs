using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Combat.Components;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Static-render smoke tests for the recursive CombatProgramEditor (P1c). The component recurses into its own tag
// for every composite child, so a render-tree imbalance or a bad self-reference would only surface at render time.
// Rendering a deeply nested model (repeat → conditional with then/else, sequence of leaves) exercises the recursion
// end-to-end and fails loudly on any imbalance. Uses the framework HtmlRenderer (no bUnit dependency).
public class CombatProgramEditorRenderTests
{
    private static async Task<string> RenderAsync(CombatNodeModel node)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(provider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CombatProgramEditor.Node)] = node,
            });
            var output = await renderer.RenderComponentAsync<CombatProgramEditor>(parameters);
            // Decode entities so assertions can use the human labels (Blazor encodes non-ASCII like … and ≥).
            return System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    [Fact]
    public async Task Renders_a_leaf_node_with_its_controls()
    {
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.Event));

        Assert.Contains("deal damage", html);
        Assert.Contains("allEnemies", html);
        Assert.Contains("event amount", html);
    }

    [Fact]
    public async Task Renders_gain_resource_id_field()
    {
        var html = await RenderAsync(new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(1), "standard.energy"));

        Assert.Contains("gain resource", html);
        Assert.Contains("standard.energy", html);
    }

    [Fact]
    public async Task Renders_the_new_resource_leaf_with_its_id_field()
    {
        // B2a: lose/modify resource now show the resource-id field via UsesResourceId (previously gainResource only).
        var html = await RenderAsync(new CombatNodeModel("loseResource", "source", CombatAmountSpec.FromConst(2), "faith"));

        Assert.Contains("lose resource", html);
        Assert.Contains("faith", html);
    }

    [Fact]
    public async Task Palette_lists_the_widened_leaf_kinds()
    {
        // The kind dropdown lists every AllKinds entry, so the B2a/B2b additions must appear as options on any node.
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(1)));

        Assert.Contains("modify max health", html);
        Assert.Contains("set health", html);
        Assert.Contains("draw cards", html);
        Assert.Contains("modify resource", html);
        Assert.Contains("apply status", html);
        Assert.Contains("remove status", html);
        Assert.Contains("cleanse (by polarity)", html);
        Assert.Contains("modify status stacks", html);
        Assert.Contains("move cards (zone → zone)", html);
    }

    [Fact]
    public async Task Renders_move_cards_with_zone_dropdowns_and_no_amount()
    {
        // B2c: moveCards shows from/to zone dropdowns (CardZone options) and no amount control.
        var html = await RenderAsync(new CombatNodeModel("moveCards", "source", FromZone: CardZone.Hand, ToZone: CardZone.DiscardPile));

        Assert.Contains("move cards (zone → zone)", html);
        Assert.Contains("DrawPile", html);
        Assert.Contains("ExhaustPile", html);
        Assert.DoesNotContain("event amount", html); // no amount control for a zone move
    }

    [Fact]
    public async Task Renders_apply_status_with_id_duration_and_charges()
    {
        // B2b: applyStatus shows a status-id field plus turns/charges inputs (and the stacks amount control).
        var html = await RenderAsync(
            new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(2), StatusId: "poison", DurationTurns: 3, Charges: 1));

        Assert.Contains("apply status", html);
        Assert.Contains("poison", html);
        Assert.Contains("turns", html);
        Assert.Contains("charges", html);
    }

    [Fact]
    public async Task Renders_the_niche_tail_amounts()
    {
        // Phase 1i tail: clamp shows three nested operands; countTargets shows a full (parameterized) selector; cardCost
        // shows the card widget + a resource id.
        var clamp = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget", new CombatAmountSpec("clamp",
            Left: CombatAmountSpec.FromConst(5), Right: CombatAmountSpec.FromConst(0), Third: CombatAmountSpec.FromConst(10))));
        Assert.Contains("clamp", clamp);

        var count = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget",
            new CombatAmountSpec("countTargets", ReadSelector: new CombatSelectorSpec("enemiesWithStatus", "poison"))));
        Assert.Contains("count targets", count);
        Assert.Contains("enemiesWithStatus", count);

        var cost = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget",
            new CombatAmountSpec("cardCost", ReadId: "standard.energy", ReadCard: new CombatCardSpec("chosen", CardZone.Hand))));
        Assert.Contains("card cost", cost);
        Assert.Contains("resource id", cost);
    }

    [Fact]
    public async Task Renders_a_state_read_amount_with_a_selector_and_id()
    {
        // Phase 1i part 2: a resource state-read amount shows a single-target selector + an id field.
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget",
            new CombatAmountSpec("currentResource", SelectorKey: "source", ReadId: "standard.energy")));

        Assert.Contains("resource", html);        // the amount kind label
        Assert.Contains("standard.energy", html);  // the id field value
    }

    [Fact]
    public async Task Renders_an_arithmetic_amount_with_nested_operands()
    {
        // Phase 1i: an arithmetic amount renders nested operand widgets (add of a counter and a constant).
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget",
            CombatAmountSpec.Binary("add", CombatAmountSpec.Counter("source", "combo"), CombatAmountSpec.FromConst(3))));

        Assert.Contains("deal damage", html);
        Assert.Contains("counter id", html); // the nested counter operand's id field
        Assert.Contains("combo", html);
        Assert.Contains("round #", html);     // the amount kind dropdown lists the new kinds
    }

    [Fact]
    public async Task Renders_a_parameterized_selector_with_status_id_and_union_members()
    {
        // Phase 1h part 2: a status-filtered selector shows a status-id field; union shows its member selectors + add.
        var withStatus = await RenderAsync(new CombatNodeModel("dealDamage", "enemiesWithStatus", CombatAmountSpec.FromConst(6), SelectorStatusId: "poison"));
        Assert.Contains("enemiesWithStatus", withStatus);
        Assert.Contains("status id", withStatus); // the status-id field placeholder
        Assert.Contains("poison", withStatus);

        var union = await RenderAsync(new CombatNodeModel("dealDamage", "union", CombatAmountSpec.FromConst(2),
            SelectorMembers: new[] { new CombatSelectorSpec("allEnemies"), new CombatSelectorSpec("source") }));
        Assert.Contains("union", union);
        Assert.Contains("+ member", union);
        Assert.Contains("allEnemies", union);
    }

    [Fact]
    public async Task Renders_repeat_until_and_random_targets_composites()
    {
        // Phase 1h: repeat-until shows its stop condition; random-targets shows count + candidate selector.
        var repeatUntil = await RenderAsync(CombatNodeModel.RepeatUntil(
            new CombatConditionSpec("isAlive", "eventTarget"),
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(3))));
        Assert.Contains("repeat until…", repeatUntil);
        Assert.Contains("until", repeatUntil);
        Assert.Contains("deal damage", repeatUntil); // the body recurses

        var randomTargets = await RenderAsync(CombatNodeModel.RandomTargets(
            "allEnemies", CombatAmountSpec.FromConst(2),
            new CombatNodeModel("applyStatus", "eventTarget", CombatAmountSpec.FromConst(1), StatusId: "poison")));
        Assert.Contains("random targets…", randomTargets);
        Assert.Contains("allEnemies", randomTargets);
    }

    [Fact]
    public async Task Renders_play_card_with_optional_target_and_replay()
    {
        // Phase 1g part 2: playCard shows the card widget + a target toggle; replay shows the card widget.
        var play = await RenderAsync(new CombatNodeModel("playCard", "source", Card: new CombatCardSpec("chosen", CardZone.Hand), HasCardTarget: true, ToSelectorKey: "eventTarget"));
        Assert.Contains("play a card", play);
        Assert.Contains("player chooses", play);
        Assert.Contains("eventTarget", play); // the card target selector shows when the toggle is on

        var replay = await RenderAsync(new CombatNodeModel("replayCardProgram", "eventTarget", Card: new CombatCardSpec("chosen", CardZone.Hand)));
        Assert.Contains("replay a card's program", replay);
    }

    [Fact]
    public async Task Renders_create_card_and_copy_card_with_zone()
    {
        // Phase 1g: createCardInstance shows a card-definition input + "into" zone; createCardCopy shows the card widget.
        var create = await RenderAsync(new CombatNodeModel("createCardInstance", "source", CombatAmountSpec.FromConst(2), ToDefinition: "wound", ToZone: CardZone.DiscardPile));
        Assert.Contains("create card(s)", create);
        Assert.Contains("wound", create);
        Assert.Contains("into", create);

        var copy = await RenderAsync(new CombatNodeModel("createCardCopy", "source", CombatAmountSpec.FromConst(1), Card: new CombatCardSpec("chosen", CardZone.Hand), ToZone: CardZone.Hand));
        Assert.Contains("copy a card", copy);
        Assert.Contains("player chooses", copy); // the card-selector widget
    }

    [Fact]
    public async Task Renders_summon_combatant_with_fields_position_and_status_list()
    {
        // Phase 1f (summon slice): team/def/name/HP fields, an optional position, and a starting-status list row.
        var html = await RenderAsync(new CombatNodeModel(
            "summonCombatant", "source", CombatAmountSpec.FromConst(20),
            TeamId: "enemies", SummonDefinitionId: "skeleton", SummonDisplayName: "Skeleton",
            PositionX: 1, PositionY: 2, StartingStatuses: new[] { new StatusGrant(new StatusDefinitionId("poison"), 3) }));

        Assert.Contains("summon combatant", html);
        Assert.Contains("skeleton", html);
        Assert.Contains("Skeleton", html);
        Assert.Contains("HP", html);
        Assert.Contains("starting statuses:", html);
        Assert.Contains("poison", html);
        Assert.Contains("+ status", html);
    }

    [Fact]
    public async Task Renders_combat_control_leaves()
    {
        // Phase 1f: lifecycle dropdown, team-id field, combat-result dropdown, rule-id field. The combat-global
        // controls (set result / remove rule) hide the target selector.
        var lifecycle = await RenderAsync(new CombatNodeModel("setCombatantLifecycleState", "eventTarget", LifecycleState: CombatantLifecycleState.Downed));
        Assert.Contains("set lifecycle state", lifecycle);
        Assert.Contains("Downed", lifecycle);

        var team = await RenderAsync(new CombatNodeModel("changeCombatantTeam", "eventTarget", TeamId: "players"));
        Assert.Contains("change team", team);
        Assert.Contains("players", team);

        var result = await RenderAsync(new CombatNodeModel("setCombatResult", "source", CombatResult: CombatResult.Victory));
        Assert.Contains("set combat result", result);
        Assert.Contains("Victory", result);
        Assert.DoesNotContain("eventTarget", result); // combat-global: no target selector

        var rule = await RenderAsync(new CombatNodeModel("removeTemporaryRule", "source", RuleId: "rule.enrage"));
        Assert.Contains("remove temporary rule", rule);
        Assert.Contains("rule.enrage", rule);
    }

    [Fact]
    public async Task Renders_move_combatant_absolute_with_x_and_y_and_swap_with_two_selectors()
    {
        // Phase 1e: absolute move shows a mode dropdown + x/y amount controls.
        var move = await RenderAsync(new CombatNodeModel(
            "moveCombatant", "source", MovementMode: MovementMode.ToAbsolute,
            MoveX: CombatAmountSpec.FromConst(1), MoveY: CombatAmountSpec.FromConst(2)));
        Assert.Contains("move combatant", move);
        Assert.Contains("ToAbsolute", move);
        Assert.Contains("x", move);
        Assert.Contains("y", move);

        var swap = await RenderAsync(new CombatNodeModel("swapPositions", "source", ToSelectorKey: "eventTarget"));
        Assert.Contains("swap positions", swap);
        Assert.Contains("eventTarget", swap); // the second selector
    }

    [Fact]
    public async Task Renders_move_combatant_relative_with_a_step()
    {
        var html = await RenderAsync(new CombatNodeModel(
            "moveCombatant", "eventTarget", MovementMode: MovementMode.PushFromSource, MoveStep: CombatAmountSpec.FromConst(2)));

        Assert.Contains("PushFromSource", html);
        Assert.Contains("step", html);
    }

    [Fact]
    public async Task Renders_set_combatant_counter_with_id_and_relative_toggle()
    {
        // Phase 1d: setCombatantCounter shows a counter-id field and a relative checkbox.
        var html = await RenderAsync(new CombatNodeModel("setCombatantCounter", "source", CombatAmountSpec.FromConst(1), CounterId: "combo", Relative: true));

        Assert.Contains("set combatant counter", html);
        Assert.Contains("combo", html);
        Assert.Contains("relative", html);
    }

    [Fact]
    public async Task Renders_a_counter_amount_source_with_selector_and_id()
    {
        // The amount widget offers "counter" and, when chosen, a single-target selector + counter-id field.
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.Counter("source", "combo")));

        Assert.Contains("counter id", html); // the counter amount's id field placeholder
        Assert.Contains("combo", html);
    }

    [Fact]
    public async Task Renders_deal_damage_with_element_and_ignores_block()
    {
        // Phase 1c: dealDamage shows an element field + an "ignores block" checkbox.
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(8), Element: "fire", IgnoresBlock: true));

        Assert.Contains("deal damage", html);
        Assert.Contains("fire", html);
        Assert.Contains("ignores block", html);
    }

    [Fact]
    public async Task Renders_refill_resource_with_id_and_max_and_no_amount()
    {
        // Phase 1b: refillResource shows a resource-id + max field and NO amount control (it refills to max).
        var html = await RenderAsync(new CombatNodeModel("refillResource", "source", ResourceId: "standard.energy", DefaultMax: 3));

        Assert.Contains("refill resource", html);
        Assert.Contains("standard.energy", html);
        Assert.Contains("max", html);
        Assert.DoesNotContain("event amount", html);
    }

    [Fact]
    public async Task Renders_modify_defensive_pool_with_a_pool_id()
    {
        var html = await RenderAsync(new CombatNodeModel("modifyDefensivePool", "source", CombatAmountSpec.FromConst(5), PoolId: "block"));

        Assert.Contains("modify defensive pool", html);
        Assert.Contains("pool id", html); // placeholder
        Assert.Contains("block", html);
    }

    [Fact]
    public async Task Renders_modify_selected_resource_with_the_resource_selection_widget()
    {
        var html = await RenderAsync(new CombatNodeModel(
            "modifySelectedResource", "eventTarget", CombatAmountSpec.FromConst(-2),
            ResourceSelection: new ResourceSelectionSpec(ResourcePoolFilter.NonEmpty, ResourcePick.Highest)));

        Assert.Contains("modify a selected resource", html);
        Assert.Contains("nonempty", html); // filter option (lower-cased)
        Assert.Contains("highest", html);  // pick option
    }

    [Fact]
    public async Task Renders_gain_resource_cap_and_modify_resource_bounds()
    {
        var capped = await RenderAsync(new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(2), "standard.energy", DefaultMax: 9));
        Assert.Contains("max", capped);

        var clamped = await RenderAsync(new CombatNodeModel("modifyResource", "source", CombatAmountSpec.FromConst(1), "faith", Min: 0, Max: 5));
        Assert.Contains("min", clamped);
        Assert.Contains("max", clamped);
    }

    [Fact]
    public async Task Renders_remove_selected_status_with_the_selection_widget_and_no_amount()
    {
        // Phase 1a: removeSelectedStatus shows the status-selection widget (polarity filter + pick) and no amount.
        var html = await RenderAsync(new CombatNodeModel(
            "removeSelectedStatus", "eventTarget",
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff, StatusPick.Random)));

        Assert.Contains("remove a selected status", html);
        Assert.Contains("buff", html);   // polarity filter option (lower-cased label)
        Assert.Contains("random", html); // pick mode option
        Assert.DoesNotContain("event amount", html); // a remove carries no amount control
    }

    [Fact]
    public async Task Renders_modify_selected_status_with_an_amount_delta()
    {
        // modifySelectedStatusStacks DOES carry an amount (its delta) alongside the selection widget.
        var html = await RenderAsync(new CombatNodeModel(
            "modifySelectedStatusStacks", "eventTarget", CombatAmountSpec.FromConst(-2),
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Debuff, StatusPick.First, Index: 1)));

        Assert.Contains("modify a selected status", html);
        Assert.Contains("debuff", html);
        Assert.Contains("first", html);
        Assert.Contains("constant", html); // the delta amount control is present
    }

    [Fact]
    public async Task Renders_steal_selected_status_with_a_second_to_selector()
    {
        // stealSelectedStatus shows the selection widget plus a "to" selector (the thief).
        var html = await RenderAsync(new CombatNodeModel(
            "stealSelectedStatus", "eventTarget",
            Selection: new StatusSelectionSpec(StatusPolarityFilter.Buff), ToSelectorKey: "source"));

        Assert.Contains("steal a selected status", html);
        Assert.Contains("to", html); // the to-selector label
    }

    [Fact]
    public async Task Renders_cleanse_with_a_polarity_dropdown_and_no_amount()
    {
        // cleanse takes no amount (UsesAmount false), so the amount kind-select is absent; it shows polarity options.
        var html = await RenderAsync(new CombatNodeModel("cleanse", "source", Polarity: StatusPolarity.Debuff));

        Assert.Contains("cleanse (by polarity)", html);
        Assert.Contains("Buff", html);
        Assert.Contains("Debuff", html);
        Assert.DoesNotContain("event amount", html); // the amount control is hidden for a no-amount leaf
    }

    [Fact]
    public async Task Renders_nested_control_flow_without_error()
    {
        // repeat 2× { if (source missing HP ≥ 10) then heal else deal damage }.
        var model = CombatNodeModel.Repeat(
            CombatAmountSpec.FromConst(2),
            CombatNodeModel.Conditional(
                new CombatConditionSpec("compare", "source", "missingHealth", ComparisonOperator.GreaterOrEqual, 10),
                new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(6)),
                new CombatNodeModel("dealDamage", "lowestHealthEnemy", CombatAmountSpec.FromConst(8))));

        var html = await RenderAsync(model);

        Assert.Contains("repeat…", html);      // composite kind option (selected)
        Assert.Contains("value compares", html); // condition kind
        Assert.Contains("missing HP", html);     // compare value kind
        Assert.Contains("then:", html);
        Assert.Contains("else:", html);
        Assert.Contains("heal", html);
        Assert.Contains("deal damage", html);
    }

    [Fact]
    public async Task Palette_lists_the_card_targeting_kinds()
    {
        var html = await RenderAsync(new CombatNodeModel("dealDamage", "source", CombatAmountSpec.FromConst(1)));

        Assert.Contains("move a card (targeted)", html);
        Assert.Contains("transform / upgrade a card", html);
        Assert.Contains("for each card in zone…", html);
    }

    [Fact]
    public async Task Renders_transform_card_with_its_selector_widget_and_target_definition()
    {
        // transformCard shows the card-selector widget (player chooses, in a zone) + the target definition field.
        var html = await RenderAsync(new CombatNodeModel(
            "transformCard", "source", Card: new CombatCardSpec("chosen", CardZone.Hand), ToDefinition: "strike.plus"));

        Assert.Contains("transform / upgrade a card", html);
        Assert.Contains("player chooses", html);   // the card-selector kind
        Assert.Contains("strike.plus", html);       // the target definition input
        Assert.DoesNotContain("event amount", html); // a card op carries no amount control
    }

    [Fact]
    public async Task Renders_forEachCardInZone_with_zone_filter_and_body()
    {
        // "upgrade every Strike in hand": the composite shows its owner + zone + filter and recurses into the body.
        var model = CombatNodeModel.ForEachCard(
            "source", CardZone.Hand,
            new CombatNodeModel("transformCard", "source",
                Card: new CombatCardSpec("iterated"), ToDefinition: "strike.plus"),
            filter: "strike");

        var html = await RenderAsync(model);

        Assert.Contains("for each card in zone…", html);
        Assert.Contains("strike", html);              // the definition filter
        Assert.Contains("current (loop card)", html); // the body's iterated card selector
    }

    [Fact]
    public async Task Renders_sequence_with_add_palette()
    {
        var model = CombatNodeModel.Sequence(new[]
        {
            new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5)),
            new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(6)),
        });

        var html = await RenderAsync(model);

        Assert.Contains("in sequence…", html);
        Assert.Contains("add:", html);
        Assert.Contains("for each target…", html); // palette lists composites too
    }
}
