using RogueDeck.Run;

namespace RogueDeck.Bot;

// What KIND of room a map node is, in one word — the word every runner's log, every report and the map
// drawing all use. A mimic answers "treasure", because that is what it looks like until it is opened, and a
// report that named it otherwise would spoil the one room in the game whose point is the surprise.
//
// It lives here rather than in the frontend because three walkers and two reports need it and only one of
// them can draw.
public static class MapRole
{
    public static string Of(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var tag = node.Tags.Count > 0 ? node.Tags[0] : node.Type.Value;
        return tag == MapNodeTags.Mimic ? MapNodeTags.Treasure : tag;
    }
}
