using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

/// <summary>
/// Immutable runtime view of all combat definitions. The only way to obtain one is
/// <see cref="CombatDefinitionRegistryBuilder.Build"/>; combat therefore cannot run against an
/// unbuilt registry. All members are read-only — registration lives on the builder.
/// </summary>
public sealed class CombatDefinitionRegistry
{
    private readonly ImmutableDictionary<StatusDefinitionId, StatusDefinition> _statusDefinitions;
    private readonly ImmutableDictionary<CardDefinitionId, CardDefinition> _cardDefinitions;
    private readonly ImmutableDictionary<Type, IEffectRequestHandler> _effectRequestHandlers;
    private readonly ImmutableDictionary<EnemyActionDefinitionId, EnemyActionDefinition> _enemyActionDefinitions;
    private readonly ImmutableDictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> _triggeredEffectDefinitions;
    // Lookup-ONLY bodies for temporary triggered rules (save/restore re-link). Unlike _triggeredEffectDefinitions
    // these are NOT active permanent rules — they never fire on their own; they only let CombatState.Restore
    // rebuild a temporary rule's program body by id.
    private readonly ImmutableDictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> _temporaryRuleDefinitions;
    private readonly ImmutableArray<IDamageAmountModifier> _damageAmountModifiers;
    private readonly ImmutableArray<ICardPlayValidator> _cardPlayValidators;
    private readonly ImmutableArray<ICardCostModifier> _cardCostModifiers;
    private readonly ImmutableArray<IStatusApplicationInterceptor> _statusApplicationInterceptors;
    private readonly ImmutableArray<IPreDownInterceptor> _preDownInterceptors;
    private readonly ImmutableArray<IDamageSplitter> _damageSplitters;
    private readonly ImmutableArray<IBlockAmountModifier> _blockAmountModifiers;
    private readonly ImmutableDictionary<DefensivePoolId, DefensivePoolDefinition> _defensivePoolDefinitions;
    // Precomputed absorb order (lowest AbsorbPriority first, id as a deterministic tie-break).
    private readonly ImmutableArray<DefensivePoolDefinition> _defensivePoolsInAbsorbOrder;
    private readonly ImmutableDictionary<Type, ImmutableArray<ICombatEventHandler>> _combatEventHandlers;
    private readonly EffectNodeExecutorRegistry _nodeExecutorRegistry;

    internal CombatDefinitionRegistry(
        ImmutableDictionary<StatusDefinitionId, StatusDefinition> statusDefinitions,
        ImmutableDictionary<CardDefinitionId, CardDefinition> cardDefinitions,
        ImmutableDictionary<Type, IEffectRequestHandler> effectRequestHandlers,
        ImmutableDictionary<EnemyActionDefinitionId, EnemyActionDefinition> enemyActionDefinitions,
        ImmutableDictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> triggeredEffectDefinitions,
        ImmutableDictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> temporaryRuleDefinitions,
        ImmutableArray<IDamageAmountModifier> damageAmountModifiers,
        ImmutableArray<ICardPlayValidator> cardPlayValidators,
        ImmutableArray<ICardCostModifier> cardCostModifiers,
        ImmutableArray<IStatusApplicationInterceptor> statusApplicationInterceptors,
        ImmutableArray<IPreDownInterceptor> preDownInterceptors,
        ImmutableArray<IDamageSplitter> damageSplitters,
        ImmutableArray<IBlockAmountModifier> blockAmountModifiers,
        ImmutableDictionary<DefensivePoolId, DefensivePoolDefinition> defensivePoolDefinitions,
        ImmutableDictionary<Type, ImmutableArray<ICombatEventHandler>> combatEventHandlers,
        EffectNodeExecutorRegistry nodeExecutorRegistry,
        bool allowsUnsafeSideEffects)
    {
        AllowsUnsafeSideEffects = allowsUnsafeSideEffects;
        _statusDefinitions = statusDefinitions;
        _cardDefinitions = cardDefinitions;
        _effectRequestHandlers = effectRequestHandlers;
        _enemyActionDefinitions = enemyActionDefinitions;
        _triggeredEffectDefinitions = triggeredEffectDefinitions;
        _temporaryRuleDefinitions = temporaryRuleDefinitions;
        _damageAmountModifiers = damageAmountModifiers;
        _cardPlayValidators = cardPlayValidators;
        _cardCostModifiers = cardCostModifiers;
        _statusApplicationInterceptors = statusApplicationInterceptors;
        _preDownInterceptors = preDownInterceptors;
        _damageSplitters = damageSplitters;
        _blockAmountModifiers = blockAmountModifiers;
        _defensivePoolDefinitions = defensivePoolDefinitions;
        _defensivePoolsInAbsorbOrder = defensivePoolDefinitions.Values
            .OrderBy(d => d.AbsorbPriority)
            .ThenBy(d => d.Id.value, StringComparer.Ordinal)
            .ToImmutableArray();
        _combatEventHandlers = combatEventHandlers;
        _nodeExecutorRegistry = nodeExecutorRegistry;
    }

    /// <summary>Always true: a <see cref="CombatDefinitionRegistry"/> only exists post-build.</summary>
    public bool IsBuilt => true;

    /// <summary>
    /// Whether this registry was built with the unsafe-side-effect opt-in. False for all normal
    /// production content; true only when a test/diagnostic build allowed <c>SideEffectNode</c>.
    /// Surfaced so tooling can flag a registry that contains nodes outside the safe Effect
    /// Program language.
    /// </summary>
    public bool AllowsUnsafeSideEffects { get; }

