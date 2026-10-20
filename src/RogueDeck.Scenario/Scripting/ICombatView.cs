using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Reporting;

namespace RogueDeck.Scenario.Scripting;

// ONE HERO'S VIEW OF A FIGHT (multiplayer M5): everything a screen reads to draw a fight from where its player
// sits — the hand, the energy, the moves that can be made, what each enemy means to do and to whom. A solo fight
// is that view of itself (InteractiveCombat); a party fight is seen through one member's seat at a time
// (PartyCombatSeatView), so the same screen draws both.
public interface ICombatView
{
    CombatState State { get; }
    CombatantId HeroId { get; }
    int Round { get; }
    int Turn { get; }
    CombatResult Result { get; }
    bool IsOver { get; }
    bool IsHeroTurn { get; }
    IReadOnlyList<ScenarioStepReport> Steps { get; }
    IReadOnlyList<CardInstance> Hand { get; }
    int HeroEnergy { get; }
    int HeroEnergyMax { get; }
    int HeroHealth { get; }
    int HeroGuard { get; }
    DisclosureSpec HeroDisclosure { get; }
    IReadOnlyList<CardInstance> RevealedDrawPile { get; }
    IReadOnlyList<CardDefinitionId> Actions { get; }

    // The fight's definitions — what a screen compiles this hero's wand against for its preview (WandCompiler).
    CombatDefinitionRegistry Registry { get; }

    ActionIntent? UpcomingIntentFor(CombatantId combatant);
    IReadOnlyList<ActionIntent> UpcomingIntentsFor(CombatantId combatant);
    bool CanPlay(CardInstanceId cardInstanceId);
    bool CanUse(CardDefinitionId actionId, CombatantId? target = null);
    IReadOnlyList<CalculatedResourceCost> ActionCost(CardDefinitionId actionId, CombatantId? target = null);
    Foresight? Foresee();

    // Whom this enemy means to hit — the hero, in a fight with one; whichever member it picked, in a party's.
    CombatantId? IntendedTarget(CombatantId enemy);
}
