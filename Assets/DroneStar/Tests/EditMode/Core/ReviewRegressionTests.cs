using System;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    /// <summary>Regression tests for defects found in the logic review.</summary>
    public class ReviewRegressionTests
    {
        static ShowDocument RingShow()
        {
            var doc = new ShowDocument { DroneCount = 60 };
            doc.Cues.Add(new Cue { Name = "Ring", HoldSeconds = 4f, Formation = new FormationSpec { Kind = FormationKind.Ring, Size = 50f } });
            return doc;
        }

        [Test]
        public void HarshAccelerationFailsTheShow()
        {
            ShowDocument doc = RingShow();
            doc.Cues[0].HoldSeconds = 1f;
            doc.Cues[0].Motion = new MotionSpec { Kind = MotionKind.Turntable, DegreesPerSecond = 20f };
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            Assert.That(r.Passed, Is.False);
            Assert.That(r.Issues.Exists(i => i.Kind == IssueKind.Acceleration && i.Severity == IssueSeverity.Error), Is.True);
        }

        [Test]
        public void HugeCustomPointsAreClampedAndCompile()
        {
            ShowDocument doc = RingShow();
            doc.Cues[0].Formation.Kind = FormationKind.Custom;
            for (int i = 0; i < 20; i++) doc.Cues[0].Formation.CustomPoints.Add(new Vector3(1e20f * (i + 1), -1e9f, 3f));
            ShowSanitizer.Sanitize(doc);
            foreach (Vector3 v in doc.Cues[0].Formation.CustomPoints)
            {
                Assert.That(Math.Abs(v.X), Is.LessThanOrEqualTo(1.5f));
                Assert.That(Math.Abs(v.Y), Is.LessThanOrEqualTo(1.5f));
            }
            CompiledShow show = new ShowCompiler().Compile(doc);
            Assert.That(show.Duration, Is.LessThan(600f));
        }

        [Test]
        public void CustomPointsSurviveShapeSwitchAndUndo()
        {
            ShowDocument doc = RingShow();
            doc.Cues[0].Formation.Kind = FormationKind.Custom;
            for (int i = 0; i < 10; i++) doc.Cues[0].Formation.CustomPoints.Add(new Vector3(i * 0.1f, 0.5f, 0f));
            var session = new ShowEditSession(doc);
            session.Edit("Shape", d => d.Cues[0].Formation.Kind = FormationKind.Sphere);
            session.Edit("Size", d => d.Cues[0].Formation.Size = 44f);
            session.Undo();
            Assert.That(session.Document.Cues[0].Formation.CustomPoints.Count, Is.EqualTo(10));
            ShowDocument reloaded = ShowSerializer.FromJson(ShowSerializer.ToJson(session.Document));
            Assert.That(reloaded.Cues[0].Formation.CustomPoints.Count, Is.EqualTo(10));
        }

        [Test]
        public void NullEntriesDoNotCrashCompileOrLoad()
        {
            ShowDocument doc = RingShow();
            doc.Cues.Add(null);
            doc.Cues.Add(new Cue { Formation = null, Light = null, Motion = null });
            Assert.DoesNotThrow(() => new ShowCompiler().Compile(doc));
            Assert.DoesNotThrow(() => new ShowEditSession(doc));
            var empty = new ShowDocument { Cues = null, Limits = null, Pad = null };
            Assert.DoesNotThrow(() => new ShowCompiler().Compile(empty));
        }

        [Test]
        public void NonFiniteValidatorStepFallsBackToDefault()
        {
            CompiledShow show = new ShowCompiler().Compile(RingShow());
            ValidationReport r = SafetyValidator.Run(show, float.NaN);
            Assert.That(r.Samples, Is.GreaterThan(100));
        }

        [Test]
        public void SavingClosesTheMergeGroup()
        {
            var session = new ShowEditSession(RingShow());
            session.Edit("Size", d => d.Cues[0].Formation.Size = 30f, "size");
            session.MarkSaved();
            session.Edit("Size", d => d.Cues[0].Formation.Size = 35f, "size");
            Assert.That(session.Undo(), Is.True);
            Assert.That(session.Document.Cues[0].Formation.Size, Is.EqualTo(30f));
            Assert.That(session.IsDirty, Is.False);
        }

        [Test]
        public void ColoursAreStoredAtFilePrecision()
        {
            var session = new ShowEditSession(RingShow());
            session.Edit("Colour", d => d.Cues[0].Light.ColorA = new LedColor(0.501f, 0.2f, 0.9f));
            LedColor c = session.Document.Cues[0].Light.ColorA;
            Assert.That(c, Is.EqualTo(LedColor.FromHex(c.ToHex())));
        }

        [TestCase(@"""\u 041""")]
        [TestCase(@"""\u004 """)]
        [TestCase(@"""\u+041""")]
        public void LooseUnicodeEscapesAreRejected(string json)
        {
            Assert.Throws<FormatException>(() => JsonValue.Parse(json));
        }

        [Test]
        public void StrictVersionAndEnums()
        {
            Assert.Throws<FormatException>(() => ShowSerializer.FromJson("{\"format\":\"dronestar-show\",\"version\":1.4}"));
            ShowDocument doc = ShowSerializer.FromJson("{\"format\":\"dronestar-show\",\"version\":1,\"cues\":[{\"formation\":{\"kind\":\"Grid, Ring\"}}]}");
            Assert.That(doc.Cues[0].Formation.Kind, Is.EqualTo(FormationKind.Sphere));
        }

        [Test]
        public void UnpairedSurrogatesAreStripped()
        {
            var doc = new ShowDocument { Title = "A\uD800B\uDC00C \uD83D\uDE80" };
            ShowSanitizer.Sanitize(doc);
            Assert.That(doc.Title, Is.EqualTo("ABC \uD83D\uDE80"));
        }
    }
}
