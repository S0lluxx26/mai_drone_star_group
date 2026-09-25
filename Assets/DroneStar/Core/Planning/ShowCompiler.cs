using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Compiles documents into <see cref="CompiledShow"/>s. Transition assignments are cached by the
    /// geometry they connect, so editing one cue only re-solves the two transitions that touch it.
    /// Keep one compiler per editing session to benefit from the cache.
    /// </summary>
    public sealed class ShowCompiler
    {
        const int CacheLimit = 256;

        /// <summary>Shortest transition the compiler will schedule, seconds.</summary>
        public const float MinimumTransition = 2f;

        /// <summary>Head-room on auto-timed transitions so rounding never touches the limits.</summary>
        public const float TimingMargin = 1.08f;

        readonly Dictionary<ulong, int[]> assignmentCache = new Dictionary<ulong, int[]>();

        public int CacheHits { get; private set; }
        public int CacheMisses { get; private set; }

        public CompiledShow Compile(ShowDocument document)
        {
            CompileJob job = Begin(document);
            while (!job.Step())
            {
            }
            return job.Result;
        }

        /// <summary>Starts an incremental compile; call <see cref="CompileJob.Step"/> until it returns true.</summary>
        public CompileJob Begin(ShowDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return new CompileJob(this, document);
        }

        internal int[] SolveCached(Vector3[] from, Vector3[] to)
        {
            ulong key = Fingerprint(from, to);
            if (assignmentCache.TryGetValue(key, out int[] cached))
            {
                CacheHits++;
                return cached;
            }
            CacheMisses++;
            int[] solved = AssignmentSolver.Solve(from, to);
            if (assignmentCache.Count >= CacheLimit) assignmentCache.Clear();
            assignmentCache[key] = solved;
            return solved;
        }

        static ulong Fingerprint(Vector3[] a, Vector3[] b)
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (uint)a.Length);
            foreach (Vector3 v in a) h = Mix(Mix(Mix(h, Bits(v.X)), Bits(v.Y)), Bits(v.Z));
            h = Mix(h, 0xA5A5A5A5U);
            foreach (Vector3 v in b) h = Mix(Mix(Mix(h, Bits(v.X)), Bits(v.Y)), Bits(v.Z));
            return h;
        }

        static uint Bits(float f) => (uint)BitConverter.SingleToInt32Bits(f);

        static ulong Mix(ulong h, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                h ^= (value >> (8 * i)) & 0xFF;
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>Auto transition time for a longest move of <paramref name="distance"/> metres.</summary>
        public static float AutoDuration(float distance, SafetyLimits limits)
        {
            float raw = ShowMath.MinimumJerkDuration(distance, limits.MaxSpeed, limits.MaxAcceleration) * TimingMargin;
            float rounded = MathF.Ceiling(raw * 10f) / 10f;
            return Math.Max(MinimumTransition, rounded);
        }

        public static float TakeoffDuration(LaunchPadSpec pad, SafetyLimits limits)
        {
            // Climb and descent use a third of the horizontal speed limit, as most airframes require.
            float verticalSpeed = Math.Max(1f, limits.MaxSpeed / 3f);
            float raw = ShowMath.MinimumJerkDuration(pad.HoverAltitude, verticalSpeed, limits.MaxAcceleration) * TimingMargin;
            return Math.Max(4f, MathF.Ceiling(raw * 10f) / 10f);
        }

        public static void LaunchGrid(ShowDocument doc, out Vector3[] pads, out Vector3[] hover)
        {
            int n = doc.DroneCount;
            int cols = doc.Pad.Columns > 0 ? Math.Min(doc.Pad.Columns, n) : (int)MathF.Ceiling(MathF.Sqrt(n));
            cols = Math.Max(1, cols);
            int rows = (n + cols - 1) / cols;
            float s = doc.Pad.Spacing;
            pads = new Vector3[n];
            hover = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                int r = i / cols, c = i % cols;
                var p = doc.Pad.Center + new Vector3((c - (cols - 1) * 0.5f) * s, 0f, (r - (rows - 1) * 0.5f) * s);
                pads[i] = p;
                hover[i] = p + new Vector3(0f, doc.Pad.HoverAltitude, 0f);
            }
        }
    }

    /// <summary>An incremental compile. Each <see cref="Step"/> does one formation or one assignment.</summary>
    public sealed class CompileJob
    {
        readonly ShowCompiler compiler;
        readonly ShowDocument doc;
        readonly int n;
        readonly int cueCount;
        readonly FormationResult[] formations;
        readonly Vector3[][] holdEnd;
        readonly int[][] permutations;
        readonly Vector3[] pads;
        readonly Vector3[] hover;
        int formationStep;
        int transitionStep;

        internal CompileJob(ShowCompiler compiler, ShowDocument source)
        {
            this.compiler = compiler;
            doc = source.Clone();
            ShowSanitizer.Sanitize(doc);
            n = doc.DroneCount;
            cueCount = doc.Cues.Count;
            formations = new FormationResult[cueCount];
            holdEnd = new Vector3[cueCount][];
            permutations = new int[cueCount + 1][];
            ShowCompiler.LaunchGrid(doc, out pads, out hover);
            TotalSteps = cueCount + (cueCount + 1) + 1;
            Status = "Preparing";
        }

        public bool IsDone { get; private set; }
        public CompiledShow Result { get; private set; }
        public int TotalSteps { get; }
        public int CompletedSteps { get; private set; }
        public float Progress => TotalSteps == 0 ? 1f : (float)CompletedSteps / TotalSteps;
        public string Status { get; private set; }

        /// <summary>Does one unit of work. Returns true once <see cref="Result"/> is ready.</summary>
        public bool Step()
        {
            if (IsDone) return true;
            if (formationStep < cueCount)
            {
                int k = formationStep++;
                Cue cue = doc.Cues[k];
                Status = "Laying out “" + cue.Name + "”";
                HoldMotion.Envelope(cue.Motion, out float growth, out float margin);
                FormationResult f = FormationGenerator.Generate(cue.Formation, n, doc.Limits.FormationSpacing, growth, margin);
                formations[k] = f;
                var end = new Vector3[n];
                for (int s = 0; s < n; s++)
                {
                    end[s] = HoldMotion.Evaluate(cue.Motion, f, f.Slots[s], cue.HoldSeconds, cue.HoldSeconds);
                }
                holdEnd[k] = end;
            }
            else if (transitionStep <= cueCount)
            {
                int m = transitionStep++;
                Vector3[] from = m == 0 ? hover : holdEnd[m - 1];
                Vector3[] to = m < cueCount ? SlotPositions(formations[m]) : hover;
                Status = m < cueCount ? "Planning flight into “" + doc.Cues[m].Name + "”" : "Planning the return";
                permutations[m] = compiler.SolveCached(from, to);
            }
            else
            {
                Status = "Building timeline";
                Result = BuildTimeline();
                IsDone = true;
                Status = "Ready";
            }
            CompletedSteps++;
            return IsDone;
        }

        static Vector3[] SlotPositions(FormationResult f)
        {
            var p = new Vector3[f.Slots.Length];
            for (int i = 0; i < p.Length; i++) p[i] = f.Slots[i].Position;
            return p;
        }

        CompiledShow BuildTimeline()
        {
            var segments = new List<ShowSegment>();
            var timings = new List<CueTiming>(cueCount);
            var slotOfDrone = new int[cueCount][];
            float t = 0f;

            void Add(ShowSegment seg, float duration)
            {
                seg.Start = t;
                seg.End = t + duration;
                t = seg.End;
                if (duration > 0f) segments.Add(seg);
            }

            // Drone i starts on pad i and climbs to hover slot i.
            var current = new int[n];
            for (int i = 0; i < n; i++) current[i] = i;

            Add(new ShowSegment { Kind = SegmentKind.Ground, From = pads }, doc.PreShowSeconds);
            float climb = ShowCompiler.TakeoffDuration(doc.Pad, doc.Limits);
            Add(new ShowSegment { Kind = SegmentKind.Takeoff, From = pads, To = hover }, climb);

            for (int k = 0; k < cueCount; k++)
            {
                Cue cue = doc.Cues[k];
                int[] perm = permutations[k];
                var next = new int[n];
                var from = new Vector3[n];
                var to = new Vector3[n];
                float longest = 0f;
                for (int i = 0; i < n; i++)
                {
                    next[i] = perm[current[i]];
                    from[i] = k == 0 ? hover[current[i]] : holdEnd[k - 1][current[i]];
                    to[i] = formations[k].Slots[next[i]].Position;
                    longest = Math.Max(longest, Vector3.Distance(from[i], to[i]));
                }
                slotOfDrone[k] = next;
                current = next;

                float transit = cue.AutoTransition ? ShowCompiler.AutoDuration(longest, doc.Limits) : cue.TransitionSeconds;
                var timing = new CueTiming { CueIndex = k, TransitStart = t, LongestMove = longest, Formation = formations[k] };
                Add(new ShowSegment { Kind = SegmentKind.Transit, CueIndex = k, FromCueIndex = k - 1, From = from, To = to }, transit);
                timing.HoldStart = t;
                Add(new ShowSegment { Kind = SegmentKind.Hold, CueIndex = k }, cue.HoldSeconds);
                timing.HoldEnd = t;
                timings.Add(timing);
            }

            int[] back = permutations[cueCount];
            var landingPad = new int[n];
            var returnFrom = new Vector3[n];
            var returnTo = new Vector3[n];
            float longestReturn = 0f;
            for (int i = 0; i < n; i++)
            {
                landingPad[i] = back[current[i]];
                returnFrom[i] = cueCount == 0 ? hover[current[i]] : holdEnd[cueCount - 1][current[i]];
                returnTo[i] = hover[landingPad[i]];
                longestReturn = Math.Max(longestReturn, Vector3.Distance(returnFrom[i], returnTo[i]));
            }
            Add(new ShowSegment { Kind = SegmentKind.Return, FromCueIndex = cueCount - 1, From = returnFrom, To = returnTo },
                ShowCompiler.AutoDuration(longestReturn, doc.Limits));

            var descentFrom = new Vector3[n];
            var descentTo = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                descentFrom[i] = hover[landingPad[i]];
                descentTo[i] = pads[landingPad[i]];
            }
            Add(new ShowSegment { Kind = SegmentKind.Landing, From = descentFrom, To = descentTo }, climb);
            Add(new ShowSegment { Kind = SegmentKind.Ground, From = descentTo }, doc.PostShowSeconds);

            return new CompiledShow(doc, segments, timings, slotOfDrone, pads, hover);
        }
    }
}
