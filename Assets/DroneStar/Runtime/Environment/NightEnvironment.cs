using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DroneStar.App
{
    /// <summary>
    /// Builds the night venue procedurally: sky, lake, audience shore with trees and lanterns, the launch
    /// barge with pad lights, a far-shore city skyline and a ring of hills. Everything is generated from a
    /// fixed seed, so the scene is identical on every run and costs nothing to download.
    /// </summary>
    public sealed class NightEnvironment : MonoBehaviour
    {
        public const float WaterLevel = -1.2f;
        public const float ShoreZ = -262f;

        [SerializeField] Material skyMaterial;
        [SerializeField] Material waterMaterial;
        [SerializeField] Material landMaterial;
        [SerializeField] Material hillMaterial;
        [SerializeField] Material deckMaterial;
        [SerializeField] Material cityMaterial;
        [SerializeField] Material treeMaterial;
        [SerializeField] Material lampMaterial;
        [SerializeField] Material padMaterial;
        [SerializeField] int seed = 20260925;

        GameObject bargeObject;
        GameObject padLightsObject;
        Vector3 lastPadCenter = new Vector3(float.NaN, 0f, 0f);
        int lastPadCount = -1;
        float lastPadExtent;

        public void Configure(Material sky, Material water, Material land, Material hill, Material deck, Material city, Material tree, Material lamp, Material pad)
        {
            skyMaterial = sky;
            waterMaterial = water;
            landMaterial = land;
            hillMaterial = hill;
            deckMaterial = deck;
            cityMaterial = city;
            treeMaterial = tree;
            lampMaterial = lamp;
            padMaterial = pad;
        }

        void Awake()
        {
            ApplyAtmosphere();
            var rng = new System.Random(seed);
            Spawn("Lake", BuildWater(), waterMaterial);
            Spawn("Audience Shore", BuildShore(), landMaterial);
            Spawn("Far Shore", BuildFarShore(), landMaterial);
            Spawn("Hills", BuildHills(rng), hillMaterial);
            Spawn("City Skyline", BuildCity(rng, out List<Vector3> beacons), cityMaterial);
            Spawn("Trees", BuildTrees(rng), treeMaterial);
            SpawnLights("Shore Lanterns", ShoreLanterns(rng), lampMaterial);
            SpawnLights("Tower Beacons", beacons, lampMaterial, new Color(1f, 0.08f, 0.04f), 5f);
        }

        void ApplyAtmosphere()
        {
            if (skyMaterial != null) RenderSettings.skybox = skyMaterial;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.05f, 0.04f, 0.1f);
            RenderSettings.fogDensity = 0.00055f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.1f, 0.11f, 0.2f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.08f, 0.13f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.025f, 0.035f);
        }

        GameObject Spawn(string objectName, Mesh mesh, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        GameObject SpawnLights(string objectName, List<Vector3> positions, Material material, Color? tint = null, float size = 1f)
        {
            var colors = new List<Color>(positions.Count);
            var sizes = new List<float>(positions.Count);
            var rng = new System.Random(positions.Count * 31 + objectName.Length);
            for (int i = 0; i < positions.Count; i++)
            {
                Color warm = Color.Lerp(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.78f, 0.45f), (float)rng.NextDouble());
                colors.Add((tint ?? warm).linear);
                sizes.Add(size * (0.8f + 0.4f * (float)rng.NextDouble()));
            }
            GlowSprites sprites = GlowSprites.FromLights(objectName, positions, colors, sizes);
            return Spawn(objectName, sprites.Mesh, material);
        }

        /// <summary>Rebuilds the barge and its pad lights when the pad grid changes.</summary>
        public void SetPads(Vector3[] pads)
        {
            if (pads == null || pads.Length == 0) return;
            Vector3 min = pads[0], max = pads[0];
            foreach (Vector3 p in pads)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 center = (min + max) * 0.5f;
            float extent = Mathf.Max(max.x - min.x, max.z - min.z);
            if (pads.Length == lastPadCount && center == lastPadCenter && Mathf.Approximately(extent, lastPadExtent)) return;
            lastPadCount = pads.Length;
            lastPadCenter = center;
            lastPadExtent = extent;

            if (bargeObject != null) DestroyWithMesh(bargeObject);
            if (padLightsObject != null) DestroyWithMesh(padLightsObject);

            var kit = new MeshKit();
            var size = new Vector3(max.x - min.x + 10f, 2.6f, max.z - min.z + 10f);
            kit.Box(new Vector3(center.x, -1.3f, center.z), size);
            // A low safety rail frame around the deck edge.
            float railY = 0.35f;
            kit.Box(new Vector3(center.x, railY, center.z - size.z * 0.5f), new Vector3(size.x, 0.12f, 0.12f));
            kit.Box(new Vector3(center.x, railY, center.z + size.z * 0.5f), new Vector3(size.x, 0.12f, 0.12f));
            kit.Box(new Vector3(center.x - size.x * 0.5f, railY, center.z), new Vector3(0.12f, 0.12f, size.z));
            kit.Box(new Vector3(center.x + size.x * 0.5f, railY, center.z), new Vector3(0.12f, 0.12f, size.z));
            bargeObject = Spawn("Launch Barge", kit.ToMesh("Launch Barge"), deckMaterial);

            var lights = new List<Vector3>(pads.Length);
            foreach (Vector3 p in pads) lights.Add(p + Vector3.up * 0.04f);
            padLightsObject = SpawnLights("Pad Lights", lights, padMaterial, new Color(0.1f, 0.75f, 1f), 0.55f);
        }

        void DestroyWithMesh(GameObject go)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Destroy(go);
        }

        static Mesh BuildWater()
        {
            // A coarse grid (not a single quad) keeps per-vertex fog accurate across the huge lake.
            const int cells = 24;
            const float minX = -3200f, maxX = 3200f, minZ = -600f, maxZ = 3200f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int j = 0; j <= cells; j++)
            {
                for (int i = 0; i <= cells; i++)
                {
                    vertices.Add(new Vector3(Mathf.Lerp(minX, maxX, (float)i / cells), WaterLevel, Mathf.Lerp(minZ, maxZ, (float)j / cells)));
                }
            }
            for (int j = 0; j < cells; j++)
            {
                for (int i = 0; i < cells; i++)
                {
                    int a = j * (cells + 1) + i, b = a + cells + 1;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                    triangles.Add(a);
                    triangles.Add(b + 1);
                    triangles.Add(a + 1);
                }
            }
            var mesh = new Mesh { name = "Lake" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildShore()
        {
            // Terrain strip from the water's edge back into the park, with a gentle beach slope.
            const int columns = 64, rows = 10;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int r = 0; r <= rows; r++)
            {
                float t = (float)r / rows;
                float z = Mathf.Lerp(ShoreZ + 6f, -1800f, t * t);
                for (int c = 0; c <= columns; c++)
                {
                    float x = Mathf.Lerp(-3200f, 3200f, (float)c / columns);
                    float y = r == 0 ? WaterLevel - 0.6f : 0.35f + Mathf.Max(0f, (-z - 320f) * 0.02f) + 0.8f * Mathf.Sin(x * 0.004f + z * 0.003f);
                    vertices.Add(new Vector3(x, y, z));
                }
            }
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int a = r * (columns + 1) + c, b = a + columns + 1;
                    triangles.Add(a);
                    triangles.Add(a + 1);
                    triangles.Add(b + 1);
                    triangles.Add(a);
                    triangles.Add(b + 1);
                    triangles.Add(b);
                }
            }
            var mesh = new Mesh { name = "Audience Shore" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildFarShore()
        {
            // A flat embankment the skyline stands on, curving around the far side of the lake.
            var kit = new MeshKit();
            const int segments = 48;
            for (int s = 0; s < segments; s++)
            {
                float a0 = Mathf.Lerp(-75f, 75f, (float)s / segments) * Mathf.Deg2Rad;
                float a1 = Mathf.Lerp(-75f, 75f, (float)(s + 1) / segments) * Mathf.Deg2Rad;
                Vector3 Ring(float angle, float radius, float y) => new Vector3(Mathf.Sin(angle) * radius, y, 260f + Mathf.Cos(angle) * radius);
                kit.Quad(Ring(a0, 640f, WaterLevel - 0.5f), Ring(a0, 700f, 1f), Ring(a1, 700f, 1f), Ring(a1, 640f, WaterLevel - 0.5f), Vector2.zero, Vector2.one, Vector2.zero);
                kit.Quad(Ring(a0, 700f, 1f), Ring(a0, 1260f, 2f), Ring(a1, 1260f, 2f), Ring(a1, 700f, 1f), Vector2.zero, Vector2.one, Vector2.zero);
            }
            return kit.ToMesh("Far Shore");
        }

        static Mesh BuildHills(System.Random rng)
        {
            const int segments = 220;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float p1 = (float)rng.NextDouble() * 10f, p2 = (float)rng.NextDouble() * 10f, p3 = (float)rng.NextDouble() * 10f;
            for (int s = 0; s <= segments; s++)
            {
                float a = Mathf.PI * 2f * s / segments;
                float height = 70f + 60f * Mathf.Sin(a * 3f + p1) + 38f * Mathf.Sin(a * 7f + p2) + 18f * Mathf.Sin(a * 17f + p3);
                // Lower behind the city so the skyline stays readable, taller on the flanks.
                float behindCity = Mathf.Clamp01(Mathf.Cos(a)) * 0.55f;
                height = Mathf.Max(25f, height * (1f - behindCity) + 30f);
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 center = new Vector3(0f, 0f, 200f);
                vertices.Add(center + dir * 1300f + Vector3.up * (WaterLevel - 2f));
                vertices.Add(center + dir * 1420f + Vector3.up * height);
                vertices.Add(center + dir * 1650f + Vector3.up * (height * 0.6f));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = s * 3, b = (s + 1) * 3;
                triangles.AddRange(new[] { a, a + 1, b + 1, a, b + 1, b, a + 1, a + 2, b + 2, a + 1, b + 2, b + 1 });
            }
            var mesh = new Mesh { name = "Hills" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildCity(System.Random rng, out List<Vector3> beacons)
        {
            beacons = new List<Vector3>();
            var kit = new MeshKit();
            for (int row = 0; row < 3; row++)
            {
                float radius = 760f + row * 150f;
                float angle = -62f;
                while (angle < 62f)
                {
                    float width = 18f + (float)rng.NextDouble() * 34f;
                    float depth = 18f + (float)rng.NextDouble() * 30f;
                    float centerBias = 1f - Mathf.Abs(angle) / 70f;
                    float height = 18f + (float)(rng.NextDouble() * rng.NextDouble()) * 150f * (0.45f + centerBias) + row * 12f;
                    float rad = angle * Mathf.Deg2Rad;
                    var pos = new Vector3(Mathf.Sin(rad) * radius, 1f + height * 0.5f, 260f + Mathf.Cos(rad) * radius);
                    kit.Box(pos, new Vector3(width, height, depth), (float)rng.NextDouble());
                    if (height > 120f) beacons.Add(pos + Vector3.up * (height * 0.5f + 1.5f));
                    angle += (width + 6f + (float)rng.NextDouble() * 18f) / (radius * Mathf.Deg2Rad);
                }
            }
            return kit.ToMesh("City Skyline");
        }

        static Mesh BuildTrees(System.Random rng)
        {
            var kit = new MeshKit();
            for (int i = 0; i < 190; i++)
            {
                float x = ((float)rng.NextDouble() * 2f - 1f) * 700f;
                if (Mathf.Abs(x) < 70f) continue; // keep the central lawn clear for the audience view
                float z = ShoreZ - 22f - (float)rng.NextDouble() * 180f;
                float h = 7f + (float)rng.NextDouble() * 9f;
                var root = new Vector3(x, 0.35f, z);
                kit.Cylinder(root, 0.35f, h * 0.3f, 5, caps: false);
                kit.Cone(root + Vector3.up * h * 0.25f, h * 0.32f, h * 0.8f, 7);
            }
            return kit.ToMesh("Trees");
        }

        static List<Vector3> ShoreLanterns(System.Random rng)
        {
            var list = new List<Vector3>();
            for (float x = -260f; x <= 260f; x += 7.5f)
            {
                list.Add(new Vector3(x + (float)rng.NextDouble() * 1.2f, 2.4f + (float)rng.NextDouble() * 0.4f, ShoreZ - 3f));
            }
            return list;
        }
    }
}
