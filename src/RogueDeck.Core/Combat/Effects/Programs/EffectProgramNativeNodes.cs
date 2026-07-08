namespace RogueDeck.Core.Combat;

public sealed class DealDamageNode<TContext> : IDealDamageNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICombatExpression<TContext, int> Amount { get; }
    public EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>? ResultKey { get; }
    public bool IgnoresBlock { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public DealDamageNode(
        ICombatantTargetSelector targetSelector,
        ICombatExpression<TContext, int> amount,
        EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>? resultKey = null,
        bool ignoresBlock = false)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(amount);

        TargetSelector = targetSelector;
        Amount = amount;
        ResultKey = resultKey;
        IgnoresBlock = ignoresBlock;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Amount.GetAllConsumers();

    int IDealDamageNodeCore.EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Amount.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class HealNode<TContext> : IHealNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICombatExpression<TContext, int> Amount { get; }
    public EffectResultKey<OrderedTargetOutcomes<HealOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public HealNode(
        ICombatantTargetSelector targetSelector,
        ICombatExpression<TContext, int> amount,
        EffectResultKey<OrderedTargetOutcomes<HealOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(amount);

        TargetSelector = targetSelector;
        Amount = amount;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Amount.GetAllConsumers();

    int IHealNodeCore.EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Amount.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class ModifyMaxHealthNode<TContext> : IModifyMaxHealthNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICombatExpression<TContext, int> Delta { get; }
    public EffectResultKey<OrderedTargetOutcomes<ModifyMaxHealthOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ModifyMaxHealthNode(
        ICombatantTargetSelector targetSelector,
        ICombatExpression<TContext, int> delta,
        EffectResultKey<OrderedTargetOutcomes<ModifyMaxHealthOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(delta);

        TargetSelector = targetSelector;
        Delta = delta;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Delta.GetAllConsumers();

    int IModifyMaxHealthNodeCore.EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat) =>
        Delta.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class SetHealthNode<TContext> : ISetHealthNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICombatExpression<TContext, int> Value { get; }
    public EffectResultKey<OrderedTargetOutcomes<SetHealthOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public SetHealthNode(
        ICombatantTargetSelector targetSelector,
        ICombatExpression<TContext, int> value,
        EffectResultKey<OrderedTargetOutcomes<SetHealthOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(value);

        TargetSelector = targetSelector;
        Value = value;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Value.GetAllConsumers();

    int ISetHealthNodeCore.EvaluateValue(IEffectExecutionContextCore ctx, CombatState combat) =>
        Value.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public interface IGainBlockNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(GainBlockEffectRequest);
    int EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat);
}

public sealed class GainBlockNode<TContext> : IGainBlockNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICombatExpression<TContext, int> Amount { get; }
    public EffectResultKey<OrderedTargetOutcomes<GainBlockOutcome>>? ResultKey { get; }
    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public GainBlockNode(
        ICombatantTargetSelector targetSelector,
        ICombatExpression<TContext, int> amount,
        EffectResultKey<OrderedTargetOutcomes<GainBlockOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(amount);
        TargetSelector = targetSelector;
        Amount = amount;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Amount.GetAllConsumers();

    int IGainBlockNodeCore.EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Amount.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class ModifyDefensivePoolNode<TContext> : IModifyDefensivePoolNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public DefensivePoolId PoolId { get; }
    public ICombatExpression<TContext, int> Delta { get; }
    public EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ModifyDefensivePoolNode(
        ICombatantTargetSelector targetSelector,
        DefensivePoolId poolId,
        ICombatExpression<TContext, int> delta,
        EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(delta);

        TargetSelector = targetSelector;
        PoolId = poolId;
        Delta = delta;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Delta.GetAllConsumers();

    int IModifyDefensivePoolNodeCore.EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat) =>
        Delta.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class GainResourceNode<TContext> : IGainResourceNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ResourceId ResourceId { get; }
    public ICombatExpression<TContext, int> Amount { get; }
    public int? DefaultMax { get; }
    public EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public GainResourceNode(
        ICombatantTargetSelector targetSelector,
        ResourceId resourceId,
        ICombatExpression<TContext, int> amount,
        int? defaultMax = null,
        EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(amount);

        if (defaultMax is < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultMax), "DefaultMax must not be negative.");

        TargetSelector = targetSelector;
        ResourceId = resourceId;
        Amount = amount;
        DefaultMax = defaultMax;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Amount.GetAllConsumers();

    int IGainResourceNodeCore.EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Amount.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class LoseResourceNode<TContext> : ILoseResourceNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ResourceId ResourceId { get; }
    public ICombatExpression<TContext, int> Amount { get; }
    public EffectResultKey<OrderedTargetOutcomes<LoseResourceOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public LoseResourceNode(
        ICombatantTargetSelector targetSelector,
        ResourceId resourceId,
        ICombatExpression<TContext, int> amount,
        EffectResultKey<OrderedTargetOutcomes<LoseResourceOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(amount);

        TargetSelector = targetSelector;
        ResourceId = resourceId;
        Amount = amount;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Amount.GetAllConsumers();

    int ILoseResourceNodeCore.EvaluateAmount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Amount.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class RefillResourceNode<TContext> : IRefillResourceNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ResourceId ResourceId { get; }
    public int DefaultMax { get; }
    public EffectResultKey<OrderedTargetOutcomes<RefillResourceOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public RefillResourceNode(
        ICombatantTargetSelector targetSelector,
        ResourceId resourceId,
        int defaultMax,
        EffectResultKey<OrderedTargetOutcomes<RefillResourceOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (defaultMax < 0)
            throw new ArgumentOutOfRangeException(nameof(defaultMax), "DefaultMax must not be negative.");

        TargetSelector = targetSelector;
        ResourceId = resourceId;
        DefaultMax = defaultMax;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
}

public sealed class ApplyStatusNode<TContext> : IApplyStatusNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public StatusDefinitionId StatusDefinitionId { get; }
    public ICombatExpression<TContext, int> Stacks { get; }
    public int DurationTurns { get; }
    public int Charges { get; }
    public EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ApplyStatusNode(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        ICombatExpression<TContext, int> stacks,
        int durationTurns = 0,
        int charges = 0,
        EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(stacks);

        if (durationTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(durationTurns), "DurationTurns must not be negative.");

        if (charges < 0)
            throw new ArgumentOutOfRangeException(nameof(charges), "Charges must not be negative.");

        TargetSelector = targetSelector;
        StatusDefinitionId = statusDefinitionId;
        Stacks = stacks;
        DurationTurns = durationTurns;
        Charges = charges;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Stacks.GetAllConsumers();

    int IApplyStatusNodeCore.EvaluateStacks(IEffectExecutionContextCore ctx, CombatState combat) =>
        Stacks.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class RemoveStatusNode<TContext> : IRemoveStatusNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public StatusDefinitionId StatusDefinitionId { get; }
    public EffectResultKey<OrderedTargetOutcomes<RemoveStatusOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public RemoveStatusNode(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        EffectResultKey<OrderedTargetOutcomes<RemoveStatusOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        TargetSelector = targetSelector;
        StatusDefinitionId = statusDefinitionId;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
}

public sealed class RemoveStatusesByPolarityNode<TContext> : IRemoveStatusesByPolarityNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public StatusPolarity Polarity { get; }
    public EffectResultKey<OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public RemoveStatusesByPolarityNode(
        ICombatantTargetSelector targetSelector,
        StatusPolarity polarity,
        EffectResultKey<OrderedTargetOutcomes<RemoveStatusesByPolarityOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        TargetSelector = targetSelector;
        Polarity = polarity;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
}

public sealed class ModifyStatusStacksNode<TContext> : IModifyStatusStacksNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public StatusDefinitionId StatusDefinitionId { get; }
    public ICombatExpression<TContext, int> Delta { get; }
    public EffectResultKey<OrderedTargetOutcomes<ModifyStatusStacksOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ModifyStatusStacksNode(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        ICombatExpression<TContext, int> delta,
        EffectResultKey<OrderedTargetOutcomes<ModifyStatusStacksOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(delta);

        TargetSelector = targetSelector;
        StatusDefinitionId = statusDefinitionId;
        Delta = delta;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Delta.GetAllConsumers();

    int IModifyStatusStacksNodeCore.EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat) =>
        Delta.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class ModifyStatusDurationNode<TContext> : IModifyStatusDurationNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public StatusDefinitionId StatusDefinitionId { get; }
    public ICombatExpression<TContext, int> Delta { get; }
    public EffectResultKey<OrderedTargetOutcomes<ModifyStatusDurationOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ModifyStatusDurationNode(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        ICombatExpression<TContext, int> delta,
        EffectResultKey<OrderedTargetOutcomes<ModifyStatusDurationOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(delta);

        TargetSelector = targetSelector;
        StatusDefinitionId = statusDefinitionId;
        Delta = delta;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Delta.GetAllConsumers();

    int IModifyStatusDurationNodeCore.EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat) =>
        Delta.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class ModifyStatusChargesNode<TContext> : IModifyStatusChargesNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public StatusDefinitionId StatusDefinitionId { get; }
    public ICombatExpression<TContext, int> Delta { get; }
    public EffectResultKey<OrderedTargetOutcomes<ModifyStatusChargesOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ModifyStatusChargesNode(
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        ICombatExpression<TContext, int> delta,
        EffectResultKey<OrderedTargetOutcomes<ModifyStatusChargesOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(delta);

        TargetSelector = targetSelector;
        StatusDefinitionId = statusDefinitionId;
        Delta = delta;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Delta.GetAllConsumers();

    int IModifyStatusChargesNodeCore.EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat) =>
        Delta.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class DrawCardsNode<TContext> : IDrawCardsNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICombatExpression<TContext, int> Count { get; }
    public EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public DrawCardsNode(
        ICombatantTargetSelector targetSelector,
        ICombatExpression<TContext, int> count,
        EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(count);

        TargetSelector = targetSelector;
        Count = count;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Count.GetAllConsumers();

    int IDrawCardsNodeCore.EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Count.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

public sealed class MoveAllCardsFromZoneNode<TContext> : IMoveAllCardsFromZoneNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public CardZone FromZone { get; }
    public CardZone ToZone { get; }
    public EffectResultKey<OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public MoveAllCardsFromZoneNode(
        ICombatantTargetSelector targetSelector,
        CardZone fromZone,
        CardZone toZone,
        EffectResultKey<OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        TargetSelector = targetSelector;
        FromZone = fromZone;
        ToZone = toZone;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
}

public sealed class CreateCardInstanceNode<TContext> : ICreateCardInstanceNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public CardDefinitionId CardDefinitionId { get; }
    public CardZone ToZone { get; }
    public ICombatExpression<TContext, int> Count { get; }
    public EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public CreateCardInstanceNode(
        ICombatantTargetSelector targetSelector,
        CardDefinitionId cardDefinitionId,
        CardZone toZone,
        ICombatExpression<TContext, int>? count = null,
        EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        TargetSelector = targetSelector;
        CardDefinitionId = cardDefinitionId;
        ToZone = toZone;
        Count = count ?? new ConstantExpression<TContext>(1);
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Count.GetAllConsumers();

    int ICreateCardInstanceNodeCore.EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Count.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

// Creates a copy of a card whose definition is read from an existing card instance (e.g. the played
// card) instead of a constant id — composes "put a copy of the last card you played into your hand".
public sealed class CreateCardCopyNode<TContext> : ICreateCardCopyNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ICardInstanceExpression<TContext> SourceCard { get; }
    public CardZone ToZone { get; }
    public ICombatExpression<TContext, int> Count { get; }
    public EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public CreateCardCopyNode(
        ICombatantTargetSelector targetSelector,
        ICardInstanceExpression<TContext> sourceCard,
        CardZone toZone,
        ICombatExpression<TContext, int>? count = null,
        EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(sourceCard);

        TargetSelector = targetSelector;
        SourceCard = sourceCard;
        ToZone = toZone;
        Count = count ?? new ConstantExpression<TContext>(1);
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Count.GetAllConsumers();

    int ICreateCardCopyNodeCore.EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Count.Evaluate((EffectExecutionContext<TContext>)ctx, combat);

    CardDefinitionId? ICreateCardCopyNodeCore.EvaluateSourceDefinitionId(
        IEffectExecutionContextCore ctx, CombatState combat)
    {
        if (SourceCard.Evaluate((EffectExecutionContext<TContext>)ctx, combat) is not { } instanceId)
            return null;

        foreach (var zones in combat.CardZonesByCombatant.Values)
            if (zones.ContainsCard(instanceId))
                return zones.GetCard(instanceId).DefinitionId;
        return null;
    }
}

// Re-runs a card's on-play program against a chosen target (echo / double-cast). The card is identified by
// a card-instance expression (e.g. the played card from a CardPlayed trigger); the definition's program is
// resolved and executed at runtime as an independent sub-program in a fresh CardPlayContext.
public sealed class ReplayCardProgramNode<TContext> : IReplayCardProgramNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICardInstanceExpression<TContext> Card { get; }
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ReplayCardProgramNode(
        ICardInstanceExpression<TContext> card,
        ICombatantTargetSelector targetSelector)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(targetSelector);

        Card = card;
        TargetSelector = targetSelector;
    }

    CardDefinitionId? IReplayCardProgramNodeCore.EvaluateCardDefinitionId(
        IEffectExecutionContextCore ctx, CombatState combat)
    {
        if (Card.Evaluate((EffectExecutionContext<TContext>)ctx, combat) is not { } instanceId)
            return null;

        foreach (var zones in combat.CardZonesByCombatant.Values)
            if (zones.ContainsCard(instanceId))
                return zones.GetCard(instanceId).DefinitionId;
        return null;
    }
}

// Summons a new combatant onto the given team with a computed max HP. No target selector; produces the
// summoned id as its outcome so a follow-up can target the new combatant.
public sealed class SummonCombatantNode<TContext> : ISummonCombatantNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public TeamId TeamId { get; }
    public CombatantDefinitionId DefinitionId { get; }
    public string DisplayNameKey { get; }
    public ICombatExpression<TContext, int> MaxHealth { get; }
    public EffectResultKey<SummonCombatantOutcome>? ResultKey { get; }
    public CombatPosition? Position { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public SummonCombatantNode(
        TeamId teamId,
        ICombatExpression<TContext, int> maxHealth,
        CombatantDefinitionId definitionId,
        string displayNameKey,
        EffectResultKey<SummonCombatantOutcome>? resultKey = null,
        CombatPosition? position = null)
    {
        ArgumentNullException.ThrowIfNull(maxHealth);
        if (string.IsNullOrWhiteSpace(displayNameKey))
            throw new ArgumentException("Display name key cannot be empty.", nameof(displayNameKey));

        TeamId = teamId;
        MaxHealth = maxHealth;
        DefinitionId = definitionId;
        DisplayNameKey = displayNameKey;
        ResultKey = resultKey;
        Position = position;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelectorCardinality.ExactlyOne)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => MaxHealth.GetAllConsumers();

    int ISummonCombatantNodeCore.EvaluateMaxHealth(IEffectExecutionContextCore ctx, CombatState combat) =>
        MaxHealth.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

// Positional movement (P2): moves the target combatant(s) on the grid. The destination per target is derived from
// Mode — ToAbsolute uses the X/Y expressions, the depth-axis modes use Step. Enqueues a MoveCombatantEffectRequest
// per target; no result outcome.
public sealed class MoveCombatantNode<TContext> : IMoveCombatantNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public MovementMode Mode { get; }
    public ICombatExpression<TContext, int>? X { get; }
    public ICombatExpression<TContext, int>? Y { get; }
    public ICombatExpression<TContext, int>? Step { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public MoveCombatantNode(
        ICombatantTargetSelector targetSelector,
        MovementMode mode,
        ICombatExpression<TContext, int>? x = null,
        ICombatExpression<TContext, int>? y = null,
        ICombatExpression<TContext, int>? step = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (mode == MovementMode.ToAbsolute)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(step);
        }

        TargetSelector = targetSelector;
        Mode = mode;
        X = x;
        Y = y;
        Step = step;
    }

    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers()
    {
        IEnumerable<IResultKeyConsumer> consumers = [];
        if (X is not null)
            consumers = consumers.Concat(X.GetAllConsumers());
        if (Y is not null)
            consumers = consumers.Concat(Y.GetAllConsumers());
        if (Step is not null)
            consumers = consumers.Concat(Step.GetAllConsumers());
        return consumers;
    }

    (int X, int Y) IMoveCombatantNodeCore.EvaluateAbsolute(IEffectExecutionContextCore ctx, CombatState combat)
    {
        var typedCtx = (EffectExecutionContext<TContext>)ctx;
        return (X?.Evaluate(typedCtx, combat) ?? 0, Y?.Evaluate(typedCtx, combat) ?? 0);
    }

    int IMoveCombatantNodeCore.EvaluateStep(IEffectExecutionContextCore ctx, CombatState combat) =>
        Step?.Evaluate((EffectExecutionContext<TContext>)ctx, combat) ?? 0;
}

// Positional movement (P2): swaps the grid cells of the first target of each selector. A no-op when either side is
// absent or unplaced. Enqueues (up to) two MoveCombatantEffectRequests.
public sealed class SwapPositionsNode<TContext> : ISwapPositionsNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector FirstSelector { get; }
    public ICombatantTargetSelector SecondSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [FirstSelector, SecondSelector];

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public SwapPositionsNode(
        ICombatantTargetSelector firstSelector,
        ICombatantTargetSelector secondSelector)
    {
        ArgumentNullException.ThrowIfNull(firstSelector);
        ArgumentNullException.ThrowIfNull(secondSelector);

        FirstSelector = firstSelector;
        SecondSelector = secondSelector;
    }
}

public sealed class SetCombatantLifecycleStateNode<TContext> : ISetCombatantLifecycleStateNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public CombatantLifecycleState LifecycleState { get; }
    public EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public SetCombatantLifecycleStateNode(
        ICombatantTargetSelector targetSelector,
        CombatantLifecycleState lifecycleState,
        EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        TargetSelector = targetSelector;
        LifecycleState = lifecycleState;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
}

public sealed class ChangeCombatantTeamNode<TContext> : IChangeCombatantTeamNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public TeamId TeamId { get; }
    public EffectResultKey<OrderedTargetOutcomes<ChangeCombatantTeamOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ChangeCombatantTeamNode(
        ICombatantTargetSelector targetSelector,
        TeamId teamId,
        EffectResultKey<OrderedTargetOutcomes<ChangeCombatantTeamOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);

        TargetSelector = targetSelector;
        TeamId = teamId;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
}

// ── ModifyResourceNode ────────────────────────────────────────────────────────

public interface IModifyResourceNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector TargetSelector { get; }
    ResourceId ResourceId { get; }
    int? Min { get; }
    int? Max { get; }
    EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>? ResultKey { get; }

    int EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat);

    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(ModifyResourceEffectRequest);
}

