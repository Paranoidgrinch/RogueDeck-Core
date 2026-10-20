namespace RogueDeck.Core.Combat;

// THE CAST, BEFORE IT HAPPENS (Sorcerer's Apprentice master §3.3, steps 1–3). Reading the wand's occupied slots
// into a finite, non-recursive program: which instructions run and in what order (after every Order and Stop),
// which Speak each modifier reaches and which modifier finds nothing (FIZZLE), what the cast costs in Focus, what
// it does to Exhaustion, and — when it may not happen — exactly why. Pure: it reads the fight and changes nothing,
// so a host can show it as the preview and the cast itself executes the very plan the player was shown.
//
// The numbers a Speak produces are NOT settled here: they are read when the Speak resolves (WandCast), against the
// fight as it then stands — a Singe spent by an earlier hit, an enemy that died. What is settled here is structure.
public static class WandCompiler
{
    // What the player picked before the cast: a run of slots (Brass Dividers), the reading direction (an Oral
    // Exam in the section), which installed Speak an Errata borrows.
    public sealed record Options(int? SectionStart = null, int? SectionEnd = null, bool Backward = false,
        CardInstanceId? ErrataSource = null);

    public static WandPlan Compile(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId bearer, CombatantId? target,
        Options? options = null)
    {
        options ??= new Options();
        var rules = registry.Wand;
        var wand = combat.GetWand(bearer);
        if (rules is null || wand is null)
            return WandPlan.Refused(bearer, "There is no wand to cast.");

        var start = Math.Clamp(options.SectionStart ?? 0, 0, wand.Capacity - 1);
        var end = Math.Clamp(options.SectionEnd ?? wand.Capacity - 1, start, wand.Capacity - 1);
        var full = start == 0 && end == wand.Capacity - 1;
        var zones = combat.GetCardZones(bearer);

        var read = new List<WandStep>();
        for (var slot = start; slot <= end; slot++)
            if (wand.Slots[slot] is { } id && zones.ContainsCard(id)
                && registry.CardDefinitions.TryGetValue(zones.GetCard(id).DefinitionId, out var definition))
                read.Add(new WandStep(slot, id, definition, definition.Wand ?? Inert));

        var plan = new WandPlan(bearer, target, start, end, full);
        if (read.Count == 0)
            return plan.Refuse("The wand has nothing in it to cast.");

        // An Oral Exam in the section lets the player read it backwards, at a price.
        if (options.Backward && read.FirstOrDefault(s => s.Imprint.Rule == WandRule.OralExam) is { } exam)
        {
            read.Reverse();
            plan.Backward = true;
            plan.Strain += exam.Imprint.Amount;
        }

        // ── the order the instructions actually run in ─────────────────────────────────────────────────────
        var waiting = new List<WandStep>(read);
        while (waiting.Count > 0)
        {
            var step = waiting[0];
            waiting.RemoveAt(0);
            step.Index = plan.Steps.Count;
            plan.Steps.Add(step);
            switch (step.Imprint.Rule)
            {
                case WandRule.SwapNext when step.Imprint.Operation == WandOperation.Order:
                    if (waiting.Count >= 2)
                    {
                        (waiting[0], waiting[1]) = (waiting[1], waiting[0]);
                        step.Swapped = true;
                    }
                    else
                    {
                        step.Fizzle("Nothing left to swap.");
                    }
                    break;
                case WandRule.Reverse:
                    if (waiting.Count >= 1)
                    {
                        if (waiting.Count >= 2)
                            step.Ground += step.Imprint.Amount;
                        waiting.Reverse();
                    }
                    else
                    {
                        step.Fizzle("Nothing left to reverse.");
                    }
                    break;
                case WandRule.Plain when step.Imprint.Operation == WandOperation.Stop:
                    if (waiting.Count > 0)
                        plan.Skipped.AddRange(waiting.Select(w => w.Slot));
                    waiting.Clear();
                    break;
                case WandRule.FinalWord when waiting.Count > 0:
                    step.Fizzle("The Last Word must be the last instruction that runs.");
                    break;
            }
        }

        // Crossing an empty slot between two executed ones (Bone Spacer reads this).
        for (var i = 1; i < plan.Steps.Count; i++)
            if (Math.Abs(plan.Steps[i].Slot - plan.Steps[i - 1].Slot) > 1)
            {
                plan.GapAfter ??= i - 1;
                plan.GapCrossed = true;
            }

        plan.Executed = plan.Steps.Count;
        plan.FocusCost = rules.FocusCostFor(plan.Executed);

        // A boost waiting for this cast (Glass Rod).
        var bearerState = combat.GetCombatant(bearer);
        foreach (var boost in rules.CastBoosts.Where(b => bearerState.Statuses.Any(s => s.DefinitionId == b.Status)))
        {
            plan.Boosts.Add(boost);
            plan.Strain += boost.Strain;
        }

        Bind(plan, combat, registry, wand, rules, target, options);
        Settle(plan, combat, registry, wand, rules, target);
        return plan;
    }

