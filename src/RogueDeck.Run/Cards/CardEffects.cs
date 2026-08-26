using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// Effects that mutate deck cards through a selector — the run treating cards as persistent entities (idea doc
// §10.2). Each resolves its selector against the run's SelectorContext (so ChooseByPlayer works during
// resolution via the run's chooser), snapshots the targets, then mutates each and raises a per-card event.
// Snapshotting first means removing/transforming while iterating is safe.

// Apply a block of effect templates to each selected card, with that card in scope (R3 data ForEach). Each
// template materialises at foreach time against the per-card context, so "this card" templates target the
// right copy and event-independent templates work too. The data-first alternative to ExpandRunEffect's lambda.
public sealed record ForEachCardRunEffect(
    IRunSelector<RunCardInstance> Selector, IReadOnlyList<IRunEffectTemplate> Templates) : IRunEffectRequest;

public sealed class ForEachCardRunEffectHandler : RunEffectHandler<ForEachCardRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, ForEachCardRunEffect request)
    {
        var scope = run.SelectorContext;
        foreach (var card in request.Selector.Select(scope).ToArray())
        {
            var cardContext = scope.WithCard(card);
            foreach (var template in request.Templates)
                run.EnqueueEffect(template.Build(cardContext));
        }
    }
}

public sealed record RemoveCardsRunEffect(IRunSelector<RunCardInstance> Selector) : IRunEffectRequest;

public sealed class RemoveCardsRunEffectHandler : RunEffectHandler<RemoveCardsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RemoveCardsRunEffect request)
    {
        foreach (var card in request.Selector.Select(run.SelectorContext).ToArray())
        {
            if (!run.RemoveDeckCard(card.Id))
                continue;
            // The run remembers what it no longer has, so content that offers a card BACK knows what it was.
            run.RememberRemovedCard(RemovedCardRecord.Of(card));
            run.AddLog(StandardRunLogTypes.CardRemoved, $"Removed card '{card.DefinitionId}' ({card.Id}).");
            run.RaiseEvent(new CardRemovedFromDeckRunEvent(card.Id, card.DefinitionId));
        }
    }
}

public sealed record UpgradeCardsRunEffect(IRunSelector<RunCardInstance> Selector, int Levels = 1)
    : IRunEffectRequest;

public sealed class UpgradeCardsRunEffectHandler : RunEffectHandler<UpgradeCardsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, UpgradeCardsRunEffect request)
    {
        foreach (var card in request.Selector.Select(run.SelectorContext).ToArray())
        {
            card.Upgrade(request.Levels);
            run.AddLog(StandardRunLogTypes.CardUpgraded, $"Upgraded card ({card.Id}) to +{card.UpgradeLevel}.");
            run.RaiseEvent(new CardUpgradedRunEvent(card.Id, card.UpgradeLevel));
        }
    }
}

// Add or remove a run tag on the selected cards; raises only for cards whose tag set actually changed.
public sealed record TagCardsRunEffect(IRunSelector<RunCardInstance> Selector, RunCardTagId Tag, bool Add = true)
    : IRunEffectRequest;

public sealed class TagCardsRunEffectHandler : RunEffectHandler<TagCardsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, TagCardsRunEffect request)
    {
        foreach (var card in request.Selector.Select(run.SelectorContext).ToArray())
        {
            var changed = request.Add ? card.AddTag(request.Tag) : card.RemoveTag(request.Tag);
            if (!changed)
                continue;
            run.AddLog(StandardRunLogTypes.CardTagChanged, $"Card ({card.Id}) tag '{request.Tag}' -> {request.Add}.");
            run.RaiseEvent(new CardTagChangedRunEvent(card.Id, request.Tag, request.Add));
        }
    }
}

public sealed record SetCardMemoryRunEffect(IRunSelector<RunCardInstance> Selector, string Key, int Value)
    : IRunEffectRequest;

public sealed class SetCardMemoryRunEffectHandler : RunEffectHandler<SetCardMemoryRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, SetCardMemoryRunEffect request)
    {
        foreach (var card in request.Selector.Select(run.SelectorContext).ToArray())
            card.SetMemory(request.Key, request.Value);
    }
}

// Add a fresh copy of each selected card to the deck — "duplicate a card" as data. The copy carries the
// original's definition, upgrade level, run tags and shred composition (it IS that card again), but is a
// new instance with its own id.
public sealed record DuplicateCardsRunEffect(IRunSelector<RunCardInstance> Selector) : IRunEffectRequest;

public sealed class DuplicateCardsRunEffectHandler : RunEffectHandler<DuplicateCardsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, DuplicateCardsRunEffect request)
    {
        foreach (var card in request.Selector.Select(run.SelectorContext).ToArray())
        {
            var copy = run.AddDeckCardTo(run.ActiveMember, card.DefinitionId,
                card.Composition.Count > 0 ? card.Composition : null);
            if (card.UpgradeLevel > 0)
                copy.Upgrade(card.UpgradeLevel);
            foreach (var tag in card.Tags)
                copy.AddTag(tag);
            run.AddLog(StandardRunLogTypes.CardAdded,
                $"Duplicated card '{card.DefinitionId}' ({card.Id}) -> ({copy.Id}).");
            run.RaiseEvent(new CardAddedToDeckRunEvent(copy.Id, copy.DefinitionId));
        }
    }
}

// Transform each selected card into a fresh copy whose kind is drawn from a pool (a fixed kind = a
// single-entry pool). The old copy is removed and a new instance is added, so per-copy state does not carry
// over — a transform makes a new card.
public sealed record TransformCardsRunEffect(
    IRunSelector<RunCardInstance> Selector, RunPool<CardDefinitionId> Pool) : IRunEffectRequest;

public sealed class TransformCardsRunEffectHandler : RunEffectHandler<TransformCardsRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, TransformCardsRunEffect request)
    {
        foreach (var card in request.Selector.Select(run.SelectorContext).ToArray())
        {
            if (!run.RemoveDeckCard(card.Id))
                continue;
            var newKind = request.Pool.Draw(run);
            var created = run.AddDeckCard(newKind);
            run.AddLog(StandardRunLogTypes.CardTransformed,
                $"Transformed card '{card.DefinitionId}' ({card.Id}) -> '{newKind}' ({created.Id}).");
            run.RaiseEvent(new CardTransformedRunEvent(card.Id, created.Id, newKind));
        }
    }
}
