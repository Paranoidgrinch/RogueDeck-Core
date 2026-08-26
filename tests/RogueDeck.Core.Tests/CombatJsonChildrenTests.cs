using System.Text.Json;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Every effect node exposes Children so the tree can be walked. On the sequence nodes that list IS the node —
// its constructor reads it back — but on a conditional it is [Then, Else] and on a loop it is [Body]: the same
// children the node already names. Writing the view as well duplicates every subtree once per level, so a
// twelve-deep conditional serialized 4096 copies of its leaves (one authored boss rule: 311,624 nodes, 15 MB).
//
// These tests pin the rule: written where a constructor reads it, walkable everywhere.
public class CombatJsonChildrenTests
{
    private static readonly JsonSerializerOptions Options = CombatJson.CreateOptions<CardPlayContext>();

    private static ICombatExpression<CardPlayContext, int> Const(int value) =>
        new ConstantExpression<CardPlayContext>(value);

    private static IEffectNode<CardPlayContext> Damage(int amount) =>
        new DealDamageNode<CardPlayContext>(new EventTargetCombatantTargetSelector(), Const(amount));

    private static ICombatExpression<CardPlayContext, bool> Always() =>
        new ComparisonExpression<CardPlayContext>(Const(1), ComparisonOperator.Equal, Const(1));

    [Fact]
    public void A_conditional_writes_its_branches_once()
    {
        IEffectNode<CardPlayContext> node = new ConditionalEffectNode<CardPlayContext>(
            Always(), Damage(7), @else: Damage(9));

        var json = CombatJson.ToJson(node, Options);

        Assert.DoesNotContain("\"Children\"", json, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(json, "\"Value\": 7"));
        Assert.Equal(1, Occurrences(json, "\"Value\": 9"));
    }

    // The point of the rule: nesting costs what it says. Depth must be LINEAR — twice as deep, about twice as
    // long. Duplicating the branches made it double per level instead, so twelve deep was 4096 copies.
    [Fact]
    public void Nesting_conditionals_does_not_multiply_what_they_hold()
    {
        IEffectNode<CardPlayContext> Nest(int depth)
        {
            var node = Damage(42);
            for (var i = 0; i < depth; i++)
                node = new ConditionalEffectNode<CardPlayContext>(Always(), node, @else: Damage(1));
            return node;
        }

        var shallow = CombatJson.ToJson(Nest(6), Options);
        var deep = CombatJson.ToJson(Nest(12), Options);

        Assert.Equal(1, Occurrences(deep, "\"Value\": 42"));
        Assert.True(deep.Length < shallow.Length * 3,
            $"twice the depth grew the document from {shallow.Length} to {deep.Length} characters");
    }

    [Fact]
    public void A_loop_writes_its_body_once()
    {
        IEffectNode<CardPlayContext> node = new ForEachTargetEffectNode<CardPlayContext>(
            new AllEnemiesOfSourceCombatantTargetSelector(), Damage(3));

        var json = CombatJson.ToJson(node, Options);

        Assert.DoesNotContain("\"Children\"", json, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(json, "\"Value\": 3"));
    }

    // …while the sequences keep writing theirs, because that list is what they are.
    [Fact]
    public void A_sequence_still_writes_the_children_it_is_made_of()
    {
        IEffectNode<CardPlayContext> sequence = new CausalSequenceEffectNode<CardPlayContext>(
            [Damage(2), Damage(4)]);

        var json = CombatJson.ToJson(sequence, Options);
        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(json, Options);

        Assert.Contains("\"Children\"", json, StringComparison.Ordinal);
        Assert.Equal(2, back.Children.Count);
        Assert.Equal(json, CombatJson.ToJson(back, Options));
    }

    // What is not written is still there after reading: Children is rebuilt from the branches it views.
    [Fact]
    public void A_conditional_read_back_can_still_be_walked()
    {
        IEffectNode<CardPlayContext> node = new ConditionalEffectNode<CardPlayContext>(
            Always(), Damage(7), @else: Damage(9));

        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(
            CombatJson.ToJson(node, Options), Options);

        var conditional = Assert.IsType<ConditionalEffectNode<CardPlayContext>>(back);
        Assert.Equal(2, conditional.Children.Count);
        Assert.Same(conditional.Then, conditional.Children[0]);
        Assert.Same(conditional.Else, conditional.Children[1]);
    }

    // A document written before the rule still carries the duplicates. It has to keep loading, unchanged.
    [Fact]
    public void A_document_that_still_carries_the_duplicates_loads_the_same_tree()
    {
        var current = CombatJson.ToJson(
            (IEffectNode<CardPlayContext>)new ConditionalEffectNode<CardPlayContext>(
                Always(), Damage(7), @else: Damage(9)),
            Options);

        // The old shape: the branches again, under "Children", exactly as the writer used to emit them.
        var branches = CombatJson.ToJson(Damage(7), Options) + ", " + CombatJson.ToJson(Damage(9), Options);
        var old = current.TrimEnd().TrimEnd('}').TrimEnd().TrimEnd('}').TrimEnd()
            + $", \"Children\": [{branches}] }} }}";

        var back = CombatJson.FromJson<IEffectNode<CardPlayContext>>(old, Options);

        Assert.Equal(current, CombatJson.ToJson(back, Options));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
