using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// ── THE RECEIPT HAS TO NAME THINGS, AND THE NAMES HAVE TO BE THE CONTENT'S OWN (P2) ──────────────────────
// A tally that says "you lost 26 health" is what the runner has always printed. What the balance question
// needs is the other half of the sentence, and it has to survive the two ways a name can go wrong: a blow
// filed under the card that provoked it, and a blow that was BLOCKED counted as life.
public class DamageLedgerTests
{
    private static readonly ResourceId Energy = StandardCombatIds.EnergyResource;

    private static InteractiveCombat Combat()
    {
        var s = new ScenarioBlueprint();
        // A card that costs blood to play: the hero is both the actor and the target, and the CARD is what
        // the receipt must name — not the hero.
        s.Cards.Add(new CardBlueprint("press_the_claim")
        {
            Program = Effects.Program(
                Effects.DealDamage(Targets.Source, 4),
                Effects.DealDamage(Targets.EventTarget, 6)),
        }.Cost(Energy, 1));
        s.Cards.Add(new CardBlueprint("brace")
        {
            Program = Effects.Program(Effects.GainBlock(Targets.Source, 5)),
        }.Cost(Energy, 1));

        s.EnemyActions.Add(new EnemyActionBlueprint("summons", new ActionIntent("Summons", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(9))),
        });

        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 60 };
        for (var i = 0; i < 4; i++)
        {
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("press_the_claim")));
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("brace")));
        }
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));
        s.TurnStartResourceRefills.Add(new ResourceRefillSpec(Energy, 3));

        var enemy = new EnemyBlueprint("bailiff") { MaxHealth = 80 };
        enemy.Actions.Add(new EnemyActionDefinitionId("summons"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        return new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
    }

    private static (DamageLedger Ledger, InteractiveCombat Fight) Played(bool guard)
    {
        var fight = Combat();
        var ledger = new DamageLedger();

        var blood = fight.Hand.First(c => c.DefinitionId.value == "press_the_claim");
        fight.PlayCard(blood.Id, fight.State.Combatants.First(c => c.Id != fight.HeroId).Id);
        if (guard)
            fight.PlayCard(fight.Hand.First(c => c.DefinitionId.value == "brace").Id, null);
        fight.EndTurn();

        ledger.Read(fight, act: 1, room: "city_summons_01", role: "combat");
        return (ledger, fight);
    }

    private static int Health(DamageLedger ledger, string source) =>
        ledger.Rows.Where(r => r.Key.Source == source).Sum(r => r.Value.Health);

    // ⚠⚠ THE ENEMY AND THE ACTION, BOTH. "The bailiff" is a complaint; "the bailiff's summons" is a lead,
    // and a lead is the only thing a balance pass can act on.
    [Fact]
    public void An_enemys_blow_is_filed_under_the_enemy_and_the_action_it_swung()
    {
        var (ledger, _) = Played(guard: false);
        Assert.Equal(9, Health(ledger, "bailiff/summons"));
    }

    // ⚠ THE HERO'S OWN CARD IS NOT THE HERO. A cost paid in blood is a balance decision somebody authored
    // onto a card, and a receipt that files it under the body it came out of cannot say which card it was.
    [Fact]
    public void Blood_a_card_costs_is_filed_under_the_card()
    {
        var (ledger, _) = Played(guard: false);
        Assert.Equal(4, Health(ledger, "card/press_the_claim"));
    }

    // ⚠⚠ WHAT THE GUARD ATE IS NOT LIFE, AND COUNTING IT AS LIFE WOULD MAKE EVERY BLOCKING RUNNER LOOK
    // HURT. Five of the nine are absorbed, so four reach the body — and the blow is still one hit, still
    // the same source, with the absorbed five kept beside it rather than inside it.
    [Fact]
    public void A_blocked_blow_costs_what_got_through_and_the_guard_is_kept_beside_it()
    {
        var (ledger, _) = Played(guard: true);
        var row = ledger.Rows.Single(r => r.Key.Source == "bailiff/summons");
        Assert.Equal(4, row.Value.Health);
        Assert.Equal(5, row.Value.Blocked);
        Assert.Equal(1, row.Value.Hits);
    }

    // The ledger adds up to what the fight actually took off the body — the reconciliation the report
    // prints as `unnamed`, asked here where there is nothing else in the way.
    [Fact]
    public void What_it_names_adds_up_to_what_the_body_lost()
    {
        var (ledger, fight) = Played(guard: false);
        Assert.Equal(60 - fight.HeroHealth, ledger.Named);
    }

    // ⚠ READ TWICE, COUNTED ONCE. Both seats hand the fight over on every answer, so the ledger is asked
    // again and again while one fight runs; a cursor that did not hold its place would multiply every blow
    // by the number of answers the fight took.
    [Fact]
    public void Reading_the_same_fight_again_does_not_count_it_again()
    {
        var (ledger, fight) = Played(guard: false);
        var once = ledger.Named;
        ledger.Read(fight, act: 1, room: "city_summons_01", role: "combat");
        ledger.Read(fight, act: 1, room: "city_summons_01", role: "combat");
        Assert.Equal(once, ledger.Named);
    }

    // …and a SECOND fight is a second fight, even though it arrives at the same method with the same room.
    // The cursor belongs to the fight, not to the ledger.
    [Fact]
    public void A_second_fight_starts_its_own_count()
    {
        var (ledger, _) = Played(guard: false);
        var first = ledger.Named;

        var next = Combat();
        next.EndTurn();
        ledger.Read(next, act: 2, room: "archives_errata_02", role: "combat");

        Assert.Equal(first + 9, ledger.Named);
        Assert.Equal(9, ledger.HealthOfAct(2));
        Assert.Equal([1, 2], ledger.Acts);
    }

    // The gate P2 was set: an act's receipt, biggest first, in the content's own names.
    [Fact]
    public void An_act_names_its_biggest_sources_first()
    {
        var (ledger, _) = Played(guard: false);
        var top = ledger.TopOfAct(1, 6);
        Assert.Equal("bailiff/summons", top[0].Source);
        Assert.Equal("card/press_the_claim", top[1].Source);
    }

    // ⚠⚠ THE LINE THAT MADE THE ENGINE CHANGE. On the first real receipt, a quarter of act I's damage was
    // filed under "—/overtime": the biggest entry on the page, and the only one nobody could act on, because
    // a ticking status told the damage pipeline nothing about itself. It does now (DealDamageEffectRequest
    // .SourceStatusId, diagnostic only), and this is what that buys.
    [Fact]
    public void A_status_that_eats_the_body_says_which_status_it_is()
    {
        var s = new ScenarioBlueprint();
        var poison = new StatusBlueprint("ink_poisoning") { UsesStacks = true };
        poison.Tags.Add(StandardCombatIds.DamageOverTimeTag);
        s.Statuses.Add(poison);
        s.Cards.Add(new CardBlueprint("spill_the_ink")
        {
            Program = Effects.Program(Effects.ApplyStatus(
                Targets.Source, new StatusDefinitionId("ink_poisoning"), 3)),
        }.Cost(Energy, 1));

        s.Hero = new HeroBlueprint("clerk") { MaxHealth = 60 };
        for (var i = 0; i < 5; i++)
            s.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("spill_the_ink")));
        s.Hero.Resources.Add(new ResourceSpec(Energy, 3, 3));
        s.TurnStartResourceRefills.Add(new ResourceRefillSpec(Energy, 3));

        // ⚠ Nothing swings in this fight. The only thing that can take health is the poison, so the source
        // cannot be attributed by accident.
        s.EnemyActions.Add(new EnemyActionBlueprint("wait", new ActionIntent("Wait", IntentKind.Unknown)));
        var enemy = new EnemyBlueprint("clerk_of_records") { MaxHealth = 90 };
        enemy.Actions.Add(new EnemyActionDefinitionId("wait"));
        s.Enemies.Add(enemy);

        var compiled = s.Compile();
        var fight = new InteractiveCombat(compiled, EnemyIntentSelectors.Build(compiled));
        fight.PlayCard(fight.Hand.First(c => c.DefinitionId.value == "spill_the_ink").Id, null);
        fight.EndTurn();

        var ledger = new DamageLedger();
        ledger.Read(fight, act: 1, room: "archives_errata_02", role: "combat");

        Assert.Equal(3, Health(ledger, "status/ink_poisoning"));
        Assert.Equal(60 - fight.HeroHealth, ledger.Named);
    }

    // ── THE CLOSING EXCHANGE OF EVERY FIGHT (found by T0's gate) ─────────────────────────────────────────
    // The ledger is read on every ANSWER, and once the last blow has landed there are no more answers in that
    // fight — so what a fight wrote after its final decision was read only for the LAST fight of a run, which
    // is the one `Finish` looks at again. Every other fight quietly lost its closing exchange into `unnamed`.
    //
    // It surfaced holding a run bounded at act II against the same run played out: the bounded one named 23
    // MORE points in act II, because it ended where the other had already moved on. Over the four immortal
    // golden seeds, reading a fight once more as it ends took `unnamed` from 1685 of 34758 to 533.
    [Fact]
    public void What_a_fight_writes_after_its_last_decision_is_still_named()
    {
        var run = SampleProject.Build().CreateInitialRun(new RunId("receipt"), randomSeed: 3);
        var mind = new BotMind(
            new RunPlayback(() => { }, new InMemoryMetaStore()),
            new BotOptions { Seed = 1 },
            NullBotLog.Instance);
        var fight = Combat();

        // The seat's order, exactly: the fight is handed over, then announced, then answered.
        mind.Observe(run, fight);
        mind.FightStarts(run, fight);
        mind.EndingTurn(fight);
        fight.EndTurn();

        // …and nobody asks again, because the bailiff's swing is the last thing that happens. Before this the
        // ledger stopped at the hero's last decision and those nine points had no name.
        Assert.Equal(0, mind.Ledger.Named);
        mind.Observe(run, null);
        Assert.Equal(9, Health(mind.Ledger, "bailiff/summons"));
    }

    // Health lost with no fight running — a door that bites, a curse collected at a shrine — is the room's,
    // and it lands in the same table so that an act's receipt is the whole act.
    [Fact]
    public void Health_lost_outside_a_fight_is_filed_under_the_room()
    {
        var ledger = new DamageLedger();
        ledger.Outside(7, act: 1, room: "city_event_toll", role: "event");
        Assert.Equal(7, Health(ledger, "room/city_event_toll"));
        Assert.Equal(7, ledger.HealthOfAct(1));
    }
}
