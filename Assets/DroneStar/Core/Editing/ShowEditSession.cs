using System;
using System.Collections.Generic;

namespace DroneStar.Core
{
    /// <summary>
    /// Owns the document being edited: snapshot-based undo/redo, merging of continuous edits (slider
    /// drags), dirty tracking against the last save, and a change event for the UI.
    /// </summary>
    public sealed class ShowEditSession
    {
        public const int MaxUndoSteps = 200;

        struct Entry
        {
            public string Label;
            public string Snapshot;
        }

        readonly List<Entry> undo = new List<Entry>();
        readonly List<Entry> redo = new List<Entry>();
        string savedSnapshot;
        string currentSnapshot;
        string activeMergeKey;

        public ShowEditSession(ShowDocument document)
        {
            Load(document ?? throw new ArgumentNullException(nameof(document)), markSaved: true);
        }

        public ShowDocument Document { get; private set; }

        /// <summary>Increments on every change (edit, undo, redo, load).</summary>
        public int Revision { get; private set; }

        public event Action Changed;

        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;
        public string UndoLabel => CanUndo ? undo[undo.Count - 1].Label : null;
        public string RedoLabel => CanRedo ? redo[redo.Count - 1].Label : null;
        public bool IsDirty => currentSnapshot != savedSnapshot;

        /// <summary>
        /// Applies <paramref name="mutate"/> as one undoable step. Consecutive edits that pass the same
        /// <paramref name="mergeKey"/> collapse into a single step until <see cref="EndMerge"/> is called.
        /// </summary>
        public bool Edit(string label, Action<ShowDocument> mutate, string mergeKey = null)
        {
            if (mutate == null) throw new ArgumentNullException(nameof(mutate));
            string before = currentSnapshot;
            try
            {
                mutate(Document);
                ShowSanitizer.Sanitize(Document);
            }
            catch
            {
                // Never leave a half-applied edit behind.
                Document = ShowSerializer.FromJson(before);
                throw;
            }
            string after = ShowSerializer.ToJson(Document, pretty: false);
            if (after == before) return false;

            bool merge = mergeKey != null && mergeKey == activeMergeKey && undo.Count > 0;
            if (!merge)
            {
                undo.Add(new Entry { Label = label, Snapshot = before });
                if (undo.Count > MaxUndoSteps) undo.RemoveAt(0);
            }
            activeMergeKey = mergeKey;
            redo.Clear();
            currentSnapshot = after;
            Revision++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Closes the current merge group (call when a drag ends).</summary>
        public void EndMerge() => activeMergeKey = null;

        public bool Undo()
        {
            if (!CanUndo) return false;
            Entry e = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            redo.Add(new Entry { Label = e.Label, Snapshot = currentSnapshot });
            Restore(e.Snapshot);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo) return false;
            Entry e = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add(new Entry { Label = e.Label, Snapshot = currentSnapshot });
            Restore(e.Snapshot);
            return true;
        }

        /// <summary>Replaces the document (new/open) and clears the history.</summary>
        public void Load(ShowDocument document, bool markSaved)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            ShowDocument copy = document.Clone();
            ShowSanitizer.Sanitize(copy);
            Document = copy;
            currentSnapshot = ShowSerializer.ToJson(copy, pretty: false);
            if (markSaved) savedSnapshot = currentSnapshot;
            else savedSnapshot = null;
            undo.Clear();
            redo.Clear();
            activeMergeKey = null;
            Revision++;
            Changed?.Invoke();
        }

        public void MarkSaved()
        {
            savedSnapshot = currentSnapshot;
            activeMergeKey = null;
        }

        public string ToJson() => ShowSerializer.ToJson(Document);

        void Restore(string snapshot)
        {
            Document = ShowSerializer.FromJson(snapshot);
            currentSnapshot = snapshot;
            activeMergeKey = null;
            Revision++;
            Changed?.Invoke();
        }
    }
}
