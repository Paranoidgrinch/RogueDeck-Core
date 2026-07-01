using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// Built-in sample relics. These exist to prove the thesis that a relic is just a run-level triggered
// program — there is no special "relic system", only a triggered program reacting to a run-event. Both are
// now authored declaratively (RunPrograms: event + condition + effect templates), so a relic is pure data.
public static class StandardRelics
{
    // Bloodstone: after winning a fight, heal a flat amount of run HP. Condition reads the combat event.
    public static RelicDefinition Bloodstone(int healAmount = 5) =>
        new(
            new RelicId("bloodstone"),
            "Bloodstone",
            runPrograms: new[]
            {
                RunPrograms.When<CombatResolvedRunEvent>(
                    RunEventValues.CombatWasVictory, new HealRunEffect(healAmount)),
            });

    // Leech: after any resolved fight, gain gold equal to the run HP lost — the effect value is read from the
    // combat event at dispatch via a template, so no lambda and no bespoke class.
    public static RelicDefinition Leech() =>
        new(
            new RelicId("leech"),
            "Leech",
            runPrograms: new[]
            {
                RunPrograms.On<CombatResolvedRunEvent>(
                    RunEffectTemplates.GainResource(StandardRunIds.Gold, RunEventValues.CombatDamageTaken)),
            });
}
