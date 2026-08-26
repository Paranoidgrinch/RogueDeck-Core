using System.Text.Json;
using System.Text.Json.Serialization;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run;

// JSON (de)serialization for the run content tree. The tree is polymorphic (IRunExpression, IRunEffectRequest,
// IRunSelector, …), so a kind registry maps each concrete data type to a stable string discriminator, and a
// polymorphic converter wraps every node as {"kind":"…","value":{…}}. Nested nodes recurse through the same
// converters. Escapes (Func-backed nodes) are not registered and fail with a clear NotSupportedException —
// data content uses data nodes. Combat content (cards/EffectPrograms) is referenced by id, never serialized.
//
// S1 registers the expression family (values + conditions). Later slices register effects, selectors, rewards
// (and redesign the Func-backed event accessors to be serializable).
public sealed class RunJsonRegistry
{
    private readonly Dictionary<string, Type> _byKind = new();
    private readonly Dictionary<Type, string> _byType = new();

    public RunJsonRegistry Register(string kind, Type type)
    {
        if (!_byKind.TryAdd(kind, type))
            throw new InvalidOperationException($"Run json kind '{kind}' is already registered.");
        if (!_byType.TryAdd(type, kind))
            throw new InvalidOperationException($"Type '{type.Name}' is already registered for run json.");
        return this;
    }

    public string KindOf(Type type) =>
        _byType.TryGetValue(type, out var kind)
            ? kind
            : throw new NotSupportedException(
                $"'{type.Name}' has no serialization kind — it is an escape / non-data node and cannot be serialized.");

    public Type TypeOf(string kind) =>
        _byKind.TryGetValue(kind, out var type)
            ? type
            : throw new JsonException($"Unknown run json kind '{kind}'.");
}

// One converter per polymorphic base (a closed interface). Writes the discriminator envelope; reads it back
// and deserializes the inner value as the registered concrete type (whose own polymorphic properties recurse).
public sealed class PolymorphicRunJsonConverter<TBase> : JsonConverter<TBase>
    where TBase : class
{
    private readonly RunJsonRegistry _registry;
    public PolymorphicRunJsonConverter(RunJsonRegistry registry) => _registry = registry;

    public override TBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var kind = root.GetProperty("kind").GetString()
            ?? throw new JsonException("Run json node is missing its 'kind'.");
        var type = _registry.TypeOf(kind);
        var value = root.GetProperty("value").GetRawText();
        return (TBase)JsonSerializer.Deserialize(value, type, options)!;
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteString("kind", _registry.KindOf(type));
        writer.WritePropertyName("value");
        JsonSerializer.Serialize(writer, value, type, options); // concrete type -> no envelope, plain fields
        writer.WriteEndObject();
    }
}

public static class RunJson
{
    // The default registry with the built-in serializable kinds registered.
    public static RunJsonRegistry DefaultRegistry()
    {
        var registry = new RunJsonRegistry();
        RegisterExpressions(registry);
        RegisterSelectors(registry);
        RegisterTemplates(registry);
        RegisterEffects(registry);
        RegisterRewards(registry);
        RegisterMeta(registry);
        RegisterNodes(registry);
        RegisterIntentConditions(registry);
        return registry;
    }

    public static JsonSerializerOptions CreateOptions(RunJsonRegistry? registry = null)
    {
        registry ??= DefaultRegistry();
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        // A blueprint carries combat effect programs, so it inherits their duplicate-children problem too.
        CombatJson.WriteChildrenOnlyWhereTheyAreRead(options);
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunExpression<int>>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunExpression<bool>>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunSelector<RunCardInstance>>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunEffectTemplate>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunEffectRequest>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRewardSource>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRewardRule>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<MetaEffect>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<EnemyIntentCondition>(registry));
        options.Converters.Add(new PolymorphicRunJsonConverter<IRunNodePayload>(registry));
        options.Converters.Add(new NodeJsonConverter());
        options.Converters.Add(new EventScriptJsonConverter());
        options.Converters.Add(new TriggeredRunEffectJsonConverter());
        options.Converters.Add(new RelicCombatRuleJsonConverter());

        // Combat effect programs carried by the blueprint's cards / enemy actions (both contexts).
        var combatRegistry = CombatJson.DefaultRegistry();
        CombatJson.AddSelectorConverter(options, combatRegistry);
        CombatJson.AddContextConverters<CardPlayContext>(options, combatRegistry);
        CombatJson.AddContextConverters<EnemyActionContext>(options, combatRegistry);
        CombatJson.AddContextConverters<CardLifecycleContext>(options, combatRegistry); // per-card lifecycle programs

