using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// A run-level triggered program: the relic's "face (a)". Mirrors the combat layer's triggered-effect
// definition (the same shape that temporary rules install via InstallTemporaryRuleEffectRequest), one etage
// up. When a raised run-event matches EventType, Build produces the effects the relic wants to enqueue.
public interface ITriggeredRunEffectDefinition
{
    Type EventType { get; }
    IReadOnlyList<IRunEffectRequest> Build(IRunEvent runEvent, RunState run);
}

// Convenience implementation: a strongly-typed reaction. This keeps relic authoring a one-liner while the
// dispatch loop stays type-driven.
public sealed class TriggeredRunEffect<TEvent> : ITriggeredRunEffectDefinition
    where TEvent : IRunEvent
{
    private readonly Func<TEvent, RunState, IReadOnlyList<IRunEffectRequest>> _build;

    public TriggeredRunEffect(Func<TEvent, RunState, IReadOnlyList<IRunEffectRequest>> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    public Type EventType => typeof(TEvent);

    public IReadOnlyList<IRunEffectRequest> Build(IRunEvent runEvent, RunState run) =>
        runEvent is TEvent typed ? _build(typed, run) : Array.Empty<IRunEffectRequest>();
}

// A relic definition. It has two faces, exactly because relics bend the whole game, not just one fight:
//   (a) RunPrograms  — react to run-level events (implemented now).
//   (b) CombatContributions — triggered programs injected into a fight when a combat node spawns. The hook
//       exists so the design is honest about relics that reach into combat; wiring it through the combat
//       bridge is a deferred slice.
public sealed class RelicDefinition
{
    public RelicId Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<ITriggeredRunEffectDefinition> RunPrograms { get; }
    public IReadOnlyList<ITriggeredEffectDefinition> CombatContributions { get; }

    public RelicDefinition(
        RelicId id,
        string displayName,
        IReadOnlyList<ITriggeredRunEffectDefinition>? runPrograms = null,
        IReadOnlyList<ITriggeredEffectDefinition>? combatContributions = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Relic display name cannot be empty.", nameof(displayName));

        Id = id;
        DisplayName = displayName;
        RunPrograms = runPrograms ?? Array.Empty<ITriggeredRunEffectDefinition>();
        CombatContributions = combatContributions ?? Array.Empty<ITriggeredEffectDefinition>();
    }
}

// An acquired relic on a live run. For now it is a thin wrapper over its definition; per-instance state
// (charges, counters) can live here later without touching the definition.
public sealed class RelicInstance
{
    public RelicDefinition Definition { get; }
    public RelicId Id => Definition.Id;

    // A disabled relic neither reacts to run events nor contributes to combats. Toggled by the
    // disable/enable effects (disable schedules a re-enable after N combats).
    public bool Enabled { get; private set; } = true;

    public void SetEnabled(bool enabled) => Enabled = enabled;

    public RelicInstance(RelicDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }
}
