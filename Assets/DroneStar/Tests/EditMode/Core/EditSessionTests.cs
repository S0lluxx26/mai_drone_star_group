using System;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class EditSessionTests
    {
        [Test]
        public void UndoAndRedoRestoreEachStep()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            int changes = 0;
            session.Changed += () => changes++;
            Assert.That(session.IsDirty, Is.False);

            session.Edit("Rename", d => d.Title = "First");
            session.Edit("Drones", d => d.DroneCount = 222);
            Assert.That(session.IsDirty, Is.True);
            Assert.That(session.UndoLabel, Is.EqualTo("Drones"));

            Assert.That(session.Undo(), Is.True);
            Assert.That(session.Document.DroneCount, Is.EqualTo(150));
            Assert.That(session.Document.Title, Is.EqualTo("First"));
            Assert.That(session.RedoLabel, Is.EqualTo("Drones"));

            Assert.That(session.Undo(), Is.True);
            Assert.That(session.Document.Title, Is.EqualTo("Untitled Show"));
            Assert.That(session.IsDirty, Is.False, "undoing back to the saved state is clean");
            Assert.That(session.Undo(), Is.False);

            Assert.That(session.Redo(), Is.True);
            Assert.That(session.Redo(), Is.True);
            Assert.That(session.Document.DroneCount, Is.EqualTo(222));
            Assert.That(session.Redo(), Is.False);
            Assert.That(changes, Is.EqualTo(6));
        }

        [Test]
        public void SliderDragsMergeIntoOneStep()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            for (int i = 0; i < 10; i++) session.Edit("Size", d => d.Cues[0].Formation.Size = 20f + i, "cue0.size");
            session.EndMerge();
            session.Edit("Size", d => d.Cues[0].Formation.Size = 50f, "cue0.size");
            Assert.That(session.Undo(), Is.True);
            Assert.That(session.Document.Cues[0].Formation.Size, Is.EqualTo(29f));
            Assert.That(session.Undo(), Is.True);
            Assert.That(session.Document.Cues[0].Formation.Size, Is.EqualTo(34f));
            Assert.That(session.CanUndo, Is.False);
        }

        [Test]
        public void NoOpEditsAreIgnoredAndNewEditsClearRedo()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            Assert.That(session.Edit("Nothing", d => { }), Is.False);
            Assert.That(session.CanUndo, Is.False);
            session.Edit("A", d => d.Title = "A");
            session.Undo();
            Assert.That(session.CanRedo, Is.True);
            session.Edit("B", d => d.Title = "B");
            Assert.That(session.CanRedo, Is.False);
        }

        [Test]
        public void EditsAreSanitised()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            session.Edit("Bad", d => d.Cues[0].Formation.Size = -10f);
            Assert.That(session.Document.Cues[0].Formation.Size, Is.EqualTo(ShowBounds.MinSize));
        }

        [Test]
        public void FailedEditRollsBack()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            Assert.Throws<InvalidOperationException>(() => session.Edit("Boom", d =>
            {
                d.Title = "Half";
                throw new InvalidOperationException();
            }));
            Assert.That(session.Document.Title, Is.EqualTo("Untitled Show"));
            Assert.That(session.CanUndo, Is.False);
        }

        [Test]
        public void HistoryIsBounded()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            for (int i = 0; i < ShowEditSession.MaxUndoSteps + 30; i++) session.Edit("T", d => d.Title = "T" + i);
            int undone = 0;
            while (session.Undo()) undone++;
            Assert.That(undone, Is.EqualTo(ShowEditSession.MaxUndoSteps));
        }

        [Test]
        public void LoadingClearsHistoryAndMarksSaved()
        {
            var session = new ShowEditSession(DemoShows.Blank());
            session.Edit("A", d => d.Title = "A");
            session.Load(DemoShows.HeartAndRings(), markSaved: true);
            Assert.That(session.CanUndo, Is.False);
            Assert.That(session.IsDirty, Is.False);
            Assert.That(session.Document.Title, Is.EqualTo("Heart & Rings"));
            session.Load(DemoShows.Blank(), markSaved: false);
            Assert.That(session.IsDirty, Is.True);
        }
    }
}
