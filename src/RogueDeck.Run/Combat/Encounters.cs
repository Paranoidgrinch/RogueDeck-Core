using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run;

// Data-defined combats (R7b). A combat node can carry an EncounterRef (pure data) instead of a hand-authored
// Func<RunState, Playthrough>. The shared combat definitions (cards, enemy actions, statuses) are authored
// ONCE in a CombatContentLibrary; an EncounterDefinition then just names a roster of enemies (by referencing
// those definitions) and the hero's combat resources. The bridge assembles the fight from library + encounter
// + run projection (deck/relics/modifiers), so the run designer composes fights from ids, not code.

// A combat node payload that references an encounter by id (resolved via the resolver's EncounterCatalog).
public sealed record EncounterRef(EncounterId Id) : IRunNodePayload;

// The shared, authored-once combat content: the definitions every encounter draws its cards / enemy actions /
// statuses from. This is the one place combat behaviour (EffectPrograms) is authored; encounters reference it.
public sealed class CombatContentLibrary
{
    public IReadOnlyList<CardBlueprint> Cards { get; }
    public IReadOnlyList<EnemyActionBlueprint> EnemyActions { get; }
    public IReadOnlyList<StatusBlueprint> Statuses { get; }

    // Triggered-effect programs (e.g. a custom status' triggers) to register into every fight so they fire.
    public IReadOnlyList<ITriggeredEffectDefinition> TriggeredPrograms { get; }

    // Status interceptors (death-prevention, debuff-block) to register into every fight.
    public IReadOnlyList<IPreDownInterceptor> PreDownInterceptors { get; }
    public IReadOnlyList<IStatusApplicationInterceptor> StatusApplicationInterceptors { get; }

    // Run-global hero combat resources (energy-like pools) and their per-turn refills, added to every fight's hero
    // (unless an encounter already defines that resource id). Empty for runs with no custom combat resources.
    public IReadOnlyList<ResourceSpec> HeroResources { get; }
    public IReadOnlyList<ResourceRefillSpec> HeroResourceRefills { get; }

    public CombatContentLibrary(
        IReadOnlyList<CardBlueprint>? cards = null,
        IReadOnlyList<EnemyActionBlueprint>? enemyActions = null,
        IReadOnlyList<StatusBlueprint>? statuses = null,
        IReadOnlyList<ITriggeredEffectDefinition>? triggeredPrograms = null,
        IReadOnlyList<IPreDownInterceptor>? preDownInterceptors = null,
        IReadOnlyList<IStatusApplicationInterceptor>? statusApplicationInterceptors = null,
        IReadOnlyList<ResourceSpec>? heroResources = null,
        IReadOnlyList<ResourceRefillSpec>? heroResourceRefills = null)
    {
        Cards = cards ?? Array.Empty<CardBlueprint>();
        EnemyActions = enemyActions ?? Array.Empty<EnemyActionBlueprint>();
        Statuses = statuses ?? Array.Empty<StatusBlueprint>();
        TriggeredPrograms = triggeredPrograms ?? Array.Empty<ITriggeredEffectDefinition>();
        PreDownInterceptors = preDownInterceptors ?? Array.Empty<IPreDownInterceptor>();
        StatusApplicationInterceptors = statusApplicationInterceptors ?? Array.Empty<IStatusApplicationInterceptor>();
        HeroResources = heroResources ?? Array.Empty<ResourceSpec>();
        HeroResourceRefills = heroResourceRefills ?? Array.Empty<ResourceRefillSpec>();
    }
}

// One enemy in an encounter — data referencing action definitions from the library. DisplayName is an optional
// human-readable label for UIs/logs; Id remains the stable slug the fight is keyed on.
public sealed record EncounterEnemy(
    string Id,
    int MaxHealth,
    IReadOnlyList<EnemyActionDefinitionId> Actions,
    IReadOnlyList<StartingStatusSpec>? StartingStatuses = null,
    string? DisplayName = null,
    // Optional starting cell on the 2D combat grid; null = unplaced (flat arena). Round-trips via RunJson.
    CombatPosition? Position = null,
    // State-conditional intent rules (#1). Empty/null ⇒ the enemy cycles Actions by round, as before.
    IReadOnlyList<EnemyIntentRule>? IntentRules = null);

