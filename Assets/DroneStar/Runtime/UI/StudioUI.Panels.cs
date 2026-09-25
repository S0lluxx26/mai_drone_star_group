using System;
using System.Collections.Generic;
using System.Globalization;
using DroneStar.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DroneStar.App
{
    public sealed partial class StudioUI
    {
        TextField titleField;
        Label dirtyMark;
        Button undoButton;
        Button redoButton;
        Button soundButton;
        IconElement soundIcon;

        ScrollView cueScroll;
        Label cueCount;

        VisualElement strip;
        VisualElement stripSegments;
        VisualElement stripMarkers;
        VisualElement playhead;
        Label timeLabel;
        Label nowLabel;
        IconElement playIcon;
        bool scrubbing;

        // ------------------------------------------------------------------ top bar

        VisualElement BuildTopBar()
        {
            var bar = Ui.El("ds-topbar");

            var brand = Ui.El("ds-brand");
            var mark = new IconElement(Icon.Star);
            mark.AddToClassList("ds-brand-mark");
            mark.style.color = new Color(1f, 0.76f, 0.1f);
            brand.Add(mark);
            var words = new VisualElement();
            words.Add(Ui.Text("Drone Star Studio", "ds-brand-title"));
            words.Add(Ui.Text("MAI DRONE STAR GROUP", "ds-brand-sub"));
            brand.Add(words);
            bar.Add(brand);

            titleField = new TextField { maxLength = 64 };
            titleField.AddToClassList("ds-title-field");
            titleField.tooltip = "Show title";
            titleField.RegisterCallback<FocusOutEvent>(_ => CommitTitle());
            titleField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) CommitTitle();
            });
            bar.Add(titleField);
            dirtyMark = Ui.Text("", "ds-dirty");
            dirtyMark.tooltip = "Unsaved changes";
            bar.Add(dirtyMark);

            bar.Add(Ui.Button("New", Icon.Plus, ShowTemplates, null, "Start from a template"));
            bar.Add(Ui.Button("Open", Icon.Folder, ShowOpen, null, "Open a saved or imported show"));
            bar.Add(Ui.Button("Save", Icon.Save, () => app.SaveToLibrary(), null, "Save on this device (Ctrl+S)"));
            bar.Add(Ui.Button("Export", Icon.Download, ShowExport, null, "Download show file, trajectories or flight report"));
            bar.Add(Ui.El("ds-divider"));
            undoButton = Ui.Button(null, Icon.Undo, app.Undo);
            redoButton = Ui.Button(null, Icon.Redo, app.Redo);
            bar.Add(undoButton);
            bar.Add(redoButton);

            bar.Add(Ui.El("ds-spacer"));

            soundButton = Ui.Button(null, Icon.Sound, () =>
            {
                app.Score.SetMuted(!app.Score.Muted);
                RefreshTopBar();
            }, null, "Demo soundtrack on/off");
            soundIcon = soundButton.Q<IconElement>();
            bar.Add(soundButton);
            bar.Add(Ui.Button(null, Icon.Help, ShowHelp, null, "Shortcuts and about (H)"));
            bar.Add(Ui.El("ds-divider"));
            bar.Add(Ui.Button("Demo Run", Icon.Film, StartDemo, "ds-btn--primary", "Play the show as a cinematic presentation (D)"));
            return bar;
        }

        void CommitTitle()
        {
            string title = titleField.value;
            Edit("Rename show", d => d.Title = title);
            titleField.SetValueWithoutNotify(app.Session.Document.Title);
        }

        void RefreshTopBar()
        {
            if (titleField == null) return;
            if (!IsTyping) titleField.SetValueWithoutNotify(app.Session.Document.Title);
            dirtyMark.style.visibility = app.Session.IsDirty ? Visibility.Visible : Visibility.Hidden;
            undoButton.SetEnabled(app.Session.CanUndo);
            redoButton.SetEnabled(app.Session.CanRedo);
            undoButton.tooltip = app.Session.CanUndo ? "Undo " + app.Session.UndoLabel + " (Ctrl+Z)" : "Nothing to undo";
            redoButton.tooltip = app.Session.CanRedo ? "Redo " + app.Session.RedoLabel + " (Ctrl+Y)" : "Nothing to redo";
            if (soundIcon != null && app.Score != null) soundIcon.Icon = app.Score.Muted ? Icon.Mute : Icon.Sound;
        }

        // ------------------------------------------------------------------ cue list

        VisualElement BuildCuePanel()
        {
            var panel = Ui.El("ds-panel ds-left");
            var header = Ui.El("ds-row");
            header.Add(Ui.Text("SHOW CUES", "ds-panel-title"));
            header.Add(Ui.El("ds-spacer"));
            cueCount = Ui.Text("", "ds-panel-title");
            header.Add(cueCount);
            panel.Add(header);

            cueScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            cueScroll.AddToClassList("ds-cue-list");
            panel.Add(cueScroll);

            var addRow = Ui.El("ds-add-row");
            addRow.Add(Ui.Text("ADD FORMATION", "ds-panel-title"));
            var grid = Ui.El("ds-row");
            grid.style.flexWrap = Wrap.Wrap;
            foreach (FormationKind kind in Enum.GetValues(typeof(FormationKind)))
            {
                if (kind == FormationKind.Custom) continue;
                FormationKind k = kind;
                grid.Add(Ui.Button(kind.ToString(), null, () => app.AddCue(k), "ds-btn--small ds-add-kind", "Insert a " + kind + " cue after the selection"));
            }
            addRow.Add(grid);
            panel.Add(addRow);
            return panel;
        }

        string cueListKey;

        /// <summary>Everything a card shows; the list is rebuilt only when this changes (not on every slider tick).</summary>
        string CueListKey(List<Cue> cues, bool fresh)
        {
            var sb = new System.Text.StringBuilder(cues.Count * 48);
            sb.Append(app.SelectedCue).Append('|').Append(fresh).Append('|').Append(app.Report != null);
            for (int i = 0; i < cues.Count; i++)
            {
                Cue c = cues[i];
                sb.Append('|').Append(c.Name).Append(',').Append((int)c.Formation.Kind).Append(',')
                  .Append(c.Light.ColorA.ToHex()).Append(c.Light.ColorB.ToHex()).Append(',').Append(c.HoldSeconds);
                if (fresh && i < app.Show.CueTimings.Count)
                {
                    CueTiming t = app.Show.CueTimings[i];
                    sb.Append(',').Append(t.TransitSeconds).Append(',').Append(t.Formation.DarkCount);
                }
            }
            return sb.ToString();
        }

        void RebuildCueList()
        {
            if (cueScroll == null) return;
            List<Cue> cues = app.Session.Document.Cues;
            bool fresh = app.Show != null && !app.ShowIsStale;
            string key = CueListKey(cues, fresh);
            if (key == cueListKey) return;
            cueListKey = key;
            Vector2 scroll = cueScroll.scrollOffset;
            cueScroll.Clear();
            cueCount.text = cues.Count + " / " + ShowBounds.MaxCues;

            for (int i = 0; i < cues.Count; i++)
            {
                int index = i;
                Cue cue = cues[i];
                var card = Ui.El("ds-cue-card");
                card.EnableInClassList("ds-cue-card--selected", i == app.SelectedCue);
                card.RegisterCallback<ClickEvent>(_ => app.Select(index));

                var swatch = Ui.El("ds-cue-swatch");
                swatch.style.backgroundColor = cue.Light.ColorA.ToDisplay();
                swatch.style.borderBottomColor = cue.Light.ColorB.ToDisplay();
                swatch.style.borderBottomWidth = 14;
                card.Add(swatch);

                var body = Ui.El("ds-cue-body");
                body.Add(Ui.Text((i + 1).ToString("00", CultureInfo.InvariantCulture) + "  " + cue.Formation.Kind.ToString().ToUpperInvariant(), "ds-cue-index"));
                body.Add(Ui.Text(cue.Name, "ds-cue-name"));
                string meta;
                if (fresh && i < app.Show.CueTimings.Count)
                {
                    CueTiming t = app.Show.CueTimings[i];
                    meta = "in " + Ui.Seconds(t.TransitSeconds) + " · hold " + Ui.Seconds(t.HoldSeconds);
                    if (t.Formation.DarkCount > 0) meta += " · " + t.Formation.DarkCount + " parked";
                }
                else
                {
                    meta = "hold " + Ui.Seconds(cue.HoldSeconds);
                }
                var metaLabel = Ui.Text(meta, "ds-cue-meta");
                body.Add(metaLabel);
                if (app.Report != null && fresh && app.Report.Issues.Exists(x => x.CueIndex == i && x.Severity == IssueSeverity.Error))
                {
                    metaLabel.AddToClassList("ds-error");
                    metaLabel.text += " · safety issue";
                }
                card.Add(body);

                if (i == app.SelectedCue)
                {
                    var actions = Ui.El("ds-cue-actions");
                    var up = Ui.Button(null, Icon.Up, () => app.MoveCue(index, -1), "ds-btn--small", "Move up");
                    var down = Ui.Button(null, Icon.Down, () => app.MoveCue(index, 1), "ds-btn--small", "Move down");
                    up.SetEnabled(i > 0);
                    down.SetEnabled(i < cues.Count - 1);
                    actions.Add(up);
                    actions.Add(down);
                    actions.Add(Ui.Button(null, Icon.Copy, () => app.DuplicateCue(index), "ds-btn--small", "Duplicate"));
                    actions.Add(Ui.Button(null, Icon.Trash, () => app.DeleteCue(index), "ds-btn--small", "Delete (Del)"));
                    card.Add(actions);
                }
                cueScroll.Add(card);
            }
            if (cues.Count == 0) cueScroll.Add(Ui.Text("No cues yet. Add a formation below.", "ds-empty"));
            cueScroll.schedule.Execute(() => cueScroll.scrollOffset = scroll);
        }

        // ------------------------------------------------------------------ timeline

        VisualElement BuildTimeline()
        {
            var panel = Ui.El("ds-panel ds-timeline");
            var transport = Ui.El("ds-transport");

            transport.Add(Ui.Button(null, Icon.SkipBack, () => app.Seek(0f), null, "Back to start (Home)"));
            var play = Ui.Button(null, Icon.Play, app.TogglePlay, "ds-btn--accent", "Play / pause (Space)");
            playIcon = play.Q<IconElement>();
            transport.Add(play);
            transport.Add(Ui.Button(null, Icon.SkipForward, SkipToNextCue, null, "Next cue"));
            timeLabel = Ui.Text("00:00.0 / 00:00.0", "ds-time");
            transport.Add(timeLabel);
            nowLabel = Ui.Text("", "ds-now");
            transport.Add(nowLabel);
            transport.Add(Ui.El("ds-spacer"));

            var speedLabels = new List<string>();
            foreach (float s in PlaybackClock.Speeds) speedLabels.Add(s.ToString("0.##", CultureInfo.InvariantCulture) + "×");
            var speed = new Segmented(speedLabels, () => Array.IndexOf(PlaybackClock.Speeds, app.Clock.Speed), i =>
            {
                app.Clock.SetSpeed(PlaybackClock.Speeds[i]);
                RefreshAll();
            });
            speed.style.flexGrow = 0;
            speed.style.width = 190;
            bindings.Add(speed);
            transport.Add(speed);

            var loop = Ui.Button(null, Icon.Loop, () =>
            {
                app.Clock.Loop = !app.Clock.Loop;
                RefreshAll();
            }, null, "Loop playback");
            bindings.Add(new Binding(() => loop.EnableInClassList("ds-btn--toggled", app.Clock.Loop)));
            transport.Add(loop);

            transport.Add(Ui.El("ds-divider"));
            var cameraModes = new[] { CameraMode.Orbit, CameraMode.Audience, CameraMode.Aerial };
            var cam = new Segmented(new[] { "Orbit", "Audience", "Aerial" }, () => Array.IndexOf(cameraModes, app.CameraRig.Mode), i =>
            {
                app.CameraRig.SetMode(cameraModes[i]);
                RefreshAll();
            });
            cam.style.flexGrow = 0;
            cam.style.width = 210;
            bindings.Add(cam);
            transport.Add(cam);
            transport.Add(Ui.Button(null, Icon.Target, app.FrameSelection, null, "Frame the selected formation (F)"));
            var trails = Ui.Button("Trails", null, () =>
            {
                app.Swarm.TrailsEnabled = !app.Swarm.TrailsEnabled;
                RefreshAll();
            }, null, "Light trails (T)");
            bindings.Add(new Binding(() => trails.EnableInClassList("ds-btn--toggled", app.Swarm.TrailsEnabled)));
            transport.Add(trails);
            panel.Add(transport);

            strip = Ui.El("ds-strip");
            stripSegments = new VisualElement { pickingMode = PickingMode.Ignore };
            stripSegments.style.position = Position.Absolute;
            stripSegments.style.left = stripSegments.style.right = stripSegments.style.top = stripSegments.style.bottom = 0;
            strip.Add(stripSegments);
            stripMarkers = new VisualElement { pickingMode = PickingMode.Ignore };
            stripMarkers.style.position = Position.Absolute;
            stripMarkers.style.left = stripMarkers.style.right = stripMarkers.style.top = stripMarkers.style.bottom = 0;
            strip.Add(stripMarkers);
            playhead = Ui.El("ds-playhead");
            playhead.pickingMode = PickingMode.Ignore;
            strip.Add(playhead);
            strip.RegisterCallback<PointerDownEvent>(e =>
            {
                scrubbing = true;
                strip.CapturePointer(e.pointerId);
                ScrubTo(e.localPosition.x);
            });
            strip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (scrubbing && strip.HasPointerCapture(e.pointerId)) ScrubTo(e.localPosition.x);
            });
            strip.RegisterCallback<PointerUpEvent>(e =>
            {
                scrubbing = false;
                strip.ReleasePointer(e.pointerId);
            });
            strip.RegisterCallback<PointerCaptureOutEvent>(_ => scrubbing = false);
            panel.Add(strip);
            return panel;
        }

        void ScrubTo(float x)
        {
            if (app.Show == null) return;
            float width = strip.contentRect.width;
            if (width <= 0f) return;
            app.Seek(Mathf.Clamp01(x / width) * app.Show.Duration);
        }

        void SkipToNextCue()
        {
            if (app.Show == null) return;
            foreach (CueTiming t in app.Show.CueTimings)
            {
                if (t.HoldStart > app.Clock.Time + 0.05f)
                {
                    app.Seek(t.HoldStart);
                    return;
                }
            }
            app.Seek(app.Show.Duration);
        }

        void RebuildTimeline()
        {
            if (stripSegments == null || app.Show == null) return;
            stripSegments.Clear();
            CompiledShow show = app.Show;
            float duration = Mathf.Max(show.Duration, 1e-3f);
            foreach (ShowSegment seg in show.Segments)
            {
                var block = Ui.El("ds-seg");
                block.pickingMode = PickingMode.Ignore;
                block.style.left = Length.Percent(seg.Start / duration * 100f);
                block.style.width = Length.Percent(seg.Duration / duration * 100f);
                if (seg.CueIndex >= 0 && seg.CueIndex < show.Document.Cues.Count)
                {
                    Cue cue = show.Document.Cues[seg.CueIndex];
                    block.style.backgroundColor = cue.Light.ColorA.ToDisplay();
                    if (seg.Kind == SegmentKind.Transit) block.AddToClassList("ds-seg--transit");
                    if (seg.Kind == SegmentKind.Hold)
                    {
                        block.EnableInClassList("ds-seg--selected", seg.CueIndex == app.SelectedCue);
                        var label = Ui.Text(cue.Name, "ds-seg-label");
                        label.pickingMode = PickingMode.Ignore;
                        block.Add(label);
                    }
                }
                else
                {
                    block.AddToClassList("ds-seg--ground");
                }
                stripSegments.Add(block);
            }
            RebuildTimelineMarkers();
        }

        void RebuildTimelineMarkers()
        {
            if (stripMarkers == null) return;
            stripMarkers.Clear();
            if (app.Show == null || app.Report == null || app.ShowIsStale) return;
            float duration = Mathf.Max(app.Show.Duration, 1e-3f);
            foreach (ValidationIssue issue in app.Report.Issues)
            {
                if (issue.Severity == IssueSeverity.Info) continue;
                var m = Ui.El("ds-marker " + (issue.Severity == IssueSeverity.Error ? "ds-marker--error" : "ds-marker--warning"));
                m.pickingMode = PickingMode.Ignore;
                m.style.left = Length.Percent(issue.Time / duration * 100f);
                stripMarkers.Add(m);
            }
        }

        int shownTenths = -1;
        int shownDurationTenths = -1;
        ShowSegment shownSegment;
        int shownCompilePercent = -1;

        void UpdateTransport()
        {
            if (timeLabel == null) return;
            CompiledShow show = app.Show;
            float duration = show != null ? show.Duration : 0f;
            float t = app.Clock.Time;
            // Only touch labels when what they display changes (avoids per-frame string garbage).
            int tenths = Mathf.FloorToInt(t * 10f), durationTenths = Mathf.FloorToInt(duration * 10f);
            if (tenths != shownTenths || durationTenths != shownDurationTenths)
            {
                shownTenths = tenths;
                shownDurationTenths = durationTenths;
                timeLabel.text = Ui.Clock(t) + " / " + Ui.Clock(duration);
            }
            playIcon.Icon = app.Clock.Playing ? Icon.Pause : Icon.Play;
            playhead.style.left = Length.Percent(duration > 0f ? t / duration * 100f : 0f);

            if (app.IsCompiling && app.Show != null)
            {
                int percent = Mathf.RoundToInt(app.CompileProgress * 100f);
                if (percent != shownCompilePercent)
                {
                    shownCompilePercent = percent;
                    shownSegment = null;
                    nowLabel.text = "Re-planning flight paths... " + percent + "%";
                }
                return;
            }
            shownCompilePercent = -1;
            ShowSegment seg = show?.SegmentAt(t);
            if (seg == shownSegment) return;
            shownSegment = seg;
            nowLabel.text = seg == null ? "" : Describe(seg, show);
        }

        static string Describe(ShowSegment seg, CompiledShow show)
        {
            switch (seg.Kind)
            {
                case SegmentKind.Ground: return seg.Start <= 0f ? "On the pads, ready for take-off" : "Landed";
                case SegmentKind.Takeoff: return "Take-off";
                case SegmentKind.Transit: return "Flying into “" + show.Document.Cues[seg.CueIndex].Name + "”";
                case SegmentKind.Hold: return "Holding “" + show.Document.Cues[seg.CueIndex].Name + "”";
                case SegmentKind.Return: return "Returning to the hover grid";
                case SegmentKind.Landing: return "Landing";
                default: return "";
            }
        }
    }
}
