using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// One player character in the run's party (party deckbuilding B1): the per-member state that persists between
// fights — its own HP pool, run resources (currency included), deck, relics, and consumables. A single-hero run
// has exactly one member (the primary), so RunState delegates its historical single-hero accessors to it and
// existing runs are unchanged. Instance-id generation for cards/consumables stays on RunState (run-scoped, so ids
// are unique across the whole party); this type just owns the collections + their membership operations.
public sealed class PartyMember
{
    public RunMemberId Id { get; }
    public HealthState Health { get; }

    // Combat identity, used when the member is projected into a fight (party deckbuilding B2). Member 0 (the hero)
    // takes its combat identity from the scenario's HeroBlueprint, so its values here are only placeholders.
    public string DisplayNameKey { get; }
    public CombatantDefinitionId DefinitionId { get; }

    private readonly Dictionary<RunResourceId, int> _resources = new();
    private readonly List<RunCardInstance> _deck = new();
    private readonly List<RelicInstance> _relics = new();
    private readonly List<RunConsumable> _consumables = new();
    private readonly Dictionary<string, int> _shreds = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<RunResourceId, int> Resources => _resources;
    public IReadOnlyList<RunCardInstance> Deck => _deck;
    public IReadOnlyList<RelicInstance> Relics => _relics;
    public IReadOnlyList<RunConsumable> Consumables => _consumables;

    // The member's card-part inventory (Shred Engine): shred kind id → owned count. Shreds are fungible per
    // kind (no per-copy state), so a count map — not an instance list — is the honest model.
    public IReadOnlyDictionary<string, int> Shreds => _shreds;

    // Relic / consumable definition ids this member should start with (party deckbuilding B3b). Seeded from the
    // member's authored data by RunSetup and granted by the runner once content is attached, mirroring the hero's
    // RunState.Starting* lists. Member 0 (the hero) keeps using the run-level lists, so these stay empty for it.
    public IReadOnlyList<string> StartingRelicIds { get; private set; } = [];
    public IReadOnlyList<string> StartingConsumableIds { get; private set; } = [];

    public void SetStartingContent(IReadOnlyList<string> relicIds, IReadOnlyList<string> consumableIds)
    {
        StartingRelicIds = relicIds ?? [];
        StartingConsumableIds = consumableIds ?? [];
    }

    public PartyMember(
        RunMemberId id, HealthState health, string? displayNameKey = null, CombatantDefinitionId? definitionId = null)
    {
        ArgumentNullException.ThrowIfNull(health);
        Id = id;
        Health = health;
        DisplayNameKey = displayNameKey ?? $"party.{id.Value}";
        DefinitionId = definitionId ?? new CombatantDefinitionId("hero");
    }

    public int GetResource(RunResourceId resource) =>
        _resources.TryGetValue(resource, out var value) ? value : 0;

    public void SetResource(RunResourceId resource, int amount) =>
        _resources[resource] = Math.Max(0, amount);

    public RunCardInstance AddDeckCard(RunCardInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        _deck.Add(instance);
        return instance;
    }

    public bool RemoveDeckCard(RunCardInstanceId id)
    {
        var index = _deck.FindIndex(c => c.Id == id);
        if (index < 0)
            return false;
        _deck.RemoveAt(index);
        return true;
    }

    public void AddRelic(RelicInstance relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        _relics.Add(relic);
    }

    public bool RemoveRelic(RelicId id)
    {
        var index = _relics.FindIndex(r => r.Id == id);
        if (index < 0)
            return false;
        _relics.RemoveAt(index);
        return true;
    }

    public RelicInstance? FindRelic(RelicId id) => _relics.FirstOrDefault(r => r.Id == id);

    public int GetShredCount(string shredId) =>
        _shreds.TryGetValue(shredId, out var count) ? count : 0;

    public void AddShreds(string shredId, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(shredId))
            throw new ArgumentException("Shred id cannot be empty.", nameof(shredId));
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Shred count must be >= 1.");
        _shreds[shredId] = GetShredCount(shredId) + count;
    }

    // Removes `count` shreds of the kind; false (and no change) when the member holds fewer. Zeroed kinds
    // leave the map so the inventory view never shows empty rows.
    public bool TryRemoveShreds(string shredId, int count = 1)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Shred count must be >= 1.");
        var held = GetShredCount(shredId);
        if (held < count)
            return false;
        if (held == count)
            _shreds.Remove(shredId);
        else
            _shreds[shredId] = held - count;
        return true;
    }

    public RunConsumable AddConsumable(RunConsumable consumable)
    {
        ArgumentNullException.ThrowIfNull(consumable);
        _consumables.Add(consumable);
        return consumable;
    }

    public RunConsumable? FindConsumable(ConsumableInstanceId id) =>
        _consumables.FirstOrDefault(c => c.Id == id);

    public bool RemoveConsumable(ConsumableInstanceId id)
    {
        var index = _consumables.FindIndex(c => c.Id == id);
        if (index < 0)
            return false;
        _consumables.RemoveAt(index);
        return true;
    }
}
