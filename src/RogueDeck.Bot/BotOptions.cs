namespace RogueDeck.Bot;

// Everything a single run needs to be told before it starts. Everything else the bot works out from the
// session it is handed.
public sealed record BotOptions
{
    public int Seed { get; init; } = 1;

    // The ceiling on ANSWERS, not on rooms. Under the replay model every answer re-runs the run from its
    // baseline, so this is a real cost and not a formality.
    public int Budget { get; init; } = 40000;

    // The map generator this run walked, for the report lines. ⚠ A runner must name its own map: two runs of
    // the same seed on two generators are two different games, and a report that does not say which one it
    // walked cannot be compared with anything.
    public string Maps { get; init; } = "—";

    // Whoever was rolled for this run, for the log header.
    public string? Character { get; init; }

    public BotPolicy? Policy { get; init; }

    // ── THE ROUTE THIS RUN IS TOLD TO WALK (O3) ──────────────────────────────────────────────────────────
    // Node ids, in order. At a fork, if one of the doors is the next id on this list, it is taken and no
    // policy is consulted; anywhere the list has nothing to say, the runner decides as it always would.
    //
    // ⚠⚠ IT IS NOT A BETTER RUNNER, IT IS A DIFFERENT QUESTION. A policy runner answers "how far does this
    // player get?", which mixes the player's navigation with the act's difficulty. Handing it the route
    // takes navigation off the table: play every route an act HAS, and what comes back is "does a way
    // through this act exist for this player at all?" — which is the question V-7 asks.
    //
    // ⚠ A ROUTE IS NOT A PROOF EITHER WAY. Cleared on some route is a constructive yes. Cleared on none is
    // "this player found none", never "none exists" — the same honesty the autopsy keeps (see FightSolver).
    public IReadOnlyList<string>? Route { get; init; }

    // What each card DOES, read once out of the shipped document — only a policy runner needs it.
    public CardFeatures? Features { get; init; }

    // ⚠ THE CHAMPION IS A DIFFERENT INSTRUMENT, NOT A BETTER SETTING (B5). It decides a play by FORKING the
    // fight, playing the card on the copy, letting the enemies answer and looking at what is left — so it
    // costs a fight-clone per candidate and buys the one skill the scoring runner cannot have: it knows what
    // is coming at it. Two runners for two questions: the coverage runner is fast, dumb and finds crashes,
    // walls and unreachable content over hundreds of seeds; the champion is slow, careful, and is the only
    // one whose failure to clear an act is evidence ABOUT THE ACT.
    public bool Champion { get; init; }

    // ⚠ WHEN A RUN DIES, PLAY THE FIGHT IT DIED IN AGAIN — every way it could have gone, and say whether ANY
    // of them wins (see FightSolver). Off by default because it costs a search per death; on, it is the only
    // thing this project has that can tell a fight that was lost from a fight that could not be won.
    public bool Autopsy { get; init; }

    public int AutopsySeconds { get; init; } = 60;

    // ⚠ HOW MANY POSITIONS ONE AUTOPSY MAY OPEN. It is a separate ceiling from the clock because the two
    // answer different questions: the clock keeps a batch moving, this one asks how big the tree ACTUALLY
    // is. Raised far past the default, the verdict stops being "the search ran out" and starts being a
    // statement about the fight — which is the only way to find out what an exhaustive answer costs here
    // rather than guessing at it.
    public int AutopsyPositions { get; init; } = 60_000;
}
