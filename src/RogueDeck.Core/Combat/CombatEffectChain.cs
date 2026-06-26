namespace RogueDeck.Core.Combat;

public readonly record struct CombatEffectChainId(long Value)
{
    public override string ToString() => Value.ToString();
}

public sealed class CombatEffectChainContext
{
    public const int DefaultMaximumTriggerDepth = 64;

    private readonly IReadOnlyList<TriggeredEffectDefinitionId> _triggeredEffectDefinitionIds;

    public CombatEffectChainId Id { get; }

    public int TriggerDepth => _triggeredEffectDefinitionIds.Count;

    public int MaximumTriggerDepth { get; }

    public IReadOnlyList<TriggeredEffectDefinitionId> TriggeredEffectDefinitionIds =>
        _triggeredEffectDefinitionIds;

    internal CombatEffectChainContext(
        CombatEffectChainId id,
        IEnumerable<TriggeredEffectDefinitionId> triggeredEffectDefinitionIds,
        int maximumTriggerDepth)
    {
        ArgumentNullException.ThrowIfNull(triggeredEffectDefinitionIds);

        if (maximumTriggerDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumTriggerDepth),
                "Maximum trigger depth must be greater than zero.");
        }

        Id = id;
        MaximumTriggerDepth = maximumTriggerDepth;
        _triggeredEffectDefinitionIds =
            Array.AsReadOnly(triggeredEffectDefinitionIds.ToArray());
    }

    public bool ContainsTriggeredEffectDefinition(
        TriggeredEffectDefinitionId definitionId)
    {
        return _triggeredEffectDefinitionIds.Contains(definitionId);
    }

    public bool CanEnterTriggeredEffectDefinition(
        ITriggeredEffectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.ReentryPolicy switch
        {
            TriggeredEffectReentryPolicy.SuppressRecursiveReentry =>
                !ContainsTriggeredEffectDefinition(definition.Id),
            TriggeredEffectReentryPolicy.AllowRecursiveReentry => true,
            _ => throw new InvalidOperationException(
                $"Unsupported triggered effect reentry policy '{definition.ReentryPolicy}'.")
        };
    }

    internal void EnsureCanAppendTriggeredEffectDefinition(
        TriggeredEffectDefinitionId definitionId)
    {
        if (TriggerDepth < MaximumTriggerDepth)
            return;

        throw new InvalidOperationException(
            $"Cannot enter triggered effect definition '{definitionId}' because effect chain '{Id}' reached the maximum trigger depth of {MaximumTriggerDepth}.");
    }

    internal CombatEffectChainContext AppendTriggeredEffectDefinition(
        TriggeredEffectDefinitionId definitionId)
    {
        EnsureCanAppendTriggeredEffectDefinition(definitionId);

        return new CombatEffectChainContext(
            Id,
            _triggeredEffectDefinitionIds.Append(definitionId),
            MaximumTriggerDepth);
    }
}

internal readonly record struct PendingEffectQueueEntry(
    IEffectRequest Request,
    CombatEffectChainContext EffectChain,
    EffectProgramExecutionId? OwningProgramExecutionId);

internal readonly record struct PendingEventQueueEntry(
    ICombatEvent CombatEvent,
    CombatEffectChainContext EffectChain);
