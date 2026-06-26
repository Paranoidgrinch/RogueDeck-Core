using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Scripting;

// A runnable playthrough: the authored content (blueprint) plus an ordered script of steps and the
// deterministic combat seed. Build the step list fluently with ScenarioScript for readability.
// (Named Playthrough rather than Scenario so the type never collides with the RogueDeck.Scenario
// namespace in consuming code.)
public sealed class Playthrough
{
    public ScenarioBlueprint Blueprint { get; }
    public IReadOnlyList<ScenarioStep> Steps { get; }
    public string CombatId { get; }
    public int RandomSeed { get; }

    public Playthrough(
        ScenarioBlueprint blueprint,
        IReadOnlyList<ScenarioStep> steps,
        string combatId = "scenario",
        int randomSeed = 1)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(steps);
        if (string.IsNullOrWhiteSpace(combatId))
            throw new ArgumentException("Combat id cannot be empty.", nameof(combatId));

        Blueprint = blueprint;
        Steps = steps;
        CombatId = combatId;
        RandomSeed = randomSeed;
    }
}

// Fluent builder for a readable scenario script. Each method appends one step and returns the builder.
public sealed class ScenarioScript
{
    private readonly List<ScenarioStep> _steps = new();

    public ScenarioScript HeroPlays(string cardId, string? target = null)
    {
        _steps.Add(new HeroPlaysCard(cardId, target));
        return this;
    }

    public ScenarioScript HeroEndsTurn()
    {
        _steps.Add(new HeroEndsTurn());
        return this;
    }

    public ScenarioScript EnemyActs(string enemyId, string actionId, string? target = null)
    {
        _steps.Add(new EnemyActs(enemyId, actionId, target));
        return this;
    }

    public ScenarioScript NextRound()
    {
        _steps.Add(new AdvanceToNextRound());
        return this;
    }

    public IReadOnlyList<ScenarioStep> Build() => _steps.ToList();
}