    private static readonly WandImprint Inert = new() { Operation = WandOperation.Speak };

    // ── which Speak each modifier reaches ──────────────────────────────────────────────────────────────────

    private static void Bind(
        WandPlan plan, CombatState combat, CombatDefinitionRegistry registry, WandState wand, WandRules rules,
        CombatantId? target, Options options)
    {
        var bearer = combat.GetCombatant(plan.Bearer);
        var bone = Hook(rules, bearer, WandHook.BoneSpacer) && plan.GapCrossed
            && !wand.TurnLatches.Contains(nameof(WandHook.BoneSpacer));
        var pending = new List<WandStep>();
        var speaks = new List<WandStep>();
        var focusLeft = wand.Focus - plan.FocusCost;

        for (var i = 0; i < plan.Steps.Count; i++)
        {
            var step = plan.Steps[i];
            if (step.Fizzled)
                continue;
            var imprint = step.Imprint;

            if (imprint.Operation == WandOperation.Speak)
            {
                step.Kind = KindOf(imprint.Atoms, registry);
                // Bone Spacer: the first Speak after the gap is heightened.
                if (bone && plan.GapAfter is { } gap && i > gap && step.Kind != WandSpeakKind.Utility)
                {
                    step.BoneSpacer = true;
                    plan.Strain += rules.BoneSpacerExhaustion;
                    bone = false;
                }
                foreach (var modifier in pending.ToList())
                    if (Accepts(modifier, step, plan.Steps, registry, focusLeft))
                    {
                        modifier.Target = step.Index;
                        step.Modifiers.Add(modifier.Index);
                        pending.Remove(modifier);
                    }
                speaks.Add(step);
                continue;
            }

            switch (imprint.Operation, imprint.Rule)
            {
                case (WandOperation.Shape, WandRule.Join):
                    if (i + 2 < plan.Steps.Count
                        && plan.Steps[i + 1].Imprint.Operation == WandOperation.Speak
                        && plan.Steps[i + 2].Imprint.Operation == WandOperation.Speak
                        && KindOf(plan.Steps[i + 1].Imprint.Atoms, registry) == WandSpeakKind.Damage
                        && KindOf(plan.Steps[i + 2].Imprint.Atoms, registry) == WandSpeakKind.Damage)
                    {
                        step.Target = i + 1;
                        step.Partner = i + 2;
                        plan.Steps[i + 1].JoinedInto = null;
                        plan.Steps[i + 2].JoinedInto = i + 1;
                        plan.Steps[i + 1].Modifiers.Add(step.Index);
                    }
                    else
                    {
                        step.Fizzle("Join needs the next two instructions to be damaging Speaks.");
                    }
                    break;

                case (WandOperation.Echo, _):
                case (WandOperation.Keep, WandRule.StoreLast):
                    if (speaks.LastOrDefault(s => !s.Fizzled && s.JoinedInto is null
                            && FilterAccepts(imprint.Filter, s.Kind) && HasCopyable(s.Imprint.Atoms)) is { } source)
                        step.Source = source.Index;
                    else if (imprint.Fallback.Count == 0)
                        step.Fizzle("There is no earlier Speak to copy.");
                    break;

                case (WandOperation.Heighten, _):
                case (WandOperation.Shape, _):
                case (WandOperation.Clause, WandRule.BonusHit):
                case (WandOperation.Clause, WandRule.FinalBreath):
                case (WandOperation.Keep, WandRule.Plain):
                case (WandOperation.Order, WandRule.Errata):
                    pending.Add(step);
                    break;
            }
        }

        // A modifier with nothing after it to act on fizzles — unless it has something of its own to do instead.
        foreach (var modifier in pending)
            if (modifier.Imprint.Fallback.Count == 0)
                modifier.Fizzle(modifier.Imprint.Operation == WandOperation.Clause && modifier.Imprint.Rule == WandRule.FinalBreath
                    ? "No single-hit damaging Speak follows."
                    : "No eligible Speak follows.");

        // An Errata borrows another installed card's Speak — the one the player picked, or none.
        foreach (var errata in plan.Steps.Where(s => s.Imprint.Rule == WandRule.Errata && s.Target is not null))
        {
            var zones = combat.GetCardZones(plan.Bearer);
            if (options.ErrataSource is { } chosen && wand.Holds(chosen) && zones.ContainsCard(chosen)
                && chosen != plan.Steps[errata.Target!.Value].Card
                && registry.CardDefinitions.TryGetValue(zones.GetCard(chosen).DefinitionId, out var borrowed)
                && borrowed.Wand is { Operation: WandOperation.Speak } template && HasCopyable(template.Atoms))
                plan.Steps[errata.Target.Value].Borrowed = (chosen, borrowed);
        }

        // Kinds of the speaks that ended up borrowing.
        foreach (var step in plan.Steps.Where(s => s.Borrowed is not null))
            step.Kind = KindOf(step.Borrowed!.Value.Definition.Wand!.Atoms, registry);

        _ = target;
    }

