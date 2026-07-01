using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Tests;

// Enemy actions are authorable as data too (EnemyActionContext), via the same CombatJson infrastructure.
public class EnemyActionJsonTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<EnemyActionContext>();

    private static EnemyActionBlueprint Slam() =>
        new("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    new EventTargetCombatantTargetSelector(), new ConstantExpression<EnemyActionContext>(4))),
        };

    [Fact]
    public void An_enemy_action_round_trips_with_its_program_and_intent()
    {
        var data = EnemyActionData.From(Slam());

        var json1 = JsonSerializer.Serialize(data, Options);
        var back = JsonSerializer.Deserialize<EnemyActionData>(json1, Options)!;

        Assert.Equal(json1, JsonSerializer.Serialize(back, Options));
        Assert.Equal("slam", back.Id);
        Assert.Equal(IntentKind.Attack, back.Intent.Kind);

        var deal = Assert.IsType<DealDamageNode<EnemyActionContext>>(back.Program!.Root);
        Assert.Equal(4, Assert.IsType<ConstantExpression<EnemyActionContext>>(deal.Amount).Value);

        // Maps back to a runnable action definition.
        Assert.NotNull(back.ToBlueprint().Compile().Build());
    }
}
