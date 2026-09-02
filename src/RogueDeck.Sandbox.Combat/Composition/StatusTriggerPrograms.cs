using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// The visual-editor bridge for a status TRIGGER program: each TriggerEvent fixes the effect program's context type
// (the same map StatusDataRebuild uses to rebuild triggers into engine definitions), so this catalog closes the
// context-free ↔ typed conversions over that type. It lets the StatusEditor author a trigger's program with the
// shared CombatProgramEditor while StatusTriggerData stays context-free JSON on the wire (as the run document stores
// it — see the TriggerData test helper). The mirror of RelicCombatTriggers, keyed by TriggerEvent instead.
public sealed class StatusTriggerProgram
{
    // Deserialize the stored context-free program and classify it into the editable model (null → outside the visual
    // subset, keep JSON), build a model back into stored context-free JSON, and a benign default for a new trigger.
    public required Func<JsonElement, CombatNodeModel?> ToModel { get; init; }
    public required Func<CombatNodeModel, JsonElement> FromModel { get; init; }
    public required Func<JsonElement> NewProgram { get; init; }
}

public static class StatusTriggerPrograms
{
    private static StatusTriggerProgram For<TContext>() where TContext : class
    {
        var options = CombatJson.CreateOptions<TContext>();
        return new StatusTriggerProgram
        {
            ToModel = json => CombatProgramModel.Classify(
                json.Deserialize<EffectProgram<TContext>>(options)
                ?? throw new JsonException("Status trigger program deserialized to null.")),
            FromModel = model => JsonSerializer.SerializeToElement(CombatProgramModel.Build<TContext>(model), options),
            // A benign starter (gain 1 block on the bearer) that classifies + round-trips in every trigger context.
            NewProgram = () => JsonSerializer.SerializeToElement(
                new EffectProgram<TContext>(new GainBlockNode<TContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<TContext>(1))),
                options),
        };
    }

    // TriggerEvent → its trigger context, exactly as StatusDataRebuild.RebuildTrigger binds each event.
    private static readonly IReadOnlyDictionary<TriggerEvent, StatusTriggerProgram> ByEvent =
        new Dictionary<TriggerEvent, StatusTriggerProgram>
        {
            [TriggerEvent.TurnStarted] = For<TurnStartedTriggeredEffectContext>(),
            [TriggerEvent.TurnEnded] = For<TurnEndedTriggeredEffectContext>(),
            [TriggerEvent.DamageTaken] = For<DamageReceivedTriggeredEffectContext>(),
            [TriggerEvent.DamageDealt] = For<DamageDealtTriggeredEffectContext>(),
            [TriggerEvent.Healed] = For<HealedTriggeredEffectContext>(),
            [TriggerEvent.CardPlayed] = For<CardPlayedTriggeredEffectContext>(),
            [TriggerEvent.Downed] = For<CombatantDownedTriggeredEffectContext>(),
            [TriggerEvent.StatusExpired] = For<StatusExpiredTriggeredEffectContext>(),
            [TriggerEvent.ResourceGained] = For<ResourceGainedTriggeredEffectContext>(),
            [TriggerEvent.CardCostPaid] = For<CardCostPaidTriggeredEffectContext>(),
            [TriggerEvent.StatusApplied] = For<StatusAppliedTriggeredEffectContext>(),
            [TriggerEvent.StatusRemoved] = For<StatusRemovedTriggeredEffectContext>(),
            [TriggerEvent.StatusMerged] = For<StatusMergedTriggeredEffectContext>(),
            [TriggerEvent.StatusStacksChanged] = For<StatusStacksChangedTriggeredEffectContext>(),
            [TriggerEvent.BlockGained] = For<BlockGainedTriggeredEffectContext>(),
            [TriggerEvent.CardsDrawn] = For<CardsDrawnTriggeredEffectContext>(),
            [TriggerEvent.CardMovedToZone] = For<CardMovedToZoneTriggeredEffectContext>(),
            [TriggerEvent.CardInstanceCreated] = For<CardInstanceCreatedTriggeredEffectContext>(),
            [TriggerEvent.RoundStarted] = For<RoundStartedTriggeredEffectContext>(),
            [TriggerEvent.RoundEnded] = For<RoundEndedTriggeredEffectContext>(),
            [TriggerEvent.StatusApplicationPrevented] = For<StatusApplicationBlockedTriggeredEffectContext>(),
            [TriggerEvent.StatusApplicationAmplified] = For<StatusApplicationAmplifiedTriggeredEffectContext>(),
            [TriggerEvent.ActionResolved] = For<ActionResolvedTriggeredEffectContext>(),
        };

    public static StatusTriggerProgram Get(TriggerEvent ev) => ByEvent[ev];
}
