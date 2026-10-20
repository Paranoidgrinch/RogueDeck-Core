namespace RogueDeck.Core.Combat;

// The wand's actions as the player takes them (master §3.2): what each costs, what it asks, what it does — and,
// when it may not be taken, why (Why). The questions go through the fight's own card and option prompts, so a
// replay answers them exactly as the player did.
public static class WandActions
{
    // A card brought back by Second Hand: if it leaves the wand again, it goes back to the Exhaust pile.
    public static readonly TagId RecallMark = new("wand_recalled");

    public static bool Inscribable(CombatDefinitionRegistry registry, WandRules rules, CardInstance card) =>
        registry.CardDefinitions.TryGetValue(card.DefinitionId, out var definition)
        && definition.Wand is not null && !definition.IsAction
        && !definition.Tags.Contains(StandardCombatIds.UnplayableTag)
        && !definition.Tags.Any(rules.UninscribableTags.Contains);

    public static int InscribeCost(WandState wand, WandRules rules) => wand.FreeInscriptionUsed ? rules.InscribeCost : 0;

    // Why an action cannot be taken now, or null when it can.
    public static string? Why(
        CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand, CombatantId bearer,
        CombatantId? target, WandAction action)
    {
        var zones = combat.GetCardZones(bearer);
        switch (action)
        {
            case WandAction.Inscribe:
                if (!wand.FreeSlots.Any())
                    return "The wand is full.";
                if (!zones.Hand.Any(c => Inscribable(registry, rules, c)))
                    return "No card in your hand can be inscribed.";
                if (wand.Focus < InscribeCost(wand, rules))
                    return $"Inscribing costs {InscribeCost(wand, rules)} Focus, you have {wand.Focus}.";
                return null;
            case WandAction.Cast:
                var plan = WandCompiler.Compile(combat, registry, bearer, target);
                if (plan.IsLegal)
                    return null;
                if (WandEngine.Hook(rules, combat.GetCombatant(bearer), WandHook.BrassDividers)
                    && !wand.TurnLatches.Contains(nameof(WandHook.BrassDividers))
                    && Sections(wand).Any(s => WandCompiler.Compile(combat, registry, bearer, target,
                        new WandCompiler.Options(s.Start, s.End)).IsLegal))
                    return null;
                return plan.Illegal;
            case WandAction.Swap:
                if (wand.Occupied < 2)
                    return "Swapping needs two cards in the wand.";
                if (wand.Focus < rules.SwapCost)
                    return $"Swapping costs {rules.SwapCost} Focus, you have {wand.Focus}.";
                return null;
            case WandAction.Withdraw:
                if (wand.Occupied == 0)
                    return "The wand is empty.";
                if (wand.Focus < rules.WithdrawCost)
                    return $"Withdrawing costs {rules.WithdrawCost} Focus, you have {wand.Focus}.";
                return null;
            case WandAction.BurnOut:
                if (wand.Occupied == 0)
                    return "The wand is empty.";
                if (wand.BurnOutUsedThisTurn)
                    return "Burn Out can be used once per turn.";
                if (wand.Exhaustion == 0)
                    return "The wand has no Exhaustion to burn out.";
                return null;
            default:
                _ = target;
                return null;
        }
    }

    // Every run of slots from one occupied slot to another — what Brass Dividers lets the player cast instead.
    public static IEnumerable<(int Start, int End)> Sections(WandState wand)
    {
        var occupied = Enumerable.Range(0, wand.Capacity).Where(i => wand.Slots[i] is not null).ToList();
        foreach (var start in occupied)
            foreach (var end in occupied.Where(e => e >= start))
                if (start != occupied[0] || end != occupied[^1])
                    yield return (start, end);
    }

    private static int Choose(CombatState combat, IReadOnlyList<string> options, string purpose)
    {
        if (options.Count <= 1 || combat.OptionChooser is not { } chooser)
            return 0;
        var picked = chooser.ChooseOptions(options, 1, purpose);
        return picked is { Count: > 0 } && picked[0] >= 0 && picked[0] < options.Count ? picked[0] : 0;
    }