    // What kind of Speak this is, for every "different kind" and "damage Speak" test: damage if it deals any,
    // else defence if it gives Block or Ward Wax, else status if it applies one, else utility.
    public static WandSpeakKind KindOf(IReadOnlyList<WandAtom> atoms, CombatDefinitionRegistry registry)
    {
        if (atoms.Any(a => a.Kind == WandAtomKind.Damage))
            return WandSpeakKind.Damage;
        if (atoms.Any(a => a.Kind is WandAtomKind.Block or WandAtomKind.WardWax))
            return WandSpeakKind.Defense;
        if (atoms.Any(a => a.Kind == WandAtomKind.Status))
            return WandSpeakKind.Status;
        _ = registry;
        return WandSpeakKind.Utility;
    }

    private static bool HasCopyable(IReadOnlyList<WandAtom> atoms) =>
        atoms.Any(a => a.Copyable && a.Kind != WandAtomKind.Program);

    private static bool FilterAccepts(WandFilter filter, WandSpeakKind kind) => filter switch
    {
        WandFilter.Damage => kind == WandSpeakKind.Damage,
        WandFilter.Defense => kind == WandSpeakKind.Defense,
        WandFilter.DamageOrDefense => kind is WandSpeakKind.Damage or WandSpeakKind.Defense,
        WandFilter.NegativeStatus => kind != WandSpeakKind.Utility,
        _ => kind != WandSpeakKind.Utility,
    };

    private static bool Negative(WandAtom atom, CombatDefinitionRegistry registry) =>
        atom.Kind == WandAtomKind.Status && atom.Targeting != WandTargeting.Self && atom.Status is { } status
        && registry.StatusDefinitions.TryGetValue(status, out var definition)
        && definition.Polarity == StatusPolarity.Debuff;

    // The hits a Speak will have when it resolves, given the shapes already bound to it.
    private static int HitsOf(WandStep speak, IReadOnlyList<WandStep> steps)
    {
        var hits = speak.Imprint.Atoms.Where(a => a.Kind == WandAtomKind.Damage).Sum(a => Math.Max(1, a.Hits));
        foreach (var index in speak.Modifiers)
            if (steps[index].Imprint.Rule == WandRule.Fork)
                hits = hits - 1 + Math.Max(2, steps[index].Imprint.Amount);
        return hits;
    }

