namespace RogueDeck.Run;

// The generic "event engine". An event node is a tiny graph of situations; each situation offers choices;
// each choice optionally requires something of the run, enqueues effects, and transitions to the next
// situation (or ends). Shops, rests, treasure and random encounters are all just authored EventScripts —
// no new resolver type is needed for them.

public sealed record EventChoice(
    string Id,
    IReadOnlyList<IRunEffectRequest> Effects,
    string? NextSituationId = null,
    Func<RunState, bool>? Requirement = null,
    string? TextKey = null)
{
    public bool IsAvailable(RunState run) => Requirement?.Invoke(run) ?? true;
}

public sealed record EventSituation(
    string Id,
    string TextKey,
    IReadOnlyList<EventChoice> Choices);

public sealed class EventScript
{
    public string StartSituationId { get; }
    public IReadOnlyDictionary<string, EventSituation> Situations { get; }

    public EventScript(string startSituationId, IReadOnlyList<EventSituation> situations)
    {
        ArgumentNullException.ThrowIfNull(situations);
        if (situations.Count == 0)
            throw new ArgumentException("An event script needs at least one situation.", nameof(situations));

        Situations = situations.ToDictionary(situation => situation.Id);

        if (!Situations.ContainsKey(startSituationId))
            throw new ArgumentException(
                $"Start situation '{startSituationId}' is not in the script.", nameof(startSituationId));

        StartSituationId = startSituationId;
    }
}

public sealed class EventNodeResolver : INodeResolver
{
    private readonly int _maxSituations;

    public EventNodeResolver(int maxSituations = 64)
    {
        _maxSituations = maxSituations;
    }

    public NodeType NodeType => StandardRunIds.EventNode;

    public NodeOutcome Resolve(RunState run, Node node, IRunChoiceProvider choices)
    {
        if (node.Payload is not EventScript script)
            throw new ArgumentException(
                $"Event node '{node.Id}' payload must be an EventScript.", nameof(node));

        var visited = 0;
        var currentId = script.StartSituationId;
        var lastChoiceId = "(none)";

        while (currentId is not null && ++visited <= _maxSituations)
        {
            var situation = script.Situations[currentId];
            var available = situation.Choices.Where(choice => choice.IsAvailable(run)).ToList();
            if (available.Count == 0)
                break;

            var chosen = choices.Choose(situation, available, run);
            foreach (var effect in chosen.Effects)
                run.EnqueueEffect(effect);

            run.AddLog(StandardRunLogTypes.EventChoiceMade,
                $"Node '{node.Id}': situation '{situation.Id}' -> choice '{chosen.Id}'.");
            run.RaiseEvent(new EventChoiceMadeRunEvent(node.Id, chosen.Id));

            lastChoiceId = chosen.Id;
            currentId = chosen.NextSituationId;
        }

        return new NodeOutcome($"event resolved (last choice '{lastChoiceId}').");
    }
}
