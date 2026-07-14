using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The working document's undo history + the host's disk autosave (DraftAutosave). Both guard against the two ways
// authoring work used to be lost: an accidental destructive edit, and a page reload (new circuit, fresh draft).
public class ProjectDraftTests
{
    [Fact]
    public void Undo_steps_back_through_distinct_writes()
    {
        var draft = new ProjectDraft();
        draft.RunJson = "v1";
        draft.RunJson = "v2";
        draft.RunJson = "v2"; // identical write: no snapshot
        draft.RunJson = "v3";

        Assert.True(draft.CanUndo);
        Assert.True(draft.Undo());
        Assert.Equal("v2", draft.RunJson);
        Assert.True(draft.Undo());
        Assert.Equal("v1", draft.RunJson);
        Assert.True(draft.Undo());
        Assert.Null(draft.RunJson); // back to the pre-first-write state
        Assert.False(draft.Undo());
    }

    [Fact]
    public void History_is_capped()
    {
        var draft = new ProjectDraft();
        for (var i = 0; i <= ProjectDraft.MaxHistory + 5; i++)
            draft.RunJson = $"v{i}";
        Assert.Equal(ProjectDraft.MaxHistory, draft.UndoDepth);
    }

    [Fact]
    public void Changed_fires_on_writes_and_undo_but_not_identical_writes()
    {
        var draft = new ProjectDraft();
        var fired = 0;
        draft.Changed += () => fired++;

        draft.RunJson = "v1";
        draft.RunJson = "v1";
        draft.Undo();

        Assert.Equal(2, fired);
    }

    [Fact]
    public void Autosave_round_trips_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rd-draft-{Guid.NewGuid():N}.json");
        try
        {
            var draft = DraftAutosave.CreateDraft(path);
            Assert.Null(draft.RunJson); // no autosave yet

            draft.RunJson = """{"Deck":[]}""";
            Assert.Equal("""{"Deck":[]}""", File.ReadAllText(path));

            var restored = DraftAutosave.CreateDraft(path);
            Assert.Equal(draft.RunJson, restored.RunJson);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
