using System;
using System.Collections;
using DroneStar.App;
using DroneStar.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace DroneStar.Tests
{
    /// <summary>
    /// Runs the real studio scene: compile, validate, render, edit + undo through the session, demo run,
    /// and the UI's core panels. Any logged error fails the test (Unity Test Framework default).
    /// </summary>
    public class StudioPlayModeTests
    {
        ShowStudioApp app;

        [UnitySetUp]
        public IEnumerator LoadStudio()
        {
            yield return SceneManager.LoadSceneAsync("DroneStarStudio", LoadSceneMode.Single);
            app = UnityEngine.Object.FindAnyObjectByType<ShowStudioApp>();
            Assert.That(app, Is.Not.Null, "scene contains the studio");
            yield return WaitFor(() => app.Show != null && !app.IsCompiling, 60f, "initial compile");
        }

        static IEnumerator WaitFor(Func<bool> condition, float timeout, string what)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeout) Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static DemoShows.Template Classic
        {
            get
            {
                foreach (DemoShows.Template t in DemoShows.Templates) if (t.Name == "Classic Night") return t;
                throw new InvalidOperationException("No Classic Night template");
            }
        }

        /// <summary>The editing tests use the 360-drone classic show, which plans in a moment on any machine.</summary>
        IEnumerator OpenClassicShow()
        {
            app.NewFromTemplate(Classic);
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "classic show compile");
            Assert.That(app.Show.DroneCount, Is.EqualTo(360));
        }

        [UnityTest]
        public IEnumerator FlagshipCompilesFromTheBakedCacheAndRendersEveryDrone()
        {
            int n = DemoShows.FlagshipDrones;
            Assert.That(app.Show.DroneCount, Is.EqualTo(n));
            Assert.That(app.CompileError, Is.Null);
            yield return null;
            Assert.That(app.Swarm.DroneCount, Is.EqualTo(n));
            var glow = app.Swarm.transform.Find("LED Glow").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(glow.vertexCount, Is.EqualTo(n * 4));
            // Model scenes need the shape library that the scene ships with.
            for (int i = 0; i < app.Show.CueTimings.Count; i++)
            {
                Assert.That(app.Show.CueTimings[i].Formation.LitCount, Is.EqualTo(n), app.Show.Document.Cues[i].Name);
            }
        }

        [UnityTest]
        public IEnumerator ClassicShowValidates()
        {
            yield return OpenClassicShow();
            yield return WaitFor(() => app.Report != null, 180f, "safety check");
            Assert.That(app.Report.Passed, Is.True, string.Join("\n", app.Report.Issues.ConvertAll(i => i.Message)));
        }

        [UnityTest]
        public IEnumerator CloseUpFollowsADroneWithDetailedAirframes()
        {
            CueTiming robot = app.Show.CueTimings[2];
            app.Seek(robot.HoldStart + 1f);
            yield return null;
            yield return null;
            app.CameraRig.SetMode(CameraMode.Orbit);
            app.FrameCloseUp();
            Assert.That(app.CameraRig.IsFollowing, Is.True);
            Assert.That(app.FollowedDrone, Is.InRange(0, DemoShows.FlagshipDrones - 1));
            app.Clock.Play(app.Show.Duration);
            yield return new WaitForSecondsRealtime(1.5f);
            float distance = Vector3.Distance(app.CameraRig.transform.position, LastPosition(app.FollowedDrone));
            Assert.That(distance, Is.LessThan(12f), "the camera flies beside the followed drone");
            Assert.That(app.Swarm.DetailedDrawn, Is.GreaterThan(0), "nearby drones use the detailed airframe");
            app.Clock.Pause();
            app.FrameSelection();
            Assert.That(app.CameraRig.IsFollowing, Is.False, "framing a formation stops following");
        }

        Vector3 LastPosition(int drone)
        {
            var positions = new System.Numerics.Vector3[app.Show.DroneCount];
            app.Show.SamplePositions(app.Clock.Time, positions);
            return positions[drone].ToUnity();
        }

        [UnityTest]
        public IEnumerator SecondDemoStagesTheFestival()
        {
            var festival = UnityEngine.Object.FindAnyObjectByType<FestivalVenue>(FindObjectsInactive.Include);
            Assert.That(festival, Is.Not.Null);
            Assert.That(festival.Visible, Is.False, "the first demo is on the plain lake");
            app.RunDemo(DemoShows.Templates[1]);
            yield return WaitFor(() => app.Show != null && !app.ShowIsStale && !app.IsCompiling && app.Show.Document.Venue == ShowVenue.Festival, 120f, "Demo 2 compile");
            yield return WaitFor(() => app.Demo.IsRunning, 10f, "Demo 2 start");
            Assert.That(festival.Visible, Is.True);
            foreach (string part in new[] { "Festival Stage", "Stage Lights", "Fountains", "Mist", "Lasers", "Flames", "Flame Reflections", "Steam" })
            {
                Assert.That(festival.transform.Find(part), Is.Not.Null, part);
            }
            // Into the Lạc bird's hold: the fountains and lasers run from the show clock, and its salvo of flames
            // opens the scene. The clock is paused so the checks see exactly these moments.
            CueTiming bird = app.Show.CueTimings[4];
            app.Clock.Pause();
            app.Seek(bird.HoldStart + 0.5f);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(app.Demo.Caption, Is.EqualTo("Rise of the Lạc Bird"));
            Assert.That(festival.FlameLevel, Is.GreaterThan(0.5f), "the salvo as the bird appears");
            Assert.That(festival.StageLight.r, Is.GreaterThan(0f), "the flames light the audience");
            // The river steams; the bird's hold does not.
            Assert.That(festival.SteamLevel, Is.EqualTo(0f));
            CueTiming river = app.Show.CueTimings[2];
            app.Seek(0.5f * (river.HoldStart + river.HoldEnd));
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(festival.SteamLevel, Is.EqualTo(1f));
            Assert.That(festival.FlameLevel, Is.EqualTo(0f));
            app.Demo.End();
            app.NewFromTemplate(Classic);
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "classic show compile");
            Assert.That(festival.Visible, Is.False, "a lake show hides the festival set");
        }

        [UnityTest]
        public IEnumerator FleetPresetRescalesAndRecompiles()
        {
            yield return OpenClassicShow();
            float heart = app.Session.Document.Cues[5].Formation.Size;
            app.SetFleetSize(512);
            Assert.That(app.Session.Document.DroneCount, Is.EqualTo(512));
            Assert.That(app.Session.Document.Cues[5].Formation.Size, Is.GreaterThan(heart));
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 120f, "recompile at 512 drones");
            Assert.That(app.Show.DroneCount, Is.EqualTo(512));
            Assert.That(app.CompileError, Is.Null);
        }

        [UnityTest]
        public IEnumerator ModelCueCanBeAddedFromTheLibrary()
        {
            yield return OpenClassicShow();
            app.Select(0, seek: false);
            app.AddCue(FormationKind.Model, "Whale");
            Cue cue = app.Session.Document.Cues[app.SelectedCue];
            Assert.That(cue.Formation.Kind, Is.EqualTo(FormationKind.Model));
            Assert.That(cue.Formation.Model, Is.EqualTo("Whale"));
            Assert.That(cue.Light.Effect, Is.EqualTo(LightEffect.Artwork));
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "recompile");
            Assert.That(app.Show.CueTimings[app.SelectedCue].Formation.LitCount, Is.EqualTo(360));
        }

        [UnityTest]
        public IEnumerator EditingRecompilesAndUndoRestores()
        {
            yield return OpenClassicShow();
            app.Select(1, seek: false);
            float before = app.Session.Document.Cues[1].Formation.Size;
            app.Session.Edit("Grow", d => d.Cues[1].Formation.Size = before + 12f);
            Assert.That(app.ShowIsStale, Is.True);
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "recompile after edit");
            Assert.That(app.Show.Document.Cues[1].Formation.Size, Is.EqualTo(before + 12f));

            app.Undo();
            Assert.That(app.Session.Document.Cues[1].Formation.Size, Is.EqualTo(before));
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "recompile after undo");
            Assert.That(app.Show.Document.Cues[1].Formation.Size, Is.EqualTo(before));
        }

        [UnityTest]
        public IEnumerator AddMoveAndDeleteCues()
        {
            yield return OpenClassicShow();
            int count = app.Session.Document.Cues.Count;
            app.Select(0, seek: false);
            app.AddCue(FormationKind.Cube);
            Assert.That(app.Session.Document.Cues.Count, Is.EqualTo(count + 1));
            Assert.That(app.SelectedCue, Is.EqualTo(1));
            Assert.That(app.Session.Document.Cues[1].Formation.Kind, Is.EqualTo(FormationKind.Cube));
            app.MoveCue(1, 1);
            Assert.That(app.Session.Document.Cues[2].Formation.Kind, Is.EqualTo(FormationKind.Cube));
            app.DeleteCue(2);
            Assert.That(app.Session.Document.Cues.Count, Is.EqualTo(count));
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "recompile");
            Assert.That(app.Show.CueTimings.Count, Is.EqualTo(count));
        }

        [UnityTest]
        public IEnumerator DemoRunDrivesTheCinematicCamera()
        {
            app.Demo.Begin();
            Assert.That(app.Demo.IsRunning, Is.True);
            Assert.That(app.CameraRig.Mode, Is.EqualTo(CameraMode.Cinematic));
            Assert.That(app.Clock.Playing, Is.True);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(app.Demo.TitleAlpha, Is.GreaterThan(0f), "title card fades in");

            CueTiming first = app.Show.CueTimings[0];
            app.Seek(first.HoldStart + 2f);
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(app.Demo.Caption, Is.EqualTo(app.Show.Document.Cues[0].Name));
            Assert.That(app.Demo.CaptionAlpha, Is.GreaterThan(0.5f));
            Vector3 camera = app.CameraRig.transform.position;
            Assert.That(camera.y, Is.GreaterThan(NightEnvironment.WaterLevel));

            app.Demo.End();
            Assert.That(app.Demo.IsRunning, Is.False);
            Assert.That(app.CameraRig.Mode, Is.Not.EqualTo(CameraMode.Cinematic));
        }

        [UnityTest]
        public IEnumerator InterfaceShowsCorePanels()
        {
            var doc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
            yield return null;
            VisualElement root = doc.rootVisualElement;
            Assert.That(root.Q(className: "ds-topbar"), Is.Not.Null);
            Assert.That(root.Q(className: "ds-timeline"), Is.Not.Null);
            Assert.That(root.Query(className: "ds-cue-card").ToList().Count, Is.EqualTo(app.Session.Document.Cues.Count));
            Assert.That(root.Q(className: "ds-cue-card--selected"), Is.Not.Null);
            Assert.That(root.Query(className: "ds-seg").ToList().Count, Is.EqualTo(app.Show.Segments.Count));
        }

        [UnityTest]
        public IEnumerator SketchCueShowsDrawingPadAndFlies()
        {
            yield return OpenClassicShow();
            app.Select(0, seek: false);
            app.AddCue(FormationKind.Custom);
            Assert.That(app.Session.Document.Cues[app.SelectedCue].Formation.Kind, Is.EqualTo(FormationKind.Custom));
            yield return null;
            var doc = UnityEngine.Object.FindAnyObjectByType<UIDocument>();
            Assert.That(doc.rootVisualElement.Q(className: "ds-sketch"), Is.Not.Null, "the Cue tab shows the sketch pad");
            yield return WaitFor(() => !app.ShowIsStale && !app.IsCompiling, 60f, "recompile");
            Assert.That(app.Show.CueTimings[app.SelectedCue].Formation.LitCount, Is.GreaterThan(50));
        }

        [UnityTest]
        public IEnumerator PlaybackAdvancesAndSeekClamps()
        {
            app.Seek(10f);
            app.Clock.Play(app.Show.Duration);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(app.Clock.Time, Is.GreaterThan(10f));
            app.Clock.Pause();
            app.Seek(1e6f);
            Assert.That(app.Clock.Time, Is.EqualTo(app.Show.Duration));
            app.Seek(-5f);
            Assert.That(app.Clock.Time, Is.EqualTo(0f));
        }
    }
}
