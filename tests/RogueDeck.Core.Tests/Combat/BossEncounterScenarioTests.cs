using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// ════════════════════════════════════════════════════════════════════════════════
// Boss encounter integration scenario — "The Ashen Conclave".
//
// One scripted multi-round fight (a hero versus the boss Pyraxis and two minions)
// that drives essentially every engine subsystem at once and proves it composes:
//
//   • multi-combatant teams + real turn automation (refill / draw / block-clear / DoT)
//   • the full damage-modifier pipeline (Source → Target → Global) with custom modifiers
//   • defensive pools (gain / modify / drain / clear) and healing
//   • the whole status surface (apply / stacks / charges / drain / merge / block / expire)
//   • cards as Effect Programs (play / draw / create / move / nested play) + cost / validator
//   • enemy actions as first-class program authors
//   • triggers across many event families + the TemporaryRuleActivated meta-trigger
//   • temporary rules and delayed effects (node-installed N-activation, owner-bound)
//   • control flow (CausalSequence / Sequence / ForEach / Repeat / Conditional) and the
//     full expression algebra (arithmetic / comparison / boolean / aggregate / outcome reads)
//   • lifecycle (auto-down at 0 HP, auto-Victory when a whole team is down)
//   • determinism (same script → identical hash; different script → different hash)
//   • registry preflight over all of the above (everything runs through Build()).
//
// Triggers are registered on the combat-independent registry, so units are identified by
// MARKER statuses (molten-core = boss, volatile = wisp, hero-mark = hero) rather than by
// combatant id. No SideEffectNode is used anywhere — only typed nodes.
// ════════════════════════════════════════════════════════════════════════════════
public class BossEncounterScenarioTests
{
    // ── Combatants ───────────────────────────────────────────────────────────────
    private static readonly CombatantId Hero = new("hero_001");
    private static readonly CombatantId Boss = new("boss_pyraxis");
    private static readonly CombatantId Acolyte = new("minion_acolyte");
    private static readonly CombatantId Wisp = new("minion_wisp");
    private static readonly PackageId Pkg = new("ashen");

    // ── Custom statuses ──────────────────────────────────────────────────────────
    private static readonly StatusDefinitionId Soulburn = new("ashen.soulburn");   // DoT + energy drain
    private static readonly StatusDefinitionId Emberward = new("ashen.emberward");  // debuff shield (charges)
    private static readonly StatusDefinitionId Overheat = new("ashen.overheat");    // takes +25% dmg / stack
    private static readonly StatusDefinitionId Tempo = new("ashen.tempo");          // attacks cost 1 less
    private static readonly StatusDefinitionId Silenced = new("ashen.silenced");    // cannot play skills
    private static readonly StatusDefinitionId Enraged = new("ashen.enraged");      // enrage latch
    private static readonly StatusDefinitionId Witnessed = new("ashen.witnessed");  // meta-trigger witness
    private static readonly StatusDefinitionId Scorched = new("ashen.scorched");    // wisp-explosion witness
    private static readonly StatusDefinitionId MoltenCore = new("ashen.molten_core"); // boss marker
    private static readonly StatusDefinitionId VolatileMark = new("ashen.volatile");  // wisp marker
    private static readonly StatusDefinitionId HeroMark = new("ashen.hero_mark");     // hero marker

    // ── Standard statuses reused ─────────────────────────────────────────────────
    private static readonly StatusDefinitionId Poison = StandardCombatIds.PoisonStatus;
    private static readonly StatusDefinitionId Strength = StandardCombatIds.StrengthStatus;
    private static readonly StatusDefinitionId Vulnerable = StandardCombatIds.VulnerableStatus;
    private static readonly StatusDefinitionId Dexterity = StandardCombatIds.DexterityStatus;
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;
    private static readonly DefensivePoolId Block = StandardCombatIds.BlockDefensivePool;

    // ── Cards ────────────────────────────────────────────────────────────────────
    private static readonly CardDefinitionId DoubleSlash = new("ashen.double_slash");
    private static readonly CardDefinitionId BladeStorm = new("ashen.blade_storm");
    private static readonly CardDefinitionId VenomDagger = new("ashen.venom_dagger");
    private static readonly CardDefinitionId Detonate = new("ashen.detonate");
    private static readonly CardDefinitionId Gather = new("ashen.gather");
    private static readonly CardDefinitionId WarCry = new("ashen.war_cry");
    private static readonly CardDefinitionId Conjure = new("ashen.conjure");
    private static readonly CardDefinitionId EmberShard = new("ashen.ember_shard"); // dummy / conjured card

    // ── Enemy actions ────────────────────────────────────────────────────────────
    private static readonly EnemyActionDefinitionId BossSmite = new("ashen.boss_smite");
    private static readonly EnemyActionDefinitionId BossIgnite = new("ashen.boss_ignite");
    private static readonly EnemyActionDefinitionId AcolyteShield = new("ashen.acolyte_shield");
    private static readonly EnemyActionDefinitionId WispBolt = new("ashen.wisp_bolt");

