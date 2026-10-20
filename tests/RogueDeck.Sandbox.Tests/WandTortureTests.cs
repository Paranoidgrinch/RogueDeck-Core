using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// THE WAND (Sorcerer's Apprentice plan): cards inscribed instead of played, read as an ordered program when the
// wand is cast. Driven through the real host path wherever a fight is involved.
public class WandTortureTests
{
    private static readonly StatusDefinitionId Bearer = new("wand_bearer");

    private static ICombatExpression<CardPlayContext, int> Const(int value) => new ConstantExpression<CardPlayContext>(value);

    private static WandRules Rules => new()
    {
        Bearer = Bearer,
        Singe = new StatusDefinitionId("singe"),
        Grit = new StatusDefinitionId("grit"),
        Hooks = new Dictionary<WandHook, StatusDefinitionId> { [WandHook.BrassDividers] = new("brass_dividers") },
    };

    // A card's wand mode and the game's wand rules are part of the exported document: what goes in comes out,
    // expressions and programs inside the imprint included.
    [Fact]
    public void The_wand_rules_and_a_cards_imprint_survive_the_document()
    {
        var card = new CardData
        {
            Id = "candle_stub",
            NameKey = "Candle Stub",
            Wand = new WandImprint
            {
                Operation = WandOperation.Speak,
                Atoms =
                [
                    new WandAtom { Kind = WandAtomKind.Damage, Amount = Const(4) },
                    new WandAtom
                    {
                        Kind = WandAtomKind.Status, Amount = Const(1), Status = new StatusDefinitionId("singe"),
                        Only = WandPosition.FirstExecuted,
                    },
                    new WandAtom
                    {
                        Kind = WandAtomKind.Program, Copyable = false, AfterCast = true,
                        Program = CombatProgramModel.Build<CardPlayContext>(
                            new CombatNodeModel("drawCards", "source", CombatAmountSpec.FromConst(1))),
                    },
                ],
                GroundIfSpeakFollows = 1,
            },
        };
        var blueprint = new RunBlueprint(
            [], new Dictionary<string, EventScript>(), [], [card], [], new RunMap([]))
        { Wand = Rules };

        var options = RunJson.CreateOptions();
        var restored = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, options), options);

        Assert.Equal(Bearer, restored.Wand!.Bearer);
        Assert.Equal(new StatusDefinitionId("brass_dividers"), restored.Wand.Hooks[WandHook.BrassDividers]);
        var imprint = restored.Cards.Single().Wand!;
        Assert.Equal(WandOperation.Speak, imprint.Operation);
        Assert.Equal(3, imprint.Atoms.Count);
        Assert.Equal(WandPosition.FirstExecuted, imprint.Atoms[1].Only);
        Assert.NotNull(imprint.Atoms[2].Program);
        Assert.Equal(1, imprint.GroundIfSpeakFollows);

        // …and a document without a wand says nothing about one.
        var plain = new RunBlueprint([], new Dictionary<string, EventScript>(), [], [], [], new RunMap([]));
        Assert.DoesNotContain("\"Wand\"", RunJson.ToJson(plain, options), StringComparison.Ordinal);
    }
}
