using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>Small procedural mesh builder: boxes, cylinders, cones and quads with optional UVs.</summary>
    public sealed class MeshKit
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uv0 = new List<Vector2>();
        public readonly List<Vector2> Uv1 = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int> Triangles = new List<int>();

        public int VertexCount => Vertices.Count;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvC, Vector2 meta)
        {
            int i = Vertices.Count;
            Vector3 n = Vector3.Cross(b - a, d - a).normalized;
            Vertices.Add(a);
            Vertices.Add(b);
            Vertices.Add(c);
            Vertices.Add(d);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(n);
                Uv1.Add(meta);
                Colors.Add(Color.white);
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

        /// <summary>Axis-aligned box; side UVs are in metres (for window grids), roofs are flagged in UV1.y.</summary>
        public void Box(Vector3 center, Vector3 size, float seed = 0f, bool bottom = false)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x, h.y, -h.z);
            Vector3 p110 = center + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z);
            Vector3 p101 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = center + new Vector3(-h.x, h.y, h.z);
            Vector3 p111 = center + new Vector3(h.x, h.y, h.z);
            var wall = new Vector2(seed, 0f);
            var roof = new Vector2(seed, 1f);
            // Front (-z), right (+x), back (+z), left (-x): a, b, c, d counter-clockwise seen from outside.
            Quad(p000, p010, p110, p100, Vector2.zero, new Vector2(size.x, size.y), wall);
            Quad(p100, p110, p111, p101, Vector2.zero, new Vector2(size.z, size.y), wall);
            Quad(p101, p111, p011, p001, Vector2.zero, new Vector2(size.x, size.y), wall);
            Quad(p001, p011, p010, p000, Vector2.zero, new Vector2(size.z, size.y), wall);
            Quad(p010, p011, p111, p110, Vector2.zero, new Vector2(size.x, size.z), roof);
            if (bottom) Quad(p000, p100, p101, p001, Vector2.zero, new Vector2(size.x, size.z), roof);
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

        public void Tri(Vector3 a, Vector3 b, Vector3 c)
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
                Uv1.Add(Vector2.zero);
                Colors.Add(Color.white);
            }
            Triangles.Add(i);
            Triangles.Add(i + 1);
            Triangles.Add(i + 2);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