    // ── Triggers / rules ─────────────────────────────────────────────────────────
    private static readonly TriggeredEffectDefinitionId EnrageTrigger = new("ashen.enrage");
    private static readonly TriggeredEffectDefinitionId SoulburnTick = new("ashen.soulburn_tick");
    private static readonly TriggeredEffectDefinitionId WispExplosion = new("ashen.wisp_explosion");
    private static readonly TriggeredEffectDefinitionId MetaWitness = new("ashen.meta_witness");
    private static readonly TriggeredEffectDefinitionId SummonRule = new("ashen.enrage_summon");
    private static readonly TriggeredEffectDefinitionId RegenRule = new("ashen.acolyte_regen");

    // Marker-based selectors (combat-independent: resolve the boss / hero by status).
    private static ICombatantTargetSelector TheBoss =>
        CombatantTargetSelectors.WithStatus(CombatantTargetSelectors.AllAliveCombatants, MoltenCore);
    private static ICombatantTargetSelector TheHero =>
        CombatantTargetSelectors.WithStatus(CombatantTargetSelectors.AllAliveCombatants, HeroMark);

    // ══════════════════════════════════════════════════════════════════════════════
    // TESTS
    // ══════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void FullBossEncounter_RunsToVictory_AndProvesEverySubsystemFired()
    {
        var registry = BuildWorld();
        var combat = NewEncounter(registry);

        Fight(combat, registry);

        // The whole enemy team is down → the standard lifecycle handler declared Victory.
        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(CombatantLifecycleState.Downed, combat.GetCombatant(Boss).LifecycleState);
        Assert.Equal(CombatantLifecycleState.Downed, combat.GetCombatant(Acolyte).LifecycleState);
        Assert.Equal(CombatantLifecycleState.Downed, combat.GetCombatant(Wisp).LifecycleState);

        var boss = combat.GetCombatant(Boss);
        var hero = combat.GetCombatant(Hero);

        // Enrage fired (DamageReceived trigger + health-percentage conditional): the boss latched
        // Enraged and gained Strength, and the hero was branded with Overheat.
        Assert.Contains(boss.Statuses, s => s.DefinitionId == Enraged);
        Assert.Contains(boss.Statuses, s => s.DefinitionId == Strength);
        Assert.Contains(hero.Statuses, s => s.DefinitionId == Overheat);

        // The TemporaryRuleActivated meta-trigger fired at least once → the boss accrued Witnessed
        // stacks (a status only ever touched by that meta-trigger).
        Assert.Contains(boss.Statuses, s => s.DefinitionId == Witnessed && s.Stacks > 0);

        // The volatile wisp's CombatantDowned explosion branded the hero with Poison.
        Assert.Contains(hero.Statuses, s => s.DefinitionId == Poison);

        // The owner-bound regeneration rule (owned by the acolyte) was pruned when the acolyte died.
        Assert.DoesNotContain(combat.TemporaryTriggeredPrograms, p => p.Id == RegenRule);
    }

