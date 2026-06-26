namespace RogueDeck.Core.Combat;

public sealed class CombatExecutionLimits
{
    public static readonly CombatExecutionLimits Default = new();

    public int MaxQueueCycles { get; }
    public int MaxEffectsPerCycle { get; }
    public int MaxEventsPerCycle { get; }

    public CombatExecutionLimits(
        int maxQueueCycles = 1024,
        int maxEffectsPerCycle = 1024,
        int maxEventsPerCycle = 1024)
    {
        if (maxQueueCycles <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxQueueCycles),
                "Maximum queue cycles must be greater than zero.");

        if (maxEffectsPerCycle <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxEffectsPerCycle),
                "Maximum effects per cycle must be greater than zero.");

        if (maxEventsPerCycle <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxEventsPerCycle),
                "Maximum events per cycle must be greater than zero.");

        MaxQueueCycles = maxQueueCycles;
        MaxEffectsPerCycle = maxEffectsPerCycle;
        MaxEventsPerCycle = maxEventsPerCycle;
    }
}