    private static IReadOnlyList<CardInstanceId> ChooseCards(
        CombatState combat, IReadOnlyList<CardInstance> candidates, int count, string purpose)
    {
        if (candidates.Count == 0 || count <= 0)
            return [];
        var fallback = candidates.Take(count).Select(c => c.Id).ToList();
        if (combat.CardChooser is not { } chooser)
            return fallback;
        var picked = chooser.ChooseCards(candidates, count, purpose);
        var clean = picked?.Where(id => candidates.Any(c => c.Id == id)).Distinct().Take(count).ToList() ?? [];
        return clean.Count > 0 ? clean : fallback;
    }

    private static string NameOf(CombatState combat, CombatantId bearer, CardInstanceId card) =>
        combat.DefinitionRegistry?.CardDefinitions.TryGetValue(combat.GetCardZones(bearer).GetCard(card).DefinitionId, out var d) == true
            ? d!.DisplayNameKey
            : card.value;

    private static List<CardInstance> Installed(CombatState combat, WandState wand, CombatantId bearer)
    {
        var zones = combat.GetCardZones(bearer);
        return [.. wand.Slots.OfType<CardInstanceId>().Where(zones.ContainsCard).Select(zones.GetCard)];
    }

    // ── INSCRIBE ───────────────────────────────────────────────────────────────────────────────────────────

    public static void Inscribe(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand,
        CombatantId bearer, bool free, int? intoSlot = null)
    {
        var hand = combat.GetCardZones(bearer).Hand.Where(c => Inscribable(registry, rules, c)).ToList();
        var slots = wand.FreeSlots.ToList();
        if (hand.Count == 0 || slots.Count == 0)
            return;
        var cost = free ? 0 : InscribeCost(wand, rules);
        if (wand.Focus < cost)
            return;

        var card = ChooseCards(combat, hand, 1, "choose a card to inscribe")[0];
        var slot = intoSlot is { } fixedSlot && slots.Contains(fixedSlot)
            ? fixedSlot
            : slots[Choose(combat, [.. slots.Select(s => $"Slot {s + 1}")], "choose a slot")];

        if (!free)
        {
            if (!wand.FreeInscriptionUsed)
                wand.FreeInscriptionUsed = true;
            else
                wand.Focus -= cost;
        }
        WandMoves.Inscribe(combat, wand, bearer, card, slot);

        // Bent Nib: once per turn, the new card may trade places with a neighbour — any card, after a Gust.
        var bearerState = combat.GetCombatant(bearer);
        if (WandEngine.Hook(rules, bearerState, WandHook.BentNib) && !wand.TurnLatches.Contains(nameof(WandHook.BentNib)))
        {
            var partners = Enumerable.Range(0, wand.Capacity)
                .Where(i => i != slot && wand.Slots[i] is not null
                    && (wand.GustResolvedThisTurn > 0 || Math.Abs(i - slot) == 1))
                .ToList();
            if (partners.Count > 0)
            {
                var pick = Choose(combat,
                    [.. partners.Select(i => $"Swap with {NameOf(combat, bearer, wand.Slots[i]!.Value)} (slot {i + 1})").Prepend("Leave it")],
                    "Bent Nib: swap the new card");
                if (pick > 0)
                {
                    WandMoves.Swap(wand, slot, partners[pick - 1]);
                    wand.TryLatchTurn(nameof(WandHook.BentNib));
                }
            }
        }
    }

    // ── SWAP, WITHDRAW, REARRANGE ──────────────────────────────────────────────────────────────────────────

    public static void Swap(CombatState combat, WandRules rules, WandState wand, CombatantId bearer, bool free)
    {
        var installed = Installed(combat, wand, bearer);
        if (installed.Count < 2 || (!free && wand.Focus < rules.SwapCost))
            return;
        var picked = ChooseCards(combat, installed, 2, "choose two wand cards to swap");
        if (picked.Count < 2)
            return;
        if (!free)
            wand.Focus -= rules.SwapCost;
        WandMoves.Swap(wand, wand.SlotOf(picked[0]), wand.SlotOf(picked[1]));
        combat.AddLogEntry(StandardCombatLogTypes.WandChanged, $"'{bearer}' swapped two wand cards.");
    }

