using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Composition;

// Translates the editor SandboxModel into a runnable Scenario (blueprint + step script). It assembles
// ONLY existing engine nodes — the same discipline as the rest of the harness, so the sandbox can never
// introduce combat semantics the engine doesn't already own.
public sealed class ScenarioComposer
{
    // Scripted (one-shot) mode: build the blueprint AND the per-round hero/enemy step script.
    public Playthrough Compose(SandboxModel model)
    {
        var blueprint = BuildBlueprint(model);
        var rounds = Math.Max(model.Rounds.Count, 1);
        var steps = BuildScript(model, rounds);
        return new Playthrough(blueprint, steps, combatId: "sandbox");
    }

    // Interactive mode: build the blueprint (no hero script — the player acts live) and an enemy-intent
    // selector that maps each enemy + 1-based round to the action it should use that round.
    public InteractiveCombat StartInteractive(SandboxModel model)
    {
        var blueprint = BuildBlueprint(model);
        var compiled = blueprint.Compile();

        var enemyBySlug = new Dictionary<string, EnemyModel>(StringComparer.Ordinal);
        foreach (var enemy in model.Enemies)
            enemyBySlug[Slug(enemy.Name)] = enemy;

        EnemyActionDefinitionId? EnemyIntent(CombatantId enemyId, int round)
        {
            if (!enemyBySlug.TryGetValue(enemyId.value, out var enemy) || enemy.Intents.Count == 0)
                return null;

            var index = Math.Min(Math.Max(0, round - 1), enemy.Intents.Count - 1);
            return new EnemyActionDefinitionId($"{Slug(enemy.Name)}_intent{index}");
        }

        return new InteractiveCombat(compiled, EnemyIntent);
    }

    private static ScenarioBlueprint BuildBlueprint(SandboxModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Hero is null)
            throw new InvalidOperationException("The sandbox needs a hero.");
        if (model.Enemies.Count == 0)
            throw new InvalidOperationException("The sandbox needs at least one enemy.");

        var blueprint = new ScenarioBlueprint
        {
            // Full-hand mode draws the whole pool each turn so every authored card is always playable;
            // real-deck mode draws a fixed number and lets normal zone movement happen.
            CardsDrawnPerTurn = model.Hero.UseRealDeck
                ? Math.Max(0, model.Hero.DrawPerTurn)
                : Math.Max(5, model.Cards.Count),
        };

