namespace RogueDeck.Run;

// Effects that reach into the shop the player is standing in. Both are no-ops outside a shop — a relic reacting
// to a purchase is always inside one, and a rule that fires elsewhere should not blow up a run — but they say so
// in the log, because a relic that never seems to do anything is worth being able to find.

// "Add one more Normal Relic to THIS shop, right now." The permanent half of that promise is a ShopStockGrant on
// the relic; this is the half that lands in the shop where it was bought.
public sealed record AddShopStockRunEffect(
    string GroupId,
    int Count = 1,
    IReadOnlyList<string>? Tags = null) : IRunEffectRequest;

public sealed class AddShopStockRunEffectHandler : RunEffectHandler<AddShopStockRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, AddShopStockRunEffect request)
    {
        if (run.ActiveShopShelf is not { } shelf)
        {
            run.AddLog(StandardRunLogTypes.ShopPurchase,
                $"Extra stock for '{request.GroupId}' was asked for outside a shop; nothing to add to.");
            return;
        }

        shelf.Grant(request.GroupId, request.Count, request.Tags);
        run.AddLog(StandardRunLogTypes.ShopPurchase,
            $"Shop shelf '{request.GroupId}' gained {request.Count} more slot(s).");
    }
}

// "Replace all unsold cards with new cards." One shelf is restocked and nothing else in the shop is touched —
// what was already bought stays bought, so the shelf comes back the size it actually is.
public sealed record RestockShopStockRunEffect(string GroupId) : IRunEffectRequest;

public sealed class RestockShopStockRunEffectHandler : RunEffectHandler<RestockShopStockRunEffect>
{
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, RestockShopStockRunEffect request)
    {
        if (run.ActiveShopShelf is not { } shelf)
        {
            run.AddLog(StandardRunLogTypes.ShopRerolled,
                $"Shelf '{request.GroupId}' was asked to restock outside a shop; nothing to restock.");
            return;
        }

        shelf.Restock(request.GroupId);
        run.AddLog(StandardRunLogTypes.ShopRerolled, $"Shop shelf '{request.GroupId}' restocked.");
    }
}
