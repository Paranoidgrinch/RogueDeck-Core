using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Run;

// WHAT A PICK IS A PICTURE OF. A frontend that draws a card as a card needs to know WHICH card, and a display
// string cannot be turned back into an id — "Levy Stamp +" is a name, not an address. So alongside the name
// and the rules text a pick carries its identity: what kind of thing it is and which one, which is exactly
// what an art slot is keyed by. Kind is a small closed set because a frontend has one widget per kind (a card
// face, a relic tile); anything the run can offer that is not one of those — gold, healing, a further reward
// — has no art and says so by being absent.
public readonly record struct EntityArt(string Kind, string Id, int UpgradeLevel = 0)
{
    public const string Card = "card";
    public const string Relic = "relic";
}

// Turns the entities the run asks the player to pick between — reward offers, deck cards, relics — into
// readable names instead of raw ids. Shared by every frontend: the InteractiveRunSession bakes these
// strings into EntitySelectionRequest.Displays, so the Studio and the Godot host both get readable
// picks from one place. Built from the blueprint's display-name maps (RunPlayback owns them).
public sealed class RunEntityLabeler
{
    // THE ONE SPELLING OF A PICKED THING'S NAME. Both seats a bot can sit in put these strings in their log
    // — the replay seat reads them off EntitySelectionRequest.Displays, the direct seat never builds one —
    // so the naming has to be here rather than inside whoever happened to ask first. A reward offer is
    // described by what it grants, a deck card / relic by its display name; raw ids appear only when no
    // labeler was supplied (older test rigs).
    public static string Display(object? candidate, RunEntityLabeler? labeler) => candidate switch
    {
        RewardOffer offer => labeler?.Offer(offer) ?? offer.Id,
        RunCardInstance card => labeler is { } known
            ? known.Card(card.DefinitionId, card.UpgradeLevel)
            : card.UpgradeLevel > 0 ? $"{card.DefinitionId} +{card.UpgradeLevel}" : card.DefinitionId.ToString(),
        RelicInstance relic => labeler?.Relic(relic.Id) ?? relic.Id.ToString(),
        _ => candidate?.ToString() ?? "?",
    };

    private readonly IReadOnlyDictionary<string, string> _cards;
    private readonly IReadOnlyDictionary<string, string> _relics;
    private readonly IReadOnlyDictionary<string, string> _resources;
    private readonly IReadOnlyDictionary<string, string> _shreds;
    private readonly IReadOnlyDictionary<string, string> _cardDescriptions;
    private readonly IReadOnlyDictionary<string, string> _relicDescriptions;

    public RunEntityLabeler(
        IReadOnlyDictionary<string, string> cards,
        IReadOnlyDictionary<string, string> relics,
        IReadOnlyDictionary<string, string> resources,
        IReadOnlyDictionary<string, string> shreds,
        IReadOnlyDictionary<string, string>? cardDescriptions = null,
        IReadOnlyDictionary<string, string>? relicDescriptions = null)
    {
        _cards = cards;
        _relics = relics;
        _resources = resources;
        _shreds = shreds;
        _cardDescriptions = cardDescriptions ?? new Dictionary<string, string>();
        _relicDescriptions = relicDescriptions ?? new Dictionary<string, string>();
    }

    // The ability / rules text for a picked entity, so a reward pick shows WHAT a card does, not just
    // its title. Sourced from the presentation manifest (a card's description text); empty when unknown.
    public string Description(object? candidate) => candidate switch
    {
        RewardOffer offer => string.Join("  ·  ",
            offer.Grant.Select(DescribeGrant).Where(s => s.Length > 0)),
        RunCardInstance card => CardDescription(card.DefinitionId.value),
        RelicInstance relic => _relicDescriptions.GetValueOrDefault(relic.Id.Value, string.Empty),
        _ => string.Empty,
    };

    // The picture a pick is of, by the same walk Description does — and STATIC, because which card a pick is
    // a picture of has nothing to do with what anything is called: a rig with no labeler still gets its art.
    // A bundled offer (gold AND a card) is the one ambiguous case; it answers with the first thing in it that
    // has a picture at all, because a bundle that contains a card IS a card reward to the eye looking at it.
    public static EntityArt? ArtFor(object? candidate) => candidate switch
    {
        RewardOffer offer => ArtForGrant(offer.Grant),
        RunCardInstance card => new EntityArt(EntityArt.Card, card.DefinitionId.value, card.UpgradeLevel),
        RelicInstance relic => new EntityArt(EntityArt.Relic, relic.Id.Value),
        _ => null,
    };

