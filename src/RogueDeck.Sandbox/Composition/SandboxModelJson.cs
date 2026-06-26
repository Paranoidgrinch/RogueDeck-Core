using System.Text.Json;
using System.Text.Json.Serialization;

namespace RogueDeck.Sandbox.Composition;

// Exports / imports the whole SandboxModel as human-readable JSON so a user can save, reload, and share
// a sandbox setup (the page state is otherwise lost on refresh). Enums are written as names.
public static class SandboxModelJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Export(SandboxModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return JsonSerializer.Serialize(model, Options);
    }

    public static SandboxModel Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("No JSON to import.");

        return JsonSerializer.Deserialize<SandboxModel>(json, Options)
            ?? throw new InvalidOperationException("The JSON did not describe a sandbox.");
    }
}
