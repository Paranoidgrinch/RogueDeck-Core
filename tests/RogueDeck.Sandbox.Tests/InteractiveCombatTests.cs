using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

public class InteractiveCombatTests
{
    [Fact]
    public void StartInteractive_RealDeck_DrawsPerTurnAndMovesCardsBetweenZones()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Knight",
                Hp = 30,
                Energy = 3,
                UseRealDeck = true,
                DrawPerTurn = 1,
                Deck = { new DeckCardModel { CardName = "Strike", Copies = 2 } },
            },
            Cards = { new CardModel { Name = "Strike", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
        };

        var combat = new ScenarioComposer().StartInteractive(model);

        Assert.Single(combat.Hand); // drew 1 of the 2 Strikes
        combat.PlayCard(combat.Hand[0].Id, new CombatantId("dummy"));
        Assert.Empty(combat.Hand);  // the played card left the hand (→ discard)

        combat.EndTurn();           // enemy passes, wrap back to the hero's next turn
        Assert.True(combat.IsHeroTurn);
        Assert.Single(combat.Hand); // drew the second Strike
        Assert.Equal(32, combat.State.GetCombatant(new CombatantId("dummy")).Health.Current); // 40 − 8
    }

    [Fact]
    public void PlayCard_WhenAnEffectThrows_RecordsAProblem_WithoutTearingDownTheSession()
    {
        // A card that installs an unlimited temporary rule. Playing it twice hits the engine's "already
        // installed" guard; the interactive driver must surface that as a problem, not throw.
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Knight", Hp = 30, Energy = 5,
                UseRealDeck = true, DrawPerTurn = 2,
                Deck = { new DeckCardModel { CardName = "Trap", Copies = 2 } },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Trap", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel
                        {
                            Line = LineKind.InstallRule,
                            RuleEvent = TriggerEvent.TurnStarted,
                            RuleName = "trap",
                            RuleLifetime = RuleLifetimeKind.Unlimited,
                            Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 1 } },
                        },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
        };

        var combat = new ScenarioComposer().StartInteractive(model);

        var trap = combat.Hand.First(c => c.DefinitionId.value == "trap");
        combat.PlayCard(trap.Id, null);                           // installs the rule
        var second = combat.Hand.First(c => c.DefinitionId.value == "trap");
        combat.PlayCard(second.Id, null);                         // duplicate install — must not throw

        Assert.True(combat.IsHeroTurn);                           // session is intact
        var problem = combat.Steps.SelectMany(s => s.Problems).FirstOrDefault(p => p.Contains("already installed"));
        Assert.NotNull(problem);
    }

    private static SandboxModel Model()
    {
        return new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel
                {
                    Name = "Strike", Cost = 1,
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } },
                },
            },
            Enemies =
            {
                new EnemyModel
                {
                    Name = "Goblin", Hp = 20,
                    Intents =
                    {
                        new IntentModel
                        {
                            Label = "Bite", Kind = IntentKind.Attack,
                            Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 4 } },
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void StartInteractive_BeginsOnTheHeroTurn_WithAFullHandAndEnergy()
    {
        var combat = new ScenarioComposer().StartInteractive(Model());

        Assert.True(combat.IsHeroTurn);
        Assert.Equal(1, combat.Round);
        Assert.Equal(3, combat.HeroEnergy);
        Assert.Single(combat.Hand); // the single-card deck was drawn
        Assert.Equal("strike", combat.Hand[0].DefinitionId.value);
    }

    [Fact]
    public void PlayCard_AppliesTheEffect_AndSpendsEnergy()
    {
        var combat = new ScenarioComposer().StartInteractive(Model());

        combat.PlayCard(combat.Hand[0].Id, new CombatantId("goblin"));

        Assert.Equal(12, combat.State.GetCombatant(new CombatantId("goblin")).Health.Current); // 20 − 8
        Assert.Equal(2, combat.HeroEnergy); // 3 − 1
    }

    [Fact]
    public void EndTurn_RunsEnemyIntents_AndReturnsToTheHeroNextRound()
    {
        var combat = new ScenarioComposer().StartInteractive(Model());
        combat.PlayCard(combat.Hand[0].Id, new CombatantId("goblin"));

        combat.EndTurn();

        // The goblin acted its round-1 intent (Bite 4) on its own turn.
        Assert.Equal(26, combat.State.GetCombatant(new CombatantId("knight")).Health.Current); // 30 − 4
        // The turn order wrapped back to the hero for a fresh round.
        Assert.True(combat.IsHeroTurn);
        Assert.Equal(2, combat.Round);
        Assert.False(combat.IsOver);
    }

    [Fact]
    public void RenderLog_ProducesAReadableNarrative_OfWhatWasPlayed()
    {
        var combat = new ScenarioComposer().StartInteractive(Model());
        combat.PlayCard(combat.Hand[0].Id, new CombatantId("goblin"));
        combat.EndTurn();

        var log = combat.RenderLog();

        Assert.Contains("goblin takes 8 damage", log);
        Assert.Contains("knight takes 4 damage", log);
        Assert.Contains("[Attack: Bite]", log);
    }

    [Fact]
    public void PlayCard_AStaleAlreadyPlayedCard_IsACleanNoOpWithAProblem()
    {
        var combat = new ScenarioComposer().StartInteractive(Model());
        var id = combat.Hand[0].Id;

        combat.PlayCard(id, new CombatantId("goblin")); // moves the card to the discard pile
        var goblinHp = combat.State.GetCombatant(new CombatantId("goblin")).Health.Current;

        combat.PlayCard(id, new CombatantId("goblin")); // same id, no longer in hand

        Assert.Equal(goblinHp, combat.State.GetCombatant(new CombatantId("goblin")).Health.Current);
        Assert.True(combat.Steps[^1].HasProblems);
    }
}
