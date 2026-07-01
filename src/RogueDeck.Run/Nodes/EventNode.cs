namespace RogueDeck.Run;

// The generic "event engine". An event node is a tiny graph of situations; each situation offers choices;
// each choice optionally requires something of the run, enqueues effects, and transitions to the next
// situation (or ends). Shops, rests, treasure and random encounters are all just authored EventScripts —
// no new resolver type is needed for them.

// A cost is checked before a choice can be taken and paid before its effects run — the difference from a
// plain effect (idea doc §9). Modeled from the existing layers: CanPay is a condition expression, Pay is a
// list of effects. A gold price is HasResource + a resource deduction; an HP price is "survive it" + damage.
public sealed record RunCost(IRunExpression<bool> CanPay, IReadOnlyList<IRunEffectRequest> Pay);

public sealed record EventChoice(
    string Id,
    IReadOnlyList<IRunEffectRequest> Effects,
    string? NextSituationId = null,
    IRunExpression<bool>? Requirement = null,
    string? TextKey = null,
    IReadOnlyList<RunCost>? Costs = null)
{
    // Offered only when visible (Requirement) and affordable (every cost's CanPay holds). Folding
    // affordability into availability keeps the scripted resolver simple: unaffordable choices are not
    // offered, rather than shown-but-disabled. Requirement is a data condition so a choice serializes.
    public bool IsAvailable(RunState run) => (Requirement is null || Requirement.Evaluate(run)) && CanAfford(run);

    public bool CanAfford(RunState run) =>
        Costs is null || Costs.All(cost => cost.CanPay.Evaluate(run));

    // The effects that pay every cost, in cost order — enqueued before the choice's own effects.
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<IRunEffectRequest> PayEffects =>
        Costs is null ? Enumerable.Empty<IRunEffectRequest>() : Costs.SelectMany(cost => cost.Pay);
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

// Marker for a data node payload — one that can be serialized (EventRef / EncounterRef), as opposed to the
// escape payloads (an inline EventScript, or a Func-based CombatNodePayload).
public interface IRunNodePayload
{
}

// A combat node payload's event counterpart: reference an authored event by id (resolved via the content
// registry) instead of embedding the EventScript in the node.
public sealed record EventRef(EventId Id) : IRunNodePayload;

public sealed class EventNodeResolver : INodeResolver
{
    private readonly RunContentRegistry? _content;
    private readonly int _maxSituations;

    public EventNodeResolver(RunContentRegistry? content = null, int maxSituations = 64)
    {
        _content = content;
        _maxSituations = maxSituations;
    }

    public NodeType NodeType => StandardRunIds.EventNode;

    public NodeOutcome Resolve(NodeResolveContext context, Node node)
    {
        var script = ResolveScript(node);

        var run = context.Run;
        var visited = 0;
        var currentId = script.StartSituationId;
        var lastChoiceId = "(none)";

        while (currentId is not null && ++visited <= _maxSituations)
        {
            var situation = script.Situations[currentId];
            var available = situation.Choices.Where(choice => choice.IsAvailable(run)).ToList();
            if (available.Count == 0)
                break;

            var chosen = context.Choices.Choose(situation, available, run);
            // Pay costs first, then run the choice's effects.
            foreach (var effect in chosen.PayEffects)
                run.EnqueueEffect(effect);
            foreach (var effect in chosen.Effects)
                run.EnqueueEffect(effect);

            run.AddLog(StandardRunLogTypes.EventChoiceMade,
                $"Node '{node.Id}': situation '{situation.Id}' -> choice '{chosen.Id}'.");
            run.RaiseEvent(new EventChoiceMadeRunEvent(node.Id, chosen.Id));

            // Apply this choice's effects before the next situation so its requirements (e.g. a shop's
            // affordability check) observe the updated run state rather than stale entry state.
            context.ResolvePendingEffects();

            lastChoiceId = chosen.Id;
            currentId = chosen.NextSituationId;
        }

        return new NodeOutcome($"event resolved (last choice '{lastChoiceId}').");
    }

    // A node carries either an inline EventScript (escape) or a data EventRef resolved via the content registry.
    private EventScript ResolveScript(Node node) => node.Payload switch
    {
        EventScript script => script,
        EventRef reference => _content is not null
            ? _content.GetEvent(reference.Id)
            : throw new InvalidOperationException(
                $"Event node '{node.Id}' references event '{reference.Id}' but the resolver has no content registry."),
        _ => throw new ArgumentException(
            $"Event node '{node.Id}' payload must be an EventScript or an EventRef.", nameof(node)),
    };
}
