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

