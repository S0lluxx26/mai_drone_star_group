using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Brings every field of a document back into its legal range. Loading a file and every editor
    /// change go through here, so the compiler never sees NaN, negative sizes or absurd counts.
    /// </summary>
    public static class ShowSanitizer
    {
        public static void Sanitize(ShowDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            doc.Title = CleanText(doc.Title, 64, "Untitled Show");
            doc.Author = CleanText(doc.Author, 64, "");
            doc.DroneCount = ShowMath.Clamp(doc.DroneCount, ShowBounds.MinDrones, ShowBounds.MaxDrones);
            if (!Enum.IsDefined(typeof(ShowVenue), doc.Venue)) doc.Venue = ShowVenue.Lake;
            doc.PreShowSeconds = Finite(doc.PreShowSeconds, 0f, 30f, 3f);
            doc.PostShowSeconds = Finite(doc.PostShowSeconds, 0f, 30f, 3f);

            doc.Limits = doc.Limits ?? new SafetyLimits();
            SafetyLimits l = doc.Limits;
            l.MinSeparation = Finite(l.MinSeparation, 0.5f, 10f, 1.5f);
            l.MaxSpeed = Finite(l.MaxSpeed, 1f, 30f, 8f);
            l.MaxAcceleration = Finite(l.MaxAcceleration, 0.5f, 20f, 4f);
            l.MaxAltitude = Finite(l.MaxAltitude, 10f, 500f, 120f);
            l.GeofenceRadius = Finite(l.GeofenceRadius, 10f, 1000f, 150f);
            l.MaxFlightSeconds = Finite(l.MaxFlightSeconds, 30f, 3600f, 900f);

            doc.Pad = doc.Pad ?? new LaunchPadSpec();
            LaunchPadSpec p = doc.Pad;
            p.Center = FiniteVector(p.Center, Vector3.Zero);
            p.Center = new Vector3(p.Center.X, 0f, p.Center.Z);
            p.Spacing = Finite(p.Spacing, 0.5f, 20f, 2.5f);
            p.HoverAltitude = Finite(p.HoverAltitude, 3f, 50f, 12f);
            p.Columns = ShowMath.Clamp(p.Columns, 0, ShowBounds.MaxDrones);

            doc.Cues = doc.Cues ?? new List<Cue>();
            doc.Cues.RemoveAll(c => c == null);
            if (doc.Cues.Count > ShowBounds.MaxCues) doc.Cues.RemoveRange(ShowBounds.MaxCues, doc.Cues.Count - ShowBounds.MaxCues);
            foreach (Cue cue in doc.Cues) SanitizeCue(cue);
        }

        public static void SanitizeCue(Cue cue)
        {
            cue.Name = CleanText(cue.Name, 40, "Cue");
            cue.TransitionSeconds = Finite(cue.TransitionSeconds, ShowBounds.MinTransition, ShowBounds.MaxTransition, 8f);
            cue.HoldSeconds = Finite(cue.HoldSeconds, 0f, ShowBounds.MaxHold, 8f);

            cue.Formation = cue.Formation ?? new FormationSpec();
            FormationSpec f = cue.Formation;
            if (!Enum.IsDefined(typeof(FormationKind), f.Kind)) f.Kind = FormationKind.Sphere;
            if (!Enum.IsDefined(typeof(FillStyle), f.Style)) f.Style = FillStyle.Filled;
            f.Center = FiniteVector(f.Center, new Vector3(0f, 60f, 0f));
            f.Center = new Vector3(
                ShowMath.Clamp(f.Center.X, -ShowBounds.MaxCoordinate, ShowBounds.MaxCoordinate),
                ShowMath.Clamp(f.Center.Y, 0f, ShowBounds.MaxCoordinate),
                ShowMath.Clamp(f.Center.Z, -ShowBounds.MaxCoordinate, ShowBounds.MaxCoordinate));
            f.Size = Finite(f.Size, ShowBounds.MinSize, ShowBounds.MaxSize, 40f);
            f.YawDegrees = Finite(f.YawDegrees, -ShowBounds.MaxAngle, ShowBounds.MaxAngle, 0f);
            f.PitchDegrees = Finite(f.PitchDegrees, -90f, 90f, 0f);
            f.Layers = ShowMath.Clamp(f.Layers, 1, ShowBounds.MaxLayers);
            f.Points = ShowMath.Clamp(f.Points, ShowBounds.MinPoints, ShowBounds.MaxPoints);
            f.Turns = Finite(f.Turns, ShowBounds.MinTurns, ShowBounds.MaxTurns, 2f);
            f.Text = CleanText(f.Text, ShowBounds.MaxTextLength, "MAI");
            f.Model = CleanText(f.Model, 40, "Robot");
            f.CustomPoints = f.CustomPoints ?? new List<Vector3>();
            f.CustomPoints.RemoveAll(v => !ShowMath.IsFinite(v));
            // Custom points are in normalised units (half-size = 1); keep them near the unit box.
            for (int i = 0; i < f.CustomPoints.Count; i++)
            {
                Vector3 v = f.CustomPoints[i];
                f.CustomPoints[i] = new Vector3(ShowMath.Clamp(v.X, -1.5f, 1.5f), ShowMath.Clamp(v.Y, -1.5f, 1.5f), ShowMath.Clamp(v.Z, -1.5f, 1.5f));
            }
            if (f.CustomPoints.Count > ShowBounds.MaxDrones) f.CustomPoints.RemoveRange(ShowBounds.MaxDrones, f.CustomPoints.Count - ShowBounds.MaxDrones);

            cue.Light = cue.Light ?? new LightSpec();
            LightSpec light = cue.Light;
            if (!Enum.IsDefined(typeof(LightEffect), light.Effect)) light.Effect = LightEffect.Solid;
            light.ColorA = CleanColor(light.ColorA);
            light.ColorB = CleanColor(light.ColorB);
            light.Speed = Finite(light.Speed, 0f, ShowBounds.MaxEffectSpeed, 1f);
            light.Brightness = Finite(light.Brightness, 0f, ShowBounds.MaxBrightness, 1f);
            light.AngleDegrees = Finite(light.AngleDegrees, -ShowBounds.MaxAngle, ShowBounds.MaxAngle, 90f);

            cue.Motion = cue.Motion ?? new MotionSpec();
            MotionSpec m = cue.Motion;
            if (!Enum.IsDefined(typeof(MotionKind), m.Kind)) m.Kind = MotionKind.None;
            m.DegreesPerSecond = Finite(m.DegreesPerSecond, -ShowBounds.MaxSpin, ShowBounds.MaxSpin, 12f);
            m.FrequencyHz = Finite(m.FrequencyHz, 0f, ShowBounds.MaxFrequency, 0.25f);
            float maxAmount = m.Kind == MotionKind.Breathe ? 0.5f : m.Kind == MotionKind.Wave ? 12f
                : m.Kind == MotionKind.Flap ? ShowBounds.MaxFlapDegrees : ShowBounds.MaxMotionAmount;
            m.Amount = Finite(m.Amount, 0f, maxAmount, 0.15f);
        }

        static float Finite(float value, float lo, float hi, float fallback)
        {
            if (!ShowMath.IsFinite(value)) return fallback;
            return ShowMath.Clamp(value, lo, hi);
        }

        static Vector3 FiniteVector(Vector3 v, Vector3 fallback) => ShowMath.IsFinite(v) ? v : fallback;

        static LedColor CleanColor(LedColor c)
        {
            float r = ShowMath.IsFinite(c.R) ? c.R : 0f;
            float g = ShowMath.IsFinite(c.G) ? c.G : 0f;
            float b = ShowMath.IsFinite(c.B) ? c.B : 0f;
            // Round to 8 bits per channel, the precision show files store, so memory and file never disagree.
            LedColor c8 = new LedColor(r, g, b).Clamped();
            return new LedColor(MathF.Round(c8.R * 255f) / 255f, MathF.Round(c8.G * 255f) / 255f, MathF.Round(c8.B * 255f) / 255f);
        }

        static string CleanText(string text, int maxLength, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var chars = new System.Text.StringBuilder(text.Length);
            string trimmed = text.Trim();
            for (int i = 0; i < trimmed.Length; i++)
            {
                char ch = trimmed[i];
                if (char.IsControl(ch)) continue;
                if (char.IsHighSurrogate(ch))
                {
                    // Keep a surrogate pair only when both halves are present and fit.
                    if (i + 1 < trimmed.Length && char.IsLowSurrogate(trimmed[i + 1]) && chars.Length + 2 <= maxLength)
                    {
                        chars.Append(ch).Append(trimmed[i + 1]);
                        i++;
                    }
                    else if (chars.Length + 2 > maxLength)
                    {
                        break;
                    }
                    continue;
                }
                if (char.IsLowSurrogate(ch)) continue;
                chars.Append(ch);
                if (chars.Length >= maxLength) break;
            }
            // Never cut a surrogate pair in half (emoji, rare CJK) when truncating.
            if (chars.Length > 0 && char.IsHighSurrogate(chars[chars.Length - 1])) chars.Length--;
            string s = chars.ToString().Trim();
            return s.Length == 0 ? fallback : s;
        }
    }
}
