namespace RogueDeck.Sandbox.Composition;

// The second built-in sample project: "Bureaucrats & Broomsticks — Act I", a complete demo game
// (62 cards, 109 encounters, a baked act map with shop/rest/treasure/boss) converted from the original
// terminal game's data by github.com/Paranoidgrinch/bnb-content. The embedded file IS the converter's
// exported game.roguedeck.json — the Studio ships exactly what the export contract describes, and the
// document is already normalized (the converter gates on a byte-identical RunJson round trip).
public static class BureaucratsSample
{
    public const string Title = "Bureaucrats & Broomsticks — Act I";

    private static readonly Lazy<string> Cached = new(() =>
    {
        var assembly = typeof(BureaucratsSample).Assembly;
        const string resource = "RogueDeck.Sandbox.Run.SampleContent.bureaucrats.roguedeck.json";
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded sample '{resource}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Json() => Cached.Value;
}
