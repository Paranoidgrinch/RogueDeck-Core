using RogueDeck.Core.Combat;

namespace RogueDeck.Scenario.Scripting;

// The multiplayer seam for a party fight (party deckbuilding C1). PartyCombat already accepts member-keyed calls
// in submission order; this layer makes the "N independent players" shape explicit and shippable: each player is
// an IPartyPlayerAgent that only ever acts on the members it owns, and the scheduler merges their submissions into
// the ONE authoritative combat in a deterministic order. In a networked game the agents live on different clients
// and the host runs the scheduler; nothing here knows about netcode. Because the engine is single-threaded and
// deterministic by request order, the merged input log fully determines the fight — record it and Replay reaches
// byte-identical state, which is exactly what a lockstep or replay-based multiplayer needs.

// A single submitted action. A closed hierarchy so the scheduler can both apply and record it.
public abstract record PartyInput;

public sealed record PlayCardInput(CombatantId Member, CardInstanceId Card, CombatantId? Target) : PartyInput;

public sealed record EndTurnInput(CombatantId Member) : PartyInput;

// One player's controller. NextAction returns the action the player wants to take next given the current combat,
// or null when the player has nothing more to submit right now (e.g. it has ended all the members it owns this
// phase). An agent must only return actions for members it owns — the scheduler does not police ownership; it
// trusts each agent, exactly as a host trusts an authenticated client's inputs for its own slots.
public interface IPartyPlayerAgent
{
    PartyInput? NextAction(PartyCombat combat);
}

public static class PartyInputScheduler
{
    // Drive the combat from N player agents, polling them round-robin and applying each submitted action to the
    // single combat as it arrives. Stops when the combat ends or a full round-robin cycle produces no action (all
    // players idle). Returns the ordered log of applied inputs — replaying it reproduces the fight exactly.
    public static IReadOnlyList<PartyInput> Run(
        PartyCombat combat, IReadOnlyList<IPartyPlayerAgent> agents, int maxSteps = 10_000)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(agents);

        var log = new List<PartyInput>();
        if (agents.Count == 0)
            return log;

        var idle = 0;
        var cursor = 0;
        while (!combat.IsOver && idle < agents.Count && log.Count < maxSteps)
        {
            var action = agents[cursor].NextAction(combat);
            if (action is null)
            {
                idle++;
            }
            else
            {
                Apply(combat, action);
                log.Add(action);
                idle = 0; // someone acted — keep the fight going
            }
            cursor = (cursor + 1) % agents.Count;
        }
        return log;
    }

    // Re-apply a recorded input log to a fresh, identically-built combat. Determinism-by-request-order means the
    // result is byte-identical to the original run — the property a replay / lockstep multiplayer relies on.
    public static void Replay(PartyCombat combat, IReadOnlyList<PartyInput> log)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(log);
        foreach (var input in log)
            Apply(combat, input);
    }

    // The one place an input becomes an engine call — the authoritative apply step both live play and replay share.
    public static void Apply(PartyCombat combat, PartyInput input)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(input);
        switch (input)
        {
            case PlayCardInput play:
                combat.PlayCard(play.Member, play.Card, play.Target);
                break;
            case EndTurnInput end:
                combat.EndTurn(end.Member);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.GetType().Name, "Unknown party input.");
        }
    }
}
