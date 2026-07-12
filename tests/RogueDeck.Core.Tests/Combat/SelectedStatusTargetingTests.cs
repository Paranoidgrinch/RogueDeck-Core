using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// #3 non-combatant target domains: an effect can now point at a single status INSTANCE on a combatant (not just
// a status definition or a whole polarity). RemoveSelectedStatusNode + StatusSelectionSpec express "remove a
// random buff", "remove the enemy's first debuff", etc. — the status-domain analog of card-instance targeting.
public class SelectedStatusTargetingTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly StatusDefinitionId StrengthId = new("test.buff_strength");
    private static readonly StatusDefinitionId DexterityId = new("test.buff_dexterity");
    private static readonly StatusDefinitionId PoisonId = new("test.debuff_poison");
    private static readonly StatusDefinitionId BurnId = new("test.debuff_burn");

    private sealed record Ctx;

    [Fact]
    public void First_pick_removes_the_selected_polarity_and_keeps_the_rest()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, StrengthId, 2);
        ApplyStatus(combat, registry, GoblinId, PoisonId, 3);

        RunRemoveSelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Debuff));

        var remaining = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(StrengthId, remaining.DefinitionId); // the buff survived; the debuff was removed
    }

    [Fact]
    public void Removing_a_random_buff_removes_exactly_one_buff_and_no_debuff()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, StrengthId, 2);
        ApplyStatus(combat, registry, GoblinId, DexterityId, 2);
        ApplyStatus(combat, registry, GoblinId, PoisonId, 3);

        RunRemoveSelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Buff, StatusPick.Random));

        var statuses = combat.GetCombatant(GoblinId).Statuses;
        Assert.Equal(2, statuses.Count); // exactly one removed
        Assert.Equal(1, statuses.Count(s => s.Polarity == StatusPolarity.Buff)); // one buff gone
        Assert.Contains(statuses, s => s.DefinitionId == PoisonId); // the debuff is untouched
    }

    [Fact]
    public void Index_selects_the_nth_matching_status()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, 3); // debuff #0
        ApplyStatus(combat, registry, GoblinId, BurnId, 1);   // debuff #1

        RunRemoveSelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Debuff, Index: 1));

        var remaining = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(PoisonId, remaining.DefinitionId); // the 2nd debuff (Burn) was removed
    }

    [Fact]
    public void No_matching_status_is_a_no_op()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, 3); // only a debuff present

        RunRemoveSelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Buff));

        Assert.Single(combat.GetCombatant(GoblinId).Statuses); // nothing removed
    }

    [Fact]
    public void The_instance_request_removes_only_the_addressed_instance()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, StrengthId, 2);
        ApplyStatus(combat, registry, GoblinId, PoisonId, 3);

        var poison = combat.GetCombatant(GoblinId).Statuses.Single(s => s.DefinitionId == PoisonId);
        combat.EnqueueEffect(new RemoveStatusInstanceEffectRequest(GoblinId, poison.Id));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var remaining = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(StrengthId, remaining.DefinitionId);
    }

    [Fact]
    public void Standard_package_registers_the_instance_removal_handler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        Assert.IsType<RemoveStatusInstanceEffectHandler>(
            registry.GetEffectRequestHandler(typeof(RemoveStatusInstanceEffectRequest)));
    }

    [Fact]
    public void Reducing_a_selected_debuff_lowers_its_stacks_and_leaves_it_present()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, 5);

        RunModifySelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Debuff), delta: -2);

        var poison = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(3, poison.Stacks); // 5 − 2
    }

    [Fact]
    public void Reducing_a_selected_debuff_to_zero_removes_it()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, PoisonId, 2);

        RunModifySelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Debuff), delta: -5);

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses); // clamped to 0 → removed
    }

    [Fact]
    public void Boosting_a_selected_buff_raises_its_stacks()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, StrengthId, 1);

        RunModifySelected(combat, registry, new StatusSelectionSpec(StatusPolarityFilter.Buff), delta: 2);

        var strength = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(3, strength.Stacks); // 1 + 2
    }

    [Fact]
    public void Stealing_a_selected_buff_moves_it_from_the_enemy_to_the_hero_with_stacks_intact()
    {
        var registry = CreateRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        ApplyStatus(combat, registry, GoblinId, StrengthId, 3); // the enemy's Strength
        ApplyStatus(combat, registry, GoblinId, PoisonId, 2);   // a debuff that must stay put

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new StealSelectedStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget, new StatusSelectionSpec(StatusPolarityFilter.Buff),
            CombatantTargetSelectors.Source));
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The goblin keeps only its debuff; the hero now carries the stolen Strength at full stacks.
        var goblinStatus = Assert.Single(combat.GetCombatant(GoblinId).Statuses);
        Assert.Equal(PoisonId, goblinStatus.DefinitionId);
        var stolen = Assert.Single(combat.GetCombatant(HeroId).Statuses);
        Assert.Equal(StrengthId, stolen.DefinitionId);
        Assert.Equal(3, stolen.Stacks);
        Assert.Equal(HeroId, stolen.OwnerCombatantId);
    }

    private static void RunModifySelected(
        CombatState combat, CombatDefinitionRegistry registry, StatusSelectionSpec spec, int delta)
    {
        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new ModifySelectedStatusStacksNode<Ctx>(
            CombatantTargetSelectors.EventTarget, spec, new ConstantExpression<Ctx>(delta)));
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void RunRemoveSelected(
        CombatState combat, CombatDefinitionRegistry registry, StatusSelectionSpec spec)
    {
        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new RemoveSelectedStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget, spec));
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static CombatDefinitionRegistry CreateRegistry()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        RegisterStatus(builder, StrengthId, StatusPolarity.Buff);
        RegisterStatus(builder, DexterityId, StatusPolarity.Buff);
        RegisterStatus(builder, PoisonId, StatusPolarity.Debuff);
        RegisterStatus(builder, BurnId, StatusPolarity.Debuff);
        return builder.Build();
    }

    private static void RegisterStatus(
        CombatDefinitionRegistryBuilder builder, StatusDefinitionId id, StatusPolarity polarity) =>
        builder.RegisterStatus(new StatusDefinition(
            id,
            new PackageId("test"),
            displayNameKey: $"status.{id}.name",
            descriptionKey: $"status.{id}.desc",
            polarity: polarity,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

    private static void ApplyStatus(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId targetId, StatusDefinitionId statusId, int stacks)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId, StatusDefinitionId: statusId, Stacks: stacks));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
}
