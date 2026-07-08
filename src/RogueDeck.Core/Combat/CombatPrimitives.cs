namespace RogueDeck.Core.Combat;

public enum CombatResult
{
    Ongoing,
    Victory,
    Defeat,
    Draw,
    Aborted
}

public enum CombatTurnPhase
{
    WaitingToStartTurn,
    TurnInProgress
}

public enum DamageKind
{
    Direct,
    DamageOverTime,
    Reflected
}

public enum CombatantLifecycleState
{
    Alive,
    Downed,
    Dead,
    Removed,
    Escaped,
    Banished
}

public enum StatusVisibility
{
    Visible,
    Hidden,
    DebugOnly
}

public enum StatusPolarity
{
    Buff,
    Debuff,
    Neutral
}

public enum StatusStackingBehavior
{
    CreateSeparateInstance,
    MergeWithExistingInstance
}

// A combatant's cell on the optional 2D combat grid. Absent (null Position) means the combatant is not placed —
// the default, in which the engine behaves exactly as the flat team arena it always has. When present, X is the
// column/lane and Y the depth/row; selectors interpret "front/back" team-relative. Cells are non-exclusive (a
// coordinate is a label; several combatants may share one). Purely opt-in: no engine code reads Position until the
// positional selectors/effects (added additively in later phases) are used by content.
public readonly record struct CombatPosition(int X, int Y);

// How a positional movement node (P2) computes its destination for each moved combatant. ToAbsolute reads two
// coordinate expressions (X, Y); the other modes step a distance along the depth (Y) axis: TowardEnemies /
// AwayFromEnemies use the mover's team-relative forward direction, PushFromSource / PullToSource use the
// source→mover depth direction (push = away from the source, pull = toward it). All are opt-in and no-op for an
// unplaced mover.
public enum MovementMode
{
    ToAbsolute,
    TowardEnemies,
    AwayFromEnemies,
    PushFromSource,
    PullToSource,
}

// A grid axis for positional reads (P3): X = column/lane, Y = depth/row.
public enum GridAxis
{
    X,
    Y,
}

public interface IEffectRequest
{
}

public interface ICombatEvent
{
}

public sealed record CombatLogEntry(
    int Round,
    int Turn,
    string Type,
    string Message
);

public sealed class HealthState
{
    public int Current { get; private set; }
    public int Max { get; private set; }

    public HealthState(int current, int max)
    {
        if (max <= 0)
            throw new ArgumentOutOfRangeException(nameof(max), "Max HP must be greater than 0.");

        if (current < 0)
            throw new ArgumentOutOfRangeException(nameof(current), "Current HP cannot be negative.");

        if (current > max)
            throw new ArgumentOutOfRangeException(nameof(current), "Current HP cannot exceed Max HP.");

        Current = current;
        Max = max;
    }

    public void SetCurrent(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Current HP cannot be negative.");

        if (value > Max)
            throw new ArgumentOutOfRangeException(nameof(value), "Current HP cannot exceed Max HP.");

        Current = value;
    }

    public void SetMax(int value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Max HP must be greater than 0.");

        Max = value;

        if (Current > Max)
            Current = Max;
    }
}

public sealed class ValuePoolState
{
    public int Current { get; private set; }
    public int? Max { get; private set; }
    public bool CanExceedMax { get; }

    public ValuePoolState(int current, int? max = null, bool canExceedMax = false)
    {
        if (current < 0)
            throw new ArgumentOutOfRangeException(nameof(current), "Pool value cannot be negative.");

        if (max is < 0)
            throw new ArgumentOutOfRangeException(nameof(max), "Pool max cannot be negative.");

        if (max.HasValue && current > max.Value && !canExceedMax)
            throw new ArgumentOutOfRangeException(nameof(current), "Pool value cannot exceed max.");

        Current = current;
        Max = max;
        CanExceedMax = canExceedMax;
    }

    public void SetCurrent(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Pool value cannot be negative.");

        if (Max.HasValue && value > Max.Value && !CanExceedMax)
            throw new ArgumentOutOfRangeException(nameof(value), "Pool value cannot exceed max.");

        Current = value;
    }

    public void SetMax(int? value)
    {
        if (value is < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Pool max cannot be negative.");

        Max = value;

        if (Max.HasValue && Current > Max.Value && !CanExceedMax)
            Current = Max.Value;
    }
}

