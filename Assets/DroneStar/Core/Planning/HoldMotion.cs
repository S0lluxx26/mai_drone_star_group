using System;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Animates a formation while it holds. Every motion is driven by <see cref="ShowMath.MotionClock"/>,
    /// so it starts and stops with zero velocity and joins the surrounding transitions smoothly.
    /// </summary>
    public static class HoldMotion
    {
        /// <summary>Seconds used to ease a motion in and out (shortened for holds under 2 × this).</summary>
        public const float RampSeconds = 3f;

        /// <summary>Clearance the formation needs for its motion, as (growth fraction, extra metres).</summary>
        public static void Envelope(MotionSpec motion, out float growth, out float margin)
        {
            growth = 0f;
            margin = 0f;
            if (motion == null) return;
            if (motion.Kind == MotionKind.Breathe) growth = Math.Max(0f, motion.Amount);
            else if (motion.Kind == MotionKind.Wave) margin = Math.Max(0f, motion.Amount);
        }

        /// <summary>Hold position of a slot. Parked (dark) slots never move.</summary>
        public static Vector3 Evaluate(MotionSpec motion, FormationResult frame, in Slot slot, float localTime, float hold)
        {
            return slot.Dark ? slot.Position : Evaluate(motion, frame, slot.Position, localTime, hold);
        }

        public static Vector3 Evaluate(MotionSpec motion, FormationResult frame, Vector3 basePosition, float localTime, float hold)
        {
            if (motion == null || frame == null || motion.Kind == MotionKind.None || !(hold > 0f)) return basePosition;
            float tau = ShowMath.MotionClock(localTime, hold, RampSeconds);
            Vector3 offset = basePosition - frame.Center;
            switch (motion.Kind)
            {
                case MotionKind.Turntable:
                    return frame.Center + Rotate(offset, Vector3.UnitY, motion.DegreesPerSecond * ShowMath.Deg2Rad * tau);
                case MotionKind.Roll:
                    return frame.Center + Rotate(offset, frame.Normal, motion.DegreesPerSecond * ShowMath.Deg2Rad * tau);
                case MotionKind.Breathe:
                {
                    float grow = motion.Amount * 0.5f * (1f - MathF.Cos(ShowMath.TwoPi * motion.FrequencyHz * tau));
                    return frame.Center + offset * (1f + grow);
                }
                case MotionKind.Wave:
                {
                    float ramp = Math.Min(RampSeconds, hold * 0.5f);
                    // Minimum-jerk envelope: the ripple starts with zero velocity *and* zero acceleration.
                    float envelope = ShowMath.MinimumJerk(localTime / ramp);
                    float x = Vector3.Dot(offset, frame.Right) / Math.Max(frame.HalfSize, 1e-3f);
                    float phase = ShowMath.TwoPi * (motion.FrequencyHz * tau - 0.5f * x);
                    return basePosition + frame.Normal * (motion.Amount * envelope * MathF.Sin(phase));
                }
                default:
                    return basePosition;
            }
        }

        /// <summary>Upper bound on the extra speed a hold motion adds to a slot, used for inspector hints.</summary>
        public static float PeakSpeed(MotionSpec motion, FormationResult frame)
        {
            if (motion == null || frame == null) return 0f;
            float radius = 0f;
            foreach (Slot s in frame.Slots)
            {
                if (s.Dark) continue;
                Vector3 offset = s.Position - frame.Center;
                if (motion.Kind == MotionKind.Turntable) offset.Y = 0f;
                else if (motion.Kind == MotionKind.Roll) offset -= frame.Normal * Vector3.Dot(offset, frame.Normal);
                radius = Math.Max(radius, offset.Length());
            }
            switch (motion.Kind)
            {
                case MotionKind.Turntable:
                case MotionKind.Roll:
                    return Math.Abs(motion.DegreesPerSecond) * ShowMath.Deg2Rad * radius;
                case MotionKind.Breathe:
                    return motion.Amount * 0.5f * ShowMath.TwoPi * motion.FrequencyHz * radius;
                case MotionKind.Wave:
                    return motion.Amount * ShowMath.TwoPi * motion.FrequencyHz;
                default:
                    return 0f;
            }
        }

        static Vector3 Rotate(Vector3 v, Vector3 axis, float angle)
        {
            return Vector3.Transform(v, Quaternion.CreateFromAxisAngle(axis, angle));
        }
    }
}
