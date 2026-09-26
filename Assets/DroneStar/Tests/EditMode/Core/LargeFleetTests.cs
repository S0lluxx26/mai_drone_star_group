using System;
using System.Collections.Generic;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    /// <summary>Fleets up to 4096 drones: model shapes, the auction solver, cache export and resizing.</summary>
    public class LargeFleetTests
    {
        [Test]
        public void ShapePackHasTheTwelveModels()
        {
            IReadOnlyList<ModelShape> shapes = ShapeLibrary.Shapes;
            Assert.That(shapes.Count, Is.EqualTo(12));
            foreach (ModelShape m in shapes)
            {
                Assert.That(m.Points.Length, Is.EqualTo(12288), m.Name + ": a deep pool, three times the largest fleet");
                Assert.That(m.Colors.Length, Is.EqualTo(12288), m.Name);
                float extent = 0f;
                foreach (Vector3 p in m.Points) extent = Math.Max(extent, Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z))));
                Assert.That(extent, Is.EqualTo(1f).Within(0.01f), m.Name + " is normalised");
            }
            Assert.That(ShapeLibrary.TryGet("eiffel tower", out _), Is.True, "lookup ignores case");
        }

        [Test]
        public void CorruptPacksAreRejected()
        {
            Assert.Throws<FormatException>(() => ShapeLibrary.ParsePack(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            byte[] pack = System.IO.File.ReadAllBytes(TestData.Find("Assets/DroneStar/Data/ShapePack.bytes"));
            var half = new byte[pack.Length / 2];
            Array.Copy(pack, half, half.Length);
            Assert.Throws<FormatException>(() => ShapeLibrary.ParsePack(half));
        }

        [Test]
        public void ModelsCarryTheirColoursAndKeepSpacing()
        {
            var spec = new FormationSpec { Kind = FormationKind.Model, Model = "Whale", Size = 160f, Center = new Vector3(0, 100, 0) };
            FormationResult f = FormationGenerator.Generate(spec, 2048, 1.7f);
            Assert.That(f.LitCount, Is.EqualTo(2048));
            Assert.That(f.MinSpacing, Is.GreaterThanOrEqualTo(1.7f * 0.999f));
            int coloured = 0;
            foreach (Slot s in f.Slots) coloured += s.Art.MaxComponent > 0.05f ? 1 : 0;
            Assert.That(coloured, Is.GreaterThan(1500), "slots keep the baked model colours");

            var art = new LightSpec { Effect = LightEffect.Artwork, Speed = 0f };
            LedColor c = LightEngine.Evaluate(art, f.Slots[10], 10, 3f);
            Assert.That(c, Is.EqualTo(f.Slots[10].Art.Clamped()));
            var unknown = new FormationSpec { Kind = FormationKind.Model, Model = "No such model", Size = 50f };
            Assert.That(FormationGenerator.Generate(unknown, 100, 2f).LitCount, Is.EqualTo(0), "an unknown model parks every drone");
        }

        [Test]
        public void FewerDronesStillDrawTheWholeModel()
        {
            var spec = new FormationSpec { Kind = FormationKind.Model, Model = "Eiffel Tower", Size = 100f, Center = new Vector3(0, 100, 0) };
            FormationResult small = FormationGenerator.Generate(spec, 256, 2.12f);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Slot s in small.Slots)
            {
                if (s.Dark) continue;
                minY = Math.Min(minY, s.Position.Y);
                maxY = Math.Max(maxY, s.Position.Y);
            }
            Assert.That(maxY - minY, Is.GreaterThan(90f), "256 drones still span the full tower height");
        }

        [Test]
        public void NewModelCuesAreSizedToLightTheFleet()
        {
            ShowDocument classic = DemoShows.ClassicNight();
            foreach (ModelShape m in ShapeLibrary.Shapes)
            {
                Cue cue = DemoShows.NewCue(FormationKind.Model, 0, classic, m.Name);
                FormationResult f = FormationGenerator.Generate(cue.Formation, classic.DroneCount, classic.Limits.FormationSpacing);
                Assert.That(f.LitCount, Is.EqualTo(classic.DroneCount), m.Name);
                Assert.That(cue.Formation.Center.Y, Is.GreaterThanOrEqualTo(cue.Formation.Size * 0.6f), m.Name + " clears the barge");
            }
            var small = new FormationSpec { Kind = FormationKind.Model, Model = "Whale", Size = 20f };
            float size = FormationGenerator.SizeToLight(small, 500, 2.12f);
            Assert.That(size, Is.GreaterThan(20f));
            small.Size = size;
            Assert.That(FormationGenerator.Generate(small, 500, 2.12f).LitCount, Is.EqualTo(500));
            small.Size = size / 1.05f;
            Assert.That(FormationGenerator.Generate(small, 500, 2.12f).LitCount, Is.LessThan(500), "and not much larger than needed");
        }

        [Test]
        public void AuctionMatchesHungarianOnFormationPairs()
        {
            FormationResult a = FormationGenerator.Generate(new FormationSpec { Kind = FormationKind.Sphere, Size = 60f }, 700, 2.12f);
            FormationResult b = FormationGenerator.Generate(new FormationSpec { Kind = FormationKind.Star, Size = 110f }, 700, 2.12f);
            Vector3[] from = Positions(a), to = Positions(b);
            int[] exact = AssignmentSolver.Solve(from, to);
            int[] near = new AuctionAssignment(from, to).Solve();
            double ce = AssignmentSolver.TotalCost(from, to, exact), cn = AssignmentSolver.TotalCost(from, to, near);
            Assert.That((cn - ce) / ce, Is.LessThan(1e-4), "auction is within 0.01 % of the optimum");
            var seen = new bool[near.Length];
            foreach (int j in near)
            {
                Assert.That(seen[j], Is.False);
                seen[j] = true;
            }
        }

        [Test]
        public void AuctionLeavesNoPairWorthSwapping()
        {
            // Far-apart shows make the auction's ε large; the repair pass must still leave every pair
            // swap-optimal, which is the condition CAPT's collision-free proof relies on.
            FormationResult a = FormationGenerator.Generate(new FormationSpec { Kind = FormationKind.Star, Size = 120f, Center = new Vector3(-700f, 100f, 0f) }, 800, 2.12f);
            FormationResult b = FormationGenerator.Generate(new FormationSpec { Kind = FormationKind.Galaxy, Size = 120f, Center = new Vector3(700f, 100f, 0f) }, 800, 2.12f);
            Vector3[] from = Positions(a), to = Positions(b);
            var auction = new AuctionAssignment(from, to);
            int[] p = auction.Solve();
            for (int i = 0; i < p.Length; i++)
            {
                for (int j = i + 1; j < p.Length; j++)
                {
                    double dot = Vector3.Dot(from[i] - from[j], to[p[i]] - to[p[j]]);
                    Assert.That(dot, Is.GreaterThan(-1e-3), "pair " + i + "," + j + " would be cheaper swapped");
                }
            }
        }

        [Test]
        public void AuctionIsResumable()
        {
            var rng = new Random(11);
            var from = new Vector3[300];
            var to = new Vector3[300];
            for (int i = 0; i < 300; i++)
            {
                from[i] = new Vector3((float)rng.NextDouble() * 80f, (float)rng.NextDouble() * 80f, 0f);
                to[i] = new Vector3((float)rng.NextDouble() * 80f, 50f + (float)rng.NextDouble() * 80f, 3f);
            }
            var stepped = new AuctionAssignment(from, to);
            int steps = 0;
            while (!stepped.Step(40)) steps++;
            int[] once = new AuctionAssignment(from, to).Solve();
            Assert.That(steps, Is.GreaterThan(5));
            Assert.That(stepped.Result, Is.EqualTo(once));
        }

        [Test]
        public void CacheRoundTripsAndRejectsGarbage()
        {
            var compiler = new ShowCompiler();
            ShowDocument doc = DemoShows.Blank();
            compiler.Compile(doc);
            byte[] cache = compiler.ExportCache();
            var fresh = new ShowCompiler();
            Assert.That(fresh.ImportCache(cache), Is.EqualTo(2));
            fresh.Compile(doc);
            Assert.That(fresh.CacheMisses, Is.EqualTo(0), "every transition comes from the imported cache");
            Assert.That(fresh.ImportCache(new byte[] { 1, 2, 3 }), Is.EqualTo(0));
            var broken = (byte[])cache.Clone();
            broken[broken.Length - 1] = 0xFF;
            broken[broken.Length - 2] = 0xFF;
            Assert.That(new ShowCompiler().ImportCache(broken), Is.EqualTo(0), "out-of-range slots are rejected");
        }

        [Test]
        public void ResizingForAFleetScalesShapesAndLimits()
        {
            ShowDocument doc = DemoShows.ClassicNight();
            float heart = doc.Cues[5].Formation.Size;
            var spins = new float[doc.Cues.Count];
            for (int i = 0; i < spins.Length; i++) spins[i] = doc.Cues[i].Motion.DegreesPerSecond;
            ShowScaler.ResizeForDroneCount(doc, 1440);
            Assert.That(doc.DroneCount, Is.EqualTo(1440));
            Assert.That(doc.Cues[5].Formation.Size, Is.EqualTo(heart * 2f).Within(0.01f));
            int turning = 0;
            for (int i = 0; i < spins.Length; i++)
            {
                MotionKind kind = doc.Cues[i].Motion.Kind;
                if (kind != MotionKind.Turntable && kind != MotionKind.Roll) continue;
                turning++;
                Assert.That(doc.Cues[i].Motion.DegreesPerSecond, Is.EqualTo(spins[i] / 2f).Within(1e-3f), "twice the size spins half as fast");
            }
            Assert.That(turning, Is.GreaterThan(0));
            foreach (Cue c in doc.Cues) Assert.That(c.Formation.Center.Y, Is.GreaterThanOrEqualTo(c.Formation.Size * 0.6f));
            Assert.That(doc.Limits.MaxAltitude, Is.GreaterThanOrEqualTo(120f));
        }

        [Test]
        public void LargeFleetCompilesWithAuctionAndFliesSafely()
        {
            ShowDocument doc = DemoShows.Blank();
            ShowScaler.ResizeForDroneCount(doc, 1200);
            CompiledShow show = new ShowCompiler().Compile(doc);
            Assert.That(show.DroneCount, Is.EqualTo(1200));
            ValidationReport r = SafetyValidator.Run(show, 0.1f);
            Assert.That(r.Passed, Is.True, string.Join("\n", r.Issues.ConvertAll(i => i.Message)));
        }

        static Vector3[] Positions(FormationResult f)
        {
            var p = new Vector3[f.Slots.Length];
            for (int i = 0; i < p.Length; i++) p[i] = f.Slots[i].Position;
            return p;
        }
    }
}
