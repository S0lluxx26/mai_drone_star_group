using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    public enum SegmentKind
    {
        /// <summary>Drones sit on their pads (before take-off or after landing).</summary>
        Ground,
        /// <summary>Vertical climb from the pads to the hover grid.</summary>
        Takeoff,
        /// <summary>Synchronised straight-line flight into a cue's formation.</summary>
        Transit,
        /// <summary>A cue's formation holds (optionally with a motion).</summary>
        Hold,
        /// <summary>Flight from the last formation back to the hover grid.</summary>
        Return,
        /// <summary>Vertical descent onto the pads.</summary>
        Landing,
    }

    public sealed class ShowSegment
    {
        public SegmentKind Kind;
        public float Start;
        public float End;

        /// <summary>Transit/Hold: the cue being flown into or held. Otherwise −1.</summary>
        public int CueIndex = -1;

        /// <summary>Transit/Return: the cue the drones leave (−1 for the hover grid).</summary>
        public int FromCueIndex = -1;

        public float Duration => End - Start;

        internal Vector3[] From;
        internal Vector3[] To;
    }

    public sealed class CueTiming
    {
        public int CueIndex;
        public float TransitStart;
        public float HoldStart;
        public float HoldEnd;

        /// <summary>Longest single-drone move of the incoming transition, metres.</summary>
        public float LongestMove;

        public FormationResult Formation;

        public float TransitSeconds => HoldStart - TransitStart;
        public float HoldSeconds => HoldEnd - HoldStart;
    }

    /// <summary>
    /// The flyable result of compiling a <see cref="ShowDocument"/>: a global segment timeline shared by
    /// every drone plus the per-drone endpoints of each segment. Sampling any instant is O(drones).
    /// </summary>
    public sealed class CompiledShow
    {
        readonly List<ShowSegment> segments;
        readonly List<CueTiming> cueTimings;
        readonly int[][] slotOfDrone;
        readonly float[] segmentStarts;

        internal CompiledShow(ShowDocument document, List<ShowSegment> segments, List<CueTiming> cueTimings,
            int[][] slotOfDrone, Vector3[] pads, Vector3[] hover)
        {
            Document = document;
            this.segments = segments;
            this.cueTimings = cueTimings;
            this.slotOfDrone = slotOfDrone;
            PadPositions = pads;
            HoverPositions = hover;
            DroneCount = document.DroneCount;
            Duration = segments.Count > 0 ? segments[segments.Count - 1].End : 0f;
            segmentStarts = new float[segments.Count];
            for (int i = 0; i < segments.Count; i++) segmentStarts[i] = segments[i].Start;
        }

        /// <summary>The frozen, sanitised copy of the document this show was compiled from.</summary>
        public ShowDocument Document { get; }

        public int DroneCount { get; }
        public float Duration { get; }
        public IReadOnlyList<ShowSegment> Segments => segments;
        public IReadOnlyList<CueTiming> CueTimings => cueTimings;
        public Vector3[] PadPositions { get; }
        public Vector3[] HoverPositions { get; }

        public int SegmentIndexAt(float t)
        {
            // Segments are contiguous and never empty, so starts are strictly increasing.
            if (segments.Count == 0) return -1;
            if (!(t > segmentStarts[0])) return 0;
            int index = Array.BinarySearch(segmentStarts, t);
            if (index < 0) index = ~index - 1;
            return Math.Min(index, segments.Count - 1);
        }

        public ShowSegment SegmentAt(float t)
        {
            int i = SegmentIndexAt(t);
            return i < 0 ? null : segments[i];
        }

        /// <summary>The cue whose formation is flying in or holding at time t, or −1.</summary>
        public int CueAt(float t)
        {
            ShowSegment s = SegmentAt(t);
            return s == null ? -1 : s.CueIndex;
        }

        public int SlotOf(int cueIndex, int drone) => slotOfDrone[cueIndex][drone];

        public void SamplePositions(float t, Vector3[] positions)
        {
            Sample(t, positions, null);
        }

        /// <summary>Fills positions (metres) and, when <paramref name="colors"/> is not null, LED colours.</summary>
        public void Sample(float t, Vector3[] positions, LedColor[] colors)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (positions.Length < DroneCount) throw new ArgumentException("Position buffer is too small.", nameof(positions));
            if (colors != null && colors.Length < DroneCount) throw new ArgumentException("Colour buffer is too small.", nameof(colors));
            int index = SegmentIndexAt(t);
            if (index < 0) return;
            ShowSegment seg = segments[index];
            float x = seg.Duration > 0f ? ShowMath.Clamp01((t - seg.Start) / seg.Duration) : 1f;
            float s = ShowMath.MinimumJerk(x);
            int n = DroneCount;

            switch (seg.Kind)
            {
                case SegmentKind.Ground:
                    for (int i = 0; i < n; i++) positions[i] = seg.From[i];
                    if (colors != null)
                    {
                        bool before = index == 0;
                        float level = before ? ShowMath.Clamp01((t - seg.Start) / 1f) : 1f - x;
                        LedColor standby = LightEngine.StandbyColor * level;
                        for (int i = 0; i < n; i++) colors[i] = standby;
                    }
                    break;

                case SegmentKind.Takeoff:
                case SegmentKind.Landing:
                    for (int i = 0; i < n; i++) positions[i] = Vector3.Lerp(seg.From[i], seg.To[i], s);
                    if (colors != null)
                    {
                        for (int i = 0; i < n; i++) colors[i] = LightEngine.StandbyColor;
                    }
                    break;

                case SegmentKind.Transit:
                case SegmentKind.Return:
                    for (int i = 0; i < n; i++) positions[i] = Vector3.Lerp(seg.From[i], seg.To[i], s);
                    if (colors != null)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            LedColor from = CueColor(seg.FromCueIndex, i, t);
                            LedColor to = seg.Kind == SegmentKind.Transit ? CueColor(seg.CueIndex, i, t) : LightEngine.StandbyColor;
                            colors[i] = LedColor.Lerp(from, to, s);
                        }
                    }
                    break;

                case SegmentKind.Hold:
                {
                    CueTiming timing = cueTimings[seg.CueIndex];
                    Cue cue = Document.Cues[seg.CueIndex];
                    Slot[] slots = timing.Formation.Slots;
                    int[] map = slotOfDrone[seg.CueIndex];
                    // Use the cue's own hold length (not End − Start, which carries float rounding) so the
                    // last hold frame matches the positions the next transition was planned from.
                    float local = Math.Min(t - seg.Start, cue.HoldSeconds);
                    for (int i = 0; i < n; i++)
                    {
                        positions[i] = HoldMotion.Evaluate(cue.Motion, timing.Formation, slots[map[i]], local, cue.HoldSeconds);
                    }
                    if (colors != null)
                    {
                        for (int i = 0; i < n; i++) colors[i] = LightEngine.Evaluate(cue.Light, slots[map[i]], i, local);
                    }
                    break;
                }
            }
        }

        LedColor CueColor(int cueIndex, int drone, float t)
        {
            if (cueIndex < 0) return LightEngine.StandbyColor;
            CueTiming timing = cueTimings[cueIndex];
            Slot slot = timing.Formation.Slots[slotOfDrone[cueIndex][drone]];
            return LightEngine.Evaluate(Document.Cues[cueIndex].Light, slot, drone, t - timing.HoldStart);
        }
    }
}
