namespace RogueDeck.Run;

// Sample non-combat events, authored ENTIRELY on EventScriptBuilder with only the standard run effects. They
// have no engine privilege whatsoever — they are content, exactly like a concrete card is content over the
// combat effect system. A content pack would write its own events the same way; the long-term goal is for
// this authoring surface to reach the composability of combat effect programs.
public static class StandardEvents
{
    // A rest site: heal a flat amount, or move on.
    public static EventScript Rest(int healAmount)
    {
        return new EventScriptBuilder("rest")
            .Situation("rest", "event.rest", situation => situation
                .Choice("rest", choice => choice
                    .TextKey("event.rest.heal")
                    .Heal(healAmount))
                .Choice("leave", choice => choice.TextKey("event.rest.leave")))
            .Build();
    }

    // A campfire / rest site with the genre's two staple options: REST (heal a flat amount) or SMITH (upgrade one
    // deck card the player chooses, up to upgradeMaxLevel). Picking either ends the node. Built entirely on the
    // standard run effects (Heal + UpgradeCards over a player-chosen upgradable-card selector) — no engine privilege.
    // Smith is offered even with nothing upgradable left; it then resolves to no upgrade (an availability gate is a
    // follow-up). Content packs place this as an EventNode carrying the script, like any other event.
    public static EventScript RestSite(int healAmount, int upgradeMaxLevel = 1)
    {
        return new EventScriptBuilder("restSite")
            .Situation("restSite", "event.restSite", situation => situation
                .Choice("rest", choice => choice
                    .TextKey("event.restSite.rest")
                    .Heal(healAmount))
                .Choice("smith", choice => choice
                    .TextKey("event.restSite.smith")
                    .UpgradeCards(
                        RunSelectors.DeckCards.Upgradable(upgradeMaxLevel).ChooseByPlayer(1, "smith: upgrade a card"))))
            .Build();
    }

    // A treasure: take a reward bundle (the contents are whatever the author passes).
    public static EventScript Treasure(RewardId reward, params IRunEffectRequest[] contents)
    {
        return new EventScriptBuilder("treasure")
            .Situation("treasure", "event.treasure", situation => situation
                .Choice("take", choice => choice
                    .Effect(new GrantRewardRunEffect(reward, contents))))
            .Build();
    }

    // What a shop sells: spend `Price` of `Currency` to apply `Payload` (add a card, a relic, heal, …).
    public sealed record ShopItem(string Id, RunResourceId Currency, int Price, IRunEffectRequest Payload);

    // A shop: each affordable item is a choice that spends the currency, applies the payload, and loops back
    // so several purchases are possible; "leave" exits. Because EventNodeResolver flushes effects between
    // situations, the affordability requirement re-evaluates against the spent-down balance each loop.
    public static EventScript Shop(IReadOnlyList<ShopItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new EventScriptBuilder("shop")
            .Situation("shop", "event.shop", situation =>
            {
                foreach (var item in items)
                    situation.Choice(item.Id, choice => choice
                        .PayResource(item.Currency, item.Price)
                        .Effect(item.Payload)
                        .Then("shop"));

                situation.Choice("leave", choice => choice.TextKey("event.shop.leave"));
            })
            .Build();
    }
}
