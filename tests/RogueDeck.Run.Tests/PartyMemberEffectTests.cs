using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Party deckbuilding B3a (per-member run economy): a member-scoped effect wraps ordinary run effects and runs
// them against each member a selector picks. RunState.PushActiveMember retargets the single-hero accessors, so
// the existing heal/damage/resource/add-card/add-relic vocabulary becomes party-aware with no parallel handlers.
public class PartyMemberEffectTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewParty(out PartyMember mage, out PartyMember rogue)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        mage = run.AddPartyMember(new HealthState(18, 25));
        rogue = run.AddPartyMember(new HealthState(22, 22));
        return run;
    }

    private static void Resolve(RunState run, RunDefinitionRegistry registry, IRunEffectRequest effect)
    {
        run.EnqueueEffect(effect);
        new RunEffectProcessor().ResolvePending(run, registry);
    }

    [Fact]
    public void Gold_gain_targets_the_selected_member_only()
    {
        var registry = BuildRegistry();
        var run = NewParty(out var mage, out _);

        Resolve(run, registry, new ForMemberRunEffect(
            RunSelectors.Member(mage.Id),
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 25) }));

        Assert.Equal(0, run.Primary.GetResource(Gold)); // hero untouched
        Assert.Equal(25, mage.GetResource(Gold));       // only the mage was paid
    }

    [Fact]
    public void All_members_can_be_healed_at_once()
    {
        var registry = BuildRegistry();
        var run = NewParty(out var mage, out var rogue);
        run.Primary.Health.SetCurrent(10);
        mage.Health.SetCurrent(5);
        rogue.Health.SetCurrent(20); // already near full (max 22)

        Resolve(run, registry, new ForMemberRunEffect(
            RunSelectors.Party, new IRunEffectRequest[] { new HealRunEffect(8) }));

        Assert.Equal(18, run.Primary.Health.Current);   // 10 + 8
        Assert.Equal(13, mage.Health.Current);          // 5 + 8
        Assert.Equal(22, rogue.Health.Current);         // 20 + 8 capped at max 22
    }

    [Fact]
    public void Lowest_health_reducer_targets_the_most_wounded_living_member()
    {
        var registry = BuildRegistry();
        var run = NewParty(out var mage, out var rogue);
        run.Primary.Health.SetCurrent(30);
        mage.Health.SetCurrent(4);   // most wounded
        rogue.Health.SetCurrent(15);

        Resolve(run, registry, new ForMemberRunEffect(
            RunSelectors.LowestHealthMember, new IRunEffectRequest[] { new HealRunEffect(6) }));

        Assert.Equal(30, run.Primary.Health.Current);
        Assert.Equal(10, mage.Health.Current);          // only the mage healed
        Assert.Equal(15, rogue.Health.Current);
    }

    [Fact]
    public void A_card_added_in_a_member_scope_lands_in_that_members_deck()
    {
        var registry = BuildRegistry();
        var run = NewParty(out _, out var rogue);

        Resolve(run, registry, new ForMemberRunEffect(
            RunSelectors.Member(rogue.Id),
            new IRunEffectRequest[] { new AddCardToDeckRunEffect(new CardDefinitionId("backstab")) }));

        Assert.Empty(run.Primary.Deck);
        Assert.Equal(new[] { "backstab" }, rogue.Deck.Select(c => c.DefinitionId.value));
    }

    [Fact]
    public void Living_selector_skips_a_downed_member()
    {
        var registry = BuildRegistry();
        var run = NewParty(out var mage, out var rogue);
        mage.Health.SetCurrent(0); // downed — out for the fight

        Resolve(run, registry, new ForMemberRunEffect(
            RunSelectors.LivingParty, new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 5) }));

        Assert.Equal(5, run.Primary.GetResource(Gold));
        Assert.Equal(0, mage.GetResource(Gold));  // downed member skipped
        Assert.Equal(5, rogue.GetResource(Gold));
    }

    [Fact]
    public void The_active_member_scope_restores_the_primary_afterwards()
    {
        var registry = BuildRegistry();
        var run = NewParty(out var mage, out _);

        Resolve(run, registry, new ForMemberRunEffect(
            RunSelectors.Member(mage.Id), new IRunEffectRequest[] { new HealRunEffect(1) }));

        // Outside any scope the single-hero accessors resolve to the primary again.
        Assert.Same(run.Primary, run.ActiveMember);
        run.SetResource(Gold, 9);
        Assert.Equal(9, run.Primary.GetResource(Gold));
    }
}
