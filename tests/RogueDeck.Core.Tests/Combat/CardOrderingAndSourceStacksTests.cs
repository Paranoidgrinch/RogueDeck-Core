using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// B&B enemy-mechanics arc, Phase 3. Two substrates:
//   (1) card-play ORDERING within a turn (first-card "opening type" + per-tag counts) plus a retained
//       previous-turn snapshot — for card-type sequencing (Wrong-Window / Triplicate) and habit predictions;
//   (2) SOURCE-SCOPED status stacks — count only the stacks a specific source placed, for "N from the same
//       source" thresholds (Overdue / Trespass).
public class CardOrderingAndSourceStacksTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly TagId Attack = new("attack");
    private static readonly TagId Skill = new("skill");

    private static CardDefinition Card(string id, params TagId[] tags)
    {
        var b = new CardDefinitionBuilder(new CardDefinitionId(id), new PackageId("test"), "n", "d");
        foreach (var t in tags)
            b.Tags.Add(t);
        return b.Build();
    }

    // ── (1) Card-play ordering + previous-turn retention ────────────────────────

    [Fact]
    public void First_card_opening_type_and_per_tag_counts_are_tracked_this_turn()
    {
        var stats = new CombatantCardPlayTurnStats();
        stats.RecordCardPlayed(Card("a1", Attack));
        stats.RecordCardPlayed(Card("s1", Skill));
        stats.RecordCardPlayed(Card("a2", Attack));

        Assert.True(stats.FirstCardPlayedThisTurnHasTag(Attack));   // opened with an Attack
        Assert.False(stats.FirstCardPlayedThisTurnHasTag(Skill));
        Assert.Equal(2, stats.GetCardsPlayedWithTagThisTurn(Attack));
        Assert.Equal(1, stats.GetCardsPlayedWithTagThisTurn(Skill));
        Assert.Equal(3, stats.CardsPlayedThisTurn);
    }

    [Fact]
    public void Reset_retains_the_previous_turns_profile_for_habit_predictions()
    {
        var stats = new CombatantCardPlayTurnStats();
        stats.RecordCardPlayed(Card("a1", Attack));
        stats.RecordCardPlayed(Card("a2", Attack));
        stats.RecordCardPlayed(Card("s1", Skill));  // "Busy" turn (3 cards), opened Attack

        stats.Reset();

        // This turn is now empty…
        Assert.Equal(0, stats.CardsPlayedThisTurn);
        Assert.False(stats.FirstCardPlayedThisTurnHasTag(Attack));
        // …but last turn's habit profile is retained.
        Assert.Equal(3, stats.CardsPlayedLastTurn);
        Assert.Equal(2, stats.GetCardsPlayedWithTagLastTurn(Attack));
        Assert.True(stats.FirstCardPlayedLastTurnHasTag(Attack));
    }

    // ── (2) Source-scoped status stacks ─────────────────────────────────────────

    private sealed record Ctx;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    [Fact]
    public void Source_scoped_stacks_count_only_one_sources_contribution()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var overdue = new StatusDefinitionId("test.overdue");

        var hero = combat.GetCombatant(HeroId);
        // 2 Overdue on the Hero from the Goblin, and 5 from the Hero itself (a different source).
        hero.AddStatus(new StatusInstance(
            new StatusInstanceId("s1"), overdue, HeroId, sourceCombatantId: GoblinId, stacks: 2));
        hero.AddStatus(new StatusInstance(
            new StatusInstanceId("s2"), overdue, HeroId, sourceCombatantId: HeroId, stacks: 5));

        // Total across all sources = 7.
        var total = new CombatantStatusStacksExpression<Ctx>(CombatantTargetSelectors.Source, overdue);
        Assert.Equal(7, total.Evaluate(MakeContext(combat), combat));

        // Only the Goblin's contribution = 2 (target = Hero/Source, source = Goblin/EventTarget).
        var fromGoblin = new CombatantStatusStacksFromSourceExpression<Ctx>(
            CombatantTargetSelectors.Source, overdue, CombatantTargetSelectors.EventTarget);
        Assert.Equal(2, fromGoblin.Evaluate(MakeContext(combat), combat));
    }
}
