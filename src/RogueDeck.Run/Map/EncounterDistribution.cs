namespace RogueDeck.Run;

// One weighted encounter candidate in a distribution. Weight skews how often it is drawn among the in-band
// candidates of its role (min 1 in practice; a non-positive weight is treated as 1).
public sealed record EncounterPoolEntry(EncounterId Encounter, int Weight = 1);

// Which encounters a generated combat-flavoured node can run, weighted, per generator role. The rule-based map
// generator draws a Combat node's encounter from the Combat list, an Elite node's from the Elite list, a Boss
// node's from the Boss list. A role with no candidates ⇒ "no distribution for this role"; the generator decides
// whether that is an error (a combat node with nothing to run) or fine (a non-combat role). Weight AND threat
// (from the BalanceManifest) together decide the draw — see EncounterSelector. A plain record of an enum-keyed
// dictionary, so it round-trips through RunJson.
public sealed record EncounterDistribution
{
    public IReadOnlyDictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>> ByRole { get; init; }
        = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>();

    // The candidates authored for a role, or an empty list when the role has none.
    public IReadOnlyList<EncounterPoolEntry> For(MapNodeKind role) =>
        ByRole.TryGetValue(role, out var list) ? list : Array.Empty<EncounterPoolEntry>();
}
