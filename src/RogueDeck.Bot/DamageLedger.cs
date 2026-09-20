using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── WHERE THE LIFE WENT (P2) ─────────────────────────────────────────────────────────────────────────────
// Every measurement this arc has made about a losing run says the same thing and none of them says WHY: the
// binding constraint is the health budget (the same 67 routes clear 5 at 70 health and 27 at 140), the
// runner is beaten at a tenth of its positions, and when it dies the autopsy calls the fight avoidable. So
// the fights are not unfair and the doors are exhausted, and still the body runs out. The one thing nobody
// has ever printed is the receipt: seventy points of health went somewhere, and WHO TOOK THEM.
//
// That is all this file is. Not a better player — an accounting of the only currency the game has.
//
// ⚠⚠ IT INVENTS NOTHING. Every point it names comes off a DamageResolvedTraceEvent the engine already
// emitted while the fight ran: the target, the source combatant, the source card, the kind, how much the
// guard ate and how much reached the body. The step the event sits in names the enemy ACTION, because the
// driver records one step per action and hands its trace slice along. Nothing here models damage; it reads
// what the damage pipeline wrote down on its way past.
//
// ⚠⚠ AND IT RECONCILES, so that it cannot quietly lose the points it cannot explain. The run's own tally
// (`BotMind.DamageTaken`, health watched at every answer) is the total; what this names is held against it
// and the difference is reported as `unnamed`. A ledger that adds up to less than the body lost is a ledger
// with a hole in it, and it says so rather than letting the top of the table look complete.
//
// ⚠ A FORK IS NOT A FIGHT. The champion plays hundreds of imagined turns per real one, but every fork is its
// own InteractiveCombat with its own trace collector, so nothing imagined ever reaches this. What is read
// here is the fight the body stood in.
public sealed class DamageLedger
{
    // One row of the receipt. Room and role are carried so that the same table answers both questions P3
    // asks: which SOURCES cost the most, and which ROOMS they cost it in.
    public readonly record struct Entry(int Act, string Room, string Role, string Source);

    public sealed class Tally
    {
        public int Health;    // what reached the body
        public int Blocked;   // what the guard ate on the way — context, never added to the health
        public int Hits;      // how many times this source landed, blocked or not
    }

    private readonly Dictionary<Entry, Tally> _rows = [];

    // How much of the fight in progress has already been read. A fight's steps only ever grow while it runs,
    // so a cursor is enough — but a RESTORED fight starts its list again (the replay seat rebuilds from a
    // snapshot), and a cursor left past the end would silently swallow everything that followed. Clamped the
    // same way BotMind clamps its narration mark, for the same reason.
    private int _read;

    // WHICH fight that cursor belongs to. Identity, not a flag a caller has to remember to set: both seats
    // hand the fight over before they announce it, so a "the fight has started" call would arrive one answer
    // too late and the first blow of every fight would be read against the last fight's cursor.
    private InteractiveCombat? _fight;

    public IReadOnlyDictionary<Entry, Tally> Rows => _rows;

    public int Named => _rows.Values.Sum(t => t.Health);

    public int Blocked => _rows.Values.Sum(t => t.Blocked);

    // Read whatever the fight has written since the last look. Called on every answer rather than once at
    // the bell, because the fight that matters most is the one the run does not walk out of: a hero killed
    // by the last blow leaves a combat nobody hands over again.
    public void Read(InteractiveCombat combat, int act, string room, string role)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (!ReferenceEquals(combat, _fight))
        {
            _fight = combat;
            _read = 0;
        }

        var steps = combat.Steps;
        if (_read > steps.Count)
            _read = 0;

