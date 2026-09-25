using System;
using System.Collections.Generic;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class FormationTests
    {
        const float Spacing = 2.12f;

        static IEnumerable<TestCaseData> AllShapes()
        {
            foreach (FormationKind kind in Enum.GetValues(typeof(FormationKind)))
            {
                foreach (FillStyle style in Enum.GetValues(typeof(FillStyle)))
                {
                    yield return new TestCaseData(kind, style).SetName("Shape_" + kind + "_" + style);
                }
            }
        }

        static FormationSpec Spec(FormationKind kind, FillStyle style, float size = 60f)
        {
            var spec = new FormationSpec { Kind = kind, Style = style, Size = size, Text = "MAI 26", Center = new Vector3(0, 60, 0) };
            if (kind == FormationKind.Custom)
            {
                for (int i = 0; i < 300; i++)
                {
                    float a = i * 0.21f;
                    spec.CustomPoints.Add(new Vector3(MathF.Cos(a) * (i / 300f), MathF.Sin(a) * (i / 300f), 0f));
                }
            }
            return spec;
        }

        [TestCaseSource(nameof(AllShapes))]
        public void EveryShapeFillsEveryDroneAtSafeSpacing(FormationKind kind, FillStyle style)
        {
            foreach (int n in new[] { 1, 7, 150, 360 })
            {
                FormationResult f = FormationGenerator.Generate(Spec(kind, style), n, Spacing);
                Assert.That(f.Slots.Length, Is.EqualTo(n), kind + " slots");
                Assert.That(f.LitCount, Is.InRange(0, n));
                Assert.That(f.MinSpacing, Is.GreaterThanOrEqualTo(Spacing * 0.999f), kind + " spacing with " + n + " drones");
                int lit = 0;
                foreach (Slot s in f.Slots)
                {
                    Assert.That(ShowMath.IsFinite(s.Position), Is.True);
                    Assert.That(s.U, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                    if (!s.Dark) lit++;
                }
                Assert.That(lit, Is.EqualTo(f.LitCount));
            }
        }

        [TestCase(FormationKind.Sphere)]
        [TestCase(FormationKind.Star)]
        [TestCase(FormationKind.Heart)]
        [TestCase(FormationKind.Text)]
        [TestCase(FormationKind.Grid)]
        [TestCase(FormationKind.Ring)]
        public void RoomyShapesLightEveryDrone(FormationKind kind)
        {
            FormationSpec spec = Spec(kind, FillStyle.Filled, kind == FormationKind.Text ? 90f : 64f);
            spec.Text = "MAI";
            FormationResult f = FormationGenerator.Generate(spec, 300, Spacing);
            Assert.That(f.LitCount, Is.EqualTo(300));
        }

        [Test]
        public void TinyShapeParksTheSurplusDark()
        {
            FormationResult f = FormationGenerator.Generate(Spec(FormationKind.Heart, FillStyle.Filled, 8f), 200, Spacing);
            Assert.That(f.LitCount, Is.GreaterThan(0).And.LessThan(200));
            Assert.That(f.DarkCount, Is.EqualTo(200 - f.LitCount));
            Assert.That(f.MinSpacing, Is.GreaterThanOrEqualTo(Spacing * 0.999f));
            foreach (Slot s in f.Slots)
            {
                if (s.Dark) Assert.That(s.Position.Y, Is.EqualTo(60f).Within(1e-3f), "reserve grid stays at formation altitude");
            }
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            FormationSpec spec = Spec(FormationKind.Galaxy, FillStyle.Filled);
            FormationResult a = FormationGenerator.Generate(spec, 250, Spacing);
            FormationResult b = FormationGenerator.Generate(spec, 250, Spacing);
            for (int i = 0; i < 250; i++) Assert.That(b.Slots[i].Position, Is.EqualTo(a.Slots[i].Position));
        }

        [Test]
        public void ZeroDronesGiveEmptyFormation()
        {
            FormationResult f = FormationGenerator.Generate(Spec(FormationKind.Star, FillStyle.Filled), 0, Spacing);
            Assert.That(f.Slots.Length, Is.EqualTo(0));
            Assert.That(float.IsPositiveInfinity(f.MinSpacing), Is.True);
        }

        [Test]
        public void OrientationRotatesAboutTheCentre()
        {
            FormationSpec spec = Spec(FormationKind.Star, FillStyle.Filled);
            spec.YawDegrees = 90f;
            FormationResult f = FormationGenerator.Generate(spec, 100, Spacing);
            Assert.That(f.LitCount, Is.EqualTo(100));
            foreach (Slot s in f.Slots)
            {
                Assert.That(MathF.Abs(s.Position.X), Is.LessThan(1e-3f), "a star yawed 90° lies in the x = 0 plane");
            }
            Assert.That(f.Normal.X, Is.EqualTo(1f).Within(1e-4f).Or.EqualTo(-1f).Within(1e-4f));
        }

        [Test]
        public void TextNormalisesAccentsAndCase()
        {
            Assert.That(StrokeFont.Normalize("Mai Hà Đông!"), Is.EqualTo("MAI HA DONG!"));
            Assert.That(StrokeFont.Normalize("@@@"), Is.EqualTo(""));
            List<Vector2[]> strokes = StrokeFont.Layout("MAI", 60f, out float unit);
            Assert.That(strokes.Count, Is.GreaterThan(3));
            Assert.That(unit, Is.EqualTo(60f / 15f).Within(1e-4f));
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector2[] s in strokes)
            {
                foreach (Vector2 p in s)
                {
                    min = Math.Min(min, p.X);
                    max = Math.Max(max, p.X);
                }
            }
            Assert.That(min, Is.EqualTo(-max).Within(1e-3f), "ink is centred");
            Assert.That(max, Is.LessThanOrEqualTo(30f));
            Assert.That(max, Is.GreaterThan(24f));
        }

        [Test]
        public void UnrenderableTextParksEveryone()
        {
            var spec = Spec(FormationKind.Text, FillStyle.Filled);
            spec.Text = "@#$";
            FormationResult f = FormationGenerator.Generate(spec, 50, Spacing);
            Assert.That(f.LitCount, Is.EqualTo(0));
            Assert.That(f.Slots.Length, Is.EqualTo(50));
        }

        [Test]
        public void LayersStackAlongTheFacingAxis()
        {
            var spec = Spec(FormationKind.Heart, FillStyle.Filled);
            spec.Layers = 3;
            FormationResult f = FormationGenerator.Generate(spec, 300, Spacing);
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Slot s in f.Slots)
            {
                if (s.Dark) continue;
                minZ = Math.Min(minZ, s.Position.Z);
                maxZ = Math.Max(maxZ, s.Position.Z);
            }
            Assert.That(maxZ - minZ, Is.EqualTo(2f * Spacing * 1.05f).Within(0.01f));
            Assert.That(f.MinSpacing, Is.GreaterThanOrEqualTo(Spacing * 0.999f));
        }
    }
}
