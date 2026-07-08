using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

// The full authored content of a scenario: status / card / enemy-action definitions plus the hero and
// enemies. Compile() folds the definitions into a built CombatDefinitionRegistry (standard package first,
// then the authored content) and collects the harness-side intent metadata. The combatant blueprints are
// passed through for the ScenarioRunner (next step) to instantiate.
public sealed class ScenarioBlueprint
{
    public List<StatusBlueprint> Statuses { get; } = new();
    public List<CardBlueprint> Cards { get; } = new();
    public List<EnemyActionBlueprint> EnemyActions { get; } = new();
    public HeroBlueprint? Hero { get; set; }
    public List<EnemyBlueprint> Enemies { get; } = new();

    // Persistent player-controlled board units fielded alongside the hero (positional combat P5c). Added to the
    // player team; they act on their own turn via the existing machinery (typically a marker-TurnStarted rule).
    // Empty (the default) ⇒ today's single-hero combat, unchanged.
    public List<AllyBlueprint> Allies { get; } = new();

    // Pre-built triggered programs (e.g. a status that runs effects on an event). Registered as-is.
    public List<ITriggeredEffectDefinition> TriggeredPrograms { get; } = new();

    // Pre-built interceptors (death-prevention / status-application blocking). Registered as-is.
    public List<IPreDownInterceptor> PreDownInterceptors { get; } = new();
    public List<IStatusApplicationInterceptor> StatusApplicationInterceptors { get; } = new();

    // Custom resources that should top up to Max at every turn start (like Energy). One turn-start refill
    // handler is registered per entry, in addition to the standard package's Energy refill.
    public List<ResourceRefillSpec> TurnStartResourceRefills { get; } = new();

    // Custom defensive pools (beyond the standard Block) that genuinely absorb damage. Registered as-is;
    // do NOT add Block here — the standard package already registers it.
    public List<DefensivePoolDefinition> DefensivePools { get; } = new();

    // How many cards the hero draws at the start of each turn. The default mirrors the standard 5-card
    // hand; an editor/sandbox can raise it (e.g. to the whole deck) so every authored card is in hand.
    public int CardsDrawnPerTurn { get; set; } = 5;

    // Opt-in board rule: when true, at most one living combatant may occupy a grid cell (movement/summoning into an
    // occupied cell is rejected). Default false ⇒ cells are non-exclusive, so flat and positional combats are
    // unchanged.
    public bool CellExclusive { get; set; }

    // Opt-in party rule (party deckbuilding A2): when true, each team's members take their turn SIMULTANEOUSLY
    // (whole team gets TurnStarted at once, each ends independently), driven by SimultaneousTurnProcessor. Default
    // false ⇒ round-robin, unchanged.
    public bool SimultaneousTeamTurns { get; set; }

    public CompiledScenario Compile()
    {
        if (Hero is null)
            throw new InvalidOperationException("A scenario needs a hero.");
        if (Enemies.Count == 0)
            throw new InvalidOperationException("A scenario needs at least one enemy.");

        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage(CardsDrawnPerTurn).RegisterDefinitions(builder);

        foreach (var status in Statuses)
            builder.RegisterStatus(status.Compile());
        foreach (var card in Cards)
            builder.RegisterCard(card.Compile());

        var intents = new Dictionary<EnemyActionDefinitionId, ActionIntent>();
        foreach (var action in EnemyActions)
        {
            builder.RegisterEnemyAction(action.Compile());
            intents[action.DefinitionId] = action.Intent;
        }

        if (TurnStartResourceRefills.Count > 0)
            builder.RegisterCombatEventHandler(
                new TurnStartResourceRefillHandler(TurnStartResourceRefills.ToList()));

        foreach (var pool in DefensivePools)
            builder.RegisterDefensivePool(pool);

        foreach (var trigger in TriggeredPrograms)
            builder.RegisterTriggeredEffectDefinition(trigger);
        foreach (var interceptor in PreDownInterceptors)
            builder.RegisterPreDownInterceptor(interceptor);
        foreach (var interceptor in StatusApplicationInterceptors)
            builder.RegisterStatusApplicationInterceptor(interceptor);

        // Atomic, validated build of the whole definition set.
        var registry = builder.Build();

        ValidateReferences(registry);

        return new CompiledScenario(registry, intents, Hero, Enemies, Allies, CellExclusive, SimultaneousTeamTurns);
    }

    private void ValidateReferences(CombatDefinitionRegistry registry)
    {
        foreach (var entry in Hero!.Deck)
            if (!registry.TryGetCard(entry.Card, out _))
                throw new InvalidOperationException(
                    $"Hero deck references unknown card '{entry.Card}'. Add a CardBlueprint for it.");

        foreach (var enemy in Enemies)
            foreach (var actionId in enemy.Actions)
                if (!intentsContain(actionId))
                    throw new InvalidOperationException(
                        $"Enemy '{enemy.Id}' references unknown action '{actionId}'. Add an EnemyActionBlueprint for it.");

        bool intentsContain(EnemyActionDefinitionId id) =>
            EnemyActions.Exists(a => a.DefinitionId == id);
    }
}

// The built, ready-to-run output of compiling a ScenarioBlueprint.
public sealed class CompiledScenario
{
    public CombatDefinitionRegistry Registry { get; }
    public IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> Intents { get; }
    public HeroBlueprint Hero { get; }
    public IReadOnlyList<EnemyBlueprint> Enemies { get; }
    public IReadOnlyList<AllyBlueprint> Allies { get; }

    // Whether the fight enforces one-combatant-per-cell (opt-in; default off).
    public bool CellExclusive { get; }

    // Whether each team's members take their turn simultaneously (opt-in; default off ⇒ round-robin).
    public bool SimultaneousTeamTurns { get; }

    internal CompiledScenario(
        CombatDefinitionRegistry registry,
        IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> intents,
        HeroBlueprint hero,
        IReadOnlyList<EnemyBlueprint> enemies,
        IReadOnlyList<AllyBlueprint>? allies = null,
        bool cellExclusive = false,
        bool simultaneousTeamTurns = false)
    {
        Registry = registry;
        Intents = intents;
        Hero = hero;
        Enemies = enemies;
        Allies = allies ?? [];
        CellExclusive = cellExclusive;
        SimultaneousTeamTurns = simultaneousTeamTurns;
    }

    public ActionIntent? IntentFor(EnemyActionDefinitionId actionId) =>
        Intents.TryGetValue(actionId, out var intent) ? intent : null;
}
