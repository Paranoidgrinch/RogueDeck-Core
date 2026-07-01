using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The serializable authoring shape of an enemy action — the enemy-side sibling of CardData. Its Program
// serializes through CombatJson.CreateOptions<EnemyActionContext>() (the same infrastructure, closed on the
// enemy-action context). Map to/from EnemyActionBlueprint with From/ToBlueprint.
public sealed record EnemyActionData
{
    public required string Id { get; init; }
    public string PackageId { get; init; } = "scenario";
    public string? NameKey { get; init; }
    public string? DescriptionKey { get; init; }
    public required ActionIntent Intent { get; init; }
    public EffectProgram<EnemyActionContext>? Program { get; init; }

    public static EnemyActionData From(EnemyActionBlueprint action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new EnemyActionData
        {
            Id = action.Id,
            PackageId = action.PackageId,
            NameKey = action.NameKey,
            DescriptionKey = action.DescriptionKey,
            Intent = action.Intent,
            Program = action.Program,
        };
    }

    public EnemyActionBlueprint ToBlueprint() =>
        new(Id, Intent)
        {
            PackageId = PackageId,
            NameKey = NameKey ?? $"enemy-action.{Id}.name",
            DescriptionKey = DescriptionKey ?? $"enemy-action.{Id}.desc",
            Program = Program,
        };
}
