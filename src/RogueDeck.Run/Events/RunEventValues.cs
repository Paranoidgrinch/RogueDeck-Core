namespace RogueDeck.Run;

// Serializable event-field access (S3). The old accessors were Func-backed (not serializable); these store a
// string field KEY and resolve it against RunEventFields at eval time. The reader Funcs live in the registry
// (engine catalog), not in the expression, so the expression is pure data — a relic/schedule condition that
// reads the triggering event can now be serialized. Valid only while a matching event is in context.
public sealed class EventIntValueExpression : IRunExpression<int>
{
    public string FieldKey { get; }
    public EventIntValueExpression(string fieldKey) => FieldKey = fieldKey;
    public int Evaluate(RunEvalContext context) => RunEventFields.ReadInt(FieldKey, context.Event);
}

public sealed class EventBoolValueExpression : IRunExpression<bool>
{
    public string FieldKey { get; }
    public EventBoolValueExpression(string fieldKey) => FieldKey = fieldKey;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadBool(FieldKey, context.Event);
}

// "The node this event happened at carries tag X." Not a key-based field like the ones above: the tag is the
// question, so it is a property of the expression rather than of the registry. Reads any INodeTaggedRunEvent
// (nodeEntered, combatResolved), and — like every other event field — fails loudly when the event in scope is
// not one, so a mis-wired reaction says so instead of silently answering false.
public sealed class EventNodeHasTagExpression : IRunExpression<bool>
{
    public string Tag { get; }
    public EventNodeHasTagExpression(string tag) => Tag = tag;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadNodeHasTag(Tag, context.Event);
}

// "How much of counter X did the hero tally in the fight that just ended." Like the tag question the counter
// name is the question, so it lives on the expression. An unknown counter reads 0 — a fight in which nothing
// was Archived simply archived nothing — but asking outside a combatResolved event still fails loudly.
public sealed class EventCombatCounterExpression : IRunExpression<int>
{
    public string Counter { get; }
    public EventCombatCounterExpression(string counter) => Counter = counter;
    public int Evaluate(RunEvalContext context) => RunEventFields.ReadCombatCounter(Counter, context.Event);
}

// "The thing just bought carries tag X" / "…is of kind X". Same shape as the node-tag question: the tag is the
// question, so it rides on the expression. Valid in a reaction to a shop purchase.
public sealed class EventShopItemHasTagExpression : IRunExpression<bool>
{
    public string Tag { get; }
    public EventShopItemHasTagExpression(string tag) => Tag = tag;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadShopItemHasTag(Tag, context.Event);
}

public sealed class EventShopItemIsKindExpression : IRunExpression<bool>
{
    public string Kind { get; }
    public EventShopItemIsKindExpression(string kind) => Kind = kind;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadShopItemIsKind(Kind, context.Event);
}

// "The reward this event is about carries tag X" / "…is of kind X". Same shape as the node- and shop-tag
// questions. Valid in a reaction to a reward being offered, chosen, or skipped.
public sealed class EventRewardHasTagExpression : IRunExpression<bool>
{
    public string Tag { get; }
    public EventRewardHasTagExpression(string tag) => Tag = tag;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadRewardHasTag(Tag, context.Event);
}

public sealed class EventRewardIsKindExpression : IRunExpression<bool>
{
    public string Kind { get; }
    public EventRewardIsKindExpression(string kind) => Kind = kind;
    public bool Evaluate(RunEvalContext context) => RunEventFields.ReadRewardIsKind(Kind, context.Event);
}

// The catalog of readable event fields, keyed by a stable string. Content may register more; the built-in
// keys cover the standard events. A reader returns null when the event in scope is not the expected type,
// which surfaces as a clear evaluation error.
public static class RunEventFields
{
    public const string CombatDamageTaken = "combat.damageTaken";
    public const string CombatHeroHpRemaining = "combat.heroHpRemaining";
    public const string CombatVictory = "combat.victory";
    public const string CombatDefeat = "combat.defeat";
    public const string HealthNewCurrent = "health.newCurrent";
    public const string HealthMax = "health.max";
    public const string ResourceNewAmount = "resource.newAmount";
    public const string ResourceDelta = "resource.delta";
    public const string CounterNewValue = "counter.newValue";
    public const string CounterDelta = "counter.delta";
    public const string NodeIsCombat = "node.isCombat";
    public const string ShopPricePaid = "shop.pricePaid";
    public const string ShopCurrencyPaid = "shop.currencyPaid";

