using System;
using System.Numerics;

namespace DroneStar.Core
{
    /// <summary>
    /// Allocation-free uniform hash grid for neighbour queries. Rebuild it with <see cref="Build"/>, then
    /// walk the 27 cells around a point with <see cref="First"/> and <see cref="Next"/>.
    /// </summary>
    public sealed class SpatialGrid
    {
        const int Bits = 21;
        const long Mask = (1L << Bits) - 1;
        const int Bias = 1 << (Bits - 1);

        long[] keys = new long[0];
        int[] heads = new int[0];
        int[] next = new int[0];
        int tableMask;
        float inverseCell;

        public float CellSize { get; private set; }

        public void Build(Vector3[] points, int count, float cellSize)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (count < 0 || count > points.Length) throw new ArgumentOutOfRangeException(nameof(count));
            CellSize = Math.Max(cellSize, 1e-3f);
            inverseCell = 1f / CellSize;

            int capacity = 16;
            while (capacity < count * 2) capacity <<= 1;
            if (keys.Length != capacity)
            {
                keys = new long[capacity];
                heads = new int[capacity];
            }
            tableMask = capacity - 1;
            for (int i = 0; i < capacity; i++) heads[i] = -1;
            if (next.Length < count) next = new int[Math.Max(count, 16)];

            for (int i = 0; i < count; i++)
            {
                long key = KeyOf(points[i]);
                int slot = FindSlot(key, insert: true);
                next[i] = heads[slot];
                heads[slot] = i;
            }
        }

        public void CellOf(Vector3 p, out int cx, out int cy, out int cz)
        {
            cx = (int)MathF.Floor(p.X * inverseCell);
            cy = (int)MathF.Floor(p.Y * inverseCell);
            cz = (int)MathF.Floor(p.Z * inverseCell);
        }

        /// <summary>First point index in the cell, or -1 when the cell is empty.</summary>
        public int First(int cx, int cy, int cz)
        {
            int slot = FindSlot(Pack(cx, cy, cz), insert: false);
            return slot < 0 ? -1 : heads[slot];
        }

        public int Next(int index) => next[index];

        long KeyOf(Vector3 p)
        {
            CellOf(p, out int cx, out int cy, out int cz);
            return Pack(cx, cy, cz);
        }

        static long Pack(int cx, int cy, int cz)
        {
            return (((long)(cx + Bias) & Mask) << (2 * Bits)) | (((long)(cy + Bias) & Mask) << Bits) | ((long)(cz + Bias) & Mask);
        }

        int FindSlot(long key, bool insert)
        {
            int slot = (int)((ulong)(key * unchecked((long)0x9E3779B97F4A7C15UL)) >> 40) & tableMask;
            while (true)
            {
                if (heads[slot] == -1)
                {
                    if (!insert) return -1;
                    keys[slot] = key;
                    return slot;
                }
                if (keys[slot] == key) return slot;
                slot = (slot + 1) & tableMask;
            }
        }
    }
}