        return options;
    }

    public static string ToJson<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, options);

    public static T FromJson<T>(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<T>(json, options)
        ?? throw new JsonException($"Deserialized null for '{typeof(T).Name}'.");

    // The blueprint-DOCUMENT entry point: upgrades the raw JSON to the current schema first (older versions
    // migrate, newer ones fail clearly — see RunBlueprintSchema), then deserializes. Every consumer of a stored
    // or imported blueprint should come through here; plain FromJson is for documents known to be current.
    public static RunBlueprint BlueprintFromJson(string json, JsonSerializerOptions options) =>
        FromJson<RunBlueprint>(RunBlueprintSchema.Upgrade(json), options);

    // State-conditional enemy-AI intent conditions (#1). Data predicates over the live CombatState, carried by
    // an encounter's EnemyIntentRules and evaluated at intent-selection time.
    private static void RegisterIntentConditions(RunJsonRegistry r) =>
        r.Register("intent.healthPercent", typeof(EnemyHealthPercentCondition))
         .Register("intent.round", typeof(RoundCondition))
         .Register("intent.selfHasStatus", typeof(SelfHasStatusCondition))
         .Register("intent.opponentHasStatus", typeof(OpponentHasStatusCondition))
         .Register("intent.selfHasCounter", typeof(SelfHasCounterCondition))
         .Register("intent.selfResource", typeof(SelfResourceCondition))
         .Register("intent.opponentCardsPlayed", typeof(OpponentCardsPlayedCondition))
         .Register("intent.allOf", typeof(AllOfCondition))
         .Register("intent.anyOf", typeof(AnyOfCondition))
         .Register("intent.not", typeof(NotCondition));

    private static void RegisterExpressions(RunJsonRegistry r)
    {
        // Value expressions (IRunExpression<int>).
        r.Register("const", typeof(RunConstantExpression))
         .Register("resource", typeof(ResourceValueExpression))
         .Register("currentHealth", typeof(CurrentHealthExpression))
         .Register("maxHealth", typeof(MaxHealthExpression))
         .Register("missingHealth", typeof(MissingHealthExpression))
         .Register("deckSize", typeof(DeckSizeExpression))
         .Register("relicCount", typeof(RelicCountExpression))
         .Register("consumableCount", typeof(ConsumableCountExpression))
         .Register("counter", typeof(CounterValueExpression))
         .Register("add", typeof(AddExpression))
         .Register("subtract", typeof(SubtractExpression))
         .Register("multiply", typeof(MultiplyExpression))
         .Register("min", typeof(MinExpression))
         .Register("max", typeof(MaxExpression))
         .Register("divide", typeof(DivideExpression))
         .Register("abs", typeof(AbsExpression))
         .Register("negate", typeof(NegateExpression))
         .Register("clamp", typeof(ClampExpression))
         .Register("randomRange", typeof(RandomRangeExpression))
         .Register("card.upgradeLevel", typeof(CardUpgradeLevelExpression))
         .Register("card.memory", typeof(CardMemoryExpression))
         .Register("act", typeof(ActNumberExpression))
         .Register("event.int", typeof(EventIntValueExpression))
         .Register("event.combatCounter", typeof(EventCombatCounterExpression));

        // Condition expressions (IRunExpression<bool>).
        r.Register("bool", typeof(RunConstantBoolExpression))
         .Register("compare", typeof(RunComparisonExpression))
         .Register("and", typeof(AndExpression))
         .Register("or", typeof(OrExpression))
         .Register("not", typeof(NotExpression))
         .Register("flag", typeof(FlagSetExpression))
         .Register("card.hasTag", typeof(CardHasTagExpression))
         .Register("card.isKind", typeof(CardIsKindExpression))
         .Register("event.bool", typeof(EventBoolValueExpression))
         .Register("event.nodeHasTag", typeof(EventNodeHasTagExpression))
         .Register("event.shopItemHasTag", typeof(EventShopItemHasTagExpression))
         .Register("event.shopItemIsKind", typeof(EventShopItemIsKindExpression))
         .Register("event.rewardHasTag", typeof(EventRewardHasTagExpression))
         .Register("event.rewardIsKind", typeof(EventRewardIsKindExpression))
         .Register("event.resourceIs", typeof(EventResourceIsExpression))
         .Register("inShop", typeof(InShopExpression))
         .Register("actFlag", typeof(ActFlagSetExpression));
    }

    // Card selectors. Only the card closing is registered (no effect consumes a relic selector yet). The
    // Func-backed WhereSelector is an escape.
    private static void RegisterSelectors(RunJsonRegistry r)
    {
        r.Register("sel.deckCards", typeof(DeckCardsSelector))
         .Register("sel.instance", typeof(InstanceSelector))
         .Register("sel.lastAddedCard", typeof(LastAddedCardSelector))
         .Register("sel.matching", typeof(MatchingCardSelector))
         .Register("sel.take", typeof(TakeSelector<RunCardInstance>))
         .Register("sel.random", typeof(RandomSelector<RunCardInstance>))
         .Register("sel.choose", typeof(ChooseSelector<RunCardInstance>));
    }

    // Effect templates (materialise a concrete effect at dispatch). CustomEffectTemplate is a Func escape.
    private static void RegisterTemplates(RunJsonRegistry r)
    {
        r.Register("tpl.literal", typeof(LiteralEffectTemplate))
         .Register("tpl.gainResource", typeof(GainResourceTemplate))
         .Register("tpl.heal", typeof(HealTemplate))
         .Register("tpl.damage", typeof(DamageTemplate))
         .Register("tpl.upgradeThisCard", typeof(UpgradeThisCardTemplate))
         .Register("tpl.tagThisCard", typeof(TagThisCardTemplate))
         .Register("tpl.removeThisCard", typeof(RemoveThisCardTemplate))
         .Register("tpl.setThisCardMemory", typeof(SetThisCardMemoryTemplate))
         .Register("tpl.transformThisCard", typeof(TransformThisCardTemplate));
    }

    // Data effects. Nested effect lists, expressions, selectors and pools recurse through their converters.
    // Effects that embed code or content objects (AddRelic/InstallProgram/ExpandRunEffect/ForEachCard/
    // AddRewardModifier/AddCombatModifier) are escapes and are not registered.
    private static void RegisterEffects(RunJsonRegistry r)
    {
        r.Register("fx.changeResource", typeof(ChangeResourceRunEffect))
         .Register("fx.damage", typeof(ApplyRunDamageRunEffect))
         .Register("fx.heal", typeof(HealRunEffect))
         .Register("fx.changeMaxHealth", typeof(ChangeMaxHealthRunEffect))
         .Register("fx.addCard", typeof(AddCardToDeckRunEffect))
         .Register("fx.addRelicById", typeof(AddRelicByIdRunEffect))
         .Register("fx.removeRelic", typeof(RemoveRelicRunEffect))
         .Register("fx.disableRelic", typeof(DisableRelicRunEffect))
         .Register("fx.enableRelic", typeof(EnableRelicRunEffect))
         .Register("fx.addMapNode", typeof(AddMapNodeRunEffect))
         .Register("fx.removeMapNode", typeof(RemoveMapNodeRunEffect))
         .Register("fx.addMapEdge", typeof(AddMapEdgeRunEffect))
         .Register("fx.removeMapEdge", typeof(RemoveMapEdgeRunEffect))
         .Register("fx.setFlag", typeof(SetFlagRunEffect))
         .Register("fx.incrementCounter", typeof(IncrementCounterRunEffect))
         .Register("fx.setCounter", typeof(SetCounterRunEffect))
         .Register("fx.computedCounter", typeof(ComputedCounterRunEffect))
         .Register("fx.grantUnrestrictedStep", typeof(GrantUnrestrictedStepRunEffect))
         .Register("fx.setActFlag", typeof(SetActFlagRunEffect))
         .Register("fx.addShopStock", typeof(AddShopStockRunEffect))
         .Register("fx.restockShopStock", typeof(RestockShopStockRunEffect))
         .Register("fx.uninstallProgram", typeof(UninstallRunProgramRunEffect))
         .Register("fx.useConsumable", typeof(UseConsumableRunEffect))
         .Register("fx.computedResource", typeof(ComputedResourceRunEffect))
         .Register("fx.computedHeal", typeof(ComputedHealRunEffect))
         .Register("fx.computedDamage", typeof(ComputedDamageRunEffect))
         .Register("fx.conditional", typeof(ConditionalRunEffect))
         .Register("fx.repeat", typeof(RepeatRunEffect))
         .Register("fx.drawEffects", typeof(DrawEffectsRunEffect))
         .Register("fx.drawManyEffects", typeof(DrawManyEffectsRunEffect))
         .Register("fx.grantReward", typeof(GrantRewardRunEffect))
         .Register("fx.offerReward", typeof(OfferRewardRunEffect))
         .Register("fx.addConsumable", typeof(AddConsumableRunEffect))
         .Register("fx.addConsumableById", typeof(AddConsumableByIdRunEffect))
         .Register("fx.installNextCombatOpening", typeof(InstallNextCombatOpeningRunEffect))
         .Register("fx.removeCards", typeof(RemoveCardsRunEffect))
         .Register("fx.duplicateCards", typeof(DuplicateCardsRunEffect))
         .Register("fx.upgradeCards", typeof(UpgradeCardsRunEffect))
         .Register("fx.tagCards", typeof(TagCardsRunEffect))
         .Register("fx.setCardMemory", typeof(SetCardMemoryRunEffect))
         .Register("fx.transformCards", typeof(TransformCardsRunEffect))
         .Register("fx.forEachCard", typeof(ForEachCardRunEffect))
         .Register("fx.addShred", typeof(ShredEngine.AddShredRunEffect))
         .Register("fx.removeShred", typeof(ShredEngine.RemoveShredRunEffect))
         .Register("fx.addComposedCard", typeof(ShredEngine.AddComposedCardRunEffect));
    }

    // Reward sources and the standing reward rules a relic carries. The Func-backed DelegateRewardSource and
    // DelegateRunRewardModifier are escapes and are not registered.
    private static void RegisterRewards(RunJsonRegistry r)
    {
        r.Register("reward.addOffer", typeof(AddRewardOfferRule))
         .Register("reward.drawMore", typeof(DrawMoreOffersRule))
         .Register("reward.appendGrant", typeof(AppendOfferGrantRule));

        r.Register("reward.fixed", typeof(FixedRewardSource))
         .Register("reward.pool", typeof(PoolRewardSource));
    }

    // Meta-progression effects (the run-end rule vocabulary). The rules + effects are content; the engine only
    // serializes the closed effect set.
    private static void RegisterMeta(RunJsonRegistry r)
    {
        r.Register("meta.setFlag", typeof(SetMetaFlag))
         .Register("meta.addCounter", typeof(AddMetaCounter))
         .Register("meta.promoteResource", typeof(PromoteRunResource))
         .Register("meta.promoteFlag", typeof(PromoteRunFlag));
    }

    // Data node payloads (references). Inline EventScript / Func combat payloads are escapes.
    private static void RegisterNodes(RunJsonRegistry r)
    {
        r.Register("node.event", typeof(EventRef))
         .Register("node.encounter", typeof(EncounterRef))
         .Register("node.shop", typeof(ShopDefinition))
         .Register("node.shopRef", typeof(ShopRef))
         .Register("node.workbench", typeof(ShredEngine.WorkbenchDefinition))
         .Register("node.workbenchRef", typeof(ShredEngine.WorkbenchRef));
    }
}

