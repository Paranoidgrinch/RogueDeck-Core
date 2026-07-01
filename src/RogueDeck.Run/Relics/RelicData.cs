namespace RogueDeck.Run;

// The serializable authoring shape of a relic — a relic is just a set of run-level triggered programs, so
// RelicData is id + name + those programs, round-tripping via RunJson (the TriggeredRunEffectJsonConverter
// handles each program). Relics that also inject COMBAT triggers (CombatContributions) are not yet expressible
// as data and are rejected by From.
public sealed record RelicData
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<ITriggeredRunEffectDefinition> RunPrograms { get; init; } = [];

    public static RelicData From(RelicDefinition relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        if (relic.CombatContributions.Count > 0)
            throw new NotSupportedException(
                $"Relic '{relic.Id.Value}' has combat contributions, which are not yet serializable as data.");
        return new RelicData
        {
            Id = relic.Id.Value,
            DisplayName = relic.DisplayName,
            RunPrograms = relic.RunPrograms,
        };
    }

    public RelicDefinition ToDefinition() => new(new RelicId(Id), DisplayName, RunPrograms);
}
