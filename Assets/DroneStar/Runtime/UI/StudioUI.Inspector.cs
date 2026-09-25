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
        enum InspectorTab
        {
            Cue,
            Show,
            Safety,
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly List<IRefreshable> cueBindings = new List<IRefreshable>();
        readonly List<IRefreshable> showBindings = new List<IRefreshable>();
        readonly Dictionary<InspectorTab, Button> tabButtons = new Dictionary<InspectorTab, Button>();
        InspectorTab activeTab = InspectorTab.Cue;
        ScrollView cuePage;
        ScrollView showPage;
        ScrollView safetyPage;
        string cueStructureKey;
        Label cueStats;
        VisualElement safetyProgress;
        VisualElement safetyProgressFill;
        Label safetyProgressLabel;

        VisualElement BuildInspector()
        {
            var panel = Ui.El("ds-panel ds-right");
            var tabs = Ui.El("ds-tabs");
            foreach (InspectorTab tab in Enum.GetValues(typeof(InspectorTab)))
            {
                InspectorTab t = tab;
                var b = new Button(() => SelectTab(t)) { text = tab.ToString().ToUpperInvariant(), focusable = false };
                b.AddToClassList("ds-tab");
                tabButtons[tab] = b;
                tabs.Add(b);
            }
            panel.Add(tabs);

            cuePage = NewPage();
            showPage = NewPage();
            safetyPage = NewPage();
            panel.Add(cuePage);
            panel.Add(showPage);
            panel.Add(safetyPage);
            BuildShowPage();
            SelectTab(InspectorTab.Cue);
            return panel;
        }

        static ScrollView NewPage()
        {
            var page = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            page.AddToClassList("ds-inspector-scroll");
            return page;
        }

        /// <summary>Switches the inspector to "cue", "show" or "safety" (used by automated captures).</summary>
        public void ShowInspectorTab(string name)
        {
            if (Enum.TryParse(name, true, out InspectorTab tab)) SelectTab(tab);
        }

        void SelectTab(InspectorTab tab)
        {
            activeTab = tab;
            foreach (KeyValuePair<InspectorTab, Button> kv in tabButtons) kv.Value.EnableInClassList("ds-tab--active", kv.Key == tab);
            cuePage.EnableInClassList("hidden", tab != InspectorTab.Cue);
            showPage.EnableInClassList("hidden", tab != InspectorTab.Show);
            safetyPage.EnableInClassList("hidden", tab != InspectorTab.Safety);
            if (tab == InspectorTab.Safety) RebuildSafety();
        }

        void RefreshInspector()
        {
            if (cuePage == null) return;
            Cue cue = SelectedCue;
            string key = cue == null
                ? "none"
                : string.Join("|", app.SelectedCue, app.Session.Document.Cues.Count, cue.Formation.Kind, cue.Formation.Style,
                    cue.Light.Effect, cue.Motion.Kind, cue.AutoTransition);
            if (key != cueStructureKey)
            {
                cueStructureKey = key;
                BuildCuePage();
            }
            foreach (IRefreshable b in cueBindings) b.Refresh();
            foreach (IRefreshable b in showBindings) b.Refresh();
            RefreshCueStats();
        }

        // ------------------------------------------------------------------ cue page

        void BuildCuePage()
        {
            cuePage.Clear();
            cueBindings.Clear();
            Cue cue = SelectedCue;
            if (cue == null)
            {
                cuePage.Add(Ui.Text("Select a cue on the left, or add a formation to begin.", "ds-empty"));
                return;
            }
            int index = app.SelectedCue;

            var nameField = new TextField { maxLength = 40 };
            nameField.RegisterCallback<FocusOutEvent>(_ => EditCue("Rename cue", c => c.Name = nameField.value));
            nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) EditCue("Rename cue", c => c.Name = nameField.value);
            });
            cueBindings.Add(new Binding(() =>
            {
                if (SelectedCue != null && nameField.focusController?.focusedElement != nameField) nameField.SetValueWithoutNotify(SelectedCue.Name);
            }));
            var head = Ui.Section("Cue " + (index + 1));
            head.Add(Ui.Field("Name", nameField));
            cuePage.Add(head);

            // Formation -----------------------------------------------------
            FormationSpec f = cue.Formation;
            var formation = Ui.Section("Formation");
            var kinds = new List<string>(Enum.GetNames(typeof(FormationKind)));
            var kindField = new DropdownField(kinds, (int)f.Kind);
            kindField.RegisterValueChangedCallback(e =>
            {
                int k = kinds.IndexOf(e.newValue);
                if (k >= 0) EditCue("Change shape", c => c.Formation.Kind = (FormationKind)k);
            });
            cueBindings.Add(new Binding(() =>
            {
                if (SelectedCue != null) kindField.SetValueWithoutNotify(SelectedCue.Formation.Kind.ToString());
            }));
            formation.Add(Ui.Field("Shape", kindField));

            if (f.Kind != FormationKind.Custom)
            {
                formation.Add(Ui.Field("Style", Bind(new Segmented(new[] { "Filled", "Outline" }, () => (int)(SelectedCue?.Formation.Style ?? 0),
                    i => EditCue("Change style", c => c.Formation.Style = (FillStyle)i)))));
            }
            if (f.Kind == FormationKind.Text)
            {
                var text = new TextField { maxLength = ShowBounds.MaxTextLength };
                text.RegisterCallback<FocusOutEvent>(_ => EditCue("Change text", c => c.Formation.Text = text.value));
                text.RegisterCallback<KeyDownEvent>(e =>
                {
                    if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) EditCue("Change text", c => c.Formation.Text = text.value);
                });
                cueBindings.Add(new Binding(() =>
                {
                    if (SelectedCue != null && text.focusController?.focusedElement != text) text.SetValueWithoutNotify(SelectedCue.Formation.Text);
                }));
                formation.Add(Ui.Field("Text", text));
                formation.Add(Ui.Text("Letters A-Z, digits and ! ? . - + * < >. Accents are folded (Hà → HA).", "ds-hint"));
            }
            formation.Add(CueSlider("Size", 4f, 200f, 1f, c => c.Formation.Size, (c, v) => c.Formation.Size = v, Metres, "size"));
            formation.Add(CueSlider("Altitude", 15f, 110f, 0.5f, c => c.Formation.Center.Y, (c, v) => c.Formation.Center = new System.Numerics.Vector3(c.Formation.Center.X, v, c.Formation.Center.Z), Metres, "alt"));
            formation.Add(CueSlider("Left / right", -120f, 120f, 0.5f, c => c.Formation.Center.X, (c, v) => c.Formation.Center = new System.Numerics.Vector3(v, c.Formation.Center.Y, c.Formation.Center.Z), Metres, "x"));
            formation.Add(CueSlider("Depth", -120f, 120f, 0.5f, c => c.Formation.Center.Z, (c, v) => c.Formation.Center = new System.Numerics.Vector3(c.Formation.Center.X, c.Formation.Center.Y, v), Metres, "z"));
            formation.Add(CueSlider("Turn", -180f, 180f, 1f, c => c.Formation.YawDegrees, (c, v) => c.Formation.YawDegrees = v, Degrees, "yaw"));
            formation.Add(CueSlider("Tilt", -90f, 90f, 1f, c => c.Formation.PitchDegrees, (c, v) => c.Formation.PitchDegrees = v, Degrees, "pitch"));
            if (FormationGenerator.IsPlanar(f))
            {
                formation.Add(CueSlider("Layers", 1f, ShowBounds.MaxLayers, 1f, c => c.Formation.Layers, (c, v) => c.Formation.Layers = Mathf.RoundToInt(v), v => v.ToString("0", Inv), "layers"));
            }
            if (f.Kind == FormationKind.Star || f.Kind == FormationKind.Flower || f.Kind == FormationKind.Galaxy)
            {
                string label = f.Kind == FormationKind.Star ? "Points" : f.Kind == FormationKind.Flower ? "Petals" : "Arms";
                float max = f.Kind == FormationKind.Galaxy ? 6f : ShowBounds.MaxPoints;
                formation.Add(CueSlider(label, f.Kind == FormationKind.Star ? 3f : 2f, max, 1f, c => c.Formation.Points, (c, v) => c.Formation.Points = Mathf.RoundToInt(v), v => v.ToString("0", Inv), "points"));
            }
            if (f.Kind == FormationKind.Helix)
            {
                formation.Add(CueSlider("Turns", ShowBounds.MinTurns, ShowBounds.MaxTurns, 0.25f, c => c.Formation.Turns, (c, v) => c.Formation.Turns = v, v => v.ToString("0.##", Inv), "turns"));
            }
            if (f.Kind == FormationKind.Custom)
            {
                formation.Add(Ui.Text(f.CustomPoints.Count + " custom points (imported from a show file).", "ds-hint"));
            }
            cueStats = Ui.Text("", "ds-stat-line");
            formation.Add(cueStats);
            cuePage.Add(formation);

            // Timing -----------------------------------------------------------
            var timing = Ui.Section("Timing");
            var auto = new Toggle("Auto flight time") { value = cue.AutoTransition };
            auto.RegisterValueChangedCallback(e => EditCue("Toggle auto timing", c => c.AutoTransition = e.newValue));
            cueBindings.Add(new Binding(() =>
            {
                if (SelectedCue != null) auto.SetValueWithoutNotify(SelectedCue.AutoTransition);
            }));
            timing.Add(auto);
            if (!cue.AutoTransition)
            {
                timing.Add(CueSlider("Flight in", ShowBounds.MinTransition, 60f, 0.1f, c => c.TransitionSeconds, (c, v) => c.TransitionSeconds = v, Ui.Seconds, "transit"));
            }
            else
            {
                timing.Add(Ui.Text("The flight time is the shortest that respects the speed and acceleration limits.", "ds-hint"));
            }
            timing.Add(CueSlider("Hold", 0f, 60f, 0.5f, c => c.HoldSeconds, (c, v) => c.HoldSeconds = v, Ui.Seconds, "hold"));
            cuePage.Add(timing);

            // Light --------------------------------------------------------------
            LightSpec l = cue.Light;
            var light = Ui.Section("Light");
            var effects = new List<string>(Enum.GetNames(typeof(LightEffect)));
            var effectField = new DropdownField(effects, (int)l.Effect);
            effectField.RegisterValueChangedCallback(e =>
            {
                int k = effects.IndexOf(e.newValue);
                if (k >= 0) EditCue("Change light effect", c => c.Light.Effect = (LightEffect)k);
            });
            cueBindings.Add(new Binding(() =>
            {
                if (SelectedCue != null) effectField.SetValueWithoutNotify(SelectedCue.Light.Effect.ToString());
            }));
            light.Add(Ui.Field("Effect", effectField));
            bool usesColors = l.Effect != LightEffect.Rainbow && l.Effect != LightEffect.Off;
            if (usesColors)
            {
                light.Add(Bind(new SwatchPicker(l.Effect == LightEffect.Fire ? "Hot" : "Colour A", () => SelectedCue?.Light.ColorA ?? LedColor.White,
                    col => EditCue("Change colour", c => c.Light.ColorA = col))));
                if (l.Effect != LightEffect.Solid)
                {
                    light.Add(Bind(new SwatchPicker(l.Effect == LightEffect.Fire ? "Cool" : "Colour B", () => SelectedCue?.Light.ColorB ?? LedColor.White,
                        col => EditCue("Change colour", c => c.Light.ColorB = col))));
                }
            }
            if (l.Effect != LightEffect.Solid && l.Effect != LightEffect.Gradient && l.Effect != LightEffect.Off)
            {
                light.Add(CueSlider("Tempo", 0f, ShowBounds.MaxEffectSpeed, 0.05f, c => c.Light.Speed, (c, v) => c.Light.Speed = v, v => v.ToString("0.00", Inv) + "×", "speed"));
            }
            if (l.Effect == LightEffect.Gradient)
            {
                light.Add(CueSlider("Direction", -180f, 180f, 1f, c => c.Light.AngleDegrees, (c, v) => c.Light.AngleDegrees = v, Degrees, "angle"));
            }
            light.Add(CueSlider("Brightness", 0f, 1f, 0.01f, c => c.Light.Brightness, (c, v) => c.Light.Brightness = v, v => Mathf.RoundToInt(v * 100f) + "%", "bright"));
            cuePage.Add(light);

            // Motion -------------------------------------------------------------
            MotionSpec m = cue.Motion;
            var motion = Ui.Section("Motion while holding");
            var motions = new List<string>(Enum.GetNames(typeof(MotionKind)));
            var motionField = new DropdownField(motions, (int)m.Kind);
            motionField.RegisterValueChangedCallback(e =>
            {
                int k = motions.IndexOf(e.newValue);
                if (k >= 0) EditCue("Change motion", c => c.Motion.Kind = (MotionKind)k);
            });
            cueBindings.Add(new Binding(() =>
            {
                if (SelectedCue != null) motionField.SetValueWithoutNotify(SelectedCue.Motion.Kind.ToString());
            }));
            motion.Add(Ui.Field("Motion", motionField));
            if (m.Kind == MotionKind.Turntable || m.Kind == MotionKind.Roll)
            {
                motion.Add(CueSlider("Spin", -45f, 45f, 0.5f, c => c.Motion.DegreesPerSecond, (c, v) => c.Motion.DegreesPerSecond = v, v => v.ToString("0.#", Inv) + "°/s", "spin"));
            }
            if (m.Kind == MotionKind.Breathe || m.Kind == MotionKind.Wave)
            {
                motion.Add(CueSlider("Rate", 0.05f, 1f, 0.01f, c => c.Motion.FrequencyHz, (c, v) => c.Motion.FrequencyHz = v, v => v.ToString("0.00", Inv) + " Hz", "freq"));
                if (m.Kind == MotionKind.Breathe)
                {
                    motion.Add(CueSlider("Grow", 0f, 0.3f, 0.01f, c => c.Motion.Amount, (c, v) => c.Motion.Amount = v, v => Mathf.RoundToInt(v * 100f) + "%", "amount"));
                }
                else
                {
                    motion.Add(CueSlider("Ripple", 0f, 6f, 0.1f, c => c.Motion.Amount, (c, v) => c.Motion.Amount = v, Metres, "amount"));
                }
            }
            motion.Add(Ui.Text("Motions ease in and out, and parked drones stay still.", "ds-hint"));
            cuePage.Add(motion);
        }

        VisualElement CueSlider(string label, float min, float max, float step, Func<Cue, float> get, Action<Cue, float> set, Func<float, string> format, string key)
        {
            var slider = new ValueSlider(label, min, max, step,
                () => SelectedCue != null ? get(SelectedCue) : min,
                (v, merge) => EditCue(label, c => set(c, v), key),
                format, key, () => app.Session.EndMerge());
            cueBindings.Add(slider);
            return slider;
        }

        T Bind<T>(T control) where T : VisualElement, IRefreshable
        {
            cueBindings.Add(control);
            return control;
        }

        static string Metres(float v) => v.ToString("0.#", Inv) + " m";

        static string Degrees(float v) => v.ToString("0", Inv) + "°";

        void RefreshCueStats()
        {
            if (cueStats == null) return;
            int i = app.SelectedCue;
            if (app.Show == null || app.ShowIsStale || i < 0 || i >= app.Show.CueTimings.Count)
            {
                cueStats.text = "Re-planning...";
                return;
            }
            CueTiming t = app.Show.CueTimings[i];
            FormationResult f = t.Formation;
            string spacing = float.IsPositiveInfinity(f.MinSpacing) ? "-" : f.MinSpacing.ToString("0.00", Inv) + " m";
            string text = string.Format(Inv, "Lit {0}/{1} · spacing {2} · longest move {3:0} m", f.LitCount, app.Show.DroneCount, spacing, t.LongestMove);
            float motionSpeed = HoldMotion.PeakSpeed(app.Show.Document.Cues[i].Motion, f);
            if (motionSpeed > 0.05f) text += string.Format(Inv, " · motion ≈ {0:0.0} m/s", motionSpeed);
            if (f.DarkCount > 0) text += "\n" + f.DarkCount + " drones park dark behind the shape, enlarge it or add layers to light them.";
            cueStats.text = text;
            cueStats.EnableInClassList("ds-warn", f.DarkCount > 0 || motionSpeed > app.Show.Document.Limits.MaxSpeed);
        }

        // ------------------------------------------------------------------ show page

        void BuildShowPage()
        {
            showPage.Clear();
            showBindings.Clear();

            var show = Ui.Section("Show");
            var author = new TextField { maxLength = 64 };
            author.RegisterCallback<FocusOutEvent>(_ => Edit("Change author", d => d.Author = author.value));
            showBindings.Add(new Binding(() =>
            {
                if (author.focusController?.focusedElement != author) author.SetValueWithoutNotify(app.Session.Document.Author);
            }));
            show.Add(Ui.Field("Author", author));
            show.Add(DocSlider("Drones", 10f, ShowBounds.MaxDrones, 10f, d => d.DroneCount, (d, v) => d.DroneCount = Mathf.RoundToInt(v), v => v.ToString("0", Inv), "drones"));
            show.Add(DocSlider("Pre-show", 0f, 30f, 0.5f, d => d.PreShowSeconds, (d, v) => d.PreShowSeconds = v, Ui.Seconds, "pre"));
            show.Add(DocSlider("Post-show", 0f, 30f, 0.5f, d => d.PostShowSeconds, (d, v) => d.PostShowSeconds = v, Ui.Seconds, "post"));
            show.Add(Ui.Text("Large swarms take longer to plan: every transition solves an optimal drone-to-slot assignment.", "ds-hint"));
            showPage.Add(show);

            var limits = Ui.Section("Safety limits");
            limits.Add(DocSlider("Separation", 0.5f, 5f, 0.1f, d => d.Limits.MinSeparation, (d, v) => d.Limits.MinSeparation = v, Metres, "sep"));
            limits.Add(DocSlider("Top speed", 2f, 15f, 0.5f, d => d.Limits.MaxSpeed, (d, v) => d.Limits.MaxSpeed = v, v => v.ToString("0.#", Inv) + " m/s", "speed"));
            limits.Add(DocSlider("Acceleration", 1f, 10f, 0.25f, d => d.Limits.MaxAcceleration, (d, v) => d.Limits.MaxAcceleration = v, v => v.ToString("0.##", Inv) + " m/s²", "accel"));
            limits.Add(DocSlider("Ceiling", 30f, 300f, 5f, d => d.Limits.MaxAltitude, (d, v) => d.Limits.MaxAltitude = v, Metres, "ceiling"));
            limits.Add(DocSlider("Geofence", 50f, 500f, 5f, d => d.Limits.GeofenceRadius, (d, v) => d.Limits.GeofenceRadius = v, Metres, "fence"));
            limits.Add(DocSlider("Battery", 2f, 30f, 0.5f, d => d.Limits.MaxFlightSeconds / 60f, (d, v) => d.Limits.MaxFlightSeconds = v * 60f, v => v.ToString("0.#", Inv) + " min", "battery"));
            limits.Add(Ui.Text("Formations keep 1.41 × the separation between neighbours; with optimal assignment and synchronised straight-line flights (CAPT) transitions then stay clear of each other.", "ds-hint"));
            showPage.Add(limits);

            var pad = Ui.Section("Launch pad");
            pad.Add(DocSlider("Pad spacing", 1f, 6f, 0.1f, d => d.Pad.Spacing, (d, v) => d.Pad.Spacing = v, Metres, "padsp"));
            pad.Add(DocSlider("Hover height", 5f, 30f, 0.5f, d => d.Pad.HoverAltitude, (d, v) => d.Pad.HoverAltitude = v, Metres, "hover"));
            pad.Add(DocSlider("Columns", 0f, 60f, 1f, d => d.Pad.Columns, (d, v) => d.Pad.Columns = Mathf.RoundToInt(v), v => v < 0.5f ? "auto" : v.ToString("0", Inv), "cols"));
            showPage.Add(pad);
        }

        VisualElement DocSlider(string label, float min, float max, float step, Func<ShowDocument, float> get, Action<ShowDocument, float> set, Func<float, string> format, string key)
        {
            var slider = new ValueSlider(label, min, max, step,
                () => get(app.Session.Document),
                (v, merge) => Edit(label, d => set(d, v), "show." + key),
                format, key, () => app.Session.EndMerge());
            showBindings.Add(slider);
            return slider;
        }

        // ------------------------------------------------------------------ safety page

        void RebuildSafety()
        {
            if (safetyPage == null) return;
            safetyPage.Clear();

            var verdict = Ui.El("ds-verdict");
            ValidationReport report = app.ShowIsStale ? null : app.Report;
            if (report == null)
            {
                verdict.Add(new IconElement(Icon.Target));
                verdict.Add(Ui.Text("Checking the flight...", "ds-verdict-title"));
            }
            else if (report.Passed)
            {
                verdict.AddToClassList("ds-verdict--ok");
                var icon = new IconElement(Icon.Check);
                icon.AddToClassList("ds-ok");
                verdict.Add(icon);
                verdict.Add(Ui.Text(report.WarningCount == 0 ? "Safe to fly" : "Safe to fly, with warnings", "ds-verdict-title"));
            }
            else
            {
                verdict.AddToClassList("ds-verdict--error");
                var icon = new IconElement(Icon.Warning);
                icon.AddToClassList("ds-error");
                verdict.Add(icon);
                verdict.Add(Ui.Text(report.ErrorCount + (report.ErrorCount == 1 ? " problem to fix" : " problems to fix"), "ds-verdict-title"));
            }
            safetyPage.Add(verdict);

            safetyProgress = Ui.El("ds-progress");
            safetyProgressFill = Ui.El("ds-progress-fill");
            safetyProgress.Add(safetyProgressFill);
            safetyProgressLabel = Ui.Text("", "ds-hint");
            safetyPage.Add(safetyProgress);
            safetyPage.Add(safetyProgressLabel);

            if (report != null && app.Show != null)
            {
                SafetyLimits l = app.Show.Document.Limits;
                var stats = Ui.Section("Flight envelope");
                string closest = float.IsPositiveInfinity(report.MinSeparation)
                    ? "> " + report.SeparationSearchRadius.ToString("0.0", Inv) + " m"
                    : report.MinSeparation.ToString("0.00", Inv) + " m";
                stats.Add(Stat("Closest pass", closest, report.MinSeparation >= l.MinSeparation));
                stats.Add(Stat("Top speed", report.MaxSpeed.ToString("0.0", Inv) + " m/s", report.MaxSpeed <= l.MaxSpeed * 1.001f));
                stats.Add(Stat("Peak acceleration", report.MaxAcceleration.ToString("0.0", Inv) + " m/s²", report.MaxAcceleration <= l.MaxAcceleration * 1.02f));
                stats.Add(Stat("Highest point", report.MaxAltitude.ToString("0.0", Inv) + " m", report.MaxAltitude <= l.MaxAltitude));
                stats.Add(Stat("Furthest from pads", report.MaxRadius.ToString("0", Inv) + " m", report.MaxRadius <= l.GeofenceRadius));
                stats.Add(Stat("Flight time", Ui.Clock(app.Show.Duration), app.Show.Duration <= l.MaxFlightSeconds));
                safetyPage.Add(stats);

                var findings = Ui.Section("Findings");
                if (report.Issues.Count == 0) findings.Add(Ui.Text("No findings. Every transition keeps its distance.", "ds-hint"));
                int shown = 0;
                foreach (ValidationIssue issue in report.Issues)
                {
                    if (++shown > 60)
                    {
                        findings.Add(Ui.Text("...and " + (report.Issues.Count - 60) + " more in the flight report.", "ds-hint"));
                        break;
                    }
                    ValidationIssue captured = issue;
                    var row = Ui.El("ds-issue");
                    var icon = new IconElement(issue.Severity == IssueSeverity.Info ? Icon.Target : Icon.Warning);
                    icon.AddToClassList(issue.Severity == IssueSeverity.Error ? "ds-error" : issue.Severity == IssueSeverity.Warning ? "ds-warn" : "ds-ok");
                    row.Add(icon);
                    row.Add(Ui.Text(issue.Message, "ds-issue-text"));
                    row.RegisterCallback<ClickEvent>(_ =>
                    {
                        if (captured.CueIndex >= 0) app.Select(captured.CueIndex, seek: false);
                        app.Clock.Pause();
                        app.Seek(Mathf.Max(0f, captured.Time - 0.5f));
                    });
                    row.tooltip = "Jump to " + Ui.Clock(issue.Time);
                    findings.Add(row);
                }
                safetyPage.Add(findings);
            }

            var actions = Ui.Section("Report");
            actions.Add(Ui.Button("Export flight report", Icon.Download, app.ExportFlightReport));
            actions.Add(Ui.Text("The check flies the whole show in simulation, testing every pair of drones between samples for the closest approach.", "ds-hint"));
            safetyPage.Add(actions);
        }

        static VisualElement Stat(string name, string value, bool ok)
        {
            var row = Ui.El("ds-stat");
            row.Add(Ui.Text(name, "ds-stat-name"));
            row.Add(Ui.Text(value, ok ? "ds-ok" : "ds-error"));
            return row;
        }

        void UpdateSafetyProgress()
        {
            if (safetyProgress == null) return;
            bool busy = app.IsCompiling || app.IsValidating || app.ShowIsStale;
            safetyProgress.EnableInClassList("hidden", !busy);
            safetyProgressLabel.EnableInClassList("hidden", !busy);
            if (!busy) return;
            float p = app.IsCompiling ? app.CompileProgress * 0.4f : 0.4f + app.ValidationProgress * 0.6f;
            safetyProgressFill.style.width = Length.Percent(p * 100f);
            safetyProgressLabel.text = app.IsCompiling ? app.CompileStatus + "..." : "Flying the show in simulation... " + Mathf.RoundToInt(app.ValidationProgress * 100f) + "%";
        }
    }
}