public sealed class ModifyResourceNode<TContext> : IModifyResourceNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector TargetSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [TargetSelector];
    public ResourceId ResourceId { get; }
    public ICombatExpression<TContext, int> Delta { get; }
    public int? Min { get; }
    public int? Max { get; }
    public EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public ModifyResourceNode(
        ICombatantTargetSelector targetSelector,
        ResourceId resourceId,
        ICombatExpression<TContext, int> delta,
        int? min = null,
        int? max = null,
        EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(targetSelector);
        ArgumentNullException.ThrowIfNull(delta);

        TargetSelector = targetSelector;
        ResourceId = resourceId;
        Delta = delta;
        Min = min;
        Max = max;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelector.Cardinality)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => Delta.GetAllConsumers();

    int IModifyResourceNodeCore.EvaluateDelta(IEffectExecutionContextCore ctx, CombatState combat) =>
        Delta.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

// ── MoveCardToZoneNode ────────────────────────────────────────────────────────

public interface IMoveCardToZoneNodeCore : INativeEffectOperationNode
{
    ICombatantTargetSelector OwnerSelector { get; }
    CardZone ToZone { get; }
    EffectResultKey<MoveCardToZoneOutcome>? ResultKey { get; }

    CardInstanceId? EvaluateCardInstanceId(IEffectExecutionContextCore ctx, CombatState combat);

    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(MoveCardToZoneEffectRequest);
}

