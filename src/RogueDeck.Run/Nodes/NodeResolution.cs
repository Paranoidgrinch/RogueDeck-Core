namespace RogueDeck.Run;

// A node resolver drives one interactive episode: it reads RunState, asks the choice provider for any player
// input, and enqueues effects / raises events on the run. It does NOT process the queue itself — RunRunner
// does that afterwards, which is where relics fire. This keeps every node kind uniform: combat, shop, rest,
// random event are all just resolvers that emit run-events.
public interface INodeResolver
{
    NodeType NodeType { get; }
    NodeOutcome Resolve(RunState run, Node node, IRunChoiceProvider choices);
}

// Informational summary of what a resolver did, for logging/inspection. State changes flow through the
// effect queue, not through this value.
public sealed record NodeOutcome(string Summary);

// Player input abstraction. A live game implements this against the UI; tests use ScriptedChoiceProvider.
public interface IRunChoiceProvider
{
    EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> available, RunState run);
}

// Deterministic provider for tests/replays: picks choices by id in a fixed order, falling back to the first
// available choice when the script runs out.
public sealed class ScriptedChoiceProvider : IRunChoiceProvider
{
    private readonly Queue<string> _choiceIds;

    public ScriptedChoiceProvider(params string[] choiceIds)
    {
        _choiceIds = new Queue<string>(choiceIds);
    }

    public EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> available, RunState run)
    {
        if (available.Count == 0)
            throw new InvalidOperationException($"Situation '{situation.Id}' has no available choices.");

        while (_choiceIds.Count > 0)
        {
            var wanted = _choiceIds.Dequeue();
            var match = available.FirstOrDefault(choice => choice.Id == wanted);
            if (match is not null)
                return match;
        }

        return available[0];
    }
}
