using RogueDeck.ShredEngine;

namespace RogueDeck.Run;

// Turns a generated node's role + chosen encounter into a concrete Node (NodeType + payload) using the spec's
// content references. The default realization the run uses when it generates a map: combat-flavoured roles carry the
// encounter the generator selected; a Shop / Workbench / Event-family role resolves to its authored id from
// MapGenerationSpec.NodeRefs. A game that wants richer realization (per-node reward tables, multiple shops) can pass
// its own content delegate to RuleBasedMapGenerator instead.
public static class MapNodeRealizer
{
    // nodeRef, when set (from NodeRefPools via the generator), is the specific authored ref this non-combat node
    // drew; null falls back to the single NodeRefs[kind]. Combat roles ignore it.
    public static NodeContent Realize(MapGenerationSpec spec, MapNodeKind kind, EncounterId? encounter, string? nodeRef = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        switch (kind)
        {
            case MapNodeKind.Combat:
            case MapNodeKind.MultiCombat: // a guaranteed multi-enemy fight — realized as a normal combat
            case MapNodeKind.Elite:
            case MapNodeKind.Boss:
            case MapNodeKind.Mimic: // a Treasure node that flipped into a fight — realized as a normal combat
                if (encounter is not { } id)
                    throw new InvalidOperationException(
                        $"A {kind} node has no encounter to run; add candidates for {kind} to MapGeneration.Encounters.");
                return new NodeContent(StandardRunIds.CombatNode, spec.VictoryRewards.TryGetValue(kind, out var reward)
                    ? new EncounterRef(id, reward.Source, new RewardId($"{reward.RewardIdPrefix}:{id.Value}"),
                        reward.PickCount)
                    : new EncounterRef(id), Tags(kind));

            case MapNodeKind.Shop:
                return new NodeContent(
                    StandardRunIds.ShopNode, new ShopRef(new ShopId(nodeRef ?? RequireRef(spec, kind))), Tags(kind));

            case MapNodeKind.Workbench:
                return new NodeContent(
                    ShredEngineIds.WorkbenchNode, new WorkbenchRef(new WorkbenchId(nodeRef ?? RequireRef(spec, kind))),
                    Tags(kind));

            case MapNodeKind.Event:
            case MapNodeKind.Rest:
            case MapNodeKind.Treasure:
                return new NodeContent(
                    StandardRunIds.EventNode, new EventRef(new EventId(nodeRef ?? RequireRef(spec, kind))), Tags(kind));

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled map node kind.");
        }
    }

    // The role a node was generated for, as a tag on the node itself — the ONLY thing that survives realization
    // (an elite and an ordinary fight are the same node type with the same payload shape). Lower-cased so the tag
    // reads like content ("elite"), not like an enum member.
    public static IReadOnlyList<string> Tags(MapNodeKind kind) => [MapNodeTags.For(kind)];

    private static string RequireRef(MapGenerationSpec spec, MapNodeKind kind) =>
        spec.NodeRefs.TryGetValue(kind, out var id) && !string.IsNullOrEmpty(id)
            ? id
            : throw new InvalidOperationException(
                $"MapGeneration has no NodeRefs entry for the {kind} role; add one so its nodes can be realized.");
}

// The stable tag names a generated node carries for its role. Content matches on these strings (a relic that
// pays "after defeating an Elite" asks for MapNodeTags.Elite), so they are written down once here rather than
// spelled out at every call site.
public static class MapNodeTags
{
    public const string Combat = "combat";
    public const string MultiCombat = "multi-combat";
    public const string Elite = "elite";
    public const string Boss = "boss";
    public const string Mimic = "mimic";
    public const string Shop = "shop";
    public const string Rest = "rest";
    public const string Event = "event";
    public const string Treasure = "treasure";
    public const string Workbench = "workbench";

    public static string For(MapNodeKind kind) => kind switch
    {
        MapNodeKind.Combat => Combat,
        MapNodeKind.MultiCombat => MultiCombat,
        MapNodeKind.Elite => Elite,
        MapNodeKind.Boss => Boss,
        MapNodeKind.Mimic => Mimic,
        MapNodeKind.Shop => Shop,
        MapNodeKind.Rest => Rest,
        MapNodeKind.Event => Event,
        MapNodeKind.Treasure => Treasure,
        MapNodeKind.Workbench => Workbench,
        _ => kind.ToString().ToLowerInvariant(),
    };
}