public sealed class MoveCardToZoneNode<TContext> : IMoveCardToZoneNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector OwnerSelector { get; }
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [OwnerSelector];
    public ICardInstanceExpression<TContext> CardExpression { get; }
    public CardZone ToZone { get; }
    public EffectResultKey<MoveCardToZoneOutcome>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public MoveCardToZoneNode(
        ICombatantTargetSelector ownerSelector,
        ICardInstanceExpression<TContext> cardExpression,
        CardZone toZone,
        EffectResultKey<MoveCardToZoneOutcome>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(ownerSelector);
        ArgumentNullException.ThrowIfNull(cardExpression);

        OwnerSelector = ownerSelector;
        CardExpression = cardExpression;
        ToZone = toZone;
        ResultKey = resultKey;
    }

    // The result is a single MoveCardToZoneOutcome (one card moved), so it is single by nature.
    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelectorCardinality.ExactlyOne)
            : null;
    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() => [];

    CardInstanceId? IMoveCardToZoneNodeCore.EvaluateCardInstanceId(IEffectExecutionContextCore ctx, CombatState combat) =>
        CardExpression.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

// ── SetCombatResultNode ───────────────────────────────────────────────────────

public interface ISetCombatResultNodeCore : INativeEffectOperationNode
{
    CombatResult Result { get; }
    EffectResultKey<SetCombatResultOutcome>? ResultKey { get; }

