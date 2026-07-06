using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// R4: the visual-editor subset. A SimpleProgram must Build into a real EffectProgram and Classify back unchanged,
// and anything outside the subset (composite root, arithmetic amount, unlisted selector, …) must Classify to null
// so the UI falls back to the JSON textarea.
public class SimpleCombatProgramTests
{
    public static IEnumerable<object[]> SubsetCases()
    {
        foreach (var node in new[] { SimpleNodeKind.GainBlock, SimpleNodeKind.Heal, SimpleNodeKind.DealDamage })
        {
            foreach (var selector in SimpleCombatProgram.SelectorKeys)
            {
                yield return
                [
                    new SimpleProgram
                    {
                        NodeKind = node, SelectorKey = selector, AmountKind = SimpleAmountKind.Const, Const = 7,
                    },
                ];
                yield return
                [
                    new SimpleProgram
                    {
                        NodeKind = node, SelectorKey = selector, AmountKind = SimpleAmountKind.EventAmount,
                    },
                ];
            }
        }
    }

    [Theory]
    [MemberData(nameof(SubsetCases))]
    public void Build_then_Classify_round_trips(SimpleProgram spec)
    {
        var program = SimpleCombatProgram.Build<TurnStartedTriggeredEffectContext>(spec);

        var back = SimpleCombatProgram.Classify(program);

        Assert.Equal(spec, back);
    }

    [Fact]
    public void Classify_returns_null_for_composite_root()
    {
        var program = new EffectProgram<TurnStartedTriggeredEffectContext>(
            new SequenceEffectNode<TurnStartedTriggeredEffectContext>(
                new IEffectNode<TurnStartedTriggeredEffectContext>[]
                {
                    new GainBlockNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<TurnStartedTriggeredEffectContext>(1)),
                }));

        Assert.Null(SimpleCombatProgram.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_arithmetic_amount()
    {
        var program = new EffectProgram<TurnStartedTriggeredEffectContext>(
            new GainBlockNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.Source,
                new AddExpression<TurnStartedTriggeredEffectContext>(
                    new ConstantExpression<TurnStartedTriggeredEffectContext>(1),
                    new ConstantExpression<TurnStartedTriggeredEffectContext>(2))));

        Assert.Null(SimpleCombatProgram.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_unlisted_selector()
    {
        var program = new EffectProgram<TurnStartedTriggeredEffectContext>(
            new GainBlockNode<TurnStartedTriggeredEffectContext>(
                CombatantTargetSelectors.EventTarget, // deliberately not in the SimpleCombatProgram.Selectors catalog
                new ConstantExpression<TurnStartedTriggeredEffectContext>(1)));

        Assert.Null(SimpleCombatProgram.Classify(program));
    }

    [Fact]
    public void Trigger_ToSimple_classifies_default_const_program_and_FromSimple_round_trips()
    {
        var trigger = RelicCombatTriggers.Get("turnStarted");

        var simple = trigger.ToSimple(trigger.NewProgram());

        Assert.NotNull(simple);
        Assert.Equal(SimpleNodeKind.GainBlock, simple!.NodeKind);
        Assert.Equal("source", simple.SelectorKey);
        Assert.Equal(SimpleAmountKind.Const, simple.AmountKind);
        Assert.Equal(3, simple.Const);

        var edited = simple with { Const = 9 };
        var rebuilt = trigger.FromSimple(edited);
        Assert.Equal(edited, trigger.ToSimple(rebuilt));
    }

    [Fact]
    public void Trigger_ToSimple_reads_event_amount_default_for_event_reading_trigger()
    {
        var trigger = RelicCombatTriggers.Get("damageReceived");

        var simple = trigger.ToSimple(trigger.NewProgram());

        Assert.NotNull(simple);
        Assert.Equal(SimpleNodeKind.GainBlock, simple!.NodeKind);
        Assert.Equal(SimpleAmountKind.EventAmount, simple.AmountKind);
    }
}
