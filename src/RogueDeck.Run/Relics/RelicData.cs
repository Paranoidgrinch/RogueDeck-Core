using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The serializable authoring shape of a relic — a relic is just a set of run-level triggered programs, so
// RelicData is id + name + those programs, round-tripping via RunJson (the TriggeredRunEffectJsonConverter
// handles each program). A relic's COMBAT triggers (CombatContributions — face (b)) are authored here as
// CombatRules: a trigger key + effect program (see RelicCombatRule / RelicCombatTriggers), built into engine
// contributions by ToDefinition. From still can't turn an already-built RelicDefinition's contributions BACK into
// rules (that reverse map is a later slice), so it rejects a definition that carries any.
public sealed record RelicData
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<ITriggeredRunEffectDefinition> RunPrograms { get; init; } = [];

    // Face (b): the relic's combat-injected rules, as data (empty for most relics). Round-trips via RunJson through
    // RelicCombatRuleJsonConverter; ToDefinition turns each into a TriggeredProgramDefinition via its trigger.
    public IReadOnlyList<RelicCombatRule> CombatRules { get; init; } = [];

    // Face (c): the relic's standing shop discounts/surcharges, as data. Null for the great majority of relics,
    // and null is kept out of the wire format so documents written before the field existed round-trip
    // byte-identically.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShopPriceRule>? ShopPriceRules { get; init; }

    // …and what it adds to a shop's shelf. Same null-is-omitted bargain.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShopStockGrant>? ShopStockGrants { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShopService>? ShopServices { get; init; }

    // …and what may settle a price besides the currency itself. Same null-is-omitted bargain.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShopCreditSource>? ShopCreditSources { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ShopDebtTerms>? ShopDebtTerms { get; init; }

    public static RelicData From(RelicDefinition relic)
    {
        ArgumentNullException.ThrowIfNull(relic);
        if (relic.CombatContributions.Count > 0)
            throw new NotSupportedException(
                $"Relic '{relic.Id.Value}' has combat contributions built in code; mapping those back to data " +
                "CombatRules is not supported. Author the relic's combat rules as data instead.");
        return new RelicData
        {
            Id = relic.Id.Value,
            DisplayName = relic.DisplayName,
            RunPrograms = relic.RunPrograms,
            ShopPriceRules = relic.ShopPriceRules.Count > 0 ? relic.ShopPriceRules : null,
            ShopStockGrants = relic.ShopStockGrants.Count > 0 ? relic.ShopStockGrants : null,
            ShopServices = relic.ShopServices.Count > 0 ? relic.ShopServices : null,
            ShopCreditSources = relic.ShopCreditSources.Count > 0 ? relic.ShopCreditSources : null,
            ShopDebtTerms = relic.ShopDebtTerms.Count > 0 ? relic.ShopDebtTerms : null,
        };
    }

    public RelicDefinition ToDefinition()
    {
        var contributions = CombatRules
            .Select((rule, i) => RelicCombatTriggers.Get(rule.Trigger).Build(
                new TriggeredEffectDefinitionId($"{Id}:combat:{i}:{rule.Trigger}"), rule.Program, rule.Priority))
            .ToList();
        return new RelicDefinition(
            new RelicId(Id), DisplayName, RunPrograms, contributions,
            ShopPriceRules, ShopStockGrants, ShopServices, ShopCreditSources, ShopDebtTerms);
    }
}
