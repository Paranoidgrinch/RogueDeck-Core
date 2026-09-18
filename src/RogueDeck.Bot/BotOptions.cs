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
}