    public EffectNodeExecutorRegistry EffectNodeExecutors => _nodeExecutorRegistry;

    public IReadOnlyDictionary<StatusDefinitionId, StatusDefinition> StatusDefinitions => _statusDefinitions;
    public IReadOnlyDictionary<CardDefinitionId, CardDefinition> CardDefinitions => _cardDefinitions;
    public IReadOnlyDictionary<Type, IEffectRequestHandler> EffectRequestHandlers => _effectRequestHandlers;
    public IReadOnlyDictionary<EnemyActionDefinitionId, EnemyActionDefinition> EnemyActionDefinitions => _enemyActionDefinitions;
    public IReadOnlyCollection<IDamageAmountModifier> DamageAmountModifiers => _damageAmountModifiers;
    public IReadOnlyCollection<ICardPlayValidator> CardPlayValidators => _cardPlayValidators;
    public IReadOnlyCollection<ICardCostModifier> CardCostModifiers => _cardCostModifiers;

    public StatusDefinition GetStatus(StatusDefinitionId id)
    {
        if (!_statusDefinitions.TryGetValue(id, out var definition))
            throw new InvalidOperationException($"Status definition '{id}' is not registered.");
        return definition;
    }

    public bool TryGetStatus(StatusDefinitionId id, out StatusDefinition? definition) =>
        _statusDefinitions.TryGetValue(id, out definition);

    public CardDefinition GetCard(CardDefinitionId id)
    {
        if (!_cardDefinitions.TryGetValue(id, out var definition))
            throw new InvalidOperationException($"Card definition '{id}' is not registered.");
        return definition;
    }

    public bool TryGetCard(CardDefinitionId id, out CardDefinition? definition) =>
        _cardDefinitions.TryGetValue(id, out definition);

    public EnemyActionDefinition GetEnemyAction(EnemyActionDefinitionId id)
    {
        if (!_enemyActionDefinitions.TryGetValue(id, out var definition))
            throw new InvalidOperationException($"Enemy action definition '{id}' is not registered.");
        return definition;
    }

    public bool TryGetEnemyAction(EnemyActionDefinitionId id, out EnemyActionDefinition? definition) =>
        _enemyActionDefinitions.TryGetValue(id, out definition);

    public IReadOnlyList<IDamageAmountModifier> GetDamageAmountModifiers() => _damageAmountModifiers;

    public IReadOnlyList<ICardPlayValidator> GetCardPlayValidators() => _cardPlayValidators;

    public IReadOnlyList<ICardCostModifier> GetCardCostModifiers() => _cardCostModifiers;

    public IReadOnlyList<IStatusApplicationInterceptor> GetStatusApplicationInterceptors() => _statusApplicationInterceptors;

    public IReadOnlyList<IPreDownInterceptor> GetPreDownInterceptors() => _preDownInterceptors;

    public IReadOnlyList<IDamageSplitter> GetDamageSplitters() => _damageSplitters;

    public IReadOnlyList<IBlockAmountModifier> GetBlockAmountModifiers() => _blockAmountModifiers;

    public IReadOnlyDictionary<DefensivePoolId, DefensivePoolDefinition> DefensivePoolDefinitions => _defensivePoolDefinitions;

    public bool TryGetDefensivePool(DefensivePoolId id, out DefensivePoolDefinition? definition)
    {
        var found = _defensivePoolDefinitions.TryGetValue(id, out var value);
        definition = value;
        return found;
    }

    // Registered defensive pools in the order incoming damage drains them (lowest AbsorbPriority first).
    public IReadOnlyList<DefensivePoolDefinition> GetDefensivePoolsInAbsorbOrder() => _defensivePoolsInAbsorbOrder;

    public IEffectRequestHandler GetEffectRequestHandler(Type requestType)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        if (!_effectRequestHandlers.TryGetValue(requestType, out var handler))
            throw new InvalidOperationException(
                $"Effect request handler for '{requestType.Name}' is not registered.");
        return handler;
    }

    public bool TryGetEffectRequestHandler(Type requestType, out IEffectRequestHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        return _effectRequestHandlers.TryGetValue(requestType, out handler);
    }

    public ITriggeredEffectDefinition GetTriggeredEffectDefinition(TriggeredEffectDefinitionId id)
    {
        if (!_triggeredEffectDefinitions.TryGetValue(id, out var definition))
            throw new InvalidOperationException(
                $"Triggered effect definition '{id}' is not registered.");
        return definition;
    }

    public bool TryGetTriggeredEffectDefinition(
        TriggeredEffectDefinitionId id,
        out ITriggeredEffectDefinition? definition) =>
        _triggeredEffectDefinitions.TryGetValue(id, out definition);

    // Lookup for a temporary-rule body by id (save/restore re-link only). Falls back to the active triggered
    // definitions so a rule whose body happens to also be a registered permanent rule still resolves.
    public bool TryGetTemporaryRuleDefinition(
        TriggeredEffectDefinitionId id,
        out ITriggeredEffectDefinition? definition) =>
        _temporaryRuleDefinitions.TryGetValue(id, out definition)
        || _triggeredEffectDefinitions.TryGetValue(id, out definition);

    public IReadOnlyCollection<ITriggeredEffectDefinition> GetTriggeredEffectDefinitions(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _triggeredEffectDefinitions.Values
            .Where(definition => definition.EventType == eventType)
            .ToArray();
    }

    public IReadOnlyCollection<ICombatEventHandler> GetCombatEventHandlers(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        return _combatEventHandlers.TryGetValue(eventType, out var handlers)
            ? handlers
            : ImmutableArray<ICombatEventHandler>.Empty;
    }
}
