using System.Text.Json;

namespace RogueDeck.Bot;

// ── WHAT A CARD (OR A RELIC) IS WORTH, IN THE GAME'S OWN NUMBERS ─────────────────────────────────────────
// Five buckets — damage, guard, status, cards, resources — filled by WALKING the authored program and adding
// up the amounts it actually applies. A policy weighs the five; the dice player never asks.
//
// ⚠⚠ THIS USED TO COUNT WORDS. Until B3 a card's damage was "how often the text `node.dealDamage` occurs in
// its JSON", so a 3-damage jab and a 30-damage haymaker scored identically, and B1 measured what that costs:
// a runner that picks greedily on a blind evaluator builds a WORSE deck than one that picks at random,
// because it fills the deck with whatever has the most effect nodes. The evaluator is now a function of the
// GAME rather than of the document's spelling.
//
// ⚠ IT IS STILL A HEURISTIC, and the three places it has to guess are named rather than hidden:
//   · a branch that may or may not be taken (`MaybeRuns`),
//   · an amount the document cannot resolve without a fight in front of it — "damage equal to your Seal
//     stacks" (`ScalingTerm`),
//   · how many cards a zone holds when a card acts on all of them (`CardsInAZone`).
// Everything else is read: constants are read, arithmetic over constants is folded, a repeat multiplies by
// its own count, a "choose N of M" scales by N/M, and a spell aimed at every enemy multiplies by how many
// enemies THIS GAME'S encounters actually have.
//
// ⚠⚠ THE NUMBERS ARE NORMALIZED BY THE GAME'S OWN AVERAGE CARD, per bucket, counting only the cards that do
// that thing at all. Raw points would put damage (tens) and cards drawn (ones) on scales that differ by an
// order of magnitude, and every weight bred for the old counting would mean something wildly different. In
// these units, 1.0 = "what a card of this game that blocks, blocks" — and the weights still have to be bred
// again, because within a bucket the spread is now real.
public sealed class CardFeatures
{
    public const int Count = 5;

    // The three numbers the document does not supply.
    private const double MaybeRuns = 0.5;      // a conditional branch, with nothing to say which way it goes
    private const double ScalingTerm = 3.0;    // an amount that depends on the fight, not on the card
    private const double CardsInAZone = 4.0;   // "for each card in your hand" — the one honest guess left

    private static readonly double[] Nothing = new double[Count];

    private readonly Dictionary<string, double[]> _byCard;
    private readonly Dictionary<string, double[]> _byRelic;

    private CardFeatures(Dictionary<string, double[]> byCard, Dictionary<string, double[]> byRelic)
    {
        _byCard = byCard;
        _byRelic = byRelic;
    }

    public static CardFeatures FromDocument(string documentJson)
    {
        using var json = JsonDocument.Parse(documentJson);
        var root = json.RootElement;
        var enemies = EnemiesInAFight(root);
        var cards = Read(root, "Cards", enemies);
        var relics = Read(root, "Relics", enemies);
        // Both are measured against the average CARD, because a relic's worth is only ever compared with
        // another relic's — and one scale is easier to read in a log than two.
        var scale = AverageCard(cards);
        foreach (var features in cards.Values.Concat(relics.Values))
            for (var bucket = 0; bucket < Count; bucket++)
                features[bucket] /= scale[bucket];
        return new CardFeatures(cards, relics);
    }

    public double[] For(string cardId) => _byCard.TryGetValue(cardId, out var features) ? features : Nothing;

    public double[] ForRelic(string relicId) =>
        _byRelic.TryGetValue(relicId, out var features) ? features : Nothing;