        for (; _read < steps.Count; _read++)
        {
            var step = steps[_read];
            // ⚠ THE TRACE IS WALKED IN ORDER, not filtered to the damage. A rule that fires says so one
            // event before it does its work, and for a hit nothing else names — a turn-start toll, a curse
            // biting at the bell — that announcement is the only name there is.
            string? fired = null;
            foreach (var evt in step.Trace)
            {
                if (evt is TriggerEvaluatedTraceEvent { Outcome: TriggerEvaluationOutcome.Fired } rule)
                {
                    fired = rule.TriggerId;
                    continue;
                }
                if (evt is not DamageResolvedTraceEvent hit || hit.TargetCombatantId != combat.HeroId)
                    continue;
                var row = new Entry(act, room, role, Source(step, hit, combat.HeroId, fired));
                if (!_rows.TryGetValue(row, out var tally))
                    _rows[row] = tally = new Tally();
                tally.Health += hit.HealthLost;
                tally.Blocked += hit.BlockedAmount;
                tally.Hits++;
            }
        }
    }

    // Health the run lost with no fight running: a door that bites, a curse collected at a shrine, an act's
    // own toll. Named by the room, because outside a fight the room IS the source.
    public void Outside(int amount, int act, string room, string role)
    {
        if (amount <= 0)
            return;
        var row = new Entry(act, room, role, $"room/{room}");
        if (!_rows.TryGetValue(row, out var tally))
            _rows[row] = tally = new Tally();
        tally.Health += amount;
        tally.Hits++;
    }

    // ── WHO TOOK IT, IN THE CONTENT'S OWN NAMES ──────────────────────────────────────────────────────────
    // The trace event is asked first and the step second, in that order and not the other way round: an
    // enemy that hits back when it is hit does so DURING the hero's card, so reading the step alone would
    // file the counter-blow under the card that provoked it. The event knows who swung.
    private static string Source(
        ScenarioStepReport step, DamageResolvedTraceEvent hit, CombatantId hero, string? fired)
    {
        // ⚠⚠ A STATUS THAT TAKES HEALTH IS ASKED FIRST, and it is the reason the engine now carries the
        // status id through the damage pipeline at all. The first receipt this project printed put a
        // QUARTER of an act's damage under "—/overtime": the largest single line on it, and the only one
        // nobody could act on. Who applied the poison is a second question; what is eating the body is
        // this one.
        if (hit.SourceStatusId is { } status)
            return $"status/{status.value}";

        // An enemy landed it. If this is the step in which that enemy acted, the action has a name too —
        // which is the whole point: "the bailiff" is a complaint, "the bailiff's summons" is a lead.
        if (hit.SourceCombatantId is { } who && who != hero)
            return step.Step is EnemyActs acting && acting.EnemyId == who.value
                ? $"{who.value}/{acting.ActionId}"
                : $"{who.value}/{Kind(hit.Kind)}";

        // The hero's own card did it: a cost paid in blood, a curse that bites when it is played.
        if (hit.SourceCardId is { } card)
            return $"card/{card.value}";

        // ⚠ NOBODY WAS NAMED, AND THE STEP STILL KNOWS WHO WAS ACTING. An effect program may take health
        // straight off the body ("HP loss, not damage" — DamageKind.DamageOverTime used directly) without
        // filling in a source, and on the first real receipt that was the LARGEST single entry of act I:
        // 25 of 64 points under "—". What was happening at the time is not a guess — it is the step the
        // engine recorded around it.
        return step.Step switch
        {
            EnemyActs acting => $"{acting.EnemyId}/{acting.ActionId}",
            HeroPlaysCard played => $"card/{played.CardId}",
            _ => fired is { } rule ? $"rule/{rule}" : $"—/{Kind(hit.Kind)}",
        };
    }

    private static string Kind(DamageKind kind) => kind switch
    {
        DamageKind.DamageOverTime => "overtime",
        DamageKind.Reflected => "reflected",
        _ => "direct",
    };

    // The act's receipt, biggest first. `unnamed` is what the run's own tally says the body lost and this
    // could not account for; it is a row like any other so that it cannot be read past.
    public IReadOnlyList<(string Source, Tally Tally)> TopOfAct(int act, int take)
    {
        var named = _rows
            .Where(r => r.Key.Act == act)
            .GroupBy(r => r.Key.Source, StringComparer.Ordinal)
            .Select(g => (Source: g.Key, Tally: new Tally
            {
                Health = g.Sum(x => x.Value.Health),
                Blocked = g.Sum(x => x.Value.Blocked),
                Hits = g.Sum(x => x.Value.Hits),
            }))
            .OrderByDescending(x => x.Tally.Health)
            .ThenBy(x => x.Source, StringComparer.Ordinal)
            .Take(take)
            .ToList();
        return named;
    }

    public int HealthOfAct(int act) => _rows.Where(r => r.Key.Act == act).Sum(r => r.Value.Health);

    public int BlockedOfAct(int act) => _rows.Where(r => r.Key.Act == act).Sum(r => r.Value.Blocked);

    public IReadOnlyList<int> Acts => [.. _rows.Keys.Select(k => k.Act).Distinct().Order()];

    // Everything, one row per source per room: the table P3's sweep adds up across seeds.
    public IEnumerable<(Entry Where, Tally Tally)> All() =>
        _rows.OrderBy(r => r.Key.Act).ThenByDescending(r => r.Value.Health)
            .Select(r => (r.Key, r.Value));
}
