using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run;

// A run→combat "opening": a combat rule installed as a OneShot temporary rule on the hero at the start of the NEXT
// combat. Authored as a turnStarted RelicCombatRule, so it fires once at the hero's first turn start (e.g. "start
// with 20 block" — after block's turn-start clear), then removes itself. The pending-combat-modifier queue makes it
// apply to exactly one fight, which is exactly the time-limited effect a consumable wants. Reuses the relic-combat-
// rule authoring + serialization + visual editor (the rule is a turnStarted EffectProgram).
public sealed class HeroOpeningRuleModifier : IRunCombatModifier
{
    private readonly RelicCombatRule _rule;

    // The rule this opening is, which is the whole of it — an opening waiting for the next fight is therefore
    // the one pending combat modifier a SAVE can capture by value (RunState.Snapshot).
    public RelicCombatRule Rule => _rule;

    // How many fights this opening still has to open, this one included (playtest feedback 2, G1: "die naechsten
    // x kaempfe"). When a fight takes it, one with a count less goes back into the queue for the next.
    public int Combats { get; }

    // WHOSE FIGHT IT OPENS, in a party (multiplayer): the member whose relic, event or consumable promised it.
    // Null is the hero — member 0, and everyone in a solo run — which is every opening there ever was before.
    public RunMemberId? Member { get; }

    public HeroOpeningRuleModifier(RelicCombatRule rule, int combats = 1, RunMemberId? member = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rule = rule;
        Combats = Math.Max(1, combats);
        Member = member;
    }

    public void Apply(ScenarioBlueprint blueprint, RunState run)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        CombatantBlueprint? hero = Member is { } member
            ? blueprint.Allies.FirstOrDefault(a => a.Id == member.Value)
            : blueprint.Hero;
        if (hero is null)
            return;

        // A member's opening listens for that member alone: in a party fight every hero's turn starts at once, and
        // the first of them to start is the hero, not whoever the opening was promised to.
        var trigger = RelicCombatTriggers.Get(_rule.Trigger);
        var id = new TriggeredEffectDefinitionId(
            $"opening:{(Member is { } owner ? owner.Value + ":" : "")}{_rule.Trigger}:{hero.OpeningTemporaryRules.Count}");
        var definition = Member is null
            ? trigger.Build(id, _rule.Program, _rule.Priority)
            : trigger.BuildFor(id, _rule.Program, _rule.Priority, hero.CombatantId);
        hero.OpeningTemporaryRules.Add(new TemporaryRuleInstallSpec(definition, TemporaryRuleLifetime.OneShot));
    }
}

// Install a "next combat opening" (see HeroOpeningRuleModifier): queues a pending combat modifier so the NEXT
// fight's hero starts with the rule installed. Serializable — its RelicCombatRule round-trips via RunJson — so a
// consumable / event / reward carries it as data. The pending queue consumes it after one combat.
// `Combats`: how many fights in a row it opens — null (the default, out of the wire format) is the next one only.
public sealed record InstallNextCombatOpeningRunEffect(
    RelicCombatRule Rule,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? Combats = null) : IRunEffectRequest;

public sealed class InstallNextCombatOpeningRunEffectHandler : RunEffectHandler<InstallNextCombatOpeningRunEffect>
{
    // The opening belongs to whoever the run is pointed at when it is promised: a member's own room, a member's
    // relic answering a party event. Member 0 is written as no one, so a solo run's openings are what they were.
    protected override void Resolve(RunState run, RunDefinitionRegistry registry, InstallNextCombatOpeningRunEffect request) =>
        run.AddPendingCombatModifier(new HeroOpeningRuleModifier(request.Rule, request.Combats ?? 1,
            ReferenceEquals(run.ActiveMember, run.Primary) ? null : run.ActiveMember.Id));
}
