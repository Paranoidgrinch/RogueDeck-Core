namespace RogueDeck.Run;

// A node resolver drives one interactive episode: it reads RunState, asks the choice provider for any player
// input, and enqueues effects / raises events on the run. RunRunner resolves the queue after the resolver
// returns (where relics fire); a resolver that needs intermediate state mid-episode can also flush it itself
// via the context (e.g. a shop re-checking affordability after each purchase). This keeps every node kind
// uniform: combat, shop, rest, random event are all just resolvers that emit run-events.
public interface INodeResolver
{
    NodeType NodeType { get; }
    NodeOutcome Resolve(NodeResolveContext context, Node node);
}

// What a resolver is handed: the run, the player-input provider, and the means to apply pending effects to a
// fixed point on demand (which is also where relics react). Built per node by RunRunner.
public sealed class NodeResolveContext
{
    private readonly RunDefinitionRegistry _registry;
    private readonly RunEffectProcessor _processor;

    public RunState Run { get; }
    public IRunChoiceProvider Choices { get; }

    public NodeResolveContext(
        RunState run,
        IRunChoiceProvider choices,
        RunDefinitionRegistry registry,
        RunEffectProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(processor);

        Run = run;
        Choices = choices;
        _registry = registry;
        _processor = processor;
    }

    // Drain the run's pending effects/events now (instead of waiting for RunRunner's post-pass). Lets a
    // multi-step event observe its own earlier effects — e.g. gold spent on a previous purchase.
    public void ResolvePendingEffects() => _processor.ResolvePending(Run, _registry);
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
// available choice when the script runs out. It also serves as the entity chooser, selecting the first `count`
// candidates — enough for deterministic tests and replays.
public sealed class ScriptedChoiceProvider : IRunChoiceProvider, IRunEntityChooser
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

    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
        candidates.Take(count).ToArray();
}
