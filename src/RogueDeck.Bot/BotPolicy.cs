using System.Text.Json;

namespace RogueDeck.Bot;

// ── the trained runner ───────────────────────────────────────────────────────────────────────────────────
// A policy is what makes one runner different from another: a handful of weights that decide which card is
// worth playing, when a turn is over, which enemy to hit, which way to walk and what to buy. No policy = the
// dice player. `--policy <file.json>` loads one; `tools/train.py` breeds them. The fitness they are bred for
// is the balance question itself: starting at 9999 hp, how much health does the whole game take off a runner
// on its way to a named act's boss?
public sealed class BotPolicy
{
    public string Name { get; set; } = "unnamed";

    // What a card is worth, per thing its program does (counted once from the document).
    public double WDamage { get; set; }
    public double WBlock { get; set; }
    public double WStatus { get; set; }
    public double WDraw { get; set; }
    public double WResource { get; set; }
    public double WCost { get; set; }
    public double EndTurnBelow { get; set; }      // a hand whose best card scores under this is done
    public double TargetLowestHp { get; set; }    // 1 = finish the weakest, 0 = hit the strongest

    // Which room to walk into, by the role the map generated it for.
    public double PathCombat { get; set; }
    public double PathElite { get; set; }
    public double PathShop { get; set; }
    public double PathRest { get; set; }
    public double PathEvent { get; set; }
    public double PathTreasure { get; set; }

    // ⚠ THE CHAMPION'S ONLY KNOB (B5). Its lookahead needs one trade-off and no more: 0 plays to survive the
    // turn it can see, 1 plays to empty the enemy. Everything else it would otherwise be told — what a card
    // is worth, when a turn is done, whom to hit — it works out by playing the card and looking.
    public double Aggression { get; set; } = 0.5;

    // Below this share of full health, a door that heals is taken over anything else it is offered beside.
    // ⚠ It exists because a rest site says "leave" like a shop does, and for the whole history of this runner
    // that was enough to walk it straight back out again — see the note in BotMind.PickChoice.
    public double RestBelow { get; set; } = 0.7;

    public double RewardSkip { get; set; }        // > 0.5: decline what may be declined
    public double ShopBuy { get; set; }           // how eagerly gold is spent
    public double EventLate { get; set; }         // 0 = always the first door, 1 = always the last

    public static BotPolicy? FromJson(string json) =>
        JsonSerializer.Deserialize<BotPolicy>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    public static BotPolicy? Load(string path) =>
        File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
}