    public static void Withdraw(CombatState combat, WandRules rules, WandState wand, CombatantId bearer, bool free, bool toHand)
    {
        var installed = Installed(combat, wand, bearer);
        if (installed.Count == 0 || (!free && wand.Focus < rules.WithdrawCost))
            return;
        var card = ChooseCards(combat, installed, 1, toHand ? "choose a wand card to take back" : "choose a wand card to withdraw")[0];
        if (!free)
            wand.Focus -= rules.WithdrawCost;
        if (toHand)
            WandMoves.ToHand(combat, wand, bearer, card);
        else
            WandMoves.Withdraw(combat, wand, bearer, card);
    }

    // Every installed card picked in its new order, laid back into the occupied slots in that order.
    public static void Rearrange(CombatState combat, WandState wand, CombatantId bearer)
    {
        var installed = Installed(combat, wand, bearer);
        if (installed.Count < 2)
            return;
        var order = ChooseCards(combat, installed, installed.Count, "choose the wand cards in their new order");
        if (order.Count != installed.Count)
            return;
        var slots = Enumerable.Range(0, wand.Capacity).Where(i => wand.Slots[i] is not null).ToList();
        for (var i = 0; i < slots.Count; i++)
            wand.Put(slots[i], order[i]);
        combat.AddLogEntry(StandardCombatLogTypes.WandChanged, $"'{bearer}' rearranged the wand.");
    }

    public static void SpendFocus(CombatState combat, WandState wand, int most)
    {
        var top = Math.Min(most, wand.Focus);
        var spend = top <= 0 ? 0 : Choose(combat, [.. Enumerable.Range(0, top + 1).Select(n => $"Spend {n} Focus")], "how much Focus to spend");
        wand.Focus -= spend;
        wand.FocusSpent = spend;
    }

    // ── CAST ───────────────────────────────────────────────────────────────────────────────────────────────

    public static void Cast(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand,
        CombatantId bearer, CombatantId? target, CombatEffectChainContext? chain, Action<CombatState>? onComplete)
    {
        var options = new WandCompiler.Options();
        var bearerState = combat.GetCombatant(bearer);

        // Brass Dividers: the whole wand, or a run of it.
        var dividers = WandEngine.Hook(rules, bearerState, WandHook.BrassDividers)
            && !wand.TurnLatches.Contains(nameof(WandHook.BrassDividers));
        if (dividers)
        {
            var sections = Sections(wand).ToList();
            if (sections.Count > 0)
            {
                var pick = Choose(combat,
                    [.. sections.Select(s => s.Start == s.End ? $"Only slot {s.Start + 1}" : $"Slots {s.Start + 1}–{s.End + 1}").Prepend("The whole wand")],
                    "Brass Dividers: cast which part");
                if (pick > 0)
                    options = options with { SectionStart = sections[pick - 1].Start, SectionEnd = sections[pick - 1].End };
            }
        }

        var draft = WandCompiler.Compile(combat, registry, bearer, target, options);

        // An Oral Exam in the section: forward or backward.
        if (draft.Steps.FirstOrDefault(s => s.Imprint.Rule == WandRule.OralExam) is { } exam)
            options = options with
            {
                Backward = Choose(combat,
                    ["Read it forward", $"Read it backward (+{exam.Imprint.Amount} Exhaustion)"],
                    "Final Examination: which way") == 1,
            };

        // An Errata reaching a Speak: which installed Speak it borrows, if any.
        draft = WandCompiler.Compile(combat, registry, bearer, target, options);
        if (draft.Steps.FirstOrDefault(s => s.Imprint.Rule == WandRule.Errata && s.Target is not null) is { } errata)
        {
            var speaking = draft.Steps[errata.Target!.Value].Card;
            var zones = combat.GetCardZones(bearer);
            var sources = wand.Slots.OfType<CardInstanceId>()
                .Where(id => id != speaking && zones.ContainsCard(id)
                    && registry.CardDefinitions.TryGetValue(zones.GetCard(id).DefinitionId, out var d)
                    && d.Wand is { Operation: WandOperation.Speak } template
                    && template.Atoms.Any(a => a.Copyable && a.Kind != WandAtomKind.Program))
                .ToList();
            if (sources.Count > 0)
            {
                var pick = Choose(combat,
                    [.. sources.Select(id => $"Speak as {NameOf(combat, bearer, id)}").Prepend("Leave it as written")],
                    "Master's Correction: borrow another Speak");
                if (pick > 0)
                    options = options with { ErrataSource = sources[pick - 1] };
            }
        }

        var plan = WandCompiler.Compile(combat, registry, bearer, target, options);
        if (plan.IsLegal && dividers && options.SectionStart is not null)
            wand.TryLatchTurn(nameof(WandHook.BrassDividers));
        WandCast.Run(combat, registry, plan, chain, onComplete);
    }

