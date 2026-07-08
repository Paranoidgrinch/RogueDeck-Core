namespace RogueDeck.Run;

// One authorable run event: the stable string key used in serialization + UI, the event CLR type, and a
// human-readable label for editors. This is the single source of truth for "which run events a relic reaction
// (or any future data trigger) may hook" — the JSON converter (TriggeredRunEffectJsonConverter) and the Studio
// editors both read it, so adding an event is one entry here, nowhere else.
public sealed record RunEventKind(string Key, Type EventType, string Label);

public static class RunEventCatalog
{
    // Curated reading order (lifecycle → rewards → cards → relics → economy/state → programs → consumables) so the
    // editor dropdown lists the common triggers first.
    public static readonly IReadOnlyList<RunEventKind> All =
    [
        new("runStarted", typeof(RunStartedRunEvent), "the run starts"),
        new("nodeEntered", typeof(NodeEnteredRunEvent), "a node is entered"),
        new("nodeChosen", typeof(NodeChosenRunEvent), "a next node is chosen"),
        new("mapChanged", typeof(MapChangedRunEvent), "the map topology changes"),
        new("combatResolved", typeof(CombatResolvedRunEvent), "a combat resolves"),
        new("eventChoiceMade", typeof(EventChoiceMadeRunEvent), "an event choice is made"),
        new("runEnded", typeof(RunEndedRunEvent), "the run ends"),

        new("rewardOffered", typeof(RewardOfferedRunEvent), "a reward is offered"),
        new("rewardChosen", typeof(RewardChosenRunEvent), "a reward is chosen"),
        new("rewardGranted", typeof(RewardGrantedRunEvent), "a reward is granted"),

        new("cardAddedToDeck", typeof(CardAddedToDeckRunEvent), "a card is added to the deck"),
        new("cardRemovedFromDeck", typeof(CardRemovedFromDeckRunEvent), "a card is removed from the deck"),
        new("cardUpgraded", typeof(CardUpgradedRunEvent), "a card is upgraded"),
        new("cardTransformed", typeof(CardTransformedRunEvent), "a card is transformed"),
        new("cardTagChanged", typeof(CardTagChangedRunEvent), "a card tag changes"),

        new("relicAcquired", typeof(RelicAcquiredRunEvent), "a relic is acquired"),
        new("relicRemoved", typeof(RelicRemovedRunEvent), "a relic is removed"),
        new("relicDisabled", typeof(RelicDisabledRunEvent), "a relic is disabled"),
        new("relicEnabled", typeof(RelicEnabledRunEvent), "a relic is enabled"),

        new("resourceChanged", typeof(ResourceChangedRunEvent), "a resource changes"),
        new("runHealthChanged", typeof(RunHealthChangedRunEvent), "health changes"),
        new("runMaxHealthChanged", typeof(RunMaxHealthChangedRunEvent), "max health changes"),
        new("runFlagChanged", typeof(RunFlagChangedRunEvent), "a flag changes"),
        new("runCounterChanged", typeof(RunCounterChangedRunEvent), "a counter changes"),

        new("runProgramInstalled", typeof(RunProgramInstalledRunEvent), "a program is installed"),
        new("runProgramUninstalled", typeof(RunProgramUninstalledRunEvent), "a program is uninstalled"),

        new("consumableGained", typeof(ConsumableGainedRunEvent), "a consumable is gained"),
        new("consumableUsed", typeof(ConsumableUsedRunEvent), "a consumable is used"),
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
