using System.Text;

namespace RogueDeck.Core.Combat;

// A trace listener that simply buffers every CombatTraceEvent in order. Attach it to
// CombatState.TraceListener before a combat runs, then read Events (or hand them to
// DiagnosticCombatLogRenderer) afterwards to inspect exactly how the engine produced each result.
//
// This is the buffering counterpart to the inline ICombatTraceListener: where the coarse CombatLog
// records outcomes, the trace stream records derivations (e.g. DamageResolvedTraceEvent carries every
// modifier-pipeline step and the block absorption that produced the final number).
public sealed class CombatTraceCollector : ICombatTraceListener
{
    private readonly List<CombatTraceEvent> _events = [];

    public IReadOnlyList<CombatTraceEvent> Events => _events;

    public void OnTrace(CombatTraceEvent evt) => _events.Add(evt);

    public void Clear() => _events.Clear();

    // Convenience: the buffered events of one concrete kind, in order.
    public IEnumerable<T> OfType<T>() where T : CombatTraceEvent => _events.OfType<T>();
}

// Renders a stream of CombatTraceEvents into a human-readable diagnostic log — the "how the engine
// produced this" view. Derivation events (currently damage; more pipelines to follow) expand into an
// indented breakdown; other trace events render as a single annotated line. Pure formatting, no state.
public static class DiagnosticCombatLogRenderer
{
    public static string Render(IEnumerable<CombatTraceEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var sb = new StringBuilder();
        foreach (var evt in events)
            AppendEvent(sb, evt);
        return sb.ToString();
    }

    private static void AppendEvent(StringBuilder sb, CombatTraceEvent evt)
    {
        var at = $"R{evt.Round}T{evt.Turn}";

        switch (evt)
        {
            case DamageResolvedTraceEvent d:
                AppendDamage(sb, at, d);
                break;

            case CombatEventDispatchedTraceEvent e:
                sb.AppendLine($"{at} event {e.EventType} → {e.HandlerCount} handler(s)");
                break;

            case TriggerEvaluatedTraceEvent t:
                AppendTriggerEvaluation(sb, at, t);
                break;

            case StatusApplicationResolvedTraceEvent s:
                AppendStatusApplication(sb, at, s);
                break;

            case HealResolvedTraceEvent h:
                AppendHeal(sb, at, h);
                break;

            case BlockGainResolvedTraceEvent b:
                AppendBlockGain(sb, at, b);
                break;

            case DefensivePoolChangeResolvedTraceEvent p:
                AppendDefensivePoolChange(sb, at, p);
                break;

            case ResourceChangeResolvedTraceEvent rc:
                AppendResourceChange(sb, at, rc);
                break;

            case SelectorResolvedTraceEvent sel:
                AppendSelectorResolved(sb, at, sel);
                break;

            case CardCostResolvedTraceEvent cc:
                AppendCardCost(sb, at, cc);
                break;

            case EffectResolvedTraceEvent r:
                sb.AppendLine($"{at} effect {r.RequestType} resolved (chain {r.ChainId})");
                break;

            case CombatResultChangedTraceEvent c:
                sb.AppendLine($"{at} result {c.PreviousResult} → {c.NewResult}");
                break;

            case CommandAppliedTraceEvent c:
                sb.AppendLine($"{at} command {c.CommandType}");
                break;

            default:
                sb.AppendLine($"{at} {evt.GetType().Name}");
                break;
        }
    }

    private static void AppendTriggerEvaluation(StringBuilder sb, string at, TriggerEvaluatedTraceEvent t)
    {
        var kind = t.IsTemporary ? "temp-trigger" : "trigger";
        var verdict = t.Outcome switch
        {
            TriggerEvaluationOutcome.Fired => "FIRED",
            TriggerEvaluationOutcome.SkippedReentrySuppressed => "skipped (re-entry suppressed)",
            TriggerEvaluationOutcome.SkippedDepthLimited => "skipped (trigger depth limit)",
            TriggerEvaluationOutcome.SkippedContextUnavailable => "skipped (context unavailable)",
            TriggerEvaluationOutcome.SkippedFilterRejected => "skipped (filter rejected)",
            _ => t.Outcome.ToString()
        };
        sb.AppendLine($"{at} {kind} {t.TriggerId} (prio {t.Priority}) on {t.EventType}: {verdict}");
    }

    private static void AppendStatusApplication(StringBuilder sb, string at, StatusApplicationResolvedTraceEvent s)
    {
        var head = $"{at} StatusApply {s.StatusDefinitionId.value} → {s.TargetCombatantId.value}" +
            $" (req stacks={s.RequestedStacks} dur={s.RequestedDurationTurns} charges={s.RequestedCharges}): ";
        switch (s.Outcome)
        {
            case StatusApplicationOutcome.Applied:
                sb.AppendLine($"{head}applied" +
                    $" (stacks={s.ResultingStacks} dur={s.ResultingDurationTurns} charges={s.ResultingCharges})");
                break;
            case StatusApplicationOutcome.Merged:
                sb.AppendLine($"{head}merged" +
                    $" (stacks={s.ResultingStacks} dur={s.ResultingDurationTurns} charges={s.ResultingCharges})");
                break;
            case StatusApplicationOutcome.BlockedByInterceptor:
                sb.AppendLine($"{head}blocked by {s.InterceptingModifierId}");
                break;
            case StatusApplicationOutcome.ReplacedByInterceptor:
                sb.AppendLine($"{head}replaced by {s.InterceptingModifierId} → {s.ReplacementRequestType}");
                break;
            default:
                sb.AppendLine($"{head}{s.Outcome}");
                break;
        }
    }

