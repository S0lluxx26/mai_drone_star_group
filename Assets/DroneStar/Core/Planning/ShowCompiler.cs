using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace DroneStar.Core
{
    /// <summary>
    /// Compiles documents into <see cref="CompiledShow"/>s. Formations are cached by the inputs that shape
    /// them and transition assignments by the geometry they connect, so editing one cue only re-lays that
    /// cue and re-solves the two transitions that touch it. Keep one compiler per editing session.
    /// </summary>
    public sealed class ShowCompiler
    {
        const int CacheLimit = 256;
        const int FormationCacheLimit = 64;
        const int PendingAuctionLimit = 4;

        /// <summary>Relative cost tolerance when checking a portable entry against today's geometry.</summary>
        const double PortableCostTolerance = 1e-6;

        /// <summary>
        /// Up to this many drones the exact Hungarian method is used (the CAPT guarantee holds exactly);
        /// above it the ε-scaling auction, which is near-optimal and can be spread across frames.
        /// </summary>
        public const int ExactAssignmentLimit = 600;

        /// <summary>Auction bids per compile step: keeps one step around a few milliseconds at any size.</summary>
        internal static int BidsPerStep(int n) => Math.Max(32, 2_000_000 / Math.Max(n, 1));

        /// <summary>Shortest transition the compiler will schedule, seconds.</summary>
        public const float MinimumTransition = 2f;

        /// <summary>Head-room on auto-timed transitions so rounding never touches the limits.</summary>
        public const float TimingMargin = 1.08f;

        // Assignment caches. Geometry keys hash the exact slot positions, so they are shared by any documents
        // that produce the same layout, but only within one runtime (transcendental functions may differ in
        // the last bit between .NET, Mono and WebAssembly, which can flip a rounding). Recipe keys hash the
        // inputs that produce the layout (specs, fleet, spacing, generator revision, shape pack) and are
        // identical on every runtime; they are what ExportCache ships, e.g. the baked demo assignments. A
        // recipe entry also records its total cost, and a hit is used only if the permutation still costs
        // that much on today's geometry, so a stale bake is re-solved instead of trusted. Imported entries
        // are never evicted.
        readonly Dictionary<ulong, int[]> assignmentCache = new Dictionary<ulong, int[]>();
        readonly Dictionary<ulong, PortableEntry> recipeCache = new Dictionary<ulong, PortableEntry>();
        readonly Dictionary<ulong, PortableEntry> importedCache = new Dictionary<ulong, PortableEntry>();
        readonly Dictionary<ulong, FormationResult> formationCache = new Dictionary<ulong, FormationResult>();
        readonly Dictionary<ulong, AuctionAssignment> pendingAuctions = new Dictionary<ulong, AuctionAssignment>();

        struct PortableEntry
        {
            public int[] Permutation;
            public double Cost;
        }

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

        /// <summary>Looks a transition up by geometry, then by recipe (0 = none).</summary>
        internal bool TryCached(Vector3[] from, Vector3[] to, ulong recipe, out ulong key, out int[] cached)
        {
            key = Fingerprint(from, to);
            if (assignmentCache.TryGetValue(key, out cached) && cached.Length == from.Length)
            {
                CacheHits++;
                return true;
            }
            if (recipe != 0 && (Portable(importedCache, recipe, from, to, out cached) || Portable(recipeCache, recipe, from, to, out cached)))
            {
                CacheHits++;
                assignmentCache[key] = cached;
                return true;
            }
            CacheMisses++;
            cached = null;
            return false;
        }

        static bool Portable(Dictionary<ulong, PortableEntry> cache, ulong recipe, Vector3[] from, Vector3[] to, out int[] permutation)
        {
            permutation = null;
            if (!cache.TryGetValue(recipe, out PortableEntry e) || e.Permutation.Length != from.Length) return false;
            double cost = AssignmentSolver.TotalCost(from, to, e.Permutation);
            if (Math.Abs(cost - e.Cost) > PortableCostTolerance * Math.Max(e.Cost, 1.0)) return false;
            permutation = e.Permutation;
            return true;
        }

        internal void Store(ulong key, ulong recipe, int[] permutation, Vector3[] from, Vector3[] to)
        {
            if (assignmentCache.Count >= CacheLimit) assignmentCache.Clear();
            if (recipeCache.Count >= CacheLimit) recipeCache.Clear();
            assignmentCache[key] = permutation;
            if (recipe != 0) recipeCache[recipe] = new PortableEntry { Permutation = permutation, Cost = AssignmentSolver.TotalCost(from, to, permutation) };
        }

        /// <summary>Portable entries currently held (what <see cref="ExportCache"/> writes).</summary>
        public int PortableCacheCount
        {
            get
            {
                int count = recipeCache.Count;
                foreach (ulong k in importedCache.Keys) if (!recipeCache.ContainsKey(k)) count++;
                return count;
            }
        }

        internal FormationResult CachedFormation(ulong recipe) => formationCache.TryGetValue(recipe, out FormationResult f) ? f : null;

        internal void StoreFormation(ulong recipe, FormationResult f)
        {
            if (formationCache.Count >= FormationCacheLimit) formationCache.Clear();
            formationCache[recipe] = f;
        }

        /// <summary>
        /// The auction for a transition, resumed if an earlier (abandoned) compile had started it: every edit
        /// starts a new compile, and a large auction should not restart from zero each time.
        /// </summary>
        internal AuctionAssignment AuctionFor(ulong key, Vector3[] from, Vector3[] to)
        {
            if (pendingAuctions.TryGetValue(key, out AuctionAssignment a) && !a.IsDone) return a;
            if (pendingAuctions.Count >= PendingAuctionLimit) pendingAuctions.Clear();
            a = new AuctionAssignment(from, to);
            pendingAuctions[key] = a;
            return a;
        }

        internal void AuctionFinished(ulong key) => pendingAuctions.Remove(key);

        /// <summary>
        /// Serialises the portable (recipe-keyed) assignments, e.g. to ship pre-solved transitions for the
        /// built-in shows. Format: "DSAC" | u16 version (3) | u32 entries | per entry: u64 key | u32 n |
        /// f64 total cost | u16 slot[n].
        /// </summary>
        public byte[] ExportCache()
        {
            var bytes = new List<byte>();
            void U16(int v)
            {
                bytes.Add((byte)v);
                bytes.Add((byte)(v >> 8));
            }
            void U32(uint v)
            {
                for (int k = 0; k < 4; k++) bytes.Add((byte)(v >> (8 * k)));
            }
            var all = new Dictionary<ulong, PortableEntry>(importedCache);
            foreach (KeyValuePair<ulong, PortableEntry> kv in recipeCache) all[kv.Key] = kv.Value;
            bytes.AddRange(new[] { (byte)'D', (byte)'S', (byte)'A', (byte)'C' });
            U16(3);
            U32((uint)all.Count);
            foreach (KeyValuePair<ulong, PortableEntry> kv in all)
            {
                U32((uint)kv.Key);
                U32((uint)(kv.Key >> 32));
                U32((uint)kv.Value.Permutation.Length);
                bytes.AddRange(BitConverter.GetBytes(kv.Value.Cost));
                foreach (int slot in kv.Value.Permutation) U16(slot);
            }
            return bytes.ToArray();
        }

        /// <summary>Adds entries from <see cref="ExportCache"/>; malformed data is rejected without side effects.</summary>
        public int ImportCache(byte[] data)
        {
            if (data == null || data.Length < 10 || data[0] != 'D' || data[1] != 'S' || data[2] != 'A' || data[3] != 'C') return 0;
            int pos = 4;
            int U16() { int v = data[pos] | (data[pos + 1] << 8); pos += 2; return v; }
            uint U32() { uint v = (uint)(data[pos] | (data[pos + 1] << 8) | (data[pos + 2] << 16) | (data[pos + 3] << 24)); pos += 4; return v; }
            if (U16() != 3) return 0;
            uint count = U32();
            var parsed = new List<KeyValuePair<ulong, PortableEntry>>();
            for (uint e = 0; e < count; e++)
            {
                if (pos + 20 > data.Length) return 0;
                ulong key = U32() | ((ulong)U32() << 32);
                uint n = U32();
                double cost = BitConverter.ToDouble(data, pos);
                pos += 8;
                if (!(cost >= 0.0) || double.IsInfinity(cost)) return 0;
                if (n > ShowBounds.MaxDrones || pos + n * 2 > data.Length) return 0;
                var perm = new int[n];
                var seen = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    int slot = U16();
                    if (slot >= n || seen[slot]) return 0;
                    seen[slot] = true;
                    perm[i] = slot;
                }
                parsed.Add(new KeyValuePair<ulong, PortableEntry>(key, new PortableEntry { Permutation = perm, Cost = cost }));
            }
            if (pos != data.Length) return 0;
            foreach (KeyValuePair<ulong, PortableEntry> kv in parsed) importedCache[kv.Key] = kv.Value;
            return parsed.Count;
        }

        /// <summary>
        /// Geometry fingerprint of a transition. Positions are rounded to a millimetre so tiny last-bit
        /// differences between runtimes (Mono, IL2CPP/WebAssembly, .NET) still share cached solutions.
        /// </summary>
        static ulong Fingerprint(Vector3[] a, Vector3[] b)
        {
            ulong h = 14695981039346656037UL;
            h = Mix(h, (uint)a.Length);
            foreach (Vector3 v in a) h = Mix(Mix(Mix(h, Quantise(v.X)), Quantise(v.Y)), Quantise(v.Z));
            h = Mix(h, 0xA5A5A5A5U);
            foreach (Vector3 v in b) h = Mix(Mix(Mix(h, Quantise(v.X)), Quantise(v.Y)), Quantise(v.Z));
            return h;
        }

        static uint Quantise(float f) => (uint)(int)MathF.Round(f * 1000f);

        internal static ulong Hash(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in text) h = Mix(h, c);
            return h == 0 ? 1 : h;
        }

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
        AuctionAssignment auction;
        ulong auctionKey;
        ulong auctionRecipe;
        int auctionIndex = -1;
        Vector3[] auctionFrom;
        Vector3[] auctionTo;

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
        public float Progress => TotalSteps == 0 ? 1f : Math.Min(1f, (CompletedSteps + AuctionFraction) / TotalSteps);

        // Rough progress inside a long auction: bids relative to a typical total of ~45 per drone.
        float AuctionFraction => auction == null ? 0f : Math.Min(0.95f, auction.Bids / (45f * Math.Max(n, 1)));
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
                ulong layout = LayoutRecipe(k, growth, margin);
                FormationResult f = compiler.CachedFormation(layout);
                if (f == null)
                {
                    f = FormationGenerator.Generate(cue.Formation, n, doc.Limits.FormationSpacing, growth, margin);
                    compiler.StoreFormation(layout, f);
                }
                formations[k] = f;
                var end = new Vector3[n];
                for (int s = 0; s < n; s++)
                {
                    end[s] = HoldMotion.Evaluate(cue.Motion, f, f.Slots[s], cue.HoldSeconds, cue.HoldSeconds);
                }
                holdEnd[k] = end;
            }
            else if (auction != null)
            {
                // Continue a large assignment; it spans several steps so frames stay smooth.
                if (!auction.Step(ShowCompiler.BidsPerStep(n))) return false;
                permutations[auctionIndex] = auction.Result;
                compiler.Store(auctionKey, auctionRecipe, auction.Result, auctionFrom, auctionTo);
                compiler.AuctionFinished(auctionKey);
                auction = null;
            }
            else if (transitionStep <= cueCount)
            {
                int m = transitionStep++;
                Vector3[] from = m == 0 ? hover : holdEnd[m - 1];
                Vector3[] to = m < cueCount ? SlotPositions(formations[m]) : hover;
                Status = m < cueCount ? "Planning flight into “" + doc.Cues[m].Name + "”" : "Planning the return";
                ulong recipe = Recipe(m);
                if (compiler.TryCached(from, to, recipe, out ulong key, out int[] cached))
                {
                    permutations[m] = cached;
                }
                else if (n <= ShowCompiler.ExactAssignmentLimit)
                {
                    permutations[m] = AssignmentSolver.Solve(from, to);
                    compiler.Store(key, recipe, permutations[m], from, to);
                }
                else
                {
                    auction = compiler.AuctionFor(key, from, to);
                    auctionKey = key;
                    auctionRecipe = recipe;
                    auctionIndex = m;
                    auctionFrom = from;
                    auctionTo = to;
                    return false;
                }
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

        /// <summary>
        /// Runtime-independent key of transition <paramref name="m"/> (from cue m-1, or the hover grid, into cue m,
        /// or back to the grid): every input that shapes the two slot sets, with floats written as exact bits.
        /// </summary>
        ulong Recipe(int m)
        {
            var sb = new StringBuilder(256);
            Common(sb, "r");
            Side(sb, m - 1);
            sb.Append('>');
            Side(sb, m);
            return ShowCompiler.Hash(sb.ToString());
        }

        /// <summary>Key of cue <paramref name="k"/>'s layout: its formation spec and the room its motion needs.</summary>
        ulong LayoutRecipe(int k, float growth, float margin)
        {
            var sb = new StringBuilder(160);
            Common(sb, "f");
            Shape(sb, doc.Cues[k].Formation);
            sb.Append("|g:").Append(Bits(growth)).Append(',').Append(Bits(margin));
            return ShowCompiler.Hash(sb.ToString());
        }

        void Common(StringBuilder sb, string tag)
        {
            sb.Append(tag).Append(FormationGenerator.Revision).Append('|').Append(ShapeLibrary.Checksum)
                .Append("|n").Append(n).Append("|s").Append(Bits(doc.Limits.FormationSpacing)).Append('|');
        }

        static void Shape(StringBuilder sb, FormationSpec f)
        {
            sb.Append("shape(").Append((int)f.Kind).Append(',').Append((int)f.Style)
                .Append(',').Append(Bits(f.Center.X)).Append(',').Append(Bits(f.Center.Y)).Append(',').Append(Bits(f.Center.Z))
                .Append(',').Append(Bits(f.Size)).Append(',').Append(Bits(f.YawDegrees)).Append(',').Append(Bits(f.PitchDegrees))
                .Append(',').Append(f.Layers).Append(',').Append(f.Points).Append(',').Append(Bits(f.Turns))
                .Append(",t:").Append(f.Text.Length).Append(':').Append(f.Text)
                .Append(",m:").Append(f.Model.Length).Append(':').Append(f.Model)
                .Append(",p:").Append(f.CustomPoints.Count);
            foreach (Vector3 v in f.CustomPoints) sb.Append(',').Append(Bits(v.X)).Append(',').Append(Bits(v.Y)).Append(',').Append(Bits(v.Z));
            sb.Append(')');
        }

        void Side(StringBuilder sb, int k)
        {
            if (k < 0 || k >= cueCount)
            {
                LaunchPadSpec p = doc.Pad;
                sb.Append("pad(").Append(Bits(p.Center.X)).Append(',').Append(Bits(p.Center.Y)).Append(',').Append(Bits(p.Center.Z))
                    .Append(',').Append(Bits(p.Spacing)).Append(',').Append(Bits(p.HoverAltitude)).Append(',').Append(p.Columns).Append(')');
                return;
            }
            Cue c = doc.Cues[k];
            MotionSpec mo = c.Motion;
            sb.Append("cue(");
            Shape(sb, c.Formation);
            sb.Append("|mo:").Append((int)mo.Kind).Append(',').Append(Bits(mo.DegreesPerSecond)).Append(',').Append(Bits(mo.FrequencyHz))
                .Append(',').Append(Bits(mo.Amount)).Append("|h:").Append(Bits(c.HoldSeconds)).Append(')');
        }

        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

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
