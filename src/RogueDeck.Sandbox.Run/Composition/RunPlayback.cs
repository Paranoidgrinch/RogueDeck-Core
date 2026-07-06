using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Composition;

// Builds and drives an InteractiveRunSession from a RunBlueprint — the shared play machinery behind both the Run
// tab (drive the authored map) and the Playtest tab (drive a one-encounter run built from a chosen encounter).
// Owns the session + combat-driver lifecycle and the per-card / per-enemy display-name lookups the RunSessionView
// needs. The host passes an onChanged callback (its StateHasChanged) that we hook onto the session's and driver's
// Changed events so the UI re-renders as the run advances.
public sealed class RunPlayback(Action onChanged) : IDisposable
{
    public InteractiveRunSession? Session { get; private set; }
    public InteractiveCombatDriver? CombatDriver { get; private set; }
    public string? Error { get; private set; }

    public IReadOnlyDictionary<string, int> CardCosts { get; private set; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, string> CardNames { get; private set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> EnemyNames { get; private set; } = new Dictionary<string, string>();
    public string? HeroName { get; private set; }

    // Start (or restart) a run from the blueprint. interactive=true hands each fight to the player via an
    // InteractiveCombatDriver (surfaced through CombatDriver); false auto-resolves fights headlessly.
    public void Start(RunBlueprint blueprint, int seed, bool interactive)
    {
        Error = null;
        Dispose();
        try
        {
            var content = BuildContent(blueprint);
            (CardCosts, CardNames, EnemyNames, HeroName) = DisplayNames(blueprint);

            ICombatDriver driver;
            if (interactive)
            {
                CombatDriver = new InteractiveCombatDriver();
                CombatDriver.Changed += onChanged;
                driver = CombatDriver;
            }
            else
            {
                driver = new AutoPlayCombatDriver();
            }

            var defs = new RunDefinitionRegistryBuilder();
            new StandardRunPackage(driver, content).RegisterDefinitions(defs);
            var registry = defs.Build();

            var run = blueprint.CreateInitialRun(new RunId("play"), seed);
            var session = new InteractiveRunSession(run, registry, content);
            session.Changed += onChanged;
            Session = session;
            session.Start();
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (Session is not null)
        {
            Session.Changed -= onChanged;
            Session.Dispose();
            Session = null;
        }
        if (CombatDriver is not null)
        {
            CombatDriver.Changed -= onChanged;
            CombatDriver.Dispose();
            CombatDriver = null;
        }
    }

    // Energy cost + display name per card id, enemy display names per enemy id, and the single hero name (authored,
    // else the first encounter's hero name), so the fight view can grey out unaffordable cards and show readable names.
    private static (Dictionary<string, int>, Dictionary<string, string>, Dictionary<string, string>, string?) DisplayNames(
        RunBlueprint blueprint)
    {
        var cardCosts = blueprint.Cards.ToDictionary(
            card => card.Id,
            card => card.Costs.Where(rc => rc.ResourceId == StandardCombatIds.EnergyResource).Sum(rc => rc.Amount));
        var cardNames = blueprint.Cards.ToDictionary(card => card.Id, card => card.NameKey ?? card.Id);
        var enemyNames = blueprint.Encounters
            .SelectMany(encounter => encounter.Enemies)
            .GroupBy(enemy => enemy.Id)
            .ToDictionary(g => g.Key, g => g.First().DisplayName ?? g.Key);
        var heroName = !string.IsNullOrWhiteSpace(blueprint.Start.HeroName)
            ? blueprint.Start.HeroName
            : blueprint.Encounters
                .Select(encounter => encounter.HeroDisplayName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        return (cardCosts, cardNames, enemyNames, heroName);
    }

    // The whole combat content library (cards + enemy actions, including their effect programs) is authored as data
    // in the blueprint; only the sample relics are still code-provided by id. Shared by the Run tab's drive/simulate
    // and the Playtest tab.
    public static RunContentRegistry BuildContent(RunBlueprint blueprint)
    {
        var interceptors = CombatImport.RebuildStatusInterceptors(blueprint.Statuses);
        var library = new CombatContentLibrary(
            cards: blueprint.Cards.Select(card => card.ToBlueprint()).ToArray(),
            enemyActions: blueprint.EnemyActions.Select(action => action.ToBlueprint()).ToArray(),
            statuses: blueprint.Statuses.Select(status => status.ToBlueprint()).ToArray(),
            triggeredPrograms: CombatImport.RebuildStatusTriggers(blueprint.Statuses),
            preDownInterceptors: interceptors.PreDown,
            statusApplicationInterceptors: interceptors.StatusApplication);

        var builder = new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(library, blueprint.Encounters));
        RegisterRelics(builder, blueprint.Relics);
        foreach (var (id, script) in blueprint.Events)
            builder.RegisterEvent(new EventId(id), script);
        return builder.Build();
    }

    // Registers the blueprint's authored relics, then the built-in sample relics (bloodstone/leech) unless an
    // authored relic already claims that id. Keeps single-source data authoring while the samples stay available.
    private static void RegisterRelics(RunContentRegistryBuilder builder, IReadOnlyList<RelicData> relics)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relic in relics)
            if (ids.Add(relic.Id))
                builder.RegisterRelic(relic.ToDefinition());
        if (ids.Add("bloodstone")) builder.RegisterRelic(StandardRelics.Bloodstone());
        if (ids.Add("leech")) builder.RegisterRelic(StandardRelics.Leech());
    }
}