    private static bool Accepts(
        WandStep modifier, WandStep speak, IReadOnlyList<WandStep> steps, CombatDefinitionRegistry registry, int focusLeft)
    {
        var imprint = modifier.Imprint;
        var atoms = speak.Imprint.Atoms;
        var kind = speak.Kind;
        if (kind == WandSpeakKind.Utility || !HasCopyable(atoms))
            return false;
        var targeted = atoms.Where(a => a.Kind == WandAtomKind.Damage).All(a => a.Targeting == WandTargeting.Target);
        return (imprint.Operation, imprint.Rule) switch
        {
            (WandOperation.Heighten, WandRule.Contrast) => true,
            (WandOperation.Heighten, WandRule.Brand) => kind == WandSpeakKind.Damage,
            (WandOperation.Heighten, _) => imprint.Filter == WandFilter.NegativeStatus
                ? atoms.Any(a => Negative(a, registry))
                : FilterAccepts(imprint.Filter, kind),
            (WandOperation.Shape, WandRule.Fork) => kind == WandSpeakKind.Damage,
            (WandOperation.Shape, WandRule.Scatter) => kind == WandSpeakKind.Damage && targeted,
            (WandOperation.Shape, WandRule.Soak) => kind == WandSpeakKind.Damage
                && atoms.All(a => a.Kind == WandAtomKind.Damage || !a.Copyable),
            (WandOperation.Shape, WandRule.Remedy) => atoms.Any(a => Negative(a, registry)),
            (WandOperation.Shape, WandRule.Convert) => atoms.Any(a => Negative(a, registry)),
            (WandOperation.Shape, WandRule.Crossfire) => kind == WandSpeakKind.Damage && HitsOf(speak, steps) > 1,
            (WandOperation.Shape, WandRule.Prism) => kind is WandSpeakKind.Damage or WandSpeakKind.Defense,
            (WandOperation.Shape, WandRule.Brand) => kind == WandSpeakKind.Damage,
            (WandOperation.Clause, WandRule.BonusHit) => kind == WandSpeakKind.Damage,
            // Only when paying left no Focus — otherwise the Speak runs once, as written.
            (WandOperation.Clause, WandRule.FinalBreath) => kind == WandSpeakKind.Damage && focusLeft == 0
                && HitsOf(speak, steps) == 1,
            (WandOperation.Keep, _) => FilterAccepts(imprint.Filter, kind),
            (WandOperation.Order, WandRule.Errata) => true,
            _ => false,
        };
    }

    private static bool Hook(WandRules rules, CombatantState bearer, WandHook hook) =>
        rules.Hooks.TryGetValue(hook, out var status) && bearer.Statuses.Any(s => s.DefinitionId == status);

    // ── Focus and Exhaustion ───────────────────────────────────────────────────────────────────────────────

    public static bool IsHeavy(WandImprint imprint) => imprint.Heavy ?? (imprint.Operation, imprint.Rule) switch
    {
        (WandOperation.Echo, _) => true,
        (WandOperation.Shape, _) => true,
        (WandOperation.Keep, WandRule.Plain) or (WandOperation.Keep, WandRule.StoreLast) => true,
        (WandOperation.Order, WandRule.Reverse) => true,
        (WandOperation.Clause, WandRule.BonusHit) or (WandOperation.Clause, WandRule.FinalBreath) => true,
        _ => false,
    };

