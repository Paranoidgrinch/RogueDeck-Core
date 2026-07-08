using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Party deckbuilding B2 (run↔combat, end-to-end): a party run drives a real fight through PartyAutoPlayCombatDriver
// — the hero AND the projected party members each play from their own decks in the simultaneous phase — and each
// member's HP is reconciled back onto RunState.Party afterwards. A downed member is out but the run continues while
// any member lives; the run is defeated only when the whole party is down.
public class PartyRunTests
{
    private static readonly CardDefinitionId Strike = new("strike");

    // A goblin fight built from the run: the hero's deck comes from the run; the bridge also projects the party
    // members as allies with their own decks (B2a) and turns on the simultaneous phase.
    private static Playthrough BuildGoblinFight(RunState run)
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("strike")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });
        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
        blueprint.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 60 }; // survives round 1, so the enemy phase runs
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static NodeResolveContext Context(RunState run) =>
        new(run, new ScriptedChoiceProvider(), Registry(), new RunEffectProcessor());

    // A two-member party: the hero (member 0) + a mage, each with a 4-card strike deck of their own.
    private static (RunState run, PartyMember mage) TwoMemberRun(int mageHp = 25)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        for (var i = 0; i < 4; i++)
            run.AddDeckCard(Strike);
        var mage = run.AddPartyMember(new HealthState(mageHp, mageHp), "party.mage", new CombatantDefinitionId("mage"));
        for (var i = 0; i < 4; i++)
            run.AddDeckCardTo(mage, Strike);
        return (run, mage);
    }

    [Fact]
    public void A_two_member_party_drives_a_real_fight_and_wins()
    {
        var (run, mage) = TwoMemberRun();
        var resolver = new CombatNodeResolver(new PartyAutoPlayCombatDriver());
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildGoblinFight));

        resolver.Resolve(Context(run), node);

        var resolved = Assert.IsType<CombatResolvedRunEvent>(
            run.EventHistory.First(e => e is CombatResolvedRunEvent));
        Assert.Equal(CombatResult.Victory, resolved.Result); // hero + mage focused the goblin down together
        Assert.False(run.IsPartyDefeated());
    }

    [Fact]
    public void Each_members_hp_is_reconciled_onto_the_party_after_the_fight()
    {
        var (run, mage) = TwoMemberRun();
        var resolver = new CombatNodeResolver(new PartyAutoPlayCombatDriver());
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildGoblinFight));

        resolver.Resolve(Context(run), node);
        new RunEffectProcessor().ResolvePending(run, Registry()); // flush the hero's ApplyRunDamage

        // The goblin slams the first living player (the hero), so the hero took damage and the mage was untouched —
        // and both HP pools came back onto their own PartyMember.
        Assert.True(run.Primary.Health.Current < 30, "the hero should have taken slam damage");
        Assert.Equal(25, mage.Health.Current); // the mage's own pool, reconciled, untouched
        Assert.True(mage.Health.Current > 0);
    }

    [Fact]
    public void The_run_is_not_defeated_while_any_member_lives()
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        var mage = run.AddPartyMember(new HealthState(20, 20));

        run.Primary.Health.SetCurrent(0); // the hero is down
        Assert.False(run.IsPartyDefeated()); // but the mage lives → not defeated

        mage.Health.SetCurrent(0);
        Assert.True(run.IsPartyDefeated()); // whole party down → defeated
    }
}