    [Fact]
    public void BossEncounter_IsDeterministic_SameScriptProducesIdenticalHash()
    {
        var registry = BuildWorld();

        var a = NewEncounter(registry);
        var b = NewEncounter(registry);
        Fight(a, registry);
        Fight(b, registry);

        var hashA = CombatStateHasher.ComputeHash(a.CreateSnapshot());
        var hashB = CombatStateHasher.ComputeHash(b.CreateSnapshot());
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void BossEncounter_DivergesOnDifferentScript_ProducesDifferentHash()
    {
        var registry = BuildWorld();

        // Two non-terminal scripts that differ by a single strike must hash differently.
        var a = NewEncounter(registry);
        var b = NewEncounter(registry);
        Strike(a, registry, Hero, DoubleSlash, Boss);
        Strike(b, registry, Hero, DoubleSlash, Boss);
        Strike(b, registry, Hero, DoubleSlash, Boss); // b strikes once more

        Assert.NotEqual(
            CombatStateHasher.ComputeHash(a.CreateSnapshot()),
            CombatStateHasher.ComputeHash(b.CreateSnapshot()));
    }

    [Fact]
    public void OpeningStrike_AppliesRepeatAndGlobalBrittleArmor()
    {
        var registry = BuildWorld();
        var combat = NewEncounter(registry);

        // Boss starts clean (no Strength/Vulnerable/Overheat, no block). Double Slash hits twice
        // for 6; Brittle Armor (global) adds +2 per hit because the boss has no block.
        // 70 - 2*(6+2) = 54.
        Strike(combat, registry, Hero, DoubleSlash, Boss);
        Assert.Equal(54, combat.GetCombatant(Boss).Health.Current);
    }

    [Fact]
    public void Emberward_BlocksDebuffAndConsumesCharge()
    {
        var registry = BuildWorld();
        var combat = NewEncounter(registry);

        // The acolyte shields the boss → Emberward with 2 charges.
        EnemyAct(combat, registry, Acolyte, AcolyteShield, Boss);
        var boss = combat.GetCombatant(Boss);
        var emberward = boss.Statuses.Single(s => s.DefinitionId == Emberward);
        Assert.Equal(2, emberward.Charges);

        // Venom Dagger tries to apply Poison → blocked by Emberward (one charge spent) → the card's
        // conditional fallback deals direct damage instead.
        Strike(combat, registry, Hero, VenomDagger, Boss);

        Assert.DoesNotContain(boss.Statuses, s => s.DefinitionId == Poison);
        Assert.Equal(1, boss.Statuses.Single(s => s.DefinitionId == Emberward).Charges);
    }

    [Fact]
    public void Tempo_ReducesAttackCardCost()
    {
        var registry = BuildWorld();
        var combat = NewEncounter(registry);
        var hero = combat.GetCombatant(Hero);
        hero.Resources[Energy].SetCurrent(2);

        // War Cry grants Tempo; Double Slash (an attack, base cost 1) then costs 0.
        Strike(combat, registry, Hero, WarCry, Hero);
        Assert.Contains(hero.Statuses, s => s.DefinitionId == Tempo);

        var before = hero.Resources[Energy].Current;
        Strike(combat, registry, Hero, DoubleSlash, Boss);
        Assert.Equal(before, hero.Resources[Energy].Current); // cost reduced to 0 by Tempo
    }

    [Fact]
    public void Silence_RejectsSkillCardButAllowsAttack()
    {
        var registry = BuildWorld();
        var combat = NewEncounter(registry);
        Apply(combat, registry, Hero, Silenced, stacks: 1);

        // Card-play validators run on the synchronous play path. The skill validator rejects a skill
        // card while silenced...
        var processor = new CombatCardPlayProcessor();
        Assert.ThrowsAny<Exception>(() =>
            processor.PlayCard(combat, registry, new CardPlayRequest(Gather, Hero)));

        // ...but an attack card is unaffected by Silence.
        processor.PlayCard(combat, registry, new CardPlayRequest(DoubleSlash, Hero, Boss));
        Assert.True(combat.GetCombatant(Boss).Health.Current < 70);
    }

    [Fact]
    public void NestedCardPlay_ForwardsTargetToWeakestEnemy()
    {
        // PlayCardNode forwards its CardTargetSelector to the nested card. Built per-combat because
        // the nested card is played by instance id.
        var builder = StandardWorldBuilder();
        var inner = new CardDefinitionId("ashen.gambit_inner");
        builder.RegisterCard(new CardDefinitionBuilder(inner, Pkg, "n", "d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(7))),
        });

        var combat = BuildCombatants();
        var innerInstance = Give(combat, Hero, inner);

        var gambit = new CardDefinitionId("ashen.gambit");
        builder.RegisterCard(new CardDefinitionBuilder(gambit, Pkg, "n", "d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new PlayCardNode<CardPlayContext>(
                    playerSelector: CombatantTargetSelectors.Source,
                    cardExpression: new ExplicitCardInstanceExpression<CardPlayContext>(innerInstance.Id),
                    cardTargetSelector: CombatantTargetSelectors.LowestHealthEnemyOfSource)),
        });
        var registry = builder.Build();

        // The wisp is the lowest-health enemy → the nested 7 damage (+2 Brittle Armor, no block)
        // hits it: 12 → 3.
        Strike(combat, registry, Hero, gambit, null);
        Assert.Equal(3, combat.GetCombatant(Wisp).Health.Current);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // THE SCRIPTED FIGHT (deterministic director — direct requests + explicit events)
    // ══════════════════════════════════════════════════════════════════════════════