    private static void Settle(
        WandPlan plan, CombatState combat, CombatDefinitionRegistry registry, WandState wand, WandRules rules,
        CombatantId? target)
    {
        var steps = plan.Steps;
        var heavyBefore = 0;
        var speaksSeen = 0;
        var heavyCap = (int?)null;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var imprint = step.Imprint;
            if (IsHeavy(imprint))
            {
                plan.Heavy++;
                // A duplicated Speak is processed twice: the copy is heavy work of its own.
                if (imprint.Operation == WandOperation.Echo && step.Source is not null)
                    plan.Heavy++;
            }
            if (!step.Fizzled && imprint.Exhaustion > 0)
                plan.Strain += imprint.Exhaustion;
            else if (!step.Fizzled && imprint.Exhaustion < 0)
                step.Ground += -imprint.Exhaustion;

            if (!step.Fizzled)
                switch (imprint.Operation, imprint.Rule)
                {
                    case (WandOperation.Speak, _):
                        if (imprint.GroundIfSpeakFollows > 0
                            && steps.Skip(i + 1).Any(s => s.Imprint.Operation == WandOperation.Speak && !s.Fizzled))
                            step.Ground += imprint.GroundIfSpeakFollows;
                        if (imprint.GroundAmount > 0 && imprint.GroundWhen is { } when
                            && Evaluate(when, combat, registry, plan.Bearer, target, step))
                            step.Ground += imprint.GroundAmount;
                        speaksSeen++;
                        break;
                    case (WandOperation.Ground, WandRule.Plain):
                    case (WandOperation.Burn, WandRule.Plain):
                        step.Ground += imprint.Amount;
                        break;
                    case (WandOperation.Ground, WandRule.Quench):
                        var singe = rules.Singe is { } singeId && AimAt(combat, plan.Bearer, target) is { } aimed
                            ? combat.GetCombatant(aimed).Statuses.Where(s => s.DefinitionId == singeId).Sum(s => s.Stacks)
                            : 0;
                        step.Ground += Math.Min(imprint.Cap, Math.Min(imprint.Amount, singe) * imprint.Percent);
                        break;
                    case (WandOperation.Ground, WandRule.PerHeavy):
                        step.Ground += Math.Min(imprint.Cap, heavyBefore);
                        break;
                    case (WandOperation.Ground, WandRule.ByLength):
                        step.Ground += plan.Executed >= imprint.Cap ? imprint.Amount2 : imprint.Amount;
                        break;
                    // Whether a hit will spend Singe is only known when it lands; the plan counts the lesser relief,
                    // the cast the real one.
                    case (WandOperation.Ground, WandRule.Firebreak):
                        step.Ground += imprint.Amount2;
                        break;
                    case (WandOperation.Ground, WandRule.HeavyCap):
                        heavyCap = imprint.Cap;
                        step.HeavyCapCost = imprint.Amount;
                        break;
                    case (WandOperation.Stop, WandRule.Plain):
                        if (speaksSeen > 0)
                            step.Ground += imprint.Amount;
                        break;
                    case (WandOperation.Stop, WandRule.FinalWord):
                        plan.Strain += imprint.Amount2;
                        break;
                    case (WandOperation.Clause, WandRule.Fourfold):
                        var kinds = ElementsAfter(wand.ElementsThisTurn, steps.Take(i), registry, rules);
                        if (kinds >= 3)
                            step.Ground += imprint.Amount2;
                        break;
                    case (WandOperation.Order, WandRule.SwapNext) when step.Swapped:
                        if (SwapChangesHeighten(plan, i))
                            step.Ground += imprint.Amount;
                        break;
                }

            if (IsHeavy(imprint))
                heavyBefore++;
            plan.Ground += step.Ground;
        }

        var heavyPart = plan.Heavy * rules.HeavyExhaustion;
        if (heavyCap is { } cap && heavyPart > cap)
        {
            heavyPart = cap;
            plan.ExtraFocus = steps.First(s => s.HeavyCapCost > 0).HeavyCapCost;
        }
        plan.HeavyPart = heavyPart;
        plan.Base = rules.BaseExhaustion;
        plan.Surcharge = plan.Executed >= rules.LongCastThreshold ? rules.LongCastSurcharge : 0;
        plan.Credit = wand.GroundCredit;
        plan.Exhaustion = Math.Max(0, plan.Base + heavyPart + plan.Surcharge + plan.Strain - plan.Ground - plan.Credit);
        plan.ExhaustionAfter = wand.Exhaustion + plan.Exhaustion;
        plan.ExhaustionCap = wand.ExhaustionCap;
        plan.Effects = CountEffects(plan, combat, registry, wand);

