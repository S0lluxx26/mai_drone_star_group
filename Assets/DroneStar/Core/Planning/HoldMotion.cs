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
            else if (motion.Kind == MotionKind.Wave || motion.Kind == MotionKind.Rise) margin = Math.Max(0f, motion.Amount);
            // A wing point at radius r from its shoulder moves at most 2·r·sin(θ/2); r stays within the shape.
            else if (motion.Kind == MotionKind.Flap) growth = 2f * MathF.Sin(0.5f * Math.Max(0f, motion.Amount) * ShowMath.Deg2Rad);
        }

        /// <summary>Hold position of a slot. Parked (dark) slots never move.</summary>
        public static Vector3 Evaluate(MotionSpec motion, FormationResult frame, in Slot slot, float localTime, float hold)
        {
            if (slot.Dark) return slot.Position;
            if (motion != null && motion.Kind == MotionKind.Flap) return Flap(motion, frame, slot, localTime, hold);
            return Evaluate(motion, frame, slot.Position, localTime, hold);
        }

        /// <summary>
        /// Wingbeat: each wing turns rigidly about its shoulder in the formation's plane, both tips rising together.
        /// Rigid wings keep their own spacing, and the models leave a gap between wing roots and body.
        /// </summary>
        static Vector3 Flap(MotionSpec motion, FormationResult frame, in Slot slot, float localTime, float hold)
        {
            if (frame == null || !frame.HasWings || slot.Group == 0 || !(hold > 0f)) return slot.Position;
            // The beat starts at the resting wing (sin 0) and the motion clock eases in and out, so no amplitude
            // envelope is needed; one would add its own acceleration on wings a hundred metres long.
            float tau = ShowMath.MotionClock(localTime, hold, RampSeconds);
            float angle = motion.Amount * ShowMath.Deg2Rad * MathF.Sin(ShowMath.TwoPi * motion.FrequencyHz * tau);
            float side = slot.Group == 2 ? 1f : -1f;
            float hx = frame.WingHinge.X * side, hy = frame.WingHinge.Y;
            float dx = slot.Local.X - hx, dy = slot.Local.Y - hy;
            float a = angle * side, c = MathF.Cos(a), s = MathF.Sin(a);
            float x = hx + c * dx - s * dy, y = hy + s * dx + c * dy;
            float h = frame.HalfSize;
            return frame.Center + frame.Right * (x * h) + frame.Up * (y * h) + frame.Normal * (slot.Local.Z * h);
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
                case MotionKind.Rise:
                {
                    // The motion clock runs 0 → hold − ramp, so this climbs Amount metres, easing in and out.
                    float travel = Math.Max(hold - Math.Min(RampSeconds, hold * 0.5f), 1e-3f);
                    return basePosition + Vector3.UnitY * (motion.Amount * ShowMath.Clamp01(tau / travel));
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
        public static float PeakSpeed(MotionSpec motion, FormationResult frame, float hold = 8f)
        {
            if (motion == null || frame == null) return 0f;
            float radius = 0f;
            if (motion.Kind == MotionKind.Flap)
            {
                if (!frame.HasWings) return 0f;
                foreach (Slot s in frame.Slots)
                {
                    if (s.Dark || s.Group == 0) continue;
                    float side = s.Group == 2 ? 1f : -1f;
                    var d = new Vector2(s.Local.X - frame.WingHinge.X * side, s.Local.Y - frame.WingHinge.Y);
                    radius = Math.Max(radius, d.Length() * frame.HalfSize);
                }
                // The envelope and motion clock add up to about a third on top of the plain sine's peak.
                return 1.35f * motion.Amount * ShowMath.Deg2Rad * ShowMath.TwoPi * motion.FrequencyHz * radius;
            }
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
                case MotionKind.Rise:
                    return 1.5f * motion.Amount / Math.Max(hold - Math.Min(RampSeconds, hold * 0.5f), 1e-3f);
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
