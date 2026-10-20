using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
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

    // ── an apprentice in a duel ──────────────────────────────────────────────────────────────────────────────

    private static WandAtom Hit(int amount, int hits = 1) => new() { Kind = WandAtomKind.Damage, Amount = Const(amount), Hits = hits };

    private static WandAtom Guard(int amount) => new() { Kind = WandAtomKind.Block, Amount = Const(amount) };

    private static CardData Card(string id, WandImprint wand, int cost = 1) => new()
    {
        Id = id,
        NameKey = id,
        Costs = [new ResourceCost(StandardCombatIds.EnergyResource, cost)],
        Program = CombatProgramModel.Build<CardPlayContext>(
            new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        Wand = wand,
    };

    private static CardData Action(string id, WandAction action) => new()
    {
        Id = id,
        NameKey = id,
        IsAction = true,
        PlayCondition = new WandCanExpression<CardPlayContext>(action),
        Program = new EffectProgram<CardPlayContext>(new WandNode<CardPlayContext>(action)),
    };

    private static readonly string[] Actions = ["inscribe", "cast", "swap", "withdraw", "burn_out"];

    // The duel the master's worked examples are played in: a 200-HP dummy that nips for 1, a hero with a wand.
    internal static RunBlueprint Apprentice(IReadOnlyList<string> deck, IEnumerable<CardData>? extra = null,
        WandRules? rules = null, IEnumerable<StatusData>? statuses = null, IEnumerable<string>? heroStatuses = null)
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(1))),
        };
        var duel = new EncounterDefinition(new EncounterId("duel"),
            [new EncounterEnemy("dummy", 200, [new EnemyActionDefinitionId("nip")], DisplayName: "Dummy")],
            [new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3)]);
        CardData[] cards =
        [
            Card("candle_stub", new WandImprint
            {
                Operation = WandOperation.Speak,
                Atoms =
                [
                    Hit(4),
                    new WandAtom { Kind = WandAtomKind.Status, Amount = Const(1), Status = new StatusDefinitionId("singe"), Only = WandPosition.FirstExecuted },
                ],
            }),
            Card("chalk_line", new WandImprint { Operation = WandOperation.Speak, Atoms = [Guard(4)], GroundIfSpeakFollows = 1 }),
            Card("ink_blot", new WandImprint { Operation = WandOperation.Heighten, Percent = 50, Amount = 1 }),
            Card("flyleaf", new WandImprint { Operation = WandOperation.Echo, Percent = 50 }, cost: 0),
            .. Actions.Select((id, i) => Action(id, (WandAction)i)),
            .. extra ?? [],
        ];
        return new RunBlueprint(
            [.. deck.Select(id => new CardDefinitionId(id))],
            new Dictionary<string, EventScript>(),
            [duel],
            cards,
            [nip],
            new RunMap([new Node(new NodeId("duel"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("duel")))]))
        {
            Start = new RunStart
            {
                HeroName = "Apprentice",
                MaxHealth = 50,
                StartingHealth = 50,
                CombatActions = [.. Actions.Select(id => new CardDefinitionId(id))],
                CombatStatuses =
                [
                    new StartingStatusSpec(Bearer, 1),
                    .. (heroStatuses ?? []).Select(id => new StartingStatusSpec(new StatusDefinitionId(id), 1)),
                ],
            },
            Statuses =
            [
                new StatusData { Id = Bearer.value, NameKey = "Wand", UsesStacks = true },
                new StatusData { Id = "singe", NameKey = "Singe", UsesStacks = true, Polarity = StatusPolarity.Debuff, StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance },
                new StatusData { Id = "grit", NameKey = "Grit", UsesStacks = true, Polarity = StatusPolarity.Buff, StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance },
                new StatusData { Id = "flow", NameKey = "Flow", UsesStacks = true, Polarity = StatusPolarity.Buff, StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance },
                new StatusData { Id = "gust", NameKey = "Gust", UsesStacks = true, Polarity = StatusPolarity.Buff, StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance },
                .. statuses ?? [],
            ],
            Wand = rules ?? Rules with { Flow = new StatusDefinitionId("flow"), Gust = new StatusDefinitionId("gust") },
        };
    }

    internal static RunPlayback Start(RunBlueprint blueprint)
    {
        var play = new RunPlayback(() => { });
        play.Start(blueprint, seed: 1, interactive: true);
        Assert.Null(play.Error);
        while (play.Session!.IsAwaitingInterlude)
            play.Session.Continue();
        Assert.NotNull(play.CombatDriver?.Current);
        return play;
    }

    // Inscribe the first card in hand with this definition into this slot, answering both questions.
    internal static void Inscribe(RunPlayback play, string card, int slot)
    {
        var driver = play.CombatDriver!;
        driver.UseAction(new CardDefinitionId("inscribe"), null);
        Assert.Null(play.Session!.Error);
        var hand = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
        driver.SupplyCardChoice([hand.First(c => c.DefinitionId.value == card).Id]);
        if (driver.PendingOptionChoice is { } slots)
            driver.SupplyOptionChoice([slots.ToList().IndexOf($"Slot {slot + 1}")]);
        Assert.Null(play.Session!.Error);
        Assert.Equal(card, Wand(play).Slots[slot] is { } id
            ? play.CombatDriver!.Current!.State.GetCardZones(Hero(play)).GetCard(id).DefinitionId.value
            : null);
    }

    internal static WandState Wand(RunPlayback play) => play.CombatDriver!.Current!.State.GetWand(Hero(play))!;
    internal static CombatantId Hero(RunPlayback play) => play.CombatDriver!.Current!.HeroId;
    internal static CombatantId Dummy(RunPlayback play) =>
        play.CombatDriver!.Current!.State.Combatants.First(c => c.TeamId == StandardCombatIds.EnemyTeam).Id;
    internal static int DummyHp(RunPlayback play) =>
        play.CombatDriver!.Current!.State.GetCombatant(Dummy(play)).Health.Current;

    internal static void Cast(RunPlayback play)
    {
        play.CombatDriver!.UseAction(new CardDefinitionId("cast"), Dummy(play));
        Assert.Null(play.Session!.Error);
    }

    // Master §12.1, the first lesson: Ink Blot → Candle Stub → Flyleaf. The Heighten lifts the 4 to 6, the Echo
    // copies the heightened 6 at half (3): nine damage in two hits, and the Candle Stub is not first, so no Singe.
    // Exhaustion 1 + Echo + the copy = 3. The first inscription was free, the next two cost 1 Focus each.
    [Fact]
    public void Heighten_then_speak_then_echo_deals_the_heightened_hit_and_half_of_it_again()
    {
        using var play = Start(Apprentice(["ink_blot", "candle_stub", "flyleaf", "chalk_line", "chalk_line"]));
        var wand = Wand(play);
        Assert.Equal((4, 1, 0, 8), (wand.Capacity, wand.Focus, wand.Exhaustion, wand.ExhaustionCap));

        // Two plays for Focus (1 → 3), then three inscriptions (free, 1, 1).
        var driver = play.CombatDriver!;
        foreach (var chalk in driver.Current!.Hand.Where(c => c.DefinitionId.value == "chalk_line").ToList())
            driver.PlayCard(chalk.Id, Dummy(play));
        Assert.Equal(3, Wand(play).Focus);
        Inscribe(play, "ink_blot", 0);
        Inscribe(play, "candle_stub", 1);
        Inscribe(play, "flyleaf", 2);
        Assert.Equal(1, Wand(play).Focus);
        Assert.Equal(0, driver.Current!.State.GetCardPlayTurnStats(Hero(play)).CardsPlayedThisTurn - 2);

        // The plan before the cast says what will happen.
        var combat = driver.Current!;
        var plan = WandCompiler.Compile(combat.State, combat.Registry, Hero(play), Dummy(play));
        Assert.Equal(3, plan.FocusCost);
        Assert.False(plan.IsLegal); // 1 Focus is not enough
        Assert.Contains("Focus", plan.Illegal);
        Assert.False(combat.CanUse(new CardDefinitionId("cast"), Dummy(play)));

        // Next turn: Focus kept, two more plays, and now the cast.
        driver.EndTurn();
        foreach (var chalk in driver.Current!.Hand.Where(c => c.DefinitionId.value == "chalk_line").ToList())
            driver.PlayCard(chalk.Id, Dummy(play));
        Assert.Equal(3, Wand(play).Focus);
        combat = driver.Current!;
        plan = WandCompiler.Compile(combat.State, combat.Registry, Hero(play), Dummy(play));
        Assert.True(plan.IsLegal, plan.Illegal);
        Assert.Equal(3, plan.Exhaustion);

        var before = DummyHp(play);
        Cast(play);
        Assert.Equal(before - 9, DummyHp(play));
        Assert.Equal((0, 3), (Wand(play).Focus, Wand(play).Exhaustion));
        Assert.Equal(3, Wand(play).Occupied); // the cards stay in the wand
        Assert.DoesNotContain(play.CombatDriver!.Current!.State.Combatants.Single(c => c.Id == Dummy(play)).Statuses,
            s => s.DefinitionId.value == "singe");
    }
}