    // The same question asked through the other doorway. A shop slot and an event choice hand a frontend
    // EFFECTS rather than a candidate object — and which effect grants which card is engine knowledge, so the
    // shelf and the event read it from here instead of each teaching itself the effect types.
    public static EntityArt? ArtForGrant(IReadOnlyList<IRunEffectRequest> effects) =>
        effects.Select(ArtOfGrant).FirstOrDefault(art => art is not null);

    private static EntityArt? ArtOfGrant(IRunEffectRequest effect) => effect switch
    {
        AddCardToDeckRunEffect card => new EntityArt(EntityArt.Card, card.Card.value),
        AddRelicByIdRunEffect relic => new EntityArt(EntityArt.Relic, relic.Relic.Value),
        AddRelicRunEffect relic => new EntityArt(EntityArt.Relic, relic.Relic.Id.Value),
        _ => null,
    };

    private string DescribeGrant(IRunEffectRequest effect) => effect switch
    {
        AddCardToDeckRunEffect card => CardDescription(card.Card.value),
        AddRelicByIdRunEffect relic => _relicDescriptions.GetValueOrDefault(relic.Relic.Value, string.Empty),
        AddRelicRunEffect relic => _relicDescriptions.GetValueOrDefault(relic.Relic.Id.Value, string.Empty),
        _ => string.Empty,
    };

    private string CardDescription(string definitionId) =>
        _cardDescriptions.GetValueOrDefault(definitionId, string.Empty);

    public string Card(CardDefinitionId card, int upgradeLevel = 0) =>
        CardName(card.value) + new string('+', upgradeLevel);

    public string Relic(RelicId relic) =>
        _relics.TryGetValue(relic.Value, out var name) ? name : Prettify(relic.Value);

    public string Resource(RunResourceId resource) =>
        _resources.TryGetValue(resource.Value, out var name) ? name : Prettify(resource.Value);

    // A reward offer described by what it grants (a card, a relic, gold, …), joined with " + " — so a
    // "spoils" offer bundling gold and a card reads "30 Gold + a card reward" instead of "spoils".
    public string Offer(RewardOffer offer)
    {
        var parts = offer.Grant.Select(Describe).Where(s => s.Length > 0).ToList();
        return parts.Count > 0 ? string.Join(" + ", parts) : Prettify(offer.Id);
    }

    private string Describe(IRunEffectRequest effect) => effect switch
    {
        AddCardToDeckRunEffect card => Card(card.Card),
        AddRelicByIdRunEffect relic => Relic(relic.Relic),
        AddRelicRunEffect relic => relic.Relic.Definition.DisplayName,
        ChangeResourceRunEffect resource => $"{resource.Delta} {Resource(resource.Resource)}",
        HealRunEffect heal => $"Heal {heal.Amount}",
        ShredEngine.AddShredRunEffect shred =>
            $"{shred.Count}× {(_shreds.TryGetValue(shred.ShredId, out var s) ? s : Prettify(shred.ShredId))}",
        // A reward that opens ANOTHER reward — the boss's purse leads to a card pick and then to its relic.
        // What the nested pick will hold is not knowable from here (its source has not been generated yet, and
        // generating it would roll the run's dice early), so the offer has to SAY what it is: that is exactly
        // what Kind is for. Unnamed, it is announced as a further reward rather than as a card, because
        // "a card reward" was a guess, and on every boss in a game that hands out relics it was the wrong one.
        OfferRewardRunEffect offer => offer.Kind switch
        {
            RewardKinds.Card => "a card reward",
            RewardKinds.Relic => "a relic",
            RewardKinds.Consumable => "a consumable",
            { Length: > 0 } other => $"a {other} reward",
            _ => "a further reward",
        },
        _ => string.Empty,
    };

    private string CardName(string definitionId)
    {
        if (_cards.TryGetValue(definitionId, out var name))
            return name;
        if (definitionId.StartsWith("shred:", StringComparison.Ordinal))
            return string.Join(" + ", definitionId["shred:".Length..].Split('+')
                .Select(part => _shreds.TryGetValue(part, out var partName) ? partName : part));
        return Prettify(definitionId);
    }

    // Last resort for an unmapped id: "card-stamp_form" → "Stamp form", "gold-30" → "Gold 30".
    private static string Prettify(string id)
    {
        var text = id.Replace('_', ' ').Replace('-', ' ').Trim();
        return text.Length == 0 ? id : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
