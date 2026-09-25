using DroneStar.Core;
using NUnit.Framework;

namespace DroneStar.Tests
{
    public class ShowMathTests
    {
        [Test]
        public void MinimumJerkStartsAndEndsAtRest()
        {
            Assert.That(ShowMath.MinimumJerk(0f), Is.EqualTo(0f));
            Assert.That(ShowMath.MinimumJerk(1f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(ShowMath.MinimumJerk(0.5f), Is.EqualTo(0.5f).Within(1e-6f));
            const float h = 1e-3f;
            Assert.That((ShowMath.MinimumJerk(h) - ShowMath.MinimumJerk(0f)) / h, Is.LessThan(1e-3f));
            Assert.That((ShowMath.MinimumJerk(1f) - ShowMath.MinimumJerk(1f - h)) / h, Is.LessThan(1e-3f));
        }

        [Test]
        public void PeakFactorsMatchTheProfile()
        {
            double maxV = 0, maxA = 0;
            const double h = 1e-4;
            for (double x = h; x < 1 - h; x += h)
            {
                double s0 = Profile(x - h), s1 = Profile(x), s2 = Profile(x + h);
                maxV = System.Math.Max(maxV, (s2 - s0) / (2 * h));
                maxA = System.Math.Max(maxA, System.Math.Abs(s2 - 2 * s1 + s0) / (h * h));
            }
            Assert.That(maxV, Is.EqualTo(ShowMath.PeakVelocityFactor).Within(1e-3));
            Assert.That(maxA, Is.EqualTo(ShowMath.PeakAccelerationFactor).Within(1e-2));
        }

        static double Profile(double x) => x * x * x * (10 - 15 * x + 6 * x * x);

        [Test]
        public void MinimumJerkDurationRespectsBothLimits()
        {
            float d = 50f;
            float t = ShowMath.MinimumJerkDuration(d, 8f, 4f);
            Assert.That(ShowMath.PeakVelocityFactor * d / t, Is.LessThanOrEqualTo(8f + 1e-4f));
            Assert.That(ShowMath.PeakAccelerationFactor * d / (t * t), Is.LessThanOrEqualTo(4f + 1e-4f));
            Assert.That(ShowMath.MinimumJerkDuration(0f, 8f, 4f), Is.EqualTo(0f));
            Assert.That(ShowMath.MinimumJerkDuration(float.NaN, 8f, 4f), Is.EqualTo(0f));
        }

        [Test]
        public void MotionClockIsContinuousAndEasesAtBothEnds()
        {
            const float hold = 10f, ramp = 3f;
            float previous = 0f;
            for (float t = 0.01f; t <= hold; t += 0.01f)
            {
                float v = ShowMath.MotionClock(t, hold, ramp);
                Assert.That(v - previous, Is.LessThanOrEqualTo(0.0101f), "clock never runs faster than real time");
                Assert.That(v, Is.GreaterThanOrEqualTo(previous - 1e-5f), "clock is monotonic");
                previous = v;
            }
            Assert.That(ShowMath.MotionClock(hold, hold, ramp), Is.EqualTo(hold - ramp).Within(1e-4f));
            Assert.That(ShowMath.MotionClock(0.001f, hold, ramp), Is.LessThan(1e-6f));
            float nearEnd = ShowMath.MotionClock(hold - 0.001f, hold, ramp);
            Assert.That(hold - ramp - nearEnd, Is.LessThan(1e-5f));
            Assert.That(ShowMath.MotionClock(5f, 0f, ramp), Is.EqualTo(0f));
        }

        [Test]
        public void ClosestApproachFindsCrossingBetweenSamples()
        {
            var a0 = new System.Numerics.Vector3(-5, 0, 0);
            var a1 = new System.Numerics.Vector3(5, 0, 0);
            var b0 = new System.Numerics.Vector3(5, 0.3f, 0);
            var b1 = new System.Numerics.Vector3(-5, 0.3f, 0);
            Assert.That(ShowMath.ClosestApproach(a0, a1, b0, b1), Is.EqualTo(0.3f).Within(1e-4f));
        }

        [Test]
        public void HashIsDeterministicAndInRange()
        {
            for (int i = 0; i < 1000; i++)
            {
                float h = ShowMath.Hash01(i, 3);
                Assert.That(h, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                Assert.That(ShowMath.Hash01(i, 3), Is.EqualTo(h));
            }
        }

        [Test]
        public void ColourHexRoundTrips()
        {
            LedColor c = LedColor.FromHex("#FF8A00");
            Assert.That(c.ToHex(), Is.EqualTo("#FF8A00"));
            Assert.That(LedColor.TryParseHex("zz", out _), Is.False);
            Assert.That(LedColor.TryParseHex("12345G", out _), Is.False);
            Assert.That(LedColor.FromHsv(0f, 1f, 1f).ToHex(), Is.EqualTo("#FF0000"));
            Assert.That(LedColor.FromHsv(1f / 3f, 1f, 1f).ToHex(), Is.EqualTo("#00FF00"));
            Assert.That(LedColor.FromHsv(-1f / 3f, 1f, 1f).ToHex(), Is.EqualTo("#0000FF"));
        }
    }
}