    private static readonly Dictionary<string, Func<IRunEvent, int?>> IntReaders = new();
    private static readonly Dictionary<string, Func<IRunEvent, bool?>> BoolReaders = new();

    static RunEventFields()
    {
        RegisterInt(CombatDamageTaken, e => e is CombatResolvedRunEvent c ? c.DamageTaken : null);
        RegisterInt(CombatHeroHpRemaining, e => e is CombatResolvedRunEvent c ? c.HeroHpRemaining : null);
        RegisterBool(CombatVictory, e => e is CombatResolvedRunEvent c ? c.Result == RogueDeck.Core.Combat.CombatResult.Victory : null);
        RegisterBool(CombatDefeat, e => e is CombatResolvedRunEvent c ? c.Result == RogueDeck.Core.Combat.CombatResult.Defeat : null);
        RegisterInt(HealthNewCurrent, e => e is RunHealthChangedRunEvent h ? h.NewCurrent : null);
        RegisterInt(HealthMax, e => e is RunHealthChangedRunEvent h ? h.Max : null);
        RegisterInt(ResourceNewAmount, e => e is ResourceChangedRunEvent r ? r.NewAmount : null);
        RegisterInt(ResourceDelta, e => e is ResourceChangedRunEvent r ? r.Delta : null);
        RegisterInt(CounterNewValue, e => e is RunCounterChangedRunEvent c ? c.NewValue : null);
        RegisterInt(CounterDelta, e => e is RunCounterChangedRunEvent c ? c.Delta : null);
        // Gates a nodeEntered reaction to combat nodes — THE data path for "at the start of each combat"
        // relics: When<NodeEnteredRunEvent>(node.isCombat, installNextCombatOpening(rule)). The entered
        // combat node consumes the pending opening itself, so nothing stacks across other node kinds.
        RegisterBool(NodeIsCombat, e => e is NodeEnteredRunEvent n ? n.NodeType == StandardRunIds.CombatNode : null);
        // What the purchase actually cost after the price rules — the number a refund or a punchcard is about.
        RegisterInt(ShopPricePaid, e => e is ShopItemPurchasedRunEvent s ? s.PricePaid : null);
        // …and how much of the currency itself actually left the purse: credit and debt settle a price without
        // any Gold being spent, and the refund relics are about Gold actually paid.
        RegisterInt(ShopCurrencyPaid, e => e is ShopItemPurchasedRunEvent s ? s.CurrencyPaid : null);
    }

    public static void RegisterInt(string key, Func<IRunEvent, int?> reader) => IntReaders[key] = reader;
    public static void RegisterBool(string key, Func<IRunEvent, bool?> reader) => BoolReaders[key] = reader;

    public static int ReadInt(string key, IRunEvent? runEvent)
    {
        if (!IntReaders.TryGetValue(key, out var reader))
            throw new InvalidOperationException($"Unknown int event field '{key}'.");
        return reader(runEvent!) ?? throw NoMatch(key, runEvent);
    }

    public static bool ReadBool(string key, IRunEvent? runEvent)
    {
        if (!BoolReaders.TryGetValue(key, out var reader))
            throw new InvalidOperationException($"Unknown bool event field '{key}'.");
        return reader(runEvent!) ?? throw NoMatch(key, runEvent);
    }

    public static bool ReadNodeHasTag(string tag, IRunEvent? runEvent) =>
        runEvent is INodeTaggedRunEvent node
            ? node.NodeTags.Contains(tag, StringComparer.Ordinal)
            : throw NoMatch("node.hasTag", runEvent);

    public static int ReadCombatCounter(string counter, IRunEvent? runEvent) =>
        runEvent is CombatResolvedRunEvent combat
            ? combat.Counters is { } counters && counters.TryGetValue(counter, out var value) ? value : 0
            : throw NoMatch("combat.counter", runEvent);

    public static bool ReadShopItemHasTag(string tag, IRunEvent? runEvent) =>
        runEvent is ShopItemPurchasedRunEvent purchase
            ? purchase.Tags is { } tags && tags.Contains(tag, StringComparer.Ordinal)
            : throw NoMatch("shop.itemHasTag", runEvent);

