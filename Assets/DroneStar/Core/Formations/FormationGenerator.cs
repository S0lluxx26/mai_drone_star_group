using System;
using System.Collections.Generic;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>One position a drone can occupy in a formation.</summary>
    public struct Slot
    {
        /// <summary>World position in metres.</summary>
        public Vector3 Position;

        /// <summary>Shape-local coordinates divided by the half-size (roughly −1..1), used by light effects.</summary>
        public Vector3 Local;

        /// <summary>Ordering parameter in [0, 1): arc length along strokes, angle around filled shapes.</summary>
        public float U;

        /// <summary>A parked reserve slot: the drone holds here with its LED off.</summary>
        public bool Dark;
    }

    public sealed class FormationResult
    {
        public Slot[] Slots = new Slot[0];
        public int LitCount;

        /// <summary>Smallest distance between any two slots (lit or parked); +∞ with fewer than two slots.</summary>
        public float MinSpacing = float.PositiveInfinity;

        public Vector3 Center;
        public Vector3 Right = Vector3.UnitX;
        public Vector3 Up = Vector3.UnitY;
        public Vector3 Normal = Vector3.UnitZ;
        public float HalfSize = 1f;

        public int DarkCount => Slots.Length - LitCount;
    }

    /// <summary>
    /// Turns a <see cref="FormationSpec"/> into exactly <c>droneCount</c> slots whose pairwise distance is at
    /// least <c>spacing</c>. When the shape cannot hold every drone at that spacing, the surplus drones are
    /// parked with their lights off on a horizontal reserve grid behind the formation.
    /// </summary>
    public static class FormationGenerator
    {
        struct Pt
        {
            public Vector3 P;
            public float U;

            public Pt(Vector3 p, float u)
            {
                P = p;
                U = u;
            }
        }

        sealed class Polyline
        {
            public readonly Vector3[] Points;
            public readonly bool Closed;
            public readonly float Length;

            public Polyline(Vector3[] points, bool closed)
            {
                Points = points;
                Closed = closed;
                float length = 0f;
                int segments = closed ? points.Length : points.Length - 1;
                for (int i = 0; i < segments; i++) length += Vector3.Distance(points[i], points[(i + 1) % points.Length]);
                Length = length;
            }

            public Vector3 At(float arc)
            {
                int segments = Closed ? Points.Length : Points.Length - 1;
                for (int i = 0; i < segments; i++)
                {
                    Vector3 a = Points[i];
                    Vector3 b = Points[(i + 1) % Points.Length];
                    float len = Vector3.Distance(a, b);
                    if (arc <= len || i == segments - 1)
                    {
                        return len > 1e-6f ? Vector3.Lerp(a, b, ShowMath.Clamp01(arc / len)) : a;
                    }
                    arc -= len;
                }
                return Points[0];
            }
        }

        sealed class Region
        {
            public Func<Vector2, bool> Inside;
            public float MinX, MaxX, MinY, MaxY;
        }

        struct Ellipse
        {
            public Vector2 C;
            public float A, B, Angle;

            public Ellipse(float cx, float cy, float a, float b, float angleDegrees)
            {
                C = new Vector2(cx, cy);
                A = a;
                B = b;
                Angle = angleDegrees * ShowMath.Deg2Rad;
            }

            public bool Contains(Vector2 p)
            {
                Vector2 d = p - C;
                float cos = MathF.Cos(-Angle), sin = MathF.Sin(-Angle);
                float x = d.X * cos - d.Y * sin;
                float y = d.X * sin + d.Y * cos;
                return (x * x) / (A * A) + (y * y) / (B * B) < 1f;
            }

            public Vector2 PointAt(float t)
            {
                float x = A * MathF.Cos(t), y = B * MathF.Sin(t);
                float cos = MathF.Cos(Angle), sin = MathF.Sin(Angle);
                return C + new Vector2(x * cos - y * sin, x * sin + y * cos);
            }
        }

        public static bool IsPlanar(FormationSpec spec)
        {
            switch (spec.Kind)
            {
                case FormationKind.Grid:
                case FormationKind.Star:
                case FormationKind.Heart:
                case FormationKind.Text:
                case FormationKind.Flower:
                case FormationKind.Butterfly:
                case FormationKind.Galaxy:
                case FormationKind.Wave:
                    return true;
                case FormationKind.Ring:
                    return spec.Style == FillStyle.Outline;
                case FormationKind.Custom:
                    // Drawn sketches are flat; imported 3D point clouds are not.
                    return spec.CustomPoints != null && spec.CustomPoints.Count > 0 && spec.CustomPoints.TrueForAll(p => p.Z == 0f);
                default:
                    return false;
            }
        }

        /// <param name="reserveGrowth">Fraction the formation may grow while holding (Breathe), so parked drones stay clear.</param>
        /// <param name="reserveMargin">Extra metres the formation may move while holding (Wave).</param>
        public static FormationResult Generate(FormationSpec spec, int droneCount, float spacing, float reserveGrowth = 0f, float reserveMargin = 0f)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            droneCount = Math.Max(0, droneCount);
            spacing = ShowMath.IsFinite(spacing) ? Math.Max(spacing, 0.1f) : 1f;
            float half = Math.Max(ShowMath.IsFinite(spec.Size) ? spec.Size : 1f, 1f) * 0.5f;

            List<Pt> lit = droneCount == 0 ? new List<Pt>() : GreedyThin(BuildLit(spec, droneCount, spacing, half), spacing);
            // Crossings and junctions can thin a few points away; ask for more and keep the best fit.
            for (int attempt = 0; attempt < 2 && lit.Count > 0 && lit.Count < droneCount; attempt++)
            {
                int deficit = droneCount - lit.Count;
                List<Pt> more = GreedyThin(BuildLit(spec, droneCount + deficit * 2, spacing, half), spacing);
                if (more.Count <= lit.Count) break;
                lit = more;
            }
            if (lit.Count > droneCount) lit = StridePick(lit, droneCount);

            Quaternion q = ShowMath.Orientation(spec.YawDegrees, spec.PitchDegrees);
            var result = new FormationResult
            {
                Center = spec.Center,
                Right = Vector3.Transform(Vector3.UnitX, q),
                Up = Vector3.Transform(Vector3.UnitY, q),
                Normal = Vector3.Transform(Vector3.UnitZ, q),
                HalfSize = half,
                Slots = new Slot[droneCount],
                LitCount = lit.Count,
            };

            float reach = 0f;
            for (int i = 0; i < lit.Count; i++)
            {
                Vector3 world = spec.Center + Vector3.Transform(lit[i].P, q);
                result.Slots[i] = new Slot { Position = world, Local = lit[i].P / half, U = ShowMath.Frac(lit[i].U), Dark = false };
                reach = Math.Max(reach, lit[i].P.Length());
            }

            int dark = droneCount - lit.Count;
            if (dark > 0)
            {
                // Parked drones hold still on a horizontal grid behind the formation, outside the sphere the
                // lit drones can sweep while spinning, breathing or waving.
                float s = spacing * 1.05f;
                float clearance = reach * (1f + Math.Max(0f, reserveGrowth)) + Math.Max(0f, reserveMargin);
                float zStart = spec.Center.Z + clearance + spacing * 3f;
                int cols = (int)MathF.Ceiling(MathF.Sqrt(dark));
                for (int k = 0; k < dark; k++)
                {
                    int row = k / cols, col = k % cols;
                    var p = new Vector3(spec.Center.X + (col - (cols - 1) * 0.5f) * s, spec.Center.Y, zStart + row * s);
                    result.Slots[lit.Count + k] = new Slot { Position = p, Local = Vector3.Zero, U = 0f, Dark = true };
                }
            }

            result.MinSpacing = MinDistance(result.Slots);
            return result;
        }

        /// <summary>Exact smallest pairwise distance (brute force; formations hold at most 1000 slots).</summary>
        public static float MinDistance(Slot[] slots)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < slots.Length; i++)
            {
                Vector3 a = slots[i].Position;
                for (int j = i + 1; j < slots.Length; j++)
                {
                    float d = Vector3.DistanceSquared(a, slots[j].Position);
                    if (d < best) best = d;
                }
            }
            return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
        }

        static List<Pt> BuildLit(FormationSpec spec, int n, float spacing, float half)
        {
            int layers = IsPlanar(spec) ? ShowMath.Clamp(spec.Layers, 1, ShowBounds.MaxLayers) : 1;
            int perLayer = (n + layers - 1) / layers;
            List<Pt> sheet = BuildShape(spec, perLayer, spacing, half);
            if (layers == 1) return sheet;

            float dz = spacing * 1.05f;
            var all = new List<Pt>(sheet.Count * layers);
            for (int k = 0; k < layers; k++)
            {
                float z = (k - (layers - 1) * 0.5f) * dz;
                foreach (Pt p in sheet) all.Add(new Pt(p.P + new Vector3(0f, 0f, z), p.U));
            }
            return all.Count > n ? StridePick(all, n) : all;
        }

        static List<Pt> BuildShape(FormationSpec spec, int n, float spacing, float half)
        {
            bool filled = spec.Style == FillStyle.Filled;
            switch (spec.Kind)
            {
                case FormationKind.Grid:
                    return filled
                        ? FitParametric(k => GridWall(k, half), n, spacing)
                        : SamplePolylines(new List<Polyline> { Rectangle(half, half * 0.6f) }, n, spacing);
                case FormationKind.Ring:
                    return filled
                        ? FitParametric(k => Torus(k, half), n, spacing)
                        : SamplePolylines(new List<Polyline> { Circle(half, 160) }, n, spacing);
                case FormationKind.Sphere:
                    return filled
                        ? FitParametric(k => Fibonacci(k, half), n, spacing)
                        : SamplePolylines(Globe(half), n, spacing);
                case FormationKind.Star:
                    return PolygonShape(StarPolygon(ShowMath.Clamp(spec.Points, 3, ShowBounds.MaxPoints), half), filled, n, spacing);
                case FormationKind.Heart:
                    return PolygonShape(HeartPolygon(half), filled, n, spacing);
                case FormationKind.Flower:
                    return PolygonShape(FlowerPolygon(ShowMath.Clamp(spec.Points, 3, ShowBounds.MaxPoints), half), filled, n, spacing);
                case FormationKind.Butterfly:
                    return Butterfly(half, filled, n, spacing);
                case FormationKind.Text:
                    return TextShape(spec.Text, half, filled, n, spacing);
                case FormationKind.Helix:
                    return SamplePolylines(Helix(half, spec.Turns, filled), n, spacing);
                case FormationKind.Galaxy:
                    return Galaxy(ShowMath.Clamp(spec.Points, 2, 6), half, filled, n, spacing);
                case FormationKind.Wave:
                    return filled
                        ? FitParametric(k => WaveSheet(k, half), n, spacing)
                        : SamplePolylines(WaveRows(half), n, spacing);
                case FormationKind.Cube:
                    return filled ? CubeSurface(n, spacing, half) : SamplePolylines(CubeEdges(half), n, spacing);
                case FormationKind.Custom:
                    return CustomShape(spec.CustomPoints, half, n, spacing);
                default:
                    return new List<Pt>();
            }
        }

        // ---------------------------------------------------------------- sampling strategies

        /// <summary>Largest point count (≤ n) whose layout keeps every pair at least <paramref name="spacing"/> apart.</summary>
        static List<Pt> FitParametric(Func<int, List<Pt>> build, int n, float spacing)
        {
            List<Pt> first = build(n);
            if (HasSpacing(first, spacing)) return first;
            int lo = 0, hi = n;
            List<Pt> best = new List<Pt>();
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                List<Pt> candidate = build(mid);
                if (HasSpacing(candidate, spacing))
                {
                    lo = mid;
                    best = candidate;
                }
                else
                {
                    hi = mid;
                }
            }
            return best;
        }

        /// <summary>
        /// Distributes up to n points evenly by arc length over the polylines, never closer than
        /// <paramref name="spacing"/> along a line. Crossings are thinned afterwards.
        /// </summary>
        static List<Pt> SamplePolylines(List<Polyline> lines, int n, float spacing)
        {
            var result = new List<Pt>();
            float total = 0f;
            foreach (Polyline line in lines) total += line.Length;
            if (n <= 0 || total <= 1e-5f) return result;

            int target = Math.Min(n, Math.Max(1, (int)MathF.Floor(total / spacing)));
            int[] quota = new int[lines.Count];
            int[] cap = new int[lines.Count];
            float[] remainder = new float[lines.Count];
            int assigned = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                cap[i] = lines[i].Length > 1e-5f ? Math.Max(1, (int)MathF.Floor(lines[i].Length / spacing)) : 0;
                float raw = target * lines[i].Length / total;
                quota[i] = Math.Min(cap[i], (int)MathF.Floor(raw));
                remainder[i] = raw - quota[i];
                assigned += quota[i];
            }
            while (assigned < target)
            {
                int pick = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (quota[i] >= cap[i]) continue;
                    if (pick < 0 || remainder[i] > remainder[pick]) pick = i;
                }
                if (pick < 0) break;
                quota[pick]++;
                remainder[pick] -= 1f;
                assigned++;
            }

            float before = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                Polyline line = lines[i];
                int k = quota[i];
                if (k > 0)
                {
                    float step = line.Length / k;
                    for (int j = 0; j < k; j++)
                    {
                        float arc = (j + 0.5f) * step;
                        result.Add(new Pt(line.At(arc), (before + arc) / total));
                    }
                }
                before += line.Length;
            }
            return result;
        }

        /// <summary>
        /// Fills a planar region with a hexagonal lattice whose pitch is as large as possible while still
        /// producing n points, and never below <paramref name="spacing"/>.
        /// </summary>
        static List<Pt> LatticeFill(Region region, int n, float spacing)
        {
            var result = new List<Pt>();
            if (n <= 0) return result;
            float lo = spacing;
            List<Vector2> pts;
            if (LatticeCount(region, lo) <= n)
            {
                pts = LatticePoints(region, lo);
            }
            else
            {
                float hi = spacing * 2f;
                for (int guard = 0; guard < 24 && LatticeCount(region, hi) >= n; guard++)
                {
                    lo = hi;
                    hi *= 2f;
                }
                for (int iter = 0; iter < 26; iter++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (LatticeCount(region, mid) >= n) lo = mid;
                    else hi = mid;
                }
                pts = StridePick(LatticePoints(region, lo), n);
            }
            foreach (Vector2 p in pts)
            {
                result.Add(new Pt(new Vector3(p.X, p.Y, 0f), AngleU(p)));
            }
            return result;
        }

        static int LatticeCount(Region region, float d)
        {
            int count = 0;
            EnumerateLattice(region, d, _ => count++);
            return count;
        }

        static List<Vector2> LatticePoints(Region region, float d)
        {
            var list = new List<Vector2>();
            EnumerateLattice(region, d, p => list.Add(p));
            return list;
        }

        static void EnumerateLattice(Region region, float d, Action<Vector2> visit)
        {
            float rowHeight = d * 0.8660254f;
            int j0 = (int)MathF.Floor(region.MinY / rowHeight) - 1;
            int j1 = (int)MathF.Ceiling(region.MaxY / rowHeight) + 1;
            int i0 = (int)MathF.Floor(region.MinX / d) - 2;
            int i1 = (int)MathF.Ceiling(region.MaxX / d) + 2;
            if ((long)(j1 - j0 + 1) * (i1 - i0 + 1) > 4_000_000L) return;
            for (int j = j0; j <= j1; j++)
            {
                float y = j * rowHeight;
                float offset = (j & 1) != 0 ? 0.5f * d : 0f;
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2(i * d + offset, y);
                    if (region.Inside(p)) visit(p);
                }
            }
        }

        static List<T> StridePick<T>(List<T> source, int n)
        {
            if (n >= source.Count) return source;
            var picked = new List<T>(n);
            if (n <= 0) return picked;
            double step = (double)source.Count / n;
            for (int i = 0; i < n; i++)
            {
                int index = Math.Min(source.Count - 1, (int)((i + 0.5) * step));
                picked.Add(source[index]);
            }
            return picked;
        }

        static bool HasSpacing(List<Pt> pts, float spacing)
        {
            if (pts.Count < 2) return true;
            var positions = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) positions[i] = pts[i].P;
            var grid = new SpatialGrid();
            grid.Build(positions, positions.Length, spacing);
            float limit = spacing * spacing * 0.9999f;
            for (int i = 0; i < positions.Length; i++)
            {
                grid.CellOf(positions[i], out int cx, out int cy, out int cz);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int j = grid.First(cx + dx, cy + dy, cz + dz); j >= 0; j = grid.Next(j))
                    {
                        if (j <= i) continue;
                        if (Vector3.DistanceSquared(positions[i], positions[j]) < limit) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Keeps points in order, dropping any that come closer than the spacing to an earlier kept point.</summary>
        static List<Pt> GreedyThin(List<Pt> pts, float spacing)
        {
            if (pts.Count < 2) return pts;
            var positions = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) positions[i] = pts[i].P;
            var grid = new SpatialGrid();
            grid.Build(positions, positions.Length, spacing);
            var kept = new bool[pts.Count];
            var result = new List<Pt>(pts.Count);
            float limit = spacing * spacing * 0.9999f;
            for (int i = 0; i < positions.Length; i++)
            {
                bool clear = true;
                grid.CellOf(positions[i], out int cx, out int cy, out int cz);
                for (int dx = -1; dx <= 1 && clear; dx++)
                for (int dy = -1; dy <= 1 && clear; dy++)
                for (int dz = -1; dz <= 1 && clear; dz++)
                {
                    for (int j = grid.First(cx + dx, cy + dy, cz + dz); j >= 0; j = grid.Next(j))
                    {
                        if (j < i && kept[j] && Vector3.DistanceSquared(positions[i], positions[j]) < limit)
                        {
                            clear = false;
                            break;
                        }
                    }
                }
                if (!clear) continue;
                kept[i] = true;
                result.Add(pts[i]);
            }
            return result;
        }

        static float AngleU(Vector2 p)
        {
            if (p.LengthSquared() < 1e-8f) return 0f;
            return ShowMath.Frac(0.25f - MathF.Atan2(p.Y, p.X) / ShowMath.TwoPi);
        }

        // ---------------------------------------------------------------- shapes

        static List<Pt> GridWall(int k, float half)
        {
            var pts = new List<Pt>(k);
            if (k <= 0) return pts;
            float width = half * 2f, height = half * 1.2f;
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(k * width / height)));
            int rows = (k + cols - 1) / cols;
            float d = Math.Min(cols > 1 ? width / (cols - 1) : width, rows > 1 ? height / (rows - 1) : height);
            for (int i = 0; i < k; i++)
            {
                int r = i / cols, c = i % cols;
                int inRow = r == rows - 1 ? k - r * cols : cols;
                float x = (c - (inRow - 1) * 0.5f) * d;
                float y = ((rows - 1) * 0.5f - r) * d;
                pts.Add(new Pt(new Vector3(x, y, 0f), (x / Math.Max(width, 1e-3f)) + 0.5f));
            }
            return pts;
        }

        static List<Pt> WaveSheet(int k, float half)
        {
            var pts = new List<Pt>(k);
            if (k <= 0) return pts;
            float width = half * 2f, height = half * 0.9f;
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(k * width / height)));
            int rows = (k + cols - 1) / cols;
            float d = Math.Min(cols > 1 ? width / (cols - 1) : width, rows > 1 ? height / (rows - 1) : height);
            for (int i = 0; i < k; i++)
            {
                int r = i / cols, c = i % cols;
                int inRow = r == rows - 1 ? k - r * cols : cols;
                float x = (c - (inRow - 1) * 0.5f) * d;
                float y = ((rows - 1) * 0.5f - r) * d;
                pts.Add(new Pt(new Vector3(x, y, WaveDepth(x, half)), (x / width) + 0.5f));
            }
            return pts;
        }

        static float WaveDepth(float x, float half) => 0.2f * half * MathF.Sin(ShowMath.TwoPi * 0.75f * x / half);

        static List<Polyline> WaveRows(float half)
        {
            var lines = new List<Polyline>();
            for (int r = 0; r < 5; r++)
            {
                float y = (r - 2) * half * 0.2f;
                var pts = new Vector3[97];
                for (int i = 0; i < pts.Length; i++)
                {
                    float x = -half + 2f * half * i / (pts.Length - 1);
                    pts[i] = new Vector3(x, y, WaveDepth(x, half));
                }
                lines.Add(new Polyline(pts, false));
            }
            return lines;
        }

        static List<Pt> Fibonacci(int k, float radius)
        {
            var pts = new List<Pt>(k);
            float golden = MathF.PI * (3f - MathF.Sqrt(5f));
            for (int i = 0; i < k; i++)
            {
                float y = 1f - 2f * (i + 0.5f) / k;
                float r = MathF.Sqrt(Math.Max(0f, 1f - y * y));
                float theta = golden * i;
                pts.Add(new Pt(new Vector3(r * MathF.Cos(theta), y, r * MathF.Sin(theta)) * radius, (float)i / Math.Max(k, 1)));
            }
            return pts;
        }

        static List<Pt> Torus(int k, float half)
        {
            var pts = new List<Pt>();
            if (k <= 0) return pts;
            float major = half * 0.8f, minor = half * 0.2f;
            int around = Math.Max(3, (int)MathF.Round(MathF.Sqrt(k * minor / major)));
            int along = (k + around - 1) / around;
            for (int i = 0; i < along; i++)
            {
                for (int j = 0; j < around; j++)
                {
                    float theta = ShowMath.TwoPi * (i + 0.5f * (j & 1)) / along;
                    float phi = ShowMath.TwoPi * j / around;
                    float ring = major + minor * MathF.Cos(phi);
                    var p = new Vector3(ring * MathF.Cos(theta), ring * MathF.Sin(theta), minor * MathF.Sin(phi));
                    pts.Add(new Pt(p, theta / ShowMath.TwoPi));
                }
            }
            return StridePick(pts, k);
        }

        static List<Pt> CubeSurface(int n, float spacing, float half)
        {
            float edge = half * 1.2f;
            int kMax = Math.Max(2, (int)MathF.Floor(edge / spacing) + 1);
            int k = kMax;
            for (int candidate = 2; candidate <= kMax; candidate++)
            {
                if (CubeSurfaceCount(candidate) >= n)
                {
                    k = candidate;
                    break;
                }
            }
            var pts = new List<Pt>(CubeSurfaceCount(k));
            float step = edge / (k - 1);
            for (int x = 0; x < k; x++)
            for (int y = 0; y < k; y++)
            {
                bool side = x == 0 || x == k - 1 || y == 0 || y == k - 1;
                for (int z = 0; z < k; z++)
                {
                    if (!side && z != 0 && z != k - 1) continue;
                    var p = new Vector3(x * step - edge * 0.5f, y * step - edge * 0.5f, z * step - edge * 0.5f);
                    pts.Add(new Pt(p, AngleU(new Vector2(p.X, p.Z))));
                }
            }
            return StridePick(pts, n);
        }

        static int CubeSurfaceCount(int k) => k < 2 ? k : k * k * k - (k - 2) * (k - 2) * (k - 2);

        static List<Polyline> CubeEdges(float half)
        {
            float a = half * 0.6f;
            var lines = new List<Polyline>();
            for (int axis = 0; axis < 3; axis++)
            {
                for (int s1 = -1; s1 <= 1; s1 += 2)
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    Vector3 from, to;
                    if (axis == 0) { from = new Vector3(-a, s1 * a, s2 * a); to = new Vector3(a, s1 * a, s2 * a); }
                    else if (axis == 1) { from = new Vector3(s1 * a, -a, s2 * a); to = new Vector3(s1 * a, a, s2 * a); }
                    else { from = new Vector3(s1 * a, s2 * a, -a); to = new Vector3(s1 * a, s2 * a, a); }
                    lines.Add(new Polyline(new[] { from, to }, false));
                }
            }
            return lines;
        }

        static Polyline Rectangle(float halfWidth, float halfHeight)
        {
            return new Polyline(new[]
            {
                new Vector3(-halfWidth, halfHeight, 0f), new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f), new Vector3(-halfWidth, -halfHeight, 0f),
            }, true);
        }

        static Polyline Circle(float radius, int segments)
        {
            var pts = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float a = MathF.PI * 0.5f - ShowMath.TwoPi * i / segments;
                pts[i] = new Vector3(radius * MathF.Cos(a), radius * MathF.Sin(a), 0f);
            }
            return new Polyline(pts, true);
        }

        static List<Polyline> Globe(float radius)
        {
            var lines = new List<Polyline>();
            foreach (float latDeg in new[] { -50f, 0f, 50f })
            {
                float lat = latDeg * ShowMath.Deg2Rad;
                float r = radius * MathF.Cos(lat), y = radius * MathF.Sin(lat);
                var pts = new Vector3[128];
                for (int i = 0; i < pts.Length; i++)
                {
                    float a = ShowMath.TwoPi * i / pts.Length;
                    pts[i] = new Vector3(r * MathF.Cos(a), y, r * MathF.Sin(a));
                }
                lines.Add(new Polyline(pts, true));
            }
            for (int m = 0; m < 8; m++)
            {
                float lon = ShowMath.TwoPi * m / 8f;
                var pts = new Vector3[48];
                for (int i = 0; i < pts.Length; i++)
                {
                    float lat = (-68f + 136f * i / (pts.Length - 1)) * ShowMath.Deg2Rad;
                    pts[i] = new Vector3(radius * MathF.Cos(lat) * MathF.Cos(lon), radius * MathF.Sin(lat), radius * MathF.Cos(lat) * MathF.Sin(lon));
                }
                lines.Add(new Polyline(pts, false));
            }
            return lines;
        }

        static List<Polyline> Helix(float half, float turns, bool rungs)
        {
            float height = half * 2f, radius = half * 0.45f;
            turns = ShowMath.Clamp(ShowMath.IsFinite(turns) ? turns : 2f, ShowBounds.MinTurns, ShowBounds.MaxTurns);
            Vector3 StrandAt(float t, float phase)
            {
                float a = ShowMath.TwoPi * turns * t + phase;
                return new Vector3(radius * MathF.Cos(a), height * (t - 0.5f), radius * MathF.Sin(a));
            }

            var lines = new List<Polyline>();
            for (int s = 0; s < 2; s++)
            {
                var pts = new Vector3[Math.Max(64, (int)(turns * 64))];
                for (int i = 0; i < pts.Length; i++) pts[i] = StrandAt((float)i / (pts.Length - 1), s * MathF.PI);
                lines.Add(new Polyline(pts, false));
            }
            if (rungs)
            {
                int count = Math.Max(4, (int)MathF.Round(turns * 7f));
                for (int r = 0; r < count; r++)
                {
                    float t = (r + 0.5f) / count;
                    Vector3 a = StrandAt(t, 0f), b = StrandAt(t, MathF.PI);
                    lines.Add(new Polyline(new[] { Vector3.Lerp(a, b, 0.16f), Vector3.Lerp(a, b, 0.84f) }, false));
                }
            }
            return lines;
        }

        static List<Pt> Galaxy(int arms, float half, bool filled, int n, float spacing)
        {
            float coreRadius = half * 0.2f;
            var core = new List<Pt>();
            if (filled)
            {
                var disk = new Region
                {
                    Inside = p => p.LengthSquared() <= coreRadius * coreRadius,
                    MinX = -coreRadius, MaxX = coreRadius, MinY = -coreRadius, MaxY = coreRadius,
                };
                core = LatticeFill(disk, Math.Max(1, (int)(n * 0.16f)), spacing);
                for (int i = 0; i < core.Count; i++) core[i] = new Pt(core[i].P, 0.999f * core[i].P.Length() / half);
            }

            // Filled arms start just outside the core so their inner lane never crowds it.
            float innerRadius = filled ? Math.Min(coreRadius + spacing * 2.2f, half * 0.5f) : half * 0.1f;
            float sweep = ShowMath.TwoPi * 1.2f;
            float growth = MathF.Log(half / innerRadius) / sweep;
            var lines = new List<Polyline>();
            float[] offsets = filled ? new[] { -1f, 0f, 1f } : new[] { 0f };
            for (int a = 0; a < arms; a++)
            {
                foreach (float o in offsets)
                {
                    var pts = new Vector3[97];
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float s = (float)i / (pts.Length - 1);
                        float theta = sweep * s;
                        float r = innerRadius * MathF.Exp(growth * theta);
                        // Side lanes thicken the arm but never sit closer than the drone spacing.
                        r += o * Math.Max(spacing * 1.05f, 0.1f * r * (0.35f + 0.65f * s));
                        float angle = theta + ShowMath.TwoPi * a / arms;
                        pts[i] = new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), 0f);
                    }
                    lines.Add(new Polyline(pts, false));
                }
            }

            List<Pt> armPts = SamplePolylines(lines, Math.Max(0, n - core.Count), spacing);
            var all = new List<Pt>(core.Count + armPts.Count);
            all.AddRange(core);
            all.AddRange(armPts);
            for (int i = 0; i < all.Count; i++)
            {
                float jitter = (ShowMath.Hash01(i, 7919) - 0.5f) * 0.1f * half;
                all[i] = new Pt(all[i].P + new Vector3(0f, 0f, jitter), all[i].U);
            }
            return all;
        }

        static List<Pt> PolygonShape(Vector2[] polygon, bool filled, int n, float spacing)
        {
            if (!filled)
            {
                var pts = new Vector3[polygon.Length];
                for (int i = 0; i < polygon.Length; i++) pts[i] = new Vector3(polygon[i].X, polygon[i].Y, 0f);
                return SamplePolylines(new List<Polyline> { new Polyline(pts, true) }, n, spacing);
            }
            var region = new Region { Inside = p => InPolygon(polygon, p) };
            Bounds(polygon, out region.MinX, out region.MaxX, out region.MinY, out region.MaxY);
            return LatticeFill(region, n, spacing);
        }

        static Vector2[] StarPolygon(int points, float half)
        {
            float inner = half * (points <= 5 ? 0.46f : 0.56f);
            var poly = new Vector2[points * 2];
            for (int i = 0; i < poly.Length; i++)
            {
                float r = (i & 1) == 0 ? half : inner;
                float a = MathF.PI * 0.5f - MathF.PI * i / points;
                poly[i] = new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
            }
            return poly;
        }

        static Vector2[] HeartPolygon(float half)
        {
            var poly = new Vector2[180];
            for (int i = 0; i < poly.Length; i++)
            {
                float t = ShowMath.TwoPi * i / poly.Length;
                float s = MathF.Sin(t);
                float x = 16f * s * s * s;
                float y = 13f * MathF.Cos(t) - 5f * MathF.Cos(2f * t) - 2f * MathF.Cos(3f * t) - MathF.Cos(4f * t);
                poly[i] = new Vector2(x, y);
            }
            return FitToWidth(poly, half);
        }

        static Vector2[] FlowerPolygon(int petals, float half)
        {
            var poly = new Vector2[288];
            for (int i = 0; i < poly.Length; i++)
            {
                float a = ShowMath.TwoPi * i / poly.Length;
                float r = half * (0.6f + 0.4f * MathF.Cos(petals * (a - MathF.PI * 0.5f)));
                poly[i] = new Vector2(r * MathF.Cos(a), r * MathF.Sin(a));
            }
            return poly;
        }

        /// <summary>Scales and recentres a polygon so its bounding box is centred and 2·half wide.</summary>
        static Vector2[] FitToWidth(Vector2[] poly, float half)
        {
            Bounds(poly, out float minX, out float maxX, out float minY, out float maxY);
            float scale = 2f * half / Math.Max(maxX - minX, 1e-5f);
            var c = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
            var result = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) result[i] = (poly[i] - c) * scale;
            return result;
        }

        static readonly Ellipse[] ButterflyParts =
        {
            new Ellipse(0.47f, 0.22f, 0.5f, 0.34f, 28f),
            new Ellipse(-0.47f, 0.22f, 0.5f, 0.34f, -28f),
            new Ellipse(0.33f, -0.33f, 0.34f, 0.24f, -30f),
            new Ellipse(-0.33f, -0.33f, 0.34f, 0.24f, 30f),
            new Ellipse(0f, -0.02f, 0.06f, 0.42f, 0f),
        };

        static List<Pt> Butterfly(float half, bool filled, int n, float spacing)
        {
            if (filled)
            {
                var region = new Region
                {
                    Inside = p =>
                    {
                        Vector2 q = p / half;
                        foreach (Ellipse e in ButterflyParts)
                        {
                            if (e.Contains(q)) return true;
                        }
                        return false;
                    },
                    MinX = -half, MaxX = half, MinY = -0.8f * half, MaxY = 0.8f * half,
                };
                return LatticeFill(region, n, spacing);
            }

            // Outline: the union boundary, i.e. each ellipse's rim minus the parts hidden inside another part.
            var lines = new List<Polyline>();
            const int samples = 180;
            for (int e = 0; e < ButterflyParts.Length; e++)
            {
                var rim = new Vector2[samples];
                var exposed = new bool[samples];
                int firstHidden = -1;
                for (int i = 0; i < samples; i++)
                {
                    rim[i] = ButterflyParts[e].PointAt(ShowMath.TwoPi * i / samples);
                    exposed[i] = true;
                    for (int o = 0; o < ButterflyParts.Length; o++)
                    {
                        if (o != e && ButterflyParts[o].Contains(rim[i]))
                        {
                            exposed[i] = false;
                            break;
                        }
                    }
                    if (!exposed[i] && firstHidden < 0) firstHidden = i;
                }
                if (firstHidden < 0)
                {
                    lines.Add(new Polyline(ToVector3(rim, half), true));
                    continue;
                }
                var run = new List<Vector2>();
                for (int step = 1; step <= samples; step++)
                {
                    int i = (firstHidden + step) % samples;
                    if (exposed[i])
                    {
                        run.Add(rim[i]);
                    }
                    else if (run.Count > 0)
                    {
                        if (run.Count > 1) lines.Add(new Polyline(ToVector3(run.ToArray(), half), false));
                        run.Clear();
                    }
                }
                if (run.Count > 1) lines.Add(new Polyline(ToVector3(run.ToArray(), half), false));
            }
            return SamplePolylines(lines, n, spacing);
        }

        static Vector3[] ToVector3(Vector2[] pts, float scale)
        {
            var result = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) result[i] = new Vector3(pts[i].X * scale, pts[i].Y * scale, 0f);
            return result;
        }

        static List<Pt> TextShape(string text, float half, bool filled, int n, float spacing)
        {
            List<Vector2[]> strokes = StrokeFont.Layout(text, half * 2f, out float unit);
            if (strokes.Count == 0) return new List<Pt>();
            if (!filled)
            {
                var lines = new List<Polyline>(strokes.Count);
                foreach (Vector2[] stroke in strokes)
                {
                    lines.Add(new Polyline(ToVector3(stroke, 1f), false));
                }
                return SamplePolylines(lines, n, spacing);
            }

            float hw = unit * 0.62f;
            var segA = new List<Vector2>();
            var segB = new List<Vector2>();
            foreach (Vector2[] stroke in strokes)
            {
                for (int i = 0; i + 1 < stroke.Length; i++)
                {
                    segA.Add(stroke[i]);
                    segB.Add(stroke[i + 1]);
                }
                if (stroke.Length == 1)
                {
                    segA.Add(stroke[0]);
                    segB.Add(stroke[0]);
                }
            }
            Vector2[] a = segA.ToArray(), b = segB.ToArray();
            float hw2 = hw * hw;
            var region = new Region
            {
                Inside = p =>
                {
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (p.X < Math.Min(a[i].X, b[i].X) - hw || p.X > Math.Max(a[i].X, b[i].X) + hw) continue;
                        if (SegmentDistanceSquared(p, a[i], b[i]) <= hw2) return true;
                    }
                    return false;
                },
                MinX = -half - hw, MaxX = half + hw, MinY = -3f * unit - hw, MaxY = 3f * unit + hw,
            };
            List<Pt> pts = LatticeFill(region, n, spacing);
            for (int i = 0; i < pts.Count; i++)
            {
                pts[i] = new Pt(pts[i].P, (pts[i].P.X + half) / (2f * half));
            }
            return pts;
        }

        static List<Pt> CustomShape(List<Vector3> points, float half, int n, float spacing)
        {
            var pts = new List<Pt>();
            if (points == null || points.Count == 0) return pts;
            for (int i = 0; i < points.Count; i++)
            {
                pts.Add(new Pt(points[i] * half, (float)i / points.Count));
            }
            return StridePick(GreedyThin(pts, spacing), n);
        }

        // ---------------------------------------------------------------- geometry helpers

        static float SegmentDistanceSquared(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 > 1e-10f ? ShowMath.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.DistanceSquared(p, a + ab * t);
        }

        static bool InPolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        static void Bounds(Vector2[] poly, out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = minY = float.PositiveInfinity;
            maxX = maxY = float.NegativeInfinity;
            foreach (Vector2 p in poly)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }
        }
    }
}
