using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The RelicEditor's control-flow body model: the leaf effects a Repeat/Conditional branch can hold, as
// IRunEffectRequest. New defaults must be editable, amount-bearing leaves must round-trip their amount through the
// Computed* effects, and anything outside the curated set classifies as non-editable so the whole control-flow
// effect stays read-only rather than being clobbered.
public class RelicRequestsTests
{
    [Theory]
    [InlineData("gold")]
    [InlineData("resource")]
    [InlineData("heal")]
    [InlineData("damage")]
    [InlineData("maxhp")]
    [InlineData("flag")]
    [InlineData("counter")]
    [InlineData("setcounter")]
    public void NewDefaults_AreAllEditable(string kind)
    {
        Assert.True(RelicRequests.IsEditable(RelicRequests.New(kind)));
    }

    [Fact]
    public void WithAmount_ConstantAndComputed_RoundTripThroughAmountOf()
    {
        // A constant heal keeps a literal HealRunEffect; a "value of…" amount becomes a ComputedHealRunEffect that
        // still classifies back to the same spec.
        var heal = RelicRequests.New("heal");

        var constant = RelicRequests.WithAmount(heal, RelicAmountSpec.FromConst(9));
        Assert.IsType<HealRunEffect>(constant);
        Assert.Equal(RelicAmountSpec.FromConst(9), RelicRequests.AmountOf(constant));

        var computed = RelicRequests.WithAmount(heal, RelicAmountSpec.FromValue("missingHp"));
        Assert.IsType<ComputedHealRunEffect>(computed);
        Assert.Equal(RelicAmountSpec.FromValue("missingHp"), RelicRequests.AmountOf(computed));
    }

    [Fact]
    public void WithAmount_OnResource_PreservesTheResourceId()
    {
        var mana = RelicRequests.WithResource(RelicRequests.New("resource"), new RunResourceId("mana"));

        var computed = RelicRequests.WithAmount(mana, RelicAmountSpec.FromValue("relicCount"));
        var effect = Assert.IsType<ComputedResourceRunEffect>(computed);
        Assert.Equal(new RunResourceId("mana"), effect.Resource);

        var back = RelicRequests.WithAmount(computed, RelicAmountSpec.FromConst(4));
        var literal = Assert.IsType<ChangeResourceRunEffect>(back);
        Assert.Equal(new RunResourceId("mana"), literal.Resource);
        Assert.Equal(4, literal.Delta);
    }

    [Fact]
    public void IsEditable_MarksAdvancedComputedAndUnknownEffectsNonEditable()
    {
        // A computed amount with arithmetic is beyond the single-value model → the body leaf is not editable.
        var arithmetic = new ComputedHealRunEffect(RunExpr.Add(RunExpr.CurrentHealth, RunExpr.Const(1)));
        Assert.False(RelicRequests.IsEditable(arithmetic));

        // An effect the body editor does not model (card grant) is not a body leaf.
        Assert.False(RelicRequests.IsEditable(new AddCardToDeckRunEffect(new CardDefinitionId("strike"))));
    }
}
