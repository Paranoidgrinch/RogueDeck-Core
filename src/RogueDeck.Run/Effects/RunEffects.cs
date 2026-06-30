using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The built-in run effects. Each one mutates RunState and raises the matching IRunEvent, so relics observe a
// uniform stream regardless of which node or effect produced the change. Registered by StandardRunPackage.

public sealed record ChangeResourceRunEffect(RunResourceId Resource, int Delta) : IRunEffectRequest;

public sealed class ChangeResourceRunEffectHandler : RunEffectHandler<ChangeResourceRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ChangeResourceRunEffect request)
    {
        var previous = run.GetResource(request.Resource);
        var next = Math.Max(0, previous + request.Delta);
        run.SetResource(request.Resource, next);

        run.AddLog(StandardRunLogTypes.ResourceChanged,
            $"{request.Resource} {previous} -> {next} ({request.Delta:+0;-0;0}).");
        run.RaiseEvent(new ResourceChangedRunEvent(request.Resource, previous, next, next - previous));
    }
}

public sealed record ApplyRunDamageRunEffect(int Amount) : IRunEffectRequest;

public sealed class ApplyRunDamageRunEffectHandler : RunEffectHandler<ApplyRunDamageRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ApplyRunDamageRunEffect request)
    {
        if (request.Amount <= 0)
            return;

        var previous = run.Health.Current;
        run.Health.SetCurrent(Math.Max(0, previous - request.Amount));
        run.RaiseEvent(new RunHealthChangedRunEvent(previous, run.Health.Current, run.Health.Max));
    }
}

public sealed record HealRunEffect(int Amount) : IRunEffectRequest;

public sealed class HealRunEffectHandler : RunEffectHandler<HealRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, HealRunEffect request)
    {
        if (request.Amount <= 0)
            return;

        var previous = run.Health.Current;
        run.Health.SetCurrent(Math.Min(run.Health.Max, previous + request.Amount));
        run.RaiseEvent(new RunHealthChangedRunEvent(previous, run.Health.Current, run.Health.Max));
    }
}

public sealed record AddCardToDeckRunEffect(CardDefinitionId Card) : IRunEffectRequest;

public sealed class AddCardToDeckRunEffectHandler : RunEffectHandler<AddCardToDeckRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddCardToDeckRunEffect request)
    {
        run.AddDeckCard(request.Card);
    }
}

public sealed record AddRelicRunEffect(RelicInstance Relic) : IRunEffectRequest;

public sealed class AddRelicRunEffectHandler : RunEffectHandler<AddRelicRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddRelicRunEffect request)
    {
        run.AddRelic(request.Relic);
        run.AddLog(StandardRunLogTypes.RelicAcquired, $"Acquired relic '{request.Relic.Id}'.");
        run.RaiseEvent(new RelicAcquiredRunEvent(request.Relic.Id));
    }
}

// A reward is just a named bundle of other effects — the run author decides what it contains.
public sealed record GrantRewardRunEffect(RewardId Reward, IReadOnlyList<IRunEffectRequest> Effects)
    : IRunEffectRequest;

public sealed class GrantRewardRunEffectHandler : RunEffectHandler<GrantRewardRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, GrantRewardRunEffect request)
    {
        foreach (var effect in request.Effects)
            run.EnqueueEffect(effect);

        run.AddLog(StandardRunLogTypes.RewardGranted, $"Granted reward '{request.Reward}'.");
        run.RaiseEvent(new RewardGrantedRunEvent(request.Reward));
    }
}
