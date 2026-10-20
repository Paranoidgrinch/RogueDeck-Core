namespace RogueDeck.Core.Combat;

// THE CAST ITSELF (Sorcerer's Apprentice master §3.3, steps 4–7): a compiled plan (WandCompiler) carried out.
// Focus is paid once; a spell stored for the next cast goes first; then every instruction in order, each Speak
// read against the fight as it stands at that moment and sent through the ordinary damage, Block and status
// requests — so Singe, Block, Ward Wax, relics and enemy passives answer a wand's hit exactly as they answer a
// card's — and each waits for the one before it to have happened. Then the settlement: burned cards exhausted,
// Exhaustion settled, the Flow refund, what waits until after the cast, the cards that move.
//
// A cast is not a card play: nothing here raises CardPlayed. It runs inside the Cast action, which is one action.
internal sealed class WandCast
{
    private readonly CombatState _combat;
    private readonly CombatDefinitionRegistry _registry;
    private readonly WandRules _rules;
    private readonly WandState _wand;
    private readonly WandPlan _plan;
    private readonly CombatEffectChainContext? _chain;
    private readonly Action<CombatState>? _onComplete;

    private readonly Dictionary<int, Resolved> _resolved = [];
    private Resolved? _last;
    private readonly List<CardInstanceId> _burns = [];
    private readonly List<(WandStep Step, WandAtom Atom)> _afterCast = [];
    private readonly HashSet<WandOperation> _executedKinds = [];
    private int _oldFlow;
    private int _flowAfter;
    private int _focusAfter;
    private int _relief;
    private bool _storedThisCast;
    private int _speaksResolved;
    private readonly List<int> _burnedSlots = [];

    private WandCast(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, WandState wand, WandPlan plan,
        CombatEffectChainContext? chain, Action<CombatState>? onComplete)
    {
        _combat = combat;
        _registry = registry;
        _rules = rules;
        _wand = wand;
        _plan = plan;
        _chain = chain;
        _onComplete = onComplete;
    }

    private CombatantId Bearer => _plan.Bearer;

    // Carry out a legal plan. An illegal one changes nothing and completes at once — no Focus, no store, no charge.
    public static void Run(CombatState combat, CombatDefinitionRegistry registry, WandPlan plan,
        CombatEffectChainContext? chain, Action<CombatState>? onComplete)
    {
        if (!plan.IsLegal || registry.Wand is not { } rules || combat.GetWand(plan.Bearer) is not { } wand)
        {
            combat.AddLogEntry(StandardCombatLogTypes.WandCast, $"Wand cast by '{plan.Bearer}' refused: {plan.Illegal}");
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
            return;
        }
        new WandCast(combat, registry, rules, wand, plan, chain, onComplete).Begin();
    }

    // Frozen effects let loose outside a cast — a spell that waited for a Burn Out, an ash trace — resolved the way a
    // cast resolves them, then `next`.
    public static void Release(CombatState combat, CombatDefinitionRegistry registry, CombatantId bearer, CombatantId? target,
        IReadOnlyList<WandFrozenEffect> effects, CardDefinitionId? source, CombatEffectChainContext? chain, Action next)
    {
        if (effects.Count == 0 || registry.Wand is not { } rules || combat.GetWand(bearer) is not { } wand)
        {
            next();
            return;
        }
        var cast = new WandCast(combat, registry, rules, wand, new WandPlan(bearer, target, 0, 0, false), chain, null);
        cast.Apply(effects, source, null, copy: false, next);
    }

