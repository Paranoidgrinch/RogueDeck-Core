using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// Rebuilds serialized status data (triggers + death/debuff-block interceptors) into live engine definitions — the
// data→engine half of the old ScenarioComposer, kept SandboxModel-FREE so it serves a run's content assembly
// (RunPlayback.BuildContent). Everything here takes StatusTriggerData / interceptor data, never an editor model.
public static class StatusDataRebuild
{
    // Rebuilds a serialized status trigger into a live triggered-effect definition. `index` makes the definition id
    // unique within the status. Deserializes the program under the event's trigger context (the JSON is context-free)
    // and binds the *HasStatus filter for the bearer.
    public static ITriggeredEffectDefinition RebuildTrigger(string statusSlug, int index, StatusTriggerData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var ev = Enum.Parse<TriggerEvent>(data.Event);
        var id = new TriggeredEffectDefinitionId($"{statusSlug}_trigger{index}");
        var statusId = new StatusDefinitionId(statusSlug);

        // Bearer scope filters the event down to the wearer; Anywhere keeps the rule alive for whoever the event
        // is about, as long as somebody still wears the status. Built per context because each trigger context is
        // its own record — they only agree on having a Combat.
        var anywhere = data.Scope == StatusTriggerScope.Anywhere;
        ITriggeredProgramFilter<TContext>[] Scoped<TContext>(
            Func<TContext, CombatState> readCombat, params ITriggeredProgramFilter<TContext>[] bearerFilters)
            where TContext : class =>
            anywhere
                ? [new AnyCombatantHasStatusTriggerFilter<TContext>(statusId, readCombat)]
                : bearerFilters;

        return ev switch
        {
            TriggerEvent.TurnStarted => TriggeredProgramContextAdapters.TurnStarted.Define(
                id, Program<TurnStartedTriggeredEffectContext>(data),
                filters: Scoped<TurnStartedTriggeredEffectContext>(c => c.Combat,
                    new TurnStartedCombatantHasStatusTriggerFilter(statusId))),
            TriggerEvent.TurnEnded => TriggeredProgramContextAdapters.TurnEnded.Define(
                id, Program<TurnEndedTriggeredEffectContext>(data),
                filters: Scoped<TurnEndedTriggeredEffectContext>(c => c.Combat,
                    new TurnEndedCombatantHasStatusTriggerFilter(statusId))),
            TriggerEvent.DamageTaken => TriggeredProgramContextAdapters.DamageReceived.Define(
                id, Program<DamageReceivedTriggeredEffectContext>(data),
                filters: Scoped<DamageReceivedTriggeredEffectContext>(c => c.Combat,
                    new DamageReceivedReceiverHasStatusTriggerFilter(statusId))),
            TriggerEvent.DamageDealt => TriggeredProgramContextAdapters.DamageDealt.Define(
                id, Program<DamageDealtTriggeredEffectContext>(data),
                filters: Scoped<DamageDealtTriggeredEffectContext>(c => c.Combat,
                    new DamageDealtSourceHasStatusTriggerFilter(statusId))),
            TriggerEvent.Healed => TriggeredProgramContextAdapters.Healed.Define(
                id, Program<HealedTriggeredEffectContext>(data),
                filters: Scoped<HealedTriggeredEffectContext>(c => c.Combat,
                    new HealedTargetHasStatusTriggerFilter(statusId))),
            TriggerEvent.CardPlayed => TriggeredProgramContextAdapters.CardPlayed.Define(
                id, Program<CardPlayedTriggeredEffectContext>(data),
                filters: Scoped<CardPlayedTriggeredEffectContext>(c => c.Combat,
                    new CardPlayedSourceHasStatusTriggerFilter(statusId))),
            TriggerEvent.Downed => TriggeredProgramContextAdapters.CombatantDowned.Define(
                id, Program<CombatantDownedTriggeredEffectContext>(data),
                filters: Scoped<CombatantDownedTriggeredEffectContext>(c => c.Combat,
                    new CombatantDownedHasStatusTriggerFilter(statusId))),
            // Bearer scope here means something slightly different from the other events: the question is
            // whether THIS status is the one that ran out. Anywhere is how a status watches every OTHER
            // status expiring, on anyone.
            TriggerEvent.StatusExpired => TriggeredProgramContextAdapters.StatusExpired.Define(
                id, Program<StatusExpiredTriggeredEffectContext>(data),
                filters: Scoped<StatusExpiredTriggeredEffectContext>(c => c.Combat,
                    new StatusExpiredStatusDefinitionTriggerFilter(statusId))),
            TriggerEvent.ResourceGained => TriggeredProgramContextAdapters.ResourceGained.Define(
                id, Program<ResourceGainedTriggeredEffectContext>(data),
                filters: Scoped<ResourceGainedTriggeredEffectContext>(c => c.Combat,
                    new ResourceGainedSourceHasStatusTriggerFilter(statusId))),
            TriggerEvent.CardCostPaid => TriggeredProgramContextAdapters.CardCostPaid.Define(
                id, Program<CardCostPaidTriggeredEffectContext>(data),
                filters: Scoped<CardCostPaidTriggeredEffectContext>(c => c.Combat,
                    new CardCostPaidSourceHasStatusTriggerFilter(statusId))),
            TriggerEvent.StatusApplied => TriggeredProgramContextAdapters.StatusApplied.Define(
                id, Program<StatusAppliedTriggeredEffectContext>(data),
                filters: Scoped<StatusAppliedTriggeredEffectContext>(c => c.Combat,
                    new StatusAppliedTargetHasStatusTriggerFilter(statusId),
                    new StatusAppliedExceptStatusDefinitionTriggerFilter(statusId))),
            TriggerEvent.StatusRemoved => TriggeredProgramContextAdapters.StatusRemoved.Define(
                id, Program<StatusRemovedTriggeredEffectContext>(data),
                filters: Scoped<StatusRemovedTriggeredEffectContext>(c => c.Combat,
                    new StatusRemovedTargetHasStatusTriggerFilter(statusId))),
            TriggerEvent.StatusMerged => TriggeredProgramContextAdapters.StatusMerged.Define(
                id, Program<StatusMergedTriggeredEffectContext>(data),
                filters: Scoped<StatusMergedTriggeredEffectContext>(c => c.Combat,
                    new StatusMergedTargetHasStatusTriggerFilter(statusId))),
            TriggerEvent.StatusStacksChanged => TriggeredProgramContextAdapters.StatusStacksChanged.Define(
                id, Program<StatusStacksChangedTriggeredEffectContext>(data),
                filters: Scoped<StatusStacksChangedTriggeredEffectContext>(c => c.Combat,
                    new StatusStacksChangedTargetHasStatusTriggerFilter(statusId))),
            TriggerEvent.BlockGained => TriggeredProgramContextAdapters.BlockGained.Define(
                id, Program<BlockGainedTriggeredEffectContext>(data),
                filters: Scoped<BlockGainedTriggeredEffectContext>(c => c.Combat,
                    new BlockGainedTargetHasStatusTriggerFilter(statusId))),
            TriggerEvent.CardsDrawn => TriggeredProgramContextAdapters.CardsDrawn.Define(
                id, Program<CardsDrawnTriggeredEffectContext>(data),
                filters: Scoped<CardsDrawnTriggeredEffectContext>(c => c.Combat,
                    new CardsDrawnSourceHasStatusTriggerFilter(statusId))),
            TriggerEvent.CardMovedToZone => TriggeredProgramContextAdapters.CardMovedToZone.Define(
                id, Program<CardMovedToZoneTriggeredEffectContext>(data),
                filters: Scoped<CardMovedToZoneTriggeredEffectContext>(c => c.Combat,
                    new CardMovedToZoneOwnerHasStatusTriggerFilter(statusId))),
            TriggerEvent.CardInstanceCreated => TriggeredProgramContextAdapters.CardInstanceCreated.Define(
                id, Program<CardInstanceCreatedTriggeredEffectContext>(data),
                filters: Scoped<CardInstanceCreatedTriggeredEffectContext>(c => c.Combat,
                    new CardInstanceCreatedOwnerHasStatusTriggerFilter(statusId))),
            TriggerEvent.RoundStarted => TriggeredProgramContextAdapters.RoundStarted.Define(
                id, Program<RoundStartedTriggeredEffectContext>(data)),
            TriggerEvent.RoundEnded => TriggeredProgramContextAdapters.RoundEnded.Define(
                id, Program<RoundEndedTriggeredEffectContext>(data)),
            // A prevention is reported on the combatant it happened TO, and the status that paid for it is
            // usually not the one reacting — so the bearer scope asks that the reacting status be on that
            // combatant, and Anywhere lets a rule watch preventions on either side of the fight.
            TriggerEvent.ActionResolved => TriggeredProgramContextAdapters.ActionResolved.Define(
                id, Program<ActionResolvedTriggeredEffectContext>(data),
                filters: Scoped<ActionResolvedTriggeredEffectContext>(c => c.Combat,
                    new ActionResolvedActorHasStatusTriggerFilter(statusId))),
            TriggerEvent.StatusApplicationPrevented => TriggeredProgramContextAdapters.StatusApplicationBlocked.Define(
                id, Program<StatusApplicationBlockedTriggeredEffectContext>(data),
                filters: Scoped<StatusApplicationBlockedTriggeredEffectContext>(c => c.Combat,
                    new StatusApplicationBlockedTargetHasStatusTriggerFilter(statusId))),
            _ => throw new InvalidOperationException($"Trigger event '{ev}' is not supported for a status trigger."),
        };
    }

