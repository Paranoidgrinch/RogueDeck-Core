using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Reporting;

namespace RogueDeck.Scenario.Scripting;

// A PARTY FIGHT SEEN FROM ONE SEAT (multiplayer M5): the member at that seat is "the hero" — its hand, its
// energy, its moves, its sight — and everything else in the fight is the fight. What a screen built for one hero
// reads, it reads here unchanged; the other heroes are simply the rest of the player team.
//
// It holds nothing of its own: every answer is read off the live PartyCombat as it stands, so a view taken
// before an answer and read after it tells the truth about after.
public sealed class PartyCombatSeatView(PartyCombat combat, CombatantId member) : ICombatView
{
    public PartyCombat Combat { get; } = combat ?? throw new ArgumentNullException(nameof(combat));

    public CombatState State => Combat.State;
    public CombatDefinitionRegistry Registry => Combat.Registry;
    public CombatantId HeroId { get; } = member;
    public int Round => Combat.Round;
    public int Turn => Combat.State.CurrentTurn;
    public CombatResult Result => Combat.Result;
    public bool IsOver => Combat.IsOver;

    // "My turn" in a party fight: the phase is the players' and this member has not ended it.
    public bool IsHeroTurn => !IsOver && Combat.ActiveMembers().Contains(HeroId);

    // Every hero's moves and every enemy's, in the order they happened — the whole table's, not only this seat's.
    public IReadOnlyList<ScenarioStepReport> Steps => Combat.Steps;

    public IReadOnlyList<CardInstance> Hand => Combat.HandOf(HeroId);
    public int HeroEnergy => Combat.EnergyOf(HeroId);

    public int HeroEnergyMax =>
        State.TryGetCombatant(HeroId, out var hero) && hero is not null
        && hero.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var pool)
            ? pool.Max ?? pool.Current
            : 0;

    public int HeroHealth => State.TryGetCombatant(HeroId, out var hero) && hero is not null ? hero.Health.Current : 0;

    public int HeroGuard =>
        State.TryGetCombatant(HeroId, out var hero) && hero is not null
        && hero.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
            ? pool.Current
            : 0;

    public DisclosureSpec HeroDisclosure => DisclosureSpec.For(State, Combat.Compiled.Registry, HeroId);

    public IReadOnlyList<CardInstance> RevealedDrawPile
    {
        get
        {
            var reach = HeroDisclosure.DrawPileCards;
            return reach <= 0 ? [] : State.GetCardZones(HeroId).DrawPile.Reverse().Take(reach).ToList();
        }
    }

    public IReadOnlyList<CardDefinitionId> Actions => Combat.ActionsOf(HeroId);

    public ActionIntent? UpcomingIntentFor(CombatantId combatant) => Combat.UpcomingIntentFor(combatant);

    public IReadOnlyList<ActionIntent> UpcomingIntentsFor(CombatantId combatant)
    {
        var depth = 1 + Math.Max(0, HeroDisclosure.IntentLookahead);
        var intents = new List<ActionIntent>(depth);
        for (var ahead = 0; ahead < depth; ahead++)
            if (Combat.UpcomingIntentFor(combatant, ahead) is { } intent)
                intents.Add(intent);
        return intents;
    }

    public bool CanPlay(CardInstanceId cardInstanceId)
    {
        if (!IsHeroTurn)
            return false;
        var zones = State.GetCardZones(HeroId);
        if (!zones.ContainsCard(cardInstanceId))
            return false;
        var card = zones.GetCard(cardInstanceId);
        var registry = Combat.Compiled.Registry;
        return card.Zone == CardZone.Hand
            && registry.CardDefinitions.TryGetValue(card.DefinitionId, out var definition)
            && CombatCardPlayProcessor.IsCardPlayAllowed(
                State, registry, definition, State.GetCombatant(HeroId), requestedTargetId: null, cardInstanceId);
    }

    public bool CanUse(CardDefinitionId actionId, CombatantId? target = null) => Combat.CanUse(HeroId, actionId, target);

    public IReadOnlyList<CalculatedResourceCost> ActionCost(CardDefinitionId actionId, CombatantId? target = null) =>
        Combat.ActionCost(HeroId, actionId, target);

    public Foresight? Foresee() => IsHeroTurn ? Combat.Foresee(HeroId) : null;

    public CombatantId? IntendedTarget(CombatantId enemy) => Combat.IntendedTarget(enemy);
}
