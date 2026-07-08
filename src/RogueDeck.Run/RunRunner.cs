namespace RogueDeck.Run;

// Walks a run's map start-to-finish, the run-layer counterpart of the combat turn loop. It adds NO node
// semantics — it only sequences: raise NodeEntered, dispatch to the registered resolver (purely by
// NodeType — note there is no node-kind switch here), then resolve pending effects (where relics fire),
// then check for defeat. A run ends when the map is exhausted or the hero's HP pool hits zero.
// An optional between-nodes interaction: RunRunner calls BetweenNodes after each node (except the last) so a UI can
// let the player act — view inventory, use consumables — before the next combat/event. Headless runs pass none, so
// the run proceeds node-to-node without pausing.
public interface IRunInterlude
{
    void BetweenNodes(RunState run);
}

public sealed class RunRunner
{
    private readonly RunDefinitionRegistry _registry;
    private readonly IRunChoiceProvider _choices;
    private readonly RunEffectProcessor _processor;
    private readonly RunContentRegistry? _content;
    private readonly IRunInterlude? _interlude;

    public RunRunner(
        RunDefinitionRegistry registry,
        IRunChoiceProvider choices,
        RunEffectProcessor? processor = null,
        RunContentRegistry? content = null,
        IRunInterlude? interlude = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(choices);

        _registry = registry;
        _choices = choices;
        _processor = processor ?? new RunEffectProcessor();
        _content = content;
        _interlude = interlude;
    }

    public void Run(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var context = new NodeResolveContext(run, _choices, _registry, _processor);

        // Bind the run's entity chooser to the choice provider when it can make selections, so selector-based
        // effects (e.g. ChooseByPlayer card removal) can offer choices during effect resolution.
        if (_choices is IRunEntityChooser chooser)
            run.SetEntityChooser(chooser);
        run.SetContent(_content);
        GrantStartingRelics(run);
        GrantStartingConsumables(run);

        run.AddLog(StandardRunLogTypes.RunStarted, $"Run '{run.Id}' started.");
        run.RaiseEvent(new RunStartedRunEvent(run.Id));
        _processor.ResolvePending(run, _registry);

        // Two map shapes over one traversal contract: a linear map (no edges) walks its nodes in order exactly as
        // before; a graph map (edges present) walks by player-chosen path. Both sequence each node identically —
        // only how the NEXT node is selected differs (see ResolveNode).
        if (run.Map.Edges.Count == 0)
            WalkLinear(run, context);
        else
            WalkGraph(run, context);

        if (run.Result == RunResult.Ongoing)
            run.SetResult(RunResult.Victory);

        run.AddLog(StandardRunLogTypes.RunEnded, $"Run '{run.Id}' ended: {run.Result}.");
        run.RaiseEvent(new RunEndedRunEvent(run.Result));
        _processor.ResolvePending(run, _registry);
    }

    // Linear walk (no edges): the historical index loop, unchanged.
    private void WalkLinear(RunState run, NodeResolveContext context)
    {
        for (var index = 0; index < run.Map.Nodes.Count; index++)
        {
            var node = run.Map.Nodes[index];
            run.AdvanceTo(index);

            if (!ResolveNode(run, context, node))
                return;

            // Between-nodes interlude: let a UI act (view inventory, use consumables) before the next node. Not
            // after the last node (the run is about to end) and not headlessly (no interlude provided).
            if (_interlude is not null && index < run.Map.Nodes.Count - 1)
            {
                _interlude.BetweenNodes(run);
                _processor.ResolvePending(run, _registry);
            }
        }
    }

