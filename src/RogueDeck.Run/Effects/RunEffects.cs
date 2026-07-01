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
        if (next == previous)
            return; // no actual change (e.g. a computed +0) — raise nothing, like the flag/counter effects

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

// Change max HP by a delta (min 1). Gaining max also heals by that much (a full-heal-on-gain convention);
// losing max caps current down. Raises a distinct event so "on max HP changed" triggers can react.
public sealed record ChangeMaxHealthRunEffect(int Delta) : IRunEffectRequest;

public sealed class ChangeMaxHealthRunEffectHandler : RunEffectHandler<ChangeMaxHealthRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ChangeMaxHealthRunEffect request)
    {
        var previousMax = run.Health.Max;
        var newMax = Math.Max(1, previousMax + request.Delta);
        if (newMax == previousMax)
            return;

        run.Health.SetMax(newMax);
        if (request.Delta > 0)
            run.Health.SetCurrent(Math.Min(newMax, run.Health.Current + request.Delta));

        run.AddLog(StandardRunLogTypes.MaxHealthChanged, $"Max HP {previousMax} -> {newMax}.");
        run.RaiseEvent(new RunMaxHealthChangedRunEvent(previousMax, newMax));
    }
}

public sealed record RemoveRelicRunEffect(RelicId Relic) : IRunEffectRequest;

public sealed class RemoveRelicRunEffectHandler : RunEffectHandler<RemoveRelicRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RemoveRelicRunEffect request)
    {
        if (!run.RemoveRelic(request.Relic))
            return;
        run.AddLog(StandardRunLogTypes.RelicRemoved, $"Removed relic '{request.Relic}'.");
        run.RaiseEvent(new RelicRemovedRunEvent(request.Relic));
    }
}

// Grant a relic by id, resolving its definition from the run's content catalog — the serializable,
// data-first way to grant a relic (AddRelicRunEffect embeds a RelicInstance and is an escape).
public sealed record AddRelicByIdRunEffect(RelicId Relic) : IRunEffectRequest;

public sealed class AddRelicByIdRunEffectHandler : RunEffectHandler<AddRelicByIdRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddRelicByIdRunEffect request)
    {
        if (run.Content is null)
            throw new InvalidOperationException(
                $"Cannot grant relic '{request.Relic}' by id: the run has no content catalog.");

        var definition = run.Content.GetRelic(request.Relic);
        run.AddRelic(new RelicInstance(definition));
        run.AddLog(StandardRunLogTypes.RelicAcquired, $"Acquired relic '{definition.Id}' (by id).");
        run.RaiseEvent(new RelicAcquiredRunEvent(definition.Id));
    }
}

// Disable a relic for the next `Combats` resolved combats, then it re-enables itself. Reuses the scheduler:
// disabling installs a one-shot "after N combats -> enable" consequence. A disabled relic neither reacts nor
// contributes to combat.
public sealed record DisableRelicRunEffect(RelicId Relic, int Combats) : IRunEffectRequest;

public sealed class DisableRelicRunEffectHandler : RunEffectHandler<DisableRelicRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, DisableRelicRunEffect request)
    {
        var relic = run.FindRelic(request.Relic);
        if (relic is null || request.Combats < 1)
            return;

        relic.SetEnabled(false);
        run.AddLog(StandardRunLogTypes.RelicDisabled, $"Disabled relic '{request.Relic}' for {request.Combats} combats.");
        run.RaiseEvent(new RelicDisabledRunEvent(request.Relic, request.Combats));

        var program = RunSchedule.AfterCombats(
            run.NextProgramId($"reenable-{request.Relic}"), request.Combats, new EnableRelicRunEffect(request.Relic));
        run.EnqueueEffect(new InstallRunProgramRunEffect(program));
    }
}

public sealed record EnableRelicRunEffect(RelicId Relic) : IRunEffectRequest;

public sealed class EnableRelicRunEffectHandler : RunEffectHandler<EnableRelicRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, EnableRelicRunEffect request)
    {
        var relic = run.FindRelic(request.Relic);
        if (relic is null || relic.Enabled)
            return;

        relic.SetEnabled(true);
        run.AddLog(StandardRunLogTypes.RelicEnabled, $"Re-enabled relic '{request.Relic}'.");
        run.RaiseEvent(new RelicEnabledRunEvent(request.Relic));
    }
}

public sealed record AddCardToDeckRunEffect(CardDefinitionId Card) : IRunEffectRequest;

public sealed class AddCardToDeckRunEffectHandler : RunEffectHandler<AddCardToDeckRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddCardToDeckRunEffect request)
    {
        var card = run.AddDeckCard(request.Card);
        run.AddLog(StandardRunLogTypes.CardAdded, $"Added card '{request.Card}' ({card.Id}).");
        run.RaiseEvent(new CardAddedToDeckRunEvent(card.Id, card.DefinitionId));
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
