using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Tests;

/// <summary>
/// A game's authored content is the same in every one of its fights. A <see cref="CompiledCombatLibrary"/>
/// compiles that shared half once; a scenario then names the library instead of carrying a copy of it.
///
/// The claim these tests have to hold up is not that it is faster — it is that it is the SAME FIGHT. So the
/// central test plays one fight both ways and compares what happened, blow by blow.
/// </summary>
public class CompiledCombatLibraryTests
{
    private static IReadOnlyList<CardBlueprint> Cards() =>
    [
        new CardBlueprint("smite") { Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)) },
        new CardBlueprint("guard") { Program = Effects.Program(Effects.GainBlock(Targets.Source, 5)) },
    ];

    private static IReadOnlyList<StatusBlueprint> Statuses()
    {
        var bulwark = new StatusBlueprint("bulwark") { Polarity = StatusPolarity.Buff, UsesStacks = true };
        bulwark.PassiveModifiers.Add(new PassiveModifierSpec(
            PassiveModifierPipeline.BlockGain, PassiveModifierOperation.AddPerStack, 1));
        return [bulwark];
    }

    private static IReadOnlyList<EnemyActionBlueprint> EnemyActions() =>
    [
        new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(14))),
        },
    ];

    // The fight itself: who is in it and what the hero brought. Identical in both arrangements.
    private static ScenarioBlueprint Fight()
    {
        var scenario = new ScenarioBlueprint();
        scenario.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 30,
            Deck =
            {
                new DeckEntry(new CardDefinitionId("smite"), 3),
                new DeckEntry(new CardDefinitionId("guard"), 2),
            },
        };
        scenario.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 40 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        scenario.Enemies.Add(goblin);
        return scenario;
    }

    // Arrangement A: the scenario carries the whole library itself, as scenarios always have.
    private static ScenarioBlueprint CarryingItsOwnContent()
    {
        var scenario = Fight();
        foreach (var status in Statuses()) scenario.Statuses.Add(status);
        foreach (var card in Cards()) scenario.Cards.Add(card);
        foreach (var action in EnemyActions()) scenario.EnemyActions.Add(action);
        return scenario;
    }

    // Arrangement B: the same content, compiled once and named.
    private static CompiledCombatLibrary Library() => CompiledCombatLibrary.Compile(
        statuses: Statuses(), cards: Cards(), enemyActions: EnemyActions());

    private static ScenarioBlueprint BuiltOnALibrary(CompiledCombatLibrary library)
    {
        var scenario = Fight();
        scenario.Library = library;
        return scenario;
    }

    [Fact]
    public void A_fight_named_on_a_library_plays_exactly_like_one_that_carries_its_content()
    {
        var carried = Play(CarryingItsOwnContent().Compile());
        var named = Play(BuiltOnALibrary(Library()).Compile());

        Assert.Equal(carried, named);
        // And the comparison has something to compare: a fight where nothing moved would match itself.
        Assert.Contains("round 3", carried);
        Assert.Contains("result Victory", carried);
        Assert.DoesNotContain("30 vs 40", carried); // both sides actually took damage
    }

    [Fact]
    public void A_library_answers_for_the_content_the_scenario_no_longer_holds()
    {
        var compiled = BuiltOnALibrary(Library()).Compile();

        Assert.True(compiled.Registry.TryGetCard(new CardDefinitionId("smite"), out _));
        Assert.True(compiled.Registry.StatusDefinitions.ContainsKey(new StatusDefinitionId("bulwark")));
        // The intent metadata travels with the library too, or the fight could not name its enemy's move.
        var intent = compiled.IntentFor(new EnemyActionDefinitionId("slam"));
        Assert.NotNull(intent);
        Assert.Equal("Slam", intent!.Label);
    }

    [Fact]
    public void An_enemy_naming_an_action_no_one_defined_is_still_refused()
    {
        // The reference check used to consult the blueprint's own action list. With a library that list is
        // empty and every enemy would have looked unknown — so it asks the compiled content instead, and a
        // genuinely unknown action must still be caught.
        var scenario = BuiltOnALibrary(Library());
        scenario.Enemies[0].Actions.Add(new EnemyActionDefinitionId("ghost-action"));

        var ex = Assert.Throws<InvalidOperationException>(() => scenario.Compile());
        Assert.Contains("ghost-action", ex.Message);
    }

    [Fact]
    public void A_card_the_library_does_not_define_is_still_refused_in_the_deck()
    {
        var scenario = BuiltOnALibrary(Library());
        scenario.Hero!.Deck.Add(new DeckEntry(new CardDefinitionId("does-not-exist")));

        Assert.Throws<InvalidOperationException>(() => scenario.Compile());
    }

    [Fact]
    public void A_fight_may_bring_content_of_its_own_on_top_of_a_library()
    {
        // The card is this fight's alone, but what it does is said in the library's words: a damage request
        // whose handler the library registered, and a status the library defines. Validating it against this
        // fight's own registrations only would reject both — the point of standing on a library is that its
        // content is in scope.
        var scenario = BuiltOnALibrary(Library());
        scenario.Cards.Add(new CardBlueprint("this-fight-only")
        {
            Program = Effects.Program(
                Effects.DealDamage(Targets.EventTarget, 99),
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new StatusDefinitionId("bulwark"),
                    stacks: new ConstantExpression<CardPlayContext>(2))),
        });
        scenario.Hero!.Deck.Add(new DeckEntry(new CardDefinitionId("this-fight-only")));

        var compiled = scenario.Compile();

        Assert.True(compiled.Registry.TryGetCard(new CardDefinitionId("this-fight-only"), out _));
        Assert.True(compiled.Registry.TryGetCard(new CardDefinitionId("smite"), out _)); // and the library's
    }

    [Fact]
    public void A_library_compiled_for_a_different_draw_count_is_refused_out_loud()
    {
        // The per-turn draw handler is built into the library's standard package. A scenario that deals a
        // different number of cards cannot use it — and must be told so rather than quietly dealt five.
        var scenario = BuiltOnALibrary(CompiledCombatLibrary.Compile(cardsDrawnPerTurn: 5, cards: Cards(),
            statuses: Statuses(), enemyActions: EnemyActions()));
        scenario.CardsDrawnPerTurn = 7;

        var ex = Assert.Throws<InvalidOperationException>(() => scenario.Compile());
        Assert.Contains("7", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void One_library_serves_many_fights()
    {
        var library = Library();

        var first = BuiltOnALibrary(library).Compile();
        var second = BuiltOnALibrary(library).Compile();

        Assert.Same(
            first.Registry.GetCard(new CardDefinitionId("smite")),
            second.Registry.GetCard(new CardDefinitionId("smite")));
        Assert.Equal(Play(first), Play(second));
    }

    // Plays the fight to its end with a fixed policy and reports what happened, turn by turn, so that two
    // arrangements of the same content can be compared as whole playthroughs rather than at one checkpoint.
    private static string Play(CompiledScenario compiled)
    {
        var combat = new InteractiveCombat(
            compiled, EnemyIntentSelectors.Build(compiled), "library-comparison", randomSeed: 4242);
        var log = new List<string>();
        var rounds = 0;
        while (!combat.IsOver && combat.IsHeroTurn && rounds++ < 50)
        {
            foreach (var card in combat.Hand.ToArray())
            {
                combat.PlayCard(card.Id, new CombatantId("goblin"));
                if (combat.IsOver)
                    break;
            }
            log.Add($"round {rounds}: {Hp(combat, "knight")} vs {Hp(combat, "goblin")}");
            if (combat.IsOver)
                break;
            combat.EndTurn();
        }
        log.Add($"result {combat.Result}");
        return string.Join("\n", log);
    }

    private static int Hp(InteractiveCombat combat, string id) =>
        combat.State.TryGetCombatant(new CombatantId(id), out var combatant) && combatant is not null
            ? combatant.Health.Current
            : -1;
}
