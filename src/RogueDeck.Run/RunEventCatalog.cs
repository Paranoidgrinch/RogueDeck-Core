namespace RogueDeck.Run;

// One authorable run event: the stable string key used in serialization + UI, the event CLR type, a
// human-readable label, and a one-sentence help text for editors. This is the single source of truth for "which
// run events a relic reaction (or any future data trigger) may hook" — the JSON converter
// (TriggeredRunEffectJsonConverter) and the Studio editors both read it, so adding an event is one entry here,
// nowhere else.
public sealed record RunEventKind(string Key, Type EventType, string Label, string Description = "");

public static class RunEventCatalog
{
    // Curated reading order (lifecycle → rewards → cards → relics → economy/state → programs → consumables) so the
    // editor dropdown lists the common triggers first.
    public static readonly IReadOnlyList<RunEventKind> All =
    [
        new("runStarted", typeof(RunStartedRunEvent), "the run starts",
            "Fires once when a new run begins."),
        new("nodeEntered", typeof(NodeEnteredRunEvent), "a node is entered",
            "Fires when the player enters a map node (a fight, a story event, a shop…)."),
        new("nodeChosen", typeof(NodeChosenRunEvent), "a next node is chosen",
            "Fires when the player picks which map node to travel to next."),
        new("mapChanged", typeof(MapChangedRunEvent), "the map topology changes",
            "Fires when the map itself changes mid-run (nodes or paths added or removed)."),
        new("combatResolved", typeof(CombatResolvedRunEvent), "a combat resolves",
            "Fires when a fight ends, win or lose."),
        new("eventChoiceMade", typeof(EventChoiceMadeRunEvent), "an event choice is made",
            "Fires when the player picks an option in a story event."),
        new("shopItemPurchased", typeof(ShopItemPurchasedRunEvent), "a shop item is purchased",
            "Fires when the player buys something in a shop."),
        new("shopRerolled", typeof(ShopRerolledRunEvent), "a shop's stock is rerolled",
            "Fires when the player pays to redraw a shop's stock."),
        new("runEnded", typeof(RunEndedRunEvent), "the run ends",
            "Fires once when the run ends, in victory or defeat."),

        new("rewardOffered", typeof(RewardOfferedRunEvent), "a reward is offered",
            "Fires when a reward choice is put in front of the player."),
        new("rewardChosen", typeof(RewardChosenRunEvent), "a reward is chosen",
            "Fires when the player picks one of the offered rewards."),
        new("rewardSkipped", typeof(RewardSkippedRunEvent), "a reward is walked away from",
            "Fires when the player is offered a reward and takes none of it."),
        new("rewardGranted", typeof(RewardGrantedRunEvent), "a reward is granted",
            "Fires when a reward's contents are actually handed over."),

        new("cardAddedToDeck", typeof(CardAddedToDeckRunEvent), "a card is added to the deck",
            "Fires when any effect puts a new card into the deck."),
        new("cardRemovedFromDeck", typeof(CardRemovedFromDeckRunEvent), "a card is removed from the deck",
            "Fires when a card is taken out of the deck for good."),
        new("cardUpgraded", typeof(CardUpgradedRunEvent), "a card is upgraded",
            "Fires when a deck card is upgraded."),
        new("cardTransformed", typeof(CardTransformedRunEvent), "a card is transformed",
            "Fires when a deck card is turned into a different card."),
        new("cardTagChanged", typeof(CardTagChangedRunEvent), "a card tag changes",
            "Fires when a tag is added to or removed from a deck card."),

        new("relicAcquired", typeof(RelicAcquiredRunEvent), "a relic is acquired",
            "Fires when the player gains a relic."),
        new("relicRemoved", typeof(RelicRemovedRunEvent), "a relic is removed",
            "Fires when the player loses a relic."),
        new("relicDisabled", typeof(RelicDisabledRunEvent), "a relic is disabled",
            "Fires when a relic is switched off (e.g. for a number of fights)."),
        new("relicEnabled", typeof(RelicEnabledRunEvent), "a relic is enabled",
            "Fires when a disabled relic switches back on."),

        new("resourceChanged", typeof(ResourceChangedRunEvent), "a resource changes",
            "Fires when a run resource (gold or a custom resource) goes up or down."),
        new("runHealthChanged", typeof(RunHealthChangedRunEvent), "health changes",
            "Fires when the hero's run health goes up or down."),
        new("runMaxHealthChanged", typeof(RunMaxHealthChangedRunEvent), "max health changes",
            "Fires when the hero's maximum health changes."),
        new("runFlagChanged", typeof(RunFlagChangedRunEvent), "a flag changes",
            "Fires when a named story flag is set or cleared."),
        new("runCounterChanged", typeof(RunCounterChangedRunEvent), "a counter changes",
            "Fires when a named run counter changes value."),

        new("runProgramInstalled", typeof(RunProgramInstalledRunEvent), "a program is installed",
            "Fires when a triggered program (e.g. a relic's reactions) is installed on the run."),
        new("runProgramUninstalled", typeof(RunProgramUninstalledRunEvent), "a program is uninstalled",
            "Fires when a triggered program is removed from the run."),

        new("consumableGained", typeof(ConsumableGainedRunEvent), "a consumable is gained",
            "Fires when the player gains a consumable."),
        new("consumableUsed", typeof(ConsumableUsedRunEvent), "a consumable is used",
            "Fires when the player uses a consumable."),

        new("shredGained", typeof(ShredEngine.ShredGainedRunEvent), "a shred is gained",
            "Fires when the player gains a card part (a shred) for the workbench."),
        new("workbenchCrafted", typeof(ShredEngine.WorkbenchCraftedRunEvent), "a card is crafted",
            "Fires when the player builds a card at a workbench (a raw composition or a recipe)."),
    ];

    private static readonly IReadOnlyDictionary<string, RunEventKind> ByKeyMap =
        All.ToDictionary(k => k.Key, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<Type, RunEventKind> ByTypeMap =
        All.ToDictionary(k => k.EventType);

    public static IEnumerable<string> Keys => All.Select(k => k.Key);

    public static bool TryByKey(string key, out RunEventKind kind) => ByKeyMap.TryGetValue(key, out kind!);

    public static bool TryByType(Type type, out RunEventKind kind) => ByTypeMap.TryGetValue(type, out kind!);

    // Reverse-map an event type to its key; unknown types fall back to nodeEntered (the neutral default).
    public static string KeyFor(Type eventType) =>
        ByTypeMap.TryGetValue(eventType, out var kind) ? kind.Key : "nodeEntered";

    public static Type? TypeFor(string key) => ByKeyMap.TryGetValue(key, out var kind) ? kind.EventType : null;

    public static string LabelFor(Type eventType) =>
        ByTypeMap.TryGetValue(eventType, out var kind) ? kind.Label : eventType.Name;

    // Build a declarative run program for the given event key by closing DataTriggeredRunEffect<TEvent> over the
    // catalog type — the same reflective construction the JSON converter uses, so UI-built and deserialized
    // programs are identical. Unknown keys fall back to nodeEntered.
    public static ITriggeredRunEffectDefinition Build(
        string key, IRunExpression<bool>? condition, IReadOnlyList<IRunEffectTemplate> templates)
    {
        var eventType = TypeFor(key) ?? typeof(NodeEnteredRunEvent);
        var closed = typeof(DataTriggeredRunEffect<>).MakeGenericType(eventType);
        return (ITriggeredRunEffectDefinition)Activator.CreateInstance(closed, condition, templates)!;
    }
}
