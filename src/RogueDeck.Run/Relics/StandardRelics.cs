using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// Built-in sample relics. These exist to prove the thesis that a relic is just a run-level triggered
// program — there is no special "relic system", only an ITriggeredRunEffectDefinition reacting to a
// run-event. Real content packs would author their own the same way.
public static class StandardRelics
{
    // Bloodstone: after winning a fight, heal a flat amount of run HP. The heal happens purely because the
    // relic reacts to CombatResolvedRunEvent — the combat node knows nothing about it.
    public static RelicDefinition Bloodstone(int healAmount = 5) =>
        new(
            new RelicId("bloodstone"),
            "Bloodstone",
            runPrograms: new ITriggeredRunEffectDefinition[]
            {
                new TriggeredRunEffect<CombatResolvedRunEvent>((evt, _) =>
                    evt.Result == CombatResult.Victory
                        ? new IRunEffectRequest[] { new HealRunEffect(healAmount) }
                        : Array.Empty<IRunEffectRequest>()),
            });
}
