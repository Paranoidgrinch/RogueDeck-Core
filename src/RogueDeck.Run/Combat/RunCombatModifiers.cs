using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run;

// A run-authored change to the next spawned combat — the run "writing a future fight" (idea doc §13.2). A
// modifier mutates the combat's still-mutable blueprint before it is compiled, so it can add starting
// statuses, enemy modifiers, and the like. Modifiers are queued on RunState (pending) and consumed by the
// combat bridge for exactly one fight.
//
// Coupling note: a modifier that references a status/definition needs the spawned combat's blueprint to
// register it — modifiers add specs, they do not define the content.
public interface IRunCombatModifier
{
    void Apply(ScenarioBlueprint blueprint, RunState run);
}

// Escape hatch: an arbitrary blueprint mutation. The named helpers below are thin wrappers over this.
public sealed class DelegateRunCombatModifier : IRunCombatModifier
{
    private readonly Action<ScenarioBlueprint, RunState> _apply;
    public DelegateRunCombatModifier(Action<ScenarioBlueprint, RunState> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        _apply = apply;
    }
    public void Apply(ScenarioBlueprint blueprint, RunState run) => _apply(blueprint, run);
}

// Readable modifier construction.
public static class RunCombat
{
    public static IRunCombatModifier Custom(Action<ScenarioBlueprint, RunState> apply) =>
        new DelegateRunCombatModifier(apply);

    public static IRunCombatModifier HeroStartsWithStatus(
        StatusDefinitionId status, int stacks = 0, int durationTurns = 0, int charges = 0) =>
        Custom((blueprint, _) =>
        {
            if (blueprint.Hero is { } hero)
                hero.StartingStatuses.Add(new StartingStatusSpec(status, stacks, durationTurns, charges));
        });

    // Applies to every enemy in the spawned fight.
    public static IRunCombatModifier EnemiesStartWithStatus(
        StatusDefinitionId status, int stacks = 0, int durationTurns = 0, int charges = 0) =>
        Custom((blueprint, _) =>
        {
            foreach (var enemy in blueprint.Enemies)
                enemy.StartingStatuses.Add(new StartingStatusSpec(status, stacks, durationTurns, charges));
        });
}

// Ready-made deck mappers for CombatNodeResolver — how a run card copy projects to a combat card definition.
public static class RunDeckMappers
{
    // Fight as the base card, ignoring per-copy state (the resolver's default).
    public static Func<RunCardInstance, CardDefinitionId> Identity { get; } = card => card.DefinitionId;

    // Convention: an upgraded copy (level > 0) fights as "<id><suffix>" (default "+"); the base id otherwise.
    // Content provides the "<id>+" combat definitions. Composed cards (Shred Engine) are exempt — their
    // derived shred:… definition is synthesized per fight and no "<id>+" variant exists to map to.
    public static Func<RunCardInstance, CardDefinitionId> UpgradeSuffix(string suffix = "+") =>
        card => card.UpgradeLevel > 0 && card.Composition.Count == 0
            ? new CardDefinitionId(card.DefinitionId + suffix)
            : card.DefinitionId;

    // UpgradeSuffix, but only when the upgraded definition actually exists — content that never authored
    // "<id>+" variants keeps its runs playable (the upgrade stays a run-side fact the fight ignores).
    public static Func<RunCardInstance, CardDefinitionId> UpgradeSuffixWhenDefined(
        Func<CardDefinitionId, bool> isDefined, string suffix = "+")
    {
        ArgumentNullException.ThrowIfNull(isDefined);
        var map = UpgradeSuffix(suffix);
        return card =>
        {
            var mapped = map(card);
            return mapped == card.DefinitionId || isDefined(mapped) ? mapped : card.DefinitionId;
        };
    }
}

// Queue a combat modifier from the normal effect flow (an event choice, a relic, a scheduled consequence).
public sealed record AddCombatModifierRunEffect(IRunCombatModifier Modifier) : IRunEffectRequest;

public sealed class AddCombatModifierRunEffectHandler : RunEffectHandler<AddCombatModifierRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddCombatModifierRunEffect request) =>
        run.AddPendingCombatModifier(request.Modifier);
}
