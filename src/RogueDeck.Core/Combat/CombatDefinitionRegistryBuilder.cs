using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

/// <summary>
/// Mutable registration surface for combat definitions. Register everything, then call
/// <see cref="Build"/> to produce an immutable <see cref="CombatDefinitionRegistry"/>.
///
/// <see cref="Build"/> is atomic: it validates the whole definition set first and only then
/// produces a registry. A failed build throws and leaves the builder intact and recoverable —
/// fix the problem and build again. Once a build succeeds the result is cached, the builder is
/// frozen, and further registration throws.
/// </summary>
public sealed class CombatDefinitionRegistryBuilder
{
    private readonly Dictionary<StatusDefinitionId, StatusDefinition> _statusDefinitions = new();
    private readonly Dictionary<CardDefinitionId, CardDefinition> _cardDefinitions = new();
    private readonly Dictionary<Type, IEffectRequestHandler> _effectRequestHandlers = new();
    private readonly Dictionary<Type, List<ICombatEventHandler>> _combatEventHandlers = new();
    private readonly List<IDamageAmountModifier> _damageAmountModifiers = new();
    private readonly List<ICardPlayValidator> _cardPlayValidators = new();
    private readonly List<ICardCostModifier> _cardCostModifiers = new();
    private readonly List<IStatusApplicationInterceptor> _statusApplicationInterceptors = new();
    private readonly List<IPreDownInterceptor> _preDownInterceptors = new();
    private readonly List<IDamageSplitter> _damageSplitters = new();
    private readonly List<IBlockAmountModifier> _blockAmountModifiers = new();
    private readonly Dictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> _triggeredEffectDefinitions = new();
    private readonly Dictionary<EnemyActionDefinitionId, EnemyActionDefinition> _enemyActionDefinitions = new();

    private readonly EffectNodeExecutorRegistry _nodeExecutorRegistry = new();

    private bool _allowUnsafeSideEffects;
    private CombatDefinitionRegistry? _built;

    public bool IsBuilt => _built is not null;

    public bool AllowUnsafeSideEffects
    {
        get => _allowUnsafeSideEffects;
        set
        {
            EnsureNotBuilt();
            _allowUnsafeSideEffects = value;
        }
    }

