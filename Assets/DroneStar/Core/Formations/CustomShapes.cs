using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Builds the point lists behind Sketch (custom) formations: freehand strokes, imported CSV point
    /// clouds and the default doodle. Points are normalised so the shape's half-size is 1; the formation
    /// generator later scales them by the cue's size and thins them to safe spacing.
    /// </summary>
    public static class CustomShapes
    {
        /// <summary>Upper bound on stored points (the sanitizer keeps at most <see cref="ShowBounds.MaxDrones"/>).</summary>
        public const int DefaultMaxPoints = 900;

        /// <summary>
        /// Resamples freehand strokes (normalised coordinates, y up) evenly by arc length. Short strokes keep
        /// at least one point, so dots drawn with a single tap survive.
        /// </summary>
        public static List<Vector3> FromStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes, int maxPoints = DefaultMaxPoints)
        {
            var result = new List<Vector3>();
            if (strokes == null || maxPoints <= 0) return result;
            float total = 0f;
            foreach (IReadOnlyList<Vector2> s in strokes)
            {
                if (s == null) continue;
                for (int i = 1; i < s.Count; i++) total += Vector2.Distance(s[i - 1], s[i]);
            }
            // Step so the whole drawing fits the budget, but never finer than 1 % of the half-size.
            float step = Math.Max(0.01f, total / Math.Max(1, maxPoints - CountStrokes(strokes)));
            foreach (IReadOnlyList<Vector2> s in strokes)
            {
                if (s == null || s.Count == 0) continue;
                if (!IsFinite(s[0])) continue;
                result.Add(new Vector3(s[0].X, s[0].Y, 0f));
                float carry = 0f;
                for (int i = 1; i < s.Count && result.Count < maxPoints; i++)
                {
                    Vector2 a = s[i - 1], b = s[i];
                    if (!IsFinite(a) || !IsFinite(b)) continue;
                    float len = Vector2.Distance(a, b);
                    float at = step - carry;
                    while (at <= len && result.Count < maxPoints)
                    {
                        Vector2 p = Vector2.Lerp(a, b, at / len);
                        result.Add(new Vector3(p.X, p.Y, 0f));
                        at += step;
                    }
                    carry = len - (at - step);
                }
                if (result.Count >= maxPoints) break;
            }
            return result;
        }

        static int CountStrokes(IReadOnlyList<IReadOnlyList<Vector2>> strokes)
        {
            int n = 0;
            foreach (IReadOnlyList<Vector2> s in strokes)
            {
                if (s != null && s.Count > 0) n++;
            }
            return n;
        }

        /// <summary>
        /// Parses "x,y" or "x,y,z" rows (comma, semicolon, tab or space separated; headers and junk rows are
        /// skipped), then centres and scales the cloud so its largest half-extent is 1. More rows than
        /// <paramref name="maxPoints"/> are evenly subsampled.
        /// </summary>
        public static List<Vector3> FromCsv(string text, int maxPoints = DefaultMaxPoints)
        {
            var raw = new List<Vector3>();
            if (string.IsNullOrEmpty(text)) return raw;
            char[] separators = { ',', ';', '\t', ' ' };
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Trim().Split(separators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!TryNumber(parts[0], out float x) || !TryNumber(parts[1], out float y)) continue;
                float z = 0f;
                if (parts.Length >= 3 && !TryNumber(parts[2], out z)) z = 0f;
                var v = new Vector3(x, y, z);
                if (ShowMath.IsFinite(v)) raw.Add(v);
            }
            return Normalise(Subsample(raw, maxPoints));
        }

        /// <summary>Centres a point cloud on its bounding box and scales its largest half-extent to 1.</summary>
        public static List<Vector3> Normalise(List<Vector3> points)
        {
            if (points.Count == 0) return points;
            Vector3 min = points[0], max = points[0];
            foreach (Vector3 p in points)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 center = (min + max) * 0.5f;
            Vector3 half = (max - min) * 0.5f;
            float extent = Math.Max(half.X, Math.Max(half.Y, half.Z));
            float scale = extent > 1e-6f ? 1f / extent : 1f;
            var result = new List<Vector3>(points.Count);
            foreach (Vector3 p in points) result.Add((p - center) * scale);
            return result;
        }

        /// <summary>A friendly default for new Sketch cues: a smiling face.</summary>
        public static List<Vector3> Smiley()
        {
            var strokes = new List<IReadOnlyList<Vector2>>
            {
                Arc(Vector2.Zero, 0.95f, 0f, 360f, 96),
                Arc(new Vector2(-0.34f, 0.3f), 0.1f, 0f, 360f, 12),
                Arc(new Vector2(0.34f, 0.3f), 0.1f, 0f, 360f, 12),
                Arc(new Vector2(0f, 0.08f), 0.55f, 205f, 335f, 32),
            };
            return FromStrokes(strokes, 600);
        }

        static List<Vector2> Arc(Vector2 center, float radius, float fromDegrees, float toDegrees, int segments)
        {
            var pts = new List<Vector2>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float a = (fromDegrees + (toDegrees - fromDegrees) * i / segments) * ShowMath.Deg2Rad;
                pts.Add(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius);
            }
            return pts;
        }

        static List<Vector3> Subsample(List<Vector3> points, int maxPoints)
        {
            if (points.Count <= maxPoints || maxPoints <= 0) return points;
            var result = new List<Vector3>(maxPoints);
            double step = (double)points.Count / maxPoints;
            for (int i = 0; i < maxPoints; i++) result.Add(points[Math.Min(points.Count - 1, (int)(i * step))]);
            return result;
        }

        static bool TryNumber(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && ShowMath.IsFinite(value);
        }

        static bool IsFinite(Vector2 v) => ShowMath.IsFinite(v.X) && ShowMath.IsFinite(v.Y);
    }
}
