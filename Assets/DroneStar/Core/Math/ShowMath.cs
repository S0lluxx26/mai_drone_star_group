using System;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>Numeric helpers shared by the planner, the light engine and the safety validator.</summary>
    public static class ShowMath
    {
        /// <summary>Peak of ds/dx for the minimum-jerk profile (reached at x = 0.5).</summary>
        public const float PeakVelocityFactor = 1.875f;

        /// <summary>Peak of |d²s/dx²| for the minimum-jerk profile, 10/√3 (reached at x = (3 ± √3)/6).</summary>
        public const float PeakAccelerationFactor = 5.773503f;

        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float TwoPi = (float)(Math.PI * 2.0);

        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        public static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);

        public static int Clamp(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);

        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Fractional part in [0, 1), also for negative inputs.</summary>
        public static float Frac(float x) => x - MathF.Floor(x);

        /// <summary>
        /// Minimum-jerk position profile s(x) = 10x³ − 15x⁴ + 6x⁵ on [0, 1].
        /// Velocity and acceleration are zero at both ends, so consecutive segments join smoothly.
        /// </summary>
        public static float MinimumJerk(float x)
        {
            x = Clamp01(x);
            return x * x * x * (10f + x * (-15f + 6f * x));
        }

        public static float Smoothstep(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Shortest time that moves a drone <paramref name="distance"/> metres along a minimum-jerk
        /// profile without exceeding the speed or acceleration limit.
        /// </summary>
        public static float MinimumJerkDuration(float distance, float maxSpeed, float maxAcceleration)
        {
            if (!(distance > 0f)) return 0f;
            float bySpeed = PeakVelocityFactor * distance / Math.Max(maxSpeed, 1e-3f);
            float byAcceleration = MathF.Sqrt(PeakAccelerationFactor * distance / Math.Max(maxAcceleration, 1e-3f));
            return Math.Max(bySpeed, byAcceleration);
        }

        /// <summary>
        /// Motion clock for a hold of length <paramref name="hold"/>: advances like real time in the middle
        /// but eases in and out over <paramref name="ramp"/> seconds, so a spinning or waving formation
        /// starts and stops with zero velocity and zero acceleration.
        /// </summary>
        public static float MotionClock(float t, float hold, float ramp)
        {
            if (!(hold > 0f) || !(t > 0f)) return 0f;
            float r = Math.Min(ramp, hold * 0.5f);
            if (!(r > 0f)) return Math.Min(t, hold);
            if (t < r)
            {
                float x = t / r;
                return r * (x * x * x - 0.5f * x * x * x * x);
            }
            float total = hold - r;
            if (t <= hold - r) return 0.5f * r + (t - r);
            if (t >= hold) return total;
            float y = (hold - t) / r;
            return total - r * (y * y * y - 0.5f * y * y * y * y);
        }

        /// <summary>Integer hash with good avalanche (lowbias32).</summary>
        public static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        /// <summary>Deterministic pseudo-random value in [0, 1) for a pair of integers.</summary>
        public static float Hash01(int a, int b)
        {
            uint h = Hash(unchecked((uint)a * 0x9E3779B9U) ^ Hash(unchecked((uint)b + 0x632BE5ABU)));
            return (h >> 8) * (1f / 16777216f);
        }

        public static Quaternion Orientation(float yawDegrees, float pitchDegrees)
        {
            return Quaternion.CreateFromYawPitchRoll(yawDegrees * Deg2Rad, pitchDegrees * Deg2Rad, 0f);
        }

        public static bool IsFinite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        public static bool IsFinite(Vector3 v) => IsFinite(v.X) && IsFinite(v.Y) && IsFinite(v.Z);

        /// <summary>Closest distance between two points that each move linearly over the same interval.</summary>
        public static float ClosestApproach(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
        {
            Vector3 r0 = b0 - a0;
            Vector3 dr = (b1 - a1) - r0;
            float dd = dr.LengthSquared();
            float s = dd > 1e-12f ? Clamp01(-Vector3.Dot(r0, dr) / dd) : 0f;
            return (r0 + dr * s).Length();
        }
    }
}
