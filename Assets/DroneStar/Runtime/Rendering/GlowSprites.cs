using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// A mesh of camera-facing glow sprites for the DroneGlow shader: four vertices per light, all at the
    /// light's position, with the corner, size and brightness packed into UV0.
    /// </summary>
    public sealed class GlowSprites
    {
        static readonly Vector2[] Corners = { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) };

        readonly Mesh mesh;
        Vector3[] vertices = new Vector3[0];
        Color[] colors = new Color[0];
        Vector4[] uvs = new Vector4[0];
        int count = -1;

        public GlowSprites(string name)
        {
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
        }

        public Mesh Mesh => mesh;
        public int Count => Mathf.Max(count, 0);

        /// <summary>Resizes the sprite set; per-sprite size and brightness default to 1.</summary>
        public void Resize(int n)
        {
            if (n == count) return;
            count = n;
            vertices = new Vector3[n * 4];
            colors = new Color[n * 4];
            uvs = new Vector4[n * 4];
            var indices = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 4; k++) uvs[i * 4 + k] = new Vector4(Corners[k].x, Corners[k].y, 1f, 1f);
                int v = i * 4, t = i * 6;
                indices[t] = v;
                indices[t + 1] = v + 1;
                indices[t + 2] = v + 2;
                indices[t + 3] = v;
                indices[t + 4] = v + 2;
                indices[t + 5] = v + 3;
            }
            mesh.Clear();
            mesh.indexFormat = n * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            // Sprites move and reflections are projected elsewhere, so never let the mesh be culled.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 20000f);
        }

        public void SetSprite(int i, Vector3 position, Color color, float size = 1f, float brightness = 1f)
        {
            int v = i * 4;
            for (int k = 0; k < 4; k++)
            {
                vertices[v + k] = position;
                colors[v + k] = color;
                uvs[v + k] = new Vector4(Corners[k].x, Corners[k].y, size, brightness);
            }
        }

        public void SetPositionsAndColors(Vector3[] positions, Color[] spriteColors, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int v = i * 4;
                Vector3 p = positions[i];
                Color c = spriteColors[i];
                vertices[v] = p;
                vertices[v + 1] = p;
                vertices[v + 2] = p;
                vertices[v + 3] = p;
                colors[v] = c;
                colors[v + 1] = c;
                colors[v + 2] = c;
                colors[v + 3] = c;
            }
        }

        public void Upload(bool includeUvs)
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            mesh.SetVertices(vertices, 0, vertices.Length, flags);
            mesh.SetColors(colors, 0, colors.Length, flags);
            if (includeUvs) mesh.SetUVs(0, uvs, 0, uvs.Length, flags);
        }

        public static GlowSprites FromLights(string name, IList<Vector3> positions, IList<Color> lightColors, IList<float> sizes)
        {
            var sprites = new GlowSprites(name);
            sprites.Resize(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                sprites.SetSprite(i, positions[i], lightColors[i], sizes != null ? sizes[i] : 1f);
            }
            sprites.Upload(includeUvs: true);
            return sprites;
        }
    }
}