        var bearer = combat.GetCombatant(plan.Bearer);
        if (wand.CastsThisTurn >= rules.CastsPerTurn)
            plan.Refuse($"Already cast {rules.CastsPerTurn} times this turn.");
        else if (wand.Focus < plan.FocusCost + plan.ExtraFocus)
            plan.Refuse($"Costs {plan.FocusCost + plan.ExtraFocus} Focus, you have {wand.Focus}.");
        else if (plan.ExhaustionAfter > wand.ExhaustionCap)
            plan.Refuse($"Exhaustion would reach {plan.ExhaustionAfter} of {wand.ExhaustionCap}.");
        else if (plan.Effects > rules.MaxEffectsPerCast)
            plan.Refuse($"Too much at once: {plan.Effects} effects, the most is {rules.MaxEffectsPerCast}.");
        else if (!bearer.IsAlive)
            plan.Refuse("The wand's bearer is down.");
    }

    // Whether the swap put a different Speak under some Heighten than the original order would have.
    private static bool SwapChangesHeighten(WandPlan plan, int swapAt)
    {
        if (swapAt + 2 >= plan.Steps.Count)
            return false;
        var first = plan.Steps[swapAt + 1];
        var second = plan.Steps[swapAt + 2];
        // Only a swap of a Speak with something else can change which Speak a Heighten reaches next.
        return first.Imprint.Operation == WandOperation.Speak ^ second.Imprint.Operation == WandOperation.Speak
            && plan.Steps.Take(swapAt).Any(s => s.Imprint.Operation == WandOperation.Heighten && s.Target is not null);
    }

    // How many of the four elements will have happened this turn by this point of the cast, counting what the
    // steps before it are written to do.
    private static int ElementsAfter(int already, IEnumerable<WandStep> before, CombatDefinitionRegistry registry, WandRules rules)
    {
        var seen = (WandElement)already;
        foreach (var step in before.Where(s => !s.Fizzled && s.Imprint.Operation == WandOperation.Speak))
            foreach (var atom in step.Imprint.Atoms.Where(a => a.Kind == WandAtomKind.Status && a.Status is not null))
                seen |= ElementOf(atom.Status!.Value, atom.Targeting, rules);
        _ = registry;
        var count = 0;
        foreach (var flag in new[] { WandElement.SingeApplied, WandElement.FlowGained, WandElement.GritGained, WandElement.GustGained })
            if (seen.HasFlag(flag))
                count++;
        return count;
    }

    internal static WandElement ElementOf(StatusDefinitionId status, WandTargeting targeting, WandRules rules)
    {
        if (status == rules.Singe && targeting != WandTargeting.Self)
            return WandElement.SingeApplied;
        if (targeting != WandTargeting.Self)
            return WandElement.None;
        if (status == rules.Flow)
            return WandElement.FlowGained;
        if (status == rules.Grit)
            return WandElement.GritGained;
        if (status == rules.Gust)
            return WandElement.GustGained;
        return WandElement.None;
    }

    // How many atomic effects the cast will emit, counted generously: every hit, every enemy an all-enemy effect
    // reaches, the copies, the stored spell. The plan is refused above the cap rather than cut short.
    private static int CountEffects(WandPlan plan, CombatState combat, CombatDefinitionRegistry registry, WandState wand)
    {
        var enemies = Math.Max(1, combat.Combatants.Count(c => c.IsAlive && c.TeamId != combat.GetCombatant(plan.Bearer).TeamId));
        int AtomsOf(IReadOnlyList<WandAtom> atoms) =>
            atoms.Sum(a => a.Kind == WandAtomKind.Program ? 1
                : Math.Max(1, a.Hits) * (a.Targeting == WandTargeting.AllEnemies ? enemies : 1));
        var counts = new int[plan.Steps.Count];
        var total = wand.StoredSpell is { } stored
            ? stored.Effects.Sum(e => Math.Max(1, e.Hits) * (e.Targeting == WandTargeting.AllEnemies ? enemies : 1))
            : 0;
        foreach (var step in plan.Steps)
        {
            if (step.Fizzled)
                continue;
            var count = step.Imprint.Operation switch
            {
                WandOperation.Speak => AtomsOf(step.Borrowed?.Definition.Wand?.Atoms ?? step.Imprint.Atoms),
                WandOperation.Burn => AtomsOf(step.Imprint.Atoms),
                WandOperation.Echo => step.Source is { } source ? counts[source] : AtomsOf(step.Imprint.Fallback),
                WandOperation.Stop when step.Imprint.Rule == WandRule.FinalWord => Math.Max(1, step.Imprint.Cap),
                _ => 0,
            };
            foreach (var index in step.Modifiers)
                count += plan.Steps[index].Imprint.Rule switch
                {
                    WandRule.Fork => count * (Math.Max(2, plan.Steps[index].Imprint.Amount) - 1),
                    WandRule.Scatter => count * (enemies - 1),
                    WandRule.Prism => count,
                    WandRule.BonusHit => 1,
                    WandRule.Brand => Math.Min(enemies, Math.Max(1, plan.Steps[index].Imprint.Cap)),
                    _ => 0,
                };
            counts[step.Index] = count;
            total += count;
        }
        _ = registry;
        return total;
    }

    // Whom a Speak aimed at the target hits: the target while it stands, else the living enemy with the least HP
    // (then the earliest in the turn order) — master §3.3.
    public static CombatantId? AimAt(CombatState combat, CombatantId bearer, CombatantId? target)
    {
        if (target is { } chosen && combat.TryGetCombatant(chosen, out var standing) && standing!.IsAlive)
            return chosen;
        var team = combat.GetCombatant(bearer).TeamId;
        return combat.TurnOrder
            .Select(id => combat.GetCombatant(id))
            .Where(c => c.IsAlive && c.TeamId != team)
            .OrderBy(c => c.Health.Current)
            .ThenBy(c => combat.TurnOrder.ToList().IndexOf(c.Id))
            .FirstOrDefault()?.Id;
    }

    internal static bool Evaluate(
        ICombatExpression<CardPlayContext, bool> expression, CombatState combat, CombatDefinitionRegistry registry,
        CombatantId bearer, CombatantId? target, WandStep step) =>
        expression.Evaluate(ContextFor(combat, step.Definition, step.Card, bearer, target), combat);

    internal static EffectExecutionContext<CardPlayContext> ContextFor(
        CombatState combat, CardDefinition card, CardInstanceId? instance, CombatantId bearer, CombatantId? target) =>
        new(new CardPlayContext(card, instance),
            CombatCardPlayProcessor.CreateCardPlayBuildContext(combat, card.Id, combat.GetCombatant(bearer),
                AimAt(combat, bearer, target)));
}

