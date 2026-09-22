using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Composition;

// WHAT WAS CHOSEN, FROM WHAT. A recording holds only the answers ("n a1-7", "p 2"); what they MEAN — which doors
// were open, which three cards were offered, what was in hand — is the engine's to say, and it says it by
// replaying. This is the readable, trainable view of a recording: one row per answer, the situation before it
// and the answer in words. Never uploaded, always derived: a row is only as true as the replay that made it.
public sealed record RunDecision(
    int Index,
    int Act,
    int Room,
    string? Node,
    int Hp,
    int MaxHp,
    // door | choice | pick | interlude | play | end-turn | combat-pick | combat-option | consumable
    string Kind,
    string? Context,
    IReadOnlyList<string> Offered,
    string Chosen);

public static class RunDecisions
{
    // Replays the recording and describes every answer. The outcome says whether the replay held; rows after a
    // divergence are not produced.
    public static (RunReplayer.Outcome Outcome, IReadOnlyList<RunDecision> Decisions) Read(
        RunBlueprint blueprint, RunRecording recording)
    {
        var rows = new List<RunDecision>();
        var outcome = RunReplayer.Replay(blueprint, recording,
            beforeAnswer: (index, answer, play) => rows.Add(Describe(index, answer, play)));
        return (outcome, rows);
    }

    private static RunDecision Describe(int index, string[] answer, RunPlayback play)
    {
        var session = play.Session!;
        var run = session.Run;
        var combat = play.CombatDriver?.Current;
        string CardName(string instance)
        {
            var card = combat?.State.CardZonesByCombatant.Values
                .SelectMany(z => z.AllCards)
                .FirstOrDefault(c => c.Id.value == instance);
            return card is null ? instance
                : play.CardNames.GetValueOrDefault(card.DefinitionId.value) ?? card.DefinitionId.value;
        }
        string Nth(IReadOnlyList<string> offered, string at) =>
            int.TryParse(at, out var i) && i >= 0 && i < offered.Count ? offered[i] : at;

        RunDecision Row(string kind, string? context, IReadOnlyList<string> offered, string chosen) =>
            new(index, run.ActNumber, run.VisitedNodes.Count, run.CurrentNodeId?.Value,
                run.Health.Current, run.Health.Max, kind, context, offered, chosen);

        switch (answer[0])
        {
            case "n":
                return Row("door", null,
                    [.. session.PendingNodeChoices.Select(n => $"{n.Id.Value} ({string.Join(",", n.Tags)})")],
                    answer[1]);
            case "e":
                return Row("choice", session.PendingSituation?.Id,
                    [.. session.PendingChoices.Select(c => c.Id)], answer[1]);
            case "p":
                {
                    var request = session.PendingEntities;
                    var offered = request?.Displays ?? [];
                    var chosen = answer.Length == 1 ? "(none)" : string.Join(", ", answer.Skip(1).Select(a => Nth(offered, a)));
                    return Row("pick", request is null ? null : $"{request.Purpose} [{request.Intent}]", offered, chosen);
                }
            case "i":
                return Row("interlude", null, [], "continue");
            case "z" or "u":
                return Row("consumable", answer[0] == "u" ? "combat" : "between rooms", [], answer[1]);
            case "c":
                {
                    var hand = combat?.Hand.Select(c => CardName(c.Id.value)).ToArray() ?? [];
                    var target = answer.Length > 2 && answer[2].Length > 0 ? $" → {answer[2]}" : "";
                    return Row("play", combat is null ? null : $"round {combat.Round}, energy {combat.HeroEnergy}",
                        hand, CardName(answer[1]) + target);
                }
            case "t":
                return Row("end-turn", combat is null ? null : $"round {combat.Round}, energy {combat.HeroEnergy}",
                    combat?.Hand.Select(c => CardName(c.Id.value)).ToArray() ?? [], "end turn");
            case "k":
                {
                    var offered = play.CombatDriver?.PendingCardChoice?.Select(c => CardName(c.Id.value)).ToArray() ?? [];
                    return Row("combat-pick", play.CombatDriver?.PendingCardChoicePurpose, offered,
                        answer.Length == 1 ? "(none)" : string.Join(", ", answer.Skip(1).Select(CardName)));
                }
            case "o":
                {
                    var offered = play.CombatDriver?.PendingOptionChoice ?? [];
                    return Row("combat-option", play.CombatDriver?.PendingOptionChoicePurpose, offered,
                        answer.Length == 1 ? "(none)" : string.Join(", ", answer.Skip(1).Select(a => Nth(offered, a))));
                }
            default:
                return Row(answer[0], null, [], string.Join(" ", answer.Skip(1)));
        }
    }
}
