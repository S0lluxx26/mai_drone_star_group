using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// Small procedural mesh builder: boxes (optionally turned), round towers, pyramids, cylinders, cones and
    /// quads. Facade UVs are in metres; UV1 = (seed, part) and UV2 = face size, which the city shader uses
    /// for window grids, corner shading and roof-line lights. Vertex colour carries per-part style data.
    /// </summary>
    public sealed class MeshKit
    {
        /// <summary>UV1.y part codes understood by CityWindows.shader.</summary>
        public const float PartWall = 0f;
        public const float PartRoof = 1f;
        public const float PartPlain = 2f;

        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uv0 = new List<Vector2>();
        public readonly List<Vector2> Uv1 = new List<Vector2>();
        public readonly List<Vector2> Uv2 = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int> Triangles = new List<int>();

        /// <summary>Colour written to every vertex added from now on (city: r = facade style, g = crown light).</summary>
        public Color CurrentColor = Color.white;

        public int VertexCount => Vertices.Count;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvC, Vector2 meta)
        {
            int i = Vertices.Count;
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            Vertices.Add(a);
            Vertices.Add(b);
            Vertices.Add(c);
            Vertices.Add(d);
            var face = new Vector2(Mathf.Abs(uvC.x - uvA.x), Mathf.Abs(uvC.y - uvA.y));
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(n);
                Uv1.Add(meta);
                Uv2.Add(face);
                Colors.Add(CurrentColor);
            }
            // a→b runs along V, a→d along U; (a, b, c) is clockwise seen from the normal side (Unity front face).
            Uv0.Add(new Vector2(uvA.x, uvA.y));
            Uv0.Add(new Vector2(uvA.x, uvC.y));
            Uv0.Add(new Vector2(uvC.x, uvC.y));
            Uv0.Add(new Vector2(uvC.x, uvA.y));
            Triangles.Add(i);
            Triangles.Add(i + 1);
            Triangles.Add(i + 2);
            Triangles.Add(i);
            Triangles.Add(i + 2);
            Triangles.Add(i + 3);
        }

        /// <summary>Box on its centre; side UVs are in metres (for window grids), roofs are flagged in UV1.y.</summary>
        public void Box(Vector3 center, Vector3 size, float seed = 0f, bool bottom = false, float yawDegrees = 0f, float wallPart = PartWall)
        {
            int first = Vertices.Count;
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x, h.y, -h.z);
            Vector3 p110 = center + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z);
            Vector3 p101 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = center + new Vector3(-h.x, h.y, h.z);
            Vector3 p111 = center + new Vector3(h.x, h.y, h.z);
            var wall = new Vector2(seed, wallPart);
            var roof = new Vector2(seed, PartRoof);
            // Front (-z), right (+x), back (+z), left (-x): a, b, c, d counter-clockwise seen from outside.
            Quad(p000, p010, p110, p100, Vector2.zero, new Vector2(size.x, size.y), wall);
            Quad(p100, p110, p111, p101, Vector2.zero, new Vector2(size.z, size.y), wall);
            Quad(p101, p111, p011, p001, Vector2.zero, new Vector2(size.x, size.y), wall);
            Quad(p001, p011, p010, p000, Vector2.zero, new Vector2(size.z, size.y), wall);
            Quad(p010, p011, p111, p110, Vector2.zero, new Vector2(size.x, size.z), roof);
            if (bottom) Quad(p000, p100, p101, p001, Vector2.zero, new Vector2(size.x, size.z), roof);
            if (Mathf.Abs(yawDegrees) > 1e-3f) RotateSince(first, center, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        /// <summary>A round tower with metre-space facade UVs (u = arc length) and a flat roof.</summary>
        public void Tower(Vector3 baseCenter, float radius, float height, int sides, float seed)
        {
            float arc = 2f * Mathf.PI * radius / sides;
            Vector3 up = Vector3.up * height;
            var wall = new Vector2(seed, PartWall);
            for (int s = 0; s < sides; s++)
            {
                float a0 = -Mathf.PI * 2f * s / sides, a1 = -Mathf.PI * 2f * (s + 1) / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                // Winding: a (bottom, a0) → b (top, a0) → c (top, a1) → d (bottom, a1) must be clockwise from outside.
                // u runs from the a1 edge ((s+1)·arc) to the a0 edge (s·arc), so it is continuous across panels.
                Quad(baseCenter + d1, baseCenter + d1 + up, baseCenter + d0 + up, baseCenter + d0,
                    new Vector2((s + 1) * arc, 0f), new Vector2(s * arc, height), wall);
                TriPart(baseCenter + up, baseCenter + d0 + up, baseCenter + d1 + up, new Vector2(seed, PartRoof));
            }
        }

        /// <summary>A square pyramid roof (no windows), turned by <paramref name="yawDegrees"/>.</summary>
        public void Pyramid(Vector3 baseCenter, float halfSize, float height, float yawDegrees, float seed)
        {
            int first = Vertices.Count;
            Vector3 tip = baseCenter + Vector3.up * height;
            Vector3[] c =
            {
                baseCenter + new Vector3(-halfSize, 0f, -halfSize), baseCenter + new Vector3(halfSize, 0f, -halfSize),
                baseCenter + new Vector3(halfSize, 0f, halfSize), baseCenter + new Vector3(-halfSize, 0f, halfSize),
            };
            var meta = new Vector2(seed, PartPlain);
            for (int i = 0; i < 4; i++) TriPart(c[i], tip, c[(i + 1) % 4], meta);
            RotateSince(first, baseCenter, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        public void Cylinder(Vector3 baseCenter, float radius, float height, int sides, bool caps = true)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = Mathf.PI * 2f * s / sides, a1 = Mathf.PI * 2f * (s + 1) / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                Vector3 up = Vector3.up * height;
                Quad(baseCenter + d0, baseCenter + d0 + up, baseCenter + d1 + up, baseCenter + d1, Vector2.zero, Vector2.one, Vector2.zero);
                if (caps)
                {
                    Tri(baseCenter + up, baseCenter + d1 + up, baseCenter + d0 + up);
                    Tri(baseCenter, baseCenter + d0, baseCenter + d1);
                }
            }
        }

        public void Cone(Vector3 baseCenter, float radius, float height, int sides)
        {
            Vector3 tip = baseCenter + Vector3.up * height;
            for (int s = 0; s < sides; s++)
            {
                float a0 = Mathf.PI * 2f * s / sides, a1 = Mathf.PI * 2f * (s + 1) / sides;
                Vector3 p0 = baseCenter + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCenter + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                Tri(p0, tip, p1);
            }
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c) => TriPart(a, b, c, Vector2.zero);

        void TriPart(Vector3 a, Vector3 b, Vector3 c, Vector2 meta)
        {
            int i = Vertices.Count;
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            Vertices.Add(a);
            Vertices.Add(b);
            Vertices.Add(c);
            for (int k = 0; k < 3; k++)
            {
                Normals.Add(n);
                Uv0.Add(Vector2.zero);
                Uv1.Add(meta);
                Uv2.Add(Vector2.one);
                Colors.Add(CurrentColor);
            }
            Triangles.Add(i);
            Triangles.Add(i + 1);
            Triangles.Add(i + 2);
        }

        void RotateSince(int first, Vector3 pivot, Quaternion q)
        {
            for (int v = first; v < Vertices.Count; v++)
            {
                Vertices[v] = pivot + q * (Vertices[v] - pivot);
                Normals[v] = q * Normals[v];
            }
        }

        /// <summary>Appends another kit's geometry (all channels stay in step).</summary>
        public void Append(MeshKit other)
        {
            int offset = Vertices.Count;
            Vertices.AddRange(other.Vertices);
            Normals.AddRange(other.Normals);
            Uv0.AddRange(other.Uv0);
            Uv1.AddRange(other.Uv1);
            Uv2.AddRange(other.Uv2);
            Colors.AddRange(other.Colors);
            foreach (int t in other.Triangles) Triangles.Add(t + offset);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetUVs(2, Uv2);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