    private static EffectProgram<TContext> Program<TContext>(StatusTriggerData data)
        where TContext : class =>
        data.Program.Deserialize<EffectProgram<TContext>>(CombatJson.CreateOptions<TContext>())
        ?? throw new InvalidOperationException($"Trigger program for '{data.Event}' deserialized to null.");

    // Rebuilds interceptor effect data into the request factory the interceptor classes run when they fire.
    private static InterceptorEffects BuildRequestFactory(IReadOnlyList<InterceptorEffectData> effects) =>
        (bearer, combat, registry) =>
        {
            var requests = new List<IEffectRequest>();
            foreach (var line in effects)
            {
                var kind = Enum.Parse<EffectKind>(line.Kind);
                var target = Enum.Parse<EffectTarget>(line.Target);
                var amount = Math.Max(0, line.Amount);
                foreach (var targetId in ResolveInterceptorTargets(target, bearer, combat))
                {
                    IEffectRequest? request = kind switch
                    {
                        EffectKind.DealDamage => new DealDamageEffectRequest(targetId, amount, bearer.Id),
                        EffectKind.GainBlock => new GainBlockEffectRequest(targetId, amount, bearer.Id),
                        EffectKind.Heal => new HealEffectRequest(targetId, amount, bearer.Id),
                        EffectKind.ApplyStatus => new ApplyStatusEffectRequest(
                            targetId, new StatusDefinitionId(line.StatusId), bearer.Id, Stacks: amount, DurationTurns: line.DurationTurns),
                        EffectKind.Cleanse => new RemoveStatusesByPolarityEffectRequest(targetId, line.Polarity),
                        EffectKind.RemoveStatus => new RemoveStatusEffectRequest(targetId, new StatusDefinitionId(line.StatusId)),
                        _ => null,
                    };
                    if (request is not null)
                        requests.Add(request);
                }
            }
            return requests;
        };

