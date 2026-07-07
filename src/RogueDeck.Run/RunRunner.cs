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

        for (var index = 0; index < run.Map.Nodes.Count; index++)
        {
            var node = run.Map.Nodes[index];
            run.AdvanceTo(index);

            run.AddLog(StandardRunLogTypes.NodeEntered, $"Entered node '{node.Id}' ({node.Type}).");
            run.RaiseEvent(new NodeEnteredRunEvent(node.Id, node.Type));

            _registry.GetResolver(node.Type).Resolve(context, node);
            _processor.ResolvePending(run, _registry);

            if (run.Health.Current <= 0)
            {
                run.SetResult(RunResult.Defeat);
                break;
            }

            // Between-nodes interlude: let a UI act (view inventory, use consumables) before the next node. Not
            // after the last node (the run is about to end) and not headlessly (no interlude provided).
            if (_interlude is not null && index < run.Map.Nodes.Count - 1)
            {
                _interlude.BetweenNodes(run);
                _processor.ResolvePending(run, _registry);
            }
        }

        if (run.Result == RunResult.Ongoing)
            run.SetResult(RunResult.Victory);

        run.AddLog(StandardRunLogTypes.RunEnded, $"Run '{run.Id}' ended: {run.Result}.");
        run.RaiseEvent(new RunEndedRunEvent(run.Result));
        _processor.ResolvePending(run, _registry);
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