    Type INativeEffectOperationNode.ProducedEffectRequestType => typeof(SetCombatResultEffectRequest);
}

public sealed class SetCombatResultNode<TContext> : ISetCombatResultNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public CombatResult Result { get; }
    public EffectResultKey<SetCombatResultOutcome>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public SetCombatResultNode(
        CombatResult result,
        EffectResultKey<SetCombatResultOutcome>? resultKey = null)
    {
        Result = result;
        ResultKey = resultKey;
    }

    // SetCombatResult has no target selector; its single outcome is inherently single.
    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelectorCardinality.ExactlyOne)
            : null;
}

public sealed class PlayCardNode<TContext> : IPlayCardNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatantTargetSelector PlayerSelector { get; }
    // Both selectors this node resolves against the current program context are reported so build
    // preflight validates the optional CardTargetSelector (capability / domain / eligibility) the same
    // way as the player selector — the forwarded card target must not be an unvalidated runtime path.
    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() =>
        CardTargetSelector is { } cardTarget ? [PlayerSelector, cardTarget] : [PlayerSelector];
    public ICardInstanceExpression<TContext> CardExpression { get; }
    public ICombatantTargetSelector? CardTargetSelector { get; }
    public EffectResultKey<OrderedTargetOutcomes<PlayCardOutcome>>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public PlayCardNode(
        ICombatantTargetSelector playerSelector,
        ICardInstanceExpression<TContext> cardExpression,
        ICombatantTargetSelector? cardTargetSelector = null,
        EffectResultKey<OrderedTargetOutcomes<PlayCardOutcome>>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(playerSelector);
        ArgumentNullException.ThrowIfNull(cardExpression);

        PlayerSelector = playerSelector;
        CardExpression = cardExpression;
        CardTargetSelector = cardTargetSelector;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], PlayerSelector.Cardinality)
            : null;

    CardInstanceId? IPlayCardNodeCore.EvaluateCardInstanceId(IEffectExecutionContextCore ctx, CombatState combat) =>
        CardExpression.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
}

