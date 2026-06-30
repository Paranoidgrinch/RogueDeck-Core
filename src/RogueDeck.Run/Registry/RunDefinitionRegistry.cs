namespace RogueDeck.Run;

// The built, immutable set of run-layer definitions: effect handlers keyed by request type and node
// resolvers keyed by node type. Mirrors CombatDefinitionRegistry. Relics are NOT here — they are acquired
// run state, so they live on RunState.
public sealed class RunDefinitionRegistry
{
    private readonly IReadOnlyDictionary<Type, IRunEffectHandler> _effectHandlers;
    private readonly IReadOnlyDictionary<NodeType, INodeResolver> _resolvers;

    internal RunDefinitionRegistry(
        IReadOnlyDictionary<Type, IRunEffectHandler> effectHandlers,
        IReadOnlyDictionary<NodeType, INodeResolver> resolvers)
    {
        _effectHandlers = effectHandlers;
        _resolvers = resolvers;
    }

    public IRunEffectHandler GetEffectHandler(Type requestType)
    {
        if (!_effectHandlers.TryGetValue(requestType, out var handler))
            throw new InvalidOperationException(
                $"No run effect handler registered for '{requestType.Name}'.");

        return handler;
    }

    public INodeResolver GetResolver(NodeType nodeType)
    {
        if (!_resolvers.TryGetValue(nodeType, out var resolver))
            throw new InvalidOperationException(
                $"No node resolver registered for node type '{nodeType}'.");

        return resolver;
    }
}

public sealed class RunDefinitionRegistryBuilder
{
    private readonly Dictionary<Type, IRunEffectHandler> _effectHandlers = new();
    private readonly Dictionary<NodeType, INodeResolver> _resolvers = new();

    public RunDefinitionRegistryBuilder RegisterEffectHandler(IRunEffectHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_effectHandlers.TryAdd(handler.RequestType, handler))
            throw new InvalidOperationException(
                $"A run effect handler for '{handler.RequestType.Name}' is already registered.");

        return this;
    }

    public RunDefinitionRegistryBuilder RegisterResolver(INodeResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (!_resolvers.TryAdd(resolver.NodeType, resolver))
            throw new InvalidOperationException(
                $"A node resolver for node type '{resolver.NodeType}' is already registered.");

        return this;
    }

    public RunDefinitionRegistry Build() => new(_effectHandlers, _resolvers);
}

// A run-layer package: a unit of registration, mirroring ICombatPackage.
public interface IRunPackage
{
    string DisplayName { get; }
    void RegisterDefinitions(RunDefinitionRegistryBuilder builder);
}
