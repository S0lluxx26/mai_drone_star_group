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

        [UnityTest]
        public IEnumerator FlagshipShowCompilesValidatesAndRendersEveryDrone()
        {
            Assert.That(app.Show.DroneCount, Is.EqualTo(360));
            yield return WaitFor(() => app.Report != null, 120f, "safety check");
            Assert.That(app.Report.Passed, Is.True, string.Join("\n", app.Report.Issues.ConvertAll(i => i.Message)));
            yield return null;
            Assert.That(app.Swarm.DroneCount, Is.EqualTo(360));
            var glow = app.Swarm.transform.Find("LED Glow").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(glow.vertexCount, Is.EqualTo(360 * 4));
        }

        [UnityTest]
        public IEnumerator EditingRecompilesAndUndoRestores()
        {
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
