using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Core.Combat;

// JSON (de)serialization for the combat effect tree — the counterpart of the run engine's RunJson, but the
// combat tree is GENERIC over TContext (CardPlayContext / EnemyActionContext / TriggeredEffectContext). So the
// kind registry maps a stable string to the OPEN generic type definition (e.g. AddExpression<>), and a
// per-context converter closes it on the concrete context. Every node serializes as {"kind":"…","value":{…}}
// and recurses through the converters registered in the options. Func-backed nodes (ContextValueExpression,
// predicate outcome expressions) have no kind and fail with a clear NotSupportedException.
//
// C-S1 registers the arithmetic/logic value + condition expressions (those that only nest expressions).
// Selector-reading expressions, nodes and target selectors land in later slices.
public sealed class CombatJsonRegistry
{
    private readonly Dictionary<string, Type> _byKind = new();
    private readonly Dictionary<Type, string> _byType = new();

    // `type` may be an open generic definition (closed on the context at read time) or a concrete type.
    public CombatJsonRegistry Register(string kind, Type type)
    {
        if (!_byKind.TryAdd(kind, type))
            throw new InvalidOperationException($"Combat json kind '{kind}' is already registered.");
        if (!_byType.TryAdd(type, kind))
            throw new InvalidOperationException($"Type '{type.Name}' is already registered for combat json.");
        return this;
    }

    public string KindOf(Type runtimeType)
    {
        var key = runtimeType.IsGenericType ? runtimeType.GetGenericTypeDefinition() : runtimeType;
        return _byType.TryGetValue(key, out var kind)
            ? kind
            : throw new NotSupportedException(
                $"'{runtimeType.Name}' has no combat serialization kind — it is an escape / non-data node.");
    }

    public Type Resolve(string kind, Type contextType)
    {
        if (!_byKind.TryGetValue(kind, out var type))
            throw new JsonException($"Unknown combat json kind '{kind}'.");
        return type.IsGenericTypeDefinition ? type.MakeGenericType(contextType) : type;
    }
}

// One converter per closed polymorphic base (e.g. ICombatExpression<CardPlayContext,int>). Writes the
// discriminator envelope; reads it back and deserializes the inner value as the registered type closed on the
// context, whose own polymorphic properties recurse through their converters.
public sealed class CombatPolymorphicConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    private readonly CombatJsonRegistry _registry;
    private readonly Type _contextType;

    public CombatPolymorphicConverter(CombatJsonRegistry registry, Type contextType)
    {
        _registry = registry;
        _contextType = contextType;
    }

    public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = root.GetProperty("kind").GetString()
            ?? throw new JsonException("Combat json node is missing its 'kind'.");
        var type = _registry.Resolve(kind, _contextType);
        return (TBase)JsonSerializer.Deserialize(root.GetProperty("value").GetRawText(), type, options)!;
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteString("kind", _registry.KindOf(type));
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value, type, options);
        writer.WriteEndObject();
    }
}

public static class CombatJson
{
    public static CombatJsonRegistry DefaultRegistry()
    {
        var registry = new CombatJsonRegistry();
        RegisterExpressions(registry);
        RegisterSelectors(registry);
        RegisterNodes(registry);
        return registry;
    }

    // Options for authoring/serializing effect trees of a specific context (e.g. CardPlayContext for cards).
    public static JsonSerializerOptions CreateOptions<TContext>(CombatJsonRegistry? registry = null)
        where TContext : class
    {
        registry ??= DefaultRegistry();
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        AddSelectorConverter(options, registry);
        AddContextConverters<TContext>(options, registry);
        return options;
    }

    // Target selectors are context-independent (non-generic); add this converter once per options.
    public static void AddSelectorConverter(JsonSerializerOptions options, CombatJsonRegistry registry) =>
        options.Converters.Add(new CombatPolymorphicConverter<ICombatantTargetSelector>(registry, typeof(object)));

    // Adds the expression + node converters for one context to an existing options — lets a host (e.g. RunJson)
    // serialize effect programs of several contexts (CardPlayContext + EnemyActionContext) in the same document.
    public static void AddContextConverters<TContext>(JsonSerializerOptions options, CombatJsonRegistry registry)
        where TContext : class
    {
        options.Converters.Add(new CombatPolymorphicConverter<ICombatExpression<TContext, int>>(registry, typeof(TContext)));
        options.Converters.Add(new CombatPolymorphicConverter<ICombatExpression<TContext, bool>>(registry, typeof(TContext)));
        options.Converters.Add(new CombatPolymorphicConverter<ICardInstanceExpression<TContext>>(registry, typeof(TContext)));
        options.Converters.Add(new CombatPolymorphicConverter<IEffectNode<TContext>>(registry, typeof(TContext)));
    }

