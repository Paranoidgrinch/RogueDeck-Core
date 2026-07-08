namespace RogueDeck.Run;

// Branching-map mutation effects (B5): serializable run effects that reshape the map mid-run — open a hidden path,
// collapse a bridge, splice in a node. Each mutates RunState.Map (rebuilt immutably) and, on a real change, logs
// it and raises MapChangedRunEvent so relics/UI can react. The graph walk reads Map fresh each step, so a change
// takes effect on the next fork. No-ops (a node/edge already present, or absent on removal) are silent.
public sealed record AddMapNodeRunEffect(Node Node) : IRunEffectRequest;

public sealed class AddMapNodeRunEffectHandler : RunEffectHandler<AddMapNodeRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddMapNodeRunEffect request)
    {
        if (run.AddMapNode(request.Node))
            MapMutation.Announce(run, $"Added map node '{request.Node.Id.Value}' ({request.Node.Type}).");
    }
}

public sealed record RemoveMapNodeRunEffect(NodeId NodeId) : IRunEffectRequest;

public sealed class RemoveMapNodeRunEffectHandler : RunEffectHandler<RemoveMapNodeRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RemoveMapNodeRunEffect request)
    {
        if (run.RemoveMapNode(request.NodeId))
            MapMutation.Announce(run, $"Removed map node '{request.NodeId.Value}'.");
    }
}

public sealed record AddMapEdgeRunEffect(NodeId From, NodeId To) : IRunEffectRequest;

public sealed class AddMapEdgeRunEffectHandler : RunEffectHandler<AddMapEdgeRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddMapEdgeRunEffect request)
    {
        if (run.AddMapEdge(request.From, request.To))
            MapMutation.Announce(run, $"Added map edge '{request.From.Value}' -> '{request.To.Value}'.");
    }
}

public sealed record RemoveMapEdgeRunEffect(NodeId From, NodeId To) : IRunEffectRequest;

public sealed class RemoveMapEdgeRunEffectHandler : RunEffectHandler<RemoveMapEdgeRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RemoveMapEdgeRunEffect request)
    {
        if (run.RemoveMapEdge(request.From, request.To))
            MapMutation.Announce(run, $"Removed map edge '{request.From.Value}' -> '{request.To.Value}'.");
    }
}

internal static class MapMutation
{
    public static void Announce(RunState run, string message)
    {
        run.AddLog(StandardRunLogTypes.MapChanged, message);
        run.RaiseEvent(new MapChangedRunEvent());
    }
}
