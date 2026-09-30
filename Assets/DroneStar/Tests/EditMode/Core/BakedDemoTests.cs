using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    /// <summary>
    /// The pre-solved transitions shipped with the flagship (Assets/DroneStar/Data/DemoAssignments.bytes).
    /// Re-bake after changing formations, the flagship or FormationGenerator.Revision:
    /// <c>dotnet test tests/DroneStar.Core.Tests --filter Name=BakeDemoAssignments</c>.
    /// </summary>
    public class BakedDemoTests
    {
        const string CachePath = "Assets/DroneStar/Data/DemoAssignments.bytes";

        [Test, Explicit("Writes " + CachePath)]
        public void BakeDemoAssignments()
        {
            var compiler = new ShowCompiler();
            foreach (ShowDocument doc in DemoShows.PresolvedShows()) compiler.Compile(doc);
            string pack = TestData.Find("Assets/DroneStar/Data/ShapePack.bytes");
            string path = Path.Combine(Path.GetDirectoryName(pack), Path.GetFileName(CachePath));
            File.WriteAllBytes(path, compiler.ExportCache());
            TestContext.WriteLine(compiler.PortableCacheCount + " transitions → " + path);
        }

        [Test]
        public void BakedAssignmentsCoverEveryPresolvedShow()
        {
            var compiler = new ShowCompiler();
            int imported = compiler.ImportCache(File.ReadAllBytes(TestData.Find(CachePath)));
            int expected = 0;
            foreach (ShowDocument doc in DemoShows.PresolvedShows())
            {
                expected += doc.Cues.Count + 1;
                // Only the transitions are looked up, so layouts are all this needs to generate.
                CompileJob job = compiler.Begin(doc);
                int missesBefore = compiler.CacheMisses;
                while (!job.Step())
                {
                    Assert.That(compiler.CacheMisses, Is.EqualTo(missesBefore),
                        doc.DroneCount + " drones: a transition is missing from the baked cache. Re-bake it (see class notes).");
                }
            }
            Assert.That(imported, Is.EqualTo(expected));
        }

        [Test, Timeout(900000)] // Four fleets up to 8,192 drones, each compiled and flown in full: minutes under Mono.
        public void EveryPresolvedFleetLightsEveryDroneAndFliesSafely()
        {
            // These are the shows the fleet presets open, flown with the baked assignments the app uses.
            var compiler = new ShowCompiler();
            compiler.ImportCache(File.ReadAllBytes(TestData.Find(CachePath)));
            foreach (ShowDocument doc in DemoShows.PresolvedShows())
            {
                CompiledShow show = compiler.Compile(doc);
                foreach (CueTiming t in show.CueTimings)
                {
                    Assert.That(t.Formation.LitCount, Is.EqualTo(doc.DroneCount), doc.DroneCount + " drones: " + doc.Cues[t.CueIndex].Name);
                }
                ValidationReport r = SafetyValidator.Run(show, 0.1f);
                string findings = doc.DroneCount + " drones:\n" + string.Join("\n", r.Issues.ConvertAll(i => i.Severity + ": " + i.Message));
                Assert.That(r.ErrorCount, Is.EqualTo(0), findings);
                Assert.That(r.WarningCount, Is.EqualTo(0), findings);
                Assert.That(r.MinSeparation, Is.GreaterThanOrEqualTo(doc.Limits.MinSeparation), findings);
                Assert.That(show.Duration, Is.LessThan(doc.Limits.MaxFlightSeconds), doc.DroneCount + " drones fit the battery");
                // Demo 2 fires the stage flames, so its safety check also measures the drones' distance from them.
                Assert.That(r.FlamesArmed, Is.EqualTo(doc.Venue == ShowVenue.Festival), doc.Title);
                TestContext.WriteLine(string.Format("{0} ({1} drones): {2:0.0} s, closest {3:0.00} m, top speed {4:0.0} m/s, peak accel {5:0.0} m/s², ceiling {6:0} m, flames {7}",
                    doc.Title, doc.DroneCount, show.Duration, r.MinSeparation, r.MaxSpeed, r.MaxAcceleration, r.MaxAltitude,
                    r.FlamesArmed ? (float.IsPositiveInfinity(r.MinFlameClearance) ? "> " + r.FlameSearchRadius + " m" : r.MinFlameClearance.ToString("0.0") + " m") : "none"));
            }
            Assert.That(compiler.CacheMisses, Is.EqualTo(0), "every transition came from the baked file");
        }

        [Test]
        public void StaleBakedEntriesAreResolvedNotTrusted()
        {
            // A baked entry whose recorded cost no longer matches today's geometry (what a layout change without
            // a re-bake produces) must be re-solved, not trusted. Fake that by altering the recorded costs.
            ShowDocument doc = DemoShows.Blank();
            doc.DroneCount = 700;
            var baker = new ShowCompiler();
            baker.Compile(doc);
            byte[] bake = baker.ExportCache();
            int pos = 10;
            while (pos < bake.Length)
            {
                uint n = BitConverter.ToUInt32(bake, pos + 8);
                byte[] cost = BitConverter.GetBytes(BitConverter.ToDouble(bake, pos + 12) * 1.01);
                Array.Copy(cost, 0, bake, pos + 12, 8);
                pos += 20 + (int)n * 2;
            }
            var fresh = new ShowCompiler();
            Assert.That(fresh.ImportCache(bake), Is.EqualTo(2));
            fresh.Compile(doc);
            Assert.That(fresh.CacheHits, Is.EqualTo(0), "entries whose cost no longer matches are not used");
        }

        [Test]
        public void BakedAssignmentsAreStillOptimal()
        {
            // A layout change without a re-bake would leave the baked permutations pointing at the old slots:
            // compare the first transitions of the 2,048-drone preset against fresh solves.
            ShowDocument doc = null;
            foreach (ShowDocument d in DemoShows.PresolvedShows()) if (d.DroneCount == 2048) doc = d;
            doc.Cues.RemoveRange(3, doc.Cues.Count - 3);
            var baked = new ShowCompiler();
            baked.ImportCache(File.ReadAllBytes(TestData.Find(CachePath)));
            CompiledShow fromCache = baked.Compile(doc);
            Assert.That(baked.CacheHits, Is.EqualTo(3), "takeoff and two scene changes come from the baked cache");
            CompiledShow fresh = new ShowCompiler().Compile(doc);

            double[] a = TransitCosts(fromCache), b = TransitCosts(fresh);
            Assert.That(a.Length, Is.EqualTo(b.Length));
            for (int k = 0; k < 3; k++)
            {
                // Solves may settle on different near-optimal answers (within ~0.1 %); a stale bake is off by far more.
                Assert.That(Math.Abs(a[k] - b[k]) / b[k], Is.LessThan(3e-3),
                    "transition " + k + " no longer matches the layouts. Bump FormationGenerator.Revision and re-bake.");
            }
        }

        /// <summary>Sum of squared straight-line moves per transit (the quantity the assignment minimises).</summary>
        static double[] TransitCosts(CompiledShow show)
        {
            var costs = new List<double>();
            var start = new Vector3[show.DroneCount];
            var end = new Vector3[show.DroneCount];
            foreach (ShowSegment seg in show.Segments)
            {
                if (seg.Kind != SegmentKind.Transit) continue;
                show.SamplePositions(seg.Start + 1e-4f, start);
                show.SamplePositions(seg.End - 1e-4f, end);
                double sum = 0;
                for (int i = 0; i < start.Length; i++) sum += Vector3.DistanceSquared(start[i], end[i]);
                costs.Add(sum);
            }
            return costs.ToArray();
        }
    }
}
