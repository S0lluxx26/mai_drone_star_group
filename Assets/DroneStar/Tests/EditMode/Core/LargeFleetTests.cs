using System;
using System.Collections.Generic;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    /// <summary>Fleets up to 8,192 drones: model shapes, the auction solver, cache export and resizing.</summary>
    public class LargeFleetTests
    {
        [Test]
        public void ShapePackHasTheModels()
        {
            IReadOnlyList<ModelShape> shapes = ShapeLibrary.Shapes;
            Assert.That(shapes.Count, Is.EqualTo(14), "twelve from Draw_in_3D, the Lạc bird and the bronze drum");
            foreach (ModelShape m in shapes)
            {
                Assert.That(m.Points.Length, Is.EqualTo(3 * ShowBounds.MaxDrones), m.Name + ": a deep pool, three times the largest fleet");
                Assert.That(m.Colors.Length, Is.EqualTo(3 * ShowBounds.MaxDrones), m.Name);
                float extent = 0f;
                foreach (Vector3 p in m.Points) extent = Math.Max(extent, Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z))));
                Assert.That(extent, Is.EqualTo(1f).Within(0.01f), m.Name + " is normalised");
            }
            Assert.That(ShapeLibrary.TryGet("eiffel tower", out _), Is.True, "lookup ignores case");
        }

        [Test]
        public void LacBirdHasWingsThatBeatRigidly()
        {
            Assert.That(ShapeLibrary.TryGet("Lac bird", out ModelShape bird), Is.True);
            Assert.That(bird.HasWings, Is.True);
            Assert.That(ShapeLibrary.TryGet("Bronze drum", out ModelShape drum) && !drum.HasWings, Is.True);
            int left = 0, right = 0;
            foreach (byte g in bird.Groups)
            {
                if (g == 1) left++;
                if (g == 2) right++;
            }
            Assert.That(left, Is.GreaterThan(bird.Points.Length / 5));
            Assert.That(right, Is.EqualTo(left).Within(left / 20), "the wings mirror each other");

            var spec = new FormationSpec { Kind = FormationKind.Model, Model = "Lac bird", Size = 200f, Center = new Vector3(0, 120, 0) };
            FormationResult f = FormationGenerator.Generate(spec, 1500, 1.7f);
            Assert.That(f.HasWings, Is.True);
            var flap = new MotionSpec { Kind = MotionKind.Flap, Amount = 20f, FrequencyHz = 0.1f };
            float hold = 20f, t = 6f;
            int wing = -1, wing2 = -1, body = -1;
            float rightTip = float.MinValue;
            for (int i = 0; i < f.Slots.Length; i++)
            {
                Slot s = f.Slots[i];
                if (s.Dark) continue;
                if (s.Group == 0 && body < 0) body = i;
                if (s.Group == 2)
                {
                    if (wing < 0) wing = i;
                    else if (wing2 < 0) wing2 = i;
                    if (s.Local.X > rightTip) rightTip = s.Local.X;
                }
            }
            Slot a = f.Slots[wing], b = f.Slots[wing2], c = f.Slots[body];
            Vector3 a1 = HoldMotion.Evaluate(flap, f, a, t, hold), b1 = HoldMotion.Evaluate(flap, f, b, t, hold);
            Assert.That(Vector3.Distance(a1, a.Position), Is.GreaterThan(0.5f), "the wing moves");
            Assert.That(Vector3.Distance(a1, b1), Is.EqualTo(Vector3.Distance(a.Position, b.Position)).Within(1e-3f), "rigidly");
            Assert.That(HoldMotion.Evaluate(flap, f, c, t, hold), Is.EqualTo(c.Position), "the body holds still");
            Assert.That(HoldMotion.Evaluate(flap, f, a, 0f, hold), Is.EqualTo(a.Position), "it starts at rest");
            Assert.That(HoldMotion.PeakSpeed(flap, f, hold), Is.GreaterThan(1f));

            // Both tips rise together: mirror images stay mirror images.
            int l = -1, r = -1;
            for (int i = 0; i < f.Slots.Length && (l < 0 || r < 0); i++)
            {
                if (f.Slots[i].Dark) continue;
                if (f.Slots[i].Group == 1 && f.Slots[i].Local.X < -0.8f && l < 0) l = i;
                if (f.Slots[i].Group == 2 && f.Slots[i].Local.X > 0.8f && r < 0) r = i;
            }
            float riseL = HoldMotion.Evaluate(flap, f, f.Slots[l], t, hold).Y - f.Slots[l].Position.Y;
            float riseR = HoldMotion.Evaluate(flap, f, f.Slots[r], t, hold).Y - f.Slots[r].Position.Y;
            Assert.That(Math.Sign(riseL), Is.EqualTo(Math.Sign(riseR)), "both wings beat the same way");

            var sphere = FormationGenerator.Generate(new FormationSpec { Kind = FormationKind.Sphere, Size = 60f }, 200, 2f);
            Assert.That(HoldMotion.Evaluate(flap, sphere, sphere.Slots[3], t, hold), Is.EqualTo(sphere.Slots[3].Position), "shapes without wings hold still");
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
            Assert.That((cn - ce) / ce, Is.LessThan(2e-3), "auction is within 0.2 % of the optimum");
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
        public void GalaxyArmsGrowLanesForBigFleets()
        {
            // Arms are lines: their capacity grows with size, a fleet's with area. More lanes keep it lit.
            var spec = new FormationSpec { Kind = FormationKind.Galaxy, Points = 4, Size = 344f, Center = new Vector3(0, 220, 0) };
            Assert.That(FormationGenerator.Generate(spec, 8192, 1.7f).LitCount, Is.EqualTo(8192));
            spec.Size = 172f;
            Assert.That(FormationGenerator.Generate(spec, 2048, 1.7f).LitCount, Is.EqualTo(2048));
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

        [Test]
        public void SafetyCheckInSmallStepsMatchesOneShot()
        {
            // Big fleets check a sample's pairs a chunk of drones at a time; the result must not change.
            ShowDocument doc = DemoShows.Blank();
            ShowScaler.ResizeForDroneCount(doc, 2500);
            CompiledShow show = new ShowCompiler().Compile(doc);
            var stepped = new SafetyValidator(show, 0.25f);
            int steps = 0;
            while (!stepped.Step(1)) steps++;
            ValidationReport once = SafetyValidator.Run(show, 0.25f);
            Assert.That(steps, Is.GreaterThan(stepped.Report.Samples * 3), "every sample is split into several steps");
            Assert.That(stepped.Report.MinSeparation, Is.EqualTo(once.MinSeparation));
            Assert.That(stepped.Report.MaxSpeed, Is.EqualTo(once.MaxSpeed));
            Assert.That(stepped.Report.MaxAcceleration, Is.EqualTo(once.MaxAcceleration));
            Assert.That(stepped.Report.Issues.Count, Is.EqualTo(once.Issues.Count));
        }

        static Vector3[] Positions(FormationResult f)
        {
            var p = new Vector3[f.Slots.Length];
            for (int i = 0; i < p.Length; i++) p[i] = f.Slots[i].Position;
            return p;
        }
    }
}