    // Graph walk (edges present): start at an entry node (the player picks when there are several), resolve it,
    // then offer its reachable-and-unvisited successors; the player picks one (or it auto-advances on a single
    // successor). A node with no such successor is a leaf — the boss / finish — and ends the run.
    private void WalkGraph(RunState run, NodeResolveContext context)
    {
        var entries = EntryNodes(run.Map);
        if (entries.Count == 0)
            return; // a map with edges but no reachable entry node has nothing to walk

        var current = PickNode(entries, run);
        while (current is not null)
        {
            run.AddLog(StandardRunLogTypes.NodeChosen, $"Chose node '{current.Id}'.");
            run.RaiseEvent(new NodeChosenRunEvent(current.Id));
            run.AdvanceToNode(current.Id);

            if (!ResolveNode(run, context, current))
                return;

            var successors = run.CurrentReachableNodes();
            if (successors.Count == 0)
                break; // leaf node: the run is complete

            if (_interlude is not null)
            {
                _interlude.BetweenNodes(run);
                _processor.ResolvePending(run, _registry);
            }

            current = PickNode(successors, run);
        }
    }

    // Enter one node: log + raise NodeEntered, dispatch to its registered resolver (purely by NodeType), then
    // resolve pending effects (where relics fire). Returns false when the hero's HP hit zero (a defeat). Shared by
    // both walks so a node is sequenced identically regardless of map shape.
    private bool ResolveNode(RunState run, NodeResolveContext context, Node node)
    {
        run.AddLog(StandardRunLogTypes.NodeEntered, $"Entered node '{node.Id}' ({node.Type}).");
        run.RaiseEvent(new NodeEnteredRunEvent(node.Id, node.Type));

        _registry.GetResolver(node.Type).Resolve(context, node);
        _processor.ResolvePending(run, _registry);

        if (run.Health.Current <= 0)
        {
            run.SetResult(RunResult.Defeat);
            return false;
        }
        return true;
    }

    // Ask the choice provider which candidate to walk to (auto-selecting when there is only one), mapping its
    // chosen id back to the node. Falls back to the first candidate if the id is not among them.
    private Node PickNode(IReadOnlyList<Node> candidates, RunState run)
    {
        if (candidates.Count == 1)
            return candidates[0];
        var chosenId = _choices.ChooseNextNode(candidates, run);
        return candidates.FirstOrDefault(node => node.Id == chosenId) ?? candidates[0];
    }

    // Where a graph walk may begin: the map's declared entry nodes, or — if none are declared — the roots (nodes
    // with no incoming edge). Falls back to the first node if the graph names no root (defensive; RunMapValidator
    // flags the malformed cases). Unknown declared entry ids are skipped.
    private static IReadOnlyList<Node> EntryNodes(RunMap map)
    {
        var ids = map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds();
        var entries = ids
            .Where(id => map.TryGetNode(id, out _))
            .Select(id => map.Nodes.First(node => node.Id == id))
            .ToList();
        if (entries.Count > 0)
            return entries;
        return map.Nodes.Count > 0 ? [map.Nodes[0]] : [];
    }

    // Grant the hero's starting relics (RunState.StartingRelicIds, seeded from RunStart) now that content is
    // attached — resolving each id from the content catalog exactly as an event's "grant relic by id" does.
    // Unknown ids are skipped (the document validator warns about them); this is initial state, so it raises no
    // RelicAcquired event (nothing should react to a relic the hero simply started with).
    private static void GrantStartingRelics(RunState run)
    {
        if (run.Content is null)
            return;
        foreach (var id in run.StartingRelicIds)
        {
            var relicId = new RelicId(id);
            if (!run.Content.HasRelic(relicId))
                continue;
            run.AddRelic(new RelicInstance(run.Content.GetRelic(relicId)));
            run.AddLog(StandardRunLogTypes.RelicAcquired, $"Starting relic '{id}'.");
        }
    }

    private static void GrantStartingConsumables(RunState run)
    {
        if (run.Content is null)
            return;
        foreach (var id in run.StartingConsumableIds)
        {
            var consumableId = new ConsumableId(id);
            if (!run.Content.HasConsumable(consumableId))
                continue;
            var definition = run.Content.GetConsumable(consumableId);
            var consumable = run.AddConsumable(definition.Id, definition.UseEffects, definition.CombatUse);
            run.AddLog(StandardRunLogTypes.ConsumableGained, $"Starting consumable '{id}' ({consumable.Id}).");
            run.RaiseEvent(new ConsumableGainedRunEvent(consumable.Id, consumable.DefinitionId));
        }
    }
}
