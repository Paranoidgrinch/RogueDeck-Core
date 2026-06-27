using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Fuzzing;

// Builds a random-but-valid SandboxModel from the full authoring vocabulary, deterministically from a seed.
// Used by the fuzzer to stress the composer + engine: every generated model is internally consistent
// (cards/statuses/enemies it references are ones it defined), so any exception thrown while running it is a
// real engine/harness bug rather than a malformed-input rejection.
public sealed class RandomScenarioGenerator
{
    private readonly Random _rng;
    private string[] _statusPool = Array.Empty<string>();
    private string[] _cardPool = Array.Empty<string>();
    private string[] _enemyPool = Array.Empty<string>();

    public RandomScenarioGenerator(int seed) => _rng = new Random(seed);

    private static readonly string[] StandardStatuses =
        EffectCatalog.Statuses.Select(s => s.Id).ToArray();

    public SandboxModel Generate()
    {
        var model = new SandboxModel
        {
            Hero = new HeroModel
            {
                Name = "fuzzhero",
                Hp = _rng.Next(10, 60),
                Energy = _rng.Next(1, 6),
            },
        };

        // Custom status names + card names + enemy names are fixed up front so effects can reference them.
        var statusCount = _rng.Next(0, 4);
        var cardCount = _rng.Next(1, 6);
        var enemyCount = _rng.Next(1, 4);

        var customStatusNames = Enumerable.Range(0, statusCount).Select(i => $"fuzzstatus{i}").ToArray();
        _cardPool = Enumerable.Range(0, cardCount).Select(i => $"fuzzcard{i}").ToArray();
        _enemyPool = Enumerable.Range(0, enemyCount).Select(i => $"fuzzenemy{i}").ToArray();
        _statusPool = StandardStatuses.Concat(customStatusNames).ToArray();

        foreach (var name in customStatusNames)
            model.Statuses.Add(GenerateStatus(name));
        foreach (var name in _cardPool)
            model.Cards.Add(GenerateCard(name));
        foreach (var name in _enemyPool)
            model.Enemies.Add(GenerateEnemy(name));

        if (_rng.NextDouble() < 0.4)
        {
            model.Hero.UseRealDeck = true;
            model.Hero.DrawPerTurn = _rng.Next(1, 6);
            foreach (var card in Pick(_cardPool, _rng.Next(1, _cardPool.Length + 1)))
                model.Hero.Deck.Add(new DeckCardModel { CardName = card, Copies = _rng.Next(1, 4) });
        }

        MaybeAddStartingStatuses(model.Hero.StartingStatuses);

        var rounds = _rng.Next(1, 4);
        for (var r = 0; r < rounds; r++)
        {
            var round = new RoundModel();
            for (var p = 0; p < _rng.Next(0, 4); p++)
                round.HeroPlays.Add(new PlayModel
                {
                    CardName = OneOf(_cardPool),
                    TargetEnemy = _rng.NextDouble() < 0.7 ? OneOf(_enemyPool) : null,
                });
            model.Rounds.Add(round);
        }

        return model;
    }

    private CustomStatusModel GenerateStatus(string name)
    {
        var status = new CustomStatusModel
        {
            Name = name,
            Polarity = OneOf<StatusPolarity>(),
            HasPassiveModifier = _rng.NextDouble() < 0.6,
            Pipeline = OneOf<PassiveModifierPipeline>(),
            Operation = OneOf<PassiveModifierOperation>(),
            Magnitude = _rng.Next(-2, 6),
            UsesDuration = _rng.NextDouble() < 0.4,
        };

        for (var t = 0; t < _rng.Next(0, 3); t++)
            status.Triggers.Add(new StatusTriggerModel
            {
                Event = OneOf(StatusTriggerEvents),
                Effects = GenerateEffects(1, 2, depth: 2),
            });

        if (_rng.NextDouble() < 0.15)
        {
            status.PreventsDeath = true;
            status.SurvivingHealth = _rng.Next(1, 10);
            status.OnPreventEffects = GenerateLeafEffects(1, 2);
        }
        if (_rng.NextDouble() < 0.15)
        {
            status.BlocksDebuffs = true;
            status.OnBlockEffects = GenerateLeafEffects(1, 2);
        }

        return status;
    }

    private CardModel GenerateCard(string name) => new()
    {
        Name = name,
        Cost = _rng.Next(0, 4),
        Effects = GenerateEffects(1, 3, depth: 2),
    };

    private EnemyModel GenerateEnemy(string name)
    {
        var enemy = new EnemyModel { Name = name, Hp = _rng.Next(8, 50) };
        MaybeAddStartingStatuses(enemy.StartingStatuses);
        for (var i = 0; i < _rng.Next(0, 3); i++)
            enemy.Intents.Add(new IntentModel
            {
                Label = $"act{i}",
                Kind = OneOf<IntentKind>(),
                Effects = GenerateEffects(1, 2, depth: 2),
            });
        return enemy;
    }

    private void MaybeAddStartingStatuses(List<StartingStatusModel> statuses)
    {
        for (var i = 0; i < _rng.Next(0, 3); i++)
            statuses.Add(new StartingStatusModel
            {
                StatusId = OneOf(_statusPool),
                Amount = _rng.Next(1, 4),
                DurationTurns = _rng.NextDouble() < 0.3 ? _rng.Next(1, 4) : 0,
            });
    }

    // ── Effect generation ────────────────────────────────────────────────────────

    private List<EffectLineModel> GenerateEffects(int min, int max, int depth)
    {
        var count = _rng.Next(min, max + 1);
        return Enumerable.Range(0, count).Select(_ => GenerateLine(depth)).ToList();
    }