    // ── Reading the document ─────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, double[]> Read(JsonElement root, string collection, double enemies)
    {
        var byId = new Dictionary<string, double[]>(StringComparer.Ordinal);
        if (!root.TryGetProperty(collection, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return byId;
        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.TryGetProperty("Id", out var name) ? name.GetString() ?? "" : "";
            var features = new double[Count];
            Walk(entry, weight: 1, features, enemies);
            byId[id] = features;
        }
        return byId;
    }

    // How many things a spell aimed at "every enemy" actually hits, asked of the encounters this game ships
    // rather than assumed. A game of duels answers 1; a game of mobs answers 4.
    private static double EnemiesInAFight(JsonElement root)
    {
        if (!root.TryGetProperty("Encounters", out var encounters) || encounters.ValueKind != JsonValueKind.Array)
            return 1;
        double fights = 0, enemies = 0;
        foreach (var encounter in encounters.EnumerateArray())
        {
            if (!encounter.TryGetProperty("Enemies", out var side) || side.ValueKind != JsonValueKind.Array)
                continue;
            fights++;
            enemies += side.GetArrayLength();
        }
        return fights == 0 ? 1 : Math.Max(1, enemies / fights);
    }

    // The average card THAT DOES THIS, bucket by bucket — never zero, so a game with no healing does not
    // divide by it.
    //
    // ⚠⚠ THE CARDS THAT DO NOTHING IN A BUCKET ARE NOT AVERAGED INTO IT, and the first version of this
    // method is why the warning is here. Averaging over ALL cards divides a bucket by all the zeroes in it,
    // so the RARER an effect is in a game the larger every instance of it scores: block, which about a fifth
    // of this game's cards give, came out 2.5× too big against damage. A runner built from it blocked for a
    // hundred turns in the first fight of the game and never killed anything. What is compared here is
    // "5 block against the block cards" with "6 damage against the damage cards" — two questions of the same
    // shape — and never "how unusual is it to block at all".
    private static double[] AverageCard(Dictionary<string, double[]> cards)
    {
        var scale = new double[Count];
        var doing = new int[Count];
        foreach (var features in cards.Values)
            for (var bucket = 0; bucket < Count; bucket++)
                if (features[bucket] > 0)
                {
                    scale[bucket] += features[bucket];
                    doing[bucket]++;
                }
        for (var bucket = 0; bucket < Count; bucket++)
            scale[bucket] = doing[bucket] == 0 || scale[bucket] <= 0 ? 1 : scale[bucket] / doing[bucket];
        return scale;
    }

    // ── Walking a program ────────────────────────────────────────────────────────────────────────────────
    // Every authored node is `{ "kind": "…", "value": { … } }`. A node this does not know is not skipped: its
    // fields are walked anyway, so a container the engine grows later still yields up what is inside it, and
    // a relic's program — buried in the rule it installs when a fight opens — is found without being named.
    private static void Walk(JsonElement node, double weight, double[] into, double enemies)
    {
        if (weight <= 0)
            return;

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                Walk(child, weight, into, enemies);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object)
            return;

        var kind = node.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (kind is null || !node.TryGetProperty("value", out var body) || body.ValueKind != JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
                Walk(property.Value, weight, into, enemies);
            return;
        }

        switch (kind)
        {
            // ── What a card DOES ──────────────────────────────────────────────────────────────────────────
            case "node.dealDamage":
                into[0] += weight * Amount(body, "Amount") * Targets(body, "TargetSelector", enemies);
                return;
            case "fx.damage":
                into[0] += weight * Amount(body, "Amount");
                return;

            case "node.gainBlock":
                into[1] += weight * Amount(body, "Amount") * Targets(body, "TargetSelector", enemies);
                return;
            // A pool moved either way is a pool being fought over: a card that strips three points of an
            // enemy's guard does as much guard-work as one that raises three of its own.
            case "node.modifyDefensivePool":
                into[1] += weight * Math.Abs(Amount(body, "Delta"));
                return;

            case "node.applyStatus":
                into[2] += weight * Amount(body, "Stacks") * Targets(body, "TargetSelector", enemies);
                return;
            case "node.modifyStatusStacks":
            case "node.modifySelectedStatusStacks":
            case "node.modifyStatusDuration":
                into[2] += weight * Math.Abs(Amount(body, "Delta")) * Targets(body, "TargetSelector", enemies);
                return;
            case "node.removeStatus":
                into[2] += weight * Targets(body, "TargetSelector", enemies);
                return;

            case "node.drawCards":
                into[3] += weight * Amount(body, "Count");
                return;
            case "node.createCardInstance":
            case "node.createCardCopy":
                into[3] += weight * Math.Max(1, Amount(body, "Count"));
                return;
            case "node.moveCardToZone":
            case "node.queueCard":
            case "node.markCardInstance":
            case "fx.upgradeCards":
                into[3] += weight;
                return;

            case "node.gainResource":
                into[4] += weight * Amount(body, "Amount");
                return;
            case "node.modifyResource":
            case "fx.changeResource":
            case "fx.computedResource":
            case "fx.incrementCounter":
                into[4] += weight * Amount(body, "Delta") + weight * Amount(body, "Amount");
                return;
            case "node.loseResource":
                into[4] -= weight * Amount(body, "Amount");
                return;
            // Health is a resource a run spends, and a relic that hands some back is doing the same work as
            // one that hands back gold.
            case "fx.heal":
            case "fx.computedHeal":
            case "fx.changeMaxHealth":
                into[4] += weight * (Amount(body, "Amount") + Amount(body, "Delta"));
                return;

            // ── What a card's SHAPE does to all of the above ──────────────────────────────────────────────
            case "node.repeat":
                Walk(Child(body, "Body"), weight * Math.Max(1, Amount(body, "Count")), into, enemies);
                return;
            case "node.forEachTarget":
                Walk(Child(body, "Body"), weight * Targets(body, "CollectionSelector", enemies), into, enemies);
                return;
            case "node.forEachCardInZone":
                Walk(Child(body, "Body"), weight * CardsInAZone, into, enemies);
                return;
            case "node.conditional":
                Walk(Child(body, "Then"), weight * MaybeRuns, into, enemies);
                Walk(Child(body, "Else"), weight * MaybeRuns, into, enemies);
                return;
            case "fx.conditional":
                Walk(Child(body, "WhenTrue"), weight * MaybeRuns, into, enemies);
                Walk(Child(body, "WhenFalse"), weight * MaybeRuns, into, enemies);
                return;
            // "Choose 1 of 3" runs a third of what is written, and the document says both numbers.
            case "node.chooseOptions":
                {
                    var children = Child(body, "Children");
                    var offered = children.ValueKind == JsonValueKind.Array ? children.GetArrayLength() : 0;
                    var taken = Math.Max(1, Amount(body, "Count"));
                    Walk(children, offered == 0 ? weight : weight * taken / offered, into, enemies);
                    return;
                }

            default:
                foreach (var property in body.EnumerateObject())
                    Walk(property.Value, weight, into, enemies);
                return;
        }
    }

