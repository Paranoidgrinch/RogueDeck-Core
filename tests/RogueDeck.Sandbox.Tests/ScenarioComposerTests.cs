using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

public class ScenarioComposerTests
{
    // A small editor model: a hero with a damage card, one enemy with a one-round attack intent.
    private static SandboxModel BasicModel()
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
            Rounds =
            {
                new RoundModel { HeroPlays = { new PlayModel { CardName = "Strike", TargetEnemy = "Goblin" } } },
            },
        };
    }

    [Fact]
    public void Compose_ProducesARunnablePlaythrough_ThatAppliesEffects()
    {
        var playthrough = new ScenarioComposer().Compose(BasicModel());
        var report = new ScenarioRunner().Run(playthrough);

        Assert.False(report.HasProblems);
        // Strike hit the goblin (20 − 8) and the goblin's bite hit the knight (30 − 4).
        Assert.Equal(12, report.FinalState.GetCombatant(new CombatantId("goblin")).Health.Current);
        Assert.Equal(26, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current);
    }

    [Fact]
    public void Compose_SurfacesIntentsAndKinds()
    {
        var playthrough = new ScenarioComposer().Compose(BasicModel());
        var report = new ScenarioRunner().Run(playthrough);

        var enemyStep = report.Steps.First(s => s.Intent is not null);
        Assert.Equal("Bite", enemyStep.Intent!.Label);
        Assert.Equal(IntentKind.Attack, enemyStep.Intent.Kind);
    }

    [Fact]
    public void Compose_MapsAllEffectKindsToRealNodes_AndTheyRunCleanly()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 40, Energy = 5 },
            Cards =
            {
                new CardModel
                {
                    Name = "Kitchen Sink", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 },
                        new EffectLineModel { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 4 },
                        new EffectLineModel { Kind = EffectKind.Heal, Target = EffectTarget.Self, Amount = 2 },
                        new EffectLineModel { Kind = EffectKind.ApplyStatus, Target = EffectTarget.Target, StatusId = "standard.poison", Amount = 2 },
                        new EffectLineModel { Kind = EffectKind.DrawCards, Target = EffectTarget.Self, Amount = 1 },
                        new EffectLineModel { Kind = EffectKind.GainResource, Target = EffectTarget.Self, Amount = 1 },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 100 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Kitchen Sink", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // 100 − 5 (direct hit) − 2 (the applied poison ticks on the dummy's own turn start).
        Assert.Equal(93, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_SlugsDisplayNamesIntoSafeIds()
    {
        var model = BasicModel();
        model.Hero.Name = "Sir Knight!";
        model.Enemies[0].Name = "Cave Goblin";
        model.Rounds[0].HeroPlays[0].TargetEnemy = "Cave Goblin";

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.True(report.FinalState.TryGetCombatant(new CombatantId("sir_knight"), out _));
        Assert.True(report.FinalState.TryGetCombatant(new CombatantId("cave_goblin"), out _));
    }

    [Fact]
    public void Compose_AppliesStartingStatuses_SoAnEnemyCanBeginBuffed()
    {
        var model = BasicModel();
        // The goblin starts with 2 Strength → its 4-damage bite hits for 4 + 2 = 6.
        model.Enemies[0].StartingStatuses.Add(new StartingStatusModel { StatusId = "standard.strength", Amount = 2 });

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(24, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // 30 − 6
    }

    [Fact]
    public void Compose_CustomStatus_ShapesDamageMathThroughTheRealPipeline()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 40, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Overcharge", Polarity = StatusPolarity.Buff,
                    Pipeline = PassiveModifierPipeline.DamageDealt,
                    Operation = PassiveModifierOperation.AddPerStack, Magnitude = 2,
                },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Empower", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.ApplyStatus, Target = EffectTarget.Self, StatusId = "overcharge", Amount = 1 } },
                },
                new CardModel
                {
                    Name = "Bolt", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 100 } },
            Rounds =
            {
                new RoundModel { HeroPlays = { new PlayModel { CardName = "Empower" }, new PlayModel { CardName = "Bolt", TargetEnemy = "Dummy" } } },
            },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Bolt deals 5 + 2 (Overcharge, 1 stack × magnitude 2) = 7.
        Assert.Equal(93, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_TriggeredStatus_TurnStarted_TicksDamageOnTheBearer()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Burn", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers =
                    {
                        new StatusTriggerModel
                        {
                            Event = TriggerEvent.TurnStarted,
                            Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 3 } },
                        },
                    },
                },
            },
            Enemies =
            {
                new EnemyModel { Name = "Dummy", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "burn", Amount = 1 } } },
            },
            Rounds = { new RoundModel() }, // hero just ends the turn so the dummy's turn starts
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Burn fired at the start of the dummy's turn: 20 − 3.
        Assert.Equal(17, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_TriggeredStatus_DamageTaken_HitsBackTheAttacker()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Thorns", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers =
                    {
                        new StatusTriggerModel
                        {
                            Event = TriggerEvent.DamageTaken,
                            // "Target" in a DamageTaken trigger = the attacker.
                            Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 2 } },
                        },
                    },
                },
            },
            Cards =
            {
                new CardModel { Name = "Poke", Cost = 1, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 } } },
            },
            Enemies =
            {
                new EnemyModel { Name = "Spiky", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "thorns", Amount = 1 } } },
            },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Poke", TargetEnemy = "Spiky" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(15, report.FinalState.GetCombatant(new CombatantId("spiky")).Health.Current); // 20 − 5
        Assert.Equal(28, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // 30 − 2 thorns
    }

    [Fact]
    public void Compose_AmountReadsTargetState_DamageEqualToTargetMissingHp()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                // Soften: deal 4 (constant) then Execute: deal damage = the target's missing HP.
                new CardModel { Name = "Soften", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 4 } } },
                new CardModel
                {
                    Name = "Execute", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, AmountSource = AmountSource.TargetMissingHp } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds =
            {
                new RoundModel { HeroPlays = { new PlayModel { CardName = "Soften", TargetEnemy = "Dummy" }, new PlayModel { CardName = "Execute", TargetEnemy = "Dummy" } } },
            },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Soften → 16 HP (missing 4). Execute deals 4 (the missing HP) → 12.
        Assert.Equal(12, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_AmountReadsCardsInHand_BlockScalesWithHandSize()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                // Three cards in the deck → all drawn into hand. Gain block = cards in hand.
                new CardModel { Name = "Hunker", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, AmountSource = AmountSource.CardsInHand } } },
                new CardModel { Name = "Filler1", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DrawCards, Amount = 0 } } },
                new CardModel { Name = "Filler2", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DrawCards, Amount = 0 } } },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Hunker" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Hand holds all 3 cards while Hunker resolves (the played card still occupies a slot) → 3 block.
        Assert.Equal(3, Block(report.FinalState, new CombatantId("knight")));
    }

    private static int Block(CombatState state, CombatantId id) =>
        state.GetCombatant(id).DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;

    [Fact]
    public void Compose_ForEachWithNestedIf_BranchesPerEnemy()
    {
        // "Smite all": for each enemy, if it is at/under 10 HP deal 100, otherwise deal 5.
        var smite = new EffectLineModel
        {
            Line = LineKind.ForEach,
            ForEachOver = EffectTarget.AllEnemies,
            Body =
            {
                new EffectLineModel
                {
                    Line = LineKind.If,
                    ConditionLeft = AmountSource.TargetCurrentHp,
                    ConditionOp = ComparisonOperator.LessOrEqual,
                    ConditionRight = 10,
                    Then = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 100 } },
                    Else = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 } },
                },
            },
        };

        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Smite All", Cost = 0, Effects = { smite } } },
            Enemies = { new EnemyModel { Name = "Weak", Hp = 8 }, new EnemyModel { Name = "Tough", Hp = 30 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Smite All" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(0, report.FinalState.GetCombatant(new CombatantId("weak")).Health.Current);  // 8 ≤ 10 → 100
        Assert.Equal(25, report.FinalState.GetCombatant(new CombatantId("tough")).Health.Current); // 30 > 10 → 5
    }

    [Fact]
    public void Compose_RepeatRunsTheBodyNTimes()
    {
        var flurry = new EffectLineModel
        {
            Line = LineKind.Repeat,
            RepeatCount = 3,
            Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 2 } },
        };

        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Flurry", Cost = 0, Effects = { flurry } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Flurry", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(14, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 20 − 2×3
    }

    [Fact]
    public void Compose_SelfStatusStacks_TicksDamageEqualToOwnStacks()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Blutfluch", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers =
                    {
                        new StatusTriggerModel
                        {
                            Event = TriggerEvent.TurnStarted,
                            Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, AmountSource = AmountSource.SelfStatusStacks, AmountStatusId = "blutfluch" } },
                        },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "blutfluch", Amount = 3 } } } },
            Rounds = { new RoundModel() },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(17, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 20 − 3 stacks
    }

    [Fact]
    public void Compose_Arithmetic_DamageEqualToHalfTargetHp()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel
                {
                    Name = "Snipe", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, AmountSource = AmountSource.TargetCurrentHp, ArithmeticOp = ArithmeticOp.Divide, ArithmeticOperand = 2 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Snipe", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(10, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 20 − (20 ÷ 2)
    }

    [Fact]
    public void Compose_ModifyStatusStacksAndCleanse_RemoveStacksAndPolarity()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel
                {
                    Name = "Purify", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel { Kind = EffectKind.ModifyStatusStacks, Target = EffectTarget.Target, StatusId = "standard.poison", AmountSource = AmountSource.Constant, Amount = -1 },
                        new EffectLineModel { Kind = EffectKind.Cleanse, Target = EffectTarget.Target, Polarity = StatusPolarity.Buff },
                    },
                },
            },
            Enemies =
            {
                new EnemyModel { Name = "Dummy", Hp = 40, StartingStatuses = { new StartingStatusModel { StatusId = "standard.poison", Amount = 3 }, new StartingStatusModel { StatusId = "standard.strength", Amount = 2 } } },
            },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Purify", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var dummy = report.FinalState.GetCombatant(new CombatantId("dummy"));
        var poison = dummy.Statuses.FirstOrDefault(s => s.DefinitionId == StandardCombatIds.PoisonStatus);
        Assert.NotNull(poison);
        Assert.Equal(2, poison!.Stacks); // 3 − 1
        Assert.DoesNotContain(dummy.Statuses, s => s.DefinitionId == StandardCombatIds.StrengthStatus); // buff cleansed
    }

    [Fact]
    public void Compose_SetHealthAndModifyMaxHealth_RewriteHpDirectly()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel
                {
                    Name = "Curse", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel { Kind = EffectKind.ModifyMaxHealth, Target = EffectTarget.Target, AmountSource = AmountSource.Constant, Amount = -10 },
                        new EffectLineModel { Kind = EffectKind.SetHealth, Target = EffectTarget.Target, AmountSource = AmountSource.Constant, Amount = 5 },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Curse", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var dummy = report.FinalState.GetCombatant(new CombatantId("dummy"));
        Assert.Equal(30, dummy.Health.Max);     // 40 − 10
        Assert.Equal(5, dummy.Health.Current);  // set to 5
    }

    [Fact]
    public void Compose_HealedTrigger_ClawsBackPartOfTheHealing()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Leech", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.Healed, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 5 } } } },
                },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Treat", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel { Kind = EffectKind.SetHealth, Target = EffectTarget.Target, AmountSource = AmountSource.Constant, Amount = 5 },
                        new EffectLineModel { Kind = EffectKind.Heal, Target = EffectTarget.Target, Amount = 10 },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "leech", Amount = 1 } } } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Treat", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // 5 → heal 10 → 15 → Leech claws back 5 → 10.
        Assert.Equal(10, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_CardPlayedTrigger_FiresWhenTheBearerPlaysACard()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "momentum", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Momentum", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.CardPlayed, Effects = { new EffectLineModel { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 2 } } } },
                },
            },
            Cards = { new CardModel { Name = "Wait", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DrawCards, Amount = 0 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Wait" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(2, Block(report.FinalState, new CombatantId("knight"))); // Momentum granted 2 block on play
    }

    [Fact]
    public void Compose_LowestHpEnemySelector_HitsTheWeakestEnemy()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel { Name = "Snipe", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.LowestHpEnemy, Amount = 5 } } },
            },
            Enemies = { new EnemyModel { Name = "Weak", Hp = 8 }, new EnemyModel { Name = "Tough", Hp = 30 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Snipe" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(3, report.FinalState.GetCombatant(new CombatantId("weak")).Health.Current);   // 8 − 5
        Assert.Equal(30, report.FinalState.GetCombatant(new CombatantId("tough")).Health.Current); // untouched
    }

    [Fact]
    public void Compose_ConditionComparesTwoReads()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel
                {
                    Name = "Bully", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel
                        {
                            Line = LineKind.If,
                            ConditionLeft = AmountSource.SelfCurrentHp,
                            ConditionOp = ComparisonOperator.Greater,
                            ConditionRightSource = AmountSource.TargetCurrentHp,
                            Then = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 10 } },
                        },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Bully", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Hero 30 HP > Dummy 20 HP → the Then branch fires → 10 damage.
        Assert.Equal(10, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_EventAmount_HalvesHealingViaHealedTrigger()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Leech", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.Healed, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, AmountSource = AmountSource.EventAmount, ArithmeticOp = ArithmeticOp.Divide, ArithmeticOperand = 2 } } } },
                },
            },
            Cards =
            {
                new CardModel { Name = "Treat", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.SetHealth, Target = EffectTarget.Target, AmountSource = AmountSource.Constant, Amount = 5 }, new EffectLineModel { Kind = EffectKind.Heal, Target = EffectTarget.Target, Amount = 8 } } },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "leech", Amount = 1 } } } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Treat", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // 5 → heal 8 → 13 → Leech claws back 8÷2 = 4 → 9 (net heal halved).
        Assert.Equal(9, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_EventAmount_SpiegelhautReflectsHalfTheDamageTaken()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Mirror", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.DamageTaken, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, AmountSource = AmountSource.EventAmount, ArithmeticOp = ArithmeticOp.Divide, ArithmeticOperand = 2 } } } },
                },
            },
            Cards = { new CardModel { Name = "Poke", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } } },
            Enemies = { new EnemyModel { Name = "Spiky", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "mirror", Amount = 1 } } } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Poke", TargetEnemy = "Spiky" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(12, report.FinalState.GetCombatant(new CombatantId("spiky")).Health.Current);  // 20 − 8
        Assert.Equal(26, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // 30 − (8 ÷ 2)
    }

    [Fact]
    public void Compose_DownedTrigger_ExplodesOntoTheBearersAllies()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Volatile", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.Downed, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.AllAllies, Amount = 3 } } } },
                },
            },
            Cards = { new CardModel { Name = "Pop", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } } },
            Enemies =
            {
                new EnemyModel { Name = "Bomb", Hp = 8, StartingStatuses = { new StartingStatusModel { StatusId = "volatile", Amount = 1 } } },
                new EnemyModel { Name = "Other", Hp = 20 },
            },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Pop", TargetEnemy = "Bomb" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(CombatantLifecycleState.Downed, report.FinalState.GetCombatant(new CombatantId("bomb")).LifecycleState);
        Assert.Equal(17, report.FinalState.GetCombatant(new CombatantId("other")).Health.Current); // 20 − 3 explosion
    }

    [Fact]
    public void Compose_StatusExpiredTrigger_FiresWhenADurationStatusRunsOut()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Curse", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false, UsesDuration = true,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.StatusExpired, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 5 } } } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "curse", Amount = 1, DurationTurns = 1 } } } },
            // Two rounds so the dummy's turn ends (the duration ticks out) and the expiry fires.
            Rounds = { new RoundModel(), new RoundModel() },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var dummy = report.FinalState.GetCombatant(new CombatantId("dummy"));
        Assert.Equal(15, dummy.Health.Current); // expiry dealt 5
        Assert.DoesNotContain(dummy.Statuses, s => s.DefinitionId == new StatusDefinitionId("curse"));
    }

    [Fact]
    public void Compose_DeathPreventionStatus_SurvivesAndRunsOnPreventEffects()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "anchor", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Anchor", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    PreventsDeath = true, SurvivingHealth = 1,
                    OnPreventEffects = { new EffectLineModel { Kind = EffectKind.ApplyStatus, Target = EffectTarget.AllEnemies, StatusId = "standard.poison", Amount = 2 } },
                },
            },
            Enemies =
            {
                new EnemyModel { Name = "Boss", Hp = 40, Intents = { new IntentModel { Label = "Crush", Kind = IntentKind.Attack, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 100 } } } } },
            },
            Rounds = { new RoundModel() },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var hero = report.FinalState.GetCombatant(new CombatantId("knight"));
        Assert.Equal(1, hero.Health.Current); // death prevented, survived at 1
        Assert.True(hero.IsAlive);
        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == new StatusDefinitionId("anchor")); // consumed
        Assert.Contains(report.FinalState.GetCombatant(new CombatantId("boss")).Statuses, s => s.DefinitionId == StandardCombatIds.PoisonStatus); // on-prevent AoE
    }

    [Fact]
    public void Compose_BlocksDebuffsStatus_SuppressesTheDebuffAndRunsOnBlock()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "ward", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Ward", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    BlocksDebuffs = true,
                    OnBlockEffects = { new EffectLineModel { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 } },
                },
            },
            Enemies =
            {
                new EnemyModel { Name = "Hexer", Hp = 20, Intents = { new IntentModel { Label = "Hex", Kind = IntentKind.Debuff, Effects = { new EffectLineModel { Kind = EffectKind.ApplyStatus, Target = EffectTarget.Target, StatusId = "standard.weak", Amount = 2 } } } } },
            },
            Rounds = { new RoundModel() },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var hero = report.FinalState.GetCombatant(new CombatantId("knight"));
        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == StandardCombatIds.WeakStatus); // debuff blocked
        Assert.DoesNotContain(hero.Statuses, s => s.DefinitionId == new StatusDefinitionId("ward"));  // ward consumed
        Assert.Equal(5, Block(report.FinalState, new CombatantId("knight")));                          // on-block effect
    }

    [Fact]
    public void Compose_ReadsSelfEnergy()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Surge", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, AmountSource = AmountSource.SelfEnergy } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Surge", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(17, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 20 − 3 energy
    }

    [Fact]
    public void Compose_ModuloArithmetic()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Tick", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, AmountSource = AmountSource.Constant, Amount = 7, ArithmeticOp = ArithmeticOp.Modulo, ArithmeticOperand = 3 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Tick", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(19, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 20 − (7 mod 3 = 1)
    }

    [Fact]
    public void Compose_SummonCreatesACombatant()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Conjure", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.Summon, Team = TeamChoice.Player, AmountSource = AmountSource.Constant, Amount = 10 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Conjure" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var players = report.FinalState.Combatants.Where(c => c.TeamId == StandardCombatIds.PlayerTeam).ToList();
        Assert.Equal(2, players.Count); // the knight + the summoned combatant
        Assert.Contains(players, c => c.Id != new CombatantId("knight") && c.Health.Max == 10);
    }

    [Fact]
    public void Compose_ModifyBlockAddsToTheBlockPool()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Brace", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.ModifyBlock, Target = EffectTarget.Self, AmountSource = AmountSource.Constant, Amount = 5 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Brace" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(5, Block(report.FinalState, new CombatantId("knight")));
    }

    [Fact]
    public void Compose_RepeatUntil_LoopsUntilTheConditionHolds()
    {
        var drain = new EffectLineModel
        {
            Line = LineKind.RepeatUntil,
            ConditionLeft = AmountSource.TargetCurrentHp,
            ConditionOp = ComparisonOperator.LessOrEqual,
            ConditionRightSource = AmountSource.Constant,
            ConditionRight = 10,
            Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 3 } },
        };
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Drain", Cost = 0, Effects = { drain } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Drain", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(8, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 20 → 17 → 14 → 11 → 8 (stop ≤10)
    }

    [Fact]
    public void Compose_RandomTargets_HitsTheChosenCount()
    {
        var storm = new EffectLineModel
        {
            Line = LineKind.RandomTargets,
            ForEachOver = EffectTarget.AllEnemies,
            RepeatCount = 2, // both enemies → all chosen
            Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 3 } },
        };
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards = { new CardModel { Name = "Storm", Cost = 0, Effects = { storm } } },
            Enemies = { new EnemyModel { Name = "A", Hp = 20 }, new EnemyModel { Name = "B", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Storm" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(17, report.FinalState.GetCombatant(new CombatantId("a")).Health.Current);
        Assert.Equal(17, report.FinalState.GetCombatant(new CombatantId("b")).Health.Current);
    }

    [Fact]
    public void Compose_ResourceGainedTrigger_ReactsToEnergyGain()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "overflow", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Overflow", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.ResourceGained, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, AmountSource = AmountSource.EventAmount } } } },
                },
            },
            // Cost 2 frees headroom (energy 3→1); then +3 caps at 3, gaining 2.
            Cards = { new CardModel { Name = "Channel", Cost = 2, Effects = { new EffectLineModel { Kind = EffectKind.GainResource, Target = EffectTarget.Self, Amount = 3 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Channel" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(28, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // took 2 (= energy gained)
    }

    [Fact]
    public void Compose_CardCostPaidTrigger_PunishesSpending()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "manaburn", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Manaburn", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.CardCostPaid, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 4 } } } },
                },
            },
            Cards = { new CardModel { Name = "Spell", Cost = 1, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 2 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Spell", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(26, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // Manaburn dealt 4 on cost paid
    }

    [Fact]
    public void Compose_CreateCard_AddsACopyToHand()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel { Name = "Maker", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.CreateCard, CreateCardName = "Ember" } } },
                new CardModel { Name = "Ember", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 3 } } },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Maker" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // One Ember from the deck + one created by Maker = 2 Ember instances in the combat.
        var embers = report.FinalState.GetCardZones(new CombatantId("knight"))
            .AllCards.Count(c => c.DefinitionId == new CardDefinitionId("ember"));
        Assert.Equal(2, embers);
    }

    [Fact]
    public void Compose_StatusAppliedTrigger_FiresOnOtherStatusesButNotItsOwnMarker()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "echoer", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Echoer", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.StatusApplied, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 2 } } } },
                },
            },
            // Dexterity (block pipeline) so the applied status does not boost Echoer's own self-damage.
            Cards = { new CardModel { Name = "Bless", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.ApplyStatus, Target = EffectTarget.Self, StatusId = "standard.dexterity", Amount = 1 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 20 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Bless" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Echoer did NOT fire on its own application at setup (else hero < 28); it fired once on Dexterity.
        Assert.Equal(28, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current);
    }

    [Fact]
    public void Compose_RoundEndedTrigger_HitsEveryBearer()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Doom", Polarity = StatusPolarity.Debuff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.RoundEnded, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 5 } } } },
                },
            },
            Enemies =
            {
                new EnemyModel { Name = "A", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "doom", Amount = 1 } } },
                new EnemyModel { Name = "B", Hp = 20, StartingStatuses = { new StartingStatusModel { StatusId = "doom", Amount = 1 } } },
            },
            Rounds = { new RoundModel(), new RoundModel() }, // one NextRound → one RoundEnded
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(15, report.FinalState.GetCombatant(new CombatantId("a")).Health.Current); // 20 − 5
        Assert.Equal(15, report.FinalState.GetCombatant(new CombatantId("b")).Health.Current);
    }

    [Fact]
    public void Compose_ReplayCard_EchoesTheTriggeringCard()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3, StartingStatuses = { new StartingStatusModel { StatusId = "echo", Amount = 1 } } },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Echo", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers =
                    {
                        new StatusTriggerModel
                        {
                            Event = TriggerEvent.CardPlayed,
                            Effects =
                            {
                                new EffectLineModel { Kind = EffectKind.ReplayCard, CardRef = CardRef.TriggeringCard, Target = EffectTarget.Target },
                                new EffectLineModel { Kind = EffectKind.RemoveStatus, Target = EffectTarget.Self, StatusId = "echo" },
                            },
                        },
                    },
                },
            },
            Cards = { new CardModel { Name = "Strike", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Strike", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(24, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 40 − 8 − 8 (echo)
    }

    [Fact]
    public void Compose_MoveCard_ExhaustsThePlayedCard()
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
                Deck = { new DeckCardModel { CardName = "Strike", Copies = 1 } },
                StartingStatuses = { new StartingStatusModel { StatusId = "purge", Amount = 1 } },
            },
            Statuses =
            {
                new CustomStatusModel
                {
                    Name = "Purge", Polarity = StatusPolarity.Buff, HasPassiveModifier = false,
                    Triggers = { new StatusTriggerModel { Event = TriggerEvent.CardPlayed, Effects = { new EffectLineModel { Kind = EffectKind.MoveCard, CardRef = CardRef.TriggeringCard, MoveToZone = CardZone.ExhaustPile } } } },
                },
            },
            Cards = { new CardModel { Name = "Strike", Cost = 0, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Strike", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var zones = report.FinalState.GetCardZones(new CombatantId("knight"));
        Assert.Single(zones.GetCardsInZone(CardZone.ExhaustPile));   // Purge sent the played Strike to exhaust
        Assert.Empty(zones.GetCardsInZone(CardZone.DiscardPile));    // not the normal discard
    }

    [Fact]
    public void Compose_RejectsAModelWithNoEnemy()
    {
        var model = new SandboxModel { Hero = new HeroModel { Name = "Lonely" } };
        Assert.Throws<InvalidOperationException>(() => new ScenarioComposer().Compose(model));
    }

    // ── Step 1: generic resources (beyond Energy) ───────────────────────────────────

    private static int Resource(CombatState state, string combatant, string resource) =>
        state.GetCombatant(new CombatantId(combatant)).Resources
            .TryGetValue(new ResourceId(resource), out var pool) ? pool.Current : -1;

    [Fact]
    public void Compose_NonEnergyCardCost_IsPaidFromTheCustomResource()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
            Resources = { new ResourceModel { Name = "Mana", Start = 3, Max = 3 } },
            Cards =
            {
                new CardModel
                {
                    Name = "Bolt", Cost = 0,
                    ExtraCosts = { new ResourceCostModel { ResourceName = "Mana", Amount = 2 } },
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Bolt", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(32, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 40 − 8
        Assert.Equal(1, Resource(report.FinalState, "mage", "mana"));                              // 3 − 2 spent
    }

    [Fact]
    public void Compose_UnaffordableNonEnergyCost_StopsTheCardResolving()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
            Resources = { new ResourceModel { Name = "Mana", Start = 1, Max = 3 } },
            Cards =
            {
                new CardModel
                {
                    Name = "Bolt", Cost = 0,
                    ExtraCosts = { new ResourceCostModel { ResourceName = "Mana", Amount = 2 } },
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Bolt", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        // 1 Mana cannot pay a 2-Mana cost → the bolt never lands and the Mana is untouched.
        Assert.Equal(40, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
        Assert.Equal(1, Resource(report.FinalState, "mage", "mana"));
    }

    [Fact]
    public void Compose_RefillEachTurn_TopsTheResourceBackUp()
    {
        SandboxModel Model(bool refill) => new()
        {
            Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
            Resources = { new ResourceModel { Name = "Mana", Start = 3, Max = 3, RefillEachTurn = refill } },
            Cards =
            {
                new CardModel
                {
                    Name = "Bolt", Cost = 0,
                    ExtraCosts = { new ResourceCostModel { ResourceName = "Mana", Amount = 2 } },
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 1 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 200 } },
            Rounds =
            {
                new RoundModel { HeroPlays = { new PlayModel { CardName = "Bolt", TargetEnemy = "Dummy" } } },
                new RoundModel(), // round 2: no plays — the resource's value is whatever the turn start left it
            },
        };

        var refilled = new ScenarioRunner().Run(new ScenarioComposer().Compose(Model(refill: true)));
        var spent = new ScenarioRunner().Run(new ScenarioComposer().Compose(Model(refill: false)));

        Assert.Equal(3, Resource(refilled.FinalState, "mage", "mana"));  // topped back up at round-2 turn start
        Assert.Equal(1, Resource(spent.FinalState, "mage", "mana"));     // stays spent without refill
    }

    [Fact]
    public void Compose_GainResource_AddsToANamedResource()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
            Resources = { new ResourceModel { Name = "Mana", Start = 0, Max = 5 } },
            Cards =
            {
                new CardModel
                {
                    Name = "Channel", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.GainResource, ResourceName = "Mana", Amount = 3 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Channel" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(3, Resource(report.FinalState, "mage", "mana"));
    }

    [Fact]
    public void Compose_AmountSource_ReadsANamedResource()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
            Resources = { new ResourceModel { Name = "Mana", Start = 4, Max = 4 } },
            Cards =
            {
                new CardModel
                {
                    Name = "Drain", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel
                        {
                            Kind = EffectKind.DealDamage, Target = EffectTarget.Target,
                            AmountSource = AmountSource.SelfResourceCurrent, AmountResourceId = "Mana",
                        },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Drain", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(36, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // 40 − 4 (my Mana)
    }

    // ── Step 2: generic defensive pools (beyond Block) ──────────────────────────────

    private static int Pool(CombatState state, string combatant, string pool) =>
        state.GetCombatant(new CombatantId(combatant)).DefensivePools
            .TryGetValue(new DefensivePoolId(pool), out var p) ? p.Current : -1;

    [Fact]
    public void Compose_CustomDefensivePool_AbsorbsIncomingDamage()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            DefensivePools = { new DefensivePoolModel { Name = "Ward", AbsorbsBeforeBlock = false, ClearsEachTurn = false } },
            Cards =
            {
                new CardModel
                {
                    Name = "Ward Up", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.ModifyBlock, Target = EffectTarget.Self, DefensivePoolName = "Ward", Amount = 5 } },
                },
            },
            Enemies =
            {
                new EnemyModel
                {
                    Name = "Ogre", Hp = 40,
                    Intents = { new IntentModel { Label = "Smash", Kind = IntentKind.Attack, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } } } },
                },
            },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Ward Up" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Ward (5) soaks 5 of the 8 hit; the remaining 3 lands on HP (30 − 3).
        Assert.Equal(27, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current);
        Assert.Equal(0, Pool(report.FinalState, "knight", "ward"));
    }

    [Fact]
    public void Compose_DefensivePoolBeforeBlock_DrainsAheadOfBlock()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            DefensivePools = { new DefensivePoolModel { Name = "Ward", AbsorbsBeforeBlock = true, ClearsEachTurn = false } },
            Cards =
            {
                new CardModel
                {
                    Name = "Brace", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel { Kind = EffectKind.GainBlock, Target = EffectTarget.Self, Amount = 5 },
                        new EffectLineModel { Kind = EffectKind.ModifyBlock, Target = EffectTarget.Self, DefensivePoolName = "Ward", Amount = 3 },
                    },
                },
            },
            Enemies =
            {
                new EnemyModel
                {
                    Name = "Ogre", Hp = 40,
                    Intents = { new IntentModel { Label = "Smash", Kind = IntentKind.Attack, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 6 } } } },
                },
            },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Brace" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // Ward (priority before Block) soaks 3 of the 6 hit; the remaining 3 falls to Block (5 → 2). HP untouched.
        Assert.Equal(30, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current);
        Assert.Equal(0, Pool(report.FinalState, "knight", "ward"));
        Assert.Equal(2, Pool(report.FinalState, "knight", "standard.block"));
    }

    // ── Step 3: card mechanics (tags, retain, zones) ────────────────────────────────

    [Fact]
    public void Compose_SkillTag_EnablesSkillCostReduction()
    {
        SandboxModel Model(bool skill) => new()
        {
            Hero = new HeroModel
            {
                Name = "Rogue", Hp = 30, Energy = 1,
                StartingStatuses = { new StartingStatusModel { StatusId = "standard.skill_cost_reduction", Amount = 1 } },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Trick", Cost = 2,
                    Tags = skill ? new List<string> { "skill" } : new List<string>(),
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 5 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Trick", TargetEnemy = "Dummy" } } } },
        };

        var withTag = new ScenarioRunner().Run(new ScenarioComposer().Compose(Model(skill: true)));
        var without = new ScenarioRunner().Run(new ScenarioComposer().Compose(Model(skill: false)));

        // Skill-tagged: cost 2 − 1 = 1 ≤ 1 energy → played, deals 5. Untagged: cost stays 2 > 1 → not played.
        Assert.Equal(35, withTag.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
        Assert.Equal(40, without.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_PlayedCardDestinationZone_ExhaustsOnPlay()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Knight", Hp = 30, Energy = 3,
                UseRealDeck = true, DrawPerTurn = 1,
                Deck = { new DeckCardModel { CardName = "Strike", Copies = 1 } },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Strike", Cost = 0, PlayedZone = CardZone.ExhaustPile,
                    Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 8 } },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Strike", TargetEnemy = "Dummy" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var zones = report.FinalState.GetCardZones(new CombatantId("knight"));
        Assert.Single(zones.GetCardsInZone(CardZone.ExhaustPile)); // exhaust-on-play sent it to exhaust
        Assert.Empty(zones.GetCardsInZone(CardZone.DiscardPile));   // not the normal discard
    }

    [Fact]
    public void Compose_RetainInHand_KeepsTheCardAtTurnEnd()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Knight", Hp = 30, Energy = 3,
                UseRealDeck = true, DrawPerTurn = 1,
                Deck = { new DeckCardModel { CardName = "Hold", Copies = 1 } },
            },
            Cards = { new CardModel { Name = "Hold", Cost = 0, RetainInHand = true } },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel() }, // hero plays nothing, then ends the turn
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var zones = report.FinalState.GetCardZones(new CombatantId("knight"));
        Assert.Single(zones.GetCardsInZone(CardZone.Hand));        // retained across the turn end
        Assert.Empty(zones.GetCardsInZone(CardZone.DiscardPile));  // not discarded
    }

    // ── Step 4: built-in statuses exposed in the catalog ────────────────────────────

    [Fact]
    public void Compose_ThornsStatus_ReflectsDamageToTheAttacker()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Knight", Hp = 30, Energy = 3,
                StartingStatuses = { new StartingStatusModel { StatusId = "standard.thorns", Amount = 3 } },
            },
            Cards = { new CardModel { Name = "Wait", Cost = 0 } },
            Enemies =
            {
                new EnemyModel
                {
                    Name = "Ogre", Hp = 40,
                    Intents = { new IntentModel { Label = "Smash", Kind = IntentKind.Attack, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 6 } } } },
                },
            },
            Rounds = { new RoundModel() }, // hero waits; the ogre attacks and eats the thorns
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(24, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // 30 − 6
        Assert.Equal(37, report.FinalState.GetCombatant(new CombatantId("ogre")).Health.Current);   // 40 − 3 thorns
    }

    // ── Step 5: temporary rules (install / remove) ──────────────────────────────────

    [Fact]
    public void Compose_InstallRule_FiresOnTheEvent()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
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
                            RuleLifetime = RuleLifetimeKind.OneShot,
                            // When a turn starts, the unit whose turn it is takes 5 (Self = the event source).
                            Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 5 } },
                        },
                    },
                },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Trap" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        // The rule was installed mid-hero-turn; the next turn start (the dummy's) fires it once: 40 − 5.
        Assert.Equal(35, report.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);
    }

    [Fact]
    public void Compose_RemoveRule_CancelsAnInstalledRule()
    {
        SandboxModel Model(bool disarm)
        {
            var plays = new RoundModel { HeroPlays = { new PlayModel { CardName = "Trap" } } };
            if (disarm)
                plays.HeroPlays.Add(new PlayModel { CardName = "Disarm" });

            return new SandboxModel
            {
                Hero = new HeroModel { Name = "Mage", Hp = 30, Energy = 3 },
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
                                Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 5 } },
                            },
                        },
                    },
                    new CardModel
                    {
                        Name = "Disarm", Cost = 0,
                        Effects = { new EffectLineModel { Kind = EffectKind.RemoveRule, RuleName = "trap" } },
                    },
                },
                Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
                Rounds = { plays },
            };
        }

        var armed = new ScenarioRunner().Run(new ScenarioComposer().Compose(Model(disarm: false)));
        var disarmed = new ScenarioRunner().Run(new ScenarioComposer().Compose(Model(disarm: true)));

        Assert.Equal(35, armed.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current);    // rule fired
        Assert.Equal(40, disarmed.FinalState.GetCombatant(new CombatantId("dummy")).Health.Current); // removed first
    }

    // ── Step 6: extra effects + trigger events ──────────────────────────────────────

    [Fact]
    public void Compose_MoveAllCards_ExhaustsTheRestOfTheHand()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "Knight", Hp = 30, Energy = 3,
                UseRealDeck = true, DrawPerTurn = 3,
                Deck = { new DeckCardModel { CardName = "Bomb", Copies = 1 }, new DeckCardModel { CardName = "Junk", Copies = 2 } },
            },
            Cards =
            {
                new CardModel
                {
                    Name = "Bomb", Cost = 0,
                    Effects = { new EffectLineModel { Kind = EffectKind.MoveAllCards, MoveFromZone = CardZone.Hand, MoveToZone = CardZone.ExhaustPile } },
                },
                new CardModel { Name = "Junk", Cost = 0 },
            },
            Enemies = { new EnemyModel { Name = "Dummy", Hp = 40 } },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Bomb" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        var zones = report.FinalState.GetCardZones(new CombatantId("knight"));
        // The whole hand (both Junk plus Bomb itself, still in hand mid-resolution) is sent to exhaust.
        Assert.Equal(3, zones.GetCardsInZone(CardZone.ExhaustPile).Count);
        Assert.Empty(zones.GetCardsInZone(CardZone.Hand));
    }

    [Fact]
    public void Compose_TemporaryRule_OnEnemyActionExecuted_Fires()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel { Name = "Knight", Hp = 30, Energy = 3 },
            Cards =
            {
                new CardModel
                {
                    Name = "Curse", Cost = 0,
                    Effects =
                    {
                        new EffectLineModel
                        {
                            Line = LineKind.InstallRule,
                            RuleEvent = TriggerEvent.EnemyActionExecuted,
                            RuleLifetime = RuleLifetimeKind.Unlimited,
                            // Whenever an enemy acts, that enemy (Self = the actor) takes 3.
                            Body = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Self, Amount = 3 } },
                        },
                    },
                },
            },
            Enemies =
            {
                new EnemyModel
                {
                    Name = "Ogre", Hp = 40,
                    Intents = { new IntentModel { Label = "Smash", Kind = IntentKind.Attack, Effects = { new EffectLineModel { Kind = EffectKind.DealDamage, Target = EffectTarget.Target, Amount = 4 } } } },
                },
            },
            Rounds = { new RoundModel { HeroPlays = { new PlayModel { CardName = "Curse" } } } },
        };

        var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

        Assert.False(report.HasProblems);
        Assert.Equal(26, report.FinalState.GetCombatant(new CombatantId("knight")).Health.Current); // 30 − 4 (ogre's smash)
        Assert.Equal(37, report.FinalState.GetCombatant(new CombatantId("ogre")).Health.Current);   // 40 − 3 (rule on its action)
    }
}
