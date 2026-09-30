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
            sb.Append(string.Format(ci, "- Furthest from pad centre: {0:0.0} m (fence {1:0} m)\n", report.MaxRadius, l.GeofenceRadius));
            if (report.FlamesArmed)
            {
                string flames = float.IsPositiveInfinity(report.MinFlameClearance)
                    ? string.Format(ci, "more than {0:0} m", report.FlameSearchRadius)
                    : string.Format(ci, "{0:0.0} m at {1:0.0} s", report.MinFlameClearance, report.MinFlameClearanceTime);
                sb.Append(string.Format(ci, "- Closest to the stage flames: {0} (keep {1:0} m while armed)\n", flames, FestivalStage.FlameSafetyDistance));
            }
            bool festival = doc.Venue == ShowVenue.Festival;
            sb.Append("- Venue: ").Append(festival ? "festival stage" : "night lake").Append("\n\n");

            sb.Append(festival
                ? "## Cues\n\n| # | Cue | Formation | Starts | Transit | Hold | Lit drones | Stage effects |\n|---|---|---|---|---|---|---|---|\n"
                : "## Cues\n\n| # | Cue | Formation | Starts | Transit | Hold | Lit drones |\n|---|---|---|---|---|---|---|\n");
            for (int i = 0; i < show.CueTimings.Count; i++)
            {
                CueTiming t = show.CueTimings[i];
                Cue cue = doc.Cues[i];
                sb.Append(string.Format(ci, "| {0} | {1} | {2} | {3:0.0} s | {4:0.0} s | {5:0.0} s | {6}/{7} |",
                    i + 1, Escape(cue.Name), cue.Formation.Kind, t.TransitStart, t.TransitSeconds, t.HoldSeconds,
                    t.Formation.LitCount, show.DroneCount));
                if (festival) sb.Append(' ').Append(FestivalStage.Describe(cue.Effects)).Append(" |");
                sb.Append('\n');
            }

            sb.Append("\n## Findings\n\n");
            if (report.Issues.Count == 0) sb.Append("No findings.\n");
            foreach (ValidationIssue issue in report.Issues)
            {
                sb.Append("- **").Append(issue.Severity).Append("** ").Append(issue.Message).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The stage cue sheet for the laser, fountain and flame operators: one row per scene with its flight in,
        /// hold and the window its flames are armed, in show seconds, plus the hold's start as SMPTE timecode at
        /// 25 frames per second (the show starts at 00:00:00:00). At the lake venue the stage columns read "none".
        /// </summary>
        public static string ToStageCueSheet(CompiledShow show)
        {
            if (show == null) throw new ArgumentNullException(nameof(show));
            CultureInfo ci = CultureInfo.InvariantCulture;
            ShowDocument doc = show.Document;
            bool festival = doc.Venue == ShowVenue.Festival;
            var sb = new StringBuilder();
            sb.Append("cue,name,flight_in_s,hold_start_s,hold_end_s,hold_start_tc,lasers,fountains,flames,flames_armed_from_s,flames_armed_to_s,steam\n");
            for (int i = 0; i < show.CueTimings.Count; i++)
            {
                CueTiming t = show.CueTimings[i];
                StageEffects fx = doc.Cues[i].Effects;
                bool flames = festival && fx.Flames != FlameMode.Off;
                sb.Append((i + 1).ToString(ci)).Append(',')
                  .Append(CsvText(doc.Cues[i].Name)).Append(',')
                  .Append(t.TransitStart.ToString("0.00", ci)).Append(',')
                  .Append(t.HoldStart.ToString("0.00", ci)).Append(',')
                  .Append(t.HoldEnd.ToString("0.00", ci)).Append(',')
                  .Append(Timecode(t.HoldStart)).Append(',')
                  .Append(festival ? fx.Lasers.ToString().ToLowerInvariant() : "none").Append(',')
                  .Append(festival ? fx.Fountains.ToString().ToLowerInvariant() : "none").Append(',')
                  .Append(festival ? fx.Flames.ToString().ToLowerInvariant() : "none").Append(',')
                  .Append(flames ? Math.Max(0f, t.HoldStart - FestivalStage.FlameArmLead).ToString("0.00", ci) : "").Append(',')
                  .Append(flames ? (t.HoldEnd + FestivalStage.FlameArmTail).ToString("0.00", ci) : "").Append(',')
                  .Append(!festival ? "none" : fx.Steam ? "on" : "off").Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>SMPTE timecode hh:mm:ss:ff at 25 frames per second.</summary>
        public static string Timecode(float seconds)
        {
            long frames = (long)Math.Round(Math.Max(0.0, seconds) * 25.0);
            long total = frames / 25;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}:{3:00}", total / 3600, total / 60 % 60, total % 60, frames % 25);
        }

        static string CsvText(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        static string Escape(string s) => (s ?? "").Replace("|", "\\|");
    }
}
