using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// B&B arc, Phase 5 (serialization proof). Card-mark content is authored as raw EffectPrograms on CardData /
// EnemyActionData and serialized through the CombatJson polymorphic converters — the exact path the Godot
// export (game.roguedeck.json) uses. This proves the new mark node, mark-counter node, owner-scoped card
// selectors and mark-read expressions all round-trip through those converters, so authored B&B content that
// uses them survives export/import intact.
public class CardMarkProgramJsonTests
{
    private static readonly TagId Misfiled = new("mark.misfiled");

    [Fact]
    public void An_enemy_program_that_marks_a_player_card_round_trips_through_combat_json()
    {
        // "Mark the top of the opponent's hand Misfiled (bound to me), then stamp a Reference strength on it."
        var program = new EffectProgram<EnemyActionContext>(
            new SequenceEffectNode<EnemyActionContext>(
            [
                new MarkCardInstanceNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new CardInOwnerZoneExpression<EnemyActionContext>(CombatantTargetSelectors.EventTarget, CardZone.Hand, 0),
                    Misfiled,
                    sourceSelector: CombatantTargetSelectors.Source),
                new SetCardInstanceMarkCounterNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new RandomCardInOwnerZoneExpression<EnemyActionContext>(CombatantTargetSelectors.EventTarget, CardZone.Hand),
                    new CounterId("mark.reference.strength"),
                    new ConstantExpression<EnemyActionContext>(3)),
            ]));

        var options = CombatJson.CreateOptions<EnemyActionContext>();
        var json = JsonSerializer.Serialize(program, options);
        var reloaded = JsonSerializer.Deserialize<EffectProgram<EnemyActionContext>>(json, options);

        Assert.NotNull(reloaded);
        // Re-serializing the reloaded program yields identical JSON — the node/expression discriminators and
        // their fields survived the round trip.
        Assert.Equal(json, JsonSerializer.Serialize(reloaded, options));
    }

    [Fact]
    public void A_redacted_output_scale_program_round_trips()
    {
        // Redacted authored as content: set the two reserved output-scale counters (1/2) on a player card.
        var program = new EffectProgram<EnemyActionContext>(
            new SequenceEffectNode<EnemyActionContext>(
            [
                new SetCardInstanceMarkCounterNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new CardInOwnerZoneExpression<EnemyActionContext>(CombatantTargetSelectors.EventTarget, CardZone.Hand, 0),
                    StandardCombatIds.CardOutputScaleNumeratorCounter,
                    new ConstantExpression<EnemyActionContext>(1)),
                new SetCardInstanceMarkCounterNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget,
                    new CardInOwnerZoneExpression<EnemyActionContext>(CombatantTargetSelectors.EventTarget, CardZone.Hand, 0),
                    StandardCombatIds.CardOutputScaleDenominatorCounter,
                    new ConstantExpression<EnemyActionContext>(2)),
            ]));

        var options = CombatJson.CreateOptions<EnemyActionContext>();
        var json = JsonSerializer.Serialize(program, options);
        var reloaded = JsonSerializer.Deserialize<EffectProgram<EnemyActionContext>>(json, options);

        Assert.NotNull(reloaded);
        Assert.Equal(json, JsonSerializer.Serialize(reloaded, options));
    }
}
