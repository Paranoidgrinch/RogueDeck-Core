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

    // Pick which node to walk to next on a branching map (B1), among the reachable candidates — also used to pick
    // the initial entry node when a map declares several. The default is a deterministic first-candidate pick
    // (headless runs / providers that don't route); an interactive provider overrides to ask the player. Never
    // called for a linear map. `candidates` is always non-empty.
    NodeId ChooseNextNode(IReadOnlyList<Node> candidates, RunState run) => candidates[0].Id;
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

    // Routes a branching map by scripted node id: reads the same id queue as event choices (a scripted route
    // lists the node ids to walk), falling back to the first candidate when the script has nothing to say —
    // keeping tests and replays deterministic.
    //
    // ⚠ It PEEKS, and only consumes on a match. The walker now asks even where there is a single successor
    // (a corridor is walked, not fallen down), so a route that DEQUEUED while searching would have its whole
    // script eaten by the first corridor before it ever reached the fork it was written for.
    public NodeId ChooseNextNode(IReadOnlyList<Node> candidates, RunState run)
    {
        if (_choiceIds.Count > 0
            && candidates.FirstOrDefault(node => node.Id.Value == _choiceIds.Peek()) is { } match)
        {
            _choiceIds.Dequeue();
            return match.Id;
        }

        return candidates[0].Id;
    }
}

// THE SEAT A PARTY ROOM ANSWERS FROM (multiplayer M2). A choice provider that can park — the interactive
// session — implements this to let a room resolve for one member at a time and to keep one open question per
// member, instead of one for the whole party. A provider that answers inline (a bot, a scripted test) does not
// need it: the members simply answer in turn.
public interface IRunMemberSeat
{
    // Every prompt from here on is this member's, and takes only this member's answers.
    void BeginMember(PartyMember member);

    // The member's room stopped at a question it has no answer for yet; keep that question as the member's.
    void MemberParked(PartyMember member);

    void EndMember();

    // At least one member is waiting: stop the run here, with every member's question standing. Throws.
    void ParkMembers();
}

// A ROOM IS EVERY MEMBER'S OWN. In a party run an event, a shop, a workbench, a combat's reward resolves once per
// member, in seat order, with that member as the run's active member and on that member's own dice — so each
// pays, gains and chooses for themselves. A member whose room stops at a question does not hold the others up:
// the room carries on for the next member and parks for all of them together at the end. A solo run resolves
// the room once, as it always did, with nothing opened around it.
public static class PartyRooms
{
    public static void ForEachMember(NodeResolveContext context, Action<PartyMember> resolve)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolve);
        var run = context.Run;
        if (run.Party.Count <= 1)
        {
            resolve(run.ActiveMember);
            return;
        }

        var seat = context.Choices as IRunMemberSeat;
        var parked = false;
        // Whatever is still queued belongs to the room as a whole (a relic answering the door), not to member 0.
        context.ResolvePendingEffects();
        run.BeginMemberStreams();
        try
        {
            for (var index = 0; index < run.Party.Count; index++)
            {
                var member = run.Party[index];
                using var scope = run.PushActiveMember(member);
                run.UseMemberStream(index);
                seat?.BeginMember(member);
                try
                {
                    resolve(member);
                    context.ResolvePendingEffects();
                }
                catch (Scenario.Scripting.CombatPromptParkedException) when (seat is not null)
                {
                    seat.MemberParked(member);
                    run.DiscardPendingWork();
                    parked = true;
                }
                finally
                {
                    seat?.EndMember();
                }
            }
        }
        finally
        {
            run.EndMemberStreams();
        }

        if (parked)
            seat!.ParkMembers();
    }
}
