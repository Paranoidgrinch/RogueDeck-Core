namespace RogueDeck.Run;

// The id-keyed catalog of run CONTENT — events, encounters, relics, reward tables — as opposed to
// RunDefinitionRegistry, which holds engine wiring (effect handlers + node resolvers). Content is what a
// designer/pack authors; keying it by id is what lets a map reference it as data (EventRef/EncounterRef) and
// is the prerequisite for serialization. Built by RunContentRegistryBuilder; Validate checks a map's
// references resolve, the run-layer counterpart of the combat registry's seal-time validation.
public sealed class RunContentRegistry
{
    private readonly IReadOnlyDictionary<EventId, EventScript> _events;
    private readonly IReadOnlyDictionary<RelicId, RelicDefinition> _relics;
    private readonly IReadOnlyDictionary<RewardTableId, IRewardSource> _rewardTables;
    private readonly IReadOnlyDictionary<ConsumableId, ConsumableDefinition> _consumables;

    public EncounterCatalog? Encounters { get; }

    internal RunContentRegistry(
        IReadOnlyDictionary<EventId, EventScript> events,
        EncounterCatalog? encounters,
        IReadOnlyDictionary<RelicId, RelicDefinition> relics,
        IReadOnlyDictionary<RewardTableId, IRewardSource> rewardTables,
        IReadOnlyDictionary<ConsumableId, ConsumableDefinition> consumables)
    {
        _events = events;
        Encounters = encounters;
        _relics = relics;
        _rewardTables = rewardTables;
        _consumables = consumables;
    }

    public EventScript GetEvent(EventId id) =>
        _events.TryGetValue(id, out var script)
            ? script
            : throw new InvalidOperationException($"No event registered with id '{id}'.");

    public RelicDefinition GetRelic(RelicId id) =>
        _relics.TryGetValue(id, out var relic)
            ? relic
            : throw new InvalidOperationException($"No relic registered with id '{id}'.");

    public IRewardSource GetRewardTable(RewardTableId id) =>
        _rewardTables.TryGetValue(id, out var table)
            ? table
            : throw new InvalidOperationException($"No reward table registered with id '{id}'.");

    public ConsumableDefinition GetConsumable(ConsumableId id) =>
        _consumables.TryGetValue(id, out var consumable)
            ? consumable
            : throw new InvalidOperationException($"No consumable registered with id '{id}'.");

    public bool HasEvent(EventId id) => _events.ContainsKey(id);
    public bool HasRelic(RelicId id) => _relics.ContainsKey(id);
    public bool HasConsumable(ConsumableId id) => _consumables.ContainsKey(id);

    // Validates that every node in a map resolves: its node type has a resolver, and any EventRef / EncounterRef
    // it carries names registered content. Throws with all problems aggregated; returns nothing on success.
    public void Validate(RunMap map, RunDefinitionRegistry definitions)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(definitions);

        var problems = new List<string>();
        foreach (var node in map.Nodes)
        {
            if (!definitions.HasResolver(node.Type))
                problems.Add($"Node '{node.Id}' has type '{node.Type}' with no registered resolver.");

            switch (node.Payload)
            {
                case EventRef reference when !HasEvent(reference.Id):
                    problems.Add($"Node '{node.Id}' references unknown event '{reference.Id}'.");
                    break;
                case EncounterRef reference when Encounters is null || !Encounters.Contains(reference.Id):
                    problems.Add($"Node '{node.Id}' references unknown encounter '{reference.Id}'.");
                    break;
            }
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "Run content validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }
}

public sealed class RunContentRegistryBuilder
{
    private readonly Dictionary<EventId, EventScript> _events = new();
    private readonly Dictionary<RelicId, RelicDefinition> _relics = new();
    private readonly Dictionary<RewardTableId, IRewardSource> _rewardTables = new();
    private readonly Dictionary<ConsumableId, ConsumableDefinition> _consumables = new();
    private EncounterCatalog? _encounters;

    public RunContentRegistryBuilder RegisterEvent(EventId id, EventScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!_events.TryAdd(id, script))
            throw new InvalidOperationException($"An event with id '{id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder RegisterRelic(RelicDefinition relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        if (!_relics.TryAdd(relic.Id, relic))
            throw new InvalidOperationException($"A relic with id '{relic.Id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder RegisterRewardTable(RewardTableId id, IRewardSource table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!_rewardTables.TryAdd(id, table))
            throw new InvalidOperationException($"A reward table with id '{id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder RegisterConsumable(ConsumableDefinition consumable)
    {
        ArgumentNullException.ThrowIfNull(consumable);
        if (!_consumables.TryAdd(consumable.Id, consumable))
            throw new InvalidOperationException($"A consumable with id '{consumable.Id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder SetEncounters(EncounterCatalog encounters)
    {
        ArgumentNullException.ThrowIfNull(encounters);
        _encounters = encounters;
        return this;
    }

    public RunContentRegistry Build() =>
        new(_events, _encounters, _relics, _rewardTables, _consumables);
}
