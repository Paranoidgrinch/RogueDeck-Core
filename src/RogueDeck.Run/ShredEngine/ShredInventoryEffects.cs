using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.ShredEngine;

// The shred inventory's run-effect vocabulary: gaining/removing card parts and adding a COMPOSED card to
// the deck. Mirrors the consumable effects — rewards, events and the workbench all speak these requests,
// so shreds flow through every existing grant channel (RewardOffer bundles, event choices, shops) with no
// changes to those systems.

// A shred is gained (or `Count` of them). Fires ShredGainedRunEvent so relics/programs can react.
public sealed record AddShredRunEffect(string ShredId, int Count = 1) : IRunEffectRequest;

public sealed class AddShredRunEffectHandler : RunEffectHandler<AddShredRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddShredRunEffect request)
    {
        run.AddShreds(request.ShredId, request.Count);
        run.AddLog(ShredEngineLogTypes.ShredGained,
            $"Gained {request.Count}x shred '{request.ShredId}' ({run.GetShredCount(request.ShredId)} held).");
        run.RaiseEvent(new ShredGainedRunEvent(request.ShredId, request.Count));
    }
}

// Shreds are removed (consumed by crafting, stolen by an event…). A no-op when fewer are held — removal
// is best-effort like other run-side removals, and the workbench checks availability before consuming.
public sealed record RemoveShredRunEffect(string ShredId, int Count = 1) : IRunEffectRequest;

public sealed class RemoveShredRunEffectHandler : RunEffectHandler<RemoveShredRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RemoveShredRunEffect request)
    {
        if (!run.TryRemoveShreds(request.ShredId, request.Count))
            return;
        run.AddLog(ShredEngineLogTypes.ShredRemoved,
            $"Removed {request.Count}x shred '{request.ShredId}'.");
    }
}

// A composed card joins the active member's deck: the instance carries the ordered shred list, and its
// definition id derives from it (ShredCardSynthesizer.DerivedId) — the per-fight injection re-synthesizes
// the actual definition, so nothing but the list persists.
public sealed record AddComposedCardRunEffect(IReadOnlyList<string> Composition) : IRunEffectRequest;

public sealed class AddComposedCardRunEffectHandler : RunEffectHandler<AddComposedCardRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddComposedCardRunEffect request)
    {
        if (request.Composition.Count == 0)
            return;
        var definitionId = new CardDefinitionId(ShredCardSynthesizer.DerivedId(request.Composition));
        var instance = run.AddDeckCardTo(run.ActiveMember, definitionId, request.Composition);
        run.AddLog(ShredEngineLogTypes.CardComposed,
            $"Composed card '{definitionId.value}' added to the deck ({instance.Id}).");
        run.RaiseEvent(new CardAddedToDeckRunEvent(instance.Id, definitionId));
    }
}

// Fired when shreds are gained, so content can react ("after finding 3 parts, …").
public sealed record ShredGainedRunEvent(string ShredId, int Count) : IRunEvent;

public static class ShredEngineLogTypes
{
    public const string ShredGained = "shred.gained";
    public const string ShredRemoved = "shred.removed";
    public const string CardComposed = "shred.card-composed";
    public const string WorkbenchCrafted = "shred.workbench-crafted";
}
