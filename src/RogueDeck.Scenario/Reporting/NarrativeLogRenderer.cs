using System.Text;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Reporting;

// Renders a ScenarioReport into a round-grouped, human-readable narrative log: it groups the steps under
// round headers, gives each step a "Turn N · actor · action [intent]" header, turns the step's trace slice
// into plain sentences (damage / heal / status / block / resources / lifecycle), and appends a problem
// summary. Pure formatting over the report — it never touches the engine.
//
// An optional name map (slug → display name) lets a caller show friendly combatant / card / action names
// instead of the raw ids. With no map it falls back to the raw ids unchanged.
public sealed class NarrativeLogRenderer
{
    private readonly Func<string, string> _name;

    public NarrativeLogRenderer(IReadOnlyDictionary<string, string>? names = null) =>
        _name = slug => names is not null && names.TryGetValue(slug, out var display) ? display : slug;

    public string Render(ScenarioReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("════════════════════════════════════");
        sb.AppendLine(" Scenario playtest log");
        sb.AppendLine("════════════════════════════════════");

        int? currentRound = null;
        foreach (var step in report.Steps)
        {
            if (currentRound != step.Round)
            {
                currentRound = step.Round;
                sb.AppendLine();
                sb.AppendLine($"── Round {step.Round} ──");
            }

            sb.AppendLine(RenderStepHeader(step));

            foreach (var beat in RenderBeats(step.Trace))
                sb.AppendLine($"    {beat}");

            foreach (var problem in step.Problems)
                sb.AppendLine($"    ⚠ {problem}");
        }

        sb.AppendLine();
        sb.AppendLine($"Result: {report.Result}");
        AppendProblemSummary(sb, report);

        return sb.ToString();
    }

    // ── Step header ──────────────────────────────────────────────────────────────

    private string RenderStepHeader(ScenarioStepReport step)
    {
        var actor = step.Actor is { } a ? Name(a) : "—";
        var intent = step.Intent is { } i ? $"  [{i.Kind}: {i.Label}]" : string.Empty;
        return $"Turn {step.Turn} · {actor} · {DescribeStep(step.Step)}{intent}";
    }

    private string DescribeStep(ScenarioStep step) => step switch
    {
        HeroPlaysCard play => $"plays '{Name(play.CardId)}'{Target(play.TargetId)}",
        HeroEndsTurn => "ends turn",
        EnemyActs enemy => $"uses '{Name(enemy.ActionId)}'{Target(enemy.TargetId)}",
        AdvanceToNextRound => "advances to the next round",
        _ => step.GetType().Name,
    };

    private string Target(string? targetId) => targetId is null ? string.Empty : $" → {Name(targetId)}";

    // ── Trace beats ──────────────────────────────────────────────────────────────

    private IEnumerable<string> RenderBeats(IReadOnlyList<CombatTraceEvent> trace)
    {
        foreach (var evt in trace)
        {
            var beat = FormatEvent(evt);
            if (beat is not null)
                yield return beat;
        }
    }

    // The narrative renders the "outcome" derivation events as sentences and skips the plumbing layer
    // (effect/event queueing, selector resolution, trigger evaluation, cost derivation).
    private string? FormatEvent(CombatTraceEvent evt) => evt switch
    {
        DamageResolvedTraceEvent d => FormatDamage(d),
        HealResolvedTraceEvent h =>
            $"{Name(h.TargetCombatantId)} heals {h.HealedAmount} → {h.HealthAfter} HP",
        BlockGainResolvedTraceEvent b =>
            $"{Name(b.TargetCombatantId)} gains {b.AmountAfterModifiers} block → {b.BlockAfter}",
        StatusApplicationResolvedTraceEvent s => FormatStatus(s),
        // A no-op resource change (e.g. a turn-start refill of an already-full pool) is pure noise.
        ResourceChangeResolvedTraceEvent { AppliedDelta: 0 } => null,
        ResourceChangeResolvedTraceEvent r =>
            $"{Name(r.CombatantId)} {Lower(r.Kind)} {Short(r.ResourceId.value)} {Signed(r.AppliedDelta)} → {r.NewCurrent}",
        DefensivePoolChangeResolvedTraceEvent p =>
            $"{Name(p.TargetCombatantId)} {Lower(p.Kind)} {Short(p.PoolId.value)} → {p.NewValue}",
        MaxHealthChangeResolvedTraceEvent m =>
            $"{Name(m.TargetCombatantId)} max HP {Signed(m.AppliedDelta)} → {m.NewMax}",
        HealthSetResolvedTraceEvent hs =>
            $"{Name(hs.TargetCombatantId)} HP set to {hs.NewValue}",
        CombatantTeamChangedResolvedTraceEvent t =>
            $"{Name(t.TargetCombatantId)} switches team {t.PreviousTeam} → {t.NewTeam}",
        CombatResultChangedTraceEvent c =>
            $"*** combat result: {c.PreviousResult} → {c.NewResult} ***",
        TurnStartedTraceEvent ts =>
            $"— {Name(ts.CombatantId)}'s turn begins —",
        TurnEndedTraceEvent te =>
            $"— {Name(te.CombatantId)}'s turn ends —",
        _ => null,
    };

    private string FormatDamage(DamageResolvedTraceEvent d)
    {
        var line = new StringBuilder($"{Name(d.TargetCombatantId)} takes {d.HealthLost} damage");
        if (d.BlockedAmount > 0)
            line.Append($" ({d.BlockedAmount} blocked)");
        if (d.IgnoresBlock)
            line.Append(" [true]");
        line.Append($" → {d.HealthAfter} HP");
        if (d.HealthLost > 0 && d.HealthAfter == 0)
            line.Append(" (downed)");
        return line.ToString();
    }

    private string FormatStatus(StatusApplicationResolvedTraceEvent s)
    {
        var magnitude =
            s.ResultingStacks > 0 ? $"{s.ResultingStacks} stk"
            : s.ResultingDurationTurns > 0 ? $"{s.ResultingDurationTurns} turns"
            : s.ResultingCharges > 0 ? $"{s.ResultingCharges} charges"
            : "—";
        return $"{Name(s.TargetCombatantId)}: {Short(s.StatusDefinitionId.value)} {s.Outcome} ({magnitude})";
    }

    // ── Problem summary ──────────────────────────────────────────────────────────

    private static void AppendProblemSummary(StringBuilder sb, ScenarioReport report)
    {
        var problemSteps = report.ProblemSteps.ToList();
        if (problemSteps.Count == 0)
        {
            sb.AppendLine("No problems detected.");
            return;
        }

        var total = problemSteps.Sum(step => step.Problems.Count);
        sb.AppendLine($"Problems ({total}):");
        foreach (var step in problemSteps)
            foreach (var problem in step.Problems)
                sb.AppendLine($"  • [step {step.Index}] {problem}");
    }

    private string Name(CombatantId id) => _name(id.value);

    private string Name(string slug) => _name(slug);

    private static string Short(string id) => id.StartsWith("standard.", StringComparison.Ordinal) ? id["standard.".Length..] : id;

    private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

    private static string Lower(Enum value) => value.ToString().ToLowerInvariant();
}
