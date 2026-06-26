using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

// Context passed to an EnemyActionDefinition's EffectProgram during execution.
// Source = the acting combatant; EventTarget = the primary target (may be null for untargeted actions).
public sealed record EnemyActionContext(EnemyActionDefinition Action);

// Immutable runtime definition for a single repeatable enemy action. Construct one through
// EnemyActionDefinitionBuilder and call Build(); combat code only ever sees the sealed result.
// An action may carry an EffectProgram<EnemyActionContext>, legacy ICombatEffectRecipe entries, or both.
// Legacy recipes run first (for compatibility), then the Effect Program executes.
public sealed class EnemyActionDefinition
{
    public EnemyActionDefinitionId Id { get; }
    public PackageId PackageId { get; }
    public string DisplayNameKey { get; }
    public string DescriptionKey { get; }

    public IReadOnlyList<ICombatEffectRecipe<EnemyActionContext>> Effects { get; }

    public EffectProgram<EnemyActionContext>? Program { get; }

    internal EnemyActionDefinition(
        EnemyActionDefinitionId id,
        PackageId packageId,
        string displayNameKey,
        string descriptionKey,
        ImmutableArray<ICombatEffectRecipe<EnemyActionContext>> effects,
        EffectProgram<EnemyActionContext>? program)
    {
        Id = id;
        PackageId = packageId;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey;
        Effects = effects;
        Program = program;
    }
}

// Mutable construction surface for an enemy action definition. Populate the effects/program, then
// call Build() to validate and produce an immutable EnemyActionDefinition. Build() is idempotent.
public sealed class EnemyActionDefinitionBuilder
{
    private EnemyActionDefinition? _built;

    public EnemyActionDefinitionId Id { get; }
    public PackageId PackageId { get; }
    public string DisplayNameKey { get; }
    public string DescriptionKey { get; }

    public List<ICombatEffectRecipe<EnemyActionContext>> Effects { get; } = new();

    public EffectProgram<EnemyActionContext>? Program { get; set; }

    public EnemyActionDefinitionBuilder(
        EnemyActionDefinitionId id,
        PackageId packageId,
        string displayNameKey,
        string descriptionKey)
    {
        Id = id;
        PackageId = packageId;
        DisplayNameKey = displayNameKey;
        DescriptionKey = descriptionKey;
    }

    public EnemyActionDefinition Build()
    {
        if (_built is not null)
            return _built;

        if (string.IsNullOrWhiteSpace(Id.value))
            throw new InvalidOperationException("Enemy action definition ID cannot be empty.");

        for (var i = 0; i < Effects.Count; i++)
        {
            if (Effects[i] is null)
                throw new InvalidOperationException(
                    $"Enemy action '{Id}' has a null recipe at Effects[{i}].");
        }

        var program = Program;
        if (program is { } p && p.Id.Value == "(unnamed)")
            program = p.WithId(new EffectProgramId($"enemy-action:{Id.value}:on-execute"));

        _built = new EnemyActionDefinition(
            Id,
            PackageId,
            DisplayNameKey,
            DescriptionKey,
            Effects.ToImmutableArray(),
            program);

        return _built;
    }
}