public enum WandSpeakKind
{
    Utility,
    Damage,
    Defense,
    Status,
}

// One instruction of a compiled cast, in the order it runs.
public sealed class WandStep(int slot, CardInstanceId card, CardDefinition definition, WandImprint imprint)
{
    public int Index { get; internal set; }
    public int Slot { get; } = slot;
    public CardInstanceId Card { get; } = card;
    public CardDefinition Definition { get; } = definition;
    public WandImprint Imprint { get; } = imprint;

    public bool Fizzled { get; private set; }
    public string? FizzleReason { get; private set; }

    // A Speak's kind; the modifiers bound to it, in the order they apply.
    public WandSpeakKind Kind { get; internal set; }
    public List<int> Modifiers { get; } = [];

    // For a forward modifier: the Speak it reaches. For Join: the first of the two, and its Partner.
    public int? Target { get; internal set; }
    public int? Partner { get; internal set; }
    // A Speak folded into an earlier one by Join.
    public int? JoinedInto { get; internal set; }
    // For Echo / StoreLast: the Speak it copies.
    public int? Source { get; internal set; }
    // An Errata's borrowed Speak, on the Speak that borrows it.
    public (CardInstanceId Card, CardDefinition Definition)? Borrowed { get; internal set; }

    public bool Swapped { get; internal set; }
    public bool BoneSpacer { get; internal set; }
    public int Ground { get; internal set; }
    public int HeavyCapCost { get; internal set; }

    internal void Fizzle(string reason)
    {
        Fizzled = true;
        FizzleReason = reason;
    }
}

// A compiled cast: the steps, the bill, and whether it may go ahead.
public sealed class WandPlan(CombatantId bearer, CombatantId? target, int start, int end, bool full)
{
    public CombatantId Bearer { get; } = bearer;
    public CombatantId? Target { get; } = target;
    public int SectionStart { get; } = start;
    public int SectionEnd { get; } = end;
    public bool FullWand { get; } = full;

    public List<WandStep> Steps { get; } = [];
    // The next-cast boosts this cast spends.
    public List<WandCastBoost> Boosts { get; } = [];
    // Slots a Stop cut off.
    public List<int> Skipped { get; } = [];
    public bool Backward { get; internal set; }
    public bool GapCrossed { get; internal set; }
    internal int? GapAfter { get; set; }

    public int Executed { get; internal set; }
    public int FocusCost { get; internal set; }
    public int ExtraFocus { get; internal set; }

    public int Base { get; internal set; }
    public int Heavy { get; internal set; }
    public int HeavyPart { get; internal set; }
    public int Surcharge { get; internal set; }
    public int Strain { get; internal set; }
    public int Ground { get; internal set; }
    public int Credit { get; internal set; }
    // What this cast adds to the wand's Exhaustion, and where that leaves it.
    public int Exhaustion { get; internal set; }
    public int ExhaustionAfter { get; internal set; }
    public int ExhaustionCap { get; internal set; }
    public int Effects { get; internal set; }

    public string? Illegal { get; private set; }
    public bool IsLegal => Illegal is null;

    internal WandPlan Refuse(string reason)
    {
        Illegal ??= reason;
        return this;
    }

    internal static WandPlan Refused(CombatantId bearer, string reason) =>
        new WandPlan(bearer, null, 0, 0, false).Refuse(reason);
}
