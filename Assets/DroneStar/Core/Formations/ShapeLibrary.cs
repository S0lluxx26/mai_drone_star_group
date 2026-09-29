using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace DroneStar.Core
{
    /// <summary>A pre-built 3D model: coloured points in farthest-point order, normalised to a unit half-size.</summary>
    public sealed class ModelShape
    {
        public string Name = "";

        /// <summary>Points centred on the bounding box, scaled so the largest half-extent is 1.</summary>
        public Vector3[] Points = new Vector3[0];

        /// <summary>Baked LED colour per point (sRGB), including the model's audience shading.</summary>
        public LedColor[] Colors = new LedColor[0];

        /// <summary>Bounding-box extents divided by the largest one (1 on the model's longest axis).</summary>
        public Vector3 Proportions = Vector3.One;

        /// <summary>
        /// Per point: 0 = rigid, 1 = left wing, 2 = right wing; null for models without wings. Wings swing about
        /// <see cref="Hinge"/> (the right shoulder, in the same normalised units as <see cref="Points"/>; the left
        /// shoulder mirrors it) under the Flap motion.
        /// </summary>
        public byte[] Groups;

        public Vector3 Hinge;

        public bool HasWings => Groups != null;
    }

    /// <summary>
    /// Registry of model formations loaded from a shape pack (see tools/export-shape-pack.mjs). Points are
    /// stored in farthest-point order, so the first n of them cover the model evenly for any drone count.
    /// </summary>
    public static class ShapeLibrary
    {
        /// <summary>Most points a model may carry. Models hold more points than the largest fleet, because the
        /// spacing filter always drops some; this cap only guards against corrupt packs.</summary>
        public const int MaxPointsPerModel = 65535;

        static readonly List<ModelShape> shapes = new List<ModelShape>();
        static readonly object gate = new object();
        static ulong checksum;

        /// <summary>FNV-1a hash of the loaded pack (0 when none): part of every portable assignment-cache key.</summary>
        public static ulong Checksum
        {
            get
            {
                lock (gate) return checksum;
            }
        }

        public static IReadOnlyList<ModelShape> Shapes
        {
            get
            {
                lock (gate) return shapes.ToArray();
            }
        }

        public static bool IsLoaded
        {
            get
            {
                lock (gate) return shapes.Count > 0;
            }
        }

        public static bool TryGet(string name, out ModelShape shape)
        {
            lock (gate)
            {
                foreach (ModelShape s in shapes)
                {
                    if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        shape = s;
                        return true;
                    }
                }
            }
            shape = null;
            return false;
        }

        /// <summary>Parses a shape pack and replaces the registry. Throws <see cref="FormatException"/> on bad data.</summary>
        public static int LoadPack(byte[] data)
        {
            List<ModelShape> parsed = ParsePack(data);
            ulong h = 14695981039346656037UL;
            foreach (byte b in data)
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            lock (gate)
            {
                shapes.Clear();
                shapes.AddRange(parsed);
                checksum = h;
            }
            return parsed.Count;
        }

        public static List<ModelShape> ParsePack(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var reader = new PackReader(data);
            if (reader.Ascii(4) != "DSSP") throw new FormatException("Not a Drone Star shape pack.");
            int version = reader.U16();
            if (version != 1 && version != 2) throw new FormatException("Unsupported shape pack version " + version + ".");
            int count = reader.U16();
            var result = new List<ModelShape>(count);
            for (int s = 0; s < count; s++)
            {
                string name = reader.Utf8(reader.U8());
                int n = reader.U16();
                if (n <= 0 || n > MaxPointsPerModel) throw new FormatException("Bad point count in " + name + ".");
                var min = new Vector3(reader.F32(), reader.F32(), reader.F32());
                var max = new Vector3(reader.F32(), reader.F32(), reader.F32());
                if (!ShowMath.IsFinite(min) || !ShowMath.IsFinite(max)) throw new FormatException("Bad bounds in " + name + ".");
                // Version 2: a flags byte; bit 0 = the model has wings (a hinge here, a group byte per point below).
                bool wings = version >= 2 && (reader.U8() & 1) != 0;
                Vector3 hinge = wings ? new Vector3(reader.F32(), reader.F32(), reader.F32()) : Vector3.Zero;
                if (!ShowMath.IsFinite(hinge)) throw new FormatException("Bad hinge in " + name + ".");
                Vector3 span = max - min;
                var raw = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    float x = min.X + (reader.I16() + 32768) / 65535f * span.X;
                    float y = min.Y + (reader.I16() + 32768) / 65535f * span.Y;
                    float z = min.Z + (reader.I16() + 32768) / 65535f * span.Z;
                    raw[i] = new Vector3(x, y, z);
                }
                var colors = new LedColor[n];
                for (int i = 0; i < n; i++) colors[i] = new LedColor(reader.U8() / 255f, reader.U8() / 255f, reader.U8() / 255f);
                byte[] groups = null;
                if (wings)
                {
                    groups = new byte[n];
                    for (int i = 0; i < n; i++)
                    {
                        groups[i] = (byte)reader.U8();
                        if (groups[i] > 2) throw new FormatException("Bad wing group in " + name + ".");
                    }
                }

                Vector3 center = (min + max) * 0.5f;
                float largest = Math.Max(span.X, Math.Max(span.Y, span.Z));
                float scale = largest > 1e-6f ? 2f / largest : 1f;
                var points = new Vector3[n];
                for (int i = 0; i < n; i++) points[i] = (raw[i] - center) * scale;
                result.Add(new ModelShape
                {
                    Name = name,
                    Points = points,
                    Colors = colors,
                    Proportions = largest > 1e-6f ? span / largest : Vector3.One,
                    Groups = groups,
                    Hinge = (hinge - center) * scale,
                });
            }
            if (!reader.AtEnd) throw new FormatException("Trailing bytes in shape pack.");
            return result;
        }

        sealed class PackReader
        {
            readonly byte[] data;
            int pos;

            public PackReader(byte[] data)
            {
                this.data = data;
            }

            public bool AtEnd => pos == data.Length;

            void Need(int count)
            {
                if (pos + count > data.Length) throw new FormatException("Shape pack is truncated.");
            }

            public int U8()
            {
                Need(1);
                return data[pos++];
            }

            public int U16()
            {
                Need(2);
                int v = data[pos] | (data[pos + 1] << 8);
                pos += 2;
                return v;
            }

            public int I16() => (short)U16();

            public float F32()
            {
                Need(4);
                float v = BitConverter.IsLittleEndian
                    ? BitConverter.ToSingle(data, pos)
                    : BitConverter.ToSingle(new[] { data[pos + 3], data[pos + 2], data[pos + 1], data[pos] }, 0);
                pos += 4;
                return v;
            }

            public string Ascii(int count)
            {
                Need(count);
                string s = Encoding.ASCII.GetString(data, pos, count);
                pos += count;
                return s;
            }

            public string Utf8(int count)
            {
                Need(count);
                string s = Encoding.UTF8.GetString(data, pos, count);
                pos += count;
                return s;
            }
        }
    }
}
