using System;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Keeps a show's look when its fleet size changes. Filled shapes need area in proportion to the drone
    /// count at a fixed spacing, so every formation grows by √(new / old): sizes scale about the formation
    /// centre, and centres scale about the launch pad so bigger shows also fly higher and further out.
    /// Spins and breathing slow down by the same factor, so the outer drones of a bigger shape keep the same
    /// speed and acceleration; climbs grow with the shape. Safety limits are raised (never lowered) to fit the
    /// new envelope.
    /// </summary>
    public static class ShowScaler
    {
        public static void ResizeForDroneCount(ShowDocument doc, int newCount)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            newCount = ShowMath.Clamp(newCount, ShowBounds.MinDrones, ShowBounds.MaxDrones);
            int oldCount = Math.Max(doc.DroneCount, ShowBounds.MinDrones);
            doc.DroneCount = newCount;
            if (newCount == oldCount) return;
            float factor = MathF.Sqrt(newCount / (float)oldCount);
            Vector3 pad = doc.Pad.Center;
            float highest = 0f, furthest = 0f;
            foreach (Cue cue in doc.Cues)
            {
                if (cue?.Formation == null) continue;
                FormationSpec f = cue.Formation;
                f.Size = ShowMath.Clamp(f.Size * factor, ShowBounds.MinSize, ShowBounds.MaxSize);
                Vector3 offset = f.Center - pad;
                // Altitude grows from the hover height, so shapes stay clear of the barge at any scale.
                float hover = doc.Pad.HoverAltitude;
                float y = hover + (offset.Y - hover) * factor;
                var center = new Vector3(pad.X + offset.X * factor, Math.Max(y, f.Size * 0.6f + hover), pad.Z + offset.Z * factor);
                f.Center = center;
                highest = Math.Max(highest, center.Y + f.Size * 0.6f);
                furthest = Math.Max(furthest, new Vector2(center.X - pad.X, center.Z - pad.Z).Length() + f.Size * 0.6f);
                MotionSpec m = cue.Motion;
                if (m == null) continue;
                if (m.Kind == MotionKind.Rise) m.Amount *= factor;
                // Edge speed is ω·r and acceleration ω²·r. Growing r by the factor while slowing ω by it keeps
                // the speed and lowers the acceleration; shrinking keeps ω, which lowers both. Either way a show
                // that was legal stays legal.
                if (factor > 1f && (m.Kind == MotionKind.Turntable || m.Kind == MotionKind.Roll)) m.DegreesPerSecond /= factor;
                if (factor > 1f && m.Kind == MotionKind.Breathe) m.FrequencyHz /= factor;
            }
            SafetyLimits l = doc.Limits;
            l.MaxAltitude = Math.Max(l.MaxAltitude, MathF.Ceiling((highest + 20f) / 10f) * 10f);
            float padReach = MathF.Sqrt(newCount) * doc.Pad.Spacing * 0.75f;
            l.GeofenceRadius = Math.Max(l.GeofenceRadius, MathF.Ceiling((Math.Max(furthest, padReach) + 30f) / 10f) * 10f);
            ShowSanitizer.Sanitize(doc);
        }
    }
}
