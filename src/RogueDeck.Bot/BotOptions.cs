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
}
