using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class ValidatorTests
    {
        static ShowDocument TwoCueShow()
        {
            var doc = new ShowDocument { DroneCount = 80 };
            doc.Cues.Add(new Cue { Name = "Ring", HoldSeconds = 3f, Formation = new FormationSpec { Kind = FormationKind.Ring, Size = 50f } });
            doc.Cues.Add(new Cue { Name = "Cube", HoldSeconds = 3f, Formation = new FormationSpec { Kind = FormationKind.Cube, Size = 30f } });
            return doc;
        }

        [Test]
        public void AutoTimedShowPasses()
        {
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(TwoCueShow()));
            Assert.That(r.Passed, Is.True, string.Join("\n", r.Issues.ConvertAll(i => i.Message)));
            Assert.That(r.MaxSpeed, Is.LessThanOrEqualTo(8f * 1.001f));
            Assert.That(r.MinSeparation, Is.GreaterThanOrEqualTo(1.5f));
            Assert.That(r.Samples, Is.GreaterThan(100));
        }

        [Test]
        public void RushedTransitionIsFlaggedForSpeed()
        {
            ShowDocument doc = TwoCueShow();
            doc.Cues[0].AutoTransition = false;
            doc.Cues[0].TransitionSeconds = 2f;
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            Assert.That(r.Passed, Is.False);
            ValidationIssue speed = r.Issues.Find(i => i.Kind == IssueKind.Speed);
            Assert.That(speed, Is.Not.Null);
            Assert.That(speed.CueIndex, Is.EqualTo(0));
            Assert.That(speed.Value, Is.GreaterThan(8f));
            Assert.That(speed.Message, Does.Contain("Ring"));
        }

        [Test]
        public void CrowdedPadsAreFlaggedForSeparation()
        {
            ShowDocument doc = TwoCueShow();
            doc.Pad.Spacing = 1f;
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            ValidationIssue sep = r.Issues.Find(i => i.Kind == IssueKind.Separation);
            Assert.That(sep, Is.Not.Null);
            Assert.That(sep.Value, Is.LessThan(1.5f));
            Assert.That(sep.DroneA, Is.Not.EqualTo(sep.DroneB));
        }

        [Test]
        public void FormationBelowGroundIsFlagged()
        {
            ShowDocument doc = TwoCueShow();
            doc.Cues[1].Formation.Center = new Vector3(0f, 8f, 0f);
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            Assert.That(r.Issues.Exists(i => i.Kind == IssueKind.Ground && i.Severity == IssueSeverity.Error), Is.True);
        }

        [Test]
        public void CeilingGeofenceAndBatteryAreChecked()
        {
            ShowDocument doc = TwoCueShow();
            doc.Cues[1].Formation.Center = new Vector3(120f, 118f, 0f);
            doc.Limits.MaxFlightSeconds = 30f;
            doc.Limits.GeofenceRadius = 100f;
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            Assert.That(r.Issues.Exists(i => i.Kind == IssueKind.Altitude), Is.True);
            Assert.That(r.Issues.Exists(i => i.Kind == IssueKind.Geofence), Is.True);
            Assert.That(r.Issues.Exists(i => i.Kind == IssueKind.FlightTime), Is.True);
        }

        [Test]
        public void FastSpinIsFlagged()
        {
            ShowDocument doc = TwoCueShow();
            doc.Cues[0].HoldSeconds = 12f;
            doc.Cues[0].Motion = new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 60f };
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            Assert.That(r.Issues.Exists(i => i.Kind == IssueKind.Speed && i.CueIndex == 0), Is.True);
        }

        [Test]
        public void ParkedDronesAreReportedAsInfo()
        {
            ShowDocument doc = TwoCueShow();
            doc.Cues[1].Formation.Size = 6f;
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            ValidationIssue parked = r.Issues.Find(i => i.Kind == IssueKind.ParkedDrones);
            Assert.That(parked, Is.Not.Null);
            Assert.That(parked.Severity, Is.EqualTo(IssueSeverity.Info));
        }

        [Test]
        public void IncrementalRunMatchesOneShot()
        {
            CompiledShow show = new ShowCompiler().Compile(TwoCueShow());
            var v = new SafetyValidator(show);
            int steps = 0;
            while (!v.Step(37)) steps++;
            ValidationReport once = SafetyValidator.Run(show);
            Assert.That(steps, Is.GreaterThan(3));
            Assert.That(v.Report.MinSeparation, Is.EqualTo(once.MinSeparation));
            Assert.That(v.Report.MaxSpeed, Is.EqualTo(once.MaxSpeed));
            Assert.That(v.Report.Issues.Count, Is.EqualTo(once.Issues.Count));
            Assert.That(v.Progress, Is.EqualTo(1f));
        }
    }
}