    // ── BURN OUT ───────────────────────────────────────────────────────────────────────────────────────────

    public static void BurnOut(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand,
        CombatantId bearer, CombatantId? target, CombatEffectChainContext? chain, Action<CombatState>? onComplete)
    {
        void Done()
        {
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
        }
        if (Why(combat, registry, rules, wand, bearer, target, WandAction.BurnOut) is not null)
        {
            Done();
            return;
        }
        var installed = Installed(combat, wand, bearer);
        var card = ChooseCards(combat, installed, 1, "choose a wand card to burn out")[0];
        var slot = wand.SlotOf(card);
        var zones = combat.GetCardZones(bearer);

        // Second Hand is armed while it sits in the wand — and not by the burning of itself.
        var recaller = installed.FirstOrDefault(c => c.Id != card
            && registry.CardDefinitions.TryGetValue(c.DefinitionId, out var d) && d.Wand?.Rule == WandRule.Recall);
        var recalls = recaller is not null && registry.CardDefinitions[recaller.DefinitionId].Wand is { } recall
            ? Math.Max(1, recall.Amount) - wand.CombatLatches.Count(l => l.StartsWith("recall:", StringComparison.Ordinal))
            : 0;

        WandMoves.Exhaust(combat, wand, bearer, card);
        wand.Exhaustion = Math.Max(0, wand.Exhaustion - rules.BurnOutRelief);
        wand.BurnOutUsedThisTurn = true;
        combat.AddLogEntry(StandardCombatLogTypes.WandChanged, $"'{bearer}' burned out '{card}'.");

        // What waited for a Burn Out: Rubbing's kept spell, Lessons in Ash's trace.
        var released = new List<WandFrozenEffect>();
        CardDefinitionId? source = null;
        if (wand.StoredSpell is { Trigger: WandStoreTrigger.NextBurnOut } kept)
        {
            wand.StoredSpell = null;
            released.AddRange(kept.Effects);
            source = kept.SourceCard;
        }
        if (wand.TakeAshTrace() is { } trace)
            released.Add(new WandFrozenEffect(WandEffectKind.Damage, trace));

        WandCast.Release(combat, registry, bearer, target, released, source, chain, () =>
        {
            Refill(combat, registry, rules, wand, bearer, slot, recalls);
            Done();
        });
        _ = zones;
    }

    // The emptied slot may be filled at once — by Second Hand from the Exhaust pile, or by Ash Bookcord from the
    // hand — but by one of them only; the one not used keeps its charge.
    internal static void Refill(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand,
        CombatantId bearer, int slot, int recalls)
    {
        if (slot < 0 || slot >= wand.Capacity || wand.Slots[slot] is not null)
            return;
        var zones = combat.GetCardZones(bearer);
        if (recalls > 0)
        {
            var exhausted = zones.ExhaustPile.Where(c => Inscribable(registry, rules, c)).ToList();
            if (exhausted.Count > 0)
            {
                var pick = Choose(combat,
                    [.. exhausted.Select(c => $"Recall {NameOf(combat, bearer, c.Id)}").Prepend("Leave the slot")],
                    "Second Hand: recall an exhausted card");
                if (pick > 0)
                {
                    var recalled = exhausted[pick - 1];
                    recalled.AddMark(RecallMark);
                    zones.MoveCardToZone(recalled.Id, CardZone.Wand);
                    wand.Put(slot, recalled.Id);
                    wand.TryLatchCombat($"recall:{wand.CombatLatches.Count}");
                    combat.EnqueueEvent(new CardMovedToZoneCombatEvent(bearer, recalled.Id, CardZone.ExhaustPile, CardZone.Wand));
                    return;
                }
            }
        }
        AshBookcord(combat, registry, rules, wand, bearer, slot);
    }

