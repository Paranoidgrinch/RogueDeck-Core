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
    public void Unknown_keys_fall_back_to_the_raw_key()
    {
        Assert.Equal("someFutureKey", StudioVocabulary.SelectorLabel("someFutureKey"));
        Assert.Equal("", StudioVocabulary.SelectorDescription("someFutureKey"));
        // The label IS the key, so Display omits the repeating parenthetical.
        Assert.Equal("SomeFutureKind", StudioVocabulary.NodeDisplay("someFutureKind"));
    }
}