    private static JsonElement Child(JsonElement body, string field) =>
        body.TryGetProperty(field, out var child) ? child : default;

    // An authored amount: a plain number, a constant, arithmetic over constants — or something that cannot be
    // known without a fight in front of it, which is worth a named guess rather than nothing.
    private static double Amount(JsonElement body, string field) =>
        body.TryGetProperty(field, out var amount) ? Amount(amount) : 0;

    private static double Amount(JsonElement amount)
    {
        if (amount.ValueKind == JsonValueKind.Number)
            return amount.GetDouble();
        if (amount.ValueKind != JsonValueKind.Object)
            return 0;
        var kind = amount.TryGetProperty("kind", out var k) ? k.GetString() : null;
        if (kind is null || !amount.TryGetProperty("value", out var body))
            return 0;

        return kind switch
        {
            "const" => Amount(body, "Value"),
            "add" => Amount(body, "Left") + Amount(body, "Right"),
            "subtract" => Amount(body, "Left") - Amount(body, "Right"),
            "multiply" => Amount(body, "Left") * Amount(body, "Right"),
            "min" => Math.Min(Amount(body, "Left"), Amount(body, "Right")),
            "max" => Math.Max(Amount(body, "Left"), Amount(body, "Right")),
            "negate" => -Amount(body, "Operand"),
            "divide" => Amount(body, "Divisor") is var by && by != 0
                ? Amount(body, "Dividend") / by
                : 0,
            _ => ScalingTerm,
        };
    }

    // How many things a selector picks out. Only the ones that name a CROWD answer with more than one — and
    // they answer with this game's own crowd, not with a number somebody typed here.
    private static double Targets(JsonElement body, string field, double enemies) =>
        body.TryGetProperty(field, out var selector)
        && selector.ValueKind == JsonValueKind.Object
        && selector.TryGetProperty("kind", out var kind)
            ? kind.GetString() switch
            {
                "sel.allEnemies" or "sel.allCombatants" or "sel.enemiesWithStatus" or "sel.withStatus" => enemies,
                _ => 1,
            }
            : 1;
}
