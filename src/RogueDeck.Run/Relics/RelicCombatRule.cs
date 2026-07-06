using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run;

// A relic's COMBAT rule (face (b), as data): a combat trigger event + an effect program to run in that event's
// context, with a firing priority. This is the serializable authoring shape of a TriggeredProgramDefinition — the
// engine's ContextFactory/BuildContext Funcs are NOT stored here; they are recovered from the Trigger key via the
// RelicCombatTriggers catalog. That catalog lookup is the whole reason a combat contribution can be data: the only
// non-serializable part of a triggered program (the context Funcs) is canonical per event, so a key names it.
public sealed record RelicCombatRule
{
    public required string Trigger { get; init; }

    // The authored EffectProgram<TContext> for this trigger's context, boxed (TContext is fixed by Trigger). Its
    // (de)serialization and its build into an engine definition both route through RelicCombatTriggers by Trigger.
    public required object Program { get; init; }

    public int Priority { get; init; }
}

// One authorable combat-trigger event: the string key a RelicCombatRule stores, paired with everything derived from
// the engine's canonical adapter — the event/context types, how to build the engine definition from a program, and
// how to round-trip the program through CombatJson (context-specific converters). Lives in RogueDeck.Run because it
// bridges the run-authoring data shape (RelicCombatRule) to the combat engine's TriggeredProgramContextAdapters.
public sealed class RelicCombatTrigger
{
    public required string Key { get; init; }
    public required Type EventType { get; init; }
    public required Type ContextType { get; init; }
    public required Func<TriggeredEffectDefinitionId, object, int, ITriggeredEffectDefinition> Build { get; init; }
    public required Func<object, JsonElement> Serialize { get; init; }
    public required Func<JsonElement, object> Deserialize { get; init; }

    // A minimal valid default program for this context (so a freshly-added rule is authorable / round-trips).
    public required Func<object> NewProgram { get; init; }

    // The visual-editor bridge: classify a boxed EffectProgram<TContext> into the editable CombatNodeModel (null →
    // outside the visual subset, keep the JSON textarea), and build a boxed program back from a model. Both close
    // over TContext exactly like Serialize/Deserialize, so the UI (CombatProgramEditor) stays context-free.
    public required Func<object, CombatNodeModel?> ToModel { get; init; }
    public required Func<CombatNodeModel, object> FromModel { get; init; }
}

// The catalog of combat triggers a relic rule may hook. Each entry closes the engine's generic adapter +
// CombatJson over one (event, context) pair, so RelicCombatRule stays a flat, key-addressed data record. This is
// deliberately a SUBSET for now (R1): more events are one `For(...)` line each, and event-value-reading programs
// need their context's value expressions registered (a later slice).
public static class RelicCombatTriggers
{
    private static RelicCombatTrigger For<TEvent, TContext>(
        string key, TriggeredProgramAdapter<TEvent, TContext> adapter, Func<EffectProgram<TContext>> newProgram)
        where TEvent : class, ICombatEvent
        where TContext : class
    {
        // Per-context CombatJson options — closes the open-generic node/expr/selector kinds on TContext, exactly
        // like RunJson does for the card / enemy-action contexts.
        var options = CombatJson.CreateOptions<TContext>();
        return new RelicCombatTrigger
        {
            Key = key,
            EventType = typeof(TEvent),
            ContextType = typeof(TContext),
            Build = (id, program, priority) => adapter.Define(id, (EffectProgram<TContext>)program, priority),
            Serialize = program => JsonSerializer.SerializeToElement((EffectProgram<TContext>)program, options),
            Deserialize = element => JsonSerializer.Deserialize<EffectProgram<TContext>>(element, options)!,
            NewProgram = newProgram,
            ToModel = program => CombatProgramModel.Classify((EffectProgram<TContext>)program),
            FromModel = model => CombatProgramModel.Build<TContext>(model),
        };
    }

    private static readonly IReadOnlyDictionary<string, RelicCombatTrigger> ByKey =
        new[]
        {
            // "At the start of your turn, gain 3 block."
            For("turnStarted", TriggeredProgramContextAdapters.TurnStarted,
                () => new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new GainBlockNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(3)))),

            // "Whenever you play a card, gain 1 block."
            For("cardPlayed", TriggeredProgramContextAdapters.CardPlayed,
                () => new EffectProgram<CardPlayedTriggeredEffectContext>(
                    new GainBlockNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<CardPlayedTriggeredEffectContext>(1)))),

            // Event-READING triggers: the default programs use EventAmountExpression, which reads the triggering
            // event's amount (damage taken / dealt, heal, resource gained) — proof a rule can react to the event,
            // not just to combat state. "Thorns": take damage → gain that much block.
            For("damageReceived", TriggeredProgramContextAdapters.DamageReceived,
                () => new EffectProgram<DamageReceivedTriggeredEffectContext>(
                    new GainBlockNode<DamageReceivedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new EventAmountExpression<DamageReceivedTriggeredEffectContext>()))),

            // "Lifesteal": deal damage → heal by that much.
            For("damageDealt", TriggeredProgramContextAdapters.DamageDealt,
                () => new EffectProgram<DamageDealtTriggeredEffectContext>(
                    new HealNode<DamageDealtTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new EventAmountExpression<DamageDealtTriggeredEffectContext>()))),

            // "When healed, gain that much block."
            For("healed", TriggeredProgramContextAdapters.Healed,
                () => new EffectProgram<HealedTriggeredEffectContext>(
                    new GainBlockNode<HealedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new EventAmountExpression<HealedTriggeredEffectContext>()))),

            // "When you gain a resource, gain that much block."
            For("resourceGained", TriggeredProgramContextAdapters.ResourceGained,
                () => new EffectProgram<ResourceGainedTriggeredEffectContext>(
                    new GainBlockNode<ResourceGainedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new EventAmountExpression<ResourceGainedTriggeredEffectContext>()))),
        }.ToDictionary(t => t.Key, StringComparer.Ordinal);

    public static IEnumerable<string> Keys => ByKey.Keys;

    public static bool Has(string key) => ByKey.ContainsKey(key);

    public static bool TryGet(string key, out RelicCombatTrigger trigger) => ByKey.TryGetValue(key, out trigger!);

    public static RelicCombatTrigger Get(string key) =>
        ByKey.TryGetValue(key, out var trigger)
            ? trigger
            : throw new KeyNotFoundException(
                $"Unknown relic combat trigger '{key}'. Known: {string.Join(", ", ByKey.Keys)}.");
}
