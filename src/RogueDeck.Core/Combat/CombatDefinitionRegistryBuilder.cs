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
    private readonly Dictionary<DefensivePoolId, DefensivePoolDefinition> _defensivePoolDefinitions = new();
    private readonly Dictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> _triggeredEffectDefinitions = new();
    private readonly Dictionary<TriggeredEffectDefinitionId, ITriggeredEffectDefinition> _temporaryRuleDefinitions = new();
    private readonly Dictionary<EnemyActionDefinitionId, EnemyActionDefinition> _enemyActionDefinitions = new();

    // The node executors this builder writes to. A builder started from a built registry does NOT copy them
    // until it has one of its own to register — a fight that adds no executors reads the base's sealed set.
    private EffectNodeExecutorRegistry? _ownNodeExecutors;

    // The already-built registry this builder stands on, or null when it builds from nothing. It is REFERENCED,
    // never copied: its definitions stay where they are and Build() adds this builder's own to them. Copying a
    // library out into mutable dictionaries and back again would cost the whole library for every fight, which
    // is exactly the cost this seam exists to remove.
    private readonly CombatDefinitionRegistry? _base;

    private bool _allowUnsafeSideEffects;
    private CombatDefinitionRegistry? _built;

    public CombatDefinitionRegistryBuilder()
    {
    }

    /// <summary>
    /// Start from a registry that is already built, and add to it. Every definition of the base is carried
    /// over as-is; whatever is registered afterwards is validated and duplicate-checked as usual.
    ///
    /// This exists so a game's authored library — the cards, statuses and enemy actions that are the same in
    /// every fight — can be compiled ONCE and shared, instead of being recompiled and re-validated for each
    /// fight. It shares definition INSTANCES, which is already how a library behaves today: definitions are
    /// read-only after Build(), and the registry hands the same instances to every combat built from it.
    ///
    /// ⚠ Registration only ever ADDS. That is what makes skipping the base's validation sound: a program was
    /// checked against a set of request handlers, node executors and the unsafe-side-effect flag, and none of
    /// those can shrink here — a handler cannot be unregistered, an executor cannot be removed, and the flag
    /// is inherited. A definition that validated in the base therefore still validates in the result.
    /// </summary>
    public CombatDefinitionRegistryBuilder(CombatDefinitionRegistry baseRegistry)
    {
        ArgumentNullException.ThrowIfNull(baseRegistry);
        _base = baseRegistry;
        _allowUnsafeSideEffects = baseRegistry.AllowsUnsafeSideEffects;
    }

    // The executors to read when validating: this builder's own if it made a copy, else the base's, else a
    // fresh empty one (a builder standing on nothing).
    private EffectNodeExecutorRegistry NodeExecutors =>
        _ownNodeExecutors ?? _base?.EffectNodeExecutors ?? OwnNodeExecutors;

    // The executors to WRITE to — taking a copy of the base's sealed set the first time one is registered.
    private EffectNodeExecutorRegistry OwnNodeExecutors =>
        _ownNodeExecutors ??= _base is null
            ? new EffectNodeExecutorRegistry()
            : new EffectNodeExecutorRegistry(_base.EffectNodeExecutors);

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
        OwnNodeExecutors.Register(nodeType, executor);
    }

    public void RegisterEffectNodeExecutorOpenGeneric(Type openGenericNodeType, IEffectNodeExecutor executor)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(executor);
        OwnNodeExecutors.RegisterOpenGeneric(openGenericNodeType, executor);
    }

    public void RegisterStatus(StatusDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Status definition ID cannot be empty or whitespace.", nameof(definition));

        if (_statusDefinitions.ContainsKey(definition.Id) || _base?.StatusesImmutable.ContainsKey(definition.Id) == true)
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

        if (_cardDefinitions.ContainsKey(definition.Id) || _base?.CardsImmutable.ContainsKey(definition.Id) == true)
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

        if (_enemyActionDefinitions.ContainsKey(definition.Id) || _base?.EnemyActionsImmutable.ContainsKey(definition.Id) == true)
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

    public void RegisterDefensivePool(DefensivePoolDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Defensive pool id cannot be empty or whitespace.", nameof(definition));

        if (_defensivePoolDefinitions.ContainsKey(definition.Id) || _base?.DefensivePoolsImmutable.ContainsKey(definition.Id) == true)
            throw new InvalidOperationException(
                $"Defensive pool '{definition.Id}' is already registered.");

        _defensivePoolDefinitions.Add(definition.Id, definition);
    }

    public void RegisterEffectRequestHandler(IEffectRequestHandler handler)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(handler);

        if (_effectRequestHandlers.ContainsKey(handler.RequestType)
            || _base?.EffectRequestHandlersImmutable.ContainsKey(handler.RequestType) == true)
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

        if (_triggeredEffectDefinitions.ContainsKey(definition.Id)
            || _base?.TriggeredEffectsImmutable.ContainsKey(definition.Id) == true)
            throw new InvalidOperationException(
                $"Triggered effect definition '{definition.Id}' is already registered.");

        _triggeredEffectDefinitions.Add(definition.Id, definition);
    }

    // Registers a temporary-rule body for save/restore RE-LINK only. It does NOT become an active permanent
    // rule (it never fires on its own) — it just lets CombatState.Restore rebuild a temporary rule installed
    // with this id, whose program body the snapshot cannot value-capture. Content that installs temporary rules
    // registers their definitions here so a saved combat carrying them can be resumed.
    public void RegisterTemporaryRuleDefinition(ITriggeredEffectDefinition definition)
    {
        EnsureNotBuilt();
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Id.value))
            throw new ArgumentException(
                "Temporary rule definition ID cannot be empty or whitespace.", nameof(definition));

        if (_temporaryRuleDefinitions.ContainsKey(definition.Id)
            || _base?.TemporaryRulesImmutable.ContainsKey(definition.Id) == true)
            throw new InvalidOperationException(
                $"Temporary rule definition '{definition.Id}' is already registered.");

        _temporaryRuleDefinitions.Add(definition.Id, definition);
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

        var alreadyThere = handlers.Any(existing => existing.GetType() == handler.GetType());
        if (!alreadyThere && _base is not null
            && _base.CombatEventHandlersImmutable.TryGetValue(handler.EventType, out var inherited))
            alreadyThere = inherited.Any(existing => existing.GetType() == handler.GetType());
        if (alreadyThere)
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

        // Only what THIS builder registered is walked. A base's programs were walked when the base was built,
        // and nothing since can have invalidated them — see the base constructor.
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

        NodeExecutors.Seal();

        // Deep-freeze status definitions so their tag sets cannot change after build (the runtime
        // registry stores these exact instances). A base's are already frozen.
        foreach (var status in _statusDefinitions.Values)
            status.Freeze();

        _built = new CombatDefinitionRegistry(
            Merged(_base?.StatusesImmutable, _statusDefinitions),
            Merged(_base?.CardsImmutable, _cardDefinitions),
            Merged(_base?.EffectRequestHandlersImmutable, _effectRequestHandlers),
            Merged(_base?.EnemyActionsImmutable, _enemyActionDefinitions),
            Merged(_base?.TriggeredEffectsImmutable, _triggeredEffectDefinitions),
            Merged(_base?.TemporaryRulesImmutable, _temporaryRuleDefinitions),
            MergedAndSorted(_base?.DamageAmountModifiersImmutable, _damageAmountModifiers,
                m => m.Priority, m => m.ModifierId),
            MergedAndSorted(_base?.CardPlayValidatorsImmutable, _cardPlayValidators,
                v => v.Priority, v => v.ModifierId),
            MergedAndSorted(_base?.CardCostModifiersImmutable, _cardCostModifiers,
                m => m.Priority, m => m.ModifierId),
            MergedAndSorted(_base?.StatusApplicationInterceptorsImmutable, _statusApplicationInterceptors,
                i => i.Priority, i => i.ModifierId),
            MergedAndSorted(_base?.PreDownInterceptorsImmutable, _preDownInterceptors,
                i => i.Priority, i => i.InterceptorId),
            MergedAndSorted(_base?.DamageSplittersImmutable, _damageSplitters,
                sp => sp.Priority, sp => sp.SplitterId),
            MergedAndSorted(_base?.BlockAmountModifiersImmutable, _blockAmountModifiers,
                m => m.Priority, m => m.ModifierId),
            Merged(_base?.DefensivePoolsImmutable, _defensivePoolDefinitions),
            MergedEventHandlers(),
            NodeExecutors,
            _allowUnsafeSideEffects);

        return _built;
    }

    // The base's entries plus this builder's own. Adding to an immutable dictionary shares the base's nodes
    // instead of rebuilding it, so a fight that adds three triggers to a library of thousands pays for three.
    private static ImmutableDictionary<TKey, TValue> Merged<TKey, TValue>(
        ImmutableDictionary<TKey, TValue>? baseEntries, Dictionary<TKey, TValue> own)
        where TKey : notnull =>
        baseEntries is null
            ? own.ToImmutableDictionary()
            : own.Count == 0 ? baseEntries : baseEntries.AddRange(own);

    // Modifiers and interceptors apply in one order: priority first, then id. Merging re-sorts so an addition
    // takes its proper place among the base's rather than being appended after them.
    private static ImmutableArray<T> MergedAndSorted<T>(
        ImmutableArray<T>? baseEntries, List<T> own, Func<T, int> priority, Func<T, string> id)
    {
        if (baseEntries is not { } inherited)
            return own.ToImmutableArray();
        if (own.Count == 0)
            return inherited;
        var merged = inherited.ToList();
        merged.AddRange(own);
        SortByPriorityThenId(merged, priority, id);
        return merged.ToImmutableArray();
    }

    private ImmutableDictionary<Type, ImmutableArray<ICombatEventHandler>> MergedEventHandlers()
    {
        var own = _combatEventHandlers.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutableArray());
        if (_base is null)
            return own;
        if (own.Count == 0)
            return _base.CombatEventHandlersImmutable;

        // A handler registered here for an event the base already handles joins that event's handlers, after
        // the base's — the order they were registered in is the order they run in.
        var merged = _base.CombatEventHandlersImmutable;
        foreach (var (eventType, handlers) in own)
            merged = merged.SetItem(
                eventType,
                merged.TryGetValue(eventType, out var inherited) ? inherited.AddRange(handlers) : handlers);
        return merged;
    }

    // What this builder can see: its own registrations AND the base's. A program validated here belongs to a
    // fight standing on a library, and the status or card it names is usually the library's.
    private bool KnowsStatus(StatusDefinitionId id) =>
        _statusDefinitions.ContainsKey(id) || _base?.StatusesImmutable.ContainsKey(id) == true;

    private bool KnowsCard(CardDefinitionId id) =>
        _cardDefinitions.ContainsKey(id) || _base?.CardsImmutable.ContainsKey(id) == true;

    private bool KnowsEffectRequestHandler(Type requestType) =>
        _effectRequestHandlers.ContainsKey(requestType)
        || _base?.EffectRequestHandlersImmutable.ContainsKey(requestType) == true;

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

        if (!NodeExecutors.TryGet(node.GetType(), out _))
            Add(CombatDiagnosticCode.MissingNodeExecutor,
                $"no executor registered for '{node.GetType().Name}'");

        if (node is ISideEffectNodeCore && !_allowUnsafeSideEffects)
            Add(CombatDiagnosticCode.UnsafeSideEffectNode,
                $"'{node.GetType().Name}' is an unsafe side-effect node and is " +
                "not allowed in production effect programs " +
                "(set CombatDefinitionRegistryBuilder.AllowUnsafeSideEffects = true to permit it in tests)");

        if (node is INativeEffectOperationNode native &&
            !KnowsEffectRequestHandler(native.ProducedEffectRequestType))
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
            !KnowsStatus(applyStatus.StatusDefinitionId))
            Add(CombatDiagnosticCode.MissingStatusDefinition,
                $"referenced status '{applyStatus.StatusDefinitionId}' is not registered");

        if (node is IRemoveStatusNodeCore removeStatus &&
            !KnowsStatus(removeStatus.StatusDefinitionId))
            Add(CombatDiagnosticCode.MissingStatusDefinition,
                $"referenced status '{removeStatus.StatusDefinitionId}' is not registered");

        if (node is ICreateCardInstanceNodeCore createCard &&
            !KnowsCard(createCard.CardDefinitionId))
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