    public static bool ReadShopItemIsKind(string kind, IRunEvent? runEvent) =>
        runEvent is ShopItemPurchasedRunEvent purchase
            ? string.Equals(purchase.Kind, kind, StringComparison.Ordinal)
            : throw NoMatch("shop.itemIsKind", runEvent);

    public static bool ReadRewardHasTag(string tag, IRunEvent? runEvent) =>
        runEvent is IRewardTaggedRunEvent reward
            ? reward.RewardTags is { } tags && tags.Contains(tag, StringComparer.Ordinal)
            : throw NoMatch("reward.hasTag", runEvent);

    public static bool ReadRewardIsKind(string kind, IRunEvent? runEvent) =>
        runEvent is IRewardTaggedRunEvent reward
            ? string.Equals(reward.RewardKind, kind, StringComparison.Ordinal)
            : throw NoMatch("reward.isKind", runEvent);

    private static InvalidOperationException NoMatch(string key, IRunEvent? runEvent) =>
        new($"Event field '{key}' was evaluated without a matching event in context (was '{runEvent?.GetType().Name ?? "none"}').");
}

// Named, data-first accessors for the fields of the built-in run events. A designer references a block like
// RunEventValues.CombatDamageTaken; it is a serializable key-based expression. Valid only inside a reaction to
// the matching event (reads RunEvalContext.Event).
public static class RunEventValues
{
    public static IRunExpression<int> CombatHeroHpRemaining { get; } = new EventIntValueExpression(RunEventFields.CombatHeroHpRemaining);
    public static IRunExpression<int> CombatDamageTaken { get; } = new EventIntValueExpression(RunEventFields.CombatDamageTaken);
    public static IRunExpression<bool> CombatWasVictory { get; } = new EventBoolValueExpression(RunEventFields.CombatVictory);
    public static IRunExpression<bool> CombatWasDefeat { get; } = new EventBoolValueExpression(RunEventFields.CombatDefeat);

    public static IRunExpression<int> HealthNewCurrent { get; } = new EventIntValueExpression(RunEventFields.HealthNewCurrent);
    public static IRunExpression<int> HealthMax { get; } = new EventIntValueExpression(RunEventFields.HealthMax);

    public static IRunExpression<int> ResourceNewAmount { get; } = new EventIntValueExpression(RunEventFields.ResourceNewAmount);
    public static IRunExpression<int> ResourceDelta { get; } = new EventIntValueExpression(RunEventFields.ResourceDelta);

    public static IRunExpression<int> CounterNewValue { get; } = new EventIntValueExpression(RunEventFields.CounterNewValue);
    public static IRunExpression<int> CounterDelta { get; } = new EventIntValueExpression(RunEventFields.CounterDelta);

    // The role the node was generated for — MapNodeTags.Elite, .Shop, .Treasure, … Valid in a reaction to
    // nodeEntered or combatResolved; that is how "after you defeat an Elite" is written.
    public static IRunExpression<bool> NodeHasTag(string tag) => new EventNodeHasTagExpression(tag);

    // What the hero tallied inside the fight that just ended — the counter a combat rule kept while it was
    // being played. Valid in a reaction to combatResolved; that is how "5 Gold per Salvage" is written.
    public static IRunExpression<int> CombatCounter(string counter) => new EventCombatCounterExpression(counter);

    // The shop purchase that just happened: what it cost after every price rule, and what it was.
    public static IRunExpression<int> ShopPricePaid { get; } = new EventIntValueExpression(RunEventFields.ShopPricePaid);
    public static IRunExpression<int> ShopCurrencyPaid { get; } = new EventIntValueExpression(RunEventFields.ShopCurrencyPaid);
    public static IRunExpression<bool> ShopItemHasTag(string tag) => new EventShopItemHasTagExpression(tag);
    public static IRunExpression<bool> ShopItemIsKind(string kind) => new EventShopItemIsKindExpression(kind);

    // The reward this reaction is about — offered, chosen, or walked away from.
    public static IRunExpression<bool> RewardHasTag(string tag) => new EventRewardHasTagExpression(tag);
    public static IRunExpression<bool> RewardIsKind(string kind) => new EventRewardIsKindExpression(kind);
}
