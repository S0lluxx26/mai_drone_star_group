using System;
using System.Collections.Generic;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class CustomShapeTests
    {
        [Test]
        public void StrokesAreResampledEvenlyWithinBudget()
        {
            var line = new List<Vector2> { new Vector2(-1f, 0f), new Vector2(1f, 0f) };
            var zigzag = new List<Vector2> { new Vector2(-1f, -0.5f), new Vector2(0f, 0.5f), new Vector2(1f, -0.5f) };
            List<Vector3> pts = CustomShapes.FromStrokes(new List<IReadOnlyList<Vector2>> { line, zigzag }, 120);
            Assert.That(pts.Count, Is.InRange(100, 120));
            // Consecutive samples on the straight stroke are evenly spaced.
            float d1 = Vector3.Distance(pts[0], pts[1]);
            float d2 = Vector3.Distance(pts[10], pts[11]);
            Assert.That(d2, Is.EqualTo(d1).Within(1e-3f));
            foreach (Vector3 p in pts) Assert.That(p.Z, Is.EqualTo(0f));
        }

        [Test]
        public void TapsSurviveAsSinglePoints()
        {
            var tap = new List<Vector2> { new Vector2(0.3f, 0.3f) };
            List<Vector3> pts = CustomShapes.FromStrokes(new List<IReadOnlyList<Vector2>> { tap });
            Assert.That(pts.Count, Is.EqualTo(1));
            Assert.That(CustomShapes.FromStrokes(null).Count, Is.EqualTo(0));
        }

        [Test]
        public void CsvIsParsedCentredAndScaled()
        {
            string csv = "x,y,z\n10,20,0\n30,20,0\n# comment\n20;40;0\nbad,row\n20\t0\t0\n";
            List<Vector3> pts = CustomShapes.FromCsv(csv);
            Assert.That(pts.Count, Is.EqualTo(4));
            float maxAbs = 0f;
            Vector3 sum = Vector3.Zero;
            foreach (Vector3 p in pts)
            {
                maxAbs = Math.Max(maxAbs, Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
                sum += p;
            }
            Assert.That(maxAbs, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(pts[0], Is.EqualTo(new Vector3(-0.5f, 0f, 0f)));
        }

        [Test]
        public void LargeCsvIsSubsampled()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 5000; i++) sb.Append(i).Append(',').Append(i % 17).Append('\n');
            Assert.That(CustomShapes.FromCsv(sb.ToString(), 500).Count, Is.EqualTo(500));
            Assert.That(CustomShapes.FromCsv("").Count, Is.EqualTo(0));
            Assert.That(CustomShapes.FromCsv("nan,1\ninf,2").Count, Is.EqualTo(0));
        }

        [Test]
        public void SketchCueFliesSafely()
        {
            Cue cue = DemoShows.NewCue(FormationKind.Custom, 0);
            Assert.That(cue.Formation.CustomPoints.Count, Is.GreaterThan(50).And.LessThanOrEqualTo(CustomShapes.DefaultMaxPoints));
            Assert.That(FormationGenerator.IsPlanar(cue.Formation), Is.True, "drawn sketches accept depth layers");
            var doc = new ShowDocument { DroneCount = 200 };
            doc.Cues.Add(cue);
            ValidationReport r = SafetyValidator.Run(new ShowCompiler().Compile(doc));
            Assert.That(r.Passed, Is.True, string.Join("\n", r.Issues.ConvertAll(i => i.Message)));
        }

        [Test]
        public void ImportedThreeDimensionalCloudsAreNotLayered()
        {
            var spec = new FormationSpec { Kind = FormationKind.Custom };
            spec.CustomPoints.Add(new Vector3(0f, 0f, 0.5f));
            Assert.That(FormationGenerator.IsPlanar(spec), Is.False);
        }
    }
}
