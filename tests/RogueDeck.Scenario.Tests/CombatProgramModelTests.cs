using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Tests;

// Phase 1a: the shared visual-editor model of a combat EffectProgram. A modelled leaf must Build into a real
// program and Classify back unchanged (context-generically), and anything outside the subset (composite root,
// result key, arithmetic amount, unlisted selector) must Classify to null so the UI keeps the JSON escape.
public class CombatProgramModelTests
{
    public static IEnumerable<object[]> LeafCases()
    {
        foreach (var kind in CombatProgramModel.NodeKinds.Select(n => n.Kind))
        {
            // ResourceId is only part of a gainResource node's identity; empty for the other kinds (see model).
            var resourceId = kind == "gainResource" ? "standard.energy" : "";
            foreach (var selector in CombatProgramModel.SelectorKeys)
            {
                yield return [new CombatNodeModel(kind, selector, CombatAmountSpec.FromConst(4), resourceId)];
                yield return [new CombatNodeModel(kind, selector, CombatAmountSpec.Event, resourceId)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(LeafCases))]
    public void Build_then_Classify_round_trips_for_CardPlayContext(CombatNodeModel model)
    {
        var program = CombatProgramModel.Build<CardPlayContext>(model);

        var back = CombatProgramModel.Classify(program);

        Assert.Equal(model, back);
    }

    [Fact]
    public void Same_model_builds_for_a_different_context()
    {
        // Context-generic: the identical model closes on EnemyActionContext with no extra work.
        var model = new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(6));

        var program = CombatProgramModel.Build<EnemyActionContext>(model);

        Assert.Equal(model, CombatProgramModel.Classify(program));
        Assert.IsType<DealDamageNode<EnemyActionContext>>(program.Root);
    }

    [Fact]
    public void Classify_returns_null_for_composite_root()
    {
        var program = new EffectProgram<CardPlayContext>(
            new SequenceEffectNode<CardPlayContext>(new IEffectNode<CardPlayContext>[]
            {
                new GainBlockNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, new ConstantExpression<CardPlayContext>(1)),
            }));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_arithmetic_amount()
    {
        var program = new EffectProgram<CardPlayContext>(
            new HealNode<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new AddExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(1),
                    new ConstantExpression<CardPlayContext>(2))));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void Classify_returns_null_for_unlisted_selector()
    {
        var program = new EffectProgram<CardPlayContext>(
            new GainBlockNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget, // not in the CombatProgramModel.Selectors catalog
                new ConstantExpression<CardPlayContext>(1)));

        Assert.Null(CombatProgramModel.Classify(program));
    }

    [Fact]
    public void GainResource_round_trips_its_resource_id()
    {
        var model = new CombatNodeModel("gainResource", "source", CombatAmountSpec.FromConst(2), "standard.energy");

        var program = CombatProgramModel.Build<CardPlayContext>(model);

        var node = Assert.IsType<GainResourceNode<CardPlayContext>>(program.Root);
        Assert.Equal("standard.energy", node.ResourceId.value);
        Assert.Equal(model, CombatProgramModel.Classify(program));
    }
}
