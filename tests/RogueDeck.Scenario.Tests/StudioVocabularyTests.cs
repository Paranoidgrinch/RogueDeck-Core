using System.Linq;
using RogueDeck.Scenario.Authoring;
using Xunit;

namespace RogueDeck.Scenario.Tests;

// Parity guards for the Studio display vocabulary: every technical key a Studio dropdown offers must carry a
// plain-language label and a one-sentence help text, so a new selector or node kind cannot ship unlabeled.
public class StudioVocabularyTests
{
    [Fact]
    public void Every_selector_key_has_a_label_and_a_description()
    {
        foreach (var key in CombatProgramModel.AllSelectorKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(StudioVocabulary.SelectorLabel(key)),
                $"Selector '{key}' has no label in StudioVocabulary.");
            Assert.NotEqual(key, StudioVocabulary.SelectorLabel(key));
            Assert.False(string.IsNullOrWhiteSpace(StudioVocabulary.SelectorDescription(key)),
                $"Selector '{key}' has no description in StudioVocabulary.");
        }
    }

    [Fact]
    public void Selector_labels_are_unique()
    {
        var labels = CombatProgramModel.AllSelectorKeys.Select(StudioVocabulary.SelectorLabel).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void Every_node_kind_has_a_description()
    {
        foreach (var (kind, label) in CombatProgramModel.AllKinds)
        {
            Assert.False(string.IsNullOrWhiteSpace(label), $"Kind '{kind}' has no label.");
            Assert.False(string.IsNullOrWhiteSpace(StudioVocabulary.NodeDescription(kind)),
                $"Kind '{kind}' has no description in StudioVocabulary.");
        }
    }

    [Fact]
    public void Display_shows_sentence_cased_label_then_technical_key()
    {
        Assert.Equal("Deal damage (dealDamage)", StudioVocabulary.Display("deal damage", "dealDamage"));
        Assert.Equal("Every enemy (allEnemies)", StudioVocabulary.SelectorDisplay("allEnemies"));
        Assert.Equal("Deal damage (dealDamage)", StudioVocabulary.NodeDisplay("dealDamage"));
    }

    [Fact]
    public void Node_kind_groups_cover_the_catalog_exactly()
    {
        var grouped = StudioVocabulary.NodeKindGroups.SelectMany(g => g.Kinds).ToList();
        Assert.Equal(grouped.Count, grouped.Distinct().Count());
        Assert.Equal(
            CombatProgramModel.AllKinds.Select(k => k.Kind).OrderBy(k => k, StringComparer.Ordinal),
            grouped.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Amount_kind_groups_cover_the_catalog_exactly()
    {
        var grouped = StudioVocabulary.AmountKindGroups.SelectMany(g => g.Kinds).ToList();
        Assert.Equal(grouped.Count, grouped.Distinct().Count());
        Assert.Equal(
            StudioVocabulary.AmountKinds.Select(k => k.Kind).OrderBy(k => k, StringComparer.Ordinal),
            grouped.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Describe_renders_common_leaves_composites_and_nested_amounts()
    {
        Assert.Equal("deal the event amount damage to every enemy",
            StudioVocabulary.Describe(new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.Event)));

        Assert.Equal("deal (missing HP of self / the acting unit × 2) damage to toughest enemy",
            StudioVocabulary.Describe(new CombatNodeModel("dealDamage", "highestHealthEnemy",
                CombatAmountSpec.Binary("mul",
                    new CombatAmountSpec("missingHealth", SelectorKey: "source"),
                    CombatAmountSpec.FromConst(2)))));

        Assert.Equal("apply 3× standard.poison to the chosen target",
            StudioVocabulary.Describe(new CombatNodeModel("applyStatus", "eventTarget",
                CombatAmountSpec.FromConst(3), StatusId: "standard.poison")));

        Assert.Equal("2× (give self / the acting unit 5 block)",
            StudioVocabulary.Describe(CombatNodeModel.Repeat(CombatAmountSpec.FromConst(2),
                new CombatNodeModel("gainBlock", "source", CombatAmountSpec.FromConst(5)))));

        // A plain sequence starts its steps together ("and"); a causal one runs them in order ("then").
        Assert.Equal("heal self / the acting unit for 4; then deal 6 damage to every enemy",
            StudioVocabulary.Describe(CombatNodeModel.CausalSequence(new[]
            {
                new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(4)),
                new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(6)),
            })));

        Assert.Equal("heal self / the acting unit for 4; and deal 6 damage to every enemy",
            StudioVocabulary.Describe(CombatNodeModel.Sequence(new[]
            {
                new CombatNodeModel("heal", "source", CombatAmountSpec.FromConst(4)),
                new CombatNodeModel("dealDamage", "allEnemies", CombatAmountSpec.FromConst(6)),
            })));
    }

    [Fact]
    public void Unknown_keys_fall_back_to_the_raw_key()
    {
        Assert.Equal("someFutureKey", StudioVocabulary.SelectorLabel("someFutureKey"));
        Assert.Equal("", StudioVocabulary.SelectorDescription("someFutureKey"));
        // The label IS the key, so Display omits the repeating parenthetical.
        Assert.Equal("SomeFutureKind", StudioVocabulary.NodeDisplay("someFutureKind"));
    }
}
