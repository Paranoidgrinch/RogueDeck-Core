using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

public static class CombatStateSnapshotter
{
    public static CombatStateSnapshot CreateSnapshot(CombatState combat)
    {
        ArgumentNullException.ThrowIfNull(combat);

        // Combatants and CardZones in TurnOrder for a deterministic, stable ordering.
        var combatants = combat.TurnOrder
            .Select(id => SnapshotCombatant(combat.GetCombatant(id)))
            .ToImmutableArray();

        var cardZones = combat.TurnOrder
            .Where(id => combat.CardZonesByCombatant.ContainsKey(id))
            .Select(id => (id, SnapshotCardZones(combat.CardZonesByCombatant[id])))
            .ToImmutableArray();

        var globalStatuses = combat.GlobalStatuses
            .Select(SnapshotStatus)
            .ToImmutableArray();

        // Ordered by id for a deterministic, install-order-independent fingerprint.
        var temporaryRules = combat.TemporaryTriggeredPrograms
            .Select(t => new TemporaryTriggeredProgramSnapshot(
                Id: t.Id.value,
                EventType: t.EventType.FullName ?? t.EventType.Name,
                RemainingActivations: t.RemainingActivations,
                ExpiresAfterRound: t.ExpiresAfterRound,
                ExpiresAfterTurn: t.ExpiresAfterTurn,
                ExpiresWhenOwnerRemoved: t.ExpiresWhenOwnerRemoved,
                OwnerCombatantId: t.OwnerCombatantId?.value,
                InstalledRound: t.InstalledRound,
                InstalledTurn: t.InstalledTurn,
                IsExpired: t.IsExpired))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToImmutableArray();

        return new CombatStateSnapshot(
            Id: combat.Id,
            RandomSeed: combat.RandomSeed,
            RandomStep: combat.RandomStep,
            Result: combat.Result,
            CurrentRound: combat.CurrentRound,
            CurrentTurn: combat.CurrentTurn,
            TurnPhase: combat.TurnPhase,
            ActiveCombatantId: combat.ActiveCombatantId,
            TurnOrder: combat.TurnOrder.ToImmutableArray(),
            Combatants: combatants,
            GlobalStatuses: globalStatuses,
            CardZones: cardZones,
            NextStatusInstanceNumber: combat.NextStatusInstanceNumber,
            NextCardInstanceNumber: combat.NextCardInstanceNumber,
            NextSummonedCombatantNumber: combat.NextSummonedCombatantNumber,
            NextEffectChainNumber: combat.NextEffectChainNumber,
            NextProgramExecutionId: combat.NextProgramExecutionId,
            TemporaryRules: temporaryRules);
    }

    private static CombatantSnapshot SnapshotCombatant(CombatantState c) =>
        new(
            Id: c.Id,
            DefinitionId: c.DefinitionId,
            TeamId: c.TeamId,
            LifecycleState: c.LifecycleState,
            HealthCurrent: c.Health.Current,
            HealthMax: c.Health.Max,
            Resources: c.Resources
                .Select(kv => (kv.Key, new PoolSnapshot(kv.Value.Current, kv.Value.Max, kv.Value.CanExceedMax)))
                .OrderBy(p => p.Key.value, StringComparer.Ordinal)
                .ToImmutableArray(),
            DefensivePools: c.DefensivePools
                .Select(kv => (kv.Key, new PoolSnapshot(kv.Value.Current, kv.Value.Max, kv.Value.CanExceedMax)))
                .OrderBy(p => p.Key.value, StringComparer.Ordinal)
                .ToImmutableArray(),
            Statuses: c.Statuses.Select(SnapshotStatus).ToImmutableArray(),
            Tags: c.Tags
                .OrderBy(t => t.value, StringComparer.Ordinal)
                .ToImmutableArray(),
            Counters: c.Counters
                .Select(kv => (kv.Key, kv.Value))
                .OrderBy(p => p.Key.value, StringComparer.Ordinal)
                .ToImmutableArray());

    private static StatusInstanceSnapshot SnapshotStatus(StatusInstance s) =>
        new(
            Id: s.Id,
            DefinitionId: s.DefinitionId,
            OwnerCombatantId: s.OwnerCombatantId,
            Stacks: s.Stacks,
            DurationTurns: s.DurationTurns,
            Charges: s.Charges,
            Polarity: s.Polarity,
            Tags: s.Tags
                .OrderBy(t => t.value, StringComparer.Ordinal)
                .ToImmutableArray(),
            Counters: s.Counters
                .Select(kv => (kv.Key, kv.Value))
                .OrderBy(p => p.Key.value, StringComparer.Ordinal)
                .ToImmutableArray());

    private static CombatantCardZonesSnapshot SnapshotCardZones(CombatantCardZones zones) =>
        new(
            DrawPile: zones.DrawPile.Select(SnapshotCard).ToImmutableArray(),
            Hand: zones.Hand.Select(SnapshotCard).ToImmutableArray(),
            DiscardPile: zones.DiscardPile.Select(SnapshotCard).ToImmutableArray(),
            ExhaustPile: zones.ExhaustPile.Select(SnapshotCard).ToImmutableArray(),
            BanishedPile: zones.BanishedPile.Select(SnapshotCard).ToImmutableArray());

    private static CardInstanceSnapshot SnapshotCard(CardInstance c) =>
        new(c.Id, c.DefinitionId, c.Zone);
}