    private static void Fight(CombatState combat, CombatDefinitionRegistry registry)
    {
        // ── Round 1: the hero opens; the conclave answers ──────────────────────────
        Strike(combat, registry, Hero, WarCry, Hero);         // Tempo + Dexterity (buffs, cost modifier)
        Strike(combat, registry, Hero, Gather, null);         // gain energy → draw (causal outcome chain)
        Strike(combat, registry, Hero, Conjure, null);        // create a card instance + move it to exhaust

        EnemyAct(combat, registry, Acolyte, AcolyteShield, Boss); // heal boss + Emberward + (owner regen pre-installed)
        EnemyAct(combat, registry, Boss, BossIgnite, Hero);       // Soulburn on the hero
        EnemyAct(combat, registry, Wisp, WispBolt, Hero);         // chip damage

        TurnStart(combat, registry, Hero);                    // refill / draw / block-clear / Soulburn tick (dmg + energy loss)

        // ── Round 2: break the shield, push the boss into its enrage phase ─────────
        Strike(combat, registry, Hero, VenomDagger, Boss);    // Poison blocked by Emberward → fallback damage
        Strike(combat, registry, Hero, VenomDagger, Boss);    // Emberward exhausted → Poison sticks
        Strike(combat, registry, Hero, Detonate, Boss);       // consume Poison stacks → burst damage
        Strike(combat, registry, Hero, BladeStorm, null);     // AoE + block = total health lost (aggregate)
        Strike(combat, registry, Hero, DoubleSlash, Boss);    // brings the boss toward its 50% threshold

        // ── Round 3: trip the enrage, cull the minions; explosion + owner-bound expiry resolve ──
        Strike(combat, registry, Hero, BladeStorm, null);     // crosses 50% → Enrage fires; finishes the wisp → explosion (Poison on hero)
        EnemyAct(combat, registry, Boss, BossSmite, Hero);    // boss strikes the now-Overheated hero (Target modifier fires live)
        Strike(combat, registry, Hero, DoubleSlash, Acolyte); // finish the acolyte → owner-bound regen rule pruned
        Strike(combat, registry, Hero, DoubleSlash, Acolyte);

        TurnStart(combat, registry, Hero);                    // another Soulburn tick
        RoundStart(combat, registry, Hero);                   // summon rule fires → TemporaryRuleActivated meta-trigger

        // ── Finish: bring the boss down → whole team down → Victory ────────────────
        for (var i = 0; i < 8 && combat.Result == CombatResult.Ongoing; i++)
            Strike(combat, registry, Hero, DoubleSlash, Boss);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // WORLD BUILDING
    // ══════════════════════════════════════════════════════════════════════════════

    private static CombatDefinitionRegistry BuildWorld() => StandardWorldBuilder().Build();

    private static CombatDefinitionRegistryBuilder StandardWorldBuilder()
    {
        var b = CombatTestFactory.CreateStandardBuilder();

        // (StandardCombatPackage now registers the LoseResourceNode executor itself — the earlier
        // manual workaround here was removed once the package gap was fixed.)

        // Custom statuses.
        RegisterStatus(b, Soulburn, StatusPolarity.Debuff, stacks: true);
        RegisterStatus(b, Emberward, StatusPolarity.Buff, charges: true);
        RegisterStatus(b, Overheat, StatusPolarity.Debuff, stacks: true);
        RegisterStatus(b, Tempo, StatusPolarity.Buff);
        RegisterStatus(b, Silenced, StatusPolarity.Debuff);
        RegisterStatus(b, Enraged, StatusPolarity.Neutral);
        RegisterStatus(b, Witnessed, StatusPolarity.Neutral, stacks: true);
        RegisterStatus(b, Scorched, StatusPolarity.Neutral, stacks: true);
        RegisterStatus(b, MoltenCore, StatusPolarity.Neutral);
        RegisterStatus(b, VolatileMark, StatusPolarity.Neutral);
        RegisterStatus(b, HeroMark, StatusPolarity.Neutral);

        // Custom modifiers / interceptor / validator (the five extension points).
        b.RegisterDamageAmountModifier(new OverheatDamageAmountModifier());
        b.RegisterDamageAmountModifier(new BrittleArmorDamageAmountModifier());
        b.RegisterStatusApplicationInterceptor(new EmberwardStatusApplicationInterceptor());
        b.RegisterCardCostModifier(new TempoAttackCostModifier());
        b.RegisterCardPlayValidator(new SilenceSkillCardPlayValidator());

        RegisterTriggers(b);
        RegisterEnemyActions(b);
        RegisterHeroDeck(b);
        return b;
    }

    private static void RegisterTriggers(CombatDefinitionRegistryBuilder b)
    {
        // Enrage: the boss, on taking damage, latches Enraged once below 50% HP — gaining Strength,
        // branding the attacker (the hero) with Overheat, and installing a recurring summon rule.
        b.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.DamageReceived.Define(
            EnrageTrigger,
            new EffectProgram<DamageReceivedTriggeredEffectContext>(
                new ConditionalEffectNode<DamageReceivedTriggeredEffectContext>(
                    new AndExpression<DamageReceivedTriggeredEffectContext>(
                        new AndExpression<DamageReceivedTriggeredEffectContext>(
                            new TargetHasStatusExpression<DamageReceivedTriggeredEffectContext>(
                                CombatantTargetSelectors.EventTarget, MoltenCore),
                            new NotExpression<DamageReceivedTriggeredEffectContext>(
                                new TargetHasStatusExpression<DamageReceivedTriggeredEffectContext>(
                                    CombatantTargetSelectors.EventTarget, Enraged))),
                        new ComparisonExpression<DamageReceivedTriggeredEffectContext>(
                            new CombatantHealthPercentageExpression<DamageReceivedTriggeredEffectContext>(
                                CombatantTargetSelectors.EventTarget),
                            ComparisonOperator.Less,
                            new ConstantExpression<DamageReceivedTriggeredEffectContext>(50))),
                    then: new CausalSequenceEffectNode<DamageReceivedTriggeredEffectContext>(
                    [
                        new ApplyStatusNode<DamageReceivedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget, Enraged,
                            new ConstantExpression<DamageReceivedTriggeredEffectContext>(1)),
                        new ApplyStatusNode<DamageReceivedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget, Strength,
                            new ConstantExpression<DamageReceivedTriggeredEffectContext>(3)),
                        new ApplyStatusNode<DamageReceivedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, Overheat,
                            new ConstantExpression<DamageReceivedTriggeredEffectContext>(2)),
                        new InstallTemporaryRuleNode<DamageReceivedTriggeredEffectContext>(
                            SummonRuleDefinition(), TemporaryRuleLifetime.Activations(2)),
                    ])))));

        // Soulburn: at the start of the afflicted unit's turn, it takes damage equal to stacks and
        // loses 1 energy (chains DamageDealt → ResourceLost).
        b.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.TurnStarted.Define(
            SoulburnTick,
            new EffectProgram<TurnStartedTriggeredEffectContext>(
                new ConditionalEffectNode<TurnStartedTriggeredEffectContext>(
                    new TargetHasStatusExpression<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget, Soulburn),
                    then: new CausalSequenceEffectNode<TurnStartedTriggeredEffectContext>(
                    [
                        new DealDamageNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget,
                            new CombatantStatusStacksExpression<TurnStartedTriggeredEffectContext>(
                                CombatantTargetSelectors.EventTarget, Soulburn)),
                        new LoseResourceNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.EventTarget, Energy,
                            new ConstantExpression<TurnStartedTriggeredEffectContext>(1)),
                    ])))));

        // Volatile explosion: when a volatile unit is downed, it scorches the hero with Poison.
        // A trigger filter gates it to volatile deaths — checked on the downed combatant directly,
        // because the EventTarget selector excludes the (now downed) target.
        b.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.CombatantDowned.Define(
            WispExplosion,
            new EffectProgram<CombatantDownedTriggeredEffectContext>(
                new SequenceEffectNode<CombatantDownedTriggeredEffectContext>(
                [
                    new ApplyStatusNode<CombatantDownedTriggeredEffectContext>(
                        CombatantTargetSelectors.WithStatus(
                            CombatantTargetSelectors.AllAliveCombatants, HeroMark),
                        Poison, new ConstantExpression<CombatantDownedTriggeredEffectContext>(3)),
                    new ApplyStatusNode<CombatantDownedTriggeredEffectContext>(
                        CombatantTargetSelectors.WithStatus(
                            CombatantTargetSelectors.AllAliveCombatants, HeroMark),
                        Scorched, new ConstantExpression<CombatantDownedTriggeredEffectContext>(1)),
                ])),
            filters: [new VolatileDownedFilter()]));

        // Meta-trigger: every time any temporary rule activates, the boss accrues a Witnessed stack.
        b.RegisterTriggeredEffectDefinition(TriggeredProgramContextAdapters.TemporaryRuleActivated.Define(
            MetaWitness,
            new EffectProgram<TemporaryRuleActivatedTriggeredEffectContext>(
                new ApplyStatusNode<TemporaryRuleActivatedTriggeredEffectContext>(
                    TheBoss, Witnessed,
                    new ConstantExpression<TemporaryRuleActivatedTriggeredEffectContext>(1)))));
    }

    // The recurring summon rule installed by Enrage: each round start it sears the hero.
    private static ITriggeredEffectDefinition SummonRuleDefinition() =>
        TriggeredProgramContextAdapters.RoundStarted.Define(
            SummonRule,
            new EffectProgram<RoundStartedTriggeredEffectContext>(
                new DealDamageNode<RoundStartedTriggeredEffectContext>(
                    TheHero, new ConstantExpression<RoundStartedTriggeredEffectContext>(2))));

    // The acolyte's owner-bound regeneration: while the acolyte lives, the boss heals each round.
    private static ITriggeredEffectDefinition RegenRuleDefinition() =>
        TriggeredProgramContextAdapters.RoundStarted.Define(
            RegenRule,
            new EffectProgram<RoundStartedTriggeredEffectContext>(
                new HealNode<RoundStartedTriggeredEffectContext>(
                    TheBoss, new ConstantExpression<RoundStartedTriggeredEffectContext>(3))));

    private static void RegisterEnemyActions(CombatDefinitionRegistryBuilder b)
    {
        b.RegisterEnemyAction(new EnemyActionDefinitionBuilder(BossSmite, Pkg, "n", "d")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(6))),
        });

        b.RegisterEnemyAction(new EnemyActionDefinitionBuilder(BossIgnite, Pkg, "n", "d")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new ApplyStatusNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, Soulburn,
                    new ConstantExpression<EnemyActionContext>(3))),
        });

        b.RegisterEnemyAction(new EnemyActionDefinitionBuilder(AcolyteShield, Pkg, "n", "d")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new CausalSequenceEffectNode<EnemyActionContext>(
                [
                    new HealNode<EnemyActionContext>(TheBoss, new ConstantExpression<EnemyActionContext>(8)),
                    new ApplyStatusNode<EnemyActionContext>(
                        TheBoss, Emberward, new ConstantExpression<EnemyActionContext>(0), charges: 2),
                ])),
        });

        b.RegisterEnemyAction(new EnemyActionDefinitionBuilder(WispBolt, Pkg, "n", "d")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
    }

    private static void RegisterHeroDeck(CombatDefinitionRegistryBuilder b)
    {
        // A dummy/conjured card with no effect — fills the draw pile and is the conjure product.
        Card(b, EmberShard, cost: 0, attack: false,
            new NoOpEffectNode<CardPlayContext>());

        // Double Slash — attack, hits twice (Repeat), reactions settle between hits.
        Card(b, DoubleSlash, cost: 1, attack: true,
            new RepeatEffectNode<CardPlayContext>(
                new ConstantExpression<CardPlayContext>(2),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(6))));

        // Blade Storm — AoE, then gain block equal to total health actually lost (aggregate read).
        var dmgKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("bladestorm.dmg");
        Card(b, BladeStorm, cost: 2, attack: true,
            new CausalSequenceEffectNode<CardPlayContext>(
            [
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<CardPlayContext>(5), resultKey: dmgKey),
                new ModifyDefensivePoolNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, Block,
                    new PreviousOutcomeSumExpression<CardPlayContext, DamageOutcome>(dmgKey, o => o.HealthLost)),
            ]));

        // Venom Dagger — apply Poison; if blocked (Emberward), deal direct damage instead (branch on outcome).
        var applyKey = new EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>("venom.apply");
        Card(b, VenomDagger, cost: 1, attack: true,
            new CausalSequenceEffectNode<CardPlayContext>(
            [
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, Poison,
                    new ConstantExpression<CardPlayContext>(3), resultKey: applyKey),
                new ConditionalEffectNode<CardPlayContext>(
                    new PreviousOutcomeBoolFieldExpression<CardPlayContext, ApplyStatusOutcome>(
                        applyKey, o => o.Blocked),
                    then: new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(5))),
            ]));

        // Detonate — drain all Poison and deal damage equal to the stacks consumed (outcome read).
        var stacksKey = new EffectResultKey<OrderedTargetOutcomes<ModifyStatusStacksOutcome>>("deto.stacks");
        Card(b, Detonate, cost: 1, attack: false,
            new CausalSequenceEffectNode<CardPlayContext>(
            [
                new ModifyStatusStacksNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, Poison,
                    new ConstantExpression<CardPlayContext>(-999), resultKey: stacksKey),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new PreviousOutcomeFieldExpression<CardPlayContext, ModifyStatusStacksOutcome>(
                        stacksKey, o => o.OldStacks)),
            ]));

        // Gather — gain energy, then draw cards equal to the amount actually gained (outcome read).
        var gainKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gather.gain");
        Card(b, Gather, cost: 0, attack: false,
            new CausalSequenceEffectNode<CardPlayContext>(
            [
                new GainResourceNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, Energy,
                    new ConstantExpression<CardPlayContext>(2), defaultMax: 6, resultKey: gainKey),
                new DrawCardsNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, GainResourceOutcome>(
                        gainKey, o => o.GainedAmount)),
            ]));

        // War Cry — self buffs that feed the custom cost modifier and block modifier.
        Card(b, WarCry, cost: 1, attack: false,
            new SequenceEffectNode<CardPlayContext>(
            [
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, Tempo, new ConstantExpression<CardPlayContext>(1)),
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, Dexterity, new ConstantExpression<CardPlayContext>(2)),
            ]));

        // Conjure — create a card instance, then move that very card to the exhaust pile (create→move chain).
        var createKey = new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("conjure.created");
        Card(b, Conjure, cost: 0, attack: false,
            new CausalSequenceEffectNode<CardPlayContext>(
            [
                new CreateCardInstanceNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, EmberShard, CardZone.Hand, resultKey: createKey),
                new MoveCardToZoneNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new CreateCardOutcomeExpression<CardPlayContext>(createKey),
                    CardZone.ExhaustPile),
            ]));
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // ENCOUNTER SETUP
    // ══════════════════════════════════════════════════════════════════════════════

    private static CombatState NewEncounter(CombatDefinitionRegistry registry)
    {
        var combat = BuildCombatants();
        SeedMarkersAndRules(combat, registry);
        return combat;
    }

    private static CombatState BuildCombatants()
    {
        var combat = new CombatState(new CombatId("ashen_conclave"), randomSeed: 7777);

        combat.AddCombatant(new CombatantState(
            Hero, new CombatantDefinitionId("ashen.hero"), "combatant.hero",
            StandardCombatIds.PlayerTeam, new HealthState(80, 80)));
        combat.AddCombatant(new CombatantState(
            Boss, new CombatantDefinitionId("ashen.boss"), "combatant.boss",
            StandardCombatIds.EnemyTeam, new HealthState(70, 70)));
        combat.AddCombatant(new CombatantState(
            Acolyte, new CombatantDefinitionId("ashen.acolyte"), "combatant.acolyte",
            StandardCombatIds.EnemyTeam, new HealthState(24, 24)));
        combat.AddCombatant(new CombatantState(
            Wisp, new CombatantDefinitionId("ashen.wisp"), "combatant.wisp",
            StandardCombatIds.EnemyTeam, new HealthState(12, 12)));

        combat.GetCombatant(Hero).AddResource(Energy, new ValuePoolState(6, max: 6));
        combat.SetActiveCombatant(Hero);

        // A small draw pile so Gather has something to draw.
        for (var i = 0; i < 4; i++)
            combat.GetCardZones(Hero).AddCard(new CardInstance(
                combat.CreateNextCardInstanceId(), EmberShard, Hero, CardZone.DrawPile));

        return combat;
    }

    private static void SeedMarkersAndRules(CombatState combat, CombatDefinitionRegistry registry)
    {
        Apply(combat, registry, Boss, MoltenCore, stacks: 1);
        Apply(combat, registry, Wisp, VolatileMark, stacks: 1);
        Apply(combat, registry, Hero, HeroMark, stacks: 1);

        // Owner-bound regeneration: directly installed with the acolyte as owner (the node-based install
        // path carries no owner). It expires when the acolyte is downed.
        combat.AddTemporaryTriggeredProgram(
            RegenRuleDefinition(), TemporaryRuleLifetime.UntilOwnerRemoved, Acolyte);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // DIRECTOR PRIMITIVES
    // ══════════════════════════════════════════════════════════════════════════════

    private static void Strike(CombatState combat, CombatDefinitionRegistry registry,
        CombatantId source, CardDefinitionId card, CombatantId? target)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;
        var instance = Give(combat, source, card);
        combat.EnqueueEffectAndResolve(new PlayCardEffectRequest(source, instance.Id, target), registry);
    }

    private static void EnemyAct(CombatState combat, CombatDefinitionRegistry registry,
        CombatantId actor, EnemyActionDefinitionId action, CombatantId? target)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;
        combat.EnqueueEffectAndResolve(new ExecuteEnemyActionEffectRequest(actor, action, target), registry);
    }

    private static void TurnStart(CombatState combat, CombatDefinitionRegistry registry, CombatantId who)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;
        combat.SetActiveCombatant(who);
        combat.EnqueueEvent(new TurnStartedCombatEvent(who, combat.CurrentRound, combat.CurrentTurn));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void RoundStart(CombatState combat, CombatDefinitionRegistry registry, CombatantId who)
    {
        if (combat.Result != CombatResult.Ongoing)
            return;
        combat.SetActiveCombatant(who);
        combat.EnqueueEvent(new RoundStartedCombatEvent(combat.CurrentRound));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void Apply(CombatState combat, CombatDefinitionRegistry registry,
        CombatantId target, StatusDefinitionId status, int stacks)
        => combat.EnqueueEffectAndResolve(new ApplyStatusEffectRequest(target, status, Stacks: stacks), registry);

    private static CardInstance Give(CombatState combat, CombatantId owner, CardDefinitionId def)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), def, owner, CardZone.Hand);
        combat.GetCardZones(owner).AddCard(inst);
        return inst;
    }

    // ── definition helpers ─────────────────────────────────────────────────────────

    private static void RegisterStatus(CombatDefinitionRegistryBuilder b, StatusDefinitionId id,
        StatusPolarity polarity, bool stacks = false, bool charges = false)
    {
        var def = new StatusDefinition(id, Pkg, $"status.{id.value}.name", $"status.{id.value}.desc",
            polarity: polarity, usesStacks: stacks, usesCharges: charges,
            showStacksInUi: stacks, showChargesInUi: charges,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);
        if (polarity == StatusPolarity.Debuff)
            def.Tags.Add(StandardCombatIds.DebuffTag);
        else if (polarity == StatusPolarity.Buff)
            def.Tags.Add(StandardCombatIds.BuffTag);
        b.RegisterStatus(def);
    }

    private static void Card(CombatDefinitionRegistryBuilder b, CardDefinitionId id, int cost, bool attack,
        IEffectNode<CardPlayContext> root)
    {
        var card = new CardDefinitionBuilder(id, Pkg, $"card.{id.value}.name", $"card.{id.value}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(root),
        };
        if (cost > 0)
            card.Costs.Add(new ResourceCost(Energy, cost));
        card.Tags.Add(attack ? StandardCombatIds.AttackCardTag : StandardCombatIds.SkillCardTag);
        b.RegisterCard(card);
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // CUSTOM EXTENSION POINTS
    // ══════════════════════════════════════════════════════════════════════════════

    // Target-side: a unit with Overheat takes +25% damage per stack.
    private sealed class OverheatDamageAmountModifier : IDamageAmountModifier
    {
        public string ModifierId => "ashen.overheat_damage";
        public int Priority => 250;
        public DamageModifierStage Stage => DamageModifierStage.Target;
        public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount)
        {
            if (currentAmount <= 0 || context.Kind != DamageKind.Direct) return currentAmount;
            var stacks = context.TargetCombatant.Statuses.Where(s => s.DefinitionId == Overheat).Sum(s => s.Stacks);
            return stacks <= 0 ? currentAmount : currentAmount * (100 + 25 * stacks) / 100;
        }
    }

    // Global: a unit with no block takes +2 (brittle without armour).
    private sealed class BrittleArmorDamageAmountModifier : IDamageAmountModifier
    {
        public string ModifierId => "ashen.brittle_armor";
        public int Priority => 100;
        public DamageModifierStage Stage => DamageModifierStage.Global;
        public int ModifyDamageAmount(DamageAmountModificationContext context, int currentAmount)
        {
            if (currentAmount <= 0 || context.Kind != DamageKind.Direct) return currentAmount;
            var hasBlock = context.TargetCombatant.DefensivePools.TryGetValue(Block, out var pool) && pool.Current > 0;
            return hasBlock ? currentAmount : currentAmount + 2;
        }
    }

    // Emberward shields the wearer from the next N debuff applications, spending a charge each time.
    private sealed class EmberwardStatusApplicationInterceptor : IStatusApplicationInterceptor
    {
        public string ModifierId => "ashen.emberward";
        public int Priority => 50;
        public InterceptionResult TryIntercept(StatusApplicationInterceptionContext context)
        {
            if (context.StatusDefinition.Polarity != StatusPolarity.Debuff)
                return InterceptionResult.Allow;
            var ward = context.TargetCombatant.Statuses.FirstOrDefault(
                s => s.DefinitionId == Emberward && s.Charges > 0);
            if (ward is null)
                return InterceptionResult.Allow;

            context.Combat.EnqueueEvent(new StatusApplicationBlockedCombatEvent(
                context.TargetCombatant.Id, context.Request.StatusDefinitionId, ward.Id, ward.DefinitionId));
            context.Combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(
                context.TargetCombatant.Id, ward.Id));
            return InterceptionResult.Block;
        }
    }

    // Attacks cost 1 less while the source has Tempo.
    private sealed class TempoAttackCostModifier : ICardCostModifier
    {
        public string ModifierId => "ashen.tempo_cost";
        public int Priority => 100;
        public int ModifyCostAmount(CardCostModificationContext context, int currentAmount)
        {
            if (currentAmount <= 0) return currentAmount;
            if (!context.Card.Tags.Contains(StandardCombatIds.AttackCardTag)) return currentAmount;
            if (!context.Source.Statuses.Any(s => s.DefinitionId == Tempo)) return currentAmount;
            return Math.Max(0, currentAmount - 1);
        }
    }

    // Skill cards cannot be played while the source is Silenced.
    private sealed class SilenceSkillCardPlayValidator : ICardPlayValidator
    {
        public string ModifierId => "ashen.silence_validator";
        public int Priority => 100;
        public void Validate(CardPlayValidationContext context)
        {
            if (!context.Card.Tags.Contains(StandardCombatIds.SkillCardTag)) return;
            if (context.Source.Statuses.Any(s => s.DefinitionId == Silenced))
                throw new InvalidOperationException(
                    $"Combatant '{context.Source.Id}' is silenced and cannot play skill '{context.Card.Id}'.");
        }
    }

    // Gates the explosion to volatile units — read off the downed combatant directly, since the
    // EventTarget selector excludes a combatant that is already downed.
    private sealed class VolatileDownedFilter : ITriggeredProgramFilter<CombatantDownedTriggeredEffectContext>
    {
        public bool Matches(CombatantDownedTriggeredEffectContext context) =>
            context.DownedCombatant.Statuses.Any(s => s.DefinitionId == VolatileMark);
    }
}

internal static class BossEncounterTestExtensions
{
    public static void EnqueueEffectAndResolve(
        this CombatState combat, IEffectRequest request, CombatDefinitionRegistry registry)
    {
        combat.EnqueueEffect(request);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }
}
