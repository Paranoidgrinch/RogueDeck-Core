using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Party deckbuilding — Part D (worked examples): end-to-end validation of the whole party stack driving REAL
// content. Proves the genre shapes the arc set out to deliver: a full four-member party fights and wins together;
// a member downed mid-fight is out but the run continues; the multiplayer seam is deterministic (identical
// interleaved inputs reproduce the fight exactly); and per-member run economy (B3) targets one member's wallet /
// HP without touching the others. Everything reduces to today's single-hero behaviour when the party has one
// member.
public class PartyWorkedExamplesTests
{
    private static readonly CardDefinitionId Strike = new("strike");

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static NodeResolveContext Context(RunState run) =>
        new(run, new ScriptedChoiceProvider(), Registry(), new RunEffectProcessor());

    // A goblin fight built from the run: the hero + every party member is projected in with its own deck (B2a),
    // and the simultaneous phase is turned on. `cardDamage` and `goblinHp` tune the fight; the goblin slams a
    // player for `slam` each enemy phase.
    private static Func<RunState, Playthrough> GoblinFight(int cardDamage, int goblinHp, int slam)
    {
        return run =>
        {
            var blueprint = new ScenarioBlueprint();
            blueprint.Cards.Add(new CardBlueprint("strike")
            {
                Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, cardDamage)),
            });
            blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
            {
                Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(slam))),
            });
            blueprint.Hero = new HeroBlueprint("hero")
            {
                MaxHealth = run.Health.Max,
                CurrentHealth = run.Health.Current,
            };
            blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
            var goblin = new EnemyBlueprint("goblin") { MaxHealth = goblinHp };
            goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
            blueprint.Enemies.Add(goblin);
            return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
        };
    }

    // A party run: the hero (member 0) plus one extra member per `memberHps`, each with a `deckSize`-card strike
    // deck of its own. Returns the run and the extra members in party order.
    private static (RunState run, IReadOnlyList<PartyMember> extras) PartyRun(
        int heroHp, int deckSize, params int[] memberHps)
    {
        var run = new RunState(new RunId("run"), new HealthState(heroHp, heroHp), new RunMap(Array.Empty<Node>()));
        for (var i = 0; i < deckSize; i++)
            run.AddDeckCard(Strike);

        var extras = new List<PartyMember>();
        foreach (var hp in memberHps)
        {
            var member = run.AddPartyMember(new HealthState(hp, hp), "party.member", new CombatantDefinitionId("hero"));
            for (var i = 0; i < deckSize; i++)
                run.AddDeckCardTo(member, Strike);
            extras.Add(member);
        }
        return (run, extras);
    }

    private static CombatResult DriveFight(
        RunState run, Func<RunState, Playthrough> fight, PartyEnemyTargeting targeting)
    {
        var resolver = new CombatNodeResolver(new PartyAutoPlayCombatDriver(targeting: targeting));
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(fight));
        resolver.Resolve(Context(run), node);
        new RunEffectProcessor().ResolvePending(run, Registry()); // flush the hero's ApplyRunDamage reconcile
        return Assert.IsType<CombatResolvedRunEvent>(
            run.EventHistory.First(e => e is CombatResolvedRunEvent)).Result;
    }

    // ── Example 1: a full four-member party fights and wins together ────────────────

    [Fact]
    public void A_four_member_party_focuses_the_enemy_down_together()
    {
        // Hero + 3 members, each with a 3-card 6-damage strike deck: 4 × 18 = 72 damage a round against a 100-HP
        // goblin, so they win over two rounds (an enemy phase happens in between).
        var (run, extras) = PartyRun(heroHp: 30, deckSize: 3, 28, 26, 24);

        var result = DriveFight(run, GoblinFight(cardDamage: 6, goblinHp: 100, slam: 4),
            PartyEnemyTargeting.FirstAlive);

        Assert.Equal(CombatResult.Victory, result);
        Assert.False(run.IsPartyDefeated());
        Assert.Equal(4, run.Party.Count);
        // Every member's HP came back onto its own pool; only the hero (first alive) took the slam.
        Assert.True(run.Primary.Health.Current < 30, "the hero soaked the goblin's slam");
        Assert.All(extras, m => Assert.Equal(m.Health.Max, m.Health.Current)); // untouched allies
    }

    // ── Example 2: a downed member is out, but the run continues ────────────────────

    [Fact]
    public void A_downed_member_is_out_for_the_fight_but_the_run_continues()
    {
        // A fragile mage (3 HP) alongside the hero (30 HP), 1-card 6-damage decks vs a 30-HP goblin that focuses
        // the weakest living player for 4. The goblin can't be killed in round 1, so its enemy phase downs the
        // mage; the hero fights on alone and finishes the goblin. The run is NOT defeated — a member lives.
        var (run, extras) = PartyRun(heroHp: 30, deckSize: 1, 3);
        var mage = extras[0];

        var result = DriveFight(run, GoblinFight(cardDamage: 6, goblinHp: 30, slam: 4),
            PartyEnemyTargeting.LowestHealth);

        Assert.Equal(CombatResult.Victory, result);
        Assert.Equal(0, mage.Health.Current);          // downed — reconciled at 0, kept in the party
        Assert.True(run.Primary.Health.Current > 0);   // the hero survived
        Assert.False(run.IsPartyDefeated());           // run continues while any member lives
    }

    // ── Example 3: the multiplayer seam is deterministic (identical inputs reproduce the fight) ─────

    [Fact]
    public void Identical_interleaved_inputs_reproduce_the_fight_exactly()
    {
        // Two independent party fights driven with the SAME interleaved call order produce byte-identical state.
        // This is the multiplayer-seam guarantee: the engine is single-threaded and deterministic by request
        // order, so concurrent players' inputs are just interleaved calls here (netcode out of scope).
        static PartyCombat Build()
        {
            var blueprint = new ScenarioBlueprint { SimultaneousTeamTurns = true };
            blueprint.Cards.Add(new CardBlueprint("strike")
            {
                Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(5))),
            });
            blueprint.Hero = new HeroBlueprint("hero") { MaxHealth = 30 };
            blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
            for (var i = 0; i < 5; i++)
                blueprint.Hero.Deck.Add(new DeckEntry(Strike));
            var knight = new AllyBlueprint("knight") { MaxHealth = 25 };
            knight.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
            for (var i = 0; i < 5; i++)
                knight.Deck.Add(new DeckEntry(Strike));
            blueprint.Allies.Add(knight);
            blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
            {
                Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
            });
            var goblin = new EnemyBlueprint("goblin") { MaxHealth = 40 };
            goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
            blueprint.Enemies.Add(goblin);
            return new PartyCombat(blueprint.Compile(), (_, _, _) => new EnemyActionDefinitionId("slam"),
                targeting: PartyEnemyTargeting.Random);
        }

        var hero = new CombatantId("hero");
        var knight = new CombatantId("knight");
        var goblin = new CombatantId("goblin");

        // The same non-trivial interleaving applied to two fresh fights: knight acts before hero, they end out of
        // order, across two rounds.
        static void Drive(PartyCombat p, CombatantId hero, CombatantId knight, CombatantId goblin)
        {
            p.PlayCard(knight, p.HandOf(knight)[0].Id, goblin);
            p.PlayCard(hero, p.HandOf(hero)[0].Id, goblin);
            p.PlayCard(knight, p.HandOf(knight)[1].Id, goblin);
            p.EndTurn(hero);
            p.EndTurn(knight); // enemy phase → round 2
            p.PlayCard(hero, p.HandOf(hero)[0].Id, goblin);
            p.EndTurn(knight);
            p.EndTurn(hero);
        }

        var a = Build();
        var b = Build();
        Drive(a, hero, knight, goblin);
        Drive(b, hero, knight, goblin);

        Assert.Equal(a.State.GetCombatant(goblin).Health.Current, b.State.GetCombatant(goblin).Health.Current);
        Assert.Equal(a.State.GetCombatant(hero).Health.Current, b.State.GetCombatant(hero).Health.Current);
        Assert.Equal(a.State.GetCombatant(knight).Health.Current, b.State.GetCombatant(knight).Health.Current);
        Assert.Equal(a.Round, b.Round);
        Assert.Equal(a.Result, b.Result);
    }

    // ── Example 4: per-member run economy targets one member (B3) ───────────────────

    [Fact]
    public void Per_member_economy_pays_and_heals_one_member_without_touching_the_others()
    {
        var gold = StandardRunIds.Gold;
        var (run, extras) = PartyRun(heroHp: 30, deckSize: 1, 25, 20);
        var mage = extras[0];
        var rogue = extras[1];
        mage.Health.SetCurrent(6);   // the mage is wounded
        rogue.Health.SetCurrent(20);

        var registry = Registry();

        // A "reward" that pays the mage 40 gold and heals whoever is most wounded — both member-targeted, the way
        // an event or reward wraps its effects in ForMemberRunEffect to target a specific member (B3).
        run.EnqueueEffect(new GrantRewardRunEffect(new RewardId("mage-boon"), new IRunEffectRequest[]
        {
            new ForMemberRunEffect(RunSelectors.Member(mage.Id),
                new IRunEffectRequest[] { new ChangeResourceRunEffect(gold, 40) }),
            new ForMemberRunEffect(RunSelectors.LowestHealthMember,
                new IRunEffectRequest[] { new HealRunEffect(10) }),
        }));
        new RunEffectProcessor().ResolvePending(run, registry);

        Assert.Equal(40, mage.GetResource(gold));   // only the mage was paid
        Assert.Equal(0, run.Primary.GetResource(gold));
        Assert.Equal(0, rogue.GetResource(gold));

        Assert.Equal(16, mage.Health.Current);       // the wounded mage (6) was healed by 10
        Assert.Equal(30, run.Primary.Health.Current); // hero untouched
        Assert.Equal(20, rogue.Health.Current);       // rogue untouched
    }
}