    public static string ToJson<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, options);

    public static T FromJson<T>(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(json, options)
        ?? throw new JsonException($"Deserialized null for '{typeof(T).Name}'.");

    private static void RegisterExpressions(CombatJsonRegistry r)
    {
        // int value expressions that only nest expressions (no selector / Func).
        r.Register("const", typeof(ConstantExpression<>))
         .Register("abs", typeof(AbsExpression<>))
         .Register("negate", typeof(NegateExpression<>))
         .Register("sign", typeof(SignExpression<>))
         .Register("add", typeof(AddExpression<>))
         .Register("subtract", typeof(SubtractExpression<>))
         .Register("multiply", typeof(MultiplyExpression<>))
         .Register("min", typeof(MinExpression<>))
         .Register("max", typeof(MaxExpression<>))
         .Register("divide", typeof(DivideExpression<>))
         .Register("remainder", typeof(RemainderExpression<>))
         .Register("clamp", typeof(ClampExpression<>))
         .Register("roundNumber", typeof(RoundNumberExpression<>))
         .Register("turnNumber", typeof(TurnNumberExpression<>))
         .Register("iterationIndex", typeof(IterationIndexExpression<>))
         // Combat-state value reads (hold a target selector).
         .Register("combatantCurrentHealth", typeof(CombatantCurrentHealthExpression<>))
         .Register("combatantMaxHealth", typeof(CombatantMaxHealthExpression<>))
         .Register("combatantMissingHealth", typeof(CombatantMissingHealthExpression<>))
         .Register("combatantHealthPercentage", typeof(CombatantHealthPercentageExpression<>))
         .Register("combatantCurrentResource", typeof(CombatantCurrentResourceExpression<>))
         .Register("combatantCounter", typeof(CombatantCounterExpression<>))
         .Register("combatantMaxResource", typeof(CombatantMaxResourceExpression<>))
         .Register("combatantMissingResource", typeof(CombatantMissingResourceExpression<>))
         .Register("combatantDefensivePool", typeof(CombatantDefensivePoolExpression<>))
         .Register("combatantZoneCardCount", typeof(CombatantZoneCardCountExpression<>))
         .Register("combatantStatusStacks", typeof(CombatantStatusStacksExpression<>))
         .Register("combatantStatusStacksFromSource", typeof(CombatantStatusStacksFromSourceExpression<>))
         .Register("combatantStatusDuration", typeof(CombatantStatusDurationExpression<>))
         .Register("combatantStatusCharges", typeof(CombatantStatusChargesExpression<>))
         .Register("combatantStacksByPolarity", typeof(CombatantStacksByPolarityExpression<>))
         // Positional reads (P3) — inert (0) in a flat combat.
         .Register("combatantCoord", typeof(CombatantCoordExpression<>))
         .Register("gridDistance", typeof(GridDistanceExpression<>))
         // Aggregates over a selector (1i Step B siblings of gridDistance — these two were missing, so a card
         // authored with them in the Studio could not be saved: ToJson threw mid-edit).
         .Register("countTargets", typeof(CountTargetsExpression<>))
         .Register("sumOverTargets", typeof(SumOverTargetsExpression<>))
         .Register("cardsPlayedThisTurn", typeof(CardsPlayedThisTurnExpression<>))
         .Register("cardsPlayedThisTurnWithTag", typeof(CardsPlayedThisTurnWithTagExpression<>))
         .Register("cardsPlayedLastTurn", typeof(CardsPlayedLastTurnExpression<>))
         .Register("cardsPlayedLastTurnWithTag", typeof(CardsPlayedLastTurnWithTagExpression<>))
         .Register("damageDealtThisTurn", typeof(DamageDealtThisTurnExpression<>))
         .Register("resourceGainedThisTurn", typeof(ResourceGainedThisTurnExpression<>))
         .Register("cardCost", typeof(CardCostExpression<>))
         .Register("cardInstanceMarkCounter", typeof(CardInstanceMarkCounterExpression<>))
         .Register("iterationTargetStatusStacks", typeof(IterationTargetStatusStacksExpression<>))
         .Register("eventAmount", typeof(EventAmountExpression<>));

        // bool condition expressions.
        r.Register("compare", typeof(ComparisonExpression<>))
         .Register("and", typeof(AndExpression<>))
         .Register("or", typeof(OrExpression<>))
         .Register("not", typeof(NotExpression<>))
         .Register("targetHasStatus", typeof(TargetHasStatusExpression<>))
         .Register("targetIsAlive", typeof(TargetIsAliveExpression<>))
         .Register("targetDowned", typeof(TargetDownedExpression<>))
         .Register("targetExists", typeof(TargetExistsExpression<>))
         .Register("cardInstanceHasMark", typeof(CardInstanceHasMarkExpression<>))
         .Register("firstCardPlayedHasTag", typeof(FirstCardPlayedThisTurnHasTagExpression<>))
         .Register("iterationTargetHasStatus", typeof(IterationTargetHasStatusExpression<>));

        // Card-instance expressions (resolve a specific card instance to act on).
        r.Register("cardInstance.explicit", typeof(ExplicitCardInstanceExpression<>))
         .Register("cardInstance.createdOutcome", typeof(CreateCardOutcomeExpression<>))
         .Register("cardInstance.played", typeof(PlayedCardInstanceExpression<>))
         .Register("cardInstance.triggerEvent", typeof(TriggerEventCardInstanceExpression<>))
         .Register("cardInstance.inZone", typeof(CardInZoneExpression<>))
         .Register("cardInstance.inOwnerZone", typeof(CardInOwnerZoneExpression<>))
         .Register("cardInstance.firstMarkedInOwnerZone", typeof(FirstMarkedCardInOwnerZoneExpression<>))
         .Register("cardInstance.chosenInZone", typeof(ChosenCardInZoneExpression<>))
         .Register("cardInstance.randomInZone", typeof(RandomCardInZoneExpression<>))
         .Register("cardInstance.randomInOwnerZone", typeof(RandomCardInOwnerZoneExpression<>))
         .Register("cardInstance.iterated", typeof(IteratedCardExpression<>));
    }

    // Combatant target selectors (context-independent, non-generic). Registered as concrete types.
    private static void RegisterSelectors(CombatJsonRegistry r)
    {
        r.Register("sel.source", typeof(SourceCombatantTargetSelector))
         .Register("sel.sourceIncludingDowned", typeof(SourceIncludingDownedCombatantTargetSelector))
         .Register("sel.eventTarget", typeof(EventTargetCombatantTargetSelector))
         .Register("sel.allAllies", typeof(AllAlliesOfSourceCombatantTargetSelector))
         .Register("sel.allEnemies", typeof(AllEnemiesOfSourceCombatantTargetSelector))
         .Register("sel.allDamagedAllies", typeof(AllDamagedAlliesOfSourceCombatantTargetSelector))
         .Register("sel.iterationTarget", typeof(IterationTargetCombatantTargetSelector))
         .Register("sel.lowestHealthEnemy", typeof(LowestHealthEnemyOfSourceCombatantTargetSelector))
         .Register("sel.highestHealthEnemy", typeof(HighestHealthEnemyOfSourceCombatantTargetSelector))
         .Register("sel.lowestHealthAlly", typeof(LowestHealthAllyOfSourceCombatantTargetSelector))
         .Register("sel.highestHealthAlly", typeof(HighestHealthAllyOfSourceCombatantTargetSelector))
         .Register("sel.allCombatants", typeof(AllCombatantsTargetSelector))
         .Register("sel.withStatus", typeof(CombatantsWithStatusTargetSelector))
         .Register("sel.alliesWithStatus", typeof(AllAlliesOfSourceWithStatusCombatantTargetSelector))
         .Register("sel.enemiesWithStatus", typeof(AllEnemiesOfSourceWithStatusCombatantTargetSelector))
         .Register("sel.union", typeof(UnionCombatantTargetSelector))
         // Positional (2D-grid) selectors (P1) — additive; resolve empty in a flat (position-less) combat.
         .Register("sel.adjacent", typeof(AdjacentToSourceCombatantTargetSelector))
         .Register("sel.sameColumn", typeof(SameColumnAsSourceCombatantTargetSelector))
         .Register("sel.sameRow", typeof(SameRowAsSourceCombatantTargetSelector))
         .Register("sel.allInColumn", typeof(AllInSourceColumnCombatantTargetSelector))
         .Register("sel.allInRow", typeof(AllInSourceRowCombatantTargetSelector))
         .Register("sel.frontmostEnemy", typeof(FrontmostEnemyOfSourceCombatantTargetSelector))
         .Register("sel.backmostEnemy", typeof(BackmostEnemyOfSourceCombatantTargetSelector))
         .Register("sel.nearestEnemy", typeof(NearestEnemyOfSourceCombatantTargetSelector))
         .Register("sel.opposingInColumn", typeof(OpposingInColumnCombatantTargetSelector));
    }

    // Leaf native operation nodes (they hold a selector + expressions, no child nodes) plus the composite/
    // control-flow nodes. A node's EffectResultKey (a plain record over a fixed outcome type) round-trips too;
    // only the Func-backed result CONSUMER expressions (PreviousOutcome*) remain non-serializable escapes.
    private static void RegisterNodes(CombatJsonRegistry r)
    {
        r.Register("node.dealDamage", typeof(DealDamageNode<>))
         .Register("node.heal", typeof(HealNode<>))
         .Register("node.modifyMaxHealth", typeof(ModifyMaxHealthNode<>))
         .Register("node.setHealth", typeof(SetHealthNode<>))
         .Register("node.gainBlock", typeof(GainBlockNode<>))
         .Register("node.modifyDefensivePool", typeof(ModifyDefensivePoolNode<>))
         .Register("node.gainResource", typeof(GainResourceNode<>))
         .Register("node.loseResource", typeof(LoseResourceNode<>))
         .Register("node.refillResource", typeof(RefillResourceNode<>))
         .Register("node.modifyResource", typeof(ModifyResourceNode<>))
         .Register("node.applyStatus", typeof(ApplyStatusNode<>))
         .Register("node.removeStatus", typeof(RemoveStatusNode<>))
         .Register("node.removeStatusesByPolarity", typeof(RemoveStatusesByPolarityNode<>))
         .Register("node.removeSelectedStatus", typeof(RemoveSelectedStatusNode<>))
         .Register("node.modifySelectedStatusStacks", typeof(ModifySelectedStatusStacksNode<>))
         .Register("node.modifySelectedResource", typeof(ModifySelectedResourceNode<>))
         .Register("node.stealSelectedStatus", typeof(StealSelectedStatusNode<>))
         .Register("node.setCombatantCounter", typeof(SetCombatantCounterNode<>))
         .Register("node.markCardInstance", typeof(MarkCardInstanceNode<>))
         .Register("node.setCardInstanceMarkCounter", typeof(SetCardInstanceMarkCounterNode<>))
         .Register("node.modifyStatusStacks", typeof(ModifyStatusStacksNode<>))
         .Register("node.modifyStatusDuration", typeof(ModifyStatusDurationNode<>))
         .Register("node.modifyStatusCharges", typeof(ModifyStatusChargesNode<>))
         .Register("node.drawCards", typeof(DrawCardsNode<>))
         .Register("node.moveAllCardsFromZone", typeof(MoveAllCardsFromZoneNode<>))
         .Register("node.createCardInstance", typeof(CreateCardInstanceNode<>))
         .Register("node.summonCombatant", typeof(SummonCombatantNode<>))
         // Positional movement nodes (P2).
         .Register("node.moveCombatant", typeof(MoveCombatantNode<>))
         .Register("node.swapPositions", typeof(SwapPositionsNode<>))
         .Register("node.setCombatantLifecycleState", typeof(SetCombatantLifecycleStateNode<>))
         .Register("node.changeCombatantTeam", typeof(ChangeCombatantTeamNode<>))
         .Register("node.setCombatResult", typeof(SetCombatResultNode<>))
         .Register("node.removeTemporaryRule", typeof(RemoveTemporaryRuleNode<>))
         // Card-instance nodes (act on a resolved card instance).
         .Register("node.moveCardToZone", typeof(MoveCardToZoneNode<>))
         .Register("node.transformCard", typeof(TransformCardNode<>))
         .Register("node.createCardCopy", typeof(CreateCardCopyNode<>))
         .Register("node.replayCardProgram", typeof(ReplayCardProgramNode<>))
         .Register("node.playCard", typeof(PlayCardNode<>))
         // Composite / control-flow nodes (nest child nodes).
         .Register("node.sequence", typeof(SequenceEffectNode<>))
         .Register("node.conditional", typeof(ConditionalEffectNode<>))
         .Register("node.forEachTarget", typeof(ForEachTargetEffectNode<>))
         .Register("node.forEachCardInZone", typeof(ForEachCardInZoneNode<>))
         .Register("node.repeat", typeof(RepeatEffectNode<>))
         .Register("node.repeatUntil", typeof(RepeatUntilEffectNode<>))
         .Register("node.randomTargets", typeof(RandomTargetSelectionNode<>))
         .Register("node.noOp", typeof(NoOpEffectNode<>));
    }
}
