using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// A single card as it persists across a run: its kind (DefinitionId) plus per-copy run-side state — upgrade
// level, tags, and a small integer memory. This is what turns "a card" from a value into an entity that
// events can mutate over time (the idea doc's "cards as persistent run objects"). The state here is run-only;
// how an upgrade or a run tag reaches a spawned combat is a combat-bridge concern (a later phase) — the
// bridge still reads DefinitionId today, so this model changes no combat behaviour on its own.
public sealed class RunCardInstance
{
    private readonly HashSet<RunCardTagId> _tags = new();
    private readonly Dictionary<string, int> _memory = new();

    public RunCardInstanceId Id { get; }
    public CardDefinitionId DefinitionId { get; }
    public int UpgradeLevel { get; private set; }

    public IReadOnlyCollection<RunCardTagId> Tags => _tags;
    public IReadOnlyDictionary<string, int> Memory => _memory;

    public RunCardInstance(RunCardInstanceId id, CardDefinitionId definitionId)
    {
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Card instance id cannot be empty.", nameof(id));
        Id = id;
        DefinitionId = definitionId;
    }

    public void Upgrade(int levels = 1)
    {
        if (levels < 1)
            throw new ArgumentOutOfRangeException(nameof(levels), levels, "Upgrade levels must be >= 1.");
        UpgradeLevel += levels;
    }

    public bool HasTag(RunCardTagId tag) => _tags.Contains(tag);

    // Returns whether the tag set actually changed, so callers can raise events only on a real change.
    public bool AddTag(RunCardTagId tag) => _tags.Add(tag);
    public bool RemoveTag(RunCardTagId tag) => _tags.Remove(tag);

    public int GetMemory(string key) => _memory.TryGetValue(key, out var value) ? value : 0;
    public void SetMemory(string key, int value) => _memory[key] = value;
}
