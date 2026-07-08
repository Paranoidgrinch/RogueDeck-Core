using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Scripting;

// Builds a fresh CombatState from a CompiledScenario the same way for both the (one-shot) ScenarioRunner
// and the (stepwise) InteractiveCombat: add the hero + enemies, deal the hero's deck into the draw pile,
// and apply starting statuses through the real effect pipeline.
internal static class ScenarioCombatFactory
{
    public static CombatState Build(CompiledScenario compiled, string combatId, int randomSeed)
    {
        var combat = new CombatState(new CombatId(combatId), randomSeed)
        {
            CellExclusive = compiled.CellExclusive,
            SimultaneousTeamTurns = compiled.SimultaneousTeamTurns,
        };

        AddCombatant(combat, compiled.Hero, StandardCombatIds.PlayerTeam);
        // Fielded player-board units act right after the hero; enemies follow (positional combat P5c).
        foreach (var ally in compiled.Allies)
            AddCombatant(combat, ally, StandardCombatIds.PlayerTeam);
        foreach (var enemy in compiled.Enemies)
            AddCombatant(combat, enemy, StandardCombatIds.EnemyTeam);

        combat.SetActiveCombatant(compiled.Hero.CombatantId);

        // Deal each player-team combatant's own deck into its draw pile; the per-combatant turn-start automation
        // draws it into that combatant's hand (party deckbuilding A1). The hero and any fielded ally/party member
        // each play from their own deck; a deckless combatant (auto-acting board unit) simply gets no cards.
        DealDeck(combat, compiled.Hero);
        foreach (var ally in compiled.Allies)
            DealDeck(combat, ally);

        // Apply starting statuses through the real pipeline so merge/stacking semantics are honoured.
        var queues = new CombatQueueProcessor();
        ApplyStartingStatuses(combat, compiled.Registry, queues, compiled.Hero);
        foreach (var ally in compiled.Allies)
            ApplyStartingStatuses(combat, compiled.Registry, queues, ally);
        foreach (var enemy in compiled.Enemies)
            ApplyStartingStatuses(combat, compiled.Registry, queues, enemy);

        // Install the hero's opening temporary rules (e.g. a consumable's "next combat starts with 20 block"): a
        // OneShot turnStarted program fires once at the hero's first turn start — after block's turn-start clear.
        InstallOpeningTemporaryRules(combat, compiled.Registry, queues, compiled.Hero);

        return combat;
    }

    // Deal a combatant's own deck into its draw pile (copy-by-copy). Deckless combatants add nothing.
    private static void DealDeck(CombatState combat, CombatantBlueprint blueprint)
    {
        if (blueprint.Deck.Count == 0)
            return;
        var zones = combat.GetCardZones(blueprint.CombatantId);
        foreach (var entry in blueprint.Deck)
            for (var copy = 0; copy < entry.Count; copy++)
                zones.AddCard(new CardInstance(
                    combat.CreateNextCardInstanceId(), entry.Card, blueprint.CombatantId, CardZone.DrawPile));
    }

    private static void AddCombatant(CombatState combat, CombatantBlueprint blueprint, TeamId team)
    {
        // A null CurrentHealth means "full"; the run layer passes a value to carry a wounded hero's HP in.
        var startingHealth = Math.Clamp(
            blueprint.CurrentHealth ?? blueprint.MaxHealth, 1, blueprint.MaxHealth);

        combat.AddCombatant(new CombatantState(
            blueprint.CombatantId,
            new CombatantDefinitionId(blueprint.Id),
            blueprint.NameKey,
            team,
            new HealthState(startingHealth, blueprint.MaxHealth)));

        var combatant = combat.GetCombatant(blueprint.CombatantId);
        foreach (var resource in blueprint.Resources)
            combatant.AddResource(resource.Resource, new ValuePoolState(resource.Current, max: resource.Max));

        // Optional grid placement — null leaves the combatant unplaced (flat arena, unchanged behavior).
        if (blueprint.Position is { } position)
            combatant.SetPosition(position);
    }

    private static void InstallOpeningTemporaryRules(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatQueueProcessor queues,
        HeroBlueprint hero)
    {
        if (hero.OpeningTemporaryRules.Count == 0)
            return;

        foreach (var spec in hero.OpeningTemporaryRules)
            combat.EnqueueEffect(new InstallTemporaryRuleEffectRequest(spec.Rule, spec.Lifetime));

        queues.ResolvePendingQueues(combat, registry);
    }

    private static void ApplyStartingStatuses(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatQueueProcessor queues,
        CombatantBlueprint blueprint)
    {
        if (blueprint.StartingStatuses.Count == 0)
            return;

        foreach (var status in blueprint.StartingStatuses)
            combat.EnqueueEffect(new ApplyStatusEffectRequest(
                blueprint.CombatantId,
                status.Status,
                Stacks: status.Stacks,
                DurationTurns: status.DurationTurns,
                Charges: status.Charges));

        queues.ResolvePendingQueues(combat, registry);
    }
}

// Collects every trace event into a list so a caller can slice [before..after] per step/action.
internal sealed class CollectingTraceListener : ICombatTraceListener
{
    public List<CombatTraceEvent> Events { get; } = new();

    public void OnTrace(CombatTraceEvent evt) => Events.Add(evt);
}