// A combat as data: the enemy roster plus the hero's combat resources / starting statuses. The hero's HP and
// deck come from the run (projected by the bridge), so an encounter is reusable across runs.
public sealed class EncounterDefinition
{
    public EncounterId Id { get; }
    public IReadOnlyList<EncounterEnemy> Enemies { get; }
    public IReadOnlyList<ResourceSpec> HeroResources { get; }
    public IReadOnlyList<StartingStatusSpec> HeroStartingStatuses { get; }

    // Optional human-readable name for the hero in this fight (UIs/logs); the combat identity stays "hero".
    public string? HeroDisplayName { get; }

    public EncounterDefinition(
        EncounterId id,
        IReadOnlyList<EncounterEnemy> enemies,
        IReadOnlyList<ResourceSpec>? heroResources = null,
        IReadOnlyList<StartingStatusSpec>? heroStartingStatuses = null,
        string? heroDisplayName = null)
    {
        if (enemies is null || enemies.Count == 0)
            throw new ArgumentException("An encounter needs at least one enemy.", nameof(enemies));
        Id = id;
        Enemies = enemies;
        HeroResources = heroResources ?? Array.Empty<ResourceSpec>();
        HeroStartingStatuses = heroStartingStatuses ?? Array.Empty<StartingStatusSpec>();
        HeroDisplayName = heroDisplayName;
    }
}

// Resolves an EncounterId to a Playthrough by assembling the library definitions, the encounter roster, and
// the run's hero projection. The deck, relic combat contributions and pending modifiers are applied afterwards
// by the resolver's ApplyRunProjection.
public sealed class EncounterCatalog
{
    private readonly CombatContentLibrary _library;
    private readonly IReadOnlyDictionary<EncounterId, EncounterDefinition> _encounters;

    public EncounterCatalog(CombatContentLibrary library, IEnumerable<EncounterDefinition> encounters)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(encounters);
        _library = library;
        _encounters = encounters.ToDictionary(encounter => encounter.Id);
    }

    public bool Contains(EncounterId id) => _encounters.ContainsKey(id);

    public IEnumerable<EncounterId> Ids => _encounters.Keys;

    public Playthrough Build(EncounterId id, RunState run, int randomSeed)
    {
        if (!_encounters.TryGetValue(id, out var encounter))
            throw new InvalidOperationException($"No encounter registered with id '{id}'.");

        var blueprint = new ScenarioBlueprint();
        // Shared definitions (read-only during Compile, so sharing the instances across fights is safe).
        foreach (var card in _library.Cards) blueprint.Cards.Add(card);
        foreach (var action in _library.EnemyActions) blueprint.EnemyActions.Add(action);
        foreach (var status in _library.Statuses) blueprint.Statuses.Add(status);
        foreach (var trigger in _library.TriggeredPrograms) blueprint.TriggeredPrograms.Add(trigger);
        foreach (var interceptor in _library.PreDownInterceptors) blueprint.PreDownInterceptors.Add(interceptor);
        foreach (var interceptor in _library.StatusApplicationInterceptors) blueprint.StatusApplicationInterceptors.Add(interceptor);

        // Hero shell: HP from the run; resources / starting statuses from the encounter; deck projected later.
        blueprint.Hero = new HeroBlueprint("hero")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        foreach (var resource in encounter.HeroResources) blueprint.Hero.Resources.Add(resource);
        foreach (var status in encounter.HeroStartingStatuses) blueprint.Hero.StartingStatuses.Add(status);

        // Run-global combat resources: add each to the hero unless the encounter already defines that id, and
        // install its per-turn refill (energy-like). This is how a designer's custom combat resource reaches every fight.
        var heroResourceIds = encounter.HeroResources.Select(r => r.Resource).ToHashSet();
        foreach (var resource in _library.HeroResources)
            if (heroResourceIds.Add(resource.Resource))
                blueprint.Hero.Resources.Add(resource);
        foreach (var refill in _library.HeroResourceRefills)
            blueprint.TurnStartResourceRefills.Add(refill);

        foreach (var spec in encounter.Enemies)
        {
            var enemy = new EnemyBlueprint(spec.Id) { MaxHealth = spec.MaxHealth, Position = spec.Position };
            foreach (var action in spec.Actions) enemy.Actions.Add(action);
            if (spec.StartingStatuses is { } statuses)
                foreach (var status in statuses) enemy.StartingStatuses.Add(status);
            if (spec.IntentRules is { } rules)
                foreach (var rule in rules) enemy.IntentRules.Add(rule);
            blueprint.Enemies.Add(enemy);
        }

        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: id.Value, randomSeed: randomSeed);
    }
}