// ── InstallTemporaryRuleNode ──────────────────────────────────────────────────
//
// Installs a temporary triggered program (a delayed effect or temporary rule) on the
// live combat. The rule definition is authored at content-build time and runs through
// the standard trigger runtime once installed. GetInstalledProgramRoot exposes the
// rule's program so the registry build preflight can validate it like any other.

public interface IInstallTemporaryRuleNodeCore : INativeEffectOperationNode
{
    ITriggeredEffectDefinition RuleDefinition { get; }
    TemporaryRuleLifetime Lifetime { get; }
    IReadOnlyList<IEffectRequest> ExpiryEffects { get; }
    EffectResultKey<InstallTemporaryRuleOutcome>? ResultKey { get; }

    IEffectNode? GetInstalledProgramRoot() => RuleDefinition.GetEffectProgramRoot();

    Type INativeEffectOperationNode.ProducedEffectRequestType =>
        typeof(InstallTemporaryRuleEffectRequest);
}

public sealed class InstallTemporaryRuleNode<TContext>
    : IInstallTemporaryRuleNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ITriggeredEffectDefinition RuleDefinition { get; }
    public TemporaryRuleLifetime Lifetime { get; }
    public IReadOnlyList<IEffectRequest> ExpiryEffects { get; }
    public EffectResultKey<InstallTemporaryRuleOutcome>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public InstallTemporaryRuleNode(
        ITriggeredEffectDefinition ruleDefinition,
        TemporaryRuleLifetime? lifetime = null,
        EffectResultKey<InstallTemporaryRuleOutcome>? resultKey = null,
        IReadOnlyList<IEffectRequest>? expiryEffects = null)
    {
        ArgumentNullException.ThrowIfNull(ruleDefinition);

        RuleDefinition = ruleDefinition;
        Lifetime = lifetime ?? TemporaryRuleLifetime.Unlimited;
        ExpiryEffects = expiryEffects ?? Array.Empty<IEffectRequest>();
        ResultKey = resultKey;
    }

    // Single outcome (the installed rule's id); inherently single.
    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelectorCardinality.ExactlyOne)
            : null;
}

