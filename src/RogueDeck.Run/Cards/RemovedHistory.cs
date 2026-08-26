using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// What the run remembers about a card it no longer has.
//
// A deck is not the only place a card can be: a game whose events offer to give one back has to know what it
// was — which card, how far it had been improved, and what had been written on it (a permanent inscription is
// an ordinary run card tag). Per-copy MEMORY is deliberately not kept: it is a scratchpad a rule owns for as
// long as the copy exists, and the copy is gone.
//
// A plain record of values, like RunCardSaveData — which is what lets the whole history ride through a save.
public sealed record RemovedCardRecord(
    CardDefinitionId Definition,
    int UpgradeLevel,
    IReadOnlyList<RunCardTagId> Tags,
    IReadOnlyList<string> Composition)
{
    public static RemovedCardRecord Of(RunCardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new RemovedCardRecord(
            card.DefinitionId, card.UpgradeLevel, [.. card.Tags], [.. card.Composition]);
    }
}

// Give back a card the run once removed: the player picks one of the remembered ones (or the first, headless),
// it is recreated in the deck with the state it was removed with, and the entry is struck out — a card can be
// recovered once. `ExtraUpgrades` and `Tags` are what the recovering EVENT adds on top ("restored, one further
// improvement, and its true name"). Nothing happens when the history is empty.
public sealed record RestoreRemovedCardRunEffect(
    int Count = 1,
    string Purpose = "choose a card to recover",
    int ExtraUpgrades = 0,
    IReadOnlyList<string>? Tags = null) : IRunEffectRequest;

public sealed class RestoreRemovedCardRunEffectHandler : RunEffectHandler<RestoreRemovedCardRunEffect>
{
    protected override void Resolve(
        RunState run, RunDefinitionRegistry registry, RestoreRemovedCardRunEffect request)
    {
        if (request.Count < 1 || run.RemovedCards.Count == 0)
            return;

        var candidates = run.RemovedCards.ToArray();
        var chosen = run.EntityChooser is { } chooser
            ? chooser.ChooseEntities(candidates, request.Count, request.Purpose)
            : candidates.Take(request.Count).ToArray();

        foreach (var record in chosen)
        {
            if (!run.ForgetRemovedCard(record))
                continue;

            var card = run.AddDeckCard(
                record.Definition, record.Composition.Count > 0 ? record.Composition : null);
            if (record.UpgradeLevel + request.ExtraUpgrades > 0)
                card.Upgrade(record.UpgradeLevel + request.ExtraUpgrades);
            foreach (var tag in record.Tags)
                card.AddTag(tag);
            foreach (var tag in request.Tags ?? [])
                card.AddTag(new RunCardTagId(tag));

            run.AddLog(StandardRunLogTypes.CardAdded,
                $"Recovered card '{record.Definition}' ({card.Id}) from the removed history.");
            run.RaiseEvent(new CardAddedToDeckRunEvent(card.Id, card.DefinitionId));
        }
    }
}

// How many cards the run could still give back — what "if the history has entries" asks.
public sealed class RemovedCardCountExpression : IRunExpression<int>
{
    public int Evaluate(RunEvalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Run.RemovedCards.Count;
    }
}