    internal static void AshBookcord(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand,
        CombatantId bearer, int slot)
    {
        if (!WandEngine.Hook(rules, combat.GetCombatant(bearer), WandHook.AshBookcord)
            || wand.TurnLatches.Contains(nameof(WandHook.AshBookcord)) || wand.Slots[slot] is not null)
            return;
        var hand = combat.GetCardZones(bearer).Hand.Where(c => Inscribable(registry, rules, c)).ToList();
        if (hand.Count == 0)
            return;
        var pick = Choose(combat,
            [.. hand.Select(c => $"Inscribe {NameOf(combat, bearer, c.Id)}").Prepend("Leave the slot")],
            "Ash Bookcord: fill the burned slot");
        if (pick == 0)
            return;
        wand.TryLatchTurn(nameof(WandHook.AshBookcord));
        WandMoves.Inscribe(combat, wand, bearer, hand[pick - 1].Id, slot);
    }
}

// GUST (§2): at the start of the bearer's turn, after the normal draw — draw X, then choose and discard X, and the X
// Gust is spent. Feather Catcher lets one of the cards chosen for the discard go into a free slot instead.
internal static class WandGust
{
    public static void Resolve(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, CombatantId bearer,
        StatusDefinitionId gust)
    {
        if (!combat.TryGetCombatant(bearer, out var holder) || !holder!.IsAlive || combat.GetWand(bearer) is not { } wand)
            return;
        var stacks = WandEngine.Stacks(holder, gust);
        if (stacks <= 0)
            return;
        combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(bearer, gust, -stacks));
        combat.EnqueueEffect(new DrawCardsEffectRequest(bearer, stacks));
        combat.EnqueueContinuation(c =>
        {
            var zones = c.GetCardZones(bearer);
            var count = Math.Min(stacks, zones.Hand.Count);
            wand.GustResolvedThisTurn = stacks;
            if (count == 0)
                return;
            var hand = zones.Hand.ToList();
            var fallback = hand.Take(count).Select(h => h.Id).ToList();
            var chosen = c.CardChooser?.ChooseCards(hand, count, $"Gust: discard {count}")?
                .Where(id => hand.Any(h => h.Id == id)).Distinct().Take(count).ToList() is { Count: > 0 } picked
                && picked.Count == count ? picked : fallback;

            if (WandEngine.Hook(rules, holder, WandHook.FeatherCatcher) && wand.FreeSlots.Any()
                && !wand.TurnLatches.Contains(nameof(WandHook.FeatherCatcher)))
            {
                var catchable = chosen.Where(id => WandActions.Inscribable(registry, rules, zones.GetCard(id))).ToList();
                if (catchable.Count > 0 && c.OptionChooser is { } chooser)
                {
                    var labels = catchable.Select(id => $"Inscribe {registry.CardDefinitions[zones.GetCard(id).DefinitionId].DisplayNameKey}")
                        .Prepend("Discard them all").ToList();
                    var answer = chooser.ChooseOptions(labels, 1, "Feather Catcher: catch one for the wand");
                    var pick = answer is { Count: > 0 } && answer[0] > 0 && answer[0] < labels.Count ? answer[0] : 0;
                    if (pick > 0)
                    {
                        wand.TryLatchTurn(nameof(WandHook.FeatherCatcher));
                        WandMoves.Inscribe(c, wand, bearer, catchable[pick - 1], wand.FreeSlots.First());
                        chosen.Remove(catchable[pick - 1]);
                    }
                }
            }
            foreach (var id in chosen)
                c.EnqueueEffect(new MoveCardToZoneEffectRequest(bearer, id, CardZone.DiscardPile));
        });
    }
}
