using System;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class DemoShowTests
    {
        [Test]
        public void EveryTemplateIsSafeToFly()
        {
            ShowCompiler compiler = TestData.BakedCompiler();
            foreach (DemoShows.Template template in DemoShows.Templates)
            {
                // Both demos are checked at every fleet size by BakedDemoTests.EveryPresolvedFleetLightsEveryDroneAndFliesSafely.
                string method = template.Create.Method.Name;
                if (method == nameof(DemoShows.StarGroupNight) || method == nameof(DemoShows.LacBirdFestival)) continue;
                ShowDocument doc = template.Create();
                CompiledShow show = compiler.Compile(doc);
                ValidationReport report = SafetyValidator.Run(show, TestData.ValidationStep(doc.DroneCount));
                string findings = string.Join("\n", report.Issues.ConvertAll(i => i.Severity + ": " + i.Message));
                Assert.That(report.ErrorCount, Is.EqualTo(0), template.Name + "\n" + findings);
                Assert.That(report.WarningCount, Is.EqualTo(0), template.Name + "\n" + findings);
                Assert.That(report.MinSeparation, Is.GreaterThanOrEqualTo(doc.Limits.MinSeparation), template.Name);
                Assert.That(show.Duration, Is.LessThan(doc.Limits.MaxFlightSeconds), template.Name);
                TestContext.WriteLine(string.Format("{0}: {1:0.0} s, closest {2:0.00} m, top speed {3:0.0} m/s, peak accel {4:0.0} m/s²",
                    template.Name, show.Duration, report.MinSeparation, report.MaxSpeed, report.MaxAcceleration));
            }
        }

        [Test]
        public void FlagshipDemoLightsEveryDroneInEveryScene()
        {
            ShowCompiler compiler = TestData.BakedCompiler();
            CompiledShow show = compiler.Compile(DemoShows.StarGroupNight());
            foreach (CueTiming t in show.CueTimings)
            {
                Assert.That(t.Formation.LitCount, Is.EqualTo(show.DroneCount), show.Document.Cues[t.CueIndex].Name);
            }
            Assert.That(show.DroneCount, Is.EqualTo(DemoShows.FlagshipDrones));
            Assert.That(show.DroneCount, Is.EqualTo(8192), "four times the original 2,048-drone flagship");
            Assert.That(show.Duration, Is.InRange(200f, 900f));
            Assert.That(compiler.CacheMisses, Is.EqualTo(0), "the flagship ships fully pre-solved");
        }

        [Test]
        public void LacBirdFestivalIsTheSecondDemo()
        {
            ShowDocument doc = DemoShows.LacBirdFestival();
            Assert.That(doc.Venue, Is.EqualTo(ShowVenue.Festival));
            Assert.That(doc.DroneCount, Is.EqualTo(DemoShows.FlagshipDrones));
            Assert.That(doc.Cues.Exists(c => c.Formation.Kind == FormationKind.Model && c.Formation.Model == "Lac bird" && c.Motion.Kind == MotionKind.Flap));
            Assert.That(DemoShows.Templates[1].Create().Title, Is.EqualTo(doc.Title));
            ShowCompiler compiler = TestData.BakedCompiler();
            CompiledShow show = compiler.Compile(doc);
            foreach (CueTiming t in show.CueTimings)
            {
                Assert.That(t.Formation.LitCount, Is.EqualTo(show.DroneCount), show.Document.Cues[t.CueIndex].Name);
            }
            Assert.That(compiler.CacheMisses, Is.EqualTo(0), "Demo 2 ships fully pre-solved");
        }

        [Test]
        public void NewCueDefaultsAreValid()
        {
            foreach (FormationKind kind in Enum.GetValues(typeof(FormationKind)))
            {
                Cue cue = DemoShows.NewCue(kind, 3);
                ShowSanitizer.SanitizeCue(cue);
                Assert.That(cue.Formation.Kind, Is.EqualTo(kind));
                Assert.That(cue.Name, Does.StartWith(kind == FormationKind.Model ? "Robot" : DemoShows.KindLabel(kind)));
                FormationResult f = FormationGenerator.Generate(cue.Formation, 200, 2.12f);
                Assert.That(f.LitCount, Is.GreaterThan(20), kind + " lights a useful share of the drones");
            }
        }
    }
}
