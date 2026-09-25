using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace DroneStar.Core
{
    /// <summary>
    /// A single-stroke vector font for drone lettering. Glyphs live on a 4 × 6 unit cell (x right, y up);
    /// each stroke is a polyline. Accented letters are folded to their base letter (e.g. "Mai Hà" → "MAI HA").
    /// </summary>
    public static class StrokeFont
    {
        public const float GlyphWidth = 4f;
        public const float GlyphHeight = 6f;
        public const float LetterGap = 1.5f;
        public const float SpaceAdvance = 3f;

        static readonly Dictionary<char, Vector2[][]> Glyphs = BuildGlyphs();

        public static bool HasGlyph(char c) => Glyphs.ContainsKey(c);

        /// <summary>Uppercases, removes diacritics and drops characters the font cannot draw.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string decomposed;
            try
            {
                decomposed = text.Normalize(NormalizationForm.FormD);
            }
            catch (Exception)
            {
                // Some AOT/web runtimes ship without normalisation tables; fall back to the raw text.
                decomposed = text;
            }
            var sb = new StringBuilder(decomposed.Length);
            foreach (char raw in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
                char c = char.ToUpperInvariant(raw);
                if (c == 'Đ') c = 'D';
                if (c == ' ' || Glyphs.ContainsKey(c)) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Lays the text out centred on the origin, <paramref name="width"/> metres wide.
        /// Returns open polylines and the metres-per-unit scale that was used.
        /// </summary>
        public static List<Vector2[]> Layout(string text, float width, out float unitScale)
        {
            var strokes = new List<Vector2[]>();
            unitScale = 0f;
            string s = Normalize(text);
            if (s.Length == 0) return strokes;

            float advance = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                advance += s[i] == ' ' ? SpaceAdvance : GlyphWidth;
                if (i < s.Length - 1) advance += LetterGap;
            }
            if (advance <= 0f) return strokes;

            unitScale = width / advance;
            float x = 0f;
            foreach (char c in s)
            {
                if (c == ' ')
                {
                    x += SpaceAdvance + LetterGap;
                    continue;
                }
                foreach (Vector2[] stroke in Glyphs[c])
                {
                    var placed = new Vector2[stroke.Length];
                    for (int k = 0; k < stroke.Length; k++)
                    {
                        placed[k] = new Vector2(x + stroke[k].X, stroke[k].Y - GlyphHeight * 0.5f);
                    }
                    strokes.Add(placed);
                }
                x += GlyphWidth + LetterGap;
            }

            // Centre on the ink rather than the advance box, so "MAI" (whose I sits inside its cell) is balanced.
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            foreach (Vector2[] stroke in strokes)
            {
                foreach (Vector2 p in stroke)
                {
                    minX = Math.Min(minX, p.X);
                    maxX = Math.Max(maxX, p.X);
                }
            }
            float shift = (minX + maxX) * 0.5f;
            foreach (Vector2[] stroke in strokes)
            {
                for (int k = 0; k < stroke.Length; k++)
                {
                    stroke[k] = new Vector2((stroke[k].X - shift) * unitScale, stroke[k].Y * unitScale);
                }
            }
            return strokes;
        }

        static Dictionary<char, Vector2[][]> BuildGlyphs()
        {
            var d = new Dictionary<char, string>
            {
                ['A'] = "0,0 2,6 4,0|0.8,2.4 3.2,2.4",
                ['B'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|3,3 4,2 4,1 3,0 0,0",
                ['C'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1",
                ['D'] = "0,0 0,6 2.5,6 4,4.5 4,1.5 2.5,0 0,0",
                ['E'] = "4,6 0,6 0,0 4,0|0,3 3,3",
                ['F'] = "4,6 0,6 0,0|0,3 3,3",
                ['G'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,3 2.2,3",
                ['H'] = "0,0 0,6|4,0 4,6|0,3 4,3",
                ['I'] = "1,6 3,6|2,6 2,0|1,0 3,0",
                ['J'] = "4,6 4,1 3,0 1,0 0,1",
                ['K'] = "0,0 0,6|4,6 0,2.4|1.4,3.4 4,0",
                ['L'] = "0,6 0,0 4,0",
                ['M'] = "0,0 0,6 2,3 4,6 4,0",
                ['N'] = "0,0 0,6 4,0 4,6",
                ['O'] = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0",
                ['P'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3",
                ['Q'] = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|2.6,1.4 4,0",
                ['R'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|2,3 4,0",
                ['S'] = "4,5 3,6 1,6 0,5 0,4 1,3 3,3 4,2 4,1 3,0 1,0 0,1",
                ['T'] = "0,6 4,6|2,6 2,0",
                ['U'] = "0,6 0,1 1,0 3,0 4,1 4,6",
                ['V'] = "0,6 2,0 4,6",
                ['W'] = "0,6 1,0 2,3 3,0 4,6",
                ['X'] = "0,0 4,6|0,6 4,0",
                ['Y'] = "0,6 2,3 4,6|2,3 2,0",
                ['Z'] = "0,6 4,6 0,0 4,0",
                ['0'] = "1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,1 1,0|0.8,1.2 3.2,4.8",
                ['1'] = "0.8,4.8 2,6 2,0|0.8,0 3.2,0",
                ['2'] = "0,5 1,6 3,6 4,5 4,4 0,0 4,0",
                ['3'] = "0,5 1,6 3,6 4,5 4,4 3,3 4,2 4,1 3,0 1,0 0,1|1.5,3 3,3",
                ['4'] = "3,0 3,6 0,2 4,2",
                ['5'] = "4,6 0,6 0,3.4 3,3.4 4,2.4 4,1 3,0 1,0 0,1",
                ['6'] = "4,5 3,6 1,6 0,5 0,1 1,0 3,0 4,1 4,2.4 3,3.4 1,3.4 0,2.4",
                ['7'] = "0,6 4,6 1.5,0",
                ['8'] = "1,3 0,4 0,5 1,6 3,6 4,5 4,4 3,3 1,3 0,2 0,1 1,0 3,0 4,1 4,2 3,3",
                ['9'] = "0,1 1,0 3,0 4,1 4,5 3,6 1,6 0,5 0,3.6 1,2.6 3,2.6 4,3.6",
                ['!'] = "2,6 2,1.8|2,0.4 2,0",
                ['?'] = "0,5 1,6 3,6 4,5 4,4 2,2.6 2,1.6|2,0.4 2,0",
                ['.'] = "1.8,0 2.2,0",
                ['-'] = "0.8,3 3.2,3",
                ['+'] = "2,1 2,5|0,3 4,3",
                ['*'] = "2,1 2,5|0.3,2 3.7,4|0.3,4 3.7,2",
                ['<'] = "4,5 0,3 4,1",
                ['>'] = "0,5 4,3 0,1",
            };

            var glyphs = new Dictionary<char, Vector2[][]>();
            foreach (KeyValuePair<char, string> kv in d)
            {
                string[] strokes = kv.Value.Split('|');
                var parsed = new Vector2[strokes.Length][];
                for (int s = 0; s < strokes.Length; s++)
                {
                    string[] pts = strokes[s].Split(' ');
                    parsed[s] = new Vector2[pts.Length];
                    for (int p = 0; p < pts.Length; p++)
                    {
                        string[] xy = pts[p].Split(',');
                        parsed[s][p] = new Vector2(
                            float.Parse(xy[0], CultureInfo.InvariantCulture),
                            float.Parse(xy[1], CultureInfo.InvariantCulture));
                    }
                }
                glyphs[kv.Key] = parsed;
            }
            return glyphs;
        }
    }
}
