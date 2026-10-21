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

    private static CombatNodeModel Apply(string status, int stacks, string to) =>
        new("applyStatus", to, CombatAmountSpec.FromConst(stacks), StatusId: status);

    private static CardData Plain(string id, CombatNodeModel? program, int cost) => new()
    {
        Id = id,
        NameKey = id,
        Costs = cost == 0 ? [] : [new ResourceCost(StandardCombatIds.EnergyResource, cost)],
        Program = program is null ? null : CombatProgramModel.Build<CardPlayContext>(program),
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
        WandRules? rules = null, IEnumerable<StatusData>? statuses = null, IEnumerable<string>? heroStatuses = null,
        int bite = 1)
    {
        var nip = new EnemyActionData
        {
            Id = "nip",
            NameKey = "Nip",
            Intent = new ActionIntent("Nip", IntentKind.Attack),
            Program = CombatProgramModel.Build<EnemyActionContext>(
                new CombatNodeModel("dealDamage", "eventTarget", CombatAmountSpec.FromConst(bite))),
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
            // Play: nothing but the Focus every play gives. Free.
            Plain("spark", null, cost: 0),
            // Play: 2 Singe on the target / 2 Flow, 2 Grit, 1 Gust on yourself. Free.
            Plain("sear", Apply("singe", 2, "eventTarget"), cost: 0),
            Plain("well", Apply("flow", 2, "source"), cost: 0),
            Plain("stone", Apply("grit", 2, "source"), cost: 0),
            Plain("breeze", Apply("gust", 1, "source"), cost: 0),
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

    internal static void Play(RunPlayback play, string card)
    {
        var driver = play.CombatDriver!;
        var instance = driver.Current!.Hand.First(c => c.DefinitionId.value == card);
        driver.PlayCard(instance.Id, Dummy(play));
        Assert.Null(play.Session!.Error);
    }

    private static int Stacks(RunPlayback play, CombatantId who, string status) =>
        play.CombatDriver!.Current!.State.GetCombatant(who).Statuses.Where(s => s.DefinitionId.value == status).Sum(s => s.Stacks);

    private static int Block(RunPlayback play) =>
        play.CombatDriver!.Current!.State.GetCombatant(Hero(play)).DefensivePools
            .GetValueOrDefault(StandardCombatIds.BlockDefensivePool)?.Current ?? 0;

    // §15 A2/A3/A6: inscribing is not a play — no CardPlayed, no Focus — and the card leaves the hand for the wand,
    // not for the discard or the Exhaust pile. Only the FIRST inscription of the combat is free.
    [Fact]
    public void Inscribing_is_not_a_play_and_only_the_first_of_the_combat_is_free()
    {
        using var play = Start(Apprentice(["candle_stub", "chalk_line", "spark", "spark", "spark"]));
        Play(play, "spark");
        Assert.Equal(2, Wand(play).Focus);
        Inscribe(play, "candle_stub", 2);
        var combat = play.CombatDriver!.Current!;
        Assert.Equal(1, combat.State.GetCardPlayTurnStats(Hero(play)).CardsPlayedThisTurn);
        Assert.Equal(2, Wand(play).Focus);                       // free
        var zones = combat.State.GetCardZones(Hero(play));
        Assert.Single(zones.Wand);
        Assert.DoesNotContain(zones.DiscardPile, c => c.DefinitionId.value == "candle_stub");
        Assert.Empty(zones.ExhaustPile);

        play.CombatDriver!.EndTurn();
        Inscribe(play, "chalk_line", 0);
        Assert.Equal(1, Wand(play).Focus);                       // the second one, a turn later, costs 1
    }

    // §15 A5: Withdraw sends a card to the discard pile for 1 Focus; Burn Out exhausts it for free, once a turn, and
    // takes 3 off the Exhaustion — two different things.
    [Fact]
    public void Withdraw_discards_and_burn_out_exhausts_and_relieves()
    {
        using var play = Start(Apprentice(["candle_stub", "chalk_line", "spark", "spark", "spark"]));
        Play(play, "spark");
        Play(play, "spark");
        Play(play, "spark");
        Inscribe(play, "candle_stub", 0);
        Inscribe(play, "chalk_line", 1);
        Assert.Equal(3, Wand(play).Focus);

        // A cast to put Exhaustion on the wand: two instructions, 2 Focus, base 1 less the Chalk Line's Ground… which
        // only counts when a Speak FOLLOWS it — it is last here, so 1.
        Cast(play);
        Assert.Equal((1, 1), (Wand(play).Focus, Wand(play).Exhaustion));

        var driver = play.CombatDriver!;
        driver.UseAction(new CardDefinitionId("withdraw"), null);
        var installed = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
        driver.SupplyCardChoice([installed.First(c => c.DefinitionId.value == "chalk_line").Id]);
        var zones = driver.Current!.State.GetCardZones(Hero(play));
        Assert.Contains(zones.DiscardPile, c => c.DefinitionId.value == "chalk_line");
        Assert.Equal(0, Wand(play).Focus);

        driver.UseAction(new CardDefinitionId("burn_out"), Dummy(play));
        installed = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
        driver.SupplyCardChoice([installed.Single().Id]);
        zones = driver.Current!.State.GetCardZones(Hero(play));
        Assert.Contains(zones.ExhaustPile, c => c.DefinitionId.value == "candle_stub");
        Assert.Equal(0, Wand(play).Exhaustion);
        Assert.Equal(1, Wand(play).WandCardsExhausted);
        Assert.Equal(0, Wand(play).Occupied);
    }

    // §15 A7: Focus is kept from turn to turn; each turn start takes 3 Exhaustion off, never below zero.
    [Fact]
    public void Focus_is_kept_and_exhaustion_recovers_at_the_turn_start()
    {
        using var play = Start(Apprentice(["ink_blot", "candle_stub", "flyleaf", "spark", "spark"]));
        Play(play, "spark");
        Play(play, "spark");
        Inscribe(play, "ink_blot", 0);
        Inscribe(play, "candle_stub", 1);
        Inscribe(play, "flyleaf", 2);
        play.CombatDriver!.EndTurn();
        Play(play, "spark");
        Play(play, "spark");
        Assert.Equal(3, Wand(play).Focus);
        Cast(play);
        Assert.Equal(3, Wand(play).Exhaustion);
        play.CombatDriver!.EndTurn();
        Assert.Equal(0, Wand(play).Exhaustion);
        Assert.Equal(0, Wand(play).CastsThisTurn);
    }

    // §15 A8: a cast that may not happen takes nothing — no Focus, no stored spell, no turn's cast.
    [Fact]
    public void An_illegal_cast_takes_nothing()
    {
        using var play = Start(Apprentice(["candle_stub", "chalk_line", "spark", "spark", "spark"]));
        Inscribe(play, "candle_stub", 0);
        Inscribe(play, "chalk_line", 1);                        // 1 Focus → 0
        var before = DummyHp(play);
        Assert.False(play.CombatDriver!.Current!.CanUse(new CardDefinitionId("cast"), Dummy(play)));
        Cast(play);
        Assert.Equal(before, DummyHp(play));
        Assert.Equal((0, 0, 0), (Wand(play).Focus, Wand(play).CastsThisTurn, Wand(play).Exhaustion));
    }

    // §15 B11/B12: Singe is spent one stack per DIRECT hit and makes that hit 2 larger — a card's hit as much as the
    // wand's. Two hits with two Singe take both stacks and both bonuses.
    [Fact]
    public void Singe_is_spent_once_per_direct_hit_for_two_more_damage()
    {
        var twice = Card("twice", new WandImprint { Operation = WandOperation.Speak, Atoms = [Hit(3, hits: 2)] });
        using var play = Start(Apprentice(["sear", "sear", "chalk_line", "twice", "spark"], [twice]));
        Play(play, "sear");
        Assert.Equal(2, Stacks(play, Dummy(play), "singe"));
        var before = DummyHp(play);
        Play(play, "chalk_line");                                // 1 damage, +2
        Assert.Equal(before - 3, DummyHp(play));
        Assert.Equal(1, Stacks(play, Dummy(play), "singe"));

        Play(play, "sear");
        Play(play, "spark");
        Inscribe(play, "twice", 0);
        before = DummyHp(play);
        Cast(play);                                              // 3 + 2, 3 + 2 — and one stack left over
        Assert.Equal(before - 10, DummyHp(play));
        Assert.Equal(1, Stacks(play, Dummy(play), "singe"));
    }

    // §15 B14: Grit comes off an enemy's hit BEFORE Block — a hit the Block would have stopped anyway still spends it.
    [Fact]
    public void Grit_softens_an_attack_before_block()
    {
        using var play = Start(Apprentice(["stone", "spark", "spark", "spark", "spark"], bite: 5));
        Play(play, "stone");
        var hp = play.CombatDriver!.Current!.State.GetCombatant(Hero(play)).Health.Current;
        play.CombatDriver!.EndTurn();                            // the dummy nips for 5: 5 - 2 = 3
        Assert.Equal(hp - 3, play.CombatDriver!.Current!.State.GetCombatant(Hero(play)).Health.Current);
        Assert.Equal(1, Stacks(play, Hero(play), "grit"));
    }

    // §15 B13: the Flow there was when a cast began refunds its Focus afterwards (up to the cost); Flow gained
    // during the cast waits for a later one.
    [Fact]
    public void Flow_from_before_the_cast_refunds_its_focus()
    {
        var spring = Card("spring", new WandImprint
        {
            Operation = WandOperation.Speak,
            Atoms = [new WandAtom { Kind = WandAtomKind.Status, Amount = Const(3), Status = new StatusDefinitionId("flow"), Targeting = WandTargeting.Self, Copyable = false }],
        });
        using var play = Start(Apprentice(["well", "candle_stub", "spring", "spark", "spark"], [spring]));
        Play(play, "well");                                      // 2 Flow, Focus 2
        Play(play, "spark");                                     // Focus 3
        Inscribe(play, "candle_stub", 0);
        Inscribe(play, "spring", 1);                             // Focus 2
        Cast(play);                                              // costs 2, refunds 2 from the old Flow
        Assert.Equal(2, Wand(play).Focus);
        Assert.Equal(3, Stacks(play, Hero(play), "flow"));       // the cast's own 3 Flow stay
    }

    // §15 B15: Gust is resolved after the turn-start draw: draw X more, then choose X to discard.
    [Fact]
    public void Gust_draws_and_discards_after_the_turn_start_draw()
    {
        using var play = Start(Apprentice(["breeze", "spark", "spark", "spark", "spark", "chalk_line", "chalk_line"]));
        Play(play, "breeze");
        var driver = play.CombatDriver!;
        driver.EndTurn();
        var offered = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
        Assert.Equal(6, offered.Count);                          // five dealt and one for the Gust
        driver.SupplyCardChoice([offered[0].Id]);
        Assert.Null(play.Session!.Error);
        Assert.Equal(5, driver.Current!.Hand.Count);
        Assert.Equal(1, Wand(play).GustResolvedThisTurn);
        Assert.Equal(0, Stacks(play, Hero(play), "gust"));
    }

    // The whole wand state survives a save in the middle of a turn: what the fight comes back as is the fight it was.
    [Fact]
    public void A_fight_saved_with_a_loaded_wand_resumes_in_the_same_place()
    {
        var blueprint = Apprentice(["ink_blot", "candle_stub", "flyleaf", "spark", "spark"]);
        using var play = Start(blueprint);
        Play(play, "spark");
        Inscribe(play, "ink_blot", 0);
        Inscribe(play, "candle_stub", 2);
        var save = play.SaveJson()!;
        var expected = CombatStateHasher.ComputeHash(play.CombatDriver!.Current!.State.CreateSnapshot());

        using var resumed = new RunPlayback(() => { });
        resumed.Resume(blueprint, RunSaveJson.FromJson(save), interactive: true);
        Assert.Null(resumed.Error);
        var back = resumed.CombatDriver!.Current!;
        Assert.Equal(expected, CombatStateHasher.ComputeHash(back.State.CreateSnapshot()));
        Assert.Equal(2, back.State.GetWand(back.HeroId)!.Occupied);
    }

    // ── the grammar (§15 C, D) ────────────────────────────────────────────────────────────────────────────────

    private static WandPlan Plan(RunPlayback play, WandCompiler.Options? options = null)
    {
        var combat = play.CombatDriver!.Current!;
        return WandCompiler.Compile(combat.State, combat.Registry, Hero(play), Dummy(play), options);
    }

    // Load the wand with these cards in these slots (Focus enough for all of it), turn by turn as hands allow.
    private static RunPlayback Loaded(IReadOnlyList<(string Card, int Slot)> wand, IEnumerable<CardData>? extra = null,
        IEnumerable<StatusData>? statuses = null, IEnumerable<string>? heroStatuses = null, IReadOnlyList<string>? also = null)
    {
        var deck = wand.Select(w => w.Card).Concat(also ?? [])
            .Concat(Enumerable.Repeat("spark", Math.Max(0, 5 - wand.Count - (also?.Count ?? 0)))).ToList();
        var play = Start(Apprentice(deck, extra, statuses: statuses, heroStatuses: heroStatuses));
        foreach (var _ in Enumerable.Range(0, 2))
            if (play.CombatDriver!.Current!.Hand.Any(c => c.DefinitionId.value == "spark"))
                Play(play, "spark");
        foreach (var (card, slot) in wand)
        {
            while (Wand(play).Focus < 1 && Wand(play).FreeInscriptionUsed)
                Spark(play);
            Inscribe(play, card, slot);
        }
        while (Wand(play).Focus < 4)
            Spark(play);
        return play;
    }

    // One more Focus: a spark from the hand, or the next turn's.
    private static void Spark(RunPlayback play)
    {
        if (!play.CombatDriver!.Current!.Hand.Any(c => c.DefinitionId.value == "spark")
            || play.CombatDriver.Current.HeroEnergy == 0 && false)
            play.CombatDriver.EndTurn();
        if (play.CombatDriver!.Current!.Hand.Any(c => c.DefinitionId.value == "spark"))
            Play(play, "spark");
        else
            play.CombatDriver.EndTurn();
    }

    private static CardData Speak(string id, params WandAtom[] atoms) =>
        Card(id, new WandImprint { Operation = WandOperation.Speak, Atoms = atoms });

    // §15 C18: an Echo with nothing before it fizzles, and the plan says so before anything is paid.
    [Fact]
    public void An_echo_with_nothing_to_copy_fizzles_in_the_plan()
    {
        using var play = Loaded([("flyleaf", 0), ("candle_stub", 1)]);
        var plan = Plan(play);
        Assert.True(plan.Steps[0].Fizzled);
        Assert.Contains("copy", plan.Steps[0].FizzleReason);
        var before = DummyHp(play);
        Cast(play);
        Assert.Equal(before - 4, DummyHp(play));                  // only the Candle Stub — and it is not first, no Singe
        Assert.Equal(0, Stacks(play, Dummy(play), "singe"));
    }

    // §12.2: Candle Stub first gets its Singe; Ink Blot then heightens the Chalk Line, and the Flyleaf copies THAT.
    [Fact]
    public void Where_a_card_sits_decides_what_it_reaches()
    {
        using var play = Loaded([("candle_stub", 0), ("ink_blot", 1), ("chalk_line", 2), ("flyleaf", 3)]);
        var plan = Plan(play);
        Assert.Equal(2, plan.Steps[1].Target);                   // Ink Blot → Chalk Line
        Assert.Equal(2, plan.Steps[3].Source);                   // Flyleaf copies Chalk Line
        var before = DummyHp(play);
        var block = Block(play);
        Cast(play);
        Assert.Equal(before - 4, DummyHp(play));
        Assert.Equal(1, Stacks(play, Dummy(play), "singe"));
        Assert.Equal(block + 6 + 3, Block(play));
    }

    // §15 C19: Fork makes one hit two of 60 % (rounded up), each its own hit for Singe; Join folds the next two damage
    // Speaks into one hit of both plus its bonus.
    [Fact]
    public void Fork_splits_a_hit_and_join_folds_two_into_one()
    {
        var fork = Card("split_wick", new WandImprint { Operation = WandOperation.Shape, Rule = WandRule.Fork, Percent = 60, Amount = 2 });
        var join = Card("cracked_slate", new WandImprint { Operation = WandOperation.Shape, Rule = WandRule.Join, Amount = 3 });
        var jab = Speak("jab", Hit(5));
        using (var play = Loaded([("split_wick", 0), ("candle_stub", 1)], [fork, join, jab]))
        {
            var before = DummyHp(play);
            Cast(play);
            Assert.Equal(before - 6, DummyHp(play));              // 3 + 3 — and the Candle Stub is not first
        }
        using (var play = Loaded([("cracked_slate", 0), ("candle_stub", 1), ("jab", 2)], [fork, join, jab], also: ["sear"]))
        {
            Play(play, "sear");
            var before = DummyHp(play);
            Cast(play);
            Assert.Equal(before - (4 + 5 + 3 + 2), DummyHp(play)); // one hit: one Singe
            Assert.Equal(1, Stacks(play, Dummy(play), "singe"));
        }
    }

    // §15 C23: Soak turns a damage-only Speak into Block — the damage is never dealt, so no Singe is spent.
    [Fact]
    public void Soak_turns_the_hit_into_block_and_spends_no_singe()
    {
        var cup = Card("cup_of_water", new WandImprint { Operation = WandOperation.Shape, Rule = WandRule.Soak, Percent = 80 });
        var jab = Speak("jab", Hit(5));
        using var play = Loaded([("cup_of_water", 0), ("jab", 1)], [cup, jab], also: ["sear"]);
        Play(play, "sear");
        var before = DummyHp(play);
        var block = Block(play);
        Cast(play);
        Assert.Equal(before, DummyHp(play));
        Assert.Equal(block + 4, Block(play));
        Assert.Equal(2, Stacks(play, Dummy(play), "singe"));
    }

    // §15 C20/C21: Keep stores the next Speak instead of speaking it; the next successful cast lets it go first.
    [Fact]
    public void Keep_stores_a_speak_for_the_next_cast()
    {
        var leaf = Card("folded_leaf", new WandImprint { Operation = WandOperation.Keep, Percent = 100, Filter = WandFilter.Any });
        var jab = Speak("jab", Hit(5));
        using var play = Loaded([("folded_leaf", 0), ("jab", 1)], [leaf, jab]);
        var before = DummyHp(play);
        Cast(play);
        Assert.Equal(before, DummyHp(play));
        Assert.Equal(WandStoreTrigger.NextCast, Wand(play).StoredSpell!.Trigger);

        // Next cast: the stored 5 first, then the program again (which stores the jab once more).
        play.CombatDriver!.EndTurn();
        Play(play, "spark");
        Play(play, "spark");
        before = DummyHp(play);
        Cast(play);
        Assert.Equal(before - 5, DummyHp(play));
        Assert.NotNull(Wand(play).StoredSpell);
    }

    // §15 C24: a Stop ends the cast after the Speak before it; nothing after it runs or is paid for.
    [Fact]
    public void A_stop_ends_the_cast_and_what_follows_is_not_paid_for()
    {
        var whistle = Card("penny_whistle", new WandImprint { Operation = WandOperation.Stop, Amount = 2 });
        using var play = Loaded([("candle_stub", 0), ("penny_whistle", 1), ("flyleaf", 2), ("chalk_line", 3)], [whistle]);
        var plan = Plan(play);
        Assert.Equal(2, plan.Executed);
        Assert.Equal(2, plan.FocusCost);
        Assert.Equal([2, 3], plan.Skipped);
        Assert.Equal(0, plan.Exhaustion);                         // 1 − 2, never below zero
        var block = Block(play);
        Cast(play);
        Assert.Equal(block, Block(play));
    }

    // §15 D26: with Brass Dividers a run of slots is cast alone, priced alone, and draws nothing from outside it.
    [Fact]
    public void Brass_dividers_cast_a_section_on_its_own()
    {
        var dividers = new StatusData { Id = "brass_dividers", NameKey = "Brass Dividers", UsesStacks = true };
        using var play = Loaded([("ink_blot", 0), ("candle_stub", 1), ("chalk_line", 2), ("flyleaf", 3)],
            statuses: [dividers], heroStatuses: ["brass_dividers"]);
        var section = Plan(play, new WandCompiler.Options(0, 1));
        Assert.Equal(2, section.FocusCost);
        Assert.Equal(2, section.Executed);

        var driver = play.CombatDriver!;
        var before = DummyHp(play);
        driver.UseAction(new CardDefinitionId("cast"), Dummy(play));
        var options = Assert.IsAssignableFrom<IReadOnlyList<string>>(driver.PendingOptionChoice);
        driver.SupplyOptionChoice([options.ToList().IndexOf("Slots 1–2")]);
        Assert.Null(play.Session!.Error);
        Assert.Equal(before - 6, DummyHp(play));                  // heightened, and nothing copied it
        Assert.Contains(nameof(WandHook.BrassDividers), Wand(play).TurnLatches);
    }

    // §15 D28: A Perfect Copy rotates the cards one occupied place AFTER a full cast; the cast itself ran as it stood.
    [Fact]
    public void A_perfect_copy_rotates_after_the_cast()
    {
        var copy = Card("a_perfect_copy", new WandImprint { Operation = WandOperation.Order, Rule = WandRule.Rotate });
        using var play = Loaded([("ink_blot", 0), ("candle_stub", 1), ("flyleaf", 2), ("a_perfect_copy", 3)], [copy]);
        var before = DummyHp(play);
        Cast(play);
        Assert.Equal(before - 9, DummyHp(play));
        var zones = play.CombatDriver!.Current!.State.GetCardZones(Hero(play));
        Assert.Equal(["a_perfect_copy", "ink_blot", "candle_stub", "flyleaf"],
            Wand(play).Slots.Select(s => zones.GetCard(s!.Value).DefinitionId.value));
    }

    // §15 C25: a cast that would emit more effects than the cap is refused in the plan, not cut short.
    [Fact]
    public void A_cast_past_the_effect_cap_is_refused()
    {
        var flurry = Speak("flurry", Hit(1, hits: 30));
        using var play = Loaded([("flurry", 0)], [flurry]);
        var plan = Plan(play);
        Assert.False(plan.IsLegal);
        Assert.Contains("effects", plan.Illegal);
    }

    // A boost waiting for the next cast (Glass Rod) strengthens its damage and Block, strains it, and is spent by it.
    [Fact]
    public void A_next_cast_boost_is_spent_by_the_next_cast()
    {
        var primed = new StatusData { Id = "glass_rod_primed", NameKey = "Glass Rod", UsesStacks = true };
        var rod = Plain("glass_rod", Apply("glass_rod_primed", 1, "source"), cost: 0);
        var rules = Rules with { CastBoosts = [new WandCastBoost(new StatusDefinitionId("glass_rod_primed"), 50, 3)] };
        using var play = Start(Apprentice(["glass_rod", "candle_stub", "spark", "spark", "spark"], [rod], rules, [primed]));
        Play(play, "glass_rod");
        Play(play, "spark");
        Inscribe(play, "candle_stub", 0);
        Assert.Equal(4, Plan(play).Exhaustion);
        var before = DummyHp(play);
        Cast(play);
        Assert.Equal(before - 6, DummyHp(play));                  // 4 × 1.5
        Assert.Equal(4, Wand(play).Exhaustion);
        Assert.Equal(0, Stacks(play, Hero(play), "glass_rod_primed"));
    }

    // "Look at the top 3": a chosen card limited to the first cards of the draw pile is offered only those.
    [Fact]
    public void A_choice_from_the_top_of_the_draw_pile_offers_only_the_top()
    {
        var dish = Plain("ink_dish", new CombatNodeModel("moveCardToZone", "source",
            Card: new CombatCardSpec("chosen", CardZone.DrawPile, Purpose: "choose one", Top: 3), ToZone: CardZone.Hand), cost: 0);
        using var play = Start(Apprentice(["ink_dish", "spark", "spark", "spark", "spark", "chalk_line", "chalk_line", "chalk_line", "chalk_line", "chalk_line"], [dish]));
        var driver = play.CombatDriver!;
        driver.PlayCard(driver.Current!.Hand.First(c => c.DefinitionId.value == "ink_dish").Id, Dummy(play));
        var offered = Assert.IsAssignableFrom<IReadOnlyList<CardInstance>>(driver.PendingCardChoice);
        Assert.Equal(3, offered.Count);
        Assert.Equal(driver.Current!.State.GetCardZones(Hero(play)).DrawPile.Take(3).Select(c => c.Id), offered.Select(c => c.Id));
    }

    // A Temporary copy is made for one play: played, it goes to the Exhaust pile; withdrawn from a wand, too.
    [Fact]
    public void A_temporary_copy_exhausts_when_played_or_withdrawn()
    {
        var rubbing = Plain("rubbing", new CombatNodeModel("createCardCopy", "source", CombatAmountSpec.FromConst(1),
            Card: new CombatCardSpec("chosen", CardZone.DiscardPile, Purpose: "copy"), ToZone: CardZone.Hand, Temporary: true), cost: 0);
        using var play = Start(Apprentice(["rubbing", "rubbing", "candle_stub", "spark", "spark"], [rubbing]));
        Play(play, "candle_stub");                                // into the discard pile
        var driver = play.CombatDriver!;
        driver.PlayCard(driver.Current!.Hand.First(c => c.DefinitionId.value == "rubbing").Id, Dummy(play));
        if (driver.PendingCardChoice is { } discard)
            driver.SupplyCardChoice([discard.First(c => c.DefinitionId.value == "candle_stub").Id]);
        var copy = driver.Current!.Hand.Single(c => c.DefinitionId.value == "candle_stub");
        Assert.Contains(StandardCombatIds.TemporaryCopyMark, copy.Marks);
        Play(play, "candle_stub");
        var zones = driver.Current!.State.GetCardZones(Hero(play));
        Assert.Equal(CardZone.ExhaustPile, zones.GetCard(copy.Id).Zone);

        // Again, and this time into the wand and out of it.
        driver.PlayCard(driver.Current!.Hand.First(c => c.DefinitionId.value == "rubbing").Id, Dummy(play));
        if (driver.PendingCardChoice is { } again)
            driver.SupplyCardChoice([again.First(c => c.DefinitionId.value == "candle_stub").Id]);
        var second = driver.Current!.Hand.Single(c => c.DefinitionId.value == "candle_stub");
        Inscribe(play, "candle_stub", 0);
        driver.UseAction(new CardDefinitionId("withdraw"), null);
        if (driver.PendingCardChoice is { } installed)
            driver.SupplyCardChoice([installed.Single().Id]);
        Assert.Equal(CardZone.ExhaustPile, driver.Current!.State.GetCardZones(Hero(play)).GetCard(second.Id).Zone);
    }

    // Gust turn after turn, the draw pile running dry and the discard shuffled back in: every turn start asks once,
    // the answer is taken, and the fight moves on.
    [Fact]
    public void Gust_every_turn_through_a_reshuffle_asks_once_and_moves_on()
    {
        using var play = Start(Apprentice(["breeze", "breeze", "breeze", "spark", "spark", "spark", "chalk_line"]));
        var driver = play.CombatDriver!;
        for (var turn = 0; turn < 6; turn++)
        {
            foreach (var breeze in driver.Current!.Hand.Where(c => c.DefinitionId.value == "breeze").ToList())
                driver.PlayCard(breeze.Id, Dummy(play));
            driver.EndTurn();
            for (var guard = 0; guard < 4 && driver.PendingCardChoice is { } offered; guard++)
                driver.SupplyCardChoice([.. offered.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
            Assert.Null(driver.PendingCardChoice);
            Assert.Null(play.Session!.Error);
            Assert.True(driver.Current!.IsHeroTurn);
        }
    }

    // ── what a wand learns over a fight ──────────────────────────────────────────────────────────────────────

    // Rote adds to every damage hit a Speak deals, Poise to every Block one gives; a rule listening to WandCast hears
    // each settled cast; and the wand counts its casts and Speaks for the whole combat.
    [Fact]
    public void Rote_grows_the_speak_a_cast_trigger_feeds_it_and_the_combat_counts_casts()
    {
        var learns = CombatProgramModel.Build<WandCastTriggeredEffectContext>(Apply("rote", 1, "source"));
        var student = new StatusData
        {
            Id = "student",
            NameKey = "Student",
            UsesStacks = true,
            Triggers = [new StatusTriggerData("WandCast", System.Text.Json.JsonSerializer.SerializeToElement(
                learns, CombatJson.CreateOptions<WandCastTriggeredEffectContext>()))],
        };
        var rote = new StatusData
        {
            Id = "rote",
            NameKey = "Rote",
            UsesStacks = true,
            Polarity = StatusPolarity.Buff,
            StackingBehavior = StatusStackingBehavior.MergeWithExistingInstance,
        };
        using var play = Start(Apprentice(
            ["candle_stub", "candle_stub", "candle_stub", "candle_stub", "candle_stub"],
            rules: Rules with { Rote = new StatusDefinitionId("rote") },
            statuses: [student, rote], heroStatuses: ["student"]));
        var driver = play.CombatDriver!;

        Inscribe(play, "candle_stub", 0);
        driver.PlayCard(driver.Current!.Hand.First(c => c.DefinitionId.value == "candle_stub").Id, Dummy(play));
        var before = DummyHp(play);
        Cast(play);
        // The first cast: 4, and the Singe of the first slot (spent by nothing yet). Then the rule hears it.
        Assert.Equal(before - 4, DummyHp(play));
        Assert.Equal(1, FightStacks(play, "rote"));
        Assert.Equal((1, 1), (Wand(play).CastsThisCombat, Wand(play).SpeaksThisCombat));

        driver.EndTurn();
        foreach (var card in driver.Current!.Hand.Where(c => c.DefinitionId.value == "candle_stub").Take(2).ToList())
            driver.PlayCard(card.Id, Dummy(play));
        before = DummyHp(play);
        var singe = FightStacks(play, "singe", Dummy(play));
        Cast(play);
        // 4 + 1 Rote, and a Singe on the dummy makes the hit 2 larger still.
        Assert.Equal(before - (5 + (singe > 0 ? 2 : 0)), DummyHp(play));
        Assert.Equal(2, FightStacks(play, "rote"));
        Assert.Equal((2, 2), (Wand(play).CastsThisCombat, Wand(play).SpeaksThisCombat));
    }

    private static int FightStacks(RunPlayback play, string status, CombatantId? on = null) =>
        play.CombatDriver!.Current!.State.GetCombatant(on ?? Hero(play)).Statuses
            .Where(s => s.DefinitionId.value == status).Sum(s => s.Stacks);
}
