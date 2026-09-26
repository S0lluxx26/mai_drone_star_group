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
            Spawn("City Skyline", BuildCity(rng, out List<Vector3> beacons, out List<Vector3> streetLights), cityMaterial);
            Spawn("Trees", BuildTrees(rng), treeMaterial);
            SpawnLights("Shore Lanterns", ShoreLanterns(rng), lampMaterial);
            SpawnLights("Tower Beacons", beacons, lampMaterial, new Color(1f, 0.08f, 0.04f), 5f);
            SpawnLights("City Street Lights", streetLights, lampMaterial, null, 2.2f);
        }

        void ApplyAtmosphere()
        {
            if (skyMaterial != null) RenderSettings.skybox = skyMaterial;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.015f, 0.03f, 0.07f);
            RenderSettings.fogDensity = 0.00055f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.06f, 0.09f, 0.18f);
            RenderSettings.ambientEquatorColor = new Color(0.04f, 0.06f, 0.12f);
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

        /// <summary>Centre of the lake the skyline wraps around.</summary>
        static readonly Vector3 CityCentre = new Vector3(0f, 0f, 260f);

        static Mesh BuildCity(System.Random rng, out List<Vector3> beacons, out List<Vector3> streetLights)
        {
            beacons = new List<Vector3>();
            streetLights = new List<Vector3>();
            var kit = new MeshKit();
            // Rows from the embankment back: low waterfront blocks, mid-rise, then the high-rise core.
            var rows = new (float radius, float minH, float maxH, float minW, float maxW)[]
            {
                (722f, 8f, 26f, 14f, 30f),
                (800f, 22f, 70f, 16f, 34f),
                (905f, 45f, 125f, 18f, 38f),
                (1020f, 70f, 175f, 22f, 44f),
            };
            for (int r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                float angle = -64f + (float)rng.NextDouble() * 3f;
                while (angle < 64f)
                {
                    float width = Mathf.Lerp(row.minW, row.maxW, (float)rng.NextDouble());
                    float depth = width * Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble());
                    float centreBias = 1f - Mathf.Abs(angle) / 72f;
                    float t = (float)(rng.NextDouble() * rng.NextDouble());
                    float height = Mathf.Lerp(row.minH, row.maxH, Mathf.Clamp01(t * (0.55f + 0.9f * centreBias)));
                    float rad = angle * Mathf.Deg2Rad;
                    float radius = row.radius + ((float)rng.NextDouble() - 0.5f) * 24f;
                    var pos = CityCentre + new Vector3(Mathf.Sin(rad) * radius, 1f, Mathf.Cos(rad) * radius);
                    // Face the lake, with some jitter so neighbouring faces catch the moon differently.
                    Vector3 away = pos - CityCentre;
                    float yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg + ((float)rng.NextDouble() - 0.5f) * 36f;
                    AddBuilding(kit, rng, pos, width, depth, height, yaw, (float)rng.NextDouble(), r, beacons);
                    angle += (width + 4f + (float)rng.NextDouble() * 14f) / (radius * Mathf.Deg2Rad);
                }
            }

            // Street lights along the far embankment: a fine line of warm points that sets the scale.
            for (float a = -66f; a <= 66f; a += 0.55f)
            {
                float rad = a * Mathf.Deg2Rad;
                streetLights.Add(CityCentre + new Vector3(Mathf.Sin(rad) * 704f, 5.5f, Mathf.Cos(rad) * 704f));
            }
            return kit.ToMesh("City Skyline");
        }

        static void AddBuilding(MeshKit kit, System.Random rng, Vector3 basePos, float width, float depth, float height,
            float yaw, float seed, int row, List<Vector3> beacons)
        {
            double roll = rng.NextDouble();
            float style = height > 80f
                ? (roll < 0.55 ? 0.5f : roll < 0.8 ? 0f : 1f)
                : height > 30f ? (roll < 0.15 ? 0.5f : roll < 0.6 ? 0f : 1f)
                : (roll < 0.6 ? 0f : 1f);
            bool crownLights = height > 90f && rng.NextDouble() < 0.45;
            float y = basePos.y;
            float top;

            if (height > 70f && rng.NextDouble() < 0.16)
            {
                // Round tower.
                kit.CurrentColor = new Color(style, crownLights ? 1f : 0f, 0f, 1f);
                float r = Mathf.Min(width, depth) * 0.5f;
                kit.Tower(new Vector3(basePos.x, y, basePos.z), r, height, 20, seed);
                top = y + height;
                kit.CurrentColor = new Color(style, 0f, 0f, 1f);
                kit.Box(new Vector3(basePos.x, top + 2f, basePos.z), new Vector3(r * 0.9f, 4f, r * 0.9f), seed, yawDegrees: yaw, wallPart: MeshKit.PartPlain);
                top += 4f;
            }
            else if (height > 80f && rng.NextDouble() < 0.75)
            {
                // Tower with setbacks: base, middle and top sections, each narrower.
                float[] share = { 0.52f, 0.3f, 0.18f };
                float[] scale = { 1f, 0.8f, 0.6f };
                for (int k = 0; k < 3; k++)
                {
                    float h = height * share[k];
                    kit.CurrentColor = new Color(style, k == 2 && crownLights ? 1f : 0f, 0f, 1f);
                    kit.Box(new Vector3(basePos.x, y + h * 0.5f, basePos.z), new Vector3(width * scale[k], h, depth * scale[k]), seed, yawDegrees: yaw);
                    y += h;
                }
                top = y;
                kit.CurrentColor = new Color(style, 0f, 0f, 1f);
                double feature = rng.NextDouble();
                if (feature < 0.4)
                {
                    float spire = height * Mathf.Lerp(0.12f, 0.22f, (float)rng.NextDouble());
                    kit.Box(new Vector3(basePos.x, top + spire * 0.5f, basePos.z), new Vector3(1.4f, spire, 1.4f), seed, yawDegrees: yaw, wallPart: MeshKit.PartPlain);
                    top += spire;
                }
                else if (feature < 0.7)
                {
                    float pyramid = width * 0.45f;
                    kit.Pyramid(new Vector3(basePos.x, top, basePos.z), width * 0.3f, pyramid, yaw, seed);
                    top += pyramid;
                }
                else
                {
                    AddRooftopPlant(kit, rng, new Vector3(basePos.x, top, basePos.z), width * 0.6f, depth * 0.6f, yaw, seed);
                }
            }
            else
            {
                kit.CurrentColor = new Color(style, 0f, 0f, 1f);
                kit.Box(new Vector3(basePos.x, y + height * 0.5f, basePos.z), new Vector3(width, height, depth), seed, yawDegrees: yaw);
                top = y + height;
                if (rng.NextDouble() < 0.7) AddRooftopPlant(kit, rng, new Vector3(basePos.x, top, basePos.z), width, depth, yaw, seed);
            }

            if (top > 120f) beacons.Add(new Vector3(basePos.x, top + 1.5f, basePos.z));
            kit.CurrentColor = Color.white;
        }

        static void AddRooftopPlant(MeshKit kit, System.Random rng, Vector3 roof, float width, float depth, float yaw, float seed)
        {
            int units = 1 + rng.Next(3);
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            for (int u = 0; u < units; u++)
            {
                var size = new Vector3(Mathf.Lerp(3f, width * 0.35f, (float)rng.NextDouble()), Mathf.Lerp(2f, 5f, (float)rng.NextDouble()), Mathf.Lerp(3f, depth * 0.35f, (float)rng.NextDouble()));
                var offset = new Vector3(((float)rng.NextDouble() - 0.5f) * (width - size.x) * 0.8f, 0f, ((float)rng.NextDouble() - 0.5f) * (depth - size.z) * 0.8f);
                kit.Box(roof + q * offset + Vector3.up * size.y * 0.5f, size, seed, yawDegrees: yaw, wallPart: MeshKit.PartPlain);
            }
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
