using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// Author-friendly, mutable specifications that compile into the engine's immutable definitions via the
// standard builders. Blueprints add NO combat semantics — they only assemble existing engine types. Ids
// and localisation keys are plain strings for hand-authoring convenience; Compile() wraps them.

public sealed class StatusBlueprint
{
    public string Id { get; }
    public string PackageId { get; init; } = "scenario";
    public string NameKey { get; init; }
    public string DescriptionKey { get; init; }
    public StatusPolarity Polarity { get; init; } = StatusPolarity.Neutral;
    public bool UsesStacks { get; init; }
    public bool UsesDuration { get; init; }
    public bool UsesCharges { get; init; }
    public StatusStackingBehavior StackingBehavior { get; init; } = StatusStackingBehavior.CreateSeparateInstance;
    public List<TagId> Tags { get; } = new();
    public List<PassiveModifierSpec> PassiveModifiers { get; } = new();

    public StatusBlueprint(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Status id cannot be empty.", nameof(id));
        Id = id;
        NameKey = $"status.{id}.name";
        DescriptionKey = $"status.{id}.desc";
    }

    public StatusDefinitionId DefinitionId => new(Id);

    public StatusDefinition Compile()
    {
        var def = new StatusDefinition(
            DefinitionId, new PackageId(PackageId), NameKey, DescriptionKey,
            polarity: Polarity, usesStacks: UsesStacks, usesDuration: UsesDuration, usesCharges: UsesCharges,
            stackingBehavior: StackingBehavior, passiveModifiers: PassiveModifiers);
        foreach (var tag in Tags) def.Tags.Add(tag);
        return def;
    }
}

public sealed class CardBlueprint
{
    public string Id { get; }
    public string PackageId { get; init; } = "scenario";
    public string NameKey { get; init; }
    public string DescriptionKey { get; init; }
    public List<ResourceCost> Costs { get; } = new();
    public List<TagId> Tags { get; } = new();
    public EffectProgram<CardPlayContext>? Program { get; set; }

    public CardBlueprint(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Card id cannot be empty.", nameof(id));
        Id = id;
        NameKey = $"card.{id}.name";
        DescriptionKey = $"card.{id}.desc";
    }

    public CardDefinitionId DefinitionId => new(Id);

    // Convenience: a single-resource cost (the common case).
    public CardBlueprint Cost(ResourceId resource, int amount)
    {
        Costs.Add(new ResourceCost(resource, amount));
        return this;
    }

    public CardDefinitionBuilder Compile()
    {
        var builder = new CardDefinitionBuilder(DefinitionId, new PackageId(PackageId), NameKey, DescriptionKey)
        {
            Program = Program,
        };
        builder.Costs.AddRange(Costs);
        builder.Tags.AddRange(Tags);
        return builder;
    }
}

public sealed class EnemyActionBlueprint
{
    public string Id { get; }
    public string PackageId { get; init; } = "scenario";
    public string NameKey { get; init; }
    public string DescriptionKey { get; init; }
    public ActionIntent Intent { get; }
    public EffectProgram<EnemyActionContext>? Program { get; set; }

    public EnemyActionBlueprint(string id, ActionIntent intent)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Enemy action id cannot be empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(intent);
        Id = id;
        Intent = intent;
        NameKey = $"enemy-action.{id}.name";
        DescriptionKey = $"enemy-action.{id}.desc";
    }

    public EnemyActionDefinitionId DefinitionId => new(Id);

    public EnemyActionDefinitionBuilder Compile() =>
        new(DefinitionId, new PackageId(PackageId), NameKey, DescriptionKey) { Program = Program };
}

// ── Combatant specifications (consumed by the ScenarioRunner in the next step) ──

public sealed record ResourceSpec(ResourceId Resource, int Current, int Max);
public sealed record StartingStatusSpec(StatusDefinitionId Status, int Stacks = 0, int DurationTurns = 0, int Charges = 0);
public sealed record DeckEntry(CardDefinitionId Card, int Count = 1);

public abstract class CombatantBlueprint
{
    public string Id { get; }
    public string NameKey { get; init; }
    public int MaxHealth { get; init; } = 1;
    public List<ResourceSpec> Resources { get; } = new();
    public List<StartingStatusSpec> StartingStatuses { get; } = new();

    protected CombatantBlueprint(string id, string nameKeyPrefix)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Combatant id cannot be empty.", nameof(id));
        Id = id;
        NameKey = $"{nameKeyPrefix}.{id}.name";
    }

    public CombatantId CombatantId => new(Id);
}

public sealed class HeroBlueprint : CombatantBlueprint
{
    public List<DeckEntry> Deck { get; } = new();
    public HeroBlueprint(string id) : base(id, "hero") { }
}

public sealed class EnemyBlueprint : CombatantBlueprint
{
    // Ordered action script — the runner cycles these; intents are surfaced into the narrative log.
    public List<EnemyActionDefinitionId> Actions { get; } = new();
    public EnemyBlueprint(string id) : base(id, "enemy") { }
}
