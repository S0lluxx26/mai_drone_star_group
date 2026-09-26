using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DroneStar.Core
{
    public enum IssueSeverity
    {
        Info,
        Warning,
        Error,
    }

    public enum IssueKind
    {
        Separation,
        Speed,
        Acceleration,
        Altitude,
        Ground,
        Geofence,
        FlightTime,
        ParkedDrones,
        /// <summary>A summary line: more intervals exist than the report lists.</summary>
        MoreFindings,
    }

    public sealed class ValidationIssue
    {
        public IssueSeverity Severity;
        public IssueKind Kind;
        public float Time;
        public float EndTime;
        public int DroneA = -1;
        public int DroneB = -1;
        public int CueIndex = -1;

        /// <summary>The worst measured value (distance, speed, altitude…) and the limit it broke.</summary>
        public float Value;

        public float Limit;
        public string Message = "";
    }

    public sealed class ValidationReport
    {
        public readonly List<ValidationIssue> Issues = new List<ValidationIssue>();
        /// <summary>Closest pass seen; +∞ means no pair ever came within <see cref="SeparationSearchRadius"/>.</summary>
        public float MinSeparation = float.PositiveInfinity;

        public float SeparationSearchRadius = float.PositiveInfinity;
        public float MinSeparationTime;
        public int MinSeparationDroneA = -1;
        public int MinSeparationDroneB = -1;
        public float MaxSpeed;
        public float MaxSpeedTime;
        public int MaxSpeedDrone = -1;
        public float MaxAcceleration;
        public float MaxAccelerationTime;
        public float MaxAltitude;
        public float MaxRadius;
        public float Duration;
        public int Samples;

        /// <summary>Violation intervals not listed because a kind hit its reporting cap.</summary>
        public int SuppressedIssues;

        public int ErrorCount => Count(IssueSeverity.Error);
        public int WarningCount => Count(IssueSeverity.Warning);
        public bool Passed => ErrorCount == 0;

        int Count(IssueSeverity severity)
        {
            int c = 0;
            foreach (ValidationIssue i in Issues)
            {
                if (i.Severity == severity) c++;
            }
            return c;
        }
    }

    /// <summary>
    /// Flies the compiled show in simulation and checks separation (continuous closest approach between
    /// samples), speed, acceleration, altitude, ground clearance, geofence and battery time.
    /// Work is incremental: call <see cref="Step"/> with a sample budget each frame, or <see cref="Run"/>.
    /// </summary>
    public sealed class SafetyValidator
    {
        public const float DefaultStep = 0.05f;

        /// <summary>Lowest altitude allowed outside take-off, landing and time on the pads.</summary>
        public const float GroundClearance = 2f;

        const int MaxIssuesPerKind = 40;

        /// <summary>Acceleration above the limit by more than this factor fails the show (below it: warning).</summary>
        public const float AccelerationErrorFactor = 1.1f;

        readonly CompiledShow show;
        readonly SafetyLimits limits;
        readonly float dt;
        readonly int sampleCount;
        readonly int n;
        readonly SpatialGrid grid = new SpatialGrid();
        readonly Dictionary<IssueKind, ValidationIssue> open = new Dictionary<IssueKind, ValidationIssue>();
        readonly Dictionary<IssueKind, int> issueCounts = new Dictionary<IssueKind, int>();
        readonly Dictionary<IssueKind, bool> hitThisSample = new Dictionary<IssueKind, bool>();
        readonly HashSet<IssueKind> suppressedOpen = new HashSet<IssueKind>();
        bool suppressedError;
        Vector3[] prev2;
        Vector3[] prev;
        Vector3[] cur;
        int nextSample;

        // The sample in progress. Its separation pairs are checked a chunk of drones at a time, so thousands of
        // drones in a crowded transit never stall a frame on a single sample.
        const int PairChunk = 1024;
        int pairCursor = -1;
        Vector3[] pairStart;
        float sampleTime;
        int sampleCue;

        public SafetyValidator(CompiledShow show, float step = DefaultStep)
        {
            this.show = show ?? throw new ArgumentNullException(nameof(show));
            limits = show.Document.Limits;
            dt = ShowMath.IsFinite(step) ? ShowMath.Clamp(step, 0.01f, 0.5f) : DefaultStep;
            n = show.DroneCount;
            double samples = Math.Ceiling(show.Duration / (double)dt) + 1.0;
            sampleCount = (int)Math.Min(samples, int.MaxValue - 1.0);
            prev2 = new Vector3[n];
            prev = new Vector3[n];
            cur = new Vector3[n];
            Report = new ValidationReport { Duration = show.Duration };
        }

        public ValidationReport Report { get; }
        public bool IsDone { get; private set; }
        public float Progress => sampleCount == 0 ? 1f : Math.Min(1f, (float)nextSample / sampleCount);

        public static ValidationReport Run(CompiledShow show, float step = DefaultStep)
        {
            var v = new SafetyValidator(show, step);
            while (!v.Step(int.MaxValue))
            {
            }
            return v.Report;
        }

        /// <summary>
        /// Does up to <paramref name="maxUnits"/> units of work: starting a sample (positions and per-drone checks)
        /// or checking the separation pairs of up to 1,024 drones. Returns true when finished.
        /// </summary>
        public bool Step(int maxUnits)
        {
            if (IsDone) return true;
            for (int budget = 0; budget < maxUnits; budget++)
            {
                if (pairCursor < 0)
                {
                    if (nextSample >= sampleCount) break;
                    int k = nextSample++;
                    float t = Math.Min(k * dt, show.Duration);
                    Vector3[] recycled = prev2;
                    prev2 = prev;
                    prev = cur;
                    cur = recycled;
                    show.SamplePositions(t, cur);
                    BeginSample(k, t);
                }
                else
                {
                    int end = Math.Min(n, pairCursor + PairChunk);
                    CheckPairs(pairCursor, end);
                    pairCursor = end;
                    if (pairCursor >= n)
                    {
                        CloseIntervalsNotHit();
                        pairCursor = -1;
                    }
                }
            }
            if (pairCursor < 0 && nextSample >= sampleCount)
            {
                Finish();
                IsDone = true;
            }
            return IsDone;
        }

        void BeginSample(int k, float t)
        {
            Report.Samples++;
            hitThisSample.Clear();
            ShowSegment seg = show.SegmentAt(t);
            bool airborneLeg = seg != null && seg.Kind != SegmentKind.Ground && seg.Kind != SegmentKind.Takeoff && seg.Kind != SegmentKind.Landing;
            int cue = seg != null ? seg.CueIndex : -1;
            float h = k > 0 ? Math.Min(dt, t - (k - 1) * dt) : dt;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = cur[i];
                if (!ShowMath.IsFinite(p))
                {
                    Flag(IssueKind.Altitude, IssueSeverity.Error, t, cue, i, -1, float.NaN, 0f);
                    continue;
                }
                Report.MaxAltitude = Math.Max(Report.MaxAltitude, p.Y);
                if (p.Y > limits.MaxAltitude) Flag(IssueKind.Altitude, IssueSeverity.Error, t, cue, i, -1, p.Y, limits.MaxAltitude);
                if (airborneLeg && p.Y < GroundClearance) Flag(IssueKind.Ground, IssueSeverity.Error, t, cue, i, -1, p.Y, GroundClearance);

                Vector3 fromPad = p - show.Document.Pad.Center;
                float radius = MathF.Sqrt(fromPad.X * fromPad.X + fromPad.Z * fromPad.Z);
                Report.MaxRadius = Math.Max(Report.MaxRadius, radius);
                if (radius > limits.GeofenceRadius) Flag(IssueKind.Geofence, IssueSeverity.Error, t, cue, i, -1, radius, limits.GeofenceRadius);

                if (k >= 1 && h > 1e-5f)
                {
                    float speed = Vector3.Distance(p, prev[i]) / h;
                    if (speed > Report.MaxSpeed)
                    {
                        Report.MaxSpeed = speed;
                        Report.MaxSpeedTime = t;
                        Report.MaxSpeedDrone = i;
                    }
                    if (speed > limits.MaxSpeed * 1.001f) Flag(IssueKind.Speed, IssueSeverity.Error, t, cue, i, -1, speed, limits.MaxSpeed);
                }
                if (k >= 2 && h > 1e-5f)
                {
                    // Second difference; the final sample may be closer than dt to the previous one, so use
                    // the non-uniform form.
                    Vector3 v1 = (prev[i] - prev2[i]) / dt;
                    Vector3 v2 = (p - prev[i]) / h;
                    float accel = (v2 - v1).Length() / (0.5f * (dt + h));
                    if (accel > Report.MaxAcceleration)
                    {
                        Report.MaxAcceleration = accel;
                        Report.MaxAccelerationTime = t;
                    }
                    if (accel > limits.MaxAcceleration * 1.02f)
                    {
                        IssueSeverity severity = accel > limits.MaxAcceleration * AccelerationErrorFactor ? IssueSeverity.Error : IssueSeverity.Warning;
                        Flag(IssueKind.Acceleration, severity, t, cue, i, -1, accel, limits.MaxAcceleration);
                    }
                }
            }

            BeginSeparation(k, t, cue);
        }

        void BeginSeparation(int k, float t, int cue)
        {
            // Pairs are tested over the interval [previous sample, this sample] with linear motion, so a
            // fast crossing between samples is still caught.
            Vector3[] a0 = k == 0 ? cur : prev;
            float maxStep = 0f;
            for (int i = 0; i < n; i++) maxStep = Math.Max(maxStep, Vector3.DistanceSquared(a0[i], cur[i]));
            maxStep = MathF.Sqrt(maxStep);
            float reach = limits.MinSeparation + 2f * maxStep;
            // Search a little wider than the violation radius so the report can quote the closest pass
            // even when it is legal (formations sit at √2 × the minimum separation).
            float searchRadius = Math.Max(reach, limits.FormationSpacing * 1.25f);
            Report.SeparationSearchRadius = Math.Min(Report.SeparationSearchRadius, searchRadius);
            grid.Build(a0, n, searchRadius);
            pairStart = a0;
            sampleTime = t;
            sampleCue = cue;
            pairCursor = 0;
        }

        void CheckPairs(int first, int end)
        {
            Vector3[] a0 = pairStart;
            float t = sampleTime;
            int cue = sampleCue;
            for (int i = first; i < end; i++)
            {
                grid.CellOf(a0[i], out int cx, out int cy, out int cz);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int j = grid.First(cx + dx, cy + dy, cz + dz); j >= 0; j = grid.Next(j))
                    {
                        if (j <= i) continue;
                        float d = ShowMath.ClosestApproach(a0[i], cur[i], a0[j], cur[j]);
                        if (d < Report.MinSeparation)
                        {
                            Report.MinSeparation = d;
                            Report.MinSeparationTime = t;
                            Report.MinSeparationDroneA = i;
                            Report.MinSeparationDroneB = j;
                        }
                        if (d < limits.MinSeparation) Flag(IssueKind.Separation, IssueSeverity.Error, t, cue, i, j, d, limits.MinSeparation, lowerIsWorse: true);
                    }
                }
            }
        }

        void Flag(IssueKind kind, IssueSeverity severity, float t, int cue, int a, int b, float value, float limit, bool lowerIsWorse = false)
        {
            hitThisSample[kind] = true;
            if (open.TryGetValue(kind, out ValidationIssue issue))
            {
                issue.EndTime = t;
                bool worse = lowerIsWorse ? value < issue.Value : value > issue.Value;
                if (worse || float.IsNaN(value))
                {
                    issue.Value = value;
                    issue.DroneA = a;
                    issue.DroneB = b;
                }
                // An interval that crosses into error territory is an error.
                if (severity > issue.Severity) issue.Severity = severity;
                return;
            }
            issueCounts.TryGetValue(kind, out int count);
            if (count >= MaxIssuesPerKind)
            {
                if (!suppressedOpen.Contains(kind))
                {
                    suppressedOpen.Add(kind);
                    Report.SuppressedIssues++;
                    if (severity == IssueSeverity.Error) suppressedError = true;
                }
                return;
            }
            issueCounts[kind] = count + 1;
            open[kind] = new ValidationIssue
            {
                Severity = severity,
                Kind = kind,
                Time = t,
                EndTime = t,
                CueIndex = cue,
                DroneA = a,
                DroneB = b,
                Value = value,
                Limit = limit,
            };
        }

        void CloseIntervalsNotHit()
        {
            suppressedOpen.RemoveWhere(k => !hitThisSample.ContainsKey(k));
            if (open.Count == 0) return;
            List<IssueKind> closing = null;
            foreach (KeyValuePair<IssueKind, ValidationIssue> kv in open)
            {
                if (hitThisSample.ContainsKey(kv.Key)) continue;
                (closing ?? (closing = new List<IssueKind>())).Add(kv.Key);
            }
            if (closing == null) return;
            foreach (IssueKind kind in closing)
            {
                Commit(open[kind]);
                open.Remove(kind);
            }
        }

        void Commit(ValidationIssue issue)
        {
            if (string.IsNullOrEmpty(issue.Message)) issue.Message = Describe(issue);
            Report.Issues.Add(issue);
        }

        void Finish()
        {
            foreach (ValidationIssue issue in open.Values) Commit(issue);
            open.Clear();

            if (show.Duration > limits.MaxFlightSeconds)
            {
                Commit(new ValidationIssue
                {
                    Severity = IssueSeverity.Error,
                    Kind = IssueKind.FlightTime,
                    Time = limits.MaxFlightSeconds,
                    EndTime = show.Duration,
                    Value = show.Duration,
                    Limit = limits.MaxFlightSeconds,
                });
            }

            for (int c = 0; c < show.CueTimings.Count; c++)
            {
                CueTiming timing = show.CueTimings[c];
                int parked = timing.Formation.DarkCount;
                if (parked == 0) continue;
                Commit(new ValidationIssue
                {
                    Severity = IssueSeverity.Info,
                    Kind = IssueKind.ParkedDrones,
                    Time = timing.HoldStart,
                    EndTime = timing.HoldEnd,
                    CueIndex = c,
                    Value = parked,
                    Limit = show.DroneCount,
                });
            }

            if (Report.SuppressedIssues > 0)
            {
                Commit(new ValidationIssue
                {
                    Severity = suppressedError ? IssueSeverity.Error : IssueSeverity.Warning,
                    Kind = IssueKind.MoreFindings,
                    Time = 0f,
                    EndTime = 0f,
                    Value = Report.SuppressedIssues,
                    Limit = MaxIssuesPerKind,
                    Message = "",
                });
                Report.Issues[Report.Issues.Count - 1].Message = string.Format(CultureInfo.InvariantCulture,
                    "{0} more violation intervals were not listed (each kind lists at most {1}). Fix the listed ones and re-check.",
                    Report.SuppressedIssues, MaxIssuesPerKind);
            }

            Report.Issues.Sort((x, y) =>
            {
                int bySeverity = y.Severity.CompareTo(x.Severity);
                return bySeverity != 0 ? bySeverity : x.Time.CompareTo(y.Time);
            });
        }

        string Describe(ValidationIssue i)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string when = i.EndTime > i.Time + 1e-3f
                ? string.Format(ci, "{0:0.0} to {1:0.0} s", i.Time, i.EndTime)
                : string.Format(ci, "{0:0.0} s", i.Time);
            string cue = i.CueIndex >= 0 && i.CueIndex < show.Document.Cues.Count ? " in “" + show.Document.Cues[i.CueIndex].Name + "”" : "";
            switch (i.Kind)
            {
                case IssueKind.Separation:
                    return string.Format(ci, "Drones {0} and {1} come within {2:0.00} m (limit {3:0.0} m) at {4}{5}.", i.DroneA + 1, i.DroneB + 1, i.Value, i.Limit, when, cue);
                case IssueKind.Speed:
                    return string.Format(ci, "Drone {0} reaches {1:0.0} m/s (limit {2:0.0}) at {3}{4}. Lengthen the transition or slow the motion.", i.DroneA + 1, i.Value, i.Limit, when, cue);
                case IssueKind.Acceleration:
                    return string.Format(ci, "Drone {0} accelerates at {1:0.0} m/s² (limit {2:0.0}) at {3}{4}.", i.DroneA + 1, i.Value, i.Limit, when, cue);
                case IssueKind.Altitude:
                    return float.IsNaN(i.Value)
                        ? string.Format(ci, "Drone {0} has an invalid position at {1}{2}.", i.DroneA + 1, when, cue)
                        : string.Format(ci, "Drone {0} climbs to {1:0.0} m (ceiling {2:0} m) at {3}{4}.", i.DroneA + 1, i.Value, i.Limit, when, cue);
                case IssueKind.Ground:
                    return string.Format(ci, "Drone {0} drops to {1:0.0} m above ground at {2}{3}. Raise the formation.", i.DroneA + 1, i.Value, when, cue);
                case IssueKind.Geofence:
                    return string.Format(ci, "Drone {0} flies {1:0.0} m from the pad centre (fence {2:0} m) at {3}{4}.", i.DroneA + 1, i.Value, i.Limit, when, cue);
                case IssueKind.FlightTime:
                    return string.Format(ci, "The flight lasts {0:0} s but batteries allow {1:0} s.", i.Value, i.Limit);
                case IssueKind.ParkedDrones:
                    return string.Format(ci, "{0:0} of {1:0} drones park dark{2}: the shape is too small for them at safe spacing.", i.Value, i.Limit, cue);
                default:
                    return i.Kind.ToString();
            }
        }
    }
}
