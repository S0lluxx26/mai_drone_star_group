using System;
using System.Collections.Generic;
using System.IO;
using DroneStar.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace DroneStar.App
{
    public sealed partial class StudioUI
    {
        VisualElement demoLayer;
        VisualElement demoTitle;
        Label demoHeadline;
        VisualElement demoCaption;
        Label demoCaptionTitle;
        Label demoCaptionSub;
        VisualElement demoEndCard;
        VisualElement demoProgressFill;
        IconElement demoPlayIcon;

        // ------------------------------------------------------------------ dialogs

        VisualElement OpenDialog(string title, string body)
        {
            CloseDialog();
            var backdrop = Ui.El("ds-backdrop");
            backdrop.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target == backdrop) CloseDialog();
            });
            var dialog = Ui.El("ds-dialog");
            dialog.Add(Ui.Text(title, "ds-dialog-title"));
            if (!string.IsNullOrEmpty(body)) dialog.Add(Ui.Text(body, "ds-dialog-body"));
            backdrop.Add(dialog);
            dialogLayer.Add(backdrop);
            return dialog;
        }

        void CloseDialog() => dialogLayer?.Clear();

        static VisualElement DialogButtons(params Button[] buttons)
        {
            var row = Ui.El("ds-dialog-buttons");
            foreach (Button b in buttons) row.Add(b);
            return row;
        }

        static VisualElement ListItem(string title, string subtitle, Action onClick, params Button[] actions)
        {
            var item = Ui.El("ds-list-item");
            var text = Ui.El("ds-list-item-text");
            text.Add(Ui.Text(title, "ds-list-item-title"));
            if (!string.IsNullOrEmpty(subtitle)) text.Add(Ui.Text(subtitle, "ds-list-item-sub"));
            item.Add(text);
            foreach (Button b in actions) item.Add(b);
            if (onClick != null) item.RegisterCallback<ClickEvent>(e =>
            {
                if (!(e.target is Button) && !(e.target is VisualElement ve && ve.GetFirstAncestorOfType<Button>() != null)) onClick();
            });
            return item;
        }

        /// <summary>Runs <paramref name="action"/>, first asking before unsaved changes are thrown away.</summary>
        void GuardUnsaved(string what, Action action)
        {
            if (!app.Session.IsDirty)
            {
                CloseDialog();
                action();
                return;
            }
            VisualElement d = OpenDialog("Discard unsaved changes?", "“" + app.Session.Document.Title + "” has changes that are not saved. " + what + " anyway?");
            d.Add(DialogButtons(
                Ui.Button("Cancel", null, CloseDialog),
                Ui.Button("Save first", Icon.Save, () =>
                {
                    // Never discard the show if saving it failed.
                    if (!app.SaveToLibrary()) return;
                    CloseDialog();
                    action();
                }),
                Ui.Button("Discard", null, () =>
                {
                    CloseDialog();
                    action();
                }, "ds-btn--accent")));
        }

        void ShowTemplates()
        {
            VisualElement d = OpenDialog("New show", "Start from a template. Everything stays editable.");
            var list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.AddToClassList("ds-dialog-list");
            foreach (DemoShows.Template template in DemoShows.Templates)
            {
                DemoShows.Template t = template;
                list.Add(ListItem(t.Name, t.Description, () => GuardUnsaved("Start a new show", () => app.NewFromTemplate(t))));
            }
            d.Add(list);
            d.Add(DialogButtons(Ui.Button("Close", null, CloseDialog)));
        }

        void ShowOpen()
        {
            VisualElement d = OpenDialog("Open show", "Shows saved on " + app.Library.Location + ".");
            var list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            list.AddToClassList("ds-dialog-list");
            List<string> saved;
            try
            {
                saved = app.Library.List();
            }
            catch (Exception e)
            {
                saved = new List<string>();
                Toast("Could not read saved shows: " + e.Message, true);
            }
            foreach (string name in saved)
            {
                string n = name;
                list.Add(ListItem(n, null, () => GuardUnsaved("Open another show", () => app.OpenFromLibrary(n)),
                    Ui.Button(null, Icon.Trash, () => ConfirmDelete(n), "ds-btn--small", "Delete")));
            }
            if (saved.Count == 0) list.Add(Ui.Text("Nothing saved yet, use Save in the top bar.", "ds-hint"));

            if (!WebBridge.IsWeb)
            {
                List<string> files = app.ImportableFiles();
                foreach (string f in files)
                {
                    string path = f;
                    list.Add(ListItem(Path.GetFileName(path), "Import folder", () => GuardUnsaved("Import this file", () => app.ImportFromPath(path))));
                }
                d.Add(list);
                d.Add(Ui.Text("To import a file, copy it into " + WebBridge.ImportFolder, "ds-hint"));
                d.Add(DialogButtons(Ui.Button("Close", null, CloseDialog)));
                return;
            }
            d.Add(list);
            d.Add(DialogButtons(
                Ui.Button("Import file...", Icon.Upload, () => GuardUnsaved("Import a file", app.ImportFile)),
                Ui.Button("Close", null, CloseDialog)));
        }

        void ConfirmDelete(string name)
        {
            VisualElement d = OpenDialog("Delete “" + name + "”?", "This removes the saved copy from " + app.Library.Location + ". It cannot be undone.");
            d.Add(DialogButtons(
                Ui.Button("Cancel", null, ShowOpen),
                Ui.Button("Delete", Icon.Trash, () =>
                {
                    app.DeleteFromLibrary(name);
                    ShowOpen();
                }, "ds-btn--accent")));
        }

        void ShowExport()
        {
            VisualElement d = OpenDialog("Export", WebBridge.IsWeb ? "Files download through your browser." : "Files are written to " + WebBridge.ExportFolder);
            d.Add(ListItem("Show file (.dronestar.json)", "The editable show, to reopen or share.", () =>
            {
                CloseDialog();
                app.ExportShowFile();
            }));
            d.Add(ListItem("Trajectories (.csv)", "Every drone's position and LED colour at 4 Hz, metres from the pad centre.", () =>
            {
                CloseDialog();
                app.ExportTrajectories();
            }));
            d.Add(ListItem("Flight report (.md)", "Safety verdict, flight envelope, cue table and every finding.", () =>
            {
                CloseDialog();
                app.ExportFlightReport();
            }));
            d.Add(DialogButtons(Ui.Button("Close", null, CloseDialog)));
        }

        void ShowHelp()
        {
            VisualElement d = OpenDialog("Drone Star Studio", "Design drone light shows for the Mai Drone Star Group: pick formations, colour them, then press Demo Run to watch.");
            var grid = Ui.El("ds-help-grid");
            void Row(string key, string desc)
            {
                grid.Add(Ui.Text(key, "ds-help-key"));
                grid.Add(Ui.Text(desc, "ds-help-desc"));
            }
            Row("Space", "Play / pause");
            Row("Left / Right  (Shift)", "Step 1 s (5 s)");
            Row("Home / End", "Start / end of show");
            Row("Up / Down", "Previous / next cue");
            Row("Ctrl+Z / Ctrl+Y", "Undo / redo");
            Row("Ctrl+S", "Save");
            Row("Del", "Delete the selected cue");
            Row("F", "Frame the selected formation");
            Row("1 / 2 / 3", "Orbit / audience / aerial camera");
            Row("T", "Light trails");
            Row("D  ·  Esc", "Start / leave the demo run");
            Row("Mouse", "Drag to orbit, right-drag to pan, wheel to zoom");
            d.Add(grid);
            d.Add(Ui.Text("Every change is re-planned: formations keep 1.41 × the minimum separation, drones are matched to slots with the Hungarian algorithm (minimum total squared distance) and all fly synchronised straight lines with a minimum-jerk profile. The safety check then flies the whole show in simulation.", "ds-hint"));
            d.Add(DialogButtons(Ui.Button("Close", null, CloseDialog, "ds-btn--primary")));
        }

        // ------------------------------------------------------------------ demo overlay

        VisualElement BuildDemoOverlay()
        {
            demoLayer = Ui.El("ds-demo hidden");
            demoLayer.pickingMode = PickingMode.Ignore;

            demoTitle = Ui.El("ds-demo-title");
            demoTitle.pickingMode = PickingMode.Ignore;
            demoTitle.Add(Ui.Text("MAI DRONE STAR GROUP PRESENTS", "ds-demo-kicker"));
            demoHeadline = Ui.Text("", "ds-demo-headline");
            demoTitle.Add(demoHeadline);
            demoTitle.Add(Ui.Text("a drone light show", "ds-demo-sub"));
            demoLayer.Add(demoTitle);

            demoCaption = Ui.El("ds-demo-caption");
            demoCaption.pickingMode = PickingMode.Ignore;
            demoCaptionTitle = Ui.Text("", "ds-demo-caption-title");
            demoCaptionSub = Ui.Text("", "ds-demo-caption-sub");
            demoCaption.Add(demoCaptionTitle);
            demoCaption.Add(demoCaptionSub);
            demoLayer.Add(demoCaption);

            demoEndCard = Ui.El("ds-demo-title");
            demoEndCard.pickingMode = PickingMode.Ignore;
            demoEndCard.Add(Ui.Text("THANK YOU FOR WATCHING", "ds-demo-kicker"));
            demoEndCard.Add(Ui.Text("Mai Drone Star Group", "ds-demo-headline"));
            demoEndCard.Add(Ui.Text("designed in Drone Star Studio", "ds-demo-sub"));
            demoLayer.Add(demoEndCard);

            var bar = Ui.El("ds-demo-bar");
            bar.pickingMode = PickingMode.Ignore;
            var play = Ui.Button(null, Icon.Pause, app.TogglePlay, null, "Pause / play (Space)");
            demoPlayIcon = play.Q<IconElement>();
            bar.Add(play);
            bar.Add(Ui.Button(null, Icon.Sound, () =>
            {
                app.Score.SetMuted(!app.Score.Muted);
                RefreshTopBar();
            }, null, "Soundtrack on/off"));
            bar.Add(Ui.Button("Exit demo", Icon.Close, () => app.Demo.End(), null, "Back to the editor (Esc)"));
            demoLayer.Add(bar);

            var progress = Ui.El("ds-demo-progress");
            progress.pickingMode = PickingMode.Ignore;
            demoProgressFill = Ui.El("ds-demo-progress-fill");
            progress.Add(demoProgressFill);
            demoLayer.Add(progress);
            return demoLayer;
        }

        void OnDemoChanged(bool running)
        {
            CloseDialog();
            editorLayer.EnableInClassList("hidden", running);
            demoLayer.EnableInClassList("hidden", !running);
            if (running) demoHeadline.text = app.Demo.Title;
            RefreshAll();
        }

        void UpdateDemoOverlay()
        {
            if (demoLayer == null || app.Demo == null || !app.Demo.IsRunning) return;
            DemoDirector d = app.Demo;
            demoTitle.style.opacity = d.TitleAlpha;
            demoCaption.style.opacity = d.CaptionAlpha;
            demoCaption.style.translate = new Translate(Length.Pixels((1f - d.CaptionAlpha) * -24f), 0);
            if (demoCaptionTitle.text != d.Caption)
            {
                demoCaptionTitle.text = d.Caption;
                demoCaptionSub.text = d.CaptionDetail.ToUpperInvariant();
            }
            demoEndCard.style.opacity = d.EndCardAlpha;
            float duration = app.Show != null ? app.Show.Duration : 1f;
            demoProgressFill.style.width = Length.Percent(Mathf.Clamp01(app.Clock.Time / Mathf.Max(duration, 1e-3f)) * 100f);
            demoPlayIcon.Icon = app.Clock.Playing ? Icon.Pause : Icon.Play;
        }
    }
}
