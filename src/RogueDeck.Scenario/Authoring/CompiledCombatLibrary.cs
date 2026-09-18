using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Authoring;

/// <summary>
/// A game's authored combat content, compiled ONCE and shared by every fight built from it.
///
/// The cards, statuses and enemy actions of a game do not change between its fights — only the roster, the
/// hero's projected deck and whatever the run adds on top do. Compiling that shared half for every single
/// fight is therefore pure repetition: it is the same definitions, built from the same blueprints, validated
/// against the same rules, thrown away, and built again. A library is that half, held.
///
/// A <see cref="ScenarioBlueprint"/> that names a library (see <see cref="ScenarioBlueprint.Library"/>)
/// starts its registry from the library's built one and registers only what is its own.
/// </summary>
public sealed class CompiledCombatLibrary
{
    /// <summary>The built registry holding the standard package plus all the authored library definitions.</summary>
    public CombatDefinitionRegistry Registry { get; }

    /// <summary>The harness-side intent metadata of the library's enemy actions.</summary>
    public IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> Intents { get; }

    /// <summary>
    /// The per-turn draw count the standard package in <see cref="Registry"/> was built with. A blueprint
    /// that draws a different number of cards cannot use this library — its draw handler would be wrong —
    /// so <see cref="ScenarioBlueprint.Compile"/> refuses that combination out loud rather than quietly
    /// dealing the wrong hand.
    /// </summary>
    public int CardsDrawnPerTurn { get; }

    private CompiledCombatLibrary(
        CombatDefinitionRegistry registry,
        IReadOnlyDictionary<EnemyActionDefinitionId, ActionIntent> intents,
        int cardsDrawnPerTurn)
    {
        Registry = registry;
        Intents = intents;
        CardsDrawnPerTurn = cardsDrawnPerTurn;
    }

    /// <summary>
    /// Compile the shared half: the standard package plus the authored definitions that every fight in this
    /// game has. Everything a fight owns for itself — its hero, its enemies, its own triggers — stays out.
    /// </summary>
    public static CompiledCombatLibrary Compile(
        int cardsDrawnPerTurn = 5,
        IEnumerable<StatusBlueprint>? statuses = null,
        IEnumerable<CardBlueprint>? cards = null,
        IEnumerable<EnemyActionBlueprint>? enemyActions = null,
        IEnumerable<ITriggeredEffectDefinition>? triggeredPrograms = null,
        IEnumerable<IPreDownInterceptor>? preDownInterceptors = null,
        IEnumerable<IStatusApplicationInterceptor>? statusApplicationInterceptors = null,
        IEnumerable<ResourceRefillSpec>? turnStartResourceRefills = null,
        IEnumerable<DefensivePoolDefinition>? defensivePools = null)
    {
        var builder = new CombatDefinitionRegistryBuilder();
        new StandardCombatPackage(cardsDrawnPerTurn).RegisterDefinitions(builder);

        foreach (var status in statuses ?? [])
            builder.RegisterStatus(status.Compile());
        foreach (var card in cards ?? [])
            builder.RegisterCard(card.Compile());

        var intents = new Dictionary<EnemyActionDefinitionId, ActionIntent>();
        foreach (var action in enemyActions ?? [])
        {
            builder.RegisterEnemyAction(action.Compile());
            intents[action.DefinitionId] = action.Intent;
        }

        var refills = (turnStartResourceRefills ?? []).ToList();
        if (refills.Count > 0)
            builder.RegisterCombatEventHandler(new TurnStartResourceRefillHandler(refills));

        foreach (var pool in defensivePools ?? [])
            builder.RegisterDefensivePool(pool);

        foreach (var trigger in triggeredPrograms ?? [])
            builder.RegisterTriggeredEffectDefinition(trigger);
        foreach (var interceptor in preDownInterceptors ?? [])
            builder.RegisterPreDownInterceptor(interceptor);
        foreach (var interceptor in statusApplicationInterceptors ?? [])
            builder.RegisterStatusApplicationInterceptor(interceptor);

        return new CompiledCombatLibrary(builder.Build(), intents, cardsDrawnPerTurn);
    }
}
