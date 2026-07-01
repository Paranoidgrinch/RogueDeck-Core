namespace RogueDeck.Run;

// Run-layer identity types. These mirror the combat layer's readonly-record-struct ids (CombatIds.cs)
// so the two engines look and feel the same one etage apart.

public readonly record struct RunId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct NodeId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RelicId(string Value)
{
    public override string ToString() => Value;
}

// A generic run-level resource (gold, keys, soul-shards…). Kept open like ResourceId in combat so any
// run can define its own economy rather than hard-coding "gold".
public readonly record struct RunResourceId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct RewardId(string Value)
{
    public override string ToString() => Value;
}

// Identity of a triggered program installed on the run (a scheduled consequence, a rule modifier, a reward
// modifier — the generalised form of what a relic's RunPrograms do). Lets a program be uninstalled by id.
public readonly record struct RunProgramId(string Value)
{
    public override string ToString() => Value;
}

// The kind of a map node. A string-backed id (not an enum) so new node kinds can be added by any package
// without touching the core — resolvers are registered against these values.
public readonly record struct NodeType(string Value)
{
    public override string ToString() => Value;
}

public enum RunResult
{
    Ongoing,
    Victory,
    Defeat,
    Aborted
}

public static class StandardRunIds
{
    public static readonly RunResourceId Gold = new("gold");

    public static readonly NodeType CombatNode = new("combat");
    public static readonly NodeType EventNode = new("event");
}

public static class StandardRunLogTypes
{
    public const string RunStarted = "run.started";
    public const string NodeEntered = "run.node-entered";
    public const string CombatResolved = "run.combat-resolved";
    public const string EventChoiceMade = "run.event-choice";
    public const string ResourceChanged = "run.resource-changed";
    public const string RelicAcquired = "run.relic-acquired";
    public const string RewardGranted = "run.reward-granted";
    public const string RunEnded = "run.ended";
    public const string ResolveGuardTripped = "run.resolve-guard-tripped";
    public const string ProgramInstalled = "run.program-installed";
    public const string ProgramUninstalled = "run.program-uninstalled";
}
