using System;
using System.Globalization;

namespace DroneStar.Core
{
    /// <summary>An RGB LED colour in display (sRGB) space, each channel in [0, 1].</summary>
    public readonly struct LedColor : IEquatable<LedColor>
    {
        public readonly float R;
        public readonly float G;
        public readonly float B;

        public LedColor(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static LedColor Black => new LedColor(0f, 0f, 0f);
        public static LedColor White => new LedColor(1f, 1f, 1f);

        /// <summary>Warm, dimmed white used while drones idle on the pads and during take-off and landing.</summary>
        public static LedColor Standby => new LedColor(1f, 0.72f, 0.42f);

        public float MaxComponent => Math.Max(R, Math.Max(G, B));

        public static LedColor Lerp(LedColor a, LedColor b, float t)
        {
            return new LedColor(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        }

        public static LedColor operator *(LedColor c, float k) => new LedColor(c.R * k, c.G * k, c.B * k);

        public LedColor Clamped()
        {
            return new LedColor(ShowMath.Clamp01(R), ShowMath.Clamp01(G), ShowMath.Clamp01(B));
        }

        /// <summary>HSV to RGB with hue in turns (0..1, wraps), saturation and value in [0, 1].</summary>
        public static LedColor FromHsv(float hue, float saturation, float value)
        {
            float h = ShowMath.Frac(hue) * 6f;
            float s = ShowMath.Clamp01(saturation);
            float v = ShowMath.Clamp01(value);
            int sector = Math.Min((int)h, 5);
            float f = h - sector;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            switch (sector)
            {
                case 0: return new LedColor(v, t, p);
                case 1: return new LedColor(q, v, p);
                case 2: return new LedColor(p, v, t);
                case 3: return new LedColor(p, q, v);
                case 4: return new LedColor(t, p, v);
                default: return new LedColor(v, p, q);
            }
        }

        public string ToHex()
        {
            LedColor c = Clamped();
            return "#" + ToByte(c.R).ToString("X2", CultureInfo.InvariantCulture)
                       + ToByte(c.G).ToString("X2", CultureInfo.InvariantCulture)
                       + ToByte(c.B).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>Parses "#RRGGBB" or "RRGGBB". Returns false (and black) on malformed input.</summary>
        public static bool TryParseHex(string text, out LedColor color)
        {
            color = Black;
            if (string.IsNullOrEmpty(text)) return false;
            string s = text.Trim();
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length != 6) return false;
            if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
            color = new LedColor(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
            return true;
        }

        public static LedColor FromHex(string text)
        {
            if (!TryParseHex(text, out LedColor c)) throw new FormatException("Not a #RRGGBB colour: " + text);
            return c;
        }

        static int ToByte(float x) => (int)Math.Round(ShowMath.Clamp01(x) * 255f);

        public bool Equals(LedColor other) => R == other.R && G == other.G && B == other.B;

        public override bool Equals(object obj) => obj is LedColor other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(R, G, B);

        public override string ToString() => ToHex();
    }
}