    // A Shield Seal put up the instant the first enemy attack reaches its bearer — before the hit, so it takes the
    // hit: its Block straight onto the Block pool, its Ward Wax applied.
    public static void RaiseSealNow(CombatState combat, CombatDefinitionRegistry registry, WandRules rules, CombatantState bearer,
        WandStoredSpell seal)
    {
        foreach (var effect in seal.Effects)
        {
            if (effect.Kind == WandEffectKind.Block)
            {
                if (bearer.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var block))
                    block.SetCurrent(block.Current + effect.Amount);
                else
                    bearer.AddDefensivePool(StandardCombatIds.BlockDefensivePool, new ValuePoolState(current: effect.Amount));
                combat.EnqueueEvent(new BlockGainedCombatEvent(bearer.Id, effect.Amount, effect.Amount, bearer.Id, seal.SourceCard));
            }
            else if (effect.Kind == WandEffectKind.WardWax && rules.WardWax is { } wax)
            {
                combat.EnqueueEffect(new ApplyStatusEffectRequest(bearer.Id, wax, bearer.Id, seal.SourceCard, Stacks: effect.Amount));
            }
        }
        combat.AddLogEntry(StandardCombatLogTypes.WandCast, $"A Shield Seal rose before the hit on '{bearer.Id}'.");
        _ = registry;
    }

    private void Begin()
    {
        _wand.Focus -= _plan.FocusCost + _plan.ExtraFocus;
        _wand.CastsThisTurn++;
        _wand.CastsThisCombat++;
        _wand.SingeSpentThisCast = 0;
        _wand.Casting = true;
        _oldFlow = Stacks(Bearer, _rules.Flow);
        _combat.AddLogEntry(StandardCombatLogTypes.WandCast,
            $"'{Bearer}' cast the wand: {_plan.Executed} instruction(s), {_plan.FocusCost + _plan.ExtraFocus} Focus.");

        // The spell kept for the next cast goes first — it has already left its source instruction.
        if (_wand.StoredSpell is { Trigger: WandStoreTrigger.NextCast } stored)
        {
            _wand.StoredSpell = null;
            Apply(stored.Effects, stored.SourceCard, null, copy: false, () => Step(0));
            return;
        }
        Step(0);
    }

    // ── the instructions ───────────────────────────────────────────────────────────────────────────────────

    private void Next(int index) => Continue(() => Step(index));

    private void Continue(Action next) =>
        _combat.EnqueueContinuation(c =>
        {
            if (c.Result != CombatResult.Ongoing)
                return;
            using (_chain is null ? null : c.EnterEffectChain(_chain))
                next();
        });

    private void Step(int index)
    {
        if (_combat.Result != CombatResult.Ongoing)
            return;
        if (index >= _plan.Steps.Count)
        {
            Settle();
            return;
        }

        var step = _plan.Steps[index];
        if (step.Fizzled)
        {
            _combat.AddLogEntry(StandardCombatLogTypes.WandCast, $"Slot {step.Slot + 1} fizzled: {step.FizzleReason}");
            Step(index + 1);
            return;
        }

        var imprint = step.Imprint;
        switch (imprint.Operation, imprint.Rule)
        {
            case (WandOperation.Speak, _) when step.JoinedInto is not null:
                // Folded into the Speak before it by a Join.
                Step(index + 1);
                return;
            case (WandOperation.Speak, _):
                Speak(step, () => Next(index + 1));
                break;
            case (WandOperation.Echo, _):
                if (step.Source is { } source && _resolved.TryGetValue(source, out var original))
                {
                    var copies = original.Copyable.Select(e => Scale(e, imprint.Percent, roundUp: true)).ToList();
                    if (imprint.Burns)
                        _burns.Add(step.Card);
                    Apply(copies, step.Definition.Id, step.Card, copy: true, () => Next(index + 1));
                }
                else
                {
                    if (imprint.Burns)
                        _burns.Add(step.Card);
                    Atoms(step, imprint.Fallback, () => Next(index + 1));
                }
                break;
            case (WandOperation.Keep, WandRule.StoreLast):
                if (step.Source is { } kept && _resolved.TryGetValue(kept, out var copied)
                    && _wand.StoredSpell is null && !_storedThisCast)
                {
                    Store(imprint.Trigger, copied.Copyable.Select(e => Scale(e, imprint.Percent, roundUp: false)), step.Definition.Id);
                    Step(index + 1);
                }
                else
                {
                    Atoms(step, imprint.Fallback, () => Next(index + 1));
                }
                break;
            case (WandOperation.Keep, WandRule.AshTrace):
                _wand.AddAshTrace(imprint.Amount);
                Step(index + 1);
                break;
            case (WandOperation.Burn, WandRule.Plain):
                _burns.Add(step.Card);
                Atoms(step, imprint.Atoms, () => Next(index + 1));
                break;
            case (WandOperation.Ground, WandRule.Quench):
                Quench(step, () => Next(index + 1));
                break;
            case (WandOperation.Ground, WandRule.Firebreak):
                if (_wand.SingeSpentThisCast > 0)
                {
                    _relief += imprint.Amount - imprint.Amount2;
                    if (_rules.Grit is { } grit)
                    {
                        Apply([new WandFrozenEffect(WandEffectKind.Status, 1, Targeting: WandTargeting.Self, Status: grit)],
                            step.Definition.Id, step.Card, copy: false, () => Next(index + 1));
                        break;
                    }
                }
                Step(index + 1);
                break;
            case (WandOperation.Clause, WandRule.Fourfold):
                Fourfold(step, () => Next(index + 1));
                break;
            case (WandOperation.Stop, WandRule.FinalWord):
                FinalWord(step, () => Next(index + 1));
                break;
            default:
                Step(index + 1);
                break;
        }
        _executedKinds.Add(imprint.Operation);
    }

    // ── a Speak ────────────────────────────────────────────────────────────────────────────────────────────

    // What a Speak came to: the part every copy may take, the part only it keeps, and its kind.
    private sealed record Resolved(List<WandFrozenEffect> Copyable, WandSpeakKind Kind);

    private void Speak(WandStep step, Action next)
    {
        var atoms = step.Borrowed?.Definition.Wand?.Atoms ?? step.Imprint.Atoms;
        var parts = Evaluate(step, atoms);

        // Join: the next damage Speak's damage is folded into this one, one hit, plus the Join's own.
        foreach (var index in step.Modifiers)
            if (_plan.Steps[index] is { Imprint.Rule: WandRule.Join, Partner: { } partner } join)
            {
                var other = Evaluate(_plan.Steps[partner], _plan.Steps[partner].Imprint.Atoms);
                var damage = parts.Where(p => p.Effect?.Kind == WandEffectKind.Damage).Sum(p => p.Effect!.Amount * p.Effect.Hits)
                    + other.Where(p => p.Effect?.Kind == WandEffectKind.Damage).Sum(p => p.Effect!.Amount * p.Effect.Hits)
                    + join.Imprint.Amount;
                parts =
                [
                    new Part(new WandFrozenEffect(WandEffectKind.Damage, damage), true, null),
                    .. parts.Where(p => p.Effect?.Kind != WandEffectKind.Damage),
                    .. other.Where(p => p.Effect?.Kind != WandEffectKind.Damage),
                ];
            }

        if (step.Borrowed is not null && _plan.Steps.FirstOrDefault(s => s.Target == step.Index && s.Imprint.Rule == WandRule.Errata) is { } errata)
            parts = [.. parts.Select(p => p.Effect is { } e && p.Copyable ? p with { Effect = Heighten(e, errata.Imprint.Percent, WandFilter.Any) } : p)];

        var kind = WandCompiler.KindOf(atoms, _registry);
        var brand = (Status: (StatusDefinitionId?)null, Each: 0, Cap: 0);
        var crossfireStatus = (Status: (StatusDefinitionId?)null, Amount: 0);
        var keep = (WandStep?)null;
        var burnsAfter = false;

        if (step.BoneSpacer)
            parts = HeightenAll(parts, _rules.BoneSpacerPercent, WandFilter.Any, 0);
        foreach (var boost in _plan.Boosts)
            parts = HeightenAll(parts, boost.Percent, WandFilter.DamageOrDefense, 0);

        foreach (var index in step.Modifiers)
        {
            var modifier = _plan.Steps[index];
            var m = modifier.Imprint;
            switch (m.Operation, m.Rule)
            {
                case (WandOperation.Heighten, WandRule.Plain):
                    parts = HeightenAll(parts, m.Percent, m.Filter, m.Amount);
                    break;
                case (WandOperation.Heighten, WandRule.Contrast):
                    if (_last is { } previous && previous.Kind != kind)
                        parts = HeightenAll(parts, m.Percent, WandFilter.Any, 0);
                    else
                        _flowAfter += m.Amount;
                    break;
                case (WandOperation.Heighten, WandRule.Brand):
                case (WandOperation.Shape, WandRule.Brand):
                    brand = (m.Status ?? _rules.Singe, Math.Max(1, m.Amount), Math.Max(1, m.Cap));
                    break;
                case (WandOperation.Shape, WandRule.Fork):
                case (WandOperation.Clause, WandRule.FinalBreath):
                    parts = [.. parts.Select(p => p.Effect is { Kind: WandEffectKind.Damage } e && p.Copyable
                        ? p with { Effect = e with { Amount = Percent(e.Amount, m.Percent, roundUp: true), Hits = e.Hits * Math.Max(2, m.Amount) } }
                        : p)];
                    break;
                case (WandOperation.Shape, WandRule.Scatter):
                    parts = [.. parts.Select(p => p.Effect is { Kind: WandEffectKind.Damage, Targeting: WandTargeting.Target } e && p.Copyable
                        ? p with { Effect = e with { Amount = Percent(e.Amount, m.Percent, roundUp: false), Targeting = WandTargeting.AllEnemies } }
                        : p)];
                    break;
                case (WandOperation.Shape, WandRule.Soak):
                    var soaked = parts.Where(p => p.Effect is { Kind: WandEffectKind.Damage } && p.Copyable)
                        .Sum(p => p.Effect!.Amount * p.Effect.Hits);
                    parts =
                    [
                        new Part(new WandFrozenEffect(WandEffectKind.Block, Percent(soaked, m.Percent, roundUp: false), Targeting: WandTargeting.Self), true, null),
                        .. parts.Where(p => !p.Copyable || p.Effect is null),
                    ];
                    break;
                case (WandOperation.Shape, WandRule.Remedy):
                    parts = [.. parts.Where(p => !(p.Effect is { Kind: WandEffectKind.Status } e && IsNegative(e)))];
                    parts = [.. parts, new Part(null, false, null) { Remedy = (m.Amount, m.Cap) }];
                    break;
                case (WandOperation.Shape, WandRule.Convert):
                    parts = [.. parts.SelectMany(p => p.Effect is { Kind: WandEffectKind.Status } e && IsNegative(e) && m.Status is { } into
                        ? Converted(p, e, m.Amount, into, m.Cap)
                        : [p])];
                    break;
                case (WandOperation.Shape, WandRule.Crossfire):
                    parts = [.. parts.Select(p => p.Effect is { Kind: WandEffectKind.Damage } e && e.Hits > 1 && p.Copyable
                        ? p with { Effect = e with { Alternate = true } }
                        : p)];
                    crossfireStatus = (m.Status ?? _rules.Singe, m.Amount);
                    break;
                case (WandOperation.Shape, WandRule.Prism):
                    parts = [.. parts.SelectMany(p => p.Effect is { Kind: WandEffectKind.Damage or WandEffectKind.Block } e && p.Copyable
                        ? Prism(p, e, m.Percent)
                        : [p])];
                    _burns.Add(modifier.Card);
                    burnsAfter = true;
                    break;
                case (WandOperation.Clause, WandRule.BonusHit):
                    if (Aim(null) is { } aimed && _combat.UpcomingIntentKind?.Invoke(_combat, aimed) is { } intent
                        && string.Equals(intent, "Attack", StringComparison.OrdinalIgnoreCase))
                        parts = [.. parts, new Part(new WandFrozenEffect(WandEffectKind.Damage, m.Amount), true, null)];
                    break;
                case (WandOperation.Keep, WandRule.Plain):
                    keep = modifier;
                    break;
            }
        }
        _ = burnsAfter;

        var copyable = parts.Where(p => p.Copyable && p.Effect is not null).Select(p => p.Effect!).ToList();
        var resolved = new Resolved(copyable, kind);
        _resolved[step.Index] = resolved;
        _last = resolved;
        _speaksResolved++;
        _wand.SpeaksThisCombat++;

        // Kept instead of spoken: the copyable part waits for its trigger; the rest still happens now.
        if (keep is { } keeper && _wand.StoredSpell is null && !_storedThisCast && copyable.Count > 0)
        {
            Store(keeper.Imprint.Trigger, copyable.Select(e => Scale(e, keeper.Imprint.Percent, roundUp: false)), step.Definition.Id);
            parts = [.. parts.Where(p => !p.Copyable || p.Effect is null)];
        }

        Deliver(step, parts, brand, crossfireStatus, next);
    }

    // One evaluated atom: a numeric effect (Effect) or a program to run (Program), and whether copies may take it.
    private sealed record Part(WandFrozenEffect? Effect, bool Copyable, WandAtom? Program)
    {
        public (int Stacks, int BlockPer)? Remedy { get; init; }
    }

    private List<Part> Evaluate(WandStep step, IReadOnlyList<WandAtom> atoms)
    {
        var parts = new List<Part>();
        var context = WandCompiler.ContextFor(_combat, step.Definition, step.Card, Bearer, _plan.Target);
        foreach (var atom in atoms)
        {
            if (!InPosition(step, atom.Only))
                continue;
            if (atom.When is { } when && !when.Evaluate(context, _combat))
                continue;
            if (atom.Kind == WandAtomKind.Program)
            {
                if (atom.AfterCast)
                    _afterCast.Add((step, atom));
                else
                    parts.Add(new Part(null, false, atom));
                continue;
            }
            var amount = atom.Amount?.Evaluate(context, _combat) ?? 0;
            if (atom.Bonus != 0 && InPosition(step, atom.BonusAt))
                amount += atom.Bonus;
            if (amount <= 0)
                continue;
            if (atom.Kind == WandAtomKind.Damage && _rules.Rote is { } rote)
                amount += Stacks(Bearer, rote);
            else if (atom.Kind == WandAtomKind.Block && _rules.Poise is { } poise)
                amount += Stacks(Bearer, poise);
            var kind = atom.Kind switch
            {
                WandAtomKind.Damage => WandEffectKind.Damage,
                WandAtomKind.Block => WandEffectKind.Block,
                WandAtomKind.WardWax => WandEffectKind.WardWax,
                _ => WandEffectKind.Status,
            };
            var targeting = atom.Kind is WandAtomKind.Block or WandAtomKind.WardWax ? WandTargeting.Self : atom.Targeting;
            parts.Add(new Part(new WandFrozenEffect(kind, amount, Math.Max(1, atom.Hits), targeting, atom.Status), atom.Copyable, null));
        }
        return parts;
    }

    // "If this is the first executed slot / the first Speak": read in the order the cast actually runs.
    private bool InPosition(WandStep step, WandPosition position) => position switch
    {
        WandPosition.FirstExecuted => _plan.Steps.Count > 0 && _plan.Steps[0].Index == step.Index,
        WandPosition.FirstSpeak => _plan.Steps.First(s => s.Imprint.Operation == WandOperation.Speak && !s.Fizzled).Index == step.Index,
        _ => true,
    };

    // ── the numbers ────────────────────────────────────────────────────────────────────────────────────────

    private static int Percent(int amount, int percent, bool roundUp) =>
        roundUp ? (amount * percent + 99) / 100 : amount * percent / 100;

    private static WandFrozenEffect Scale(WandFrozenEffect effect, int percent, bool roundUp) =>
        effect with { Amount = Math.Max(effect.Kind == WandEffectKind.Status ? 1 : 0, Percent(effect.Amount, percent, roundUp)) };

    private static WandFrozenEffect Heighten(WandFrozenEffect effect, int percent, WandFilter filter) =>
        effect.Kind switch
        {
            WandEffectKind.Damage when filter is WandFilter.Any or WandFilter.Damage or WandFilter.DamageOrDefense =>
                effect with { Amount = Percent(effect.Amount, 100 + percent, roundUp: false) },
            WandEffectKind.Block or WandEffectKind.WardWax when filter is WandFilter.Any or WandFilter.Defense or WandFilter.DamageOrDefense =>
                effect with { Amount = Percent(effect.Amount, 100 + percent, roundUp: false) },
            _ => effect,
        };

    // A Heighten: damage and Block by its percentage; a status-only Speak by stacks instead (its first status, or
    // its first negative one for a Heighten that only looks at those).
    private List<Part> HeightenAll(List<Part> parts, int percent, WandFilter filter, int stacks)
    {
        var numeric = parts.Any(p => p.Copyable && p.Effect?.Kind is WandEffectKind.Damage or WandEffectKind.Block or WandEffectKind.WardWax);
        if (filter == WandFilter.NegativeStatus || !numeric)
        {
            var done = false;
            return [.. parts.Select(p =>
            {
                if (done || stacks <= 0 || !p.Copyable || p.Effect is not { Kind: WandEffectKind.Status } e
                    || (filter == WandFilter.NegativeStatus && !IsNegative(e)))
                    return p;
                done = true;
                return p with { Effect = e with { Amount = e.Amount + stacks } };
            })];
        }
        return [.. parts.Select(p => p.Copyable && p.Effect is { } e ? p with { Effect = Heighten(e, percent, filter) } : p)];
    }

    private bool IsNegative(WandFrozenEffect effect) =>
        effect.Kind == WandEffectKind.Status && effect.Targeting != WandTargeting.Self && effect.Status is { } status
        && _registry.StatusDefinitions.TryGetValue(status, out var definition) && definition.Polarity == StatusPolarity.Debuff;

    private static IEnumerable<Part> Converted(Part part, WandFrozenEffect effect, int most, StatusDefinitionId into, int each)
    {
        var taken = Math.Min(most, effect.Amount);
        if (effect.Amount - taken > 0)
            yield return part with { Effect = effect with { Amount = effect.Amount - taken } };
        if (taken > 0)
            yield return part with { Effect = effect with { Status = into, Amount = taken * Math.Max(1, each) } };
    }

    private static IEnumerable<Part> Prism(Part part, WandFrozenEffect effect, int percent)
    {
        var amount = Percent(effect.Amount * effect.Hits, percent, roundUp: false);
        if (amount <= 0)
            yield break;
        yield return part with { Effect = new WandFrozenEffect(WandEffectKind.Damage, amount, Targeting: effect.Kind == WandEffectKind.Damage ? effect.Targeting : WandTargeting.Target) };
        yield return part with { Effect = new WandFrozenEffect(WandEffectKind.Block, amount, Targeting: WandTargeting.Self) };
    }

    // ── putting effects into the fight ─────────────────────────────────────────────────────────────────────

    private void Deliver(WandStep step, List<Part> parts, (StatusDefinitionId? Status, int Each, int Cap) brand,
        (StatusDefinitionId? Status, int Amount) crossfire, Action next)
    {
        var struck = new List<CombatantId>();
        void Part(int at)
        {
            if (_combat.Result != CombatResult.Ongoing)
                return;
            if (at >= parts.Count)
            {
                Finish();
                return;
            }
            var part = parts[at];
            if (part.Effect is { } effect)
                Effect(effect, step.Definition.Id, step.Card, copy: false, struck, () => Continue(() => Part(at + 1)));
            else if (part.Program is { Program: { } program })
                RunProgram(step, program, () => Continue(() => Part(at + 1)));
            else if (part.Remedy is { } remedy)
                Remedy(step, remedy.Stacks, remedy.BlockPer, () => Continue(() => Part(at + 1)));
            else
                Part(at + 1);
        }

        // Brand and Crossfire mark whom the Speak actually reached, once each.
        void Finish()
        {
            var marks = new List<(CombatantId Enemy, StatusDefinitionId Status, int Stacks)>();
            if (brand.Status is { } burn)
            {
                var given = 0;
                foreach (var enemy in struck.Distinct())
                    if (given + brand.Each <= brand.Cap)
                    {
                        marks.Add((enemy, burn, brand.Each));
                        given += brand.Each;
                    }
            }
            if (crossfire.Status is { } extra && crossfire.Amount > 0)
                foreach (var enemy in struck.Distinct().Take(2))
                    marks.Add((enemy, extra, crossfire.Amount));
            marks.RemoveAll(m => !_combat.GetCombatant(m.Enemy).IsAlive);
            if (marks.Count == 0)
            {
                next();
                return;
            }
            foreach (var (enemy, status, stacks) in marks)
                _combat.EnqueueEffect(new ApplyStatusEffectRequest(enemy, status, Bearer, step.Definition.Id, Stacks: stacks));
            Continue(next);
        }

        Part(0);
    }

    // A list of frozen effects, in order, each waiting for the one before it.
    private void Apply(IReadOnlyList<WandFrozenEffect> effects, CardDefinitionId? card, CardInstanceId? instance, bool copy, Action next)
    {
        var struck = new List<CombatantId>();
        void At(int index)
        {
            if (_combat.Result != CombatResult.Ongoing)
                return;
            if (index >= effects.Count)
            {
                next();
                return;
            }
            Effect(effects[index], card, instance, copy, struck, () => Continue(() => At(index + 1)));
        }
        At(0);
    }

    private void Atoms(WandStep step, IReadOnlyList<WandAtom> atoms, Action next)
    {
        var parts = Evaluate(step, atoms);
        Deliver(step, parts, (null, 0, 0), (null, 0), next);
    }

    // One effect: every hit its own request, each landing on whoever it should land on at that moment.
    private void Effect(WandFrozenEffect effect, CardDefinitionId? card, CardInstanceId? instance, bool copy,
        List<CombatantId> struck, Action next)
    {
        _wand.CopyingNow = copy;
        var hits = effect.Kind == WandEffectKind.Damage ? Math.Max(1, effect.Hits) : 1;
        void Hit(int at)
        {
            if (_combat.Result != CombatResult.Ongoing)
                return;
            if (at >= hits)
            {
                _wand.CopyingNow = false;
                next();
                return;
            }
            foreach (var target in TargetsOf(effect, at))
            {
                if (effect.Kind == WandEffectKind.Damage)
                    struck.Add(target);
                _combat.EnqueueEffect(effect.Kind switch
                {
                    WandEffectKind.Damage => new DealDamageEffectRequest(target, effect.Amount, Bearer, card),
                    WandEffectKind.Block => new GainBlockEffectRequest(Bearer, effect.Amount, Bearer, card),
                    WandEffectKind.WardWax when _rules.WardWax is { } wax =>
                        new ApplyStatusEffectRequest(Bearer, wax, Bearer, card, Stacks: effect.Amount),
                    _ => new ApplyStatusEffectRequest(target, effect.Status!.Value, Bearer, card, Stacks: effect.Amount),
                });
            }
            Continue(() => Hit(at + 1));
        }
        if (effect.Kind == WandEffectKind.WardWax && _rules.WardWax is null
            || effect.Kind == WandEffectKind.Status && effect.Status is null)
        {
            _wand.CopyingNow = false;
            next();
            return;
        }
        _ = instance;
        Hit(0);
    }

    private IEnumerable<CombatantId> TargetsOf(WandFrozenEffect effect, int hit)
    {
        if (effect.Kind is WandEffectKind.Block or WandEffectKind.WardWax || effect.Targeting == WandTargeting.Self)
            return [Bearer];
        if (effect.Targeting == WandTargeting.AllEnemies)
            return Enemies();
        // Crossfire: odd hits go to a second enemy while there is one.
        if (effect.Alternate && hit % 2 == 1 && Aim(null) is { } first
            && Enemies().Where(e => e != first).OrderBy(e => _combat.GetCombatant(e).Health.Current).FirstOrDefault() is { value: not null } second)
            return [second];
        return Aim(null) is { } aimed ? [aimed] : [];
    }

    private CombatantId? Aim(CombatantId? _) => WandCompiler.AimAt(_combat, Bearer, _plan.Target);

    private List<CombatantId> Enemies()
    {
        var team = _combat.GetCombatant(Bearer).TeamId;
        return [.. _combat.TurnOrder.Where(id => _combat.GetCombatant(id) is { IsAlive: true } c && c.TeamId != team)];
    }

    private void RunProgram(WandStep step, EffectProgram<CardPlayContext> program, Action next)
    {
        var context = WandCompiler.ContextFor(_combat, step.Definition, step.Card, Bearer, _plan.Target);
        EffectProgramExecutor.Execute(program, context, _combat, onComplete: _ => next(), registry: _registry.EffectNodeExecutors);
    }

    private void Store(WandStoreTrigger trigger, IEnumerable<WandFrozenEffect> effects, CardDefinitionId source)
    {
        var frozen = effects.Where(e => e.Amount > 0).ToList();
        if (frozen.Count == 0)
            return;
        _wand.StoredSpell = new WandStoredSpell(trigger, [.. frozen], source);
        _storedThisCast = true;
        _combat.AddLogEntry(StandardCombatLogTypes.WandCast, $"A spell is kept for {trigger}.");
    }

    // ── the few instructions that do something of their own while the cast runs ───────────────────────────

    private void Quench(WandStep step, Action next)
    {
        if (_rules.Singe is not { } singe || Aim(null) is not { } aimed)
        {
            next();
            return;
        }
        var spent = Math.Min(step.Imprint.Amount, Stacks(aimed, singe));
        if (spent > 0)
            _combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(aimed, singe, -spent));
        Continue(next);
    }

    private void Remedy(WandStep step, int stacks, int blockPer, Action next)
    {
        var bearer = _combat.GetCombatant(Bearer);
        var removed = 0;
        foreach (var status in bearer.Statuses.Where(s => s.Polarity == StatusPolarity.Debuff).ToList())
        {
            if (removed >= stacks)
                break;
            var take = Math.Min(stacks - removed, status.Stacks);
            if (take <= 0)
                continue;
            _combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(Bearer, status.DefinitionId, -take));
            removed += take;
        }
        if (removed > 0 && blockPer > 0)
            _combat.EnqueueEffect(new GainBlockEffectRequest(Bearer, removed * blockPer, Bearer, step.Definition.Id));
        Continue(next);
    }

    private void Fourfold(WandStep step, Action next)
    {
        var kinds = 0;
        foreach (var flag in new[] { WandElement.SingeApplied, WandElement.FlowGained, WandElement.GritGained, WandElement.GustGained })
            if (((WandElement)_wand.ElementsThisTurn).HasFlag(flag))
                kinds++;
        var imprint = step.Imprint;
        // The plan counted the relief it could see coming; settle it to what happened.
        var projected = step.Ground;
        var actual = kinds >= 3 ? imprint.Amount2 : 0;
        _relief += actual - projected;
        if (kinds >= 4)
            _focusAfter += imprint.Cap;
        if (kinds >= 2 && imprint.Amount > 0)
        {
            _combat.EnqueueEffect(new GainBlockEffectRequest(Bearer, imprint.Amount, Bearer, step.Definition.Id));
            Continue(next);
            return;
        }
        next();
    }

    private void FinalWord(WandStep step, Action next)
    {
        var imprint = step.Imprint;
        var kinds = Math.Min(Math.Max(1, imprint.Cap), _executedKinds.Count);
        if (kinds == 0)
        {
            next();
            return;
        }
        var extra = Math.Min(kinds - 1, _wand.Focus);
        _wand.Focus -= extra;
        var hits = 1 + extra;
        Apply([new WandFrozenEffect(WandEffectKind.Damage, imprint.Amount, hits)], step.Definition.Id, step.Card, copy: false, next);
    }

    private int Stacks(CombatantId combatant, StatusDefinitionId? status) =>
        status is { } id && _combat.TryGetCombatant(combatant, out var holder)
            ? holder!.Statuses.Where(s => s.DefinitionId == id).Sum(s => s.Stacks)
            : 0;

    // ── the settlement ─────────────────────────────────────────────────────────────────────────────────────

    private void Settle()
    {
        // Flow: what was there when the cast began refunds what it cost, never what the cast itself brought.
        var refund = Math.Min(_oldFlow, Math.Min(Stacks(Bearer, _rules.Flow), _plan.FocusCost));
        var points = 0;
        if (refund > 0 && Hook(WandHook.BlueInkstone) && !_wand.TurnLatches.Contains(nameof(WandHook.BlueInkstone)))
        {
            var most = Math.Min(refund, _rules.InkstonePoints);
            var options = Enumerable.Range(0, most + 1)
                .Select(n => n == 0 ? "Refund it all as Focus" : $"{n} point(s) lower Exhaustion instead")
                .ToList();
            points = Math.Clamp(Choose(options, "Blue Inkstone: refund Focus or ease the wand"), 0, most);
            if (points > 0)
                _wand.TryLatchTurn(nameof(WandHook.BlueInkstone));
        }
        if (refund > 0 && _rules.Flow is { } flow)
            _combat.EnqueueEffect(new ModifyStatusStacksEffectRequest(Bearer, flow, -refund));
        _wand.Focus = Math.Min(_rules.MaxFocus, _wand.Focus + refund - points);

        var load = Math.Max(0, _plan.Exhaustion - _relief - points);
        _wand.Exhaustion = Math.Clamp(_wand.Exhaustion + load, 0, _wand.ExhaustionCap);
        _wand.GroundCredit = 0;
        _wand.InscribedSinceCast = 0;
        foreach (var boost in _plan.Boosts)
            _combat.EnqueueEffect(new RemoveStatusEffectRequest(Bearer, boost.Status));

        // Burned cards leave the wand for the Exhaust pile, through the ordinary move (CardMovedToZone hears it).
        foreach (var card in _burns.Distinct())
            if (_wand.Holds(card))
            {
                _burnedSlots.Add(_wand.SlotOf(card));
                WandMoves.Exhaust(_combat, _wand, Bearer, card);
            }

        Continue(AfterCast);
    }

    private void AfterCast()
    {
        void Program(int at)
        {
            if (_combat.Result != CombatResult.Ongoing)
                return;
            if (at >= _afterCast.Count)
            {
                Moves();
                return;
            }
            var (step, atom) = _afterCast[at];
            if (atom.Program is { } program)
                RunProgram(step, program, () => Continue(() => Program(at + 1)));
            else
                Program(at + 1);
        }

        if (_flowAfter > 0 && _rules.Flow is { } flow)
            _combat.EnqueueEffect(new ApplyStatusEffectRequest(Bearer, flow, Bearer, null, Stacks: _flowAfter));
        if (_focusAfter > 0)
            _wand.Focus = Math.Min(_rules.MaxFocus, _wand.Focus + _focusAfter);
        Continue(() => Program(0));
    }

    // What moves once the cast is over: Slip, Rotate, the relics that hand a card back or fill a burned slot.
    private void Moves()
    {
        foreach (var step in _plan.Steps.Where(s => !s.Fizzled && s.Imprint.Rule == WandRule.Slip))
            if (_wand.Holds(step.Card) && WandMoves.Slip(_wand, step.Card, bothWays: step.Imprint.Amount > 0))
                _wand.Focus = Math.Min(_rules.MaxFocus, _wand.Focus + 1);

        if (_plan.FullWand && _plan.Steps.FirstOrDefault(s => !s.Fizzled && s.Imprint.Rule == WandRule.Rotate) is { } rotate)
        {
            var left = rotate.Imprint.Amount > 0
                && Choose(["Rotate right", "Rotate left"], "A Perfect Copy: which way") == 1;
            WandMoves.Rotate(_wand, left);
        }

        if (Hook(WandHook.SootyFerrule) && _wand.SingeSpentThisCast > 0 && _wand.TryLatchTurn(nameof(WandHook.SootyFerrule)))
            _wand.Focus = Math.Min(_rules.MaxFocus, _wand.Focus + _rules.SootyFerruleFocus);

        if (Hook(WandHook.KnottedWristcord) && _plan.Executed is 1 or 2
            && _wand.TryLatchTurn(nameof(WandHook.KnottedWristcord)))
        {
            var candidates = _plan.Steps.Where(s => _wand.Holds(s.Card)).ToList();
            if (candidates.Count > 0)
            {
                var names = candidates.Select(s => s.Definition.DisplayNameKey).Prepend("Keep them in the wand").ToList();
                var pick = Choose(names, "Knotted Wristcord: take a card back to your hand");
                if (pick > 0)
                    WandMoves.ToHand(_combat, _wand, Bearer, candidates[pick - 1].Card);
            }
        }

        // Ash Bookcord: a slot this cast burned may be filled from the hand, free.
        foreach (var slot in _burnedSlots)
            WandActions.AshBookcord(_combat, _registry, _rules, _wand, Bearer, slot);

        _wand.Casting = false;
        _combat.AddLogEntry(StandardCombatLogTypes.WandCast,
            $"The wand settled: Exhaustion {_wand.Exhaustion}/{_wand.ExhaustionCap}, Focus {_wand.Focus}.");
        _combat.EnqueueEvent(new WandCastResolvedCombatEvent(Bearer, _plan.Executed, _speaksResolved));
        if (_onComplete is { } done)
            _combat.EnqueueContinuation(done);
    }

    private bool Hook(WandHook hook) =>
        _rules.Hooks.TryGetValue(hook, out var status)
        && _combat.GetCombatant(Bearer).Statuses.Any(s => s.DefinitionId == status);

    private int Choose(IReadOnlyList<string> options, string purpose)
    {
        if (_combat.OptionChooser is not { } chooser)
            return 0;
        var picked = chooser.ChooseOptions(options, 1, purpose);
        return picked is { Count: > 0 } && picked[0] >= 0 && picked[0] < options.Count ? picked[0] : 0;
    }
}
