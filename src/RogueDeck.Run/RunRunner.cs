namespace RogueDeck.Run;

// Walks a run's map start-to-finish, the run-layer counterpart of the combat turn loop. It adds NO node
// semantics — it only sequences: raise NodeEntered, dispatch to the registered resolver (purely by
// NodeType — note there is no node-kind switch here), then resolve pending effects (where relics fire),
// then check for defeat. A run ends when the map is exhausted or the hero's HP pool hits zero.
public sealed class RunRunner
{
    private readonly RunDefinitionRegistry _registry;
    private readonly IRunChoiceProvider _choices;
    private readonly RunEffectProcessor _processor;
    private readonly RunContentRegistry? _content;

    public RunRunner(
        RunDefinitionRegistry registry,
        IRunChoiceProvider choices,
        RunEffectProcessor? processor = null,
        RunContentRegistry? content = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(choices);

        _registry = registry;
        _choices = choices;
        _processor = processor ?? new RunEffectProcessor();
        _content = content;
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
        }

        if (run.Result == RunResult.Ongoing)
            run.SetResult(RunResult.Victory);

        run.AddLog(StandardRunLogTypes.RunEnded, $"Run '{run.Id}' ended: {run.Result}.");
        run.RaiseEvent(new RunEndedRunEvent(run.Result));
        _processor.ResolvePending(run, _registry);
    }
}
