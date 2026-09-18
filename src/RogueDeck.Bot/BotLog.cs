namespace RogueDeck.Bot;

// Where a bot's account of itself goes. The log IS the reproduction — seed and character at the top, then
// every room, every choice and every play in order, with the engine's own narration folded in — so it has to
// come out somewhere that is not tied to a host. Godot passes GD.Print; the console runner passes a writer.
public interface IBotLog
{
    void Line(string text);
}

// A log that hands each line to a delegate.
public sealed class DelegateBotLog(Action<string> write) : IBotLog
{
    private readonly Action<string> _write = write ?? throw new ArgumentNullException(nameof(write));

    public void Line(string text) => _write(text);
}

// A log that keeps nothing. For a caller that only wants the result line.
public sealed class NullBotLog : IBotLog
{
    public static readonly NullBotLog Instance = new();

    public void Line(string text) { }
}
