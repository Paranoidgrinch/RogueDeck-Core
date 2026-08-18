using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// Rebuilds an encounter's serialized cross-combatant triggers (EncounterTriggerData) into live triggered-effect
// definitions for that encounter's combat. Unlike status triggers (StatusDataRebuild), these carry NO
// bearer-has-status filter — they fire on every event of their kind within the fight and the program itself
// gates + targets (an enemy passive that reacts to a player action). Registered per-encounter by
// EncounterCatalog.Build, so they are inert in fights that don't declare them.
public static class EncounterTriggerRebuild
{
    public static ITriggeredEffectDefinition Rebuild(string encounterId, int index, EncounterTriggerData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var ev = Enum.Parse<TriggerEvent>(data.Event);
        var id = new TriggeredEffectDefinitionId($"encounter_{encounterId}_trigger{index}");
        return ev switch
        {
            TriggerEvent.TurnStarted => TriggeredProgramContextAdapters.TurnStarted.Define(
                id, Program<TurnStartedTriggeredEffectContext>(data)),
            TriggerEvent.TurnEnded => TriggeredProgramContextAdapters.TurnEnded.Define(
                id, Program<TurnEndedTriggeredEffectContext>(data)),
            TriggerEvent.DamageTaken => TriggeredProgramContextAdapters.DamageReceived.Define(
                id, Program<DamageReceivedTriggeredEffectContext>(data)),
            TriggerEvent.DamageDealt => TriggeredProgramContextAdapters.DamageDealt.Define(
                id, Program<DamageDealtTriggeredEffectContext>(data)),
            TriggerEvent.Healed => TriggeredProgramContextAdapters.Healed.Define(
                id, Program<HealedTriggeredEffectContext>(data)),
            TriggerEvent.CardPlayed => TriggeredProgramContextAdapters.CardPlayed.Define(
                id, Program<CardPlayedTriggeredEffectContext>(data)),
            TriggerEvent.Downed => TriggeredProgramContextAdapters.CombatantDowned.Define(
                id, Program<CombatantDownedTriggeredEffectContext>(data)),
            TriggerEvent.StatusExpired => TriggeredProgramContextAdapters.StatusExpired.Define(
                id, Program<StatusExpiredTriggeredEffectContext>(data)),
            TriggerEvent.ResourceGained => TriggeredProgramContextAdapters.ResourceGained.Define(
                id, Program<ResourceGainedTriggeredEffectContext>(data)),
            TriggerEvent.CardCostPaid => TriggeredProgramContextAdapters.CardCostPaid.Define(
                id, Program<CardCostPaidTriggeredEffectContext>(data)),
            TriggerEvent.StatusApplied => TriggeredProgramContextAdapters.StatusApplied.Define(
                id, Program<StatusAppliedTriggeredEffectContext>(data)),
            TriggerEvent.StatusRemoved => TriggeredProgramContextAdapters.StatusRemoved.Define(
                id, Program<StatusRemovedTriggeredEffectContext>(data)),
            TriggerEvent.StatusMerged => TriggeredProgramContextAdapters.StatusMerged.Define(
                id, Program<StatusMergedTriggeredEffectContext>(data)),
            TriggerEvent.RoundStarted => TriggeredProgramContextAdapters.RoundStarted.Define(
                id, Program<RoundStartedTriggeredEffectContext>(data)),
            TriggerEvent.RoundEnded => TriggeredProgramContextAdapters.RoundEnded.Define(
                id, Program<RoundEndedTriggeredEffectContext>(data)),
            _ => throw new InvalidOperationException($"Trigger event '{ev}' is not supported for an encounter trigger."),
        };
    }

    private static EffectProgram<TContext> Program<TContext>(EncounterTriggerData data)
        where TContext : class =>
        data.Program.Deserialize<EffectProgram<TContext>>(CombatJson.CreateOptions<TContext>())
        ?? throw new InvalidOperationException($"Encounter trigger program for '{data.Event}' deserialized to null.");
}
