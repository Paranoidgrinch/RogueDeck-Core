namespace RogueDeck.Core.Combat;

public sealed class SequenceEffectNode<TContext> : IEffectNode<TContext>
{
    private readonly IEffectNode<TContext>[] _children;

    public IReadOnlyList<IEffectNode<TContext>> Children => _children;

    public string GetChildPathSegment(int childIndex) => $"sequence[{childIndex}]";

    // The list constructor is the one JSON uses (its parameter type matches the Children property, which
    // IEnumerable does not); the IEnumerable overload delegates to it.
    [System.Text.Json.Serialization.JsonConstructor]
    public SequenceEffectNode(IReadOnlyList<IEffectNode<TContext>> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        _children = children.ToArray();
    }

    public SequenceEffectNode(IEnumerable<IEffectNode<TContext>> children)
        : this((IReadOnlyList<IEffectNode<TContext>>)(children?.ToArray() ?? throw new ArgumentNullException(nameof(children))))
    {
    }
}

public sealed class NoOpEffectNode<TContext> : IEffectNode<TContext>
{
    public IReadOnlyList<IEffectNode<TContext>> Children =>
        Array.Empty<IEffectNode<TContext>>();

}

public sealed class CausalSequenceEffectNode<TContext> : IEffectNode<TContext>
{
    private readonly IEffectNode<TContext>[] _children;

    public IReadOnlyList<IEffectNode<TContext>> Children => _children;

    public string GetChildPathSegment(int childIndex) => $"causal[{childIndex}]";

    // The list constructor is the one JSON uses (its parameter type matches the Children property, which
    // IEnumerable does not); the IEnumerable overload delegates to it.
    [System.Text.Json.Serialization.JsonConstructor]
    public CausalSequenceEffectNode(IReadOnlyList<IEffectNode<TContext>> children)
    {
        ArgumentNullException.ThrowIfNull(children);

        _children = children.ToArray();
    }

    public CausalSequenceEffectNode(IEnumerable<IEffectNode<TContext>> children)
        : this((IReadOnlyList<IEffectNode<TContext>>)(children?.ToArray() ?? throw new ArgumentNullException(nameof(children))))
    {
    }
}

public sealed class ConditionalEffectNode<TContext> : IConditionalNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public ICombatExpression<TContext, bool> Condition { get; }
    public IEffectNode<TContext> Then { get; }
    public IEffectNode<TContext>? Else { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children { get; }

    public string GetChildPathSegment(int childIndex) => childIndex switch
    {
        0 => "conditional.then",
        1 => "conditional.else",
        _ => throw new ArgumentOutOfRangeException(nameof(childIndex)),
    };

    public ConditionalEffectNode(
        ICombatExpression<TContext, bool> condition,
        IEffectNode<TContext> then,
        IEffectNode<TContext>? @else = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);

        Condition = condition;
        Then = then;
        Else = @else;
        Children = @else is not null
            ? (IReadOnlyList<IEffectNode<TContext>>)[then, @else]
            : [then];
    }

    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() =>
        Condition.GetAllConsumers();

    bool IConditionalNodeCore.EvaluateCondition(IEffectExecutionContextCore ctx, CombatState combat) =>
        Condition.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
    IEffectNode IConditionalNodeCore.Then => Then;
    IEffectNode? IConditionalNodeCore.Else => Else;
}

public sealed class SideEffectNode<TContext> : ISideEffectNodeCore, IEffectNode<TContext>
    where TContext : class
{
    private readonly Action<IEffectExecutionContextCore, CombatState> _effect;

    public IReadOnlyList<IEffectNode<TContext>> Children => [];

    public SideEffectNode(Action<IEffectExecutionContextCore, CombatState> effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _effect = effect;
    }

    void ISideEffectNodeCore.Execute(IEffectExecutionContextCore ctx, CombatState combat)
        => _effect(ctx, combat);
}

public sealed class RepeatEffectNode<TContext> : IRepeatNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public const int DefaultMaxCount = 32;

    public ICombatExpression<TContext, int> Count { get; }
    public IEffectNode<TContext> Body { get; }
    public int MaxCount { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [Body];

    public string GetChildPathSegment(int childIndex) => childIndex == 0
        ? "repeat.body"
        : throw new ArgumentOutOfRangeException(nameof(childIndex));

    public RepeatEffectNode(
        ICombatExpression<TContext, int> count,
        IEffectNode<TContext> body,
        int maxCount = DefaultMaxCount)
    {
        ArgumentNullException.ThrowIfNull(count);
        ArgumentNullException.ThrowIfNull(body);

        if (maxCount <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxCount),
                "Maximum repeat count must be greater than zero.");

        Count = count;
        Body = body;
        MaxCount = maxCount;
    }

    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() =>
        Count.GetAllConsumers();

    int IRepeatNodeCore.EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Count.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
    IEffectNode IRepeatNodeCore.Body => Body;
}

// Repeat-until: runs Body, then evaluates StopCondition; repeats while it is false, stopping when it
// becomes true or MaxIterations is reached. Body runs at least once; the 0-based pass number is exposed
// to the body as IterationIndex (so a body can escalate per pass, e.g. Avalanche's +1 each pass).
public sealed class RepeatUntilEffectNode<TContext> : IRepeatUntilNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public const int DefaultMaxIterations = 32;

    public ICombatExpression<TContext, bool> StopCondition { get; }
    public IEffectNode<TContext> Body { get; }
    public int MaxIterations { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [Body];

    public string GetChildPathSegment(int childIndex) => childIndex == 0
        ? "repeatUntil.body"
        : throw new ArgumentOutOfRangeException(nameof(childIndex));

    public RepeatUntilEffectNode(
        ICombatExpression<TContext, bool> stopCondition,
        IEffectNode<TContext> body,
        int maxIterations = DefaultMaxIterations)
    {
        ArgumentNullException.ThrowIfNull(stopCondition);
        ArgumentNullException.ThrowIfNull(body);

        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxIterations),
                "Maximum iteration count must be greater than zero.");

        StopCondition = stopCondition;
        Body = body;
        MaxIterations = maxIterations;
    }

    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() =>
        StopCondition.GetAllConsumers();

    bool IRepeatUntilNodeCore.EvaluateStopCondition(IEffectExecutionContextCore ctx, CombatState combat) =>
        StopCondition.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
    IEffectNode IRepeatUntilNodeCore.Body => Body;
}

