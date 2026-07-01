namespace RogueDeck.Run;

// One-shot items the run carries and spends (idea doc §10.3 consumables). A consumable is data: a kind id
// plus the effects applied when it is used. Using it enqueues those effects and removes the copy. "Use" is
// normally player-driven at runtime; gaining one is a reward/event effect.
public sealed class RunConsumable
{
    public ConsumableInstanceId Id { get; }
    public ConsumableId DefinitionId { get; }
    public IReadOnlyList<IRunEffectRequest> UseEffects { get; }

    public RunConsumable(
        ConsumableInstanceId id, ConsumableId definitionId, IReadOnlyList<IRunEffectRequest> useEffects)
    {
        ArgumentNullException.ThrowIfNull(useEffects);
        Id = id;
        DefinitionId = definitionId;
        UseEffects = useEffects;
    }
}

public sealed record AddConsumableRunEffect(ConsumableId Definition, IReadOnlyList<IRunEffectRequest> UseEffects)
    : IRunEffectRequest;

public sealed class AddConsumableRunEffectHandler : RunEffectHandler<AddConsumableRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddConsumableRunEffect request)
    {
        var consumable = run.AddConsumable(request.Definition, request.UseEffects);
        run.AddLog(StandardRunLogTypes.ConsumableGained, $"Gained consumable '{request.Definition}' ({consumable.Id}).");
        run.RaiseEvent(new ConsumableGainedRunEvent(consumable.Id, consumable.DefinitionId));
    }
}

// Use a consumable by instance id: enqueue its use effects, then remove it. No-op if it is not held.
public sealed record UseConsumableRunEffect(ConsumableInstanceId Instance) : IRunEffectRequest;

public sealed class UseConsumableRunEffectHandler : RunEffectHandler<UseConsumableRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, UseConsumableRunEffect request)
    {
        var consumable = run.FindConsumable(request.Instance);
        if (consumable is null)
            return;

        foreach (var effect in consumable.UseEffects)
            run.EnqueueEffect(effect);
        run.RemoveConsumable(consumable.Id);

        run.AddLog(StandardRunLogTypes.ConsumableUsed, $"Used consumable '{consumable.DefinitionId}' ({consumable.Id}).");
        run.RaiseEvent(new ConsumableUsedRunEvent(consumable.Id, consumable.DefinitionId));
    }
}

// How many consumables the run holds (for conditions like "inventory is full").
public sealed class ConsumableCountExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context) => context.Run.Consumables.Count;
}