        AddCustomStatuses(model, blueprint);
        AddDefensivePools(model, blueprint);
        AddCards(model, blueprint);
        AddHero(model, blueprint);
        AddEnemies(model, blueprint);
        return blueprint;
    }

    private static void AddCustomStatuses(SandboxModel model, ScenarioBlueprint blueprint)
    {
        foreach (var status in model.Statuses)
        {
            var slug = Slug(status.Name);
            var bp = new StatusBlueprint(slug)
            {
                Polarity = status.Polarity,
                UsesStacks = true,
                UsesDuration = status.UsesDuration,
            };
            if (status.HasPassiveModifier)
                bp.PassiveModifiers.Add(new PassiveModifierSpec(status.Pipeline, status.Operation, status.Magnitude));
            blueprint.Statuses.Add(bp);

            for (var i = 0; i < status.Triggers.Count; i++)
            {
                var trigger = status.Triggers[i];
                if (trigger.Effects.Count == 0)
                    continue;
                blueprint.TriggeredPrograms.Add(BuildTrigger(slug, i, trigger));
            }

            var statusId = new StatusDefinitionId(slug);
            if (status.PreventsDeath)
                blueprint.PreDownInterceptors.Add(new StatusDeathPreventionInterceptor(
                    statusId, status.SurvivingHealth, BuildRequestFactory(status.OnPreventEffects)));
            if (status.BlocksDebuffs)
                blueprint.StatusApplicationInterceptors.Add(new StatusBlocksApplicationInterceptor(
                    statusId, StatusPolarity.Debuff, BuildRequestFactory(status.OnBlockEffects)));
        }
    }

    // Translates a status' interceptor effects into a request factory. Interceptors run outside a program,
    // so only leaf effects with constant amounts are supported (targets resolved by team at fire time).
    private static InterceptorEffects BuildRequestFactory(IReadOnlyList<EffectLineModel> lines)
    {
        var effects = lines.Where(l => l.Line == LineKind.Effect).ToList();
        return (bearer, combat, registry) =>
        {
            var requests = new List<IEffectRequest>();
            foreach (var line in effects)
            {
                var amount = Math.Max(0, line.Amount);
                foreach (var targetId in ResolveInterceptorTargets(line.Target, bearer, combat))
                {
                    IEffectRequest? request = line.Kind switch
                    {
                        EffectKind.DealDamage => new DealDamageEffectRequest(targetId, amount, bearer.Id),
                        EffectKind.GainBlock => new GainBlockEffectRequest(targetId, amount, bearer.Id),
                        EffectKind.Heal => new HealEffectRequest(targetId, amount, bearer.Id),
                        EffectKind.ApplyStatus => new ApplyStatusEffectRequest(
                            targetId, new StatusDefinitionId(line.StatusId), bearer.Id, Stacks: amount, DurationTurns: line.DurationTurns),
                        EffectKind.Cleanse => new RemoveStatusesByPolarityEffectRequest(targetId, line.Polarity),
                        EffectKind.RemoveStatus => new RemoveStatusEffectRequest(targetId, new StatusDefinitionId(line.StatusId)),
                        _ => null,
                    };
                    if (request is not null)
                        requests.Add(request);
                }
            }
            return requests;
        };
    }

    private static IEnumerable<CombatantId> ResolveInterceptorTargets(
        EffectTarget target, CombatantState bearer, CombatState combat) => target switch
        {
            EffectTarget.AllEnemies => combat.Combatants.Where(c => c.IsAlive && c.TeamId != bearer.TeamId).Select(c => c.Id).ToList(),
            EffectTarget.AllAllies => combat.Combatants.Where(c => c.IsAlive && c.TeamId == bearer.TeamId && c.Id != bearer.Id).Select(c => c.Id).ToList(),
            EffectTarget.AllCombatants => combat.Combatants.Where(c => c.IsAlive).Select(c => c.Id).ToList(),
            _ => new List<CombatantId> { bearer.Id }, // Self / Target / extremes → the bearer
        };

    // Builds one status-bound triggered program: it fires on the event, but only for combatants that carry
    // the status (the *HasStatus filter), and its effects target relative to the bearer.
    private static ITriggeredEffectDefinition BuildTrigger(string statusSlug, int index, StatusTriggerModel trigger)
    {
        var id = new TriggeredEffectDefinitionId($"{statusSlug}_trigger{index}");
        var statusId = new StatusDefinitionId(statusSlug);
        var selector = TriggerSelector(trigger.Event);

        return trigger.Event switch
        {
            TriggerEvent.TurnStarted => TriggeredProgramContextAdapters.TurnStarted.Define(
                id, BuildProgram<TurnStartedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new TurnStartedCombatantHasStatusTriggerFilter(statusId)]),
            TriggerEvent.TurnEnded => TriggeredProgramContextAdapters.TurnEnded.Define(
                id, BuildProgram<TurnEndedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new TurnEndedCombatantHasStatusTriggerFilter(statusId)]),
            TriggerEvent.DamageTaken => TriggeredProgramContextAdapters.DamageReceived.Define(
                id, BuildProgram<DamageReceivedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new DamageReceivedReceiverHasStatusTriggerFilter(statusId)]),
            TriggerEvent.DamageDealt => TriggeredProgramContextAdapters.DamageDealt.Define(
                id, BuildProgram<DamageDealtTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new DamageDealtSourceHasStatusTriggerFilter(statusId)]),
            TriggerEvent.Healed => TriggeredProgramContextAdapters.Healed.Define(
                id, BuildProgram<HealedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new HealedTargetHasStatusTriggerFilter(statusId)]),
            TriggerEvent.CardPlayed => TriggeredProgramContextAdapters.CardPlayed.Define(
                id, BuildProgram<CardPlayedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new CardPlayedSourceHasStatusTriggerFilter(statusId)]),
            TriggerEvent.Downed => TriggeredProgramContextAdapters.CombatantDowned.Define(
                id, BuildProgram<CombatantDownedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new CombatantDownedHasStatusTriggerFilter(statusId)]),
            TriggerEvent.StatusExpired => TriggeredProgramContextAdapters.StatusExpired.Define(
                id, BuildProgram<StatusExpiredTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new StatusExpiredStatusDefinitionTriggerFilter(statusId)]),
            TriggerEvent.ResourceGained => TriggeredProgramContextAdapters.ResourceGained.Define(
                id, BuildProgram<ResourceGainedTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new ResourceGainedSourceHasStatusTriggerFilter(statusId)]),
            TriggerEvent.CardCostPaid => TriggeredProgramContextAdapters.CardCostPaid.Define(
                id, BuildProgram<CardCostPaidTriggeredEffectContext>(trigger.Effects, selector)!,
                filters: [new CardCostPaidSourceHasStatusTriggerFilter(statusId)]),
            TriggerEvent.StatusApplied => TriggeredProgramContextAdapters.StatusApplied.Define(
                id, BuildProgram<StatusAppliedTriggeredEffectContext>(trigger.Effects, selector)!,
                // Fire only when the bearer gains some OTHER status (never on the marker's own application).
                filters:
                [
                    new StatusAppliedTargetHasStatusTriggerFilter(statusId),
                    new StatusAppliedExceptStatusDefinitionTriggerFilter(statusId),
                ]),
            TriggerEvent.RoundStarted => TriggeredProgramContextAdapters.RoundStarted.Define(
                id, BuildRoundProgram<RoundStartedTriggeredEffectContext>(trigger.Effects, statusId)),
            TriggerEvent.RoundEnded => TriggeredProgramContextAdapters.RoundEnded.Define(
                id, BuildRoundProgram<RoundEndedTriggeredEffectContext>(trigger.Effects, statusId)),
            _ => throw new InvalidOperationException($"Unknown trigger event '{trigger.Event}'."),
        };
    }

    // Target mapping for a trigger's effects. "Self" is always the status bearer; "Target" is the other
    // party in the event (attacker for DamageTaken, victim for DamageDealt; nothing for turn events).
    private static Func<EffectTarget, ICombatantTargetSelector> TriggerSelector(TriggerEvent ev)
    {
        var bearerIsSource = ev is TriggerEvent.TurnStarted or TriggerEvent.TurnEnded
            or TriggerEvent.DamageDealt or TriggerEvent.CardPlayed
            or TriggerEvent.ResourceGained or TriggerEvent.CardCostPaid;
        // Only Self/Target carry the bearer/other meaning; every other selector resolves relative to the
        // event source (the bearer for source-events) exactly as in a card.
        return target => target switch
        {
            EffectTarget.Self => bearerIsSource ? CombatantTargetSelectors.Source : CombatantTargetSelectors.EventTarget,
            EffectTarget.Target => bearerIsSource ? CombatantTargetSelectors.EventTarget : CombatantTargetSelectors.Source,
            _ => ToSelector(target),
        };
    }

    // Custom defensive pools that genuinely absorb damage. AbsorbsBeforeBlock maps to a negative priority so
    // it drains ahead of Block (priority 0); otherwise a positive priority drains after it.
    private static void AddDefensivePools(SandboxModel model, ScenarioBlueprint blueprint)
    {
        foreach (var pool in model.DefensivePools)
        {
            if (string.IsNullOrWhiteSpace(pool.Name))
                continue;
            blueprint.DefensivePools.Add(new DefensivePoolDefinition(
                new DefensivePoolId(Slug(pool.Name)),
                AbsorbPriority: pool.AbsorbsBeforeBlock ? -10 : 10,
                ClearsOnOwnerTurnStart: pool.ClearsEachTurn));
        }
    }

    private static void AddCards(SandboxModel model, ScenarioBlueprint blueprint)
    {
        foreach (var card in model.Cards)
        {
            var bp = new CardBlueprint(Slug(card.Name));
            if (card.Cost > 0)
                bp.Cost(StandardCombatIds.EnergyResource, card.Cost);
            foreach (var extra in card.ExtraCosts)
                if (!string.IsNullOrWhiteSpace(extra.ResourceName) && extra.Amount > 0)
                    bp.Cost(new ResourceId(Slug(extra.ResourceName)), extra.Amount);
            foreach (var tag in card.Tags)
                if (!string.IsNullOrWhiteSpace(tag))
                    bp.Tags.Add(new TagId(tag));
            bp.RetainInHandOnTurnEnd = card.RetainInHand;
            bp.TurnEndHandDestinationZone = card.TurnEndZone;
            bp.PlayedCardDestinationZone = card.PlayedZone;
            bp.Program = BuildProgram<CardPlayContext>(card.Effects, ToSelector);
            blueprint.Cards.Add(bp);
        }
    }

    private static void AddHero(SandboxModel model, ScenarioBlueprint blueprint)
    {
        var hero = new HeroBlueprint(Slug(model.Hero.Name)) { MaxHealth = Math.Max(1, model.Hero.Hp) };
        var energy = Math.Max(0, model.Hero.Energy);
        hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, energy, energy));

        // Custom resources: give the hero each pool and, if it refills, install the turn-start top-up handler.
        foreach (var resource in model.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Name))
                continue;
            var resourceId = new ResourceId(Slug(resource.Name));
            var max = Math.Max(0, resource.Max);
            hero.Resources.Add(new ResourceSpec(resourceId, Math.Clamp(resource.Start, 0, max), max));
            if (resource.RefillEachTurn)
                blueprint.TurnStartResourceRefills.Add(new ResourceRefillSpec(resourceId, max));
        }

        if (model.Hero.UseRealDeck)
        {
            foreach (var entry in model.Hero.Deck)
                if (!string.IsNullOrWhiteSpace(entry.CardName) && entry.Copies > 0)
                    hero.Deck.Add(new DeckEntry(new CardDefinitionId(Slug(entry.CardName)), entry.Copies));
        }
        else
        {
            // Full-hand mode: one of every defined card.
            foreach (var card in model.Cards)
                hero.Deck.Add(new DeckEntry(new CardDefinitionId(Slug(card.Name))));
        }

        AddStartingStatuses(hero, model.Hero.StartingStatuses);
        blueprint.Hero = hero;
    }

    private static void AddEnemies(SandboxModel model, ScenarioBlueprint blueprint)
    {
        foreach (var enemy in model.Enemies)
        {
            var enemyId = Slug(enemy.Name);
            var eb = new EnemyBlueprint(enemyId) { MaxHealth = Math.Max(1, enemy.Hp) };
            AddStartingStatuses(eb, enemy.StartingStatuses);

            for (var i = 0; i < enemy.Intents.Count; i++)
            {
                var intent = enemy.Intents[i];
                var actionId = $"{enemyId}_intent{i}";
                blueprint.EnemyActions.Add(new EnemyActionBlueprint(
                    actionId,
                    new ActionIntent(string.IsNullOrWhiteSpace(intent.Label) ? "Act" : intent.Label, intent.Kind))
                {
                    Program = BuildProgram<EnemyActionContext>(intent.Effects, ToSelector),
                });
                eb.Actions.Add(new EnemyActionDefinitionId(actionId));
            }

            blueprint.Enemies.Add(eb);
        }
    }

    private static void AddStartingStatuses(CombatantBlueprint blueprint, List<StartingStatusModel> statuses)
    {
        foreach (var status in statuses)
            if (!string.IsNullOrWhiteSpace(status.StatusId) && (status.Amount > 0 || status.DurationTurns > 0))
                blueprint.StartingStatuses.Add(new StartingStatusSpec(
                    new StatusDefinitionId(status.StatusId), Stacks: status.Amount, DurationTurns: status.DurationTurns));
    }

    private static IReadOnlyList<ScenarioStep> BuildScript(SandboxModel model, int rounds)
    {
        var script = new ScenarioScript();

        for (var round = 0; round < rounds; round++)
        {
            if (round < model.Rounds.Count)
                foreach (var play in model.Rounds[round].HeroPlays)
                    if (!string.IsNullOrWhiteSpace(play.CardName))
                        script.HeroPlays(
                            Slug(play.CardName),
                            string.IsNullOrWhiteSpace(play.TargetEnemy) ? null : Slug(play.TargetEnemy));

            script.HeroEndsTurn();

            foreach (var enemy in model.Enemies)
            {
                if (enemy.Intents.Count == 0)
                    continue;

                // Round i uses intent i, clamped to the last defined intent.
                var index = Math.Min(round, enemy.Intents.Count - 1);
                // Enemy intents target the hero (EventTarget = hero); self/AoE selectors ignore it.
                script.EnemyActs(Slug(enemy.Name), $"{Slug(enemy.Name)}_intent{index}", Slug(model.Hero.Name));
            }

            if (round < rounds - 1)
                script.NextRound();
        }

        return script.Build();
    }

    // ── Effect → engine node translation ─────────────────────────────────────────

    private static EffectProgram<TContext>? BuildProgram<TContext>(
        IReadOnlyList<EffectLineModel> effects,
        Func<EffectTarget, ICombatantTargetSelector> selector)
        where TContext : class
    {
        if (effects.Count == 0)
            return null; // a card / action with no effect lines does nothing

        return new EffectProgram<TContext>(BuildRoot<TContext>(effects, selector));
    }

    // A single node for a list of effect lines (a Sequence when more than one; a no-op when empty).
    private static IEffectNode<TContext> BuildRoot<TContext>(
        IReadOnlyList<EffectLineModel> effects,
        Func<EffectTarget, ICombatantTargetSelector> selector)
        where TContext : class
    {
        if (effects.Count == 0)
            return new NoOpEffectNode<TContext>();

        var nodes = effects.Select(e => ToNode<TContext>(e, selector)).ToArray();
        return nodes.Length == 1 ? nodes[0] : new SequenceEffectNode<TContext>(nodes);
    }

    private static IEffectNode<TContext> ToNode<TContext>(
        EffectLineModel line,
        Func<EffectTarget, ICombatantTargetSelector> selector)
        where TContext : class => line.Line switch
        {
            LineKind.If => new ConditionalEffectNode<TContext>(
                new ComparisonExpression<TContext>(
                    BuildReadBase<TContext>(line.ConditionLeft, line.Amount, line.AmountStatusId, line.AmountResourceId, line.DefensivePoolName, selector),
                    line.ConditionOp,
                    line.ConditionRightSource == AmountSource.Constant
                        ? new ConstantExpression<TContext>(line.ConditionRight)
                        : BuildReadBase<TContext>(line.ConditionRightSource, line.ConditionRight, line.AmountStatusId, line.AmountResourceId, line.DefensivePoolName, selector)),
                BuildRoot<TContext>(line.Then, selector),
                line.Else.Count > 0 ? BuildRoot<TContext>(line.Else, selector) : null),
            LineKind.Repeat => new RepeatEffectNode<TContext>(
                new ConstantExpression<TContext>(Math.Max(0, line.RepeatCount)),
                BuildRoot<TContext>(line.Body, selector)),
            LineKind.ForEach => new ForEachTargetEffectNode<TContext>(
                selector(line.ForEachOver),
                BuildRoot<TContext>(line.Body, ForEachSelector(selector))),
            LineKind.Causal => line.Body.Count == 0
                ? new NoOpEffectNode<TContext>()
                : new CausalSequenceEffectNode<TContext>(line.Body.Select(e => ToNode<TContext>(e, selector))),
            LineKind.RandomTargets => new RandomTargetSelectionNode<TContext>(
                selector(line.ForEachOver),
                new ConstantExpression<TContext>(Math.Max(0, line.RepeatCount)),
                BuildRoot<TContext>(line.Body, ForEachSelector(selector))),
            LineKind.RepeatUntil => new RepeatUntilEffectNode<TContext>(
                new ComparisonExpression<TContext>(
                    BuildReadBase<TContext>(line.ConditionLeft, line.Amount, line.AmountStatusId, line.AmountResourceId, line.DefensivePoolName, selector),
                    line.ConditionOp,
                    line.ConditionRightSource == AmountSource.Constant
                        ? new ConstantExpression<TContext>(line.ConditionRight)
                        : BuildReadBase<TContext>(line.ConditionRightSource, line.ConditionRight, line.AmountStatusId, line.AmountResourceId, line.DefensivePoolName, selector)),
                BuildRoot<TContext>(line.Body, selector)),
            _ => BuildLeaf<TContext>(line, selector),
        };

    private static IEffectNode<TContext> BuildLeaf<TContext>(
        EffectLineModel line,
        Func<EffectTarget, ICombatantTargetSelector> selector)
        where TContext : class
    {
        var target = selector(line.Target);
        var amount = BuildAmount<TContext>(line, selector);

        // These ops reject negative amounts in the engine, so clamp to ≥ 0 (constants and live reads alike).
        var nonNeg = new MaxExpression<TContext>(amount, new ConstantExpression<TContext>(0));

        return line.Kind switch
        {
            EffectKind.DealDamage => new DealDamageNode<TContext>(target, nonNeg, ignoresBlock: line.IgnoresBlock),
            EffectKind.GainBlock => new GainBlockNode<TContext>(target, nonNeg),
            EffectKind.Heal => new HealNode<TContext>(target, nonNeg),
            EffectKind.ApplyStatus => new ApplyStatusNode<TContext>(
                target, new StatusDefinitionId(line.StatusId), nonNeg, durationTurns: line.DurationTurns),
            EffectKind.DrawCards => new DrawCardsNode<TContext>(CombatantTargetSelectors.Source, nonNeg),
            EffectKind.GainResource => new GainResourceNode<TContext>(
                CombatantTargetSelectors.Source, ResourceIdFor(line.ResourceName), nonNeg, null),
            EffectKind.LoseResource => new LoseResourceNode<TContext>(
                target, ResourceIdFor(line.ResourceName), nonNeg),
            EffectKind.SetHealth => new SetHealthNode<TContext>(target, amount),
            EffectKind.ModifyMaxHealth => new ModifyMaxHealthNode<TContext>(target, amount),
            EffectKind.ModifyStatusStacks => new ModifyStatusStacksNode<TContext>(
                target, new StatusDefinitionId(line.StatusId), amount),
            EffectKind.RemoveStatus => new RemoveStatusNode<TContext>(target, new StatusDefinitionId(line.StatusId)),
            EffectKind.Cleanse => new RemoveStatusesByPolarityNode<TContext>(target, line.Polarity),
            EffectKind.Down => new SetCombatantLifecycleStateNode<TContext>(target, CombatantLifecycleState.Downed),
            EffectKind.Revive => new SetCombatantLifecycleStateNode<TContext>(target, CombatantLifecycleState.Alive),
            EffectKind.ModifyStatusCharges => new ModifyStatusChargesNode<TContext>(
                target, new StatusDefinitionId(line.StatusId), amount),
            EffectKind.ModifyStatusDuration => new ModifyStatusDurationNode<TContext>(
                target, new StatusDefinitionId(line.StatusId), amount),
            EffectKind.ModifyBlock => new ModifyDefensivePoolNode<TContext>(
                target, DefensivePoolIdFor(line.DefensivePoolName), amount),
            EffectKind.ModifyEnergy => new ModifyResourceNode<TContext>(target, ResourceIdFor(line.ResourceName), amount),
            EffectKind.RefillEnergy => new RefillResourceNode<TContext>(
                target, ResourceIdFor(line.ResourceName), Math.Max(0, line.Amount)),
            EffectKind.ChangeTeam => new ChangeCombatantTeamNode<TContext>(target, TeamOf(line.Team)),
            EffectKind.Summon => new SummonCombatantNode<TContext>(
                TeamOf(line.Team), new MaxExpression<TContext>(amount, new ConstantExpression<TContext>(1)),
                new CombatantDefinitionId("sandbox.summon"), "sandbox.summon.name"),
            EffectKind.EndCombat => new SetCombatResultNode<TContext>(line.Result),
            EffectKind.CreateCard => new CreateCardInstanceNode<TContext>(
                CombatantTargetSelectors.Source, new CardDefinitionId(Slug(line.CreateCardName)), CardZone.Hand),
            EffectKind.MoveCard => new MoveCardToZoneNode<TContext>(
                CombatantTargetSelectors.Source, CardInstanceExpr<TContext>(line.CardRef), line.MoveToZone),
            EffectKind.ReplayCard => new ReplayCardProgramNode<TContext>(
                CardInstanceExpr<TContext>(line.CardRef), selector(line.Target)),
            _ => throw new InvalidOperationException($"Unknown effect kind '{line.Kind}'."),
        };
    }

    private static TeamId TeamOf(TeamChoice team) =>
        team == TeamChoice.Player ? StandardCombatIds.PlayerTeam : StandardCombatIds.EnemyTeam;

    // Resolves an authored resource name to its id; an empty name means the built-in Energy resource, so
    // existing "energy" effects keep working unchanged.
    private static ResourceId ResourceIdFor(string? name) =>
        string.IsNullOrWhiteSpace(name) ? StandardCombatIds.EnergyResource : new ResourceId(Slug(name));

    // Resolves an authored defensive-pool name to its id; an empty name means the built-in Block pool, so
    // existing "block" effects keep working unchanged.
    private static DefensivePoolId DefensivePoolIdFor(string? name) =>
        string.IsNullOrWhiteSpace(name) ? StandardCombatIds.BlockDefensivePool : new DefensivePoolId(Slug(name));

    // The authorable card-instance references: the card being played, or the card that fired a CardPlayed
    // trigger. Each resolves to nothing (op no-ops) outside its valid context.
    private static ICardInstanceExpression<TContext> CardInstanceExpr<TContext>(CardRef cardRef)
        where TContext : class =>
        cardRef == CardRef.TriggeringCard
            ? new TriggerEventCardInstanceExpression<TContext>()
            : new PlayedCardInstanceExpression<TContext>();

    // The effect's amount: a read (constant or live state) with an optional arithmetic step on top.
    private static ICombatExpression<TContext, int> BuildAmount<TContext>(
        EffectLineModel line,
        Func<EffectTarget, ICombatantTargetSelector> selector)
        where TContext : class
    {
        var read = BuildReadBase<TContext>(line.AmountSource, line.Amount, line.AmountStatusId, line.AmountResourceId, line.DefensivePoolName, selector);
        if (line.ArithmeticOp == ArithmeticOp.None)
            return read;

        var operand = new ConstantExpression<TContext>(line.ArithmeticOperand);
        return line.ArithmeticOp switch
        {
            ArithmeticOp.Multiply => new MultiplyExpression<TContext>(read, operand),
            ArithmeticOp.Divide => new DivideExpression<TContext>(read, operand),
            ArithmeticOp.Add => new AddExpression<TContext>(read, operand),
            ArithmeticOp.Subtract => new SubtractExpression<TContext>(read, operand),
            ArithmeticOp.Modulo => new RemainderExpression<TContext>(read, operand),
            ArithmeticOp.Min => new MinExpression<TContext>(read, operand),
            ArithmeticOp.Max => new MaxExpression<TContext>(read, operand),
            _ => read,
        };
    }

    // A single-target live read (or constant) bound to the same Self/Target sense as the effect, so it
    // reads the right unit in cards, actions, and triggers. No arithmetic — used for amounts and conditions.
    private static ICombatExpression<TContext, int> BuildReadBase<TContext>(
        AmountSource source,
        int constant,
        string statusId,
        string resourceId,
        string defensivePoolId,
        Func<EffectTarget, ICombatantTargetSelector> selector)
        where TContext : class
    {
        var self = selector(EffectTarget.Self);
        var other = selector(EffectTarget.Target);
        var resource = ResourceIdFor(resourceId);

        return source switch
        {
            AmountSource.SelfCurrentHp => new CombatantCurrentHealthExpression<TContext>(self),
            AmountSource.SelfMissingHp => new CombatantMissingHealthExpression<TContext>(self),
            AmountSource.SelfMaxHp => new CombatantMaxHealthExpression<TContext>(self),
            AmountSource.SelfStatusStacks => new CombatantStatusStacksExpression<TContext>(self, new StatusDefinitionId(statusId)),
            AmountSource.TargetCurrentHp => new CombatantCurrentHealthExpression<TContext>(other),
            AmountSource.TargetMissingHp => new CombatantMissingHealthExpression<TContext>(other),
            AmountSource.TargetMaxHp => new CombatantMaxHealthExpression<TContext>(other),
            AmountSource.TargetBlock => new CombatantDefensivePoolExpression<TContext>(other, StandardCombatIds.BlockDefensivePool),
            AmountSource.TargetDefensivePool => new CombatantDefensivePoolExpression<TContext>(other, DefensivePoolIdFor(defensivePoolId)),
            AmountSource.TargetStatusStacks => new CombatantStatusStacksExpression<TContext>(other, new StatusDefinitionId(statusId)),
            AmountSource.CardsInHand => new CombatantZoneCardCountExpression<TContext>(self, CardZone.Hand),
            AmountSource.EventAmount => EventAmountExpression<TContext>(),
            AmountSource.SelfHealthPercent => new CombatantHealthPercentageExpression<TContext>(self),
            AmountSource.TargetHealthPercent => new CombatantHealthPercentageExpression<TContext>(other),
            AmountSource.SelfEnergy => new CombatantCurrentResourceExpression<TContext>(self, StandardCombatIds.EnergyResource),
            AmountSource.SelfMaxEnergy => new CombatantMaxResourceExpression<TContext>(self, StandardCombatIds.EnergyResource),
            AmountSource.SelfMissingEnergy => new CombatantMissingResourceExpression<TContext>(self, StandardCombatIds.EnergyResource),
            AmountSource.SelfResourceCurrent => new CombatantCurrentResourceExpression<TContext>(self, resource),
            AmountSource.SelfResourceMax => new CombatantMaxResourceExpression<TContext>(self, resource),
            AmountSource.SelfResourceMissing => new CombatantMissingResourceExpression<TContext>(self, resource),
            AmountSource.SelfBuffStacks => new CombatantStacksByPolarityExpression<TContext>(self, StatusPolarity.Buff),
            AmountSource.SelfDebuffStacks => new CombatantStacksByPolarityExpression<TContext>(self, StatusPolarity.Debuff),
            AmountSource.TargetBuffStacks => new CombatantStacksByPolarityExpression<TContext>(other, StatusPolarity.Buff),
            AmountSource.TargetDebuffStacks => new CombatantStacksByPolarityExpression<TContext>(other, StatusPolarity.Debuff),
            AmountSource.RoundNumber => new RoundNumberExpression<TContext>(),
            AmountSource.TurnNumber => new TurnNumberExpression<TContext>(),
            AmountSource.SelfCardsPlayedThisTurn => new CardsPlayedThisTurnExpression<TContext>(self),
            AmountSource.SelfDamageDealtThisTurn => new DamageDealtThisTurnExpression<TContext>(self),
            _ => new ConstantExpression<TContext>(constant), // may be negative (e.g. lower max HP, remove stacks)
        };
    }

    // The HP amount of the event that fired the trigger — heal amount in a Healed trigger, HP damage in a
    // damage trigger. Resolved per trigger-context type; 0 in any context without such an amount.
    private static ICombatExpression<TContext, int> EventAmountExpression<TContext>()
        where TContext : class
    {
        if (typeof(TContext) == typeof(HealedTriggeredEffectContext))
            return (ICombatExpression<TContext, int>)(object)new ContextValueExpression<HealedTriggeredEffectContext>(c => c.CombatEvent.HealedAmount);
        if (typeof(TContext) == typeof(DamageReceivedTriggeredEffectContext))
            return (ICombatExpression<TContext, int>)(object)new ContextValueExpression<DamageReceivedTriggeredEffectContext>(c => c.CombatEvent.HealthDamage);
        if (typeof(TContext) == typeof(DamageDealtTriggeredEffectContext))
            return (ICombatExpression<TContext, int>)(object)new ContextValueExpression<DamageDealtTriggeredEffectContext>(c => c.CombatEvent.HealthDamage);
        if (typeof(TContext) == typeof(ResourceGainedTriggeredEffectContext))
            return (ICombatExpression<TContext, int>)(object)new ContextValueExpression<ResourceGainedTriggeredEffectContext>(c => c.CombatEvent.GainedAmount);

        return new ConstantExpression<TContext>(0);
    }

    // A round trigger has no bearer in the event, so it runs its effects once per marker-bearer via a
    // ForEach. Inside, Self/Target = the current bearer (single → scalar reads work); AllCombatants = all.
    private static EffectProgram<TContext> BuildRoundProgram<TContext>(
        IReadOnlyList<EffectLineModel> effects, StatusDefinitionId statusId)
        where TContext : class
    {
        var bearers = CombatantTargetSelectors.WithStatus(CombatantTargetSelectors.AllAliveCombatants, statusId);
        var body = BuildRoot<TContext>(effects, RoundBodySelector);
        return new EffectProgram<TContext>(new ForEachTargetEffectNode<TContext>(bearers, body));
    }

    private static ICombatantTargetSelector RoundBodySelector(EffectTarget target) =>
        target == EffectTarget.AllCombatants
            ? CombatantTargetSelectors.AllAliveCombatants
            : CombatantTargetSelectors.IterationTarget;

    // Inside a ForEach, "Target" resolves to the current iteration member; everything else is unchanged.
    private static Func<EffectTarget, ICombatantTargetSelector> ForEachSelector(
        Func<EffectTarget, ICombatantTargetSelector> parent) =>
        target => target == EffectTarget.Target ? CombatantTargetSelectors.IterationTarget : parent(target);

    private static ICombatantTargetSelector ToSelector(EffectTarget target) => target switch
    {
        EffectTarget.LowestHpEnemy => CombatantTargetSelectors.LowestHealthEnemyOfSource,
        EffectTarget.HighestHpEnemy => CombatantTargetSelectors.HighestHealthEnemyOfSource,
        EffectTarget.LowestHpAlly => CombatantTargetSelectors.LowestHealthAllyOfSource,
        EffectTarget.HighestHpAlly => CombatantTargetSelectors.HighestHealthAllyOfSource,
        EffectTarget.DamagedAllies => CombatantTargetSelectors.AllDamagedAlliesOfSource,
        EffectTarget.AllCombatants => CombatantTargetSelectors.AllAliveCombatants,
        EffectTarget.Target => CombatantTargetSelectors.EventTarget,
        EffectTarget.Self => CombatantTargetSelectors.Source,
        EffectTarget.AllEnemies => CombatantTargetSelectors.AllEnemiesOfSource,
        EffectTarget.AllAllies => CombatantTargetSelectors.AllAlliesOfSource,
        _ => CombatantTargetSelectors.EventTarget,
    };

    // Turns a display name into a registry-safe id (lowercase, non-alphanumerics → '_').
    internal static string Slug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "x";

        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        var slug = new string(chars).Trim('_');
        return slug.Length == 0 ? "x" : slug;
    }
}