// ── RemoveTemporaryRuleNode ───────────────────────────────────────────────────
//
// Explicitly removes a previously installed temporary triggered program by id.

public interface IRemoveTemporaryRuleNodeCore : INativeEffectOperationNode
{
    TriggeredEffectDefinitionId RuleId { get; }
    EffectResultKey<RemoveTemporaryRuleOutcome>? ResultKey { get; }

    Type INativeEffectOperationNode.ProducedEffectRequestType =>
        typeof(RemoveTemporaryRuleEffectRequest);
}

public sealed class RemoveTemporaryRuleNode<TContext>
    : IRemoveTemporaryRuleNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public TriggeredEffectDefinitionId RuleId { get; }
    public EffectResultKey<RemoveTemporaryRuleOutcome>? ResultKey { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public RemoveTemporaryRuleNode(
        TriggeredEffectDefinitionId ruleId,
        EffectResultKey<RemoveTemporaryRuleOutcome>? resultKey = null)
    {
        ArgumentNullException.ThrowIfNull(ruleId.value);
        RuleId = ruleId;
        ResultKey = resultKey;
    }

    public ProducedResult? GetProducedResult() =>
        ResultKey is { } key
            ? new ProducedResult(key.Name, key.GetType().GenericTypeArguments[0], TargetSelectorCardinality.ExactlyOne)
            : null;
}