public sealed class RandomTargetSelectionNode<TContext> : IRandomTargetSelectionNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public const int DefaultMaxIterations = 32;

    public ICombatantTargetSelector CandidateSelector { get; }
    public ICombatExpression<TContext, int> Count { get; }
    public IEffectNode<TContext> Body { get; }
    public int MaxIterations { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [Body];

    public string GetChildPathSegment(int childIndex) => childIndex == 0
        ? "randomTargets.body"
        : throw new ArgumentOutOfRangeException(nameof(childIndex));

    public RandomTargetSelectionNode(
        ICombatantTargetSelector candidateSelector,
        ICombatExpression<TContext, int> count,
        IEffectNode<TContext> body,
        int maxIterations = DefaultMaxIterations)
    {
        ArgumentNullException.ThrowIfNull(candidateSelector);
        ArgumentNullException.ThrowIfNull(count);
        ArgumentNullException.ThrowIfNull(body);

        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxIterations),
                "Maximum iterations must be greater than zero.");

        CandidateSelector = candidateSelector;
        Count = count;
        Body = body;
        MaxIterations = maxIterations;
    }

    public IEnumerable<IResultKeyConsumer> GetExpressionConsumers() =>
        Count.GetAllConsumers();

    int IRandomTargetSelectionNodeCore.EvaluateCount(IEffectExecutionContextCore ctx, CombatState combat) =>
        Count.Evaluate((EffectExecutionContext<TContext>)ctx, combat);
    IEffectNode IRandomTargetSelectionNodeCore.Body => Body;
}

public sealed class ForEachTargetEffectNode<TContext> : IForEachNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public const int DefaultMaxIterations = 32;

    public ICombatantTargetSelector CollectionSelector { get; }
    public IEffectNode<TContext> Body { get; }
    public int MaxIterations { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [Body];

    public string GetChildPathSegment(int childIndex) => childIndex == 0
        ? "forEach.body"
        : throw new ArgumentOutOfRangeException(nameof(childIndex));

    public ForEachTargetEffectNode(
        ICombatantTargetSelector collectionSelector,
        IEffectNode<TContext> body,
        int maxIterations = DefaultMaxIterations)
    {
        ArgumentNullException.ThrowIfNull(collectionSelector);
        ArgumentNullException.ThrowIfNull(body);

        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxIterations),
                "Maximum iterations must be greater than zero.");

        CollectionSelector = collectionSelector;
        Body = body;
        MaxIterations = maxIterations;
    }

    IEffectNode IForEachNodeCore.Body => Body;
}

// Runs its body once per card in a combatant's zone (optionally only cards whose definition matches the filter
// and/or whose definition carries the tag, optionally only the first N matches in zone order), binding each card
// as the iteration card so a card op in the body (via IteratedCardExpression) targets it. The card-domain
// counterpart of ForEachTargetEffectNode: "upgrade every Strike in hand", "exhaust all junk cards in hand".
// The card list is snapshotted at loop start, so moving/transforming a card in the body doesn't disturb the walk.
public sealed class ForEachCardInZoneNode<TContext> : IForEachCardInZoneNodeCore, IEffectNode<TContext>
    where TContext : class
{
    public const int DefaultMaxIterations = 64;

    public ICombatantTargetSelector OwnerSelector { get; }
    public CardZone Zone { get; }
    public CardDefinitionId? DefinitionFilter { get; }
    public TagId? TagFilter { get; }
    // Null stays out of the wire format so every program written before marks could be searched round-trips
    // byte-identically — the older filters beside it predate that rule and still write their nulls.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public TagId? MarkFilter { get; }
    public int? TakeFirst { get; }
    public IEffectNode<TContext> Body { get; }
    public int MaxIterations { get; }

    public IReadOnlyList<IEffectNode<TContext>> Children => [Body];

    public string GetChildPathSegment(int childIndex) => childIndex == 0
        ? "forEachCard.body"
        : throw new ArgumentOutOfRangeException(nameof(childIndex));

    public IEnumerable<ICombatantTargetSelector> GetTargetSelectors() => [OwnerSelector];

    public ForEachCardInZoneNode(
        ICombatantTargetSelector ownerSelector,
        CardZone zone,
        IEffectNode<TContext> body,
        CardDefinitionId? definitionFilter = null,
        int maxIterations = DefaultMaxIterations,
        TagId? tagFilter = null,
        int? takeFirst = null,
        TagId? markFilter = null)
    {
        ArgumentNullException.ThrowIfNull(ownerSelector);
        ArgumentNullException.ThrowIfNull(body);

        if (maxIterations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxIterations),
                "Maximum iterations must be greater than zero.");
        if (takeFirst is <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(takeFirst),
                "Take-first must be greater than zero (or null for all matches).");

        OwnerSelector = ownerSelector;
        Zone = zone;
        Body = body;
        DefinitionFilter = definitionFilter;
        TagFilter = tagFilter;
        MarkFilter = markFilter;
        TakeFirst = takeFirst;
        MaxIterations = maxIterations;
    }

    IEffectNode IForEachCardInZoneNodeCore.Body => Body;
}
