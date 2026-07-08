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

    private readonly Dictionary<RunResourceId, int> _resources = new();
    private readonly List<RunCardInstance> _deck = new();
    private readonly List<RelicInstance> _relics = new();
    private readonly List<RunConsumable> _consumables = new();

    public IReadOnlyDictionary<RunResourceId, int> Resources => _resources;
    public IReadOnlyList<RunCardInstance> Deck => _deck;
    public IReadOnlyList<RelicInstance> Relics => _relics;
    public IReadOnlyList<RunConsumable> Consumables => _consumables;

    public PartyMember(RunMemberId id, HealthState health)
    {
        ArgumentNullException.ThrowIfNull(health);
        Id = id;
        Health = health;
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