    public void RegisterEffectNodeExecutor(Type nodeType, IEffectNodeExecutor executor)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(executor);
        _nodeExecutorRegistry.Register(nodeType, executor);
    }

    public void RegisterEffectNodeExecutorOpenGeneric(Type openGenericNodeType, IEffectNodeExecutor executor)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(executor);
        _nodeExecutorRegistry.RegisterOpenGeneric(openGenericNodeType, executor);
    }

    public void RegisterStatus(StatusDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Status definition ID cannot be empty or whitespace.", nameof(definition));

        if (_statusDefinitions.ContainsKey(definition.Id))
            throw new InvalidOperationException($"Status definition '{definition.Id}' is already registered.");

        _statusDefinitions.Add(definition.Id, definition);
    }

    public void RegisterCard(CardDefinitionBuilder builder)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(builder.Id.value))
            throw new ArgumentException(
                "Card definition ID cannot be empty or whitespace.", nameof(builder));

        RegisterCard(builder.Build());
    }

    public void RegisterCard(CardDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Card definition ID cannot be empty or whitespace.", nameof(definition));

        if (_cardDefinitions.ContainsKey(definition.Id))
            throw new InvalidOperationException($"Card definition '{definition.Id}' is already registered.");

        _cardDefinitions.Add(definition.Id, definition);
    }

    public void RegisterEnemyAction(EnemyActionDefinitionBuilder builder)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(builder);

        if (string.IsNullOrWhiteSpace(builder.Id.value))
            throw new ArgumentException(
                "Enemy action definition ID cannot be empty or whitespace.", nameof(builder));

        RegisterEnemyAction(builder.Build());
    }

    public void RegisterEnemyAction(EnemyActionDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Enemy action definition ID cannot be empty or whitespace.", nameof(definition));

        if (_enemyActionDefinitions.ContainsKey(definition.Id))
            throw new InvalidOperationException($"Enemy action definition '{definition.Id}' is already registered.");

        _enemyActionDefinitions.Add(definition.Id, definition);
    }

    public void RegisterDamageAmountModifier(IDamageAmountModifier modifier)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(modifier);

        if (_damageAmountModifiers.Any(existing => existing.ModifierId == modifier.ModifierId))
            throw new InvalidOperationException(
                $"Damage amount modifier '{modifier.ModifierId}' is already registered.");

        _damageAmountModifiers.Add(modifier);
        SortByPriorityThenId(_damageAmountModifiers, m => m.Priority, m => m.ModifierId);
    }

    public void RegisterCardPlayValidator(ICardPlayValidator validator)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(validator);

        if (_cardPlayValidators.Any(existing => existing.ModifierId == validator.ModifierId))
            throw new InvalidOperationException(
                $"Card play validator '{validator.ModifierId}' is already registered.");

        _cardPlayValidators.Add(validator);
        SortByPriorityThenId(_cardPlayValidators, v => v.Priority, v => v.ModifierId);
    }

    public void RegisterCardCostModifier(ICardCostModifier modifier)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(modifier);

        if (_cardCostModifiers.Any(existing => existing.ModifierId == modifier.ModifierId))
            throw new InvalidOperationException(
                $"Card cost modifier '{modifier.ModifierId}' is already registered.");

        _cardCostModifiers.Add(modifier);
        SortByPriorityThenId(_cardCostModifiers, m => m.Priority, m => m.ModifierId);
    }

    public void RegisterStatusApplicationInterceptor(IStatusApplicationInterceptor interceptor)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(interceptor);

        if (_statusApplicationInterceptors.Any(existing => existing.ModifierId == interceptor.ModifierId))
            throw new InvalidOperationException(
                $"Status application interceptor '{interceptor.ModifierId}' is already registered.");

        _statusApplicationInterceptors.Add(interceptor);
        SortByPriorityThenId(_statusApplicationInterceptors, i => i.Priority, i => i.ModifierId);
    }

    public void RegisterPreDownInterceptor(IPreDownInterceptor interceptor)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(interceptor);

        if (_preDownInterceptors.Any(existing => existing.InterceptorId == interceptor.InterceptorId))
            throw new InvalidOperationException(
                $"Pre-down interceptor '{interceptor.InterceptorId}' is already registered.");

        _preDownInterceptors.Add(interceptor);
        SortByPriorityThenId(_preDownInterceptors, i => i.Priority, i => i.InterceptorId);
    }

    public void RegisterDamageSplitter(IDamageSplitter splitter)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(splitter);

        if (_damageSplitters.Any(existing => existing.SplitterId == splitter.SplitterId))
            throw new InvalidOperationException(
                $"Damage splitter '{splitter.SplitterId}' is already registered.");

        _damageSplitters.Add(splitter);
        SortByPriorityThenId(_damageSplitters, s => s.Priority, s => s.SplitterId);
    }

    public void RegisterBlockAmountModifier(IBlockAmountModifier modifier)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(modifier);

        if (_blockAmountModifiers.Any(existing => existing.ModifierId == modifier.ModifierId))
            throw new InvalidOperationException(
                $"Block amount modifier '{modifier.ModifierId}' is already registered.");

        _blockAmountModifiers.Add(modifier);
        SortByPriorityThenId(_blockAmountModifiers, m => m.Priority, m => m.ModifierId);
    }

    public void RegisterEffectRequestHandler(IEffectRequestHandler handler)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(handler);

        if (_effectRequestHandlers.ContainsKey(handler.RequestType))
            throw new InvalidOperationException(
                $"Effect request handler for '{handler.RequestType.Name}' is already registered.");

        _effectRequestHandlers.Add(handler.RequestType, handler);
    }

    public void RegisterTriggeredEffectDefinition(ITriggeredEffectDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Triggered effect definition ID cannot be empty or whitespace.", nameof(definition));

        if (_triggeredEffectDefinitions.ContainsKey(definition.Id))
            throw new InvalidOperationException(
                $"Triggered effect definition '{definition.Id}' is already registered.");

        _triggeredEffectDefinitions.Add(definition.Id, definition);
    }

    public void RegisterCombatEventHandler(ICombatEventHandler handler)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(handler);

        if (!_combatEventHandlers.TryGetValue(handler.EventType, out var handlers))
        {
            handlers = new List<ICombatEventHandler>();
            _combatEventHandlers.Add(handler.EventType, handlers);
        }

        if (handlers.Any(existing => existing.GetType() == handler.GetType()))
            throw new InvalidOperationException(
                $"Combat event handler '{handler.GetType().Name}' for event '{handler.EventType.Name}' is already registered.");

        handlers.Add(handler);
    }

    /// <summary>
    /// Validate the full definition set and produce an immutable runtime registry. Throws on
    /// the validation errors without mutating builder state, so the builder can be corrected and
    /// rebuilt. After a successful build the result is cached and the builder is frozen.
    /// </summary>
    public CombatDefinitionRegistry Build()
    {
        if (_built is not null)
            return _built;

        var diagnostics = new List<CombatDiagnostic>();

        foreach (var (cardId, card) in _cardDefinitions)
            if (card.Program is { } p)
                ValidateEffectProgramTree(
                    p.Root, "card", cardId.ToString(), $"card:'{cardId}'",
                    p.Id.Value, EffectProgramNodePath.Root, diagnostics);

        foreach (var (defId, def) in _triggeredEffectDefinitions)
            if (def.GetEffectProgramRoot() is { } root)
                ValidateEffectProgramTree(
                    root, "trigger", defId.ToString(), $"trigger:'{defId}'",
                    null, EffectProgramNodePath.Root, diagnostics);

        foreach (var (actionId, action) in _enemyActionDefinitions)
            if (action.Program is { } p)
                ValidateEffectProgramTree(
                    p.Root, "enemy-action", actionId.ToString(), $"enemy-action:'{actionId}'",
                    p.Id.Value, EffectProgramNodePath.Root, diagnostics);

        if (diagnostics.Count > 0)
            throw new CombatDefinitionBuildException(diagnostics);

        _nodeExecutorRegistry.Seal();

        // Deep-freeze status definitions so their tag sets cannot change after build (the runtime
        // registry stores these exact instances).
        foreach (var status in _statusDefinitions.Values)
            status.Freeze();

        _built = new CombatDefinitionRegistry(
            _statusDefinitions.ToImmutableDictionary(),
            _cardDefinitions.ToImmutableDictionary(),
            _effectRequestHandlers.ToImmutableDictionary(),
            _enemyActionDefinitions.ToImmutableDictionary(),
            _triggeredEffectDefinitions.ToImmutableDictionary(),
            _damageAmountModifiers.ToImmutableArray(),
            _cardPlayValidators.ToImmutableArray(),
            _cardCostModifiers.ToImmutableArray(),
            _statusApplicationInterceptors.ToImmutableArray(),
            _preDownInterceptors.ToImmutableArray(),
            _damageSplitters.ToImmutableArray(),
            _blockAmountModifiers.ToImmutableArray(),
            _combatEventHandlers.ToImmutableDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableArray()),
            _nodeExecutorRegistry,
            _allowUnsafeSideEffects);

        return _built;
    }

    private void EnsureNotBuilt()
    {
        if (_built is not null)
            throw new InvalidOperationException(
                "Cannot register definitions after the builder has been built.");
    }

    private void ValidateEffectProgramTree(
        IEffectNode node,
        string ownerKind,
        string ownerId,
        string ownerLabel,
        string? programId,
        EffectProgramNodePath path,
        List<CombatDiagnostic> diagnostics)
    {
        void Add(CombatDiagnosticCode code, string message, string? selectorName = null) =>
            diagnostics.Add(new CombatDiagnostic(
                code, CombatDiagnosticSeverity.Error, ownerKind, ownerId, programId, path.Value,
                $"{ownerLabel}: {message}", selectorName));

        if (!_nodeExecutorRegistry.TryGet(node.GetType(), out _))
            Add(CombatDiagnosticCode.MissingNodeExecutor,
                $"no executor registered for '{node.GetType().Name}'");

        if (node is ISideEffectNodeCore && !_allowUnsafeSideEffects)
            Add(CombatDiagnosticCode.UnsafeSideEffectNode,
                $"'{node.GetType().Name}' is an unsafe side-effect node and is " +
                "not allowed in production effect programs " +
                "(set CombatDefinitionRegistryBuilder.AllowUnsafeSideEffects = true to permit it in tests)");

        if (node is INativeEffectOperationNode native &&
            !_effectRequestHandlers.ContainsKey(native.ProducedEffectRequestType))
            Add(CombatDiagnosticCode.MissingRequestHandler,
                $"no handler registered for '{native.ProducedEffectRequestType.Name}' " +
                $"(required by '{node.GetType().Name}')");

        // Targeting contracts: validate each selector this node addresses against the operation's
        // accepted target domain and eligibility, and against what the program context provides.
        var providedCapabilities = EffectContextCapabilities.ForContextType(node.NodeContextType);
        var nativeOp = node as INativeEffectOperationNode;
        foreach (var selector in node.GetTargetSelectors())
        {
            var selectorName = selector.GetType().Name;

            var missingCapabilities = selector.RequiredContextCapabilities & ~providedCapabilities;
            if (missingCapabilities != EffectContextCapability.None)
                Add(CombatDiagnosticCode.ContextCapabilityMissing,
                    $"selector '{selectorName}' requires context capability " +
                    $"{missingCapabilities} which context '{node.NodeContextType.Name}' does not provide",
                    selectorName);

            if (nativeOp is not null && selector.TargetDomain != nativeOp.AcceptedTargetDomain)
                Add(CombatDiagnosticCode.TargetDomainMismatch,
                    $"selector '{selectorName}' addresses domain {selector.TargetDomain} " +
                    $"but operation '{node.GetType().Name}' accepts {nativeOp.AcceptedTargetDomain}",
                    selectorName);

            if (nativeOp is { TargetEligibility: TargetEligibility.LivingOnly } &&
                selector.MayIncludeDownedTargets)
                Add(CombatDiagnosticCode.OperationEligibilityMismatch,
                    $"operation '{node.GetType().Name}' is living-only but selector " +
                    $"'{selectorName}' may resolve a downed combatant; use a living-only " +
                    "selector or an operation that accepts downed combatants",
                    selectorName);
        }

        if (node is IApplyStatusNodeCore applyStatus &&
            !_statusDefinitions.ContainsKey(applyStatus.StatusDefinitionId))
            Add(CombatDiagnosticCode.MissingStatusDefinition,
                $"referenced status '{applyStatus.StatusDefinitionId}' is not registered");

        if (node is IRemoveStatusNodeCore removeStatus &&
            !_statusDefinitions.ContainsKey(removeStatus.StatusDefinitionId))
            Add(CombatDiagnosticCode.MissingStatusDefinition,
                $"referenced status '{removeStatus.StatusDefinitionId}' is not registered");

        if (node is ICreateCardInstanceNodeCore createCard &&
            !_cardDefinitions.ContainsKey(createCard.CardDefinitionId))
            Add(CombatDiagnosticCode.MissingCardDefinition,
                $"referenced card definition '{createCard.CardDefinitionId}' is not registered");

        // A temporary-rule node carries a whole sub-program for a different event context.
        // Its nodes are not structural children, so validate that program tree separately.
        if (node is IInstallTemporaryRuleNodeCore install &&
            install.GetInstalledProgramRoot() is { } installedRoot)
            ValidateEffectProgramTree(
                installedRoot, "temporary-rule", install.RuleDefinition.Id.ToString(),
                $"{ownerLabel} → temporary-rule '{install.RuleDefinition.Id}'",
                install.RuleDefinition.GetEffectProgramRoot() is not null
                    ? $"trigger:{install.RuleDefinition.Id}"
                    : programId,
                EffectProgramNodePath.Root, diagnostics);

        var children = node.ChildNodes.ToArray();
        for (var i = 0; i < children.Length; i++)
            ValidateEffectProgramTree(
                children[i], ownerKind, ownerId, ownerLabel, programId,
                path.Child(node.GetChildPathSegment(i)), diagnostics);
    }

    private static void SortByPriorityThenId<T>(
        List<T> list, Func<T, int> priority, Func<T, string> id) =>
        list.Sort((left, right) =>
        {
            var priorityComparison = priority(left).CompareTo(priority(right));
            return priorityComparison != 0
                ? priorityComparison
                : string.Compare(id(left), id(right), StringComparison.Ordinal);
        });
}
