namespace RogueDeck.Core.Combat;

// Every way a card gets into or out of a wand, or moves inside it. Each move is made at once — the slot a card
// takes must be known before the next instruction reads the wand — and each card that crosses a zone line raises
// the ordinary CardMovedToZone event, so the rules that watch a card leave the hand without being played, or
// watch a card be exhausted, hear it exactly as they hear any other move.
public static class WandMoves
{
    // A card from the hand into a free slot. Not a play; nothing that counts plays hears of it.
    public static void Inscribe(CombatState combat, WandState wand, CombatantId bearer, CardInstanceId card, int slot)
    {
        var zones = combat.GetCardZones(bearer);
        var from = zones.GetCard(card).Zone;
        zones.MoveCardToZone(card, CardZone.Wand);
        wand.Put(slot, card);
        wand.InscribedThisTurn++;
        wand.InscribedSinceCast++;
        combat.AddLogEntry(StandardCombatLogTypes.WandChanged, $"'{bearer}' inscribed '{card}' into slot {slot + 1}.");
        combat.EnqueueEvent(new CardMovedToZoneCombatEvent(bearer, card, from, CardZone.Wand));
    }

    // Burned or burned out: to the Exhaust pile, its slot empty.
    public static void Exhaust(CombatState combat, WandState wand, CombatantId bearer, CardInstanceId card) =>
        Leave(combat, wand, bearer, card, CardZone.ExhaustPile, burned: true);

    // Withdrawn: to the discard pile — or, a fight-only copy, to the Exhaust pile.
    public static void Withdraw(CombatState combat, WandState wand, CombatantId bearer, CardInstanceId card) =>
        Leave(combat, wand, bearer, card, IsTemporary(combat, bearer, card) ? CardZone.ExhaustPile : CardZone.DiscardPile, burned: false);

    // Back to the hand (Knotted Wristcord, Torn Flyleaf).
    public static void ToHand(CombatState combat, WandState wand, CombatantId bearer, CardInstanceId card) =>
        Leave(combat, wand, bearer, card, CardZone.Hand, burned: false);

    private static void Leave(CombatState combat, WandState wand, CombatantId bearer, CardInstanceId card, CardZone to, bool burned)
    {
        var zones = combat.GetCardZones(bearer);
        wand.Clear(card);
        if (!zones.ContainsCard(card) || zones.GetCard(card).Zone != CardZone.Wand)
            return;
        // A card Second Hand brought back goes back to the Exhaust pile, wherever it was headed.
        if (zones.GetCard(card).HasMark(WandActions.RecallMark))
            to = CardZone.ExhaustPile;
        zones.MoveCardToZone(card, to);
        if (burned)
            wand.WandCardsExhausted++;
        combat.AddLogEntry(StandardCombatLogTypes.WandChanged, $"'{card}' left the wand for {to}.");
        combat.EnqueueEvent(new CardMovedToZoneCombatEvent(bearer, card, CardZone.Wand, to));
    }

    public static void Swap(WandState wand, int first, int second)
    {
        var a = wand.Slots[first];
        wand.Put(first, wand.Slots[second]);
        wand.Put(second, a);
    }

    // Into the nearest free slot to its right (or on either side), if there is one. True when it moved.
    public static bool Slip(WandState wand, CardInstanceId card, bool bothWays)
    {
        var at = wand.SlotOf(card);
        if (at < 0)
            return false;
        for (var distance = 1; distance < wand.Capacity; distance++)
        {
            foreach (var to in bothWays ? new[] { at + distance, at - distance } : [at + distance])
                if (to >= 0 && to < wand.Capacity && wand.Slots[to] is null)
                {
                    wand.Put(to, card);
                    wand.Put(at, null);
                    return true;
                }
        }
        return false;
    }

    // The installed cards each move one OCCUPIED place on (gaps stay where they are).
    public static void Rotate(WandState wand, bool left)
    {
        var occupied = Enumerable.Range(0, wand.Capacity).Where(i => wand.Slots[i] is not null).ToList();
        if (occupied.Count < 2)
            return;
        var cards = occupied.Select(i => wand.Slots[i]).ToList();
        if (left)
            cards = [.. cards.Skip(1), cards[0]];
        else
            cards = [cards[^1], .. cards.Take(cards.Count - 1)];
        for (var i = 0; i < occupied.Count; i++)
            wand.Put(occupied[i], cards[i]);
    }

    public static bool IsTemporary(CombatState combat, CombatantId bearer, CardInstanceId card)
    {
        if (combat.GetCardZones(bearer) is { } own && own.ContainsCard(card) && own.GetCard(card).HasMark(StandardCombatIds.TemporaryCopyMark))
            return true;
        if (combat.DefinitionRegistry?.Wand?.TemporaryTag is not { } tag)
            return false;
        var zones = combat.GetCardZones(bearer);
        return zones.ContainsCard(card)
            && combat.DefinitionRegistry.CardDefinitions.TryGetValue(zones.GetCard(card).DefinitionId, out var definition)
            && definition.Tags.Contains(tag);
    }
}
