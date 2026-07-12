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

    // Per-card lifecycle programs (e.g. TurnEndInHand for a burn/curse). Empty for an ordinary card.
    public Dictionary<CardLifecycleTrigger, EffectProgram<CardLifecycleContext>> LifecyclePrograms { get; init; } = new();

    // Card-lifecycle flags (default to the standard discard-on-everything behaviour).
    public bool RetainInHandOnTurnEnd { get; set; }
    public CardZone TurnEndHandDestinationZone { get; set; } = CardZone.DiscardPile;
    public CardZone PlayedCardDestinationZone { get; set; } = CardZone.DiscardPile;

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
            RetainInHandOnTurnEnd = RetainInHandOnTurnEnd,
            TurnEndHandDestinationZone = TurnEndHandDestinationZone,
            PlayedCardDestinationZone = PlayedCardDestinationZone,
        };
        builder.Costs.AddRange(Costs);
        builder.Tags.AddRange(Tags);
        foreach (var (trigger, program) in LifecyclePrograms)
            builder.LifecyclePrograms[trigger] = program;
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

// Requests that a resource be topped up to Max at the start of every combatant's turn — the same automation
// the standard package installs for Energy. Registered by ScenarioBlueprint.Compile().
public sealed record ResourceRefillSpec(ResourceId Resource, int Max);
public sealed record StartingStatusSpec(StatusDefinitionId Status, int Stacks = 0, int DurationTurns = 0, int Charges = 0);
public sealed record DeckEntry(CardDefinitionId Card, int Count = 1);

// A temporary triggered rule to install on the combatant when the combat opens — e.g. a consumable's "next combat
// starts with X" opening: a OneShot turnStarted program that fires once at the hero's first turn start (after
// block's turn-start clear), then removes itself. Installed by ScenarioCombatFactory via
// InstallTemporaryRuleEffectRequest; Lifetime governs how long it lives within that fight.
public sealed record TemporaryRuleInstallSpec(ITriggeredEffectDefinition Rule, TemporaryRuleLifetime Lifetime);

public abstract class CombatantBlueprint
{
    public string Id { get; }
    public string NameKey { get; init; }
    public int MaxHealth { get; init; } = 1;

    // Starting current health. Null (the default) means "full" — start at MaxHealth. The run layer sets this
    // so a wounded hero carries their current HP into the next fight instead of being healed to full.
    public int? CurrentHealth { get; init; }

    public List<ResourceSpec> Resources { get; } = new();
    public List<StartingStatusSpec> StartingStatuses { get; } = new();

    // The combatant's own deck, dealt into its draw pile at combat start (party deckbuilding A1). Any player-team
    // combatant — the hero or a fielded ally/party member — draws + plays from its own deck through the existing
    // per-combatant card machinery. Empty (the default) ⇒ a deckless combatant (enemies, auto-acting board units).
    public List<DeckEntry> Deck { get; } = new();

    // Optional starting cell on the 2D combat grid; null = unplaced (flat arena, today's behavior). Applied by
    // ScenarioCombatFactory when the combatant is added.
    public CombatPosition? Position { get; init; }

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
    // Temporary rules installed when the combat opens (e.g. a consumable's "next combat starts with 20 block").
    // Applied by the run→combat bridge as pending combat modifiers; installed by ScenarioCombatFactory at build.
    public List<TemporaryRuleInstallSpec> OpeningTemporaryRules { get; } = new();

    public HeroBlueprint(string id) : base(id, "hero") { }
}

public sealed class EnemyBlueprint : CombatantBlueprint
{
    // Ordered action script — the runner cycles these; intents are surfaced into the narrative log.
    public List<EnemyActionDefinitionId> Actions { get; } = new();

    // State-conditional intent rules (#1). Evaluated highest-Priority first each turn; the first rule whose
    // condition matches the live combat state overrides the Actions cycle. Empty (the default) ⇒ pure cycling,
    // identical to before. See EnemyIntentRules.cs.
    public List<EnemyIntentRule> IntentRules { get; } = new();

    public EnemyBlueprint(string id) : base(id, "enemy") { }
}

// A persistent player-controlled board unit fielded alongside the hero (positional combat P5c). It is added to the
// PLAYER team and acts on its own turn through the existing machinery — typically a marker-status-filtered
// TurnStarted-triggered program (the P5a auto-action), so it carries no action script of its own. The run→combat
// bridge projects each roster unit into an AllyBlueprint (id + carried HP via CurrentHealth + position + statuses).
public sealed class AllyBlueprint : CombatantBlueprint
{
    public AllyBlueprint(string id) : base(id, "ally") { }
}
