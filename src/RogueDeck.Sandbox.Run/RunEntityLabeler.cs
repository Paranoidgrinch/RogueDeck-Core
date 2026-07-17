using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Run;

// Turns the entities the run asks the player to pick between — reward offers, deck cards, relics — into
// readable names instead of raw ids. Shared by every frontend: the InteractiveRunSession bakes these
// strings into EntitySelectionRequest.Displays, so the Studio and the Godot host both get readable
// picks from one place. Built from the blueprint's display-name maps (RunPlayback owns them).
public sealed class RunEntityLabeler
{
    private readonly IReadOnlyDictionary<string, string> _cards;
    private readonly IReadOnlyDictionary<string, string> _relics;
    private readonly IReadOnlyDictionary<string, string> _resources;
    private readonly IReadOnlyDictionary<string, string> _shreds;

    public RunEntityLabeler(
        IReadOnlyDictionary<string, string> cards,
        IReadOnlyDictionary<string, string> relics,
        IReadOnlyDictionary<string, string> resources,
        IReadOnlyDictionary<string, string> shreds)
    {
        _cards = cards;
        _relics = relics;
        _resources = resources;
        _shreds = shreds;
    }

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
        OfferRewardRunEffect => "a card reward",
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
