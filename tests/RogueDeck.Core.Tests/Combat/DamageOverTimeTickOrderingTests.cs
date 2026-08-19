using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// A turn-start trigger that REMOVES damage-over-time stacks must shrink that same turn's tick — the
// classic "antidote/bookworm" shape ("immediately before the poison would resolve, remove up to X of
// it"). Turn-start triggers run before the damage-over-time automation (pinned by
// TurnStartedEffectRecipeArchitectureTests), but their stack removal is an ENQUEUED effect, so the tick
// must read the stacks when it RESOLVES rather than when it was queued.
public class DamageOverTimeTickOrderingTests
{
    private static readonly StatusDefinitionId Poison = new("standard.poison");

    [Fact]
    public void A_turn_start_trigger_that_removes_stacks_shrinks_the_same_turns_tick()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        // "Antidote": at the bearer's turn start, remove 2 poison stacks.
        builder.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.TurnStarted.Define(
            new TriggeredEffectDefinitionId("antidote"),
            new EffectProgram<TurnStartedTriggeredEffectContext>(
                new ModifyStatusStacksNode<TurnStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source, Poison,
                    new ConstantExpression<TurnStartedTriggeredEffectContext>(-2)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var heroId = new CombatantId("hero_001");

        new CombatEffectResolver().Resolve(combat, registry,
            new ApplyStatusEffectRequest(heroId, Poison, Stacks: 5));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);
        Assert.Equal(3, hero.Statuses.Single(s => s.DefinitionId == Poison).Stacks);
        Assert.Equal(17, hero.Health.Current); // 5 poison − 2 removed = 3 damage, not 5
    }
}
