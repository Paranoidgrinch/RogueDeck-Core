using RogueDeck.Core.Combat;

namespace RogueDeck.Bot;

// HOW THE BOT HANDLES A WAND. It takes a hero's actions in the order the character lists them — which for a wand
// would inscribe, then withdraw what it inscribed, then swap it about, all for Focus. So a wand's actions are
// ordered by what they are for: cast whenever the wand can be cast; burn out only when the wand is nearly at its
// cap; inscribe while the wand is nearly empty or the combat's free inscription is still there; never swap or
// withdraw on its own. Every other action keeps its place. A plain walker's sense, not a planner's.
public static class WandBotPolicy
{
    public static IEnumerable<CardDefinitionId> Order(
        CombatDefinitionRegistry registry, CombatState combat, CombatantId bearer, IEnumerable<CardDefinitionId> actions)
    {
        var ranked = new List<(int Rank, int Index, CardDefinitionId Action)>();
        var index = 0;
        foreach (var action in actions)
        {
            var rank = Rank(registry, combat, bearer, action);
            if (rank >= 0)
                ranked.Add((rank, index, action));
            index++;
        }
        return ranked.OrderBy(r => r.Rank).ThenBy(r => r.Index).Select(r => r.Action);
    }

    // -1: not now. Lower runs first.
    private static int Rank(CombatDefinitionRegistry registry, CombatState combat, CombatantId bearer, CardDefinitionId action)
    {
        if (!registry.CardDefinitions.TryGetValue(action, out var definition)
            || definition.Program?.Root is not IWandNodeCore wandNode)
            return 10;
        var wand = combat.GetWand(bearer);
        return wandNode.Action switch
        {
            WandAction.Cast => 0,
            WandAction.BurnOut => wand is not null && wand.Exhaustion >= wand.ExhaustionCap - 2 ? 1 : -1,
            WandAction.Inscribe => wand is null || !wand.FreeInscriptionUsed || wand.Occupied < 2 ? 2 : -1,
            _ => -1,
        };
    }
}
