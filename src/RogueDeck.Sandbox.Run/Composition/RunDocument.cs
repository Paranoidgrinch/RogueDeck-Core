using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// Shared accessor for the one Run authoring document (ProjectDraft.RunJson ↔ RunBlueprint). Each focused Studio
// tab — Relics, Events, Encounters, Hero, … — is a LENS over this single JSON document: Load parses it (seeding
// an empty starter on first use so a tab opened before the Run tab still has a document), Save writes it back.
// One JsonSerializerOptions and one seed live here, so the per-concern tabs stay thin and edit the same run.
public sealed class RunDocument(ProjectDraft draft)
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    public RunBlueprint Load()
    {
        draft.RunJson ??= RunJson.ToJson(Empty(), Options);
        return RunJson.FromJson<RunBlueprint>(draft.RunJson, Options);
    }

    public void Save(RunBlueprint blueprint) =>
        draft.RunJson = RunJson.ToJson(blueprint, Options);

    private static RunBlueprint Empty() => new(
        Array.Empty<CardDefinitionId>(),
        new Dictionary<string, EventScript>(),
        Array.Empty<EncounterDefinition>(),
        Array.Empty<CardData>(),
        Array.Empty<EnemyActionData>(),
        new RunMap(Array.Empty<Node>()));
}
