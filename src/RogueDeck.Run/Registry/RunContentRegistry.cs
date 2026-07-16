using RogueDeck.ShredEngine;

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
    private readonly IReadOnlyDictionary<ShopId, ShopDefinition> _shops;
    private readonly IReadOnlyDictionary<RunProgramSourceId, ITriggeredRunEffectDefinition> _programDefinitions;
    private readonly IReadOnlyDictionary<string, ShredData> _shreds;
    private readonly IReadOnlyDictionary<WorkbenchId, WorkbenchDefinition> _workbenches;

    public EncounterCatalog? Encounters { get; }

    // Shred-Engine content: the authored recipes (matched as unordered multisets when a card is built) and
    // the per-game composition rules. Both defaulted so shred-free games carry nothing.
    public IReadOnlyList<RecipeData> Recipes { get; }
    public ShredRules ShredRules { get; }

    internal RunContentRegistry(
        IReadOnlyDictionary<EventId, EventScript> events,
        EncounterCatalog? encounters,
        IReadOnlyDictionary<RelicId, RelicDefinition> relics,
        IReadOnlyDictionary<RewardTableId, IRewardSource> rewardTables,
        IReadOnlyDictionary<ConsumableId, ConsumableDefinition> consumables,
        IReadOnlyDictionary<ShopId, ShopDefinition> shops,
        IReadOnlyDictionary<RunProgramSourceId, ITriggeredRunEffectDefinition> programDefinitions,
        IReadOnlyDictionary<string, ShredData>? shreds = null,
        IReadOnlyList<RecipeData>? recipes = null,
        ShredRules? shredRules = null,
        IReadOnlyDictionary<WorkbenchId, WorkbenchDefinition>? workbenches = null)
    {
        _events = events;
        Encounters = encounters;
        _relics = relics;
        _rewardTables = rewardTables;
        _consumables = consumables;
        _shops = shops;
        _programDefinitions = programDefinitions;
        _shreds = shreds ?? new Dictionary<string, ShredData>();
        Recipes = recipes ?? [];
        ShredRules = shredRules ?? new ShredRules();
        _workbenches = workbenches ?? new Dictionary<WorkbenchId, WorkbenchDefinition>();
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

    public ShopDefinition GetShop(ShopId id) =>
        _shops.TryGetValue(id, out var shop)
            ? shop
            : throw new InvalidOperationException($"No shop registered with id '{id}'.");

    public ShredData GetShred(string id) =>
        _shreds.TryGetValue(id, out var shred)
            ? shred
            : throw new InvalidOperationException($"No shred registered with id '{id}'.");

    public WorkbenchDefinition GetWorkbench(WorkbenchId id) =>
        _workbenches.TryGetValue(id, out var workbench)
            ? workbench
            : throw new InvalidOperationException($"No workbench registered with id '{id}'.");

    public bool HasEvent(EventId id) => _events.ContainsKey(id);
    public bool HasRelic(RelicId id) => _relics.ContainsKey(id);
    public bool HasConsumable(ConsumableId id) => _consumables.ContainsKey(id);
    public bool HasShop(ShopId id) => _shops.ContainsKey(id);
    public bool HasShred(string id) => _shreds.ContainsKey(id);
    public bool HasWorkbench(WorkbenchId id) => _workbenches.ContainsKey(id);

    // Every registered shred kind, in registration order — the workbench's offer list.
    public IEnumerable<ShredData> Shreds => _shreds.Values;

    // Look up a registered program reaction body for save/restore re-link (RunState.Restore). Returns false if
    // no definition was registered under this source id.
    public bool TryGetProgramDefinition(
        RunProgramSourceId id, out ITriggeredRunEffectDefinition definition) =>
        _programDefinitions.TryGetValue(id, out definition!);

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
                case ShopRef reference when !HasShop(reference.Id):
                    problems.Add($"Node '{node.Id}' references unknown shop '{reference.Id}'.");
                    break;
                case WorkbenchRef reference when !HasWorkbench(reference.Id):
                    problems.Add($"Node '{node.Id}' references unknown workbench '{reference.Id}'.");
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
    private readonly Dictionary<ShopId, ShopDefinition> _shops = new();
    private readonly Dictionary<RunProgramSourceId, ITriggeredRunEffectDefinition> _programDefinitions = new();
    private readonly Dictionary<string, ShredData> _shreds = new(StringComparer.Ordinal);
    private readonly List<RecipeData> _recipes = new();
    private readonly Dictionary<WorkbenchId, WorkbenchDefinition> _workbenches = new();
    private ShredRules? _shredRules;
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

    public RunContentRegistryBuilder RegisterShop(ShopId id, ShopDefinition shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        if (!_shops.TryAdd(id, shop))
            throw new InvalidOperationException($"A shop with id '{id}' is already registered.");
        return this;
    }

    // Register a STATELESS program reaction body under a stable source id. This is a save/restore RE-LINK entry
    // only — it does not install or fire the program on its own. Content that installs a persistent by-ref
    // program registers its definition here so a saved run carrying the program can rebuild it on restore
    // (the run-layer counterpart of CombatDefinitionRegistryBuilder.RegisterTemporaryRuleDefinition).
    public RunContentRegistryBuilder RegisterProgramDefinition(
        RunProgramSourceId id, ITriggeredRunEffectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Program source id cannot be empty or whitespace.", nameof(id));
        if (!_programDefinitions.TryAdd(id, definition))
            throw new InvalidOperationException($"A program definition with id '{id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder SetEncounters(EncounterCatalog encounters)
    {
        ArgumentNullException.ThrowIfNull(encounters);
        _encounters = encounters;
        return this;
    }

    // ── Shred-Engine content ────────────────────────────────────────────────────────

    public RunContentRegistryBuilder RegisterShred(ShredData shred)
    {
        ArgumentNullException.ThrowIfNull(shred);
        if (!_shreds.TryAdd(shred.Id, shred))
            throw new InvalidOperationException($"A shred with id '{shred.Id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder RegisterRecipe(RecipeData recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (_recipes.Any(r => r.Id == recipe.Id))
            throw new InvalidOperationException($"A recipe with id '{recipe.Id}' is already registered.");
        _recipes.Add(recipe);
        return this;
    }

    public RunContentRegistryBuilder RegisterWorkbench(WorkbenchId id, WorkbenchDefinition workbench)
    {
        ArgumentNullException.ThrowIfNull(workbench);
        if (!_workbenches.TryAdd(id, workbench))
            throw new InvalidOperationException($"A workbench with id '{id}' is already registered.");
        return this;
    }

    public RunContentRegistryBuilder SetShredRules(ShredRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _shredRules = rules;
        return this;
    }

    public RunContentRegistry Build() =>
        new(_events, _encounters, _relics, _rewardTables, _consumables, _shops, _programDefinitions,
            _shreds, _recipes, _shredRules, _workbenches);
}
