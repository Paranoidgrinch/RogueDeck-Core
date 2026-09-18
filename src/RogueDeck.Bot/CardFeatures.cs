using System.Text.Json;

namespace RogueDeck.Bot;

// What each card DOES, read once out of the shipped document: how often its program deals damage, raises a
// guard, puts something on somebody, draws, or pays. A policy weighs these counts; the dice player never asks.
//
// It counts occurrences of node names in the card's raw JSON rather than walking the effect tree. That is
// deliberately crude — it is a heuristic a search breeds weights against, not a rules engine — and it is
// cheap enough to do for every card in the game at startup.
public sealed class CardFeatures
{
    public const int Count = 5;

    private static readonly double[] Nothing = new double[Count];

    private readonly Dictionary<string, double[]> _byCard;

    private CardFeatures(Dictionary<string, double[]> byCard) => _byCard = byCard;

    public static CardFeatures FromDocument(string documentJson)
    {
        var byCard = new Dictionary<string, double[]>(StringComparer.Ordinal);
        using var json = JsonDocument.Parse(documentJson);
        if (json.RootElement.TryGetProperty("Cards", out var cards))
            foreach (var card in cards.EnumerateArray())
            {
                var id = card.GetProperty("Id").GetString() ?? "";
                var text = card.GetRawText();
                int Occurrences(string kind)
                {
                    var found = 0;
                    for (var at = text.IndexOf(kind, StringComparison.Ordinal); at >= 0;
                         at = text.IndexOf(kind, at + 1, StringComparison.Ordinal))
                        found++;
                    return found;
                }
                byCard[id] =
                [
                    Occurrences("node.dealDamage"),
                    Occurrences("node.gainBlock") + Occurrences("node.modifyDefensivePool"),
                    Occurrences("node.applyStatus") + Occurrences("node.modifyStatusStacks"),
                    Occurrences("node.drawCards") + Occurrences("node.moveCardToZone"),
                    Occurrences("node.gainResource"),
                ];
            }
        return new CardFeatures(byCard);
    }

    public double[] For(string cardId) => _byCard.TryGetValue(cardId, out var features) ? features : Nothing;
}
