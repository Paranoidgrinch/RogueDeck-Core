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

// Identity of a reward table (a named reward source) in the content registry.
public readonly record struct RewardTableId(string Value)
{
    public override string ToString() => Value;
}

// Identity of a triggered program installed on the run (a scheduled consequence, a rule modifier, a reward
// modifier — the generalised form of what a relic's RunPrograms do). Lets a program be uninstalled by id.
public readonly record struct RunProgramId(string Value)
{
    public override string ToString() => Value;
}

// A named boolean fact remembered for the whole run (e.g. "stole-from-merchant"). Open like the resource id
// so content defines its own vocabulary rather than the engine hard-coding flags.
public readonly record struct RunFlagId(string Value)
{
    public override string ToString() => Value;
}

// A named integer the run accumulates (e.g. "debt", "elites-defeated"). Absent counters read as 0.
public readonly record struct RunCounterId(string Value)
{
    public override string ToString() => Value;
}

// Identity of a single card as it lives in the run's deck. Unlike CardDefinitionId (the kind of card), this
// is the individual copy, so per-copy state (upgrade level, tags, memory) can attach to it.
public readonly record struct RunCardInstanceId(string Value)
{
    public override string ToString() => Value;
}

// A run-side tag on a card instance (e.g. "cursed", "scarred"). Open like the other run ids so content owns
// its vocabulary. Kept distinct from any combat-layer tag — a run tag is metadata on the persistent copy.
public readonly record struct RunCardTagId(string Value)
{
    public override string ToString() => Value;
}

// Identity of an authored event, referenced by an event node instead of embedding the EventScript.
public readonly record struct EventId(string Value)
{
    public override string ToString() => Value;
}

// Identity of a data-defined combat encounter, referenced by a combat node instead of a hand-authored fight.
public readonly record struct EncounterId(string Value)
{
    public override string ToString() => Value;
}

// The kind of a consumable (e.g. "potion.fire"); and the identity of one owned copy in the run inventory.
public readonly record struct ConsumableId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct ConsumableInstanceId(string Value)
{
    public override string ToString() => Value;
}

// A persistent player-controlled board unit carried across combats (P5c). One per live RunUnit in the roster.
public readonly record struct RunUnitInstanceId(string Value)
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
    public const string NodeChosen = "run.node-chosen";
    public const string MapChanged = "run.map-changed";
    public const string CombatResolved = "run.combat-resolved";
    public const string EventChoiceMade = "run.event-choice";
    public const string ResourceChanged = "run.resource-changed";
    public const string RelicAcquired = "run.relic-acquired";
    public const string RelicRemoved = "run.relic-removed";
    public const string RelicDisabled = "run.relic-disabled";
    public const string RelicEnabled = "run.relic-enabled";
    public const string MaxHealthChanged = "run.max-health-changed";
    public const string RewardGranted = "run.reward-granted";
    public const string RewardOffered = "run.reward-offered";
    public const string RewardChosen = "run.reward-chosen";
    public const string ConsumableGained = "run.consumable-gained";
    public const string ConsumableUsed = "run.consumable-used";
    public const string RunEnded = "run.ended";
    public const string ResolveGuardTripped = "run.resolve-guard-tripped";
    public const string ProgramInstalled = "run.program-installed";
    public const string ProgramUninstalled = "run.program-uninstalled";
    public const string FlagChanged = "run.flag-changed";
    public const string CounterChanged = "run.counter-changed";
    public const string CardAdded = "run.card-added";
    public const string CardRemoved = "run.card-removed";
    public const string CardUpgraded = "run.card-upgraded";
    public const string CardTransformed = "run.card-transformed";
    public const string CardTagChanged = "run.card-tag-changed";
}
