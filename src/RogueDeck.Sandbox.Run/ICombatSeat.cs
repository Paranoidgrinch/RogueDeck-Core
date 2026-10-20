using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Run;

// WHERE A PLAYER SITS IN A FIGHT (multiplayer M5): the fight as that player sees it, the questions it is asking,
// and the moves that player can make in it. The solo driver is one seat (InteractiveCombatDriver); a party fight
// has one per member (PartyCombatSeat), each making its moves as that member — so a screen built against a seat
// draws and plays a solo fight and a party fight alike.
public interface ICombatSeat
{
    ICombatView? Current { get; }
    bool IsResolving { get; }

    IReadOnlyList<CardInstance>? PendingCardChoice { get; }
    int PendingCardChoiceCount { get; }
    string PendingCardChoicePurpose { get; }
    IReadOnlyList<string>? PendingOptionChoice { get; }
    int PendingOptionChoiceCount { get; }
    string PendingOptionChoicePurpose { get; }

    void PlayCard(CardInstanceId cardId, CombatantId? target);
    void UseAction(CardDefinitionId actionId, CombatantId? target);
    void EndTurn();
    void SupplyCardChoice(IReadOnlyList<CardInstanceId> picks);
    void SupplyOptionChoice(IReadOnlyList<int> picks);
}

// One member's seat at a party fight. Its moves are handed to `submit` as the answers they are — on one machine
// straight to the run (RunPlayback.Apply), at a table to the host — never played on the fight behind the run's
// back, so every machine sees the same moves in the same order.
public sealed class PartyCombatSeat(PartyInteractiveCombatDriver driver, CombatantId member, Action<ReplayEntry> submit)
    : ICombatSeat
{
    private PartyCombatSeatView? _view;

    public CombatantId Member { get; } = member;

    // Rebuilt only when the driver hands over a new fight object (every replay builds one).
    public ICombatView? Current
    {
        get
        {
            if (driver.Current is not { } fight)
                return _view = null;
            if (_view is null || !ReferenceEquals(_view.Combat, fight))
                _view = new PartyCombatSeatView(fight, Member);
            return _view;
        }
    }

    public bool IsResolving => driver.IsResolving;
    public IReadOnlyList<CardInstance>? PendingCardChoice => driver.PendingCardChoice;
    public int PendingCardChoiceCount => driver.PendingCardChoiceCount;
    public string PendingCardChoicePurpose => driver.PendingCardChoicePurpose;
    public IReadOnlyList<string>? PendingOptionChoice => driver.PendingOptionChoice;
    public int PendingOptionChoiceCount => driver.PendingOptionChoiceCount;
    public string PendingOptionChoicePurpose => driver.PendingOptionChoicePurpose;

    public void PlayCard(CardInstanceId cardId, CombatantId? target) => submit(new CombatPlayEntry(Member, cardId, target));

    public void UseAction(CardDefinitionId actionId, CombatantId? target) =>
        submit(new CombatActionEntry(Member, actionId, target));

    public void EndTurn() => submit(new CombatEndTurnEntry(Member));

    public void SupplyCardChoice(IReadOnlyList<CardInstanceId> picks) => submit(new CardPicksEntry(picks));

    public void SupplyOptionChoice(IReadOnlyList<int> picks) => submit(new OptionPicksEntry(picks));
}
