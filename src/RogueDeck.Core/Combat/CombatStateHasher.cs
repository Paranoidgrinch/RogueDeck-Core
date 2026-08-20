using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace RogueDeck.Core.Combat;

// Computes a stable, deterministic SHA-256 hash from a CombatStateSnapshot.
// The hash is suitable for determinism tests and replay verification.
// It is NOT a security hash — just a compact fingerprint.
//
// Format: canonical key=value text, UTF-8, SHA-256, returned as lowercase hex.
// Field order is fixed; collections are written in the order stored in the snapshot
// (which the snapshotter already sorts). Dictionary-backed fields are sorted by key.
public static class CombatStateHasher
{
    public static string ComputeHash(CombatStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sb = new StringBuilder(1024);
        AppendSnapshot(sb, snapshot);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void AppendSnapshot(StringBuilder sb, CombatStateSnapshot s)
    {
        sb.Append("id=").Append(s.Id.value).Append('\n');
        sb.Append("seed=").Append(s.RandomSeed).Append('\n');
        sb.Append("step=").Append(s.RandomStep).Append('\n');
        sb.Append("result=").Append((int)s.Result).Append('\n');
        sb.Append("round=").Append(s.CurrentRound).Append('\n');
        sb.Append("turn=").Append(s.CurrentTurn).Append('\n');
        sb.Append("phase=").Append((int)s.TurnPhase).Append('\n');
        sb.Append("active=").Append(s.ActiveCombatantId?.value ?? "").Append('\n');
        sb.Append("status_num=").Append(s.NextStatusInstanceNumber).Append('\n');
        sb.Append("card_num=").Append(s.NextCardInstanceNumber).Append('\n');
        sb.Append("chain_num=").Append(s.NextEffectChainNumber).Append('\n');
        sb.Append("exec_num=").Append(s.NextProgramExecutionId).Append('\n');

        sb.Append("turn_order=");
        foreach (var id in s.TurnOrder)
            sb.Append(id.value).Append(',');
        sb.Append('\n');

        foreach (var status in s.GlobalStatuses)
        {
            sb.Append("gs=");
            AppendStatus(sb, status);
        }

        foreach (var combatant in s.Combatants)
            AppendCombatant(sb, combatant);

        foreach (var (combatantId, zones) in s.CardZones)
        {
            sb.Append("cz=").Append(combatantId.value).Append('\n');
            AppendCardZones(sb, zones);
        }

        foreach (var rule in s.TemporaryRules)
            sb.Append("tr=").Append(rule.Id)
                .Append(' ').Append(rule.EventType)
                .Append(" act=").Append(rule.RemainingActivations?.ToString() ?? "inf")
                .Append(" expR=").Append(rule.ExpiresAfterRound?.ToString() ?? "none")
                .Append(" expT=").Append(rule.ExpiresAfterTurn?.ToString() ?? "none")
                .Append(" owner=").Append(rule.OwnerCombatantId ?? "none")
                .Append(rule.ExpiresWhenOwnerRemoved ? " ownerbound" : "")
                .Append(" inst=").Append(rule.InstalledRound).Append('.').Append(rule.InstalledTurn)
                .Append(rule.IsExpired ? " dead" : "")
                .Append('\n');
    }

    private static void AppendCombatant(StringBuilder sb, CombatantSnapshot c)
    {
        sb.Append("c=").Append(c.Id.value)
            .Append(" def=").Append(c.DefinitionId.value)
            .Append(" team=").Append(c.TeamId.value)
            .Append(" lc=").Append((int)c.LifecycleState)
            .Append(" hp=").Append(c.HealthCurrent).Append('/').Append(c.HealthMax)
            .Append('\n');

        foreach (var (key, pool) in c.Resources)
            sb.Append("res=").Append(key.value)
                .Append(' ').Append(pool.Current).Append('/').Append(pool.Max?.ToString() ?? "null")
                .Append('\n');

        foreach (var (key, pool) in c.DefensivePools)
            sb.Append("dp=").Append(key.value)
                .Append(' ').Append(pool.Current).Append('/').Append(pool.Max?.ToString() ?? "null")
                .Append('\n');

        foreach (var status in c.Statuses)
        {
            sb.Append("s=");
            AppendStatus(sb, status);
        }

        sb.Append("tags=");
        foreach (var tag in c.Tags)
            sb.Append(tag.value).Append(',');
        sb.Append('\n');

        foreach (var (key, value) in c.Counters)
            sb.Append("cnt=").Append(key.value).Append('=').Append(value).Append('\n');
    }

    private static void AppendStatus(StringBuilder sb, StatusInstanceSnapshot s)
    {
        sb.Append(s.Id.value)
            .Append(' ').Append(s.DefinitionId.value)
            .Append(" stk=").Append(s.Stacks)
            .Append(" dur=").Append(s.DurationTurns)
            .Append(" chg=").Append(s.Charges)
            .Append(" pol=").Append((int)s.Polarity);
        if (s.PendingTurns > 0)
            sb.Append(" pend=").Append(s.PendingTurns);
        sb.Append('\n');
    }

    private static void AppendCardZones(StringBuilder sb, CombatantCardZonesSnapshot zones)
    {
        AppendCards(sb, "draw", zones.DrawPile);
        AppendCards(sb, "hand", zones.Hand);
        AppendCards(sb, "disc", zones.DiscardPile);
        AppendCards(sb, "exh", zones.ExhaustPile);
        AppendCards(sb, "ban", zones.BanishedPile);
    }

    private static void AppendCards(
        StringBuilder sb,
        string zone,
        ImmutableArray<CardInstanceSnapshot> cards)
    {
        foreach (var card in cards)
        {
            sb.Append(zone).Append('=').Append(card.Id.value)
                .Append(' ').Append(card.DefinitionId.value);

            if (!card.Marks.IsDefaultOrEmpty)
                foreach (var mark in card.Marks)
                    sb.Append(" m:").Append(mark.value);

            if (!card.MarkCounters.IsDefaultOrEmpty)
                foreach (var (key, value) in card.MarkCounters)
                    sb.Append(" mc:").Append(key.value).Append('=').Append(value);

            if (card.MarkSourceCombatantId is { } src)
                sb.Append(" msrc:").Append(src.value);

            sb.Append('\n');
        }
    }
}
