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

    public CombatContentLibrary(
        IReadOnlyList<CardBlueprint>? cards = null,
        IReadOnlyList<EnemyActionBlueprint>? enemyActions = null,
        IReadOnlyList<StatusBlueprint>? statuses = null)
    {
        Cards = cards ?? Array.Empty<CardBlueprint>();
        EnemyActions = enemyActions ?? Array.Empty<EnemyActionBlueprint>();
        Statuses = statuses ?? Array.Empty<StatusBlueprint>();
    }
}

// One enemy in an encounter — data referencing action definitions from the library. DisplayName is an optional
// human-readable label for UIs/logs; Id remains the stable slug the fight is keyed on.
public sealed record EncounterEnemy(
    string Id,
    int MaxHealth,
    IReadOnlyList<EnemyActionDefinitionId> Actions,
    IReadOnlyList<StartingStatusSpec>? StartingStatuses = null,
    string? DisplayName = null);

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

        // Hero shell: HP from the run; resources / starting statuses from the encounter; deck projected later.
        blueprint.Hero = new HeroBlueprint("hero")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        foreach (var resource in encounter.HeroResources) blueprint.Hero.Resources.Add(resource);
        foreach (var status in encounter.HeroStartingStatuses) blueprint.Hero.StartingStatuses.Add(status);

        foreach (var spec in encounter.Enemies)
        {
            var enemy = new EnemyBlueprint(spec.Id) { MaxHealth = spec.MaxHealth };
            foreach (var action in spec.Actions) enemy.Actions.Add(action);
            if (spec.StartingStatuses is { } statuses)
                foreach (var status in statuses) enemy.StartingStatuses.Add(status);
            blueprint.Enemies.Add(enemy);
        }

        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: id.Value, randomSeed: randomSeed);
    }
}
