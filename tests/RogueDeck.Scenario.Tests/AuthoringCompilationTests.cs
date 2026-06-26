using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;

namespace RogueDeck.Scenario.Tests;

public class AuthoringCompilationTests
{
    private static ScenarioBlueprint SampleScenario()
    {
        var scenario = new ScenarioBlueprint();

        scenario.Statuses.Add(new StatusBlueprint("rooted")
        {
            Polarity = StatusPolarity.Debuff,
            PassiveModifiers = { },
        });
        // A status that scales block via a passive-modifier spec — proves specs flow through authoring.
        var bulwark = new StatusBlueprint("bulwark") { Polarity = StatusPolarity.Buff, UsesStacks = true };
        bulwark.PassiveModifiers.Add(new PassiveModifierSpec(
            PassiveModifierPipeline.BlockGain, PassiveModifierOperation.AddPerStack, 1));
        scenario.Statuses.Add(bulwark);

        scenario.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });

        scenario.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });

        scenario.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = 30,
            Deck = { new DeckEntry(new CardDefinitionId("smite"), 2) },
        };
        scenario.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));

        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 20 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        scenario.Enemies.Add(goblin);

        return scenario;
    }

    [Fact]
    public void Compile_ProducesBuiltRegistryWithAuthoredContentAndIntents()
    {
        var compiled = SampleScenario().Compile();

        Assert.True(compiled.Registry.IsBuilt);
        Assert.True(compiled.Registry.TryGetCard(new CardDefinitionId("smite"), out _));
        Assert.True(compiled.Registry.StatusDefinitions.ContainsKey(new StatusDefinitionId("bulwark")));

        var intent = compiled.IntentFor(new EnemyActionDefinitionId("slam"));
        Assert.NotNull(intent);
        Assert.Equal("Slam", intent!.Label);
        Assert.Equal(IntentKind.Attack, intent.Kind);
    }

    [Fact]
    public void Compile_RejectsHeroDeckReferencingUnknownCard()
    {
        var scenario = SampleScenario();
        scenario.Hero!.Deck.Add(new DeckEntry(new CardDefinitionId("does-not-exist")));
        Assert.Throws<InvalidOperationException>(() => scenario.Compile());
    }

    [Fact]
    public void Compile_RejectsEnemyReferencingUnknownAction()
    {
        var scenario = SampleScenario();
        scenario.Enemies[0].Actions.Add(new EnemyActionDefinitionId("ghost-action"));
        Assert.Throws<InvalidOperationException>(() => scenario.Compile());
    }

    [Fact]
    public void Compile_RequiresHeroAndEnemy()
    {
        Assert.Throws<InvalidOperationException>(() => new ScenarioBlueprint().Compile());
    }

    // End-to-end: a DSL-authored card program is real engine nodes — playing it deals damage.
    [Fact]
    public void DslAuthoredCard_ActuallyDealsDamageWhenPlayed()
    {
        var registry = SampleScenario().Compile().Registry;

        var combat = new CombatState(new CombatId("scenario_001"), randomSeed: 42);
        var hero = new CombatantState(
            new CombatantId("knight"), new CombatantDefinitionId("hero.knight"), "hero.knight.name",
            StandardCombatIds.PlayerTeam, new HealthState(current: 30, max: 30));
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        combat.AddCombatant(hero);
        combat.AddCombatant(new CombatantState(
            new CombatantId("goblin"), new CombatantDefinitionId("enemy.goblin"), "enemy.goblin.name",
            StandardCombatIds.EnemyTeam, new HealthState(current: 20, max: 20)));

        var card = new CardInstance(combat.CreateNextCardInstanceId(), new CardDefinitionId("smite"), new CombatantId("knight"), CardZone.Hand);
        combat.GetCardZones(new CombatantId("knight")).AddCard(card);
        combat.EnqueueEffect(new PlayCardEffectRequest(new CombatantId("knight"), card.Id, new CombatantId("goblin")));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(14, combat.GetCombatant(new CombatantId("goblin")).Health.Current); // 20 − 6
    }
}
