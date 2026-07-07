namespace RogueDeck.Run;

// The serializable authoring shape of a consumable KIND — id + display name + the effects applied when a copy is
// used. Round-trips via RunJson (UseEffects are IRunEffectRequest, already polymorphic there). ToDefinition builds
// the registry ConsumableDefinition the by-id grant resolves. The run/relic counterpart of RelicData.
public sealed record ConsumableData
{
    public required string Id { get; init; }
    public string DisplayName { get; init; } = "";
    public IReadOnlyList<IRunEffectRequest> UseEffects { get; init; } = [];

    // Optional combat-use program (a turnStarted RelicCombatRule) applied to the live fight when used DURING combat.
    // Round-trips via RunJson (RelicCombatRuleJsonConverter). Null for run-only consumables.
    public RelicCombatRule? CombatUse { get; init; }

    public ConsumableDefinition ToDefinition() =>
        new(new ConsumableId(Id), string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName, UseEffects, CombatUse);

    public static ConsumableData From(ConsumableDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ConsumableData
        {
            Id = definition.Id.Value,
            DisplayName = definition.DisplayName,
            UseEffects = definition.UseEffects,
            CombatUse = definition.CombatUse,
        };
    }
}
