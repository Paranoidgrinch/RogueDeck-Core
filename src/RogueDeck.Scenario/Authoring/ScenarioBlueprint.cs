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

    // Opt-in: shuffle each player-team combatant's draw pile at combat start (deterministically, seeded by the
    // combat's random seed) so the opening hand is randomized like a real deckbuilder — not the deck's authored
    // order. Default false keeps scenario tests deterministic; the RUN layer turns it on for every real fight.
    // Innate cards are still pulled to the top AFTER the shuffle, so they stay in the opening hand.
    public bool ShuffleDrawPileOnStart { get; set; }

    // The shared, already-compiled half of this game's combat content (see CompiledCombatLibrary). When set,
    // Compile() starts from the library's built registry instead of building the standard package and the
    // library's definitions again — which is what makes assembling a fight cheap. Null (the default) keeps the
    // self-contained behaviour: everything this blueprint needs is authored on this blueprint.
    public CompiledCombatLibrary? Library { get; set; }

    public CompiledScenario Compile()
    {
        if (Hero is null)
            throw new InvalidOperationException("A scenario needs a hero.");
        if (Enemies.Count == 0)
            throw new InvalidOperationException("A scenario needs at least one enemy.");
        if (Library is { } mismatched && mismatched.CardsDrawnPerTurn != CardsDrawnPerTurn)
            throw new InvalidOperationException(
                $"This scenario draws {CardsDrawnPerTurn} card(s) per turn but its library was compiled for "
                + $"{mismatched.CardsDrawnPerTurn}. Compile a library for this draw count instead — the draw "
                + "handler lives in the library's standard package and cannot be corrected afterwards.");

        // With a library: start from its built registry and add only what this fight brings. Without one:
        // build the whole thing, exactly as before.
        var builder = Library is { } library
            ? new CombatDefinitionRegistryBuilder(library.Registry)
            : new CombatDefinitionRegistryBuilder();
        if (Library is null)
            new StandardCombatPackage(CardsDrawnPerTurn).RegisterDefinitions(builder);

        foreach (var status in Statuses)
            builder.RegisterStatus(status.Compile());
        foreach (var card in Cards)
            builder.RegisterCard(card.Compile());

        // The library's intents, plus this blueprint's own when it has any. A fight that adds no enemy actions
        // — which is every fight assembled from a catalog — reads the library's dictionary where it lies,
        // rather than copying a few hundred entries to change none of them.
        IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> intents;
        if (EnemyActions.Count == 0 && Library is { } onlyTheLibrarys)
        {
            intents = onlyTheLibrarys.Intents;
        }
        else
        {
            var mine = Library is { } withIntents
                ? new Dictionary<EnemyActionDefinitionId, ActionIntent>(withIntents.Intents)
                : new Dictionary<EnemyActionDefinitionId, ActionIntent>();
            foreach (var action in EnemyActions)
            {
                builder.RegisterEnemyAction(action.Compile());
                mine[action.DefinitionId] = action.Intent;
            }
            intents = mine;
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

        ValidateReferences(registry, intents);

        return new CompiledScenario(
            registry, intents, Hero, Enemies, Allies, CellExclusive, SimultaneousTeamTurns, ShuffleDrawPileOnStart);
    }

    // The known actions are the COMPILED ones (library + this blueprint's own), not this blueprint's list:
    // with a library, the actions an enemy names are registered there and this blueprint's list is empty.
    private void ValidateReferences(
        CombatDefinitionRegistry registry, IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> intents)
    {
        foreach (var entry in Hero!.Deck)
            if (!registry.TryGetCard(entry.Card, out _))
                throw new InvalidOperationException(
                    $"Hero deck references unknown card '{entry.Card}'. Add a CardBlueprint for it.");

        foreach (var enemy in Enemies)
        {
            foreach (var actionId in enemy.Actions)
                if (!intentsContain(actionId))
                    throw new InvalidOperationException(
                        $"Enemy '{enemy.Id}' references unknown action '{actionId}'. Add an EnemyActionBlueprint for it.");

            foreach (var rule in enemy.IntentRules)
                if (!intentsContain(rule.Action))
                    throw new InvalidOperationException(
                        $"Enemy '{enemy.Id}' intent rule references unknown action '{rule.Action}'. Add an EnemyActionBlueprint for it.");
        }

        bool intentsContain(EnemyActionDefinitionId id) => intents.ContainsKey(id);
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

    // Whether to shuffle each player draw pile at combat start (opt-in; default off ⇒ authored deck order).
    public bool ShuffleDrawPileOnStart { get; }

    internal CompiledScenario(
        CombatDefinitionRegistry registry,
        IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> intents,
        HeroBlueprint hero,
        IReadOnlyList<EnemyBlueprint> enemies,
        IReadOnlyList<AllyBlueprint>? allies = null,
        bool cellExclusive = false,
        bool simultaneousTeamTurns = false,
        bool shuffleDrawPileOnStart = false)
    {
        Registry = registry;
        Intents = intents;
        Hero = hero;
        Enemies = enemies;
        Allies = allies ?? [];
        CellExclusive = cellExclusive;
        SimultaneousTeamTurns = simultaneousTeamTurns;
        ShuffleDrawPileOnStart = shuffleDrawPileOnStart;
    }

    public ActionIntent? IntentFor(EnemyActionDefinitionId actionId) =>
        Intents.TryGetValue(actionId, out var intent) ? intent : null;
}