    private List<EffectLineModel> GenerateLeafEffects(int min, int max)
    {
        var count = _rng.Next(min, max + 1);
        return Enumerable.Range(0, count).Select(_ => GenerateLeaf()).ToList();
    }

    private EffectLineModel GenerateLine(int depth)
    {
        // At depth 0 only leaf effects; otherwise a small chance of a control node.
        if (depth <= 0 || _rng.NextDouble() < 0.75)
            return GenerateLeaf();

        return OneOf(new[] { LineKind.If, LineKind.Repeat, LineKind.ForEach, LineKind.Causal, LineKind.RandomTargets, LineKind.RepeatUntil }) switch
        {
            LineKind.If => new EffectLineModel
            {
                Line = LineKind.If,
                ConditionLeft = OneOf(ReadSources),
                ConditionOp = OneOf<ComparisonOperator>(),
                ConditionRightSource = _rng.NextDouble() < 0.5 ? AmountSource.Constant : OneOf(ReadSources),
                ConditionRight = _rng.Next(0, 30),
                AmountStatusId = OneOf(_statusPool),
                Then = GenerateEffects(1, 2, depth - 1),
                Else = _rng.NextDouble() < 0.5 ? GenerateEffects(1, 1, depth - 1) : new List<EffectLineModel>(),
            },
            LineKind.Repeat => new EffectLineModel { Line = LineKind.Repeat, RepeatCount = _rng.Next(0, 4), Body = GenerateEffects(1, 2, depth - 1) },
            LineKind.ForEach => new EffectLineModel { Line = LineKind.ForEach, ForEachOver = OneOf(Collections), Body = GenerateEffects(1, 2, depth - 1) },
            LineKind.Causal => new EffectLineModel { Line = LineKind.Causal, Body = GenerateEffects(1, 2, depth - 1) },
            LineKind.RandomTargets => new EffectLineModel { Line = LineKind.RandomTargets, RepeatCount = _rng.Next(0, 3), ForEachOver = OneOf(Collections), Body = GenerateEffects(1, 2, depth - 1) },
            _ => new EffectLineModel
            {
                Line = LineKind.RepeatUntil,
                ConditionLeft = OneOf(ReadSources),
                ConditionOp = OneOf<ComparisonOperator>(),
                ConditionRightSource = AmountSource.Constant,
                ConditionRight = _rng.Next(0, 20),
                AmountStatusId = OneOf(_statusPool),
                Body = GenerateLeafEffects(1, 2),
            },
        };
    }

    private EffectLineModel GenerateLeaf()
    {
        var line = new EffectLineModel
        {
            Line = LineKind.Effect,
            Kind = WeightedKind(),
            Target = OneOf<EffectTarget>(),
            AmountSource = _rng.NextDouble() < 0.6 ? AmountSource.Constant : OneOf(ReadSources),
            Amount = _rng.Next(-3, 12),
            StatusId = OneOf(_statusPool),
            AmountStatusId = OneOf(_statusPool),
            Polarity = OneOf<StatusPolarity>(),
            IgnoresBlock = _rng.NextDouble() < 0.2,
            Team = OneOf<TeamChoice>(),
            Result = OneOf(new[] { CombatResult.Victory, CombatResult.Defeat, CombatResult.Draw }),
            CreateCardName = OneOf(_cardPool),
            CardRef = OneOf<CardRef>(),
            MoveToZone = OneOf<CardZone>(),
            DurationTurns = _rng.NextDouble() < 0.3 ? _rng.Next(1, 4) : 0,
        };
        if (_rng.NextDouble() < 0.3)
        {
            line.ArithmeticOp = OneOf(new[] { ArithmeticOp.Multiply, ArithmeticOp.Divide, ArithmeticOp.Add, ArithmeticOp.Subtract, ArithmeticOp.Modulo, ArithmeticOp.Min, ArithmeticOp.Max });
            line.ArithmeticOperand = _rng.Next(1, 6);
        }
        return line;
    }

    // EndCombat is weighted very low so combats do not end immediately and lose coverage.
    private EffectKind WeightedKind()
    {
        var kinds = Enum.GetValues<EffectKind>();
        while (true)
        {
            var k = kinds[_rng.Next(kinds.Length)];
            if (k == EffectKind.EndCombat && _rng.NextDouble() < 0.95)
                continue;
            return k;
        }
    }

    private static readonly AmountSource[] ReadSources = Enum.GetValues<AmountSource>()
        .Where(a => a != AmountSource.Constant).ToArray();

    private static readonly EffectTarget[] Collections =
        { EffectTarget.AllEnemies, EffectTarget.AllAllies, EffectTarget.DamagedAllies, EffectTarget.AllCombatants };

    // Only the events a status-bound trigger supports (mirrors the composer's BuildTrigger); the broader
    // resource/card events are for temporary rules, which the fuzzer does not generate.
    private static readonly TriggerEvent[] StatusTriggerEvents =
    {
        TriggerEvent.TurnStarted, TriggerEvent.TurnEnded, TriggerEvent.DamageTaken, TriggerEvent.DamageDealt,
        TriggerEvent.Healed, TriggerEvent.CardPlayed, TriggerEvent.Downed, TriggerEvent.StatusExpired,
        TriggerEvent.ResourceGained, TriggerEvent.CardCostPaid, TriggerEvent.StatusApplied,
        TriggerEvent.StatusRemoved, TriggerEvent.StatusMerged, TriggerEvent.RoundStarted, TriggerEvent.RoundEnded,
    };

    // ── small helpers ──
    private T OneOf<T>() where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[_rng.Next(values.Length)];
    }

    private T OneOf<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];

    private IEnumerable<T> Pick<T>(IReadOnlyList<T> items, int count) =>
        items.OrderBy(_ => _rng.Next()).Take(Math.Min(count, items.Count));
}
