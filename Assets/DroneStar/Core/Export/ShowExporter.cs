using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace DroneStar.Core
{
    /// <summary>Exports compiled shows for flight controllers and for people.</summary>
    public static class ShowExporter
    {
        /// <summary>
        /// Long-format trajectory table: one row per drone per sample. Coordinates are metres relative to
        /// the pad centre (x east/right, y up, z north/away from the audience); colours are 0–255 sRGB.
        /// </summary>
        public static string ToCsv(CompiledShow show, float rateHz = 4f)
        {
            if (show == null) throw new ArgumentNullException(nameof(show));
            rateHz = ShowMath.Clamp(rateHz, 0.5f, 30f);
            CultureInfo ci = CultureInfo.InvariantCulture;
            int n = show.DroneCount;
            var positions = new Vector3[n];
            var colors = new LedColor[n];
            Vector3 origin = show.Document.Pad.Center;
            int samples = (int)MathF.Floor(show.Duration * rateHz) + 1;
            long estimate = 64L + (long)samples * n * 40L;
            var sb = new StringBuilder((int)Math.Min(estimate, 1L << 26));
            sb.Append("time_s,drone,x_m,y_m,z_m,red,green,blue\n");
            for (int k = 0; k < samples; k++)
            {
                float t = Math.Min(k / rateHz, show.Duration);
                show.Sample(t, positions, colors);
                string time = t.ToString("0.###", ci);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = positions[i] - origin;
                    LedColor c = colors[i].Clamped();
                    sb.Append(time).Append(',')
                      .Append((i + 1).ToString(ci)).Append(',')
                      .Append(p.X.ToString("0.###", ci)).Append(',')
                      .Append(p.Y.ToString("0.###", ci)).Append(',')
                      .Append(p.Z.ToString("0.###", ci)).Append(',')
                      .Append(((int)MathF.Round(c.R * 255f)).ToString(ci)).Append(',')
                      .Append(((int)MathF.Round(c.G * 255f)).ToString(ci)).Append(',')
                      .Append(((int)MathF.Round(c.B * 255f)).ToString(ci)).Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>A Markdown flight-safety report suitable for sharing with a show director.</summary>
        public static string ToFlightReport(CompiledShow show, ValidationReport report)
        {
            if (show == null) throw new ArgumentNullException(nameof(show));
            if (report == null) throw new ArgumentNullException(nameof(report));
            CultureInfo ci = CultureInfo.InvariantCulture;
            ShowDocument doc = show.Document;
            SafetyLimits l = doc.Limits;
            var sb = new StringBuilder();
            sb.Append("# Flight report — ").Append(doc.Title).Append('\n').Append('\n');
            sb.Append(report.Passed ? "**Result: PASSED**" : "**Result: NOT SAFE TO FLY**")
              .Append(string.Format(ci, " ({0} errors, {1} warnings)\n\n", report.ErrorCount, report.WarningCount));
            sb.Append(string.Format(ci, "- Drones: {0}\n", show.DroneCount));
            sb.Append(string.Format(ci, "- Flight time: {0:0.0} s (battery budget {1:0} s)\n", show.Duration, l.MaxFlightSeconds));
            string separation = float.IsPositiveInfinity(report.MinSeparation)
                ? string.Format(ci, "more than {0:0.0} m", report.SeparationSearchRadius)
                : string.Format(ci, "{0:0.00} m at {1:0.0} s", report.MinSeparation, report.MinSeparationTime);
            sb.Append(string.Format(ci, "- Closest pass: {0} (limit {1:0.0} m)\n", separation, l.MinSeparation));
            sb.Append(string.Format(ci, "- Top speed: {0:0.0} m/s at {1:0.0} s (limit {2:0.0} m/s)\n", report.MaxSpeed, report.MaxSpeedTime, l.MaxSpeed));
            sb.Append(string.Format(ci, "- Peak acceleration: {0:0.0} m/s² (limit {1:0.0} m/s²)\n", report.MaxAcceleration, l.MaxAcceleration));
            sb.Append(string.Format(ci, "- Highest point: {0:0.0} m (ceiling {1:0} m)\n", report.MaxAltitude, l.MaxAltitude));
            sb.Append(string.Format(ci, "- Furthest from pad centre: {0:0.0} m (fence {1:0} m)\n\n", report.MaxRadius, l.GeofenceRadius));

            sb.Append("## Cues\n\n| # | Cue | Formation | Starts | Transit | Hold | Lit drones |\n|---|---|---|---|---|---|---|\n");
            for (int i = 0; i < show.CueTimings.Count; i++)
            {
                CueTiming t = show.CueTimings[i];
                Cue cue = doc.Cues[i];
                sb.Append(string.Format(ci, "| {0} | {1} | {2} | {3:0.0} s | {4:0.0} s | {5:0.0} s | {6}/{7} |\n",
                    i + 1, Escape(cue.Name), cue.Formation.Kind, t.TransitStart, t.TransitSeconds, t.HoldSeconds,
                    t.Formation.LitCount, show.DroneCount));
            }

            sb.Append("\n## Findings\n\n");
            if (report.Issues.Count == 0) sb.Append("No findings.\n");
            foreach (ValidationIssue issue in report.Issues)
            {
                sb.Append("- **").Append(issue.Severity).Append("** ").Append(issue.Message).Append('\n');
            }
            return sb.ToString();
        }

        static string Escape(string s) => (s ?? "").Replace("|", "\\|");
    }
}