    private static void AppendSelectorResolved(StringBuilder sb, string at, SelectorResolvedTraceEvent sel)
    {
        var targets = sel.ResolvedTargetIds.Count == 0
            ? "(none)"
            : string.Join(", ", sel.ResolvedTargetIds);
        sb.AppendLine($"{at} Selector {sel.SelectorType} [{sel.Cardinality}] → {targets}");
    }

    private static void AppendCardCost(StringBuilder sb, string at, CardCostResolvedTraceEvent cc)
    {
        sb.AppendLine($"{at} CardCost {cc.CardId.value} {cc.ResourceId.value}: base={cc.BaseAmount}");

        foreach (var step in cc.ModifierSteps)
            sb.AppendLine($"{at}     {step.ModifierId}: {step.Before} → {step.After}");

        if (cc.FinalAmount != cc.BaseAmount || cc.ModifierSteps.Count > 0)
            sb.AppendLine($"{at}     = final: {cc.FinalAmount}");
    }

    private static void AppendResourceChange(StringBuilder sb, string at, ResourceChangeResolvedTraceEvent rc)
    {
        var bound = rc.ReachedMaximum ? " [max]" : rc.ReachedMinimum ? " [min]" : string.Empty;
        sb.AppendLine(
            $"{at} Resource {rc.ResourceId.value} on {rc.CombatantId.value} {rc.Kind.ToString().ToLowerInvariant()}:" +
            $" requested {rc.RequestedAmount}, applied {rc.AppliedDelta} ({rc.PreviousCurrent} → {rc.NewCurrent}){bound}");
    }

    private static void AppendHeal(StringBuilder sb, string at, HealResolvedTraceEvent h)
    {
        var source = h.SourceCombatantId is { } s ? s.value : "—";
        sb.AppendLine(
            $"{at} HealResolved: {source} → {h.TargetCombatantId.value}  requested={h.RequestedAmount}" +
            $" healed={h.HealedAmount} (health {h.HealthBefore} → {h.HealthAfter})");
    }

    private static void AppendBlockGain(StringBuilder sb, string at, BlockGainResolvedTraceEvent b)
    {
        sb.AppendLine(
            $"{at} BlockGain → {b.TargetCombatantId.value}  requested={b.RequestedAmount}");

        foreach (var step in b.ModifierSteps)
            sb.AppendLine($"{at}     {step.ModifierId}: {step.Before} → {step.After}");

        if (b.AmountAfterModifiers != b.RequestedAmount || b.ModifierSteps.Count > 0)
            sb.AppendLine($"{at}     = after modifiers: {b.AmountAfterModifiers}");

        sb.AppendLine($"{at}     block: {b.BlockBefore} → {b.BlockAfter}");
    }

    private static void AppendDefensivePoolChange(StringBuilder sb, string at, DefensivePoolChangeResolvedTraceEvent p)
    {
        var verb = p.Kind == DefensivePoolChangeKind.Cleared ? "cleared" : "modified";
        sb.AppendLine(
            $"{at} DefensivePool {p.PoolId.value} on {p.TargetCombatantId.value} {verb}:" +
            $" requested {p.RequestedDelta}, applied {p.AppliedDelta} ({p.PreviousValue} → {p.NewValue})");
    }

    private static void AppendDamage(StringBuilder sb, string at, DamageResolvedTraceEvent d)
    {
        var source = d.SourceCombatantId is { } s ? s.value : "—";
        var card = d.SourceCardId is { } c ? $" card={c.value}" : string.Empty;
        sb.AppendLine(
            $"{at} DamageResolved: {source} → {d.TargetCombatantId.value}  base={d.BaseAmount} kind={d.Kind}{card}");

        foreach (var step in d.ModifierSteps)
            sb.AppendLine($"{at}     {step.Stage,-6} {step.ModifierId}: {step.Before} → {step.After}");

        if (d.AmountAfterModifiers != d.BaseAmount || d.ModifierSteps.Count > 0)
            sb.AppendLine($"{at}     = after modifiers: {d.AmountAfterModifiers}");

        if (d.BlockPoolId is { } pool)
            sb.AppendLine(
                $"{at}     block  {pool.value}: absorbed {d.BlockedAmount} ({d.BlockBefore} → {d.BlockAfter})");

        sb.AppendLine(
            $"{at}     health {d.TargetCombatantId.value}: {d.HealthBefore} → {d.HealthAfter} (lost {d.HealthLost})");
    }
}
