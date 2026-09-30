using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DroneStar.App
{
    /// <summary>
    /// Automated screenshots for visual review and store pages. Inert unless the player is started with
    /// <c>-capture &lt;folder&gt;</c>. Optional: <c>-captureTimes 5,30,90</c> (show seconds),
    /// or <c>-captureTimes c0,c4</c> (scenes), <c>-captureCamera orbit|audience|aerial|closeup</c>, <c>-captureTab</c>,
    /// <c>-captureSelect &lt;cue&gt;</c>, <c>-captureSection "Stage effects"</c> (scroll the cue page to a section),
    /// <c>-captureAddCue &lt;kind&gt;</c>, <c>-captureLive</c> (capture while playing,
    /// so drones lean and props turn) and <c>-demo</c> to capture the cinematic run.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        void Start()
        {
            string folder = Arg("-capture");
            if (string.IsNullOrEmpty(folder)) return;
            StartCoroutine(Run(folder));
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        IEnumerator Run(string folder)
        {
            Directory.CreateDirectory(folder);
            var app = FindAnyObjectByType<ShowStudioApp>();
            float started = Time.realtimeSinceStartup;
            while (app != null && (app.Show == null || app.IsCompiling))
            {
                if (Time.realtimeSinceStartup - started > 120f) break;
                yield return null;
            }
            if (app == null || app.Show == null)
            {
                Debug.LogError("[Capture] The show never compiled.");
                Application.Quit(2);
                yield break;
            }

            string tab = Arg("-captureTab");
            if (!string.IsNullOrEmpty(tab))
            {
                var ui = FindAnyObjectByType<StudioUI>();
                if (ui != null) ui.ShowInspectorTab(tab);
            }
            string add = Arg("-captureAddCue");
            if (!string.IsNullOrEmpty(add) && Enum.TryParse(add, true, out Core.FormationKind kind))
            {
                app.AddCue(kind);
                while (app.ShowIsStale || app.IsCompiling) yield return null;
            }
            string select = Arg("-captureSelect");
            if (!string.IsNullOrEmpty(select) && int.TryParse(select, NumberStyles.Integer, CultureInfo.InvariantCulture, out int selected))
            {
                app.Select(selected, seek: false);
            }
            string section = Arg("-captureSection");
            if (!string.IsNullOrEmpty(section))
            {
                var inspector = FindAnyObjectByType<StudioUI>();
                // Let the inspector lay out before scrolling it.
                yield return null;
                yield return null;
                if (inspector != null) inspector.ScrollCuePageTo(section);
            }

            string mode = Arg("-captureCamera");
            if (mode == "audience") app.CameraRig.SetMode(CameraMode.Audience);
            else if (mode == "aerial") app.CameraRig.SetMode(CameraMode.Aerial);

            // Entries are show seconds ("42.5") or scenes ("c3" = three seconds into cue 3's hold).
            var times = new List<float>();
            foreach (string s in (Arg("-captureTimes") ?? "30").Split(','))
            {
                string token = s.Trim();
                if (token.StartsWith("c", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(token.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cue)
                    && cue >= 0 && cue < app.Show.CueTimings.Count)
                {
                    times.Add(app.Show.CueTimings[cue].HoldStart + 3f);
                }
                else if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                {
                    times.Add(t);
                }
            }

            // Let the safety check finish so its UI state is captured too.
            while (app.IsValidating && Time.realtimeSinceStartup - started < 180f) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);

            bool live = Array.IndexOf(Environment.GetCommandLineArgs(), "-captureLive") >= 0;
            foreach (float t in times)
            {
                app.Clock.Pause();
                app.Seek(live ? Mathf.Max(0f, t - 1.5f) : t);
                yield return new WaitForSecondsRealtime(0.3f);
                if (mode == "closeup") app.FrameCloseUp();
                if (live)
                {
                    app.Clock.Play(app.Show.Duration);
                    while (app.Clock.Playing && app.Clock.Time < t) yield return null;
                    app.Clock.Pause();
                }
                yield return new WaitForSecondsRealtime(1.2f);
                yield return new WaitForEndOfFrame();
                string path = Path.Combine(folder, string.Format(CultureInfo.InvariantCulture, "shot_{0:000.0}.png", t));
                ScreenCapture.CaptureScreenshot(path);
                yield return null;
                yield return null;
                Debug.Log("[Capture] " + path);
            }
            Application.Quit(0);
        }
    }
}