// A map node: id + type + a data payload (IRunNodePayload). Non-data payloads (inline EventScript, Func combat
// payloads) are not serializable and fault clearly.
public sealed class NodeJsonConverter : JsonConverter<Node>
{
    public override Node Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var id = new NodeId(root.GetProperty("id").GetString()!);
        var type = new NodeType(root.GetProperty("type").GetString()!);
        var payload = JsonSerializer.Deserialize<IRunNodePayload>(root.GetProperty("payload").GetRawText(), options)!;
        var tags = root.TryGetProperty("tags", out var tagged)
            ? JsonSerializer.Deserialize<List<string>>(tagged.GetRawText(), options)
            : null;
        return new Node(id, type, payload, tags);
    }

    public override void Write(Utf8JsonWriter writer, Node value, JsonSerializerOptions options)
    {
        if (value.Payload is not IRunNodePayload payload)
            throw new NotSupportedException(
                $"Node '{value.Id}' has a non-serializable payload '{value.Payload.GetType().Name}' (use EventRef/EncounterRef).");
        writer.WriteStartObject();
        writer.WriteString("id", value.Id.Value);
        writer.WriteString("type", value.Type.Value);
        writer.WritePropertyName("payload");
        JsonSerializer.Serialize(writer, payload, options);
        // Written only when the node actually carries tags, so every document authored before node tags
        // existed still round-trips byte-identically.
        if (value.Tags.Count > 0)
        {
            writer.WritePropertyName("tags");
            JsonSerializer.Serialize(writer, value.Tags, options);
        }
        writer.WriteEndObject();
    }
}

// EventScript keeps its situations as a dictionary but is constructed from a list — serialize the list form.
public sealed class EventScriptJsonConverter : JsonConverter<EventScript>
{
    public override EventScript Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var start = root.GetProperty("startSituationId").GetString()!;
        var situations = JsonSerializer.Deserialize<List<EventSituation>>(
            root.GetProperty("situations").GetRawText(), options)!;
        return new EventScript(start, situations);
    }

    public override void Write(Utf8JsonWriter writer, EventScript value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("startSituationId", value.StartSituationId);
        writer.WritePropertyName("situations");
        JsonSerializer.Serialize(writer, value.Situations.Values.ToList(), options);
        writer.WriteEndObject();
    }
}