    // Rebuild a status' interceptors from data into live engine interceptors (used by a run's BuildContent). The
    // internal interceptor classes are constructed only here.
    public static IPreDownInterceptor RebuildDeathPrevention(string statusSlug, StatusDeathPreventionData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new StatusDeathPreventionInterceptor(
            new StatusDefinitionId(statusSlug), data.SurvivingHealth, BuildRequestFactory(data.Effects));
    }

    public static IStatusApplicationInterceptor RebuildDebuffBlock(string statusSlug, StatusDebuffBlockData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new StatusBlocksApplicationInterceptor(
            new StatusDefinitionId(statusSlug), StatusPolarity.Debuff, BuildRequestFactory(data.Effects));
    }

    private static IEnumerable<CombatantId> ResolveInterceptorTargets(
        EffectTarget target, CombatantState bearer, CombatState combat) => target switch
        {
            EffectTarget.AllEnemies => combat.Combatants.Where(c => c.IsAlive && c.TeamId != bearer.TeamId).Select(c => c.Id).ToList(),
            EffectTarget.AllAllies => combat.Combatants.Where(c => c.IsAlive && c.TeamId == bearer.TeamId && c.Id != bearer.Id).Select(c => c.Id).ToList(),
            EffectTarget.AllCombatants => combat.Combatants.Where(c => c.IsAlive).Select(c => c.Id).ToList(),
            _ => new List<CombatantId> { bearer.Id }, // Self / Target / extremes → the bearer
        };
}
