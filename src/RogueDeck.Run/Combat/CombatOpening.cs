using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run;

// A run→combat "opening": a combat rule installed as a OneShot temporary rule on the hero at the start of the NEXT
// combat. Authored as a turnStarted RelicCombatRule, so it fires once at the hero's first turn start (e.g. "start
// with 20 block" — after block's turn-start clear), then removes itself. The pending-combat-modifier queue makes it
// apply to exactly one fight, which is exactly the time-limited effect a consumable wants. Reuses the relic-combat-
// rule authoring + serialization + visual editor (the rule is a turnStarted EffectProgram).
public sealed class HeroOpeningRuleModifier : IRunCombatModifier
{
    private readonly RelicCombatRule _rule;

    public HeroOpeningRuleModifier(RelicCombatRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rule = rule;
    }

    public void Apply(ScenarioBlueprint blueprint, RunState run)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        if (blueprint.Hero is not { } hero)
            return;

        var definition = RelicCombatTriggers.Get(_rule.Trigger).Build(
            new TriggeredEffectDefinitionId($"opening:{_rule.Trigger}:{hero.OpeningTemporaryRules.Count}"),
            _rule.Program,
            _rule.Priority);
        hero.OpeningTemporaryRules.Add(new TemporaryRuleInstallSpec(definition, TemporaryRuleLifetime.OneShot));
    }
}

// Install a "next combat opening" (see HeroOpeningRuleModifier): queues a pending combat modifier so the NEXT
// fight's hero starts with the rule installed. Serializable — its RelicCombatRule round-trips via RunJson — so a
// consumable / event / reward carries it as data. The pending queue consumes it after one combat.
public sealed record InstallNextCombatOpeningRunEffect(RelicCombatRule Rule) : IRunEffectRequest;

public sealed class InstallNextCombatOpeningRunEffectHandler : RunEffectHandler<InstallNextCombatOpeningRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, InstallNextCombatOpeningRunEffect request) =>
        run.AddPendingCombatModifier(new HeroOpeningRuleModifier(request.Rule));
}
