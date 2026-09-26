using System;
using System.Numerics;
using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class CompilerTests
    {
        static ShowDocument SmallShow(int drones = 60)
        {
            var doc = new ShowDocument { Title = "Test", DroneCount = drones };
            doc.Cues.Add(new Cue { Name = "Sphere", HoldSeconds = 4f, Formation = new FormationSpec { Kind = FormationKind.Sphere, Size = 30f } });
            doc.Cues.Add(new Cue
            {
                Name = "Star",
                HoldSeconds = 5f,
                Formation = new FormationSpec { Kind = FormationKind.Star, Size = 40f },
                Motion = new MotionSpec { Kind = MotionKind.Roll, DegreesPerSecond = 10f },
                Light = new LightSpec { Effect = LightEffect.Chase },
            });
            doc.Cues.Add(new Cue { Name = "Heart", HoldSeconds = 0f, Formation = new FormationSpec { Kind = FormationKind.Heart, Size = 40f } });
            return doc;
        }

        [Test]
        public void TimelineIsContiguousAndStartsAndEndsOnThePads()
        {
            CompiledShow show = new ShowCompiler().Compile(SmallShow());
            Assert.That(show.Segments[0].Start, Is.EqualTo(0f));
            for (int i = 1; i < show.Segments.Count; i++)
            {
                Assert.That(show.Segments[i].Start, Is.EqualTo(show.Segments[i - 1].End));
                Assert.That(show.Segments[i].Duration, Is.GreaterThan(0f));
            }
            Assert.That(show.Duration, Is.EqualTo(show.Segments[show.Segments.Count - 1].End));

            var p = new Vector3[show.DroneCount];
            show.SamplePositions(0f, p);
            for (int i = 0; i < p.Length; i++) Assert.That(p[i], Is.EqualTo(show.PadPositions[i]));
            show.SamplePositions(show.Duration, p);
            var landed = new bool[p.Length];
            foreach (Vector3 q in p)
            {
                int pad = Array.IndexOf(show.PadPositions, q);
                Assert.That(pad, Is.GreaterThanOrEqualTo(0), "every drone ends on a pad");
                Assert.That(landed[pad], Is.False, "one drone per pad");
                landed[pad] = true;
            }
        }

        [Test]
        public void PositionsAreContinuousAcrossSegmentBoundaries()
        {
            CompiledShow show = new ShowCompiler().Compile(SmallShow());
            int n = show.DroneCount;
            var before = new Vector3[n];
            var after = new Vector3[n];
            foreach (ShowSegment seg in show.Segments)
            {
                if (seg.Start <= 0f) continue;
                show.SamplePositions(seg.Start - 1e-3f, before);
                show.SamplePositions(seg.Start + 1e-3f, after);
                for (int i = 0; i < n; i++)
                {
                    Assert.That(Vector3.Distance(before[i], after[i]), Is.LessThan(0.02f), seg.Kind + " at " + seg.Start + " drone " + i);
                }
            }
        }

        [Test]
        public void EveryCueHoldsItsFormation()
        {
            CompiledShow show = new ShowCompiler().Compile(SmallShow());
            var p = new Vector3[show.DroneCount];
            CueTiming sphere = show.CueTimings[0];
            show.SamplePositions(sphere.HoldStart + 0.001f, p);
            for (int i = 0; i < p.Length; i++)
            {
                Slot slot = sphere.Formation.Slots[show.SlotOf(0, i)];
                Assert.That(Vector3.Distance(p[i], slot.Position), Is.LessThan(0.01f));
            }
            // Every slot of every cue is used by exactly one drone.
            for (int c = 0; c < show.CueTimings.Count; c++)
            {
                var used = new bool[show.DroneCount];
                for (int i = 0; i < show.DroneCount; i++)
                {
                    int s = show.SlotOf(c, i);
                    Assert.That(used[s], Is.False);
                    used[s] = true;
                }
            }
        }

        [Test]
        public void AutoTransitionsRespectTheLimits()
        {
            ShowDocument doc = SmallShow();
            CompiledShow show = new ShowCompiler().Compile(doc);
            foreach (CueTiming t in show.CueTimings)
            {
                float peakSpeed = ShowMath.PeakVelocityFactor * t.LongestMove / t.TransitSeconds;
                float peakAccel = ShowMath.PeakAccelerationFactor * t.LongestMove / (t.TransitSeconds * t.TransitSeconds);
                Assert.That(peakSpeed, Is.LessThanOrEqualTo(doc.Limits.MaxSpeed));
                Assert.That(peakAccel, Is.LessThanOrEqualTo(doc.Limits.MaxAcceleration));
                Assert.That(t.TransitSeconds, Is.GreaterThanOrEqualTo(ShowCompiler.MinimumTransition));
            }
        }

        [Test]
        public void ManualTransitionTimeIsHonoured()
        {
            ShowDocument doc = SmallShow();
            doc.Cues[1].AutoTransition = false;
            doc.Cues[1].TransitionSeconds = 30f;
            CompiledShow show = new ShowCompiler().Compile(doc);
            Assert.That(show.CueTimings[1].TransitSeconds, Is.EqualTo(30f).Within(1e-3f));
        }

        [Test]
        public void EditingOneCueOnlyResolvesItsTransitions()
        {
            var compiler = new ShowCompiler();
            ShowDocument doc = SmallShow();
            compiler.Compile(doc);
            Assert.That(compiler.CacheMisses, Is.EqualTo(4));
            compiler.Compile(doc);
            Assert.That(compiler.CacheMisses, Is.EqualTo(4), "unchanged show is fully cached");
            doc.Cues[2].Light.Effect = LightEffect.Rainbow;
            compiler.Compile(doc);
            Assert.That(compiler.CacheMisses, Is.EqualTo(4), "light edits do not touch flight paths");
            doc.Cues[2].Formation.Size = 44f;
            compiler.Compile(doc);
            Assert.That(compiler.CacheMisses, Is.EqualTo(6), "resizing the last cue re-plans the way in and the way home");
        }

        [Test]
        public void IncrementalJobReportsProgress()
        {
            CompileJob job = new ShowCompiler().Begin(SmallShow());
            float last = -1f;
            int guard = 0;
            while (!job.Step())
            {
                Assert.That(job.Progress, Is.GreaterThan(last));
                last = job.Progress;
                Assert.That(++guard, Is.LessThan(100));
            }
            Assert.That(job.Progress, Is.EqualTo(1f));
            Assert.That(job.Result, Is.Not.Null);
            Assert.That(job.Step(), Is.True);
        }

        [Test]
        public void CompilingDoesNotMutateTheSource()
        {
            ShowDocument doc = SmallShow();
            doc.DroneCount = -5;
            string before = ShowSerializer.ToJson(doc);
            CompiledShow show = new ShowCompiler().Compile(doc);
            Assert.That(ShowSerializer.ToJson(doc), Is.EqualTo(before), "the compiler sanitises its own copy");
            Assert.That(show.DroneCount, Is.EqualTo(ShowBounds.MinDrones));
        }

        [Test]
        public void ShowWithoutCuesTakesOffAndLands()
        {
            var doc = new ShowDocument { DroneCount = 12 };
            CompiledShow show = new ShowCompiler().Compile(doc);
            Assert.That(show.CueTimings.Count, Is.EqualTo(0));
            ValidationReport report = SafetyValidator.Run(show);
            Assert.That(report.Passed, Is.True);
        }

        [Test]
        public void SamplingOutsideTheShowClamps()
        {
            CompiledShow show = new ShowCompiler().Compile(SmallShow(10));
            var p = new Vector3[10];
            var c = new LedColor[10];
            show.Sample(-5f, p, c);
            show.Sample(show.Duration + 100f, p, c);
            show.Sample(float.NaN, p, c);
            foreach (Vector3 v in p) Assert.That(ShowMath.IsFinite(v), Is.True);
            Assert.Throws<ArgumentException>(() => show.Sample(0f, new Vector3[3], null));
        }

        [Test]
        public void ColoursStayInRangeAndParkedDronesAreDark()
        {
            ShowDocument doc = SmallShow(200);
            doc.Cues[2].Formation.Size = 10f;
            doc.Cues[2].HoldSeconds = 2f;
            CompiledShow show = new ShowCompiler().Compile(doc);
            var p = new Vector3[200];
            var c = new LedColor[200];
            for (float t = 0f; t <= show.Duration; t += 0.37f)
            {
                show.Sample(t, p, c);
                foreach (LedColor col in c)
                {
                    Assert.That(col.R, Is.InRange(0f, 1f));
                    Assert.That(col.G, Is.InRange(0f, 1f));
                    Assert.That(col.B, Is.InRange(0f, 1f));
                }
            }
            CueTiming heart = show.CueTimings[2];
            Assert.That(heart.Formation.DarkCount, Is.GreaterThan(0));
            show.Sample(heart.HoldStart + 0.01f, p, c);
            for (int i = 0; i < 200; i++)
            {
                if (heart.Formation.Slots[show.SlotOf(2, i)].Dark) Assert.That(c[i].MaxComponent, Is.EqualTo(0f));
            }
        }
    }
}
